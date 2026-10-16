using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What a unit card offers for abilities: which actives a body holds, where
    /// on the card they sit, what each cell says, and whether it is locked or
    /// cooling. Pulled out of CommandCard for the reason <see cref="ProductionMenu"/>
    /// was: the questions worth checking here (does every faction's list fit the
    /// card, does a cell agree with what the simulation would refuse) need no
    /// graphics device, so Replay/AbilityMenuChecks.cs runs them under plain
    /// dotnet. Free of UnityEngine on purpose.
    ///
    /// Nothing here is state and nothing here is a rule. Locked is
    /// <see cref="World.AbilityUnlocked"/>, the price is <see cref="World.AbilityCost"/>,
    /// the cooldown is the caster's own <see cref="Entity.AbilityCooldown"/>; a
    /// second copy of any of them is a cell that says one thing while the
    /// simulation answers another.
    /// </summary>
    public static class AbilityMenu
    {
        /// <summary>
        /// The cells of the fighter card that hold abilities, in the order they
        /// fill: the fourth column first (U J M), then the rest of row 2. Cell 6
        /// is free on that card too and is left alone, so the first three
        /// abilities of every faction keep the keys they were learned on.
        /// </summary>
        public static readonly int[] FighterCells = { 3, 7, 11, 8, 9, 10 };

        /// <summary>The worker card's: the same, minus cell 8, where Build lives.</summary>
        public static readonly int[] WorkerCells = { 3, 7, 11, 9, 10 };

        /// <summary>
        /// The most pages an ability list may need. Today every body holds one
        /// active, so this is a ceiling for the day a roster grows, the way
        /// <see cref="ProductionMenu.MaxPages"/> is for production.
        /// </summary>
        public const int MaxPages = 4;

        /// <summary>
        /// The cell the card puts More on when a list pages: the last cell of the
        /// 3x4 card, the same cell the building card uses, so the key that turns a
        /// page is M on every card. A paging list gives up this cell, so both
        /// cell lists contain it.
        /// </summary>
        public const int MoreCell = 11;

        /// <summary>
        /// One active the card can turn into a button, or one autocast the info
        /// panel lists. The strings are built once with the entry, so a frame that
        /// draws the card allocates none. What changes between frames (locked,
        /// cooling, the price a research changed, a deployed cannon) is read live
        /// and only picks among them.
        /// </summary>
        public readonly struct Entry
        {
            /// <summary>The row's index in the faction's ability table, which is the Arg of a Cast.</summary>
            public readonly int Index;
            public readonly AbilityRow Row;

            /// <summary>The cell text at the row's own price: the name, and the price on a second line when there is one.</summary>
            public readonly string Label;

            /// <summary>The same at the price once the boost research lands. Equal to Label on a row with no such price.</summary>
            public readonly string BoostedLabel;

            /// <summary>전개 only: the name of the cell on a body already deployed, at which it packs up. Equal to Label elsewhere.</summary>
            public readonly string ReleaseLabel;

            /// <summary>The info panel line of an autocast, and the same with "locked" beside it.</summary>
            public readonly string AutocastText;
            public readonly string LockedAutocastText;

            public Entry(int index, AbilityRow row)
            {
                Index = index;
                Row = row;
                Label = Labelled(row.Name, row.Cost);
                BoostedLabel = row.Boost >= 0 && row.BoostedCost > 0 ? Labelled(row.Name, row.BoostedCost) : Label;
                ReleaseLabel = row.Kind == AbilityKind.Deploy ? row.Name + " 해제" : Label;
                AutocastText = row.Name + "  autocast";
                LockedAutocastText = AutocastText + ", locked";
            }

            private static string Labelled(string name, int cost) => cost > 0 ? name + "\n" + cost : name;
        }

        // One listing per (faction, role, slot), built the first time a card asks
        // and kept: the ability table is static, so it cannot go stale. Not
        // simulation state, so a Dictionary is fine here; it is only ever looked up.
        private sealed class Listing
        {
            public Entry[] Actives;
            public Entry[] Autocasts;
        }

        private static readonly Dictionary<int, Listing> listings = new Dictionary<int, Listing>();

        private static Listing ListingOf(Faction faction, Role role, int slot)
        {
            int key = ((int)faction * FactionData.RoleCount + (int)role) * 64 + slot;
            if (listings.TryGetValue(key, out Listing listing)) return listing;

            var actives = new List<Entry>();
            var autocasts = new List<Entry>();
            int count = FactionData.AbilityCount(faction);
            for (int i = 0; i < count; i++)
            {
                AbilityRow row = FactionData.Ability(faction, i);
                if (row.Role != role || row.Slot != slot) continue;
                if (FactionData.IsCast(row.Kind)) actives.Add(new Entry(i, row));
                else if (IsAutocast(row.Kind)) autocasts.Add(new Entry(i, row));
            }
            listing = new Listing { Actives = actives.ToArray(), Autocasts = autocasts.ToArray() };
            listings[key] = listing;
            return listing;
        }

        /// <summary>
        /// The cells the card of a body of this kind gives abilities: the worker
        /// card for a worker, the fighter card for anything else that walks.
        /// </summary>
        public static int[] CellsOf(EntityKind kind) => kind == EntityKind.Worker ? WorkerCells : FighterCells;

        /// <summary>
        /// The actives this entry holds, by ascending table index: the rows a
        /// Cast may name for it (the same Role and Slot test the simulation's
        /// CastRefusal takes), in the order the card places them. The cached
        /// list itself, never to be changed by the caller.
        /// </summary>
        public static IReadOnlyList<Entry> For(Faction faction, Role role, int slot) =>
            ListingOf(faction, role, slot).Actives;

        /// <summary>
        /// The abilities this entry holds that go off on their own, by ascending
        /// table index. They get no button: the info panel says "autocast" and
        /// nothing a player presses changes them. Cached like <see cref="For"/>.
        /// </summary>
        public static IReadOnlyList<Entry> Autocasts(Faction faction, Role role, int slot) =>
            ListingOf(faction, role, slot).Autocasts;

        /// <summary>
        /// The kinds docs/TECH-TREE.md's ability tables type as 오토캐스트:
        /// 질풍, 굴러 들이받기, 잔해 수리 and 수리. The simulation has no such
        /// flag (it runs them from AbilitySystem with no order), so the view
        /// keeps its own list. Replay/AbilityMenuChecks.cs compares it with a
        /// second hand-typed list of the four names, which catches a row added
        /// or renamed on one side only; it does not read the document.
        /// </summary>
        public static bool IsAutocast(AbilityKind kind) =>
            kind == AbilityKind.GaleAfterAttack || kind == AbilityKind.RollCharge ||
            kind == AbilityKind.RemnantRepair || kind == AbilityKind.Repair;

        /// <summary>
        /// How many pages a list of this many actives takes on a card with this
        /// many ability cells: one when it fits whole, otherwise the entries
        /// divided by one fewer than the cells (the last cell is More), rounded
        /// up. An empty list is one empty page, so the card never has zero.
        /// </summary>
        public static int Pages(int count, int capacity) =>
            count <= capacity ? 1 : (count + capacity - 2) / (capacity - 1);

        /// <summary>
        /// Which entries sit on this page, and the page that is, clamped into
        /// range so a stale page number shows the nearest real one. The same
        /// shape as <see cref="ProductionMenu.PageRange"/>.
        /// </summary>
        public static int PageRange(int count, int capacity, int page, out int first, out int length)
        {
            if (count <= capacity)
            {
                first = 0;
                length = count;
                return 0;
            }
            int size = capacity - 1;
            page = Math.Max(0, Math.Min(page, Pages(count, capacity) - 1));
            first = page * size;
            length = Math.Min(size, count - first);
            return page;
        }

        /// <summary>
        /// Whether this body is one a Cast of this row can name: the row's own
        /// Role and Slot, alive, not growing, and not a building or a node. The
        /// opening tests of CastRefusal ("caster" and "row"), so a selection is
        /// filtered to exactly the bodies the simulation would not refuse for
        /// being the wrong body or one that cannot use abilities yet.
        /// </summary>
        public static bool Holds(Entity e, AbilityRow row) =>
            e.Alive && !World.IsGrowing(e) && row.Role == e.Role && row.Slot == e.Slot &&
            e.Kind != EntityKind.Building && e.Kind != EntityKind.ResourceNode;

        /// <summary>
        /// Whether a Cast of this row, pressed with this representative leading
        /// the selection, goes to this body. Every body that <see cref="Holds"/> the
        /// row, with two exceptions that are kinds and not names:
        ///
        /// 굳기 goes to the representative alone, because it ends its caster and
        /// one press that killed every selected 성벽 골렘 is a wall nobody asked for.
        ///
        /// 전개 goes to the bodies in the representative's state. It flips each body
        /// it reaches, so a mixed selection sent to all of them would pack the
        /// deployed and deploy the packed and never agree; sent to one state it
        /// brings the whole group to the other, and the next press moves them
        /// together.
        /// </summary>
        public static bool Receives(Entity representative, Entity e, AbilityRow row)
        {
            if (!Holds(e, row)) return false;
            if (row.Kind == AbilityKind.Harden) return e.Id == representative.Id;
            if (row.Kind == AbilityKind.Deploy) return e.Deployed == representative.Deployed;
            return true;
        }

        /// <summary>Whether a Cast of this row needs a point, which is every row but the ones aimed at nothing.</summary>
        public static bool NeedsPoint(AbilityRow row) => row.CastTarget != CastTarget.NoTarget;

        /// <summary>
        /// True while the research that unlocks this ability has not landed, for
        /// the whole peer: it is the simulation's own AbilityUnlocked, the rule
        /// CastRefusal answers "unlock" on.
        /// </summary>
        public static bool Locked(World world, int peer, AbilityRow row) => !world.AbilityUnlocked(peer, row);

        /// <summary>
        /// How much of the wait is left, 1 for all of it and 0 for none. The
        /// cooldown over the row's cooldown for every active that has one. 전개
        /// has none and waits on its transition instead, so its clock over the
        /// row's ticks, which is the other thing that makes a second Cast
        /// refuse. Clamped, so a clock read a tick late never draws past the
        /// ends of the meter.
        /// </summary>
        public static float Remaining(Entity caster, AbilityRow row)
        {
            if (row.Cooldown > 0) return Clamp01(caster.AbilityCooldown / (float)row.Cooldown);
            if (row.Kind == AbilityKind.Deploy && row.Ticks > 0) return Clamp01(caster.AbilityTicksLeft / (float)row.Ticks);
            return 0f;
        }

        /// <summary>
        /// The cell's text: the name, and on a second line the price when the
        /// row has one. The price is what this peer would be charged
        /// (<see cref="World.AbilityCost"/>), so 깨우기 reads 25 once its cost
        /// research lands. 전개 on a body already deployed reads as taking it
        /// down, because the same cell does both and the name alone would not
        /// say which is next. Picks among the strings the entry built.
        /// </summary>
        public static string Label(World world, int peer, Entity caster, Entry entry)
        {
            if (entry.Row.Kind == AbilityKind.Deploy && caster.Deployed) return entry.ReleaseLabel;
            return world.AbilityCost(peer, entry.Row) == entry.Row.Cost ? entry.Label : entry.BoostedLabel;
        }

        /// <summary>
        /// One info panel line for an autocast: "name  autocast", and "locked"
        /// beside it while its research has not landed, so a player does not
        /// wait on a body that was never going to do it.
        /// </summary>
        public static string AutocastLine(World world, int peer, Entry entry) =>
            Locked(world, peer, entry.Row) ? entry.LockedAutocastText : entry.AutocastText;

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
