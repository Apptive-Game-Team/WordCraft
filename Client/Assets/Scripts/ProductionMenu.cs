using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Which roster entries a faction's buildings can produce, and the order the
    /// command card lists them in. Pulled out of CommandCard so the one thing
    /// about it worth checking without a graphics device — does every faction's
    /// list still fit the card's pages — can run under plain dotnet
    /// (Replay/ProductionMenuChecks.cs), the same way issue #96 pulled
    /// HitDetection and AlertWindow out of Hits.cs and Alert.cs. Free of
    /// UnityEngine on purpose; CommandCard is the Unity-facing half that turns
    /// this list into CardSlots and keys.
    /// </summary>
    public static class ProductionMenu
    {
        /// <summary>What a building may produce, in the order the card lists it.</summary>
        public static readonly Role[] Producible = { Role.Melee, Role.Ranged, Role.Signature };

        /// <summary>
        /// Cells the command card gives production: rows 1 and 2 of its 3x4, eight
        /// in total, with row 0 kept for Rally, Cancel and Arrive
        /// (CommandCard.Building). A plain number here rather than read off
        /// CommandCard.Cols/Cells, because CLAUDE.md's layering — Sim and Net
        /// depend on nothing above them — extends the same way to this harness:
        /// Replay may not depend on a Unity-touching file, and CommandCard imports
        /// UnityEngine for its key table. docs/UI-STYLE.md 레이아웃 has the card's
        /// own geometry.
        ///
        /// A list that fits in these eight is drawn whole. A longer one is paged:
        /// the last cell becomes More, so each page holds <see cref="PageSize"/>
        /// entries, one fewer than the cells. 차원 유랑종 fields six today.
        /// </summary>
        public const int Cells = 8;

        /// <summary>Entries on one page of a list that needs paging, because the eighth cell is More.</summary>
        public const int PageSize = Cells - 1;

        /// <summary>
        /// The most pages a list may need. Four pages are 28 entries; the roster
        /// #166 designs (a dozen units a faction) needs two. Past four a player is
        /// paging for longer than a fight allows, so the harness calls it an
        /// overflow and the card is due a redesign.
        /// </summary>
        public const int MaxPages = 4;

        /// <summary>
        /// How many pages a list of this length takes: one when it fits the cells
        /// whole, otherwise the entries divided by <see cref="PageSize"/>, rounded
        /// up. An empty list is one empty page, so the card never has zero.
        /// </summary>
        public static int Pages(int count) =>
            count <= Cells ? 1 : (count + PageSize - 1) / PageSize;

        /// <summary>
        /// Which entries of the list sit on this page, and the page that is. The
        /// page is clamped into range, so a stale page number from a selection that
        /// has since changed shows the nearest real page rather than an empty card.
        /// Returned so the caller labels the page it actually drew: the entry range
        /// and a "2/3" read off the same number cannot disagree.
        /// </summary>
        public static int PageRange(int count, int page, out int first, out int length)
        {
            if (count <= Cells)
            {
                first = 0;
                length = count;
                return 0;
            }
            page = Math.Max(0, Math.Min(page, Pages(count) - 1));
            first = page * PageSize;
            length = Math.Min(PageSize, count - first);
            return page;
        }

        /// <summary>
        /// The page More turns to: the next one, and the first after the last. Here
        /// rather than in Orders so the wrap is checked headless like the rest. A
        /// stale page is clamped first, so More always lands on a page that exists.
        /// </summary>
        public static int NextPage(int page, int pages) =>
            pages <= 1 ? 0 : (Math.Max(0, Math.Min(page, pages - 1)) + 1) % pages;

        /// <summary>One roster entry the card can turn into a button.</summary>
        public readonly struct Entry
        {
            public readonly Role Role;
            public readonly int Slot;
            public readonly string Name;
            public readonly int Resources;

            public Entry(Role role, int slot, string name, int resources)
            {
                Role = role;
                Slot = slot;
                Name = name;
                Resources = resources;
            }
        }

        /// <summary>
        /// Every entry this faction's buildings can produce, in card order: role
        /// by role, and within a role entry 0 first. Only entries the production
        /// table actually prices — Produced, not Has, for the reason
        /// CommandCard.Building's own comment gives: 지옥불 Ranged entry 0 (자손)
        /// has a name and art but no price, because the warlord spawns it rather
        /// than a queue building it.
        /// </summary>
        public static List<Entry> For(Faction faction)
        {
            var entries = new List<Entry>();
            for (int i = 0; i < Producible.Length; i++)
            {
                Role role = Producible[i];
                for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                {
                    ProductionCost cost = FactionData.Production(faction, role, s);
                    if (!cost.Produced) continue;
                    entries.Add(new Entry(role, s, FactionData.Name(faction, role, s), cost.Resources));
                }
            }
            return entries;
        }
    }
}
