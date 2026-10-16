using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    internal static class BalanceChecks
    {
        private static readonly IReadOnlyList<Command> Idle = Array.Empty<Command>();

        internal static void Check()
        {
            OpeningArmiesHaveComparablePrices();
            EveryCrossFactionMatchRepeats();
        }

        private static void OpeningArmiesHaveComparablePrices()
        {
            int minimum = int.MaxValue, maximum = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                Faction faction = (Faction)f;
                World world = MatchScenario.Build(0xC0FFEE, faction, faction);
                int cost = 0;
                for (int id = 0; id < world.EntityCount; id++)
                {
                    Entity entity = world.GetEntity(id);
                    // Military starting units only; starting bank, base and
                    // workers cannot dilute a large opening-army difference.
                    if (entity.Owner == 0 && entity.Kind == EntityKind.Unit)
                        cost += FactionData.Production(faction, entity.Role, entity.Slot).Resources;
                }
                minimum = Math.Min(minimum, cost);
                maximum = Math.Max(maximum, cost);
                Console.WriteLine("balance opening army: " + faction + " = " + cost);
            }
            if (minimum <= 0 || maximum * 2 > minimum * 3)
                throw new Exception("Opening army price spread exceeds 1.5x: " + minimum + ".." + maximum);
        }

        private static void EveryCrossFactionMatchRepeats()
        {
            for (int a = 0; a < FactionData.FactionCount; a++)
                for (int b = 0; b < FactionData.FactionCount; b++)
                {
                    if (a == b) continue;
                    World first = MatchScenario.Build(0xC0FFEE, (Faction)a, (Faction)b);
                    World repeat = MatchScenario.Build(0xC0FFEE, (Faction)a, (Faction)b);
                    for (int peer = 0; peer < 2; peer++)
                    {
                        first.SetPeerAi(peer, true);
                        repeat.SetPeerAi(peer, true);
                    }
                    for (int tick = 0; tick < 2000 && !first.MatchOver; tick++)
                    {
                        first.Step(Idle);
                        repeat.Step(Idle);
                        if (first.Hash() != repeat.Hash())
                            throw new Exception("Balance repeat differs: " + (Faction)a + "/" + (Faction)b + " tick " + first.Tick);
                    }
                }
        }
    }
}
