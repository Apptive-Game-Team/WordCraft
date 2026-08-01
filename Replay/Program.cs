using System;
using System.Collections.Generic;
using System.Reflection;
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
                ScriptedMatchSameHashes();
                ScriptedMatchDivergenceIsDetected();
                SimAssemblyIsClean();
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

            int firstMismatch = FirstMismatch(clean, tampered);
            Check(firstMismatch >= 0, "a tampered run produced identical hashes");
            Check(firstMismatch <= 101, "divergence took too long to surface: tick " + firstMismatch);
        }

        /// <summary>
        /// The same shape as SameLogSameHashes, but over a world that gathers,
        /// builds, produces, and fights. A movement-only world would not exercise
        /// the state that actually desyncs.
        /// </summary>
        private static void ScriptedMatchSameHashes()
        {
            List<Command>[] log = BuildMatchLog();
            ulong[] left = RunMatch(log, mutateAtTick: -1, out World world);
            ulong[] right = RunMatch(log, mutateAtTick: -1, out _);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "match hash drift at tick " + t);
            }

            // Without these the check could pass on a match where nothing happened.
            Check(world.GetEntity(MatchNode0).Resource < NodeAmount, "no resources were gathered");
            Check(world.GetResources(0) > 10, "peer 0 never banked a delivery");
            Check(world.GetEntity(MatchSite0).Kind == EntityKind.Building, "peer 0 building was not placed");
            Check(world.GetEntity(MatchSite0).BuildTicksLeft == 0, "peer 0 building never finished");
            Check(world.EntityCount > MatchSite1 + 1, "no units were produced");
            Check(!world.GetEntity(MatchFighter1).Alive, "combat never killed anything");
        }

        private static void ScriptedMatchDivergenceIsDetected()
        {
            List<Command>[] log = BuildMatchLog();
            ulong[] clean = RunMatch(log, mutateAtTick: -1, out _);
            ulong[] tampered = RunMatch(log, mutateAtTick: 200, out _);

            int firstMismatch = FirstMismatch(clean, tampered);
            Check(firstMismatch >= 0, "a tampered match produced identical hashes");
            Check(firstMismatch <= 201, "match divergence took too long to surface: tick " + firstMismatch);
        }

        /// <summary>
        /// Reflection stand-in for a lint rule: the simulation assembly must not
        /// carry floating point or a Unity reference. Signatures only, which is
        /// enough to catch the mistake anyone actually makes.
        /// </summary>
        private static void SimAssemblyIsClean()
        {
            Assembly sim = typeof(World).Assembly;

            foreach (AssemblyName reference in sim.GetReferencedAssemblies())
            {
                Check(!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal),
                    "Sim references " + reference.Name);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.DeclaredOnly;

            foreach (Type type in sim.GetTypes())
            {
                foreach (FieldInfo f in type.GetFields(all))
                {
                    CheckNotFloating(f.FieldType, type.FullName + "." + f.Name);
                }
                foreach (MethodInfo m in type.GetMethods(all))
                {
                    CheckNotFloating(m.ReturnType, type.FullName + "." + m.Name + " return");
                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        CheckNotFloating(p.ParameterType, type.FullName + "." + m.Name + " parameter " + p.Name);
                    }
                }
            }
        }

        private static void CheckNotFloating(Type t, string where)
        {
            Type bare = t.IsByRef || t.IsArray ? t.GetElementType() : t;
            Check(bare != typeof(float) && bare != typeof(double) && bare != typeof(decimal),
                "forbidden floating point type in Sim: " + where);
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

        // Entity ids of the scripted match. Ids are handed out in spawn order and
        // never reused, so they are stable enough to assert on.
        private const int MatchWorker0A = 1;
        private const int MatchWorker0B = 2;
        private const int MatchNode0 = 3;
        private const int MatchBase1 = 4;
        private const int MatchWorker1A = 5;
        private const int MatchWorker1B = 6;
        private const int MatchNode1 = 7;
        private const int MatchFighter1 = 9;
        private const int MatchSite0 = 10; // placed at tick 150 by peer 0
        private const int MatchSite1 = 11; // placed at tick 150 by peer 1
        private const int NodeAmount = 200;

        private static World BuildMatchWorld()
        {
            var world = new World(Seed);
            Fix half = Fix.Ratio(1, 2);

            world.SpawnBuilding(0, At(5, 5), complete: true);       // 0
            world.SpawnWorker(0, At(7, 5));                          // 1
            world.SpawnWorker(0, At(7, 7));                          // 2
            world.SpawnResourceNode(At(14, 14), NodeAmount);         // 3

            world.SpawnBuilding(1, At(50, 50), complete: true);      // 4
            world.SpawnWorker(1, At(48, 50));                        // 5
            world.SpawnWorker(1, At(48, 48));                        // 6
            world.SpawnResourceNode(At(42, 42), NodeAmount);         // 7

            // Two units placed in each other's face so combat resolves inside the
            // replay window instead of after it.
            world.SpawnUnit(0, At(30, 30), World.UnitSpeed, World.UnitHp);   // 8
            world.SpawnUnit(1, new FixVec2(Fix.FromInt(31) + half, Fix.FromInt(30) + half),
                World.UnitSpeed, World.UnitHp);                              // 9

            world.GrantResources(0, 100);
            world.GrantResources(1, 100);
            return world;
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        /// <summary>Gather, build, produce, in that order, for both peers.</summary>
        private static List<Command>[] BuildMatchLog()
        {
            var log = new List<Command>[Ticks];
            for (int t = 0; t < Ticks; t++) log[t] = new List<Command>();
            var seq = new int[2];

            log[2].Add(new Command(2, 0, seq[0]++, CommandType.Gather, MatchWorker0A, FixVec2.Zero, MatchNode0));
            log[2].Add(new Command(2, 0, seq[0]++, CommandType.Gather, MatchWorker0B, FixVec2.Zero, MatchNode0));
            log[2].Add(new Command(2, 1, seq[1]++, CommandType.Gather, MatchWorker1A, FixVec2.Zero, MatchNode1));
            log[2].Add(new Command(2, 1, seq[1]++, CommandType.Gather, MatchWorker1B, FixVec2.Zero, MatchNode1));

            log[150].Add(new Command(150, 0, seq[0]++, CommandType.Build, -1, At(20, 20)));
            log[150].Add(new Command(150, 1, seq[1]++, CommandType.Build, -1, At(44, 48)));

            foreach (int t in new[] { 300, 400 })
            {
                log[t].Add(new Command(t, 0, seq[0]++, CommandType.Produce, 0, FixVec2.Zero));
                log[t].Add(new Command(t, 1, seq[1]++, CommandType.Produce, MatchBase1, FixVec2.Zero));
            }

            return log;
        }

        private static ulong[] RunMatch(List<Command>[] log, int mutateAtTick, out World world)
        {
            world = BuildMatchWorld();
            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var commands = log[t];
                if (t == mutateAtTick)
                {
                    commands = new List<Command>(commands)
                    {
                        new Command(t, 0, 9999, CommandType.Move, MatchWorker0A, At(3, 3))
                    };
                }
                world.Step(commands);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private static int FirstMismatch(ulong[] a, ulong[] b)
        {
            for (int t = 0; t < a.Length; t++)
            {
                if (a[t] != b[t]) return t;
            }
            return -1;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
