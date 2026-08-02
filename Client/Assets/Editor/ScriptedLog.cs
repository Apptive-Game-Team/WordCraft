using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// A fixed input log over MatchScenario that exercises gathering, building,
    /// production, movement, and combat. Deliberately free of UnityEngine so the
    /// same file can be compiled against the plain dotnet build of Sim and the
    /// two runtimes compared hash for hash.
    /// </summary>
    public static class ScriptedLog
    {
        public const int Ticks = 600;

        /// <summary>
        /// What this log hashes to after Ticks steps. Unity's Mono runtime and
        /// plain CoreCLR both assert it, which is the only thing that catches a
        /// fixed-point or JIT difference between the two runtimes the game ships
        /// on. Update it only when the simulation intentionally changes, and only
        /// after both runtimes agree on the new value.
        /// </summary>
        public const ulong GoldenHash = 0x29D703B6C716AB37UL;

        /// <summary>
        /// The factions the golden hash was taken over. Factions are hashed, so
        /// any other pair is a different match and the two runtimes would be
        /// compared against different worlds.
        /// </summary>
        public const Faction Peer0Faction = Faction.TreeSpirits;
        public const Faction Peer1Faction = Faction.Hellfire;

        // Ids are handed out in MatchScenario.Build order and never reused.
        private const int Base0 = 0, Worker0A = 1, Worker0B = 2, Melee0 = 3, Ranged0 = 4, Node0 = 5;
        private const int Base1 = 6, Worker1A = 7, Worker1B = 8, Melee1 = 9, Ranged1 = 10, Node1 = 11;

        public static List<Command>[] Build()
        {
            var log = new List<Command>[Ticks];
            for (int t = 0; t < Ticks; t++) log[t] = new List<Command>();

            var seq = new int[MatchScenario.Peers];
            // Its own stream: drawing from World.Random would change the
            // simulation's draw count, which is hashed state.
            var script = new DetRandom(0xBADC0DE);

            Add(log, 2, 0, seq, CommandType.Gather, Worker0A, FixVec2.Zero, Node0);
            Add(log, 2, 0, seq, CommandType.Gather, Worker0B, FixVec2.Zero, Node0);
            Add(log, 2, 1, seq, CommandType.Gather, Worker1A, FixVec2.Zero, Node1);
            Add(log, 2, 1, seq, CommandType.Gather, Worker1B, FixVec2.Zero, Node1);

            // Walk both fighters at the same cell so combat resolves inside the log.
            Add(log, 40, 0, seq, CommandType.Move, Melee0, At(31, 31), 0);
            Add(log, 40, 1, seq, CommandType.Move, Melee1, At(31, 31), 0);
            Add(log, 40, 0, seq, CommandType.Move, Ranged0, At(33, 31), 0);
            Add(log, 40, 1, seq, CommandType.Move, Ranged1, At(29, 31), 0);

            Add(log, 150, 0, seq, CommandType.Build, -1, At(20, 20), 0);
            Add(log, 150, 1, seq, CommandType.Build, -1, At(44, 44), 0);

            Add(log, 300, 0, seq, CommandType.Produce, Base0, FixVec2.Zero, 0);
            Add(log, 300, 1, seq, CommandType.Produce, Base1, FixVec2.Zero, 0);
            Add(log, 420, 0, seq, CommandType.Produce, Base0, FixVec2.Zero, 0);
            Add(log, 420, 1, seq, CommandType.Produce, Base1, FixVec2.Zero, 0);

            for (int t = 60; t < Ticks; t += 37)
            {
                for (int peer = 0; peer < MatchScenario.Peers; peer++)
                {
                    int entity = peer == 0 ? Melee0 : Melee1;
                    Add(log, t, peer, seq, CommandType.Move, entity,
                        At(script.NextInt(MatchScenario.MapSize), script.NextInt(MatchScenario.MapSize)), 0);
                }
            }

            return log;
        }

        private static void Add(List<Command>[] log, int tick, int peer, int[] seq,
            CommandType type, int entity, FixVec2 target, int arg)
        {
            log[tick].Add(new Command(tick, peer, seq[peer]++, type, entity, target, arg));
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }
    }
}
