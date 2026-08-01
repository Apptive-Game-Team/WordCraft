using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Determinism self-check. Run with `dotnet run --project Replay`.
    /// Everything the lockstep netcode assumes has to hold here first.
    /// </summary>
    internal static class Program
    {
        private const ulong Seed = 0xC0FFEE;
        private const int Ticks = 600; // 30 seconds at 20 Hz

        private static int Main()
        {
            try
            {
                FixedPointSanity();
                RandomSanity();
                SameLogSameHashes();
                CommandOrderDoesNotMatter();
                DivergenceIsDetected();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                return 1;
            }

            Console.WriteLine("OK: all determinism checks passed");
            return 0;
        }

        private static void FixedPointSanity()
        {
            Check(Fix.FromInt(3) + Fix.FromInt(4) == Fix.FromInt(7), "add");
            Check(Fix.FromInt(3) * Fix.FromInt(4) == Fix.FromInt(12), "mul");
            Check(Fix.FromInt(12) / Fix.FromInt(4) == Fix.FromInt(3), "div");
            Check(Fix.Sqrt(Fix.FromInt(144)) == Fix.FromInt(12), "sqrt exact");
            Check(Fix.Sqrt(Fix.Zero) == Fix.Zero, "sqrt zero");
            Check(Fix.Ratio(1, 2) + Fix.Ratio(1, 2) == Fix.OneF, "ratio");

            // A 3-4-5 triangle must land exactly on 5, or movement drifts.
            var v = new FixVec2(Fix.FromInt(3), Fix.FromInt(4));
            Check(v.Magnitude == Fix.FromInt(5), "magnitude 3-4-5");

            // Normalizing a zero vector must not divide by zero.
            Check(FixVec2.Zero.Normalized().Equals(FixVec2.Zero), "normalize zero");
        }

        private static void RandomSanity()
        {
            var a = new DetRandom(Seed);
            var b = new DetRandom(Seed);
            for (int i = 0; i < 1000; i++)
            {
                Check(a.NextULong() == b.NextULong(), "rng stream differs at draw " + i);
            }
            Check(a.DrawCount == b.DrawCount, "rng draw count differs");

            var bounded = new DetRandom(Seed);
            for (int i = 0; i < 1000; i++)
            {
                int r = bounded.NextInt(7);
                Check(r >= 0 && r < 7, "rng bound escaped: " + r);
            }
        }

        private static void SameLogSameHashes()
        {
            List<Command>[] log = BuildLog();
            ulong[] left = Run(log, mutateAtTick: -1);
            ulong[] right = Run(log, mutateAtTick: -1);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "hash drift at tick " + t);
            }
        }

        private static void CommandOrderDoesNotMatter()
        {
            List<Command>[] log = BuildLog();
            List<Command>[] shuffled = new List<Command>[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var copy = new List<Command>(log[t]);
                copy.Reverse(); // arrival order on the wire is not canonical order
                shuffled[t] = copy;
            }

            ulong[] ordered = Run(log, mutateAtTick: -1);
            ulong[] reversed = Run(shuffled, mutateAtTick: -1);

            for (int t = 0; t < ordered.Length; t++)
            {
                Check(ordered[t] == reversed[t], "arrival order changed the result at tick " + t);
            }
        }

        private static void DivergenceIsDetected()
        {
            List<Command>[] log = BuildLog();
            ulong[] clean = Run(log, mutateAtTick: -1);
            ulong[] tampered = Run(log, mutateAtTick: 100);

            int firstMismatch = -1;
            for (int t = 0; t < clean.Length; t++)
            {
                if (clean[t] != tampered[t]) { firstMismatch = t; break; }
            }

            Check(firstMismatch >= 0, "a tampered run produced identical hashes");
            Check(firstMismatch <= 101, "divergence took too long to surface: tick " + firstMismatch);
        }

        /// <summary>Two players, deterministic pseudo-random move orders.</summary>
        private static List<Command>[] BuildLog()
        {
            var scripted = new DetRandom(0xBADC0DE);
            var log = new List<Command>[Ticks];
            var seq = new int[2];

            for (int t = 0; t < Ticks; t++)
            {
                log[t] = new List<Command>();
                if (t % 17 != 0) continue;

                for (int peer = 0; peer < 2; peer++)
                {
                    int entity = peer * 3 + scripted.NextInt(3);
                    var target = new FixVec2(
                        Fix.FromInt(scripted.NextInt(64)),
                        Fix.FromInt(scripted.NextInt(64)));
                    log[t].Add(new Command(t, peer, seq[peer]++, CommandType.Move, entity, target));
                }
            }
            return log;
        }

        /// <summary>
        /// Runs the log in a fresh world and returns the per-tick hash.
        /// mutateAtTick injects a single state change to prove detection works.
        /// </summary>
        private static ulong[] Run(List<Command>[] log, int mutateAtTick)
        {
            var world = new World(Seed);
            for (int peer = 0; peer < 2; peer++)
            {
                for (int i = 0; i < 3; i++)
                {
                    world.SpawnUnit(
                        peer,
                        new FixVec2(Fix.FromInt(peer * 40), Fix.FromInt(i * 5)),
                        Fix.Ratio(1, 4),
                        100);
                }
            }

            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var commands = log[t];
                if (t == mutateAtTick)
                {
                    // One extra command on one peer only: the classic desync.
                    commands = new List<Command>(commands)
                    {
                        new Command(t, 0, 9999, CommandType.Move, 0,
                            new FixVec2(Fix.FromInt(7), Fix.FromInt(7)))
                    };
                }
                world.Step(commands);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
