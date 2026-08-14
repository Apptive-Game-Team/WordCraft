using System;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track B's write scope is Client/Assets/Scripts/ (.plan/general/2026-08-11-parallel-milestones.md).
    /// Replay/Program.cs belongs to track A's checks, so this file holds the one
    /// issue #114 adds and Program.cs only carries the single line that calls
    /// Check() — the same split AlertChecks.cs set up for #96.
    ///
    /// Checks ProductionMenu.For, the one decision #114 made that a graphics
    /// device cannot see coming: whether every faction's roster still fits the
    /// command card's fixed six production cells. CommandCard itself — turning
    /// the list into CardSlots, keys, and IMGUI buttons — is Unity plumbing over
    /// this and stays untested here, same as Hits.cs and Alert.cs stay untested
    /// next to HitDetection and AlertWindow.
    /// </summary>
    internal static class ProductionMenuChecks
    {
        public static void Check()
        {
            NoFactionOverflowsTheCard();
            DriftworldsIsTheTightFit();
        }

        /// <summary>
        /// The rule #114 exists to keep true, checked the way nothing on a
        /// batch-run Unity pass can: card capacity is a fixed number
        /// (ProductionMenu.Cells) and the roster grows independently of it, so a
        /// faction that outgrows the card fails here on every commit rather than
        /// waiting for someone to notice a missing button on screen.
        /// </summary>
        private static void NoFactionOverflowsTheCard()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int count = ProductionMenu.For(faction).Count;
                Check(count <= ProductionMenu.Cells,
                    faction + " needs " + count + " production cells, the card only has " + ProductionMenu.Cells);
            }
        }

        /// <summary>
        /// The premise behind ProductionMenu.Cells being six rather than three:
        /// 차원 유랑종 fields three melee entries (two temporary extinct-slime
        /// summons beside its standing 화산편), two ranged, one signature. Pinned
        /// to an exact count, not just "at or under the limit", so a roster edit
        /// that quietly dropped one of its entries would fail a check here
        /// instead of just changing what NoFactionOverflowsTheCard was ever
        /// exercising.
        /// </summary>
        private static void DriftworldsIsTheTightFit()
        {
            int count = ProductionMenu.For(Faction.Driftworlds).Count;
            Check(count == ProductionMenu.Cells,
                "차원 유랑종 fields " + count + " producible entries, expected exactly " + ProductionMenu.Cells +
                " — the card's six production cells were sized for this faction specifically");
        }

        // ponytail: a button's name is not checked here. 인간's melee row has no
        // name and no art (docs/FACTIONS.md by design) but Economy.TryQueueUnit
        // does not test Has, so it is still Produced and still lands a nameless,
        // priced cell on the card (PR #110's own "남은 것" note — the AI's
        // fallback has to land first). That is Sim's gap to close, not this
        // card's; a check here would fail on a faction the roster has always
        // shipped this way, for a reason #114 did not create and is not scoped
        // to fix.

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
