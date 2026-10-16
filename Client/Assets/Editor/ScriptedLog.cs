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
        public const ulong GoldenHash = 0x42E9F28F9B63D88DUL;

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

        // The two production buildings, placed by the Builds at tick 150 right
        // after the neutral rocks MatchScenario spawns last. The fighters below
        // come out of these: a 세계수 정령 or 지옥불 base makes workers and
        // nothing that fights.
        private const int Works0 = 18, Works1 = 19;

        // Peer 0's 기술 건물, placed by the Build at tick 250. Ids are handed out
        // in order and never reused, so it is 20 on every run of this log;
        // Replay's UpgradeChecks.ScriptedLogResearches asserts the id still
        // names it and that the order lands.
        private const int Tech0 = 20;

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

            // One of every building a faction can place, in the order a player
            // reaches them: the production building opens tier 2, which is what the
            // tech building needs, so a log that only ever placed one kind would
            // leave the tier gate on Build untested by the golden hash.
            Add(log, 150, 0, seq, CommandType.Build, -1, At(20, 20), (int)Role.Production);
            Add(log, 150, 1, seq, CommandType.Build, -1, At(44, 44), (int)Role.Production);

            // On the tick the production building finishes, so tier 2 stands, and
            // as early as that allows: the 기술 건물 has to stand by 370 for the
            // research below to land inside the log.
            Add(log, 250, 0, seq, CommandType.Build, -1, At(20, 22), (int)Role.Tech);
            Add(log, 250, 1, seq, CommandType.Build, -1, At(44, 42), (int)Role.Tech);

            Add(log, 300, 0, seq, CommandType.Build, -1, At(22, 20), (int)Role.Supply);
            Add(log, 300, 1, seq, CommandType.Build, -1, At(42, 44), (int)Role.Supply);

            Add(log, 480, 0, seq, CommandType.Build, -1, At(22, 22), (int)Role.Defense);
            Add(log, 480, 1, seq, CommandType.Build, -1, At(42, 42), (int)Role.Defense);

            // Peer 1 orders one 용암 갑각 악마 rather than two. It costs 90 per
            // docs/FACTION-MECHANICS.md, and a second one at 420 would leave
            // peer 1 with 35 at 480, short of the defense building below.
            Add(log, 300, 0, seq, CommandType.Produce, Works0, FixVec2.Zero, 0);
            Add(log, 300, 1, seq, CommandType.Produce, Works1, FixVec2.Zero, 0);
            Add(log, 420, 0, seq, CommandType.Produce, Works0, FixVec2.Zero, 0);

            // One research, on the 기술 건물 that finished at 369, so the golden
            // hash covers the Research command, the price it takes, the
            // ResearchSystem clock and the level landing at 529. 공격력 level 1 is
            // 50 and peer 0 has 155 by then. The attack order after it sends
            // peer 0's archer at peer 1's melee unit, so the hits at 576 and 594
            // land with the +1 in them; Replay's ScriptedLogResearches asserts
            // that a researched hit happens.
            Add(log, 370, 0, seq, CommandType.Research, Tech0, FixVec2.Zero, 0);
            Add(log, 530, 0, seq, CommandType.Attack, Ranged0, FixVec2.Zero, Melee1);

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
