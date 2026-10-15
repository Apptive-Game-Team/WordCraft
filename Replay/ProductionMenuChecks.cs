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
    /// Checks ProductionMenu, the part of the command card a graphics device
    /// cannot see coming. #114 asked whether every faction's roster fits the
    /// card's fixed production cells. #169 widened the card to eight cells and
    /// made a longer list a paged one, so the question is now whether the page
    /// count stays inside ProductionMenu.MaxPages and whether the paging
    /// arithmetic puts every entry on exactly one page. CommandCard itself —
    /// turning the list into CardSlots, keys, and IMGUI buttons — is Unity
    /// plumbing over this and stays untested here, same as Hits.cs and Alert.cs
    /// stay untested next to HitDetection and AlertWindow.
    /// </summary>
    internal static class ProductionMenuChecks
    {
        public static void Check()
        {
            NoFactionNeedsTooManyPages();
            DriftworldsIsTheTightFit();
            PageCountBoundaries();
            EveryEntryIsOnExactlyOnePage();
            MoreWrapsToTheFirstPage();
        }

        /// <summary>
        /// The rule #114 exists to keep true, in its paged form: a list may run
        /// past one page (seven entries and a More) and that is not a failure, but
        /// a list that needs more than ProductionMenu.MaxPages pages is. Checked
        /// the way nothing on a batch-run Unity pass can, so a faction that
        /// outgrows the card fails here on every commit rather than waiting for
        /// someone to notice a missing button on screen.
        /// </summary>
        private static void NoFactionNeedsTooManyPages()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int count = ProductionMenu.For(faction).Count;
                int pages = ProductionMenu.Pages(count);
                Check(pages <= ProductionMenu.MaxPages,
                    faction + " needs " + count + " production entries, which is " + pages +
                    " pages of " + ProductionMenu.PageSize + "; the card pages at most " + ProductionMenu.MaxPages);
            }
        }

        /// <summary>
        /// The premise the card was sized from: 차원 유랑종 fields three melee
        /// entries (two temporary extinct-slime summons beside its standing
        /// 화산편), two ranged, one signature. Pinned to an exact count, not just
        /// "fits", so a roster edit that quietly dropped one of its entries would
        /// fail a check here instead of just changing what the other checks were
        /// ever exercising. Six is under the eight cells, so it is one page and no
        /// More; the day it passes eight this fails and says the first page grew
        /// a More cell.
        /// </summary>
        private static void DriftworldsIsTheTightFit()
        {
            int count = ProductionMenu.For(Faction.Driftworlds).Count;
            Check(count == 6,
                "차원 유랑종 fields " + count + " producible entries, expected exactly 6 — " +
                "the card was sized for this faction specifically");
            Check(ProductionMenu.Pages(count) == 1,
                "차원 유랑종's " + count + " entries should fit one page of " + ProductionMenu.Cells + " cells");
        }

        /// <summary>
        /// The edges of the page rule. Eight entries fill the eight cells, so there
        /// is no More; the ninth is the first to need one, and then each page holds
        /// seven. Four pages top out at 28, and 29 is the first overflow.
        /// </summary>
        private static void PageCountBoundaries()
        {
            ExpectPages(0, 1);
            ExpectPages(1, 1);
            ExpectPages(ProductionMenu.Cells, 1);
            ExpectPages(ProductionMenu.Cells + 1, 2);
            ExpectPages(2 * ProductionMenu.PageSize, 2);
            ExpectPages(2 * ProductionMenu.PageSize + 1, 3);
            ExpectPages(ProductionMenu.MaxPages * ProductionMenu.PageSize, ProductionMenu.MaxPages);
            ExpectPages(ProductionMenu.MaxPages * ProductionMenu.PageSize + 1, ProductionMenu.MaxPages + 1);
        }

        /// <summary>
        /// Paging may not lose or repeat a button: for every list length up to the
        /// overflow point, walking pages 0..Pages-1 visits entries 0..count-1 in
        /// order, once each, and no page holds more than a page's worth when there
        /// is a More to leave room for. A page number past the end clamps to the
        /// last page, the case a stale Orders.Page after a selection change hits.
        /// </summary>
        private static void EveryEntryIsOnExactlyOnePage()
        {
            for (int count = 0; count <= ProductionMenu.MaxPages * ProductionMenu.PageSize + 1; count++)
            {
                int pages = ProductionMenu.Pages(count);
                int expected = 0;
                for (int page = 0; page < pages; page++)
                {
                    int drawn = ProductionMenu.PageRange(count, page, out int first, out int length);
                    Check(drawn == page,
                        count + " entries: page " + page + " came back as page " + drawn);
                    Check(first == expected,
                        count + " entries: page " + page + " starts at " + first + ", expected " + expected);
                    int room = pages > 1 ? ProductionMenu.PageSize : ProductionMenu.Cells;
                    Check(length >= 0 && length <= room,
                        count + " entries: page " + page + " holds " + length + ", room for " + room);
                    expected += length;
                }
                Check(expected == count, count + " entries: the pages cover " + expected);

                int past = ProductionMenu.PageRange(count, pages + 5, out int lastFirst, out int lastLength);
                int want = ProductionMenu.PageRange(count, pages - 1, out int wantFirst, out int wantLength);
                Check(past == want && lastFirst == wantFirst && lastLength == wantLength,
                    count + " entries: a page past the end did not clamp to the last page");
                int below = ProductionMenu.PageRange(count, -3, out int firstFirst, out _);
                Check(below == 0 && firstFirst == 0,
                    count + " entries: a negative page did not clamp to the first page");
            }
        }

        /// <summary>
        /// What More does: the next page, and from the last page the first. A single
        /// page has nowhere to turn to, and a stale page past the end still lands on
        /// a page that exists.
        /// </summary>
        private static void MoreWrapsToTheFirstPage()
        {
            Check(ProductionMenu.NextPage(0, 1) == 0, "one page: More should stay on page 0");
            Check(ProductionMenu.NextPage(0, 3) == 1, "page 0 of 3: More should go to page 1");
            Check(ProductionMenu.NextPage(1, 3) == 2, "page 1 of 3: More should go to page 2");
            Check(ProductionMenu.NextPage(2, 3) == 0, "last page of 3: More should wrap to page 0");
            Check(ProductionMenu.NextPage(9, 3) == 0, "stale page 9 of 3: More should land on page 0");
        }

        private static void ExpectPages(int count, int pages)
        {
            int got = ProductionMenu.Pages(count);
            Check(got == pages, count + " entries should take " + pages + " pages, got " + got);
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
