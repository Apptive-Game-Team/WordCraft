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
    /// arithmetic puts every entry on exactly one page. Since #180 the list is
    /// the selected building's, so both questions are asked per building
    /// entry. CommandCard itself —
    /// turning the list into CardSlots, keys, and IMGUI buttons — is Unity
    /// plumbing over this and stays untested here, same as Hits.cs and Alert.cs
    /// stay untested next to HitDetection and AlertWindow.
    /// </summary>
    internal static class ProductionMenuChecks
    {
        public static void Check()
        {
            EveryBuildingListsWhatItMakes();
            NoBuildingNeedsTooManyPages();
            TheLongListsPageRatherThanOverflow();
            EveryNamedEntryHasABuilding();
            PageCountBoundaries();
            EveryEntryIsOnExactlyOnePage();
            MoreWrapsToTheFirstPage();
        }

        /// <summary>
        /// How many card entries each producing building lists after
        /// docs/TECH-TREE.md 묶음 3, from the 생산 column of the document's
        /// building tables: Melee, Ranged and Signature entries only, because the
        /// card lists nothing else. Written out rather than read back, so a roster
        /// edit that moves an entry onto another building fails here.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, int Count)[] DocumentedLists =
        {
            (Faction.TreeSpirits, Role.Production, 0, 3),   // 풀씨 둥지: 잎날, 번개, 바람 정령
            (Faction.Hellfire, Role.Base, 0, 1),            // 균열 제단: 화염 악마
            (Faction.Hellfire, Role.Production, 0, 6),      // 악마 산란장
            (Faction.Hellfire, Role.Production, 1, 4),      // 큰 균열
            (Faction.WaterSlimes, Role.Base, 0, 2),         // 수맥 웅덩이: 거품 정령, 침 뱉는 슬라임
            (Faction.WaterSlimes, Role.Production, 0, 10),  // 거품 생성기
            (Faction.WaterSlimes, Role.Production, 1, 2),   // 구름 둥지: 구름 용, 비구름
            (Faction.RockGolems, Role.Base, 0, 2),          // 이끼바위 성소: 이끼바위 골렘, 돌팔매 꼬마돌
            (Faction.RockGolems, Role.Production, 0, 7),    // 각성 바위
            (Faction.RockGolems, Role.Production, 1, 4),    // 각성의 원
            (Faction.Driftworlds, Role.Base, 0, 1),         // 정박한 세계: 폭풍편
            (Faction.Driftworlds, Role.Production, 0, 9),   // 통로
            (Faction.Driftworlds, Role.Production, 1, 2),   // 깊은 통로: 떠도는 섬, 층위 관측자
            (Faction.Humans, Role.Base, 0, 1),              // 마나 샘: 석궁 사수
            (Faction.Humans, Role.Production, 0, 3),        // 마법 탑: 공수 특공대, 마도사, 공방 기술자
            (Faction.Humans, Role.Production, 1, 3),        // 석조 병영: 석궁 사수, 공수 특공대, 마도 기수
            (Faction.Humans, Role.Production, 2, 5),        // 마도 공창
        };

        /// <summary>
        /// Every building entry of every faction lists exactly the documented
        /// number of entries, and a building the table does not name lists none.
        /// </summary>
        private static void EveryBuildingListsWhatItMakes()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    if (!FactionData.IsBuilding(role)) continue;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        int expected = 0;
                        foreach (var row in DocumentedLists)
                        {
                            if (row.Faction == faction && row.Role == role && row.Slot == s) expected = row.Count;
                        }
                        int count = ProductionMenu.For(faction, role, s).Count;
                        Check(count == expected,
                            faction + " " + FactionData.Name(faction, role, s) + " lists " + count +
                            " production entries, expected " + expected);
                    }
                }
            }
        }

        /// <summary>
        /// The rule #114 exists to keep true, in its paged and per-building form:
        /// a list may run past one page (seven entries and a More) and that is not
        /// a failure, but a list that needs more than ProductionMenu.MaxPages
        /// pages is. Asked of every building entry of every faction, since the
        /// card lists what the selected building makes (#180).
        /// </summary>
        private static void NoBuildingNeedsTooManyPages()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    if (!FactionData.IsBuilding(role)) continue;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        int count = ProductionMenu.For(faction, role, s).Count;
                        int pages = ProductionMenu.Pages(count);
                        Check(pages <= ProductionMenu.MaxPages,
                            faction + " " + FactionData.Name(faction, role, s) + " needs " + count +
                            " production entries, which is " + pages + " pages of " + ProductionMenu.PageSize +
                            "; the card pages at most " + ProductionMenu.MaxPages);
                    }
                }
            }
        }

        /// <summary>
        /// The three longest lists docs/TECH-TREE.md 묶음 3 gives a building, and
        /// that each reaches every entry: 거품 생성기 (10) and 통로 (9) take two
        /// pages behind a More, 각성 바위 (7) fits the eight cells whole. Walking
        /// the pages has to visit every entry of the list once, in order.
        /// </summary>
        private static void TheLongListsPageRatherThanOverflow()
        {
            (Faction Faction, int Count, int Pages)[] longest =
            {
                (Faction.WaterSlimes, 10, 2),
                (Faction.Driftworlds, 9, 2),
                (Faction.RockGolems, 7, 1),
            };
            foreach (var (faction, expected, expectedPages) in longest)
            {
                int count = ProductionMenu.For(faction, Role.Production, 0).Count;
                string what = faction + " " + FactionData.Name(faction, Role.Production, 0);
                Check(count == expected, what + " lists " + count + " entries, expected " + expected);
                int pages = ProductionMenu.Pages(count);
                Check(pages == expectedPages, what + " takes " + pages + " pages, expected " + expectedPages);
                int reached = 0;
                for (int page = 0; page < pages; page++)
                {
                    ProductionMenu.PageRange(count, page, out int first, out int length);
                    Check(first == reached, what + " page " + page + " starts at " + first + ", expected " + reached);
                    reached += length;
                }
                Check(reached == count, what + ": the pages reach " + reached + " of " + count + " entries");
            }
        }

        /// <summary>
        /// Splitting the list per building may not lose an entry: every entry the
        /// faction-wide list holds that the roster names comes out of at least one
        /// building. 인간's nameless melee row is the one entry the faction-wide
        /// list holds and no building makes, which is the roster refusing it.
        /// </summary>
        private static void EveryNamedEntryHasABuilding()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                foreach (ProductionMenu.Entry entry in ProductionMenu.For(faction))
                {
                    if (!FactionData.Has(faction, entry.Role, entry.Slot)) continue;
                    bool listed = false;
                    for (int r = 0; r < FactionData.RoleCount && !listed; r++)
                    {
                        if (!FactionData.IsBuilding((Role)r)) continue;
                        for (int s = 0; s < FactionData.SlotCount(faction, (Role)r) && !listed; s++)
                        {
                            foreach (ProductionMenu.Entry e in ProductionMenu.For(faction, (Role)r, s))
                            {
                                if (e.Role == entry.Role && e.Slot == entry.Slot) listed = true;
                            }
                        }
                    }
                    Check(listed, faction + " " + entry.Name + " is on no building's production card");
                }
            }
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

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
