using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>Run explicitly with -executeMethod WordCraft.View.SoloPauseChecks.Run.</summary>
    public static class SoloPauseChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            Check((ok, message) =>
            {
                if (ok) Debug.Log("OK: " + message);
                else { Debug.LogError("FAIL: " + message); EditorApplication.Exit(1); }
            });
        }

        public static void Check(Action<bool, string> report)
        {
            try
            {
                RunChecks();
                report(true, "solo pause freezes time, preserves queued orders, resumes without catch-up, resets, and ignores menu/result/network");
            }
            catch (Exception error) { report(false, "solo pause: " + error); }
        }

        private static void RunChecks()
        {
            var root = new GameObject("solo pause check");
            var runner = root.AddComponent<MatchRunner>();
            try
            {
                runner.ShowStart();
                Check(!Pause(runner, 1000, true) && !runner.Paused, "menu accepted pause");
                runner.StartSolo(Faction.TreeSpirits);
                runner.Session.Update(1000);
                Set(runner, "startMs", 1000L);
                Set(runner, "lastStepMs", 1000L);
                ulong hash = runner.World.Hash();
                Check(Pause(runner, 1025, true) && runner.Paused, "solo did not pause");
                float alpha = runner.Alpha;
                Check(Pause(runner, 61025, false), "paused frame would advance the match");
                Check(runner.Alpha == alpha && runner.World.Hash() == hash && runner.World.Tick == 0,
                    "pause changed simulation or interpolation");

                int worker = -1;
                for (int id = 0; id < runner.World.EntityCount; id++)
                    if (runner.World.GetEntity(id).Owner == 0 && runner.World.GetEntity(id).Kind == EntityKind.Worker)
                    { worker = id; break; }
                Check(worker >= 0, "missing worker fixture");
                FixVec2 before = runner.World.GetEntity(worker).Position;
                runner.Session.Issue(CommandType.Move, worker, new FixVec2(Fix.FromInt(20), Fix.FromInt(12)));
                Check(runner.World.Hash() == hash, "queued paused order executed early");
                Check(!Pause(runner, 61025, true) && !runner.Paused, "solo did not resume");
                Check(Get<long>(runner, "startMs") == 61000 && Get<long>(runner, "lastStepMs") == 61000,
                    "pause duration became catch-up time");
                for (int tick = 0; tick < 20; tick++)
                {
                    runner.Session.Update(61025 + tick * 50);
                    Check(runner.Session.TryStep(61025 + tick * 50), "solo barrier closed after resume");
                }
                var moved = runner.World.GetEntity(worker).Position;
                Check(moved.X != before.X || moved.Y != before.Y, "paused order was lost on resume");

                Pause(runner, 63000, true);
                runner.StartSolo(Faction.Humans);
                Check(!runner.Paused, "new solo inherited pause");
                runner.Session.Update(64000);
                Get<MatchConfig>(runner, "config").Solo = false;
                Check(!Pause(runner, 64025, true) && !runner.Paused, "network configuration accepted pause");
                runner.StartSolo(Faction.TreeSpirits);
                runner.Session.Update(65000);
                typeof(MatchRunner).GetMethod("End", Private).Invoke(runner, null);
                Check(!Pause(runner, 65025, true) && !runner.Paused, "result accepted pause");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static bool Pause(MatchRunner runner, long now, bool pressed) =>
            (bool)typeof(MatchRunner).GetMethod("PauseFrame", Private).Invoke(runner, new object[] { now, pressed });
        private static T Get<T>(MatchRunner runner, string field) =>
            (T)typeof(MatchRunner).GetField(field, Private).GetValue(runner);
        private static void Set(MatchRunner runner, string field, object value) =>
            typeof(MatchRunner).GetField(field, Private).SetValue(runner, value);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
