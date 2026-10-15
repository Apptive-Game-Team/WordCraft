using System;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #154's checks: the two opening fighters MatchScenario spawns follow
    /// the roster. 인간's melee entry 0 is a blank row with no name and no art, and
    /// the map used to spawn it anyway because it spawned Role.Melee for everyone.
    ///
    /// Three questions. 세계수 정령 and 지옥불 still open on Melee 0 and Ranged 0,
    /// because ScriptedLog names their entity ids and its golden hash was taken
    /// over those bodies. A 인간 opening has no nameless body. And every faction's
    /// two openers are named and armed, which is the whole rule OpeningFighter
    /// states, asked of the bodies the map actually spawned rather than of the
    /// function that was meant to pick them.
    /// </summary>
    internal static class OpeningArmyChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            ScriptedLogFactionsKeepTheirOpeners();
            HumansOpenWithNoNamelessBody();
            HumansOpenWithTwoSpecialForces();
            AMixedMatchPlacesEachPeersOwnOpeners();
            EveryFactionOpensTwoArmedNamedFighters();
        }

        private static void ScriptedLogFactionsKeepTheirOpeners()
        {
            foreach (Faction faction in new[] { Faction.TreeSpirits, Faction.Hellfire })
            {
                (Role Role, int Slot) first = FactionData.OpeningFighter(faction, 0);
                (Role Role, int Slot) second = FactionData.OpeningFighter(faction, 1);
                Check(first.Role == Role.Melee && first.Slot == 0,
                    faction + " no longer opens on Melee 0: " + first.Role + " " + first.Slot);
                Check(second.Role == Role.Ranged && second.Slot == 0,
                    faction + " no longer opens on Ranged 0: " + second.Role + " " + second.Slot);
            }
        }

        private static void HumansOpenWithNoNamelessBody()
        {
            World world = MatchScenario.Build(Seed, Faction.Humans, Faction.Humans);
            int units = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Kind != EntityKind.Unit || e.Owner < 0) continue;
                units++;
                Check(FactionData.Has(Faction.Humans, e.Role, e.Slot),
                    "a 인간 opening spawned the nameless body " + e.Role + " entry " + e.Slot);
            }
            Check(units == 4, "a 인간 mirror match opens with " + units + " fighters, not 4");
        }

        /// <summary>
        /// 인간's melee entry 0 is blank, so the first named, armed entry in the
        /// melee-first order and in the ranged-first order is the same one, Ranged
        /// 0, and 인간 opens with two 공수 특공대. Pinned on purpose. It is the
        /// answer the rule gives, and a roster row that changes it should show up
        /// here and be decided rather than slip in.
        /// </summary>
        private static void HumansOpenWithTwoSpecialForces()
        {
            Check(FactionData.OpeningFighter(Faction.Humans, 0).Equals((Role.Ranged, 0)),
                "인간's first opener is no longer Ranged 0");
            Check(FactionData.OpeningFighter(Faction.Humans, 1).Equals((Role.Ranged, 0)),
                "인간's second opener is no longer Ranged 0");
            World world = MatchScenario.Build(Seed, Faction.Humans, Faction.Humans);
            int special = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Kind == EntityKind.Unit && e.Owner == 0 && e.Role == Role.Ranged && e.Slot == 0) special++;
            }
            Check(special == 2, "인간 opens with " + special + " 공수 특공대, not 2");
        }

        /// <summary>
        /// Two different factions, so the two peers' openers are different rows.
        /// Each peer's two units are the OpeningFighter rows of its own faction, in
        /// spawn order, and not the other peer's.
        /// </summary>
        private static void AMixedMatchPlacesEachPeersOwnOpeners()
        {
            World world = MatchScenario.Build(Seed, Faction.Humans, Faction.TreeSpirits);
            for (int peer = 0; peer < MatchScenario.Peers; peer++)
            {
                Faction faction = world.FactionOf(peer);
                int index = 0;
                for (int i = 0; i < world.EntityCount; i++)
                {
                    Entity e = world.GetEntity(i);
                    if (e.Kind != EntityKind.Unit || e.Owner != peer) continue;
                    (Role Role, int Slot) want = FactionData.OpeningFighter(faction, index);
                    Check(e.Role == want.Role && e.Slot == want.Slot,
                        "peer " + peer + " (" + faction + ") opener " + index + " is " + e.Role + " " + e.Slot +
                        ", not " + want.Role + " " + want.Slot);
                    index++;
                }
                Check(index == 2, "peer " + peer + " opens with " + index + " fighters, not 2");
            }
        }

        private static void EveryFactionOpensTwoArmedNamedFighters()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                World world = MatchScenario.Build(Seed, faction, faction);
                int peer0Fighters = 0;
                for (int i = 0; i < world.EntityCount; i++)
                {
                    Entity e = world.GetEntity(i);
                    if (e.Kind != EntityKind.Unit || e.Owner != 0) continue;
                    peer0Fighters++;
                    Check(FactionData.Has(faction, e.Role, e.Slot),
                        faction + " opens on a nameless body: " + e.Role + " entry " + e.Slot);
                    Check(FactionData.Stats(faction, e.Role, e.Slot).HasWeapon,
                        faction + " opens on an unarmed body: " + e.Role + " entry " + e.Slot);
                }
                Check(peer0Fighters == 2, faction + " opens with " + peer0Fighters + " fighters, not 2");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
