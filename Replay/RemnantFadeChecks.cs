using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #182's check on the number MatchView divides debris life by. Program.cs
    /// carries only the one line that calls Check().
    ///
    /// RemnantFade.LongestTicks is asked of a world with nothing researched and of
    /// one where a single peer has researched 오래 남는 잔해, the research run
    /// through the simulation's own Research command. The expected numbers are the
    /// simulation's constants, not a second typing of the rule.
    /// </summary>
    internal static class RemnantFadeChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            World world = Build(out int tech);
            Require(RemnantFade.LongestTicks(world) == World.RemnantTicks,
                "an unresearched world fades debris over " + RemnantFade.LongestTicks(world) +
                " ticks, expected " + World.RemnantTicks);

            int row = -1;
            for (int i = 0; i < FactionData.UpgradeCount(Faction.RockGolems); i++)
            {
                if (FactionData.Upgrade(Faction.RockGolems, i).Effect == UpgradeEffect.RemnantTicks) row = i;
            }
            Require(row >= 0, "돌 골렘 has no 오래 남는 잔해 row");

            world.Step(new List<Command>
            {
                new Command(0, 0, 0, CommandType.Research, tech, FixVec2.Zero, row),
            });
            Require(world.GetEntity(tech).ResearchIndex == row, "research of 오래 남는 잔해 was refused");
            var idle = new List<Command>();
            for (int t = 0; t < FactionData.Upgrade(Faction.RockGolems, row).Ticks; t++) world.Step(idle);
            Require(world.UpgradeLevelOf(0, UpgradeEffect.RemnantTicks) > 0, "오래 남는 잔해 never landed");

            Require(RemnantFade.LongestTicks(world) == World.RemnantTicksResearched,
                "a world where one peer researched 오래 남는 잔해 fades debris over " +
                RemnantFade.LongestTicks(world) + " ticks, expected " + World.RemnantTicksResearched);
        }

        private static World Build(out int tech)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SetPeerFaction(1, Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.GrantResources(1, 10000);
            Fix half = Fix.Ratio(1, 2);
            tech = world.SpawnBuilding(0, Role.Tech, new FixVec2(Fix.FromInt(20) + half, Fix.FromInt(20) + half), complete: true);
            return world;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
