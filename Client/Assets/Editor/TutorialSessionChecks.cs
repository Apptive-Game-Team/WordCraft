using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Checks tutorial session isolation through the real runner lifecycle.</summary>
    public static class TutorialSessionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            Run((ok, message) =>
            {
                if (ok) Debug.Log("OK: " + message);
                else { Debug.LogError("FAIL: " + message); EditorApplication.Exit(1); }
            });
        }

        public static void Run(Action<bool, string> report)
        {
            var previousRunner = MatchRunner.Instance;
            var previousSelection = Selection.Instance;
            var mixerField = typeof(Hud).GetField("mixerOpen", BindingFlags.Static | BindingFlags.NonPublic);
            bool previousMixer = Hud.MixerOpen;
            var root = new GameObject("tutorial session check");
            try
            {
                var runner = root.AddComponent<MatchRunner>();
                var selection = root.AddComponent<Selection>();
                // Edit-mode AddComponent does not guarantee MonoBehaviour.Awake.
                SetInstance(typeof(MatchRunner), runner);
                SetInstance(typeof(Selection), selection);
                runner.StartSolo(Faction.TreeSpirits);
                World oldWorld = runner.World;
                int oldWorker = Worker(oldWorld);
                typeof(Selection).GetField("held", Private).SetValue(selection, oldWorld);
                selection.SelectOnly(oldWorker);

                runner.StartTutorial();
                Check(runner.Tutorial != null && runner.Phase == Phase.Match, "tutorial did not open in a match");
                Check(!runner.World.IsPeerAi(1), "tutorial opponent AI is enabled");
                Check(runner.World.FactionOf(0) == Faction.TreeSpirits, "tutorial opened with the wrong faction");
                mixerField.SetValue(null, false);
                var panel = (Rect)typeof(TutorialPanel).GetMethod("Bounds", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
                var panelPoint = new Vector2(panel.center.x, Screen.height - panel.center.y);
                Check(TutorialPanel.Covers(panelPoint) && Hud.OverUi(panelPoint),
                    "tutorial panel did not block world input at its screen-space center");
                mixerField.SetValue(null, true);
                Check(!TutorialPanel.Covers(panelPoint), "tutorial hit area remained active behind sound mixer");
                mixerField.SetValue(null, false);
                Check(selection.SelectedIn(runner.World).Count == 0, "old-world selection leaked into tutorial");
                Invoke(runner, "ObserveTutorial");
                Check(runner.Tutorial.Step == 0, "stale worker id completed the selection lesson");

                typeof(Selection).GetField("held", Private).SetValue(selection, runner.World);
                selection.SelectOnly(Worker(runner.World));
                ulong beforeObservation = runner.World.Hash();
                Invoke(runner, "ObserveTutorial");
                Check(runner.Tutorial.Step == 1, "current-world worker selection was not observed");
                Check(runner.World.Hash() == beforeObservation, "tutorial observation mutated simulation");

                runner.Session.Update(1000);
                int count = runner.World.EntityCount;
                int enemyBank = runner.World.GetResources(1);
                for (int tick = 0; tick < 400; tick++)
                {
                    long now = 1000 + tick * 50;
                    runner.Session.Update(now);
                    Check((bool)Invoke(runner, "StepOnce", now), "tutorial solo barrier stalled");
                    Check(runner.World.LastTickCommands.Count == 0, "passive tutorial emitted an AI command");
                    for (int id = 0; id < runner.World.EntityCount; id++)
                    {
                        var entity = runner.World.GetEntity(id);
                        if (entity.Owner == 1) Check(entity.QueueCount == 0, "passive opponent started production");
                    }
                }
                Check(runner.World.EntityCount == count && runner.World.GetResources(1) == enemyBank,
                    "passive opponent spawned, built, or spent resources");

                int pausedTick = runner.World.Tick;
                var progress = runner.Tutorial;
                ulong pausedHash = runner.World.Hash();
                Check((bool)Invoke(runner, "PauseFrame", 25000L, true), "tutorial rejected solo pause");
                Check((bool)Invoke(runner, "PauseFrame", 85000L, false), "tutorial pause did not hold");
                Invoke(runner, "ObserveTutorial");
                Check(runner.Paused && runner.World.Tick == pausedTick && runner.World.Hash() == pausedHash &&
                    ReferenceEquals(progress, runner.Tutorial), "paused tutorial advanced or replaced the session");

                Invoke(runner, "PauseFrame", 85000L, true);
                Check(!runner.Paused, "tutorial fixture did not resume before completion");
                // Legal lesson completion is covered by TutorialChecks; isolate the
                // runner's completion boundary here, with overdue ticks waiting.
                typeof(TutorialProgress).GetProperty("Step").GetSetMethod(true)
                    .Invoke(progress, new object[] { TutorialProgress.StepCount });
                typeof(MatchRunner).GetField("startMs", Private).SetValue(runner, -60000L);
                for (int frame = 0; frame < 12; frame++) Invoke(runner, "Update");
                Check(runner.Tutorial.Completed && runner.Paused && runner.Phase == Phase.Match &&
                    runner.World.Tick == pausedTick && runner.World.Hash() == pausedHash,
                    "completed tutorial advanced ticks or replaced its completion panel with results");

                runner.StartSolo(Faction.Humans);
                Check(runner.Tutorial == null && !runner.Paused && runner.World.IsPeerAi(1),
                    "normal solo inherited tutorial state, pause, or passive AI");
                runner.Session.Update(90000);
                bool aiCommand = false, economyCommand = false;
                for (int tick = 0; tick < 200; tick++)
                {
                    long now = 90000 + tick * 50;
                    runner.Session.Update(now);
                    Check((bool)Invoke(runner, "StepOnce", now), "restored solo barrier stalled");
                    foreach (var command in runner.World.LastTickCommands)
                    {
                        if (command.PeerId != 1) continue;
                        aiCommand = true;
                        if (command.Type == CommandType.Build || command.Type == CommandType.Produce) economyCommand = true;
                    }
                }
                Check(aiCommand && economyCommand, "normal solo opponent never resumed AI economy commands");
                runner.StartTutorial();
                Check(runner.Tutorial != null && !ReferenceEquals(progress, runner.Tutorial), "tutorial reused old progress");
                runner.ShowStart();
                Check(runner.Tutorial == null && !runner.Paused && runner.Phase == Phase.Start, "menu retained tutorial state");
                report(true, "tutorial uses passive AI, isolates selection, supports pause, and restores normal solo/menu state");
            }
            catch (Exception error) { report(false, "tutorial session: " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SetInstance(typeof(MatchRunner), previousRunner);
                SetInstance(typeof(Selection), previousSelection);
                mixerField.SetValue(null, previousMixer);
            }
        }

        private static void SetInstance(Type type, object value) =>
            type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                .GetSetMethod(true).Invoke(null, new[] { value });

        private static object Invoke(MatchRunner runner, string method, params object[] arguments) =>
            typeof(MatchRunner).GetMethod(method, Private).Invoke(runner, arguments);

        private static int Worker(World world)
        {
            for (int id = 0; id < world.EntityCount; id++)
                if (world.GetEntity(id).Owner == 0 && world.GetEntity(id).Kind == EntityKind.Worker) return id;
            throw new Exception("missing tutorial worker fixture");
        }

        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    }
}
