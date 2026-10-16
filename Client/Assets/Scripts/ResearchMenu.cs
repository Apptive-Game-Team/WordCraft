using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What a 기술 건물's card offers and what its info panel says while it works:
    /// which upgrade rows a faction lists, the text on each cell, how far along
    /// a running research is, and the +attack/+armor readout beside a unit's name.
    ///
    /// Pulled out of CommandCard and Hud for the reason <see cref="ProductionMenu"/>
    /// was: the questions worth checking here — does every faction's list fit the
    /// card's pages, does a cell say the level and price the simulation would
    /// charge — need no graphics device, so Replay/ResearchMenuChecks.cs runs them
    /// under plain dotnet. Free of UnityEngine on purpose. CommandCard turns the
    /// list into CardSlots and Hud draws the strings.
    ///
    /// Nothing here is state and nothing here is a rule. Levels come from
    /// <see cref="World.UpgradeLevel(int, int)"/>, the price from
    /// <see cref="World.ResearchCost"/>, the bonuses from <see cref="World.AttackBonus"/>
    /// and <see cref="World.ArmorBonus"/>; a second copy of any of them is a
    /// card that says one number while the simulation charges another.
    /// </summary>
    public static class ResearchMenu
    {
        /// <summary>One row of the faction's upgrade table, as the card lists it.</summary>
        public readonly struct Entry
        {
            /// <summary>The row's index in the faction's table, which is the Arg of a Research.</summary>
            public readonly int Index;
            public readonly string Name;
            public readonly int MaxLevel;

            public Entry(int index, string name, int maxLevel)
            {
                Index = index;
                Name = name;
                MaxLevel = maxLevel;
            }
        }

        /// <summary>
        /// Every row this faction can research, by ascending index, which is the
        /// order the card places them. A row with no level to buy (MaxLevel 0) is
        /// the table's way of saying "not there" and is left off, so the card
        /// never carries a button for an index the simulation refuses outright.
        /// </summary>
        public static List<Entry> For(Faction faction)
        {
            var entries = new List<Entry>();
            int count = FactionData.UpgradeCount(faction);
            for (int i = 0; i < count; i++)
            {
                UpgradeRow row = FactionData.Upgrade(faction, i);
                if (row.MaxLevel <= 0) continue;
                entries.Add(new Entry(i, row.Name, row.MaxLevel));
            }
            return entries;
        }

        /// <summary>
        /// The rows of <see cref="For(Faction)"/> that one building entry
        /// researches: those whose ResearchAt names it, the test the simulation's
        /// own CanResearch takes. docs/TECH-TREE.md 묶음 3 moves rows onto second
        /// and third research buildings (바람 토템, 재 분화구, 결속대 and the
        /// rest), and their card lists what they run rather than every row.
        /// </summary>
        public static List<Entry> For(Faction faction, Role buildingRole, int buildingSlot)
        {
            List<Entry> all = For(faction);
            var entries = new List<Entry>();
            for (int i = 0; i < all.Count; i++)
            {
                UpgradeRow row = FactionData.Upgrade(faction, all[i].Index);
                if (row.ResearchAt.Role == buildingRole && row.ResearchAt.Slot == buildingSlot) entries.Add(all[i]);
            }
            return entries;
        }


        /// <summary>
        /// True when this peer has bought every level of the row. Asked of the
        /// level rather than of CanResearch: a row short of tier or resources
        /// stays live and the simulation refuses it, the rule the build menu
        /// follows, so the only cell that goes dead is the one with nothing left
        /// to buy.
        /// </summary>
        public static bool Maxed(World world, int peer, Entry entry) =>
            world.UpgradeLevel(peer, entry.Index) >= entry.MaxLevel;

        /// <summary>
        /// The cell's text: "name Lv n/max" on the first line, the price of the
        /// next level on the second. The price is the simulation's own
        /// (<see cref="World.ResearchCost"/>, resources times the level being
        /// bought), so level 2 reads twice level 1. A maxed row has no next level
        /// and says so instead of quoting a price nobody will be charged.
        /// </summary>
        public static string Label(World world, int peer, Entry entry)
        {
            int level = world.UpgradeLevel(peer, entry.Index);
            string head = entry.Name + " Lv " + level + "/" + entry.MaxLevel;
            return head + "\n" + (level >= entry.MaxLevel ? "max" : world.ResearchCost(peer, entry.Index).ToString());
        }

        /// <summary>
        /// The name of the row this building is researching, or null when it is
        /// not researching. The info panel writes "researching " in front of it.
        /// </summary>
        public static string Researching(World world, Entity building)
        {
            if (!building.Alive || building.ResearchIndex < 0) return null;
            string name = FactionData.Upgrade(world.FactionOf(building.Owner), building.ResearchIndex).Name;
            return name.Length > 0 ? name : "research";
        }

        /// <summary>
        /// How far through its research this building is, 0 to 1, or -1 when it
        /// is not researching. 1 - ResearchTicksLeft / Ticks, the same shape as
        /// the production queue's bar. Clamped, so a row timed at no ticks or a
        /// clock read a tick late never draws past the ends of the meter.
        /// </summary>
        public static float Progress(World world, Entity building)
        {
            if (!building.Alive || building.ResearchIndex < 0) return -1f;
            int ticks = FactionData.Upgrade(world.FactionOf(building.Owner), building.ResearchIndex).Ticks;
            if (ticks <= 0) return 1f;
            float done = 1f - building.ResearchTicksLeft / (float)ticks;
            return Math.Max(0f, Math.Min(1f, done));
        }

        /// <summary>
        /// The peer's current bonuses in the form the name line shows: "+1/+2",
        /// attack first and armor second. Both zero reads "+0/+0" rather than
        /// nothing, so the readout is there to be learned before anything has
        /// been researched.
        /// </summary>
        public static string Bonus(World world, int peer) =>
            "+" + world.AttackBonus(peer) + "/+" + world.ArmorBonus(peer);

        /// <summary>True when either bonus is above zero, which is when the readout is worth a colour.</summary>
        public static bool HasBonus(World world, int peer) =>
            world.AttackBonus(peer) > 0 || world.ArmorBonus(peer) > 0;
    }
}
