using System;
using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    public static class ModelPortraitChecks
    {
        public static void Run(Action<bool, string> report)
        {
            RenderTexture previousTarget = RenderTexture.active;
            RenderTexture sentinel = null;
            try
            {
                ModelPortrait.ClearCache();
                Require(ModelPortrait.CachedCount == 0, "clear did not empty the portrait cache");
                HashSet<int> sceneBefore = SceneObjects();
                Light sun = RenderSettings.sun;
                Color ambient = RenderSettings.ambientLight;
                string[] globalNames = { "_WcFogParams", "_WcSkyLight", "_WcEarthLight", "_WcBands", "_WcRim", "_WcHaze" };
                var globals = new Vector4[globalNames.Length];
                for (int i = 0; i < globals.Length; i++) globals[i] = Shader.GetGlobalVector(globalNames[i]);
                var lights = new List<(Light Light, int Mask)>();
                foreach (Light light in Resources.FindObjectsOfTypeAll<Light>()) lights.Add((light, light.cullingMask));
                sentinel = new RenderTexture(8, 8, 0);
                sentinel.Create();
                RenderTexture.active = sentinel;
                var textures = new List<Texture2D>();
                var identities = new HashSet<int>();
                var pixelsSeen = new HashSet<ulong>();
                World live = MatchRunner.Instance == null ? null : MatchRunner.Instance.World;
                ulong liveHash = live == null ? 0 : live.Hash();
                foreach (Faction faction in Enum.GetValues(typeof(Faction)))
                {
                    World world = MatchScenario.Build(47, faction, Faction.Hellfire);
                    ulong before = world.Hash();
                    foreach (Role role in Enum.GetValues(typeof(Role)))
                    {
                        if (role == Role.None) continue;
                        for (int slot = 0; slot < FactionData.SlotCount(faction, role); slot++)
                        {
                            if (!FactionData.Has(faction, role, slot)) continue;
                            string key = faction + "/" + role + "/" + slot;
                            Texture2D texture = ModelPortrait.Get(faction, role, slot);
                            Require(texture != null && texture.width == ModelPortrait.Size && texture.height == ModelPortrait.Size,
                                key + " has no correctly sized model portrait");
                            Require(identities.Add(texture.GetInstanceID()), key + " shares another roster entry's cache texture");
                            Require(ReferenceEquals(texture, ModelPortrait.Get(faction, role, slot)), key + " did not reuse its cache");
                            var card = new CardSlot { Type = FactionData.IsBuilding(role) ? CommandType.Build : CommandType.Produce, Produce = role, Slot = slot };
                            Require(ReferenceEquals(texture, CardPortrait.For(faction, card)), key + " card bypassed the model portrait");
                            if (!FactionData.IsBuilding(role))
                            {
                                card.Type = CommandType.Grow;
                                Require(ReferenceEquals(texture, CardPortrait.For(faction, card)), key + " growth portrait differs from production");
                            }
                            Require(RenderTexture.active == sentinel, key + " changed the caller's render target");
                            Color32[] pixels = texture.GetPixels32();
                            int visible = 0;
                            Color32 background = pixels[0];
                            ulong signature = 14695981039346656037UL;
                            foreach (Color32 pixel in pixels)
                            {
                                int contrast = Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b);
                                if (pixel.a > 16 && contrast > 18) visible++;
                                unchecked { signature = (signature ^ pixel.r) * 1099511628211UL;
                                    signature = (signature ^ pixel.g) * 1099511628211UL;
                                    signature = (signature ^ pixel.b) * 1099511628211UL;
                                    signature = (signature ^ pixel.a) * 1099511628211UL; }
                            }
                            Require(visible > 32, key + " contains only the portrait background");
                            pixelsSeen.Add(signature);
                            textures.Add(texture);
                        }
                    }
                    Require(world.Hash() == before, "portrait creation changed " + faction + " simulation");
                }
                Require(textures.Count == 128 && ModelPortrait.CachedCount == 128, "model portrait cache does not cover all 128 roster entries");
                Require(pixelsSeen.Count > 6, "distinct factions and roles rendered the same placeholder");
                Require(SceneObjects().SetEquals(sceneBefore), "portrait capture left temporary scene objects behind");
                Require(RenderSettings.sun == sun && RenderSettings.ambientLight == ambient, "portrait capture changed scene lighting");
                for (int i = 0; i < globals.Length; i++)
                    Require(Shader.GetGlobalVector(globalNames[i]) == globals[i], "portrait capture changed shader global " + globalNames[i]);
                foreach (var light in lights)
                    Require(light.Light != null && light.Light.cullingMask == light.Mask, "portrait capture changed an existing light");
                if (live != null) Require(live.Hash() == liveHash, "portraits mutated the live match");
                ModelPortrait.ClearCache();
                Require(ModelPortrait.CachedCount == 0, "cleanup retained cache entries");
                foreach (Texture2D texture in textures) Require(texture == null, "cleanup retained an owned portrait texture");
                Require(ModelPortrait.Get((Faction)(-1), Role.Worker, 0) == null &&
                    ModelPortrait.Get(Faction.Humans, Role.Melee, 0) == null &&
                    ModelPortrait.Get(Faction.TreeSpirits, Role.Worker, -1) == null && ModelPortrait.CachedCount == 0,
                    "invalid roster keys allocated portraits");
                Texture2D rebuilt = ModelPortrait.Get(Faction.TreeSpirits, Role.Worker, 0);
                Require(rebuilt != null && ModelPortrait.CachedCount == 1, "portrait cache did not rebuild after cleanup");
                ModelPortrait.ClearCache();
                Require(rebuilt == null && SceneObjects().SetEquals(sceneBefore), "repeated capture leaked resources");
                report(true, "128 model portraits render, cache by roster entry, preserve the scene and release owned textures");
            }
            catch (Exception error) { report(false, "model portraits: " + error); }
            finally
            {
                ModelPortrait.ClearCache();
                RenderTexture.active = previousTarget;
                if (sentinel != null) { sentinel.Release(); UnityEngine.Object.DestroyImmediate(sentinel); }
            }
        }

        private static HashSet<int> SceneObjects()
        {
            var ids = new HashSet<int>();
            foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
                if (item.scene.IsValid()) ids.Add(item.GetInstanceID());
            return ids;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
