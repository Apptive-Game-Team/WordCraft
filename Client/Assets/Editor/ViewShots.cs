using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Renders the field to PNG files from the command line, so that what the
    /// player will see can be looked at without a player, a window or a second
    /// person. Four shots a run: the home base, the lane and its high ground, the
    /// whole map, and a close-up of bodies.
    ///
    ///   Unity -batchmode -quit -projectPath Client \
    ///         -executeMethod WordCraft.View.ViewShots.Run \
    ///         -shotsOutput ../shots -shotsTicks 900
    ///
    /// Not -nographics: this needs a GPU to render. The scene is assembled by
    /// hand from the same pieces the view assembles at runtime — Field, FieldMesh,
    /// Sky, Models — and then a camera is pointed at it by CameraRig's own pose
    /// math, so a shot is the game's renderer and not a second one that happens to
    /// agree with it. The scripted log is stepped first, so the shots hold a match
    /// in progress rather than two bases staring at each other.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class ViewShots
    {
        private const int Width = 1920;
        private const int Height = 1080;

        /// <summary>Where the camera looks for each shot: frame on the plane, distance, heading.</summary>
        private static readonly (string Name, Vector2 Frame, float Distance, float Yaw)[] Shots =
        {
            ("base", new Vector2(10f, 10f), 30f, 0f),
            ("lane", new Vector2(30f, 36f), 26f, 35f),
            ("overview", new Vector2(32f, 32f), 58f, 0f),
            ("close", new Vector2(10f, 9f), 14f, -30f),
            ("flank", new Vector2(52f, 8f), 22f, 150f),
        };

        public static void Run()
        {
            string output = Arg("-shotsOutput");
            if (string.IsNullOrEmpty(output)) output = "shots";
            int ticks = int.TryParse(Arg("-shotsTicks"), out int parsed) ? parsed : 900;

            try
            {
                Directory.CreateDirectory(output);
                World world = Advance(ticks);
                Shoot(world, output);
                Debug.Log("OK: " + Shots.Length + " shots written to " + Path.GetFullPath(output));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("FAIL: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The scripted match, some way in, so there is something on the field to look at.</summary>
        private static World Advance(int ticks)
        {
            World world = MatchScenario.Build(new WordCraft.Net.MatchConfig().Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            List<Command>[] log = ScriptedLog.Build();
            var idle = new List<Command>();
            for (int t = 0; t < ticks; t++) world.Step(t < log.Length ? log[t] : idle);
            return world;
        }

        private static void Shoot(World world, string output)
        {
            var root = new GameObject("Shots");
            var cam = new GameObject("ShotCamera").AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = CameraRig.FieldOfView;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 400f;

            var target = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, mipChain: false);

            try
            {
                Sky.Build(cam);
                Field.Build(world);
                FieldMesh.Build(world, root.transform);
                Bodies(world, root.transform);

                // No fog in a shot: the point is to see the field, not one peer's
                // view of it. The texture is still bound, because the shaders sample
                // it whatever the parameter says.
                Shader.SetGlobalTexture("_WcFog", Fog.Texture);
                Shader.SetGlobalVector("_WcFogParams", new Vector4(1f / World.GridSize, 0f, 0f, 0f));
                Sky.Update(cam);

                cam.targetTexture = target;
                foreach (var shot in Shots)
                {
                    CameraRig.Aim(cam, shot.Frame, shot.Distance, shot.Yaw, hud: false);
                    cam.Render();

                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;

                    File.WriteAllBytes(Path.Combine(output, shot.Name + ".png"), image.EncodeToPNG());
                }
            }
            finally
            {
                cam.targetTexture = null;
                FieldMesh.Drop();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
                foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        /// <summary>Every live body, dressed the way MatchView.Dress dresses it, standing where it stands.</summary>
        private static void Bodies(World world, Transform parent)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive) continue;

                bool neutral = world.IsNeutralRock(e);
                Faction faction = e.Owner >= 0
                    ? world.FactionOf(e.Owner)
                    : neutral ? Faction.RockGolems : Faction.TreeSpirits;

                BodyRig body = Models.Build(parent, faction, e.Role, e.Slot, e.Kind, e.Owner, neutral);
                body.Tint(neutral ? UiStyle.Neutral : Color.white);
                body.Show(true);
                body.Progress(1f);
                body.Place(Field.At(MatchRunner.ToView(e.Position)));
                // Nothing moved, so nothing faces anywhere: turn each body a little
                // by id, or a whole army stands in parade formation facing north.
                body.Face((i * 47) % 360);
            }
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
