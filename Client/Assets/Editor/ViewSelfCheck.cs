using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Runs the client's scenario and input log inside Unity's runtime and checks
    /// that nothing the view does perturbs the simulation.
    ///
    ///   Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.ViewSelfCheck.Run
    ///
    /// The final hash it prints is comparable with the same log run under plain
    /// dotnet, which is what catches a fixed-point difference between runtimes.
    /// </summary>
    public static class ViewSelfCheck
    {
        private static int failures;

        public static void Run()
        {
            failures = 0;

            ConversionRoundTripsExactly();
            SameLogSameHashes();
            ViewReadsDoNotMutate();

            ulong final = FinalHash();
            Debug.Log("final hash after " + ScriptedLog.Ticks + " ticks: 0x" + final.ToString("X16"));
            Check(final == ScriptedLog.GoldenHash,
                "Mono disagrees with the golden hash: got 0x" + final.ToString("X16") +
                ", expected 0x" + ScriptedLog.GoldenHash.ToString("X16"));

            if (failures > 0)
            {
                Debug.LogError("FAIL: " + failures + " view determinism check(s) failed");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("OK: all view determinism checks passed");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Every position on the map survives Fix -> float -> Fix unchanged, which
        /// is why a click target may be computed in float. Raw values top out at
        /// 64 &lt;&lt; 16, inside a float mantissa, so nothing is rounded away.
        /// </summary>
        private static void ConversionRoundTripsExactly()
        {
            for (int x = 0; x <= MatchScenario.MapSize; x++)
            {
                for (int y = 0; y <= MatchScenario.MapSize; y++)
                {
                    var p = new FixVec2(
                        Fix.FromInt(x) + Fix.Ratio(1, 2),
                        Fix.FromInt(y) + Fix.Ratio(1, 2));
                    FixVec2 back = MatchRunner.ToSim(MatchRunner.ToView(p));
                    Check(back.Equals(p), "fixed point did not survive the view round trip at " + x + "," + y);
                }
            }
        }

        private static void SameLogSameHashes()
        {
            List<Command>[] log = ScriptedLog.Build();
            ulong[] left = RunLog(log, viewPass: false);
            ulong[] right = RunLog(log, viewPass: false);

            for (int t = 0; t < left.Length; t++)
            {
                if (Check(left[t] == right[t], "hash drift at tick " + t)) return;
            }
        }

        /// <summary>
        /// The same log, but with a full pass over every entity through the view's
        /// own accessors between ticks. Identical hashes mean the view is a reader.
        /// </summary>
        private static void ViewReadsDoNotMutate()
        {
            List<Command>[] log = ScriptedLog.Build();
            ulong[] clean = RunLog(log, viewPass: false);
            ulong[] watched = RunLog(log, viewPass: true);

            for (int t = 0; t < clean.Length; t++)
            {
                if (Check(clean[t] == watched[t], "a view pass changed the simulation at tick " + t)) return;
            }
        }

        private static ulong FinalHash()
        {
            ulong[] hashes = RunLog(ScriptedLog.Build(), viewPass: false);
            return hashes[hashes.Length - 1];
        }

        private static ulong[] RunLog(List<Command>[] log, bool viewPass)
        {
            World world = MatchScenario.Build(new MatchConfig().Seed);
            var hashes = new ulong[log.Length];

            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                if (viewPass) ReadEverything(world);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        /// <summary>Whatever the renderer, the picker, and the HUD read in a frame.</summary>
        private static void ReadEverything(World world)
        {
            var sink = new List<string>(world.EntityCount);
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                Vector2 drawn = MatchRunner.ToView(e.Position);
                FixVec2 aimed = MatchRunner.ToSim(drawn + Vector2.one * 0.25f);
                sink.Add(e.Kind + " " + e.Hp + " " + e.QueueCount + " " +
                         Vector2.Distance(drawn, MatchRunner.ToView(aimed)));
            }
            for (int peer = 0; peer < MatchScenario.Peers; peer++) sink.Add(world.GetResources(peer).ToString());
        }

        /// <summary>Returns true when the check failed, so callers can stop at the first one.</summary>
        private static bool Check(bool condition, string message)
        {
            if (condition) return false;
            Debug.LogError(message);
            failures++;
            return true;
        }
    }
}
