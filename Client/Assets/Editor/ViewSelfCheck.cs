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
            RosterArtResolves();
            RestartIsClean();

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

        /// <summary>
        /// Every sprite the roster names has to load, or the faction silently
        /// renders as a primitive and looks exactly like a slot that has no art.
        /// An empty name is a slot docs/FACTIONS.md marks as still a concept.
        /// Every entry of a slot is checked, not only the one the view draws, or
        /// a typo in the second unit of a role waits until that unit ships.
        /// </summary>
        private static void RosterArtResolves()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    int slots = FactionData.SlotCount((Faction)f, (Role)r);
                    for (int s = 0; s < slots; s++)
                    {
                        string file = FactionData.Sprite((Faction)f, (Role)r, s);
                        if (file.Length == 0) continue;
                        Check(Resources.Load<Sprite>(MatchView.SpriteFolder + file) != null,
                            "roster sprite missing: " + (Faction)f + "." + (Role)r + "[" + s + "] -> " + file);
                    }
                }
            }
        }

        /// <summary>
        /// A second match in the same process has to be the first match again.
        /// Two built players clicking through the start screen is not something a
        /// batch run can drive, so the reset itself is checked instead: run one
        /// match forward, go back to the menu, start another, and require the new
        /// world to be a different object that starts exactly where the old one did.
        ///
        /// The runner is driven directly rather than through Unity's lifecycle,
        /// because the editor never calls Awake and every reset lives in the two
        /// methods the start screen's buttons call.
        /// </summary>
        private static void RestartIsClean()
        {
            var go = new GameObject("restart check");
            var runner = go.AddComponent<MatchRunner>();

            try
            {
                // Port 0 takes whatever the OS has free, so the check never fights
                // the default port and never needs a peer: no connection is required
                // for the reset to be observable.
                if (Check(runner.StartMatch(null, 0, ScriptedLog.Peer0Faction) == null,
                    "the first match would not start")) return;

                World first = runner.World;
                LockstepSession firstSession = runner.Session;
                int count = first.EntityCount;
                ulong start = first.Hash();

                // Move it, or a restart that changed nothing would pass by accident.
                var idle = new List<Command>();
                for (int t = 0; t < 60; t++) first.Step(idle);
                if (Check(first.Hash() != start, "the first world never moved, so this check proves nothing")) return;

                runner.ShowStart();
                if (Check(runner.StartMatch(null, 0, ScriptedLog.Peer0Faction) == null,
                    "the second match would not start")) return;

                Check(!ReferenceEquals(runner.World, first), "the restart reused the first world");
                Check(!ReferenceEquals(runner.Session, firstSession), "the restart reused the first session");
                Check(runner.World.Tick == 0, "the second match began at tick " + runner.World.Tick);
                Check(runner.World.EntityCount == count,
                    "the second match has " + runner.World.EntityCount + " entities, the first had " + count);
                Check(runner.World.Hash() == start,
                    "the second match begins at 0x" + runner.World.Hash().ToString("X16") +
                    ", the first began at 0x" + start.ToString("X16"));

                // The interpolation buffers are per match too, and a stale one puts
                // every entity a match late without touching a single hash.
                Entity last = runner.World.GetEntity(count - 1);
                Check(runner.DrawPosition(count - 1) == MatchRunner.ToView(last.Position),
                    "the draw positions carried over from the first match");
            }
            finally
            {
                runner.ShowStart(); // closes the socket
                Object.DestroyImmediate(go);
            }
        }

        private static ulong FinalHash()
        {
            ulong[] hashes = RunLog(ScriptedLog.Build(), viewPass: false);
            return hashes[hashes.Length - 1];
        }

        private static ulong[] RunLog(List<Command>[] log, bool viewPass)
        {
            World world = MatchScenario.Build(new MatchConfig().Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
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
