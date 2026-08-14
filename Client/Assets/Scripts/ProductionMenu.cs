using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Which roster entries a faction's buildings can produce, and the order the
    /// command card lists them in. Pulled out of CommandCard so the one thing
    /// about it worth checking without a graphics device — does every faction's
    /// list still fit the card — can run under plain dotnet
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
        /// Cells the command card gives production: rows 1 and 2 of its 3x3, six
        /// in total, with row 0 kept for Rally and Cancel (CommandCard.Building).
        /// A plain number here rather than read off CommandCard.Cols/Cells,
        /// because CLAUDE.md's layering — Sim and Net depend on nothing above
        /// them — extends the same way to this harness: Replay may not depend on
        /// a Unity-touching file, and CommandCard imports UnityEngine for its key
        /// table. 차원 유랑종 uses all six today (see docs/UI-STYLE.md 레이아웃 for
        /// the card's own geometry); a seventh entry on any faction's list would
        /// overflow it, which is exactly what ForOverflows below exists to catch
        /// before a person notices a missing button on screen.
        /// </summary>
        public const int Cells = 6;

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
