using System;
using System.Collections.Generic;
using System.Globalization;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Host
{
    /// <summary>
    /// Runs both AI peers on the client's map. Each game has a second world
    /// stepped beside the first, so the tick where their states part is known
    /// before a win or an average can hide it. Sim draws no randomness yet: seed
    /// plus game index changes only the hashed RNG state. Alternating peer slots
    /// is the only gameplay variation between games of a pair.
    /// </summary>
    internal static class Arena
    {
        private const int DefaultGames = 1;
        private const int DefaultTicks = 6000;
        private const ulong DefaultSeed = 0xC0FFEE;
        private static readonly IReadOnlyList<Command> NoCommands = Array.Empty<Command>();

        private sealed class PairResult
        {
            public Faction A;
            public Faction B;
            public int Games;
            public int AWins;
            public int BWins;
            public int Draws;
            public int Peer0Wins;
            public int Peer1Wins;
            public long Ticks;
            public long PeakA;
            public long PeakB;
        }

        private sealed class FactionResult
        {
            public int Games;
            public int Wins;
            public int Losses;
            public int Draws;
        }

        public static int Run(string[] args)
        {
            int games = DefaultGames, ticks = DefaultTicks;
            ulong seed = DefaultSeed;
            Faction? a = null, b = null;

            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length || !TryOption(args[i], args[i + 1],
                    ref games, ref ticks, ref seed, ref a, ref b))
                {
                    PrintUsage();
                    return 2;
                }
            }
            if (a.HasValue != b.HasValue)
            {
                Console.WriteLine("-a and -b must be given together");
                return 2;
            }
            if (games < 1 || ticks < 1)
            {
                PrintUsage();
                return 2;
            }

            var factions = (Faction[])Enum.GetValues(typeof(Faction));
            var totals = new FactionResult[factions.Length];
            for (int i = 0; i < totals.Length; i++) totals[i] = new FactionResult();
            var pairs = new List<PairResult>();
            long peer0Wins = 0, peer1Wins = 0, draws = 0;

            Console.WriteLine("arena: " + (a.HasValue ? "1 pair" : factions.Length * factions.Length + " pairs") +
                              ", " + games + " game(s) each, at most " + ticks +
                              " ticks, seed 0x" + seed.ToString("X"));
            if (games > 1)
                Console.WriteLine("note: Sim draws no randomness yet; -seed changes nothing, side alternation is the only variation");
            try
            {
                foreach (Faction first in factions)
                {
                    if (a.HasValue && first != a.Value) continue;
                    foreach (Faction second in factions)
                    {
                        if (b.HasValue && second != b.Value) continue;
                        PairResult pair = PlayPair(first, second, games, ticks, seed);
                        pairs.Add(pair);
                        peer0Wins += pair.Peer0Wins;
                        peer1Wins += pair.Peer1Wins;
                        draws += pair.Draws;
                        AddTotals(totals[(int)first], pair.Games, pair.AWins, pair.BWins, pair.Draws);
                        AddTotals(totals[(int)second], pair.Games, pair.BWins, pair.AWins, pair.Draws);
                        Console.WriteLine(FormatPair(pair));
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine("Faction       Games   Wins Losses  Draws  Win rate");
            foreach (Faction faction in factions)
            {
                FactionResult result = totals[(int)faction];
                if (result.Games == 0) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-13} {1,5} {2,6} {3,6} {4,6} {5,8:F2}%",
                    faction, result.Games, result.Wins, result.Losses, result.Draws,
                    100.0 * result.Wins / result.Games));
            }

            Console.WriteLine("peer 0 wins " + peer0Wins + ", peer 1 wins " + peer1Wins + ", draws " + draws);

            pairs.Sort((left, right) =>
            {
                // Divide by games only after comparing, so the order is exact
                // when a future run gives different pairs different game counts.
                long leftSpread = Math.Abs(left.AWins - left.BWins) * (long)right.Games;
                long rightSpread = Math.Abs(right.AWins - right.BWins) * (long)left.Games;
                int spreadOrder = rightSpread.CompareTo(leftSpread);
                if (spreadOrder != 0) return spreadOrder;
                int firstOrder = left.A.CompareTo(right.A);
                return firstOrder != 0 ? firstOrder : left.B.CompareTo(right.B);
            });
            Console.WriteLine("Most skewed pairs (win difference / games) (ties ordered by faction):");
            for (int i = 0; i < Math.Min(5, pairs.Count); i++)
            {
                PairResult pair = pairs[i];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-28} {1,6:F2}% ({2} to {3})", pair.A + " vs " + pair.B,
                    100.0 * Math.Abs(pair.AWins - pair.BWins) / pair.Games,
                    pair.AWins, pair.BWins));
            }
            return 0;
        }

        /// <summary>One result line, also used by SelfCheck to exercise the whole pair path twice.</summary>
        internal static string ResultLine(Faction a, Faction b, int games, int ticks, ulong seed) =>
            FormatPair(PlayPair(a, b, games, ticks, seed));

        private static void PrintUsage()
        {
            Console.WriteLine("usage: arena [-games N] [-ticks T] [-seed S] [-a <faction>] [-b <faction>]");
            Console.WriteLine("a is peer 0 on even games and peer 1 on odd games (game indexes start at 0)");
        }

        private static bool TryOption(string name, string value, ref int games, ref int ticks,
            ref ulong seed, ref Faction? a, ref Faction? b)
        {
            if (name == "-games") return int.TryParse(value, out games);
            if (name == "-ticks") return int.TryParse(value, out ticks);
            if (name == "-seed")
            {
                string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? value.Substring(2) : value;
                NumberStyles style = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
                return ulong.TryParse(digits, style, CultureInfo.InvariantCulture, out seed);
            }
            if (name == "-a" || name == "-b")
            {
                if (!Enum.TryParse(value, true, out Faction faction) ||
                    !Enum.IsDefined(typeof(Faction), faction)) return false;
                if (name == "-a") a = faction;
                else b = faction;
                return true;
            }
            return false;
        }

        private static PairResult PlayPair(Faction a, Faction b, int games, int ticks, ulong seed)
        {
            var result = new PairResult { A = a, B = b, Games = games };
            for (int game = 0; game < games; game++)
            {
                ulong gameSeed = unchecked(seed + (ulong)game);
                int peerA = game % 2, peerB = 1 - peerA;
                Faction peer0 = peerA == 0 ? a : b;
                Faction peer1 = peerA == 0 ? b : a;
                World first = NewWorld(gameSeed, peer0, peer1);
                World repeat = NewWorld(gameSeed, peer0, peer1);
                int peakA = first.GetPopulation(peerA), peakB = first.GetPopulation(peerB);
                CheckHashes(first, repeat, a, b, gameSeed);

                while (first.Tick < ticks && !first.MatchOver)
                {
                    first.Step(NoCommands);
                    repeat.Step(NoCommands);
                    CheckHashes(first, repeat, a, b, gameSeed);
                    peakA = Math.Max(peakA, first.GetPopulation(peerA));
                    peakB = Math.Max(peakB, first.GetPopulation(peerB));
                }

                if (first.MatchOver && (first.Winner == 0 || first.Winner == 1))
                {
                    if (first.Winner == peerA) result.AWins++;
                    else result.BWins++;
                    if (first.Winner == 0) result.Peer0Wins++;
                    else result.Peer1Wins++;
                }
                else result.Draws++;
                result.Ticks += first.Tick;
                result.PeakA += peakA;
                result.PeakB += peakB;
            }
            return result;
        }

        private static void CheckHashes(World first, World repeat, Faction a, Faction b, ulong seed)
        {
            if (first.Hash() != repeat.Hash())
                throw new InvalidOperationException("FAIL: desync at tick " + first.Tick +
                    " (" + a + " vs " + b + ", 0x" + seed.ToString("X") + ")");
        }

        /// <summary>
        /// One game with fixed seats, played without the pair bookkeeping: the
        /// peer that won it, or -1 for a draw at the tick cap. SelfCheck plays each
        /// game of a pair through this and checks that the pair line credits every
        /// win to the faction in the winning seat, not to the seat.
        /// </summary>
        internal static int GameWinner(Faction peer0, Faction peer1, int ticks, ulong seed)
        {
            World world = NewWorld(seed, peer0, peer1);
            while (world.Tick < ticks && !world.MatchOver) world.Step(NoCommands);
            return world.MatchOver && (world.Winner == 0 || world.Winner == 1) ? world.Winner : -1;
        }

        private static World NewWorld(ulong seed, Faction a, Faction b)
        {
            World world = MatchScenario.Build(seed, a, b);
            world.SetPeerAi(0, true);
            world.SetPeerAi(1, true);
            return world;
        }

        private static void AddTotals(FactionResult total, int games, int wins, int losses, int draws)
        {
            total.Games += games;
            total.Wins += wins;
            total.Losses += losses;
            total.Draws += draws;
        }

        private static string FormatPair(PairResult pair) =>
            string.Format(CultureInfo.InvariantCulture,
                "{0,-31} a wins {1,3}, b wins {2,3}, draws {3,3}, avg ticks {4,6:F1}, avg peak population {5,5:F1}/{6,5:F1}",
                pair.A + " vs " + pair.B + ":", pair.AWins, pair.BWins, pair.Draws,
                (double)pair.Ticks / pair.Games, (double)pair.PeakA / pair.Games,
                (double)pair.PeakB / pair.Games);
    }
}
