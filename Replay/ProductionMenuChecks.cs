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
    /// device cannot see coming: whether what a building produces still fits
    /// the command card's fixed six production cells. Per building since
    /// docs/TECH-TREE.md 묶음 3 (#180), which gave every faction more entries
    /// than one card holds. CommandCard itself — turning
    /// the list into CardSlots, keys, and IMGUI buttons — is Unity plumbing over
    /// this and stays untested here, same as Hits.cs and Alert.cs stay untested
    /// next to HitDetection and AlertWindow.
    /// </summary>
    internal static class ProductionMenuChecks
    {
        public static void Check()
        {
            EveryBuildingListsWhatItMakes();
            OnlyThreeBuildingsOverflowTheCard();
            EveryNamedEntryHasABuilding();
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
        /// The rule #114 exists to keep true, in the form this base can keep: a
        /// building whose list runs past the six cells is one of exactly three,
        /// 물 슬라임 거품 생성기, 돌 골렘 각성 바위 and 차원 유랑종 통로. Their tail
        /// has no button until the paged card of #169 (PR #171) lands, and a
        /// fourth building joining them fails here rather than on screen.
        /// </summary>
        // ponytail: pinned rather than fixed. The six-cell card has no pages in
        // this base; #169 brings them, and its own check replaces this one.
        private static void OnlyThreeBuildingsOverflowTheCard()
        {
            int overflowing = 0;
            foreach (var row in DocumentedLists)
            {
                if (row.Count <= ProductionMenu.Cells) continue;
                overflowing++;
                Check(row.Role == Role.Production && row.Slot == 0 &&
                      (row.Faction == Faction.WaterSlimes || row.Faction == Faction.RockGolems ||
                       row.Faction == Faction.Driftworlds),
                    row.Faction + " " + row.Role + " " + row.Slot + " overflows the card and is not a known overflow");
            }
            Check(overflowing == 3, overflowing + " buildings overflow the card, expected the known 3");
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

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
