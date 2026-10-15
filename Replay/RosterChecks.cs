using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #180's checks: the rows docs/TECH-TREE.md 묶음 3 adds. Kept out of
    /// Program.cs the way the other per-issue files are.
    ///
    /// Every new row is written out here column by column from the document's
    /// unit and building tables and compared with FactionData, because a check
    /// that read the table back against itself would pin nothing: name, art
    /// name, slot, tier, prerequisite, how it is obtained, price and clock, the
    /// stats, the producers. Then the roster-wide rules the rows have to keep:
    /// every walker has a footprint, every weapon reaches something, everything
    /// obtainable is priced and timed, the prerequisites form no loop, and each
    /// faction fields twelve units and nine buildings. Then the research rows
    /// and what each one moves, the two 묶음 1 stand-ins being gone, and the AI
    /// putting at least one new row on the field in a solo match.
    /// </summary>
    internal static class RosterChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            EveryNewRowIsInTheTable();
            EveryWalkingRowHasAFootprint();
            EveryWeaponReachesSomething();
            EveryObtainableRowIsPricedAndTimed();
            PrerequisitesAreFieldedBuildingsWithNoLoop();
            EveryFactionFieldsTwelveUnitsAndNineBuildings();
            TheStandInsAreGone();
            TheResearchRowsAreTheDocumentedOnes();
            EachResearchMovesOnlyItsEntry();
            FleeSpeedIsReadOnlyWhileFleeing();
            AStandingBodyNeverChases();
            AResearchedReachAcquiresAtThatReach();
            EveryFlyingRowFlies();
            TheAiPutsANewRowOnTheField();
            NoSoloAiSitsAtThePopulationCap();
        }

        // ---------------------------------------------------------------------
        // The rows

        /// <summary>How the document says a unit row is obtained in this batch.</summary>
        private enum Got
        {
            /// <summary>On a building's production list.</summary>
            Produced,
            /// <summary>Grown into, from a 풀씨 정령 or (M6, #197) from another unit.</summary>
            Grown,
            /// <summary>
            /// On no list: spawned by 산란 악마 (M8) or woken by 이끼 현자 (깨우기).
            /// The row exists and Has answers yes; MechanicsChecks covers the spawn.
            /// </summary>
            Neither,
        }

        private struct Row
        {
            public bool Unit;
            public Faction Faction;
            public Role Role;
            public int Slot;
            public string Name, Sprite;
            public int Tier;
            public (Role Role, int Slot) Needs;
            public Got Got;
            public int Resources, Ticks;
            public int Hp, Armor, Damage, Range, AttackTicks, SpeedNum, SpeedDen;
            public bool Air;
            public string Reach;
            public (Role Role, int Slot) First, Second;
        }

        /// <summary>
        /// One unit row: 단계, 선행, 자원, 틱, 체력, 방어, 피해, 사거리, 주기, 속도,
        /// 이동 and 대공/대지 as the table gives them, 없음 written as 0 or "".
        /// First and Second are the 생산 buildings, Role.None where there is none.
        /// </summary>
        private static Row Unit(Faction faction, Role role, int slot, string name, string sprite, int tier,
            Role needsRole, int needsSlot, Got got, int resources, int ticks, int hp, int armor, int damage,
            int range, int attackTicks, int speedNum, int speedDen, bool air, string reach,
            Role firstRole, int firstSlot, Role secondRole, int secondSlot) =>
            new Row
            {
                Unit = true, Faction = faction, Role = role, Slot = slot, Name = name, Sprite = sprite,
                Tier = tier, Needs = (needsRole, needsSlot), Got = got, Resources = resources, Ticks = ticks,
                Hp = hp, Armor = armor, Damage = damage, Range = range, AttackTicks = attackTicks,
                SpeedNum = speedNum, SpeedDen = speedDen, Air = air, Reach = reach,
                First = (firstRole, firstSlot), Second = (secondRole, secondSlot),
            };

        /// <summary>
        /// One building row: 선행, 자원, 틱, 체력, and 피해, 사거리, 주기 and reach
        /// from the 기능 column for a building that shoots.
        /// </summary>
        private static Row Building(Faction faction, Role role, int slot, string name, string sprite, int tier,
            Role needsRole, int needsSlot, int resources, int ticks, int hp, int damage, int range,
            int attackTicks, string reach) =>
            new Row
            {
                Unit = false, Faction = faction, Role = role, Slot = slot, Name = name, Sprite = sprite,
                Tier = tier, Needs = (needsRole, needsSlot), Got = Got.Neither, Resources = resources,
                Ticks = ticks, Hp = hp, Damage = damage, Range = range, AttackTicks = attackTicks,
                SpeedDen = 1, Reach = reach, First = (Role.None, 0), Second = (Role.None, 0),
            };

        /// <summary>
        /// Every additional column of docs/TECH-TREE.md 슬롯 대조, in faction then
        /// role order, with its unit or building table row. The sprite is the
        /// English resource name .art/TECH-TREE-ART.md gives the entry, or the
        /// existing file name.
        /// </summary>
        private static readonly Row[] NewRows =
        {
            // 세계수 정령
            Building(Faction.TreeSpirits, Role.Production, 1, "씨앗 요람", "SeedCradle", 3, Role.Tech, 0, 150, 100, 300, 0, 0, 0, ""),
            Building(Faction.TreeSpirits, Role.Defense, 1, "회복 토템", "HealingTotem", 2, Role.Production, 0, 100, 60, 250, 0, 0, 0, ""),
            Unit(Faction.TreeSpirits, Role.Melee, 2, "찌릿쥐", "ZapMouse", 1, Role.None, 0, Got.Grown, 15, 20, 45, 0, 5, 2, 10, 3, 8, false, "지", Role.None, 0, Role.None, 0),
            Unit(Faction.TreeSpirits, Role.Melee, 3, "폭풍 기수", "StormRider", 2, Role.Tech, 2, Got.Grown, 60, 60, 160, 1, 11, 2, 15, 3, 8, false, "지", Role.None, 0, Role.None, 0),
            Unit(Faction.TreeSpirits, Role.Ranged, 2, "번개새 무리", "ThunderBirdSwarm", 3, Role.Tech, 1, Got.Grown, 60, 50, 120, 0, 4, 4, 10, 3, 8, true, "지·공", Role.None, 0, Role.None, 0),
            Unit(Faction.TreeSpirits, Role.Ranged, 3, "거대 덩쿨", "GiantVine", 3, Role.Tech, 0, Got.Grown, 100, 120, 350, 2, 20, 9, 40, 0, 1, false, "지", Role.None, 0, Role.None, 0),
            Unit(Faction.TreeSpirits, Role.Signature, 1, "도깨비불", "WillOWisp", 2, Role.Defense, 1, Got.Grown, 40, 50, 60, 0, 0, 0, 0, 3, 8, true, "", Role.None, 0, Role.None, 0),
            Unit(Faction.TreeSpirits, Role.Signature, 2, "덩굴 군집", "VineColony", 2, Role.Production, 0, Got.Grown, 45, 60, 150, 1, 0, 0, 0, 1, 8, false, "", Role.None, 0, Role.None, 0),
            Building(Faction.TreeSpirits, Role.Tech, 1, "바람 토템", "WindTotem", 3, Role.Tech, 0, 150, 100, 250, 0, 0, 0, ""),
            Building(Faction.TreeSpirits, Role.Tech, 2, "광란의 토템", "FrenzyTotem", 2, Role.Production, 0, 125, 80, 250, 0, 0, 0, ""),
            // 지옥불 군단
            Building(Faction.Hellfire, Role.Production, 1, "큰 균열", "GrandRift", 3, Role.Tech, 0, 250, 140, 450, 0, 0, 0, ""),
            Building(Faction.Hellfire, Role.Defense, 1, "집결의 횃불", "RallyingTorch", 2, Role.Production, 0, 90, 50, 200, 0, 0, 0, ""),
            Building(Faction.Hellfire, Role.Defense, 2, "화염 첨탑", "FlameSpire", 2, Role.Production, 0, 110, 70, 260, 12, 7, 20, "공"),
            Unit(Faction.Hellfire, Role.Melee, 1, "화염 악마", "FireSpirit", 1, Role.None, 0, Got.Produced, 50, 50, 140, 1, 9, 2, 15, 1, 4, false, "지", Role.Base, 0, Role.Production, 0),
            Unit(Faction.Hellfire, Role.Melee, 2, "균열 사냥개", "RiftHound", 2, Role.None, 0, Got.Produced, 60, 50, 110, 0, 8, 2, 12, 3, 8, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.Hellfire, Role.Melee, 3, "잿불 새끼", "EmberWhelp", 2, Role.None, 0, Got.Neither, 0, 0, 45, 0, 5, 2, 15, 1, 4, false, "지", Role.None, 0, Role.None, 0),
            Unit(Faction.Hellfire, Role.Ranged, 2, "용암 투석 악마", "LavaCatapultDemon", 3, Role.None, 0, Got.Produced, 150, 120, 200, 2, 16, 9, 45, 1, 6, false, "지", Role.Production, 1, Role.None, 0),
            Unit(Faction.Hellfire, Role.Ranged, 3, "재날개 악마", "AshWingDemon", 3, Role.None, 0, Got.Produced, 110, 90, 140, 1, 9, 6, 15, 3, 8, true, "공", Role.Production, 1, Role.None, 0),
            Unit(Faction.Hellfire, Role.Signature, 1, "산란 악마", "BroodDemon", 2, Role.Tech, 1, Got.Produced, 160, 120, 320, 2, 6, 2, 20, 1, 6, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.Hellfire, Role.Signature, 2, "운석 술사", "MeteorInvoker", 3, Role.None, 0, Got.Produced, 180, 130, 160, 1, 0, 0, 0, 1, 4, false, "", Role.Production, 1, Role.None, 0),
            Building(Faction.Hellfire, Role.Tech, 1, "재 분화구", "AshCaldera", 2, Role.Production, 0, 150, 100, 250, 0, 0, 0, ""),
            // 물 슬라임
            Building(Faction.WaterSlimes, Role.Production, 1, "구름 둥지", "CloudNest", 2, Role.Production, 0, 150, 100, 350, 0, 0, 0, ""),
            Building(Faction.WaterSlimes, Role.Defense, 1, "솟는 샘", "RisingSpring", 2, Role.Production, 0, 80, 50, 220, 0, 0, 0, ""),
            Building(Faction.WaterSlimes, Role.Defense, 2, "거품 장벽", "BubbleMound", 1, Role.None, 0, 50, 40, 500, 0, 0, 0, ""),
            Unit(Faction.WaterSlimes, Role.Melee, 1, "물막이 슬라임", "WaterScreenSlime", 2, Role.None, 0, Got.Produced, 70, 60, 240, 2, 5, 2, 18, 1, 5, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.WaterSlimes, Role.Melee, 2, "급류 슬라임", "RiptideSlime", 2, Role.None, 0, Got.Produced, 50, 40, 100, 0, 7, 2, 12, 5, 16, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.WaterSlimes, Role.Ranged, 2, "침 뱉는 슬라임", "WaterSlimeAttackSpit", 1, Role.None, 0, Got.Produced, 25, 25, 45, 0, 4, 4, 15, 1, 4, false, "지·공", Role.Base, 0, Role.Production, 0),
            Unit(Faction.WaterSlimes, Role.Ranged, 3, "물대포 슬라임", "WaterCannonSlime", 3, Role.Tech, 1, Got.Produced, 130, 110, 160, 1, 14, 8, 40, 1, 6, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.WaterSlimes, Role.Signature, 1, "비구름", "RainCloud", 2, Role.None, 0, Got.Produced, 75, 60, 90, 0, 0, 0, 0, 1, 4, true, "", Role.Production, 1, Role.None, 0),
            Unit(Faction.WaterSlimes, Role.Signature, 2, "간헐천 술사", "GeyserMage", 3, Role.Tech, 0, Got.Produced, 120, 100, 100, 0, 0, 0, 0, 1, 4, false, "", Role.Production, 0, Role.None, 0),
            Unit(Faction.WaterSlimes, Role.Signature, 3, "샘물 운반자", "SpringCarrier", 2, Role.None, 0, Got.Produced, 80, 70, 140, 1, 0, 0, 0, 1, 5, false, "", Role.Production, 0, Role.None, 0),
            Building(Faction.WaterSlimes, Role.Tech, 1, "물결 수련장", "WaveTrainingPool", 2, Role.Production, 0, 150, 100, 250, 0, 0, 0, ""),
            // 돌 골렘 부족
            Building(Faction.RockGolems, Role.Production, 1, "각성의 원", "AwakeningCircle", 3, Role.Tech, 0, 200, 120, 500, 0, 0, 0, ""),
            Building(Faction.RockGolems, Role.Defense, 1, "부유석 첨탑", "HoverstoneSpire", 2, Role.Production, 0, 110, 70, 320, 12, 7, 20, "공"),
            Building(Faction.RockGolems, Role.Defense, 2, "잠듦의 고리", "DormancyRing", 3, Role.Tech, 0, 120, 80, 300, 0, 0, 0, ""),
            Unit(Faction.RockGolems, Role.Melee, 1, "구르는 바위", "RockRolling", 2, Role.None, 0, Got.Produced, 75, 60, 180, 2, 10, 2, 20, 1, 4, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.RockGolems, Role.Melee, 2, "성벽 골렘", "WallGolem", 2, Role.None, 0, Got.Produced, 60, 50, 200, 2, 6, 2, 20, 1, 6, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.RockGolems, Role.Melee, 3, "깨어난 바위", "AwakenedStone", 3, Role.None, 0, Got.Neither, 0, 0, 150, 1, 8, 2, 15, 1, 5, false, "지", Role.None, 0, Role.None, 0),
            Unit(Faction.RockGolems, Role.Ranged, 1, "돌팔매 꼬마돌", "PebbleSlinger", 1, Role.None, 0, Got.Produced, 35, 35, 70, 0, 5, 5, 18, 1, 4, false, "지·공", Role.Base, 0, Role.Production, 0),
            Unit(Faction.RockGolems, Role.Ranged, 2, "부유석 파수", "FloatingStoneSentinel", 3, Role.Tech, 1, Got.Produced, 140, 120, 220, 2, 10, 6, 22, 1, 6, true, "지·공", Role.Production, 1, Role.None, 0),
            Unit(Faction.RockGolems, Role.Ranged, 3, "모래 술사", "SandShaper", 3, Role.None, 0, Got.Produced, 130, 110, 110, 0, 5, 5, 20, 1, 4, false, "지·공", Role.Production, 1, Role.None, 0),
            Unit(Faction.RockGolems, Role.Signature, 1, "이끼 현자", "MossSage", 3, Role.None, 0, Got.Produced, 150, 120, 140, 1, 0, 0, 0, 1, 5, false, "", Role.Production, 1, Role.None, 0),
            Unit(Faction.RockGolems, Role.Signature, 2, "이끼 포자 무리", "MossSporeCluster", 2, Role.Tech, 1, Got.Produced, 70, 60, 80, 0, 0, 0, 0, 1, 4, true, "", Role.Production, 0, Role.None, 0),
            Building(Faction.RockGolems, Role.Tech, 1, "이끼 정원", "MossGarden", 2, Role.Production, 0, 150, 100, 250, 0, 0, 0, ""),
            // 차원 유랑종
            Building(Faction.Driftworlds, Role.Production, 1, "깊은 통로", "DeepPassage", 3, Role.Tech, 0, 220, 130, 400, 0, 0, 0, ""),
            Building(Faction.Driftworlds, Role.Defense, 1, "위상 닻", "PhaseAnchor", 2, Role.Production, 0, 40, 30, 80, 0, 0, 0, ""),
            Building(Faction.Driftworlds, Role.Defense, 2, "틈새 포대", "RiftBattery", 2, Role.Production, 0, 120, 70, 260, 12, 7, 20, "지·공"),
            Unit(Faction.Driftworlds, Role.Ranged, 2, "덩쿨편", "VineShard", 2, Role.None, 0, Got.Produced, 55, 50, 80, 0, 6, 5, 18, 1, 4, false, "지·공", Role.Production, 0, Role.None, 0),
            Unit(Faction.Driftworlds, Role.Ranged, 3, "멸종한 바람 슬라임", "ExtinctWindSlime", 3, Role.Tech, 0, Got.Produced, 45, 30, 90, 0, 7, 5, 15, 3, 8, true, "지·공", Role.Production, 0, Role.None, 0),
            Unit(Faction.Driftworlds, Role.Signature, 1, "떠도는 섬", "WanderingIsland", 3, Role.None, 0, Got.Produced, 200, 140, 260, 2, 0, 0, 0, 1, 6, true, "", Role.Production, 1, Role.None, 0),
            Unit(Faction.Driftworlds, Role.Signature, 2, "층위 관측자", "StrataObserver", 3, Role.None, 0, Got.Produced, 120, 100, 110, 0, 0, 0, 0, 1, 4, false, "", Role.Production, 1, Role.None, 0),
            Unit(Faction.Driftworlds, Role.Signature, 3, "멸종한 자연 슬라임", "ExtinctNatureSlime", 3, Role.Tech, 0, Got.Produced, 40, 30, 100, 0, 0, 0, 0, 1, 4, false, "", Role.Production, 0, Role.None, 0),
            Building(Faction.Driftworlds, Role.Tech, 1, "외피 연마장", "ShellPolishingYard", 2, Role.Production, 0, 150, 100, 250, 0, 0, 0, ""),
            // 인간 마법 문명
            Building(Faction.Humans, Role.Production, 1, "석조 병영", "StoneBarracks", 1, Role.None, 0, 120, 80, 400, 0, 0, 0, ""),
            Building(Faction.Humans, Role.Production, 2, "마도 공창", "ArcaneFoundry", 2, Role.Production, 0, 180, 120, 450, 0, 0, 0, ""),
            Unit(Faction.Humans, Role.Ranged, 1, "석궁 사수", "CrossbowMarksman", 1, Role.None, 0, Got.Produced, 40, 35, 70, 0, 6, 5, 18, 1, 4, false, "지", Role.Base, 0, Role.Production, 1),
            Unit(Faction.Humans, Role.Ranged, 2, "마도사", "ArcaneMage", 2, Role.Tech, 1, Got.Produced, 90, 70, 80, 0, 8, 6, 22, 1, 4, false, "지", Role.Production, 0, Role.None, 0),
            Unit(Faction.Humans, Role.Ranged, 3, "석조 보행 포대", "StoneWalkerBattery", 2, Role.None, 0, Got.Produced, 120, 90, 260, 3, 11, 6, 22, 1, 6, false, "지", Role.Production, 2, Role.None, 0),
            Unit(Faction.Humans, Role.Ranged, 4, "견인 대포", "TowedCannon", 3, Role.None, 0, Got.Produced, 150, 120, 180, 1, 15, 7, 40, 1, 6, false, "지", Role.Production, 2, Role.None, 0),
            Unit(Faction.Humans, Role.Ranged, 5, "마도 폭격선", "ArcaneBomber", 3, Role.None, 0, Got.Produced, 160, 120, 200, 1, 14, 4, 40, 1, 4, true, "지", Role.Production, 2, Role.None, 0),
            Unit(Faction.Humans, Role.Ranged, 6, "마도 기수", "ArcaneRider", 2, Role.None, 0, Got.Produced, 70, 55, 110, 1, 6, 4, 14, 3, 8, false, "지", Role.Production, 1, Role.None, 0),
            Unit(Faction.Humans, Role.Signature, 2, "공방 기술자", "WorkshopEngineer", 2, Role.None, 0, Got.Produced, 60, 50, 80, 0, 0, 0, 0, 1, 4, false, "", Role.Production, 0, Role.None, 0),
            Unit(Faction.Humans, Role.Signature, 3, "마나 증폭기", "ManaAmplifier", 3, Role.None, 0, Got.Produced, 110, 90, 150, 1, 0, 0, 0, 1, 5, false, "", Role.Production, 2, Role.None, 0),
            Building(Faction.Humans, Role.Tech, 1, "마도 학당", "ArcaneAcademy", 2, Role.Production, 0, 150, 100, 250, 0, 0, 0, ""),
            Building(Faction.Humans, Role.Tech, 2, "결속대", "BindingPlatform", 2, Role.Production, 0, 100, 80, 250, 0, 0, 0, ""),
        };

        /// <summary>True when docs/TECH-TREE.md 묶음 3 added this entry.</summary>
        public static bool IsNewRow(Faction faction, Role role, int slot) => Find(faction, role, slot) >= 0;

        /// <summary>
        /// True when the document gives this new entry no weapon: 피해 없음 in a
        /// unit table, 무기 없음 in a building table. Program.cs's rule that only
        /// a named list of entries is unarmed reads this, so the list is the
        /// document's and not the roster's.
        /// </summary>
        public static bool IsDocumentedUnarmed(Faction faction, Role role, int slot)
        {
            int i = Find(faction, role, slot);
            return i >= 0 && Unarmed(NewRows[i]);
        }

        /// <summary>
        /// A unit or a defense building with no weapon. Production and Tech
        /// buildings never carry one and are not what the word means.
        /// </summary>
        private static bool Unarmed(Row row) => row.Damage == 0 && (row.Unit || row.Role == Role.Defense);

        /// <summary>How many new rows the document leaves unarmed: 13 units and 6 defense buildings.</summary>
        public const int DocumentedUnarmedCount = 19;

        private static int Find(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < NewRows.Length; i++)
            {
                if (NewRows[i].Faction == faction && NewRows[i].Role == role && NewRows[i].Slot == slot) return i;
            }
            return -1;
        }

        private static void EveryNewRowIsInTheTable()
        {
            Check(NewRows.Length == 65, "the table above holds " + NewRows.Length + " rows, not the 65 슬롯 대조 adds");
            int unarmed = 0;
            foreach (Row row in NewRows)
            {
                Faction f = row.Faction;
                string where = f + "." + row.Role + "[" + row.Slot + "] " + row.Name;
                Check(FactionData.Has(f, row.Role, row.Slot), where + " is not fielded");
                Check(FactionData.Name(f, row.Role, row.Slot) == row.Name,
                    where + " is named " + FactionData.Name(f, row.Role, row.Slot));
                Check(FactionData.Sprite(f, row.Role, row.Slot) == row.Sprite,
                    where + " has art " + FactionData.Sprite(f, row.Role, row.Slot) + ", not " + row.Sprite);
                Check(FactionData.Tier(f, row.Role, row.Slot) == row.Tier,
                    where + " opens at tier " + FactionData.Tier(f, row.Role, row.Slot) + ", not " + row.Tier);
                var needs = FactionData.Prerequisite(f, row.Role, row.Slot);
                Check(needs.Role == row.Needs.Role && (needs.Role == Role.None || needs.Slot == row.Needs.Slot),
                    where + " needs " + needs.Role + " " + needs.Slot + ", not " + row.Needs.Role + " " + row.Needs.Slot);
                Check(FactionData.IsBuilding(row.Role) != row.Unit, where + " is in the wrong table");
                if (Unarmed(row)) unarmed++;

                UnitStats s = FactionData.Stats(f, row.Role, row.Slot);
                Check(s.Hp == row.Hp, where + " hp " + s.Hp + ", not " + row.Hp);
                Check(s.Armor == row.Armor, where + " armor " + s.Armor + ", not " + row.Armor);
                Check(s.Damage == row.Damage, where + " damage " + s.Damage + ", not " + row.Damage);
                Check(s.Range == Fix.FromInt(row.Range), where + " range " + s.Range.ToInt() + ", not " + row.Range);
                Check(s.AttackTicks == row.AttackTicks, where + " rate " + s.AttackTicks + ", not " + row.AttackTicks);
                Check(s.Speed == Fix.Ratio(row.SpeedNum, row.SpeedDen), where + " has the wrong speed");
                Check(s.Air == row.Air, where + " flies: " + s.Air + ", not " + row.Air);
                Check(s.HitsAir == row.Reach.Contains("공"), where + " has the wrong air reach");
                Check(s.HitsGround == row.Reach.Contains("지"), where + " has the wrong ground reach");

                if (!row.Unit)
                {
                    Check(FactionData.BuildCost(f, row.Role, row.Slot) == row.Resources,
                        where + " costs " + FactionData.BuildCost(f, row.Role, row.Slot) + ", not " + row.Resources);
                    Check(FactionData.BuildTicks(f, row.Role, row.Slot) == row.Ticks,
                        where + " takes " + FactionData.BuildTicks(f, row.Role, row.Slot) + " ticks, not " + row.Ticks);
                    // New buildings open nothing: only entry 0 of Production and
                    // Tech grants a tier, and every new building is entry 1 or more.
                    Check(FactionData.GrantsTier(f, row.Role, row.Slot) == 0, where + " opens a tier");
                    continue;
                }

                ProductionCost cost = FactionData.Production(f, row.Role, row.Slot);
                GrowthCost growth = FactionData.Growth(f, row.Role, row.Slot);
                Check(cost.Produced == (row.Got == Got.Produced), where + " produced: " + cost.Produced);
                Check(growth.Grown == (row.Got == Got.Grown), where + " grown: " + growth.Grown);
                if (row.Got == Got.Produced)
                {
                    Check(cost.Resources == row.Resources && cost.Ticks == row.Ticks,
                        where + " is priced " + cost.Resources + "/" + cost.Ticks + ", not " + row.Resources + "/" + row.Ticks);
                }
                if (row.Got == Got.Grown)
                {
                    Check(growth.Resources == row.Resources && growth.Ticks == row.Ticks,
                        where + " grows for " + growth.Resources + "/" + growth.Ticks + ", not " + row.Resources + "/" + row.Ticks);
                }
                var first = FactionData.ProducedAt(f, row.Role, row.Slot, 0);
                var second = FactionData.ProducedAt(f, row.Role, row.Slot, 1);
                Check(first == row.First && second == row.Second,
                    where + " comes out of " + first + " and " + second + ", not " + row.First + " and " + row.Second);
            }
            Check(unarmed == DocumentedUnarmedCount, unarmed + " new rows are unarmed, not " + DocumentedUnarmedCount);
        }

        // ---------------------------------------------------------------------
        // The rules every row keeps

        /// <summary>
        /// Every walker pushes and is pushed, per docs/TECH-TREE.md 읽는 법: a row
        /// with a speed has BodyRadius, and a row without one (buildings, 거대
        /// 덩쿨) has none. Every entry of every faction, not only the new ones.
        /// </summary>
        private static void EveryWalkingRowHasAFootprint()
        {
            ForEveryEntry((f, role, slot) =>
            {
                UnitStats s = FactionData.Stats(f, role, slot);
                string where = f + "." + role + "[" + slot + "] " + FactionData.Name(f, role, slot);
                if (s.Speed.Raw != 0) Check(s.Footprint == FactionData.BodyRadius, where + " walks with no footprint");
                else Check(s.Footprint.Raw == 0, where + " stands still with a footprint");
            });
        }

        /// <summary>An armed row that reaches neither layer fires at nothing.</summary>
        private static void EveryWeaponReachesSomething()
        {
            ForEveryEntry((f, role, slot) =>
            {
                UnitStats s = FactionData.Stats(f, role, slot);
                if (!s.HasWeapon) return;
                Check(s.HitsAir || s.HitsGround,
                    f + "." + role + "[" + slot + "] carries a weapon that reaches neither layer");
            });
        }

        /// <summary>
        /// Everything a player can obtain has a price and a clock: a produced
        /// entry and a growth target, and a building. A produced entry also comes
        /// out of at least one building, or it is on a list nothing serves.
        /// </summary>
        private static void EveryObtainableRowIsPricedAndTimed()
        {
            ForEveryEntry((f, role, slot) =>
            {
                string where = f + "." + role + "[" + slot + "] " + FactionData.Name(f, role, slot);
                if (FactionData.IsBuilding(role))
                {
                    Check(FactionData.BuildCost(f, role, slot) > 0, where + " is free to place");
                    Check(FactionData.BuildTicks(f, role, slot) > 0, where + " stands up at once");
                    return;
                }
                ProductionCost cost = FactionData.Production(f, role, slot);
                if (cost.Produced)
                {
                    Check(cost.Resources > 0 && cost.Ticks > 0, where + " is free or instant to produce");
                    Check(FactionData.ProducedAt(f, role, slot, 0).Role != Role.None, where + " comes out of no building");
                }
                GrowthCost growth = FactionData.Growth(f, role, slot);
                if (growth.Grown) Check(growth.Resources > 0 && growth.Ticks > 0, where + " is free or instant to grow");
            });
        }

        /// <summary>
        /// Every prerequisite names a building the faction fields, and following
        /// prerequisites from any entry never comes back to where it started.
        /// The chain is short — the longest is a 바람 정령 behind a 바람 토템
        /// behind a 뿌리 회당 behind a 풀씨 둥지 — so a walk of RoleCount times
        /// the widest slot is a loop.
        /// </summary>
        private static void PrerequisitesAreFieldedBuildingsWithNoLoop()
        {
            ForEveryEntry((f, role, slot) =>
            {
                string where = f + "." + role + "[" + slot + "] " + FactionData.Name(f, role, slot);
                var at = (Role: role, Slot: slot);
                var seen = new List<(Role, int)> { (role, slot) };
                for (int step = 0; step < 64; step++)
                {
                    var needs = FactionData.Prerequisite(f, at.Role, at.Slot);
                    if (needs.Role == Role.None) return;
                    Check(FactionData.Has(f, needs.Role, needs.Slot) && FactionData.IsBuilding(needs.Role),
                        where + " needs " + needs.Role + " " + needs.Slot + ", which is no fielded building");
                    Check(!seen.Contains((needs.Role, needs.Slot)), where + "'s prerequisites loop at " + needs.Role + " " + needs.Slot);
                    seen.Add((needs.Role, needs.Slot));
                    at = (needs.Role, needs.Slot);
                }
                Check(false, where + "'s prerequisite chain never ends");
            });
        }

        /// <summary>
        /// StarCraft 한 종족 규모, per docs/TECH-TREE.md 규모 요약: twelve units
        /// and nine buildings for every faction, counted through Has. 인간 fields
        /// eleven building entries, the 마법 탑 twice.
        /// </summary>
        private static void EveryFactionFieldsTwelveUnitsAndNineBuildings()
        {
            int[] buildings = { 9, 9, 9, 9, 9, 11 };
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int units = 0, placed = 0;
                for (int r = 1; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        if (!FactionData.Has(faction, role, s)) continue;
                        if (FactionData.IsBuilding(role)) placed++;
                        else units++;
                    }
                }
                Check(units == 12, faction + " fields " + units + " units, not 12");
                Check(placed == buildings[f], faction + " fields " + placed + " buildings, not " + buildings[f]);
            }
        }

        /// <summary>
        /// 묶음 1 put two entries on stand-ins for buildings this batch adds, and
        /// left four second producers for later. All six now name the building the
        /// document gives them.
        /// </summary>
        private static void TheStandInsAreGone()
        {
            Check(FactionData.Prerequisite(Faction.TreeSpirits, Role.Signature, 0) == (Role.Tech, 1),
                "바람 정령 does not need the 바람 토템");
            Check(FactionData.Prerequisite(Faction.Humans, Role.Signature, 1).Role == Role.None,
                "마도 정찰기 still needs the 공방 stand-in");
            Producers(Faction.Humans, Role.Signature, 1, "마도 정찰기", (Role.Production, 2), FactionData.NoBuilding);
            Producers(Faction.Hellfire, Role.Signature, 0, "지옥불 군단장", (Role.Production, 0), (Role.Production, 1));
            Producers(Faction.WaterSlimes, Role.Signature, 0, "구름 용", (Role.Production, 0), (Role.Production, 1));
            Producers(Faction.RockGolems, Role.Signature, 0, "고대 골렘", (Role.Production, 0), (Role.Production, 1));
            Producers(Faction.Humans, Role.Ranged, 0, "공수 특공대", (Role.Production, 0), (Role.Production, 1));
        }

        private static void Producers(Faction f, Role role, int slot, string name,
            (Role, int) first, (Role, int) second)
        {
            Check(FactionData.Name(f, role, slot) == name, name + " is not at " + f + "." + role + "[" + slot + "]");
            Check(FactionData.ProducedAt(f, role, slot, 0) == first && FactionData.ProducedAt(f, role, slot, 1) == second,
                name + " comes out of " + FactionData.ProducedAt(f, role, slot, 0) + " and " +
                FactionData.ProducedAt(f, role, slot, 1));
        }

        // ---------------------------------------------------------------------
        // The research

        private struct ResearchRow
        {
            public Faction Faction;
            public string Name;
            public int Resources, Ticks, Tier;
            public UpgradeEffect Effect;
            public (Role Role, int Slot) At, Requires;
        }

        private static ResearchRow Research(Faction faction, string name, int resources, int ticks, int tier,
            UpgradeEffect effect, Role atRole, int atSlot, Role requiresRole, int requiresSlot) =>
            new ResearchRow
            {
                Faction = faction, Name = name, Resources = resources, Ticks = ticks, Tier = tier,
                Effect = effect, At = (atRole, atSlot), Requires = (requiresRole, requiresSlot),
            };

        /// <summary>
        /// docs/TECH-TREE.md 연구 행 배치 묶음 3 and 묶음 4 (#197), each row as its
        /// faction's 업그레이드 table gives it, in the order the faction's table
        /// appends them behind the four rows of #164.
        /// </summary>
        private static readonly ResearchRow[] NewResearch =
        {
            // 세계수 정령
            Research(Faction.TreeSpirits, "덩쿨 사거리", 100, 160, 3, UpgradeEffect.VineRange, Role.Tech, 0, Role.None, 0),
            Research(Faction.TreeSpirits, "거대 덩쿨 사거리", 150, 200, 3, UpgradeEffect.GiantVineRange, Role.Tech, 0, Role.None, 0),
            Research(Faction.TreeSpirits, "번개새 공격 주기", 100, 160, 3, UpgradeEffect.ThunderBirdAttackTicks, Role.Tech, 1, Role.None, 0),
            Research(Faction.TreeSpirits, "잎날 속도", 100, 160, 2, UpgradeEffect.LeafbladeSpeed, Role.Tech, 2, Role.None, 0),
            // 묶음 4 (#197): read off the free queue table, not an entry.
            Research(Faction.TreeSpirits, "씨앗 요람 반경", 100, 160, 3, UpgradeEffect.SeedCradleRadius, Role.Tech, 0, Role.Production, 1),
            // 지옥불 군단
            Research(Faction.Hellfire, "용암 투석 사거리", 150, 200, 3, UpgradeEffect.LavaCatapultRange, Role.Tech, 0, Role.None, 0),
            Research(Faction.Hellfire, "재날개 공격 주기", 100, 160, 3, UpgradeEffect.AshWingAttackTicks, Role.Tech, 0, Role.None, 0),
            Research(Faction.Hellfire, "갑각 경화", 150, 200, 3, UpgradeEffect.HardenedCarapace, Role.Tech, 1, Role.Tech, 0),
            // 묶음 4 (#197): read off the spawner table, not an entry.
            Research(Faction.Hellfire, "산란 상한", 100, 160, 2, UpgradeEffect.BroodCap, Role.Tech, 1, Role.None, 0),
            // 물 슬라임
            Research(Faction.WaterSlimes, "물결 궁수 사거리", 125, 200, 2, UpgradeEffect.ArcherRange, Role.Tech, 1, Role.None, 0),
            Research(Faction.WaterSlimes, "침 사거리", 75, 120, 2, UpgradeEffect.SpitterRange, Role.Tech, 1, Role.None, 0),
            Research(Faction.WaterSlimes, "물대포 사거리", 150, 200, 3, UpgradeEffect.WaterCannonRange, Role.Tech, 1, Role.None, 0),
            // 돌 골렘 부족
            Research(Faction.RockGolems, "돌팔매 사거리", 75, 120, 2, UpgradeEffect.SlingerRange, Role.Tech, 1, Role.None, 0),
            Research(Faction.RockGolems, "부유석 사거리", 150, 200, 3, UpgradeEffect.FloatingStoneRange, Role.Tech, 1, Role.None, 0),
            // 차원 유랑종
            Research(Faction.Driftworlds, "폭풍편 공격 주기", 100, 160, 2, UpgradeEffect.StormShardAttackTicks, Role.Tech, 1, Role.None, 0),
            Research(Faction.Driftworlds, "덩쿨편 사거리", 75, 120, 2, UpgradeEffect.VineShardRange, Role.Tech, 1, Role.None, 0),
            Research(Faction.Driftworlds, "틈새 포대 사거리", 100, 160, 2, UpgradeEffect.RiftBatteryRange, Role.Tech, 1, Role.None, 0),
            Research(Faction.Driftworlds, "도주 가속", 100, 160, 2, UpgradeEffect.FleeSpeed, Role.Tech, 1, Role.None, 0),
            // 묶음 4 (#197): read off the arrival table, not an entry.
            Research(Faction.Driftworlds, "위상 닻 반경", 100, 160, 2, UpgradeEffect.PhaseAnchorRadius, Role.Tech, 1, Role.None, 0),
            // 인간 마법 문명
            Research(Faction.Humans, "석궁 사거리", 75, 120, 3, UpgradeEffect.CrossbowRange, Role.Tech, 0, Role.None, 0),
            Research(Faction.Humans, "마도 기수 속도", 100, 160, 3, UpgradeEffect.ArcaneRiderSpeed, Role.Tech, 0, Role.None, 0),
            Research(Faction.Humans, "결속 강화", 125, 200, 2, UpgradeEffect.BindingRange, Role.Tech, 2, Role.None, 0),
            Research(Faction.Humans, "결속 갑주", 125, 200, 2, UpgradeEffect.BindingArmor, Role.Tech, 2, Role.None, 0),
            // 묶음 5 (#200): read off the ability table. Each faction's rows land
            // behind its own, in the order docs/TECH-TREE.md 연구 행 배치 lists them.
            Research(Faction.TreeSpirits, "휘감기 지속", 100, 160, 3, UpgradeEffect.EntangleDuration, Role.Tech, 0, Role.None, 0),
            Research(Faction.TreeSpirits, "연쇄 번개", 150, 200, 3, UpgradeEffect.ChainLightning, Role.Tech, 1, Role.None, 0),
            Research(Faction.Hellfire, "화염탄", 100, 160, 2, UpgradeEffect.FireShot, Role.Tech, 1, Role.None, 0),
            Research(Faction.Hellfire, "피 냄새", 100, 160, 2, UpgradeEffect.BloodScent, Role.Tech, 1, Role.None, 0),
            Research(Faction.Hellfire, "화상 오라 강화", 100, 160, 2, UpgradeEffect.BurnAuraDamage, Role.Tech, 1, Role.None, 0),
            Research(Faction.Hellfire, "집결 반경", 75, 120, 2, UpgradeEffect.RallyRadius, Role.Tech, 1, Role.Defense, 1),
            Research(Faction.WaterSlimes, "회복의 비 강화", 100, 160, 3, UpgradeEffect.HealingRainAmount, Role.Tech, 0, Role.None, 0),
            Research(Faction.WaterSlimes, "거품 벽", 100, 160, 2, UpgradeEffect.BubbleWall, Role.Tech, 1, Role.None, 0),
            Research(Faction.RockGolems, "이끼 재생 강화", 100, 160, 2, UpgradeEffect.MossRegrowthAmount, Role.Tech, 1, Role.None, 0),
            Research(Faction.Humans, "연쇄 방전", 125, 200, 2, UpgradeEffect.ChainDischarge, Role.Tech, 1, Role.None, 0),
            Research(Faction.Humans, "증폭 반경", 100, 160, 2, UpgradeEffect.AmplifyRadius, Role.Tech, 1, Role.None, 0),
            Research(Faction.Humans, "표식 강화", 100, 160, 2, UpgradeEffect.MarkAmount, Role.Tech, 1, Role.None, 0),
            // 묶음 6 (#202): summons that lapse and bursts.
            Research(Faction.Driftworlds, "소환 수명", 150, 200, 3, UpgradeEffect.SummonLifetime, Role.Tech, 0, Role.None, 0),
        };

        private static void TheResearchRowsAreTheDocumentedOnes()
        {
            int[] counts = { 11, 12, 9, 7, 10, 11 };
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                Check(FactionData.UpgradeCount(faction) == counts[f],
                    faction + " has " + FactionData.UpgradeCount(faction) + " research rows, not " + counts[f]);
                int index = 4;
                foreach (ResearchRow want in NewResearch)
                {
                    if (want.Faction != faction) continue;
                    UpgradeRow row = FactionData.Upgrade(faction, index);
                    string where = faction + " row " + index + " " + want.Name;
                    Check(row.Name == want.Name, where + " is named " + row.Name);
                    Check(row.Resources == want.Resources && row.Ticks == want.Ticks,
                        where + " costs " + row.Resources + "/" + row.Ticks);
                    Check(row.MaxLevel == 1, where + " has " + row.MaxLevel + " levels");
                    Check(row.Tier == want.Tier, where + " opens at tier " + row.Tier);
                    Check(row.Effect == want.Effect, where + " has effect " + row.Effect);
                    Check(row.ResearchAt == want.At, where + " is researched at " + row.ResearchAt);
                    Check(row.Requires == want.Requires, where + " requires " + row.Requires);
                    index++;
                }
                Check(index == counts[f], faction + " has rows the document does not");
            }
        }

        /// <summary>
        /// Which entry each research row moves and to what, from the 효과 column.
        /// Range, AttackTicks and Speed are the researched value; Armor is added.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, string Name, UpgradeEffect Effect, UpgradedStat Stat, Fix Value)[] Moves =
        {
            (Faction.TreeSpirits, Role.Ranged, 1, "덩쿨 정령", UpgradeEffect.VineRange, UpgradedStat.Range, Fix.FromInt(6)), // 사거리 5 → 6
            (Faction.TreeSpirits, Role.Ranged, 3, "거대 덩쿨", UpgradeEffect.GiantVineRange, UpgradedStat.Range, Fix.FromInt(11)), // 사거리 9 → 11
            (Faction.TreeSpirits, Role.Ranged, 2, "번개새 무리", UpgradeEffect.ThunderBirdAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(8)), // 주기 10 → 8틱
            (Faction.TreeSpirits, Role.Melee, 0, "잎날 정령", UpgradeEffect.LeafbladeSpeed, UpgradedStat.Speed, Fix.Ratio(5, 16)), // 속도 1/4 → 5/16
            (Faction.Hellfire, Role.Ranged, 2, "용암 투석 악마", UpgradeEffect.LavaCatapultRange, UpgradedStat.Range, Fix.FromInt(10)), // 사거리 9 → 10
            (Faction.Hellfire, Role.Ranged, 3, "재날개 악마", UpgradeEffect.AshWingAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(12)), // 주기 15 → 12틱
            (Faction.Hellfire, Role.Melee, 0, "용암 갑각 악마", UpgradeEffect.HardenedCarapace, UpgradedStat.Armor, Fix.FromInt(2)), // 방어 +2
            (Faction.Hellfire, Role.Signature, 1, "산란 악마", UpgradeEffect.HardenedCarapace, UpgradedStat.Armor, Fix.FromInt(2)), // 방어 +2
            (Faction.WaterSlimes, Role.Ranged, 0, "물결 궁수", UpgradeEffect.ArcherRange, UpgradedStat.Range, Fix.FromInt(7)), // 사거리 6 → 7
            (Faction.WaterSlimes, Role.Ranged, 2, "침 뱉는 슬라임", UpgradeEffect.SpitterRange, UpgradedStat.Range, Fix.FromInt(5)), // 사거리 4 → 5
            (Faction.WaterSlimes, Role.Ranged, 3, "물대포 슬라임", UpgradeEffect.WaterCannonRange, UpgradedStat.Range, Fix.FromInt(9)), // 사거리 8 → 9
            (Faction.RockGolems, Role.Ranged, 1, "돌팔매 꼬마돌", UpgradeEffect.SlingerRange, UpgradedStat.Range, Fix.FromInt(6)), // 사거리 5 → 6
            (Faction.RockGolems, Role.Ranged, 2, "부유석 파수", UpgradeEffect.FloatingStoneRange, UpgradedStat.Range, Fix.FromInt(7)), // 사거리 6 → 7
            (Faction.Driftworlds, Role.Melee, 0, "폭풍편", UpgradeEffect.StormShardAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(12)), // 주기 15 → 12틱
            (Faction.Driftworlds, Role.Ranged, 2, "덩쿨편", UpgradeEffect.VineShardRange, UpgradedStat.Range, Fix.FromInt(6)), // 사거리 5 → 6
            (Faction.Driftworlds, Role.Defense, 2, "틈새 포대", UpgradeEffect.RiftBatteryRange, UpgradedStat.Range, Fix.FromInt(8)), // 사거리 7 → 8
            (Faction.Driftworlds, Role.Signature, 0, "경계 운반자", UpgradeEffect.FleeSpeed, UpgradedStat.FleeSpeed, Fix.Ratio(1, 2)), // 도주 중 속도 3/8 → 1/2
            (Faction.Humans, Role.Ranged, 1, "석궁 사수", UpgradeEffect.CrossbowRange, UpgradedStat.Range, Fix.FromInt(6)), // 사거리 5 → 6
            (Faction.Humans, Role.Ranged, 6, "마도 기수", UpgradeEffect.ArcaneRiderSpeed, UpgradedStat.Speed, Fix.Ratio(1, 2)), // 속도 3/8 → 1/2
            (Faction.Humans, Role.Signature, 0, "Towerback", UpgradeEffect.BindingRange, UpgradedStat.Range, Fix.FromInt(8)), // 사거리 6 → 8
            (Faction.Humans, Role.Signature, 0, "Towerback", UpgradeEffect.BindingArmor, UpgradedStat.Armor, Fix.FromInt(2)), // 방어 +2
        };

        /// <summary>
        /// Every research row, researched in a world that has its building, its
        /// tier and its required building, moves exactly the number of exactly the
        /// entries the document names and nothing else on them. The bodies are
        /// spawned before the level lands, so the lookups are read off standing
        /// bodies rather than off a table: a level is not state on a body, and a
        /// body that was there before it is a body it has to reach.
        ///
        /// 도주 가속 moves a fleeing body only; its own check is below.
        /// </summary>
        private static void EachResearchMovesOnlyItsEntry()
        {
            Check(FactionData.EntryUpgradeCount == Moves.Length,
                "the entry upgrade table holds " + FactionData.EntryUpgradeCount + " rows, not " + Moves.Length);
            foreach (ResearchRow research in NewResearch)
            {
                Faction f = research.Faction;
                int index = IndexOf(f, research.Effect);
                World world = ResearchWorld(f, research, out int building);

                // Every new entry of the faction and every entry a row names, so
                // a row that reached one entry too many shows up on a neighbour.
                var entries = new List<(Role Role, int Slot, string Name)>();
                foreach (Row row in NewRows)
                {
                    if (row.Faction == f) entries.Add((row.Role, row.Slot, row.Name));
                }
                foreach (var move in Moves)
                {
                    if (move.Faction == f && !entries.Contains((move.Role, move.Slot, move.Name)))
                        entries.Add((move.Role, move.Slot, move.Name));
                }
                var bodies = new List<int>();
                var before = new List<int[]>();
                foreach (var entry in entries)
                {
                    int id = Spawn(world, entry.Role, entry.Slot, bodies.Count);
                    bodies.Add(id);
                    before.Add(Readings(world, world.GetEntity(id)));
                }

                ResearchToCompletion(world, building, index);

                for (int k = 0; k < entries.Count; k++)
                {
                    var entry = entries[k];
                    int[] now = Readings(world, world.GetEntity(bodies[k]));
                    int[] was = before[k];
                    for (int stat = 0; stat < 4; stat++)
                    {
                        // What this research names on this body's entry, which
                        // is not only one row: Towerback carries two.
                        var named = Named(f, entry.Role, entry.Slot, research.Effect, (UpgradedStat)(stat + 1));
                        string what = f + " " + research.Name + " on " + entry.Name + " " + (UpgradedStat)(stat + 1);
                        if (named.Stat == UpgradedStat.None)
                        {
                            Check(now[stat] == was[stat], what + " moved from " + was[stat] + " to " + now[stat]);
                            continue;
                        }
                        int want =
                            named.Stat == UpgradedStat.Armor ? was[stat] + named.Value.ToInt() :
                            named.Stat == UpgradedStat.AttackTicks ? named.Value.ToInt() :
                            (int)named.Value.Raw;
                        Check(now[stat] == want, what + " is " + now[stat] + ", not " + want);
                        Check(now[stat] != was[stat], what + " did not move");
                    }
                }
            }
        }

        /// <summary>
        /// 도주 가속: a fleeing 경계 운반자 walks at 1/2 once researched and at its
        /// own 3/8 before, and one that is not fleeing walks at 3/8 either way.
        /// </summary>
        private static void FleeSpeedIsReadOnlyWhileFleeing()
        {
            foreach (bool researched in new[] { false, true })
            {
                ResearchRow row = default;
                foreach (ResearchRow r in NewResearch)
                {
                    if (r.Effect == UpgradeEffect.FleeSpeed) row = r;
                }
                World world = ResearchWorld(Faction.Driftworlds, row, out int building);
                world.SetPeerFaction(1, Faction.Hellfire);
                if (researched) ResearchToCompletion(world, building, IndexOf(Faction.Driftworlds, UpgradeEffect.FleeSpeed));

                int carrier = world.SpawnUnit(0, World.CarrierRole, World.CarrierSlot, At(58, 30));
                Check(world.SpeedFor(world.GetEntity(carrier)) == Fix.Ratio(3, 8),
                    "a 경계 운반자 that is not fleeing walks at " + world.SpeedFor(world.GetEntity(carrier)).Raw);
                world.SpawnUnit(1, Role.Melee, At(63, 30));
                world.Step(Idle);
                Entity body = world.GetEntity(carrier);
                Check(body.Fleeing, "the 경계 운반자 did not flee the armed body beside it");
                Fix want = researched ? Fix.Ratio(1, 2) : Fix.Ratio(3, 8);
                Check(world.SpeedFor(body) == want,
                    "a fleeing 경계 운반자 walks at raw " + world.SpeedFor(body).Raw + " with 도주 가속 " +
                    (researched ? "researched" : "not researched"));
                Check(body.Speed == Fix.Ratio(3, 8), "도주 가속 rewrote the hashed speed");
            }
        }

        /// <summary>
        /// The row of <see cref="Moves"/> by which this research changes this
        /// number of this entry, or a row with Stat None when it changes nothing
        /// there. FleeSpeed is never named here: it moves a fleeing body only.
        /// </summary>
        private static (UpgradedStat Stat, Fix Value) Named(Faction f, Role role, int slot, UpgradeEffect effect, UpgradedStat stat)
        {
            foreach (var m in Moves)
            {
                if (m.Faction == f && m.Role == role && m.Slot == slot && m.Effect == effect && m.Stat == stat)
                    return (m.Stat, m.Value);
            }
            return (UpgradedStat.None, Fix.Zero);
        }

        /// <summary>
        /// 거대 덩쿨 has no speed. With a hostile inside the distance it acquires
        /// at (10) and outside its reach (9), it takes the target and does not
        /// chase: Target, which is hashed, and Position stay where they were.
        /// The chase branch's Speed test is what holds Target; without it the
        /// branch writes the hostile's position into Target every tick.
        /// </summary>
        private static void AStandingBodyNeverChases()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SetPeerFaction(1, Faction.Hellfire);
            int vine = world.SpawnUnit(0, Role.Ranged, 3, At(30, 30));
            int prey = world.SpawnBuilding(1, Role.Supply, At(40, 30), complete: true);
            Entity start = world.GetEntity(vine);
            Check(start.Speed.Raw == 0, "거대 덩쿨 walks, so this proves nothing");
            for (int t = 0; t < 30; t++) world.Step(Idle);
            Entity now = world.GetEntity(vine);
            Check(now.TargetId == prey, "거대 덩쿨 did not take the hostile ten cells away");
            Check(now.Target.Equals(start.Target), "거대 덩쿨's Target moved toward a hostile it cannot walk to");
            Check(now.Position.Equals(start.Position), "거대 덩쿨 moved");
            Check(world.GetEntity(prey).Hp == world.GetEntity(prey).MaxHp, "거대 덩쿨 hit something outside its reach");
        }

        /// <summary>
        /// A reach past AcquireRange is a reach the body acquires at. 거대 덩쿨
        /// with 거대 덩쿨 사거리 reaches 11, one past the 10 every body notices a
        /// hostile at; it takes and hits a building 11 cells away on its own. The
        /// control is the same world unresearched, where the building stands
        /// untouched and untaken.
        /// </summary>
        private static void AResearchedReachAcquiresAtThatReach()
        {
            ResearchRow row = default;
            foreach (ResearchRow r in NewResearch)
            {
                if (r.Effect == UpgradeEffect.GiantVineRange) row = r;
            }
            foreach (bool researched in new[] { false, true })
            {
                World world = ResearchWorld(Faction.TreeSpirits, row, out int building);
                if (researched) ResearchToCompletion(world, building, IndexOf(Faction.TreeSpirits, UpgradeEffect.GiantVineRange));
                int vine = world.SpawnUnit(0, Role.Ranged, 3, At(30, 30));
                int prey = world.SpawnBuilding(1, Role.Supply, At(41, 30), complete: true);
                for (int t = 0; t < 60; t++) world.Step(Idle);
                Entity v = world.GetEntity(vine);
                Entity p = world.GetEntity(prey);
                if (researched)
                {
                    Check(world.AcquisitionRange(v) == Fix.FromInt(11), "researched 거대 덩쿨 acquires at " + world.AcquisitionRange(v).ToInt());
                    Check(v.TargetId == prey, "researched 거대 덩쿨 did not take a building 11 cells away");
                    Check(p.Hp < p.MaxHp, "researched 거대 덩쿨 never hit a building 11 cells away");
                }
                else
                {
                    Check(v.TargetId != prey && p.Hp == p.MaxHp, "unresearched 거대 덩쿨 reached 11 cells");
                }
            }
        }

        /// <summary>
        /// Every row whose roster stats say it flies is a body the world flies:
        /// World.Flies reads the body's own entry, so 지옥불 자손 flies and 균열
        /// 파수병 behind it walks. Pins the data side of what the fog, movement
        /// and every reach rule ask through Flies.
        /// </summary>
        private static void EveryFlyingRowFlies()
        {
            int fliers = 0;
            ForEveryEntry((f, role, slot) =>
            {
                if (FactionData.IsBuilding(role)) return;
                var world = new World(Seed);
                world.SetPeerFaction(0, f);
                int id = role == Role.Worker ? world.SpawnWorker(0, At(30, 30)) : world.SpawnUnit(0, role, slot, At(30, 30));
                bool air = FactionData.Stats(f, role, slot).Air;
                Check(world.Flies(world.GetEntity(id)) == air,
                    f + "." + role + "[" + slot + "] " + FactionData.Name(f, role, slot) + " flies: " +
                    world.Flies(world.GetEntity(id)) + ", its row says " + air);
                if (air) fliers++;
            });
            // docs/TECH-TREE.md 규모 요약 counts 14 fliers. 마도 정찰기 is the
            // one it counts that still walks: its 공중 is a proposal the roster
            // has not taken.
            Check(fliers == 13, fliers + " roster entries fly, expected 13");
        }

        /// <summary>The four lookups, as raw numbers: range raw, rate, speed raw, armor.</summary>
        private static int[] Readings(World world, Entity e) =>
            new[] { (int)world.RangeFor(e).Raw, world.AttackTicksFor(e), (int)world.SpeedFor(e).Raw, world.ArmorFor(e) };

        private static int IndexOf(Faction faction, UpgradeEffect effect)
        {
            for (int i = 0; i < FactionData.UpgradeCount(faction); i++)
            {
                if (FactionData.Upgrade(faction, i).Effect == effect) return i;
            }
            throw new Exception(faction + " has no " + effect + " row");
        }

        /// <summary>
        /// Peer 0 of this faction with a deep bank, its production building and
        /// 기술 건물 finished so tier 3 is open, and the row's research building
        /// and required building finished too. Peer 1 owns nothing.
        /// </summary>
        private static World ResearchWorld(Faction faction, ResearchRow row, out int building)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, faction);
            world.SetPeerFaction(1, faction == Faction.Hellfire ? Faction.TreeSpirits : Faction.Hellfire);
            world.GrantResources(0, 100000);
            world.SpawnBuilding(0, Role.Production, 0, At(4, 4), complete: true);
            int tech = world.SpawnBuilding(0, Role.Tech, 0, At(4, 8), complete: true);
            building = row.At == (Role.Tech, 0)
                ? tech
                : world.SpawnBuilding(0, row.At.Role, row.At.Slot, At(4, 12), complete: true);
            Check(world.TierOf(0) == FactionData.MaxTier, faction + " research world is not at tier 3");
            return world;
        }

        private static int Spawn(World world, Role role, int slot, int n)
        {
            FixVec2 at = At(20 + (n % 8) * 3, 30 + (n / 8) * 3);
            return FactionData.IsBuilding(role)
                ? world.SpawnBuilding(0, role, slot, at, complete: true)
                : world.SpawnUnit(0, role, slot, at);
        }

        private static void ResearchToCompletion(World world, int building, int index)
        {
            Entity b = world.GetEntity(building);
            world.Step(new List<Command> { new Command(0, b.Owner, 900 + world.Tick, CommandType.Research, building, FixVec2.Zero, index) });
            Check(world.GetEntity(building).ResearchIndex == index,
                world.FactionOf(b.Owner) + " research of row " + index + " was refused");
            int ticks = FactionData.Upgrade(world.FactionOf(b.Owner), index).Ticks;
            for (int t = 0; t < ticks - 1; t++) world.Step(Idle);
            Check(world.UpgradeLevel(b.Owner, index) == 1, world.FactionOf(b.Owner) + " research of row " + index + " never landed");
        }

        // ---------------------------------------------------------------------
        // The AI

        /// <summary>
        /// A solo match per faction, the AI on peer 1 against an idle 세계수 정령:
        /// somewhere before it is decided the AI has put at least one new row on
        /// the field. Four factions do: 지옥불 화염 악마, 물 슬라임 침 뱉는
        /// 슬라임, 돌 골렘 돌팔매 꼬마돌 and 인간 석궁 사수, each the cheap tier-1
        /// body its base now makes. Two are left out on purpose. 세계수 정령's new
        /// bodies are all grown, and the ladder grows nothing. 차원 유랑종's ladder
        /// takes the first entry at its highest tier, which at tier 3 is 멸종한
        /// 화염 슬라임 ahead of every new row. Teaching the ladder either is
        /// docs/TECH-TREE.md 묶음 9.
        /// </summary>
        private static void TheAiPutsANewRowOnTheField()
        {
            foreach (Faction faction in new[] { Faction.Hellfire, Faction.WaterSlimes, Faction.RockGolems, Faction.Humans })
            {
                const int ai = 1;
                World world = MatchScenario.Build(Seed, Faction.TreeSpirits, faction);
                world.SetPeerAi(ai, true);
                string found = null;
                for (int t = 0; t < 6000 && !world.MatchOver && found == null; t++)
                {
                    world.Step(Idle);
                    for (int i = 0; i < world.EntityCount && found == null; i++)
                    {
                        Entity e = world.GetEntity(i);
                        if (e.Alive && e.Owner == ai && e.Kind == EntityKind.Unit && IsNewRow(faction, e.Role, e.Slot))
                            found = FactionData.Name(faction, e.Role, e.Slot);
                    }
                }
                Check(found != null, faction + "'s AI never fielded a new row in the solo match");
            }
        }

        /// <summary>
        /// The longest run of ticks a solo AI may stand at its population cap.
        /// A supply building takes 50 ticks to stand up, so one late order costs
        /// about that; twice it is a ladder that is not building supply at all.
        /// </summary>
        private const int MostTicksAtCap = 100;

        /// <summary>
        /// Every faction's AI, in a solo match against an idle opponent, never
        /// stands at its population cap for more than MostTicksAtCap ticks in a
        /// row within 1500 ticks. This pins the ladder's order: supply is decided
        /// before research and production, and while one is due nothing else is
        /// bought. Before that order, with 묶음 3's bases making fighters, 차원
        /// 유랑종's AI stood at 18/18 for 192 ticks and 돌 골렘's for 115.
        /// </summary>
        private static void NoSoloAiSitsAtThePopulationCap()
        {
            const int ai = 1;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                var idle = faction == Faction.TreeSpirits ? Faction.Hellfire : Faction.TreeSpirits;
                World world = MatchScenario.Build(Seed, idle, faction);
                world.SetPeerAi(ai, true);
                int run = 0, longest = 0;
                for (int t = 0; t < 1500 && !world.MatchOver; t++)
                {
                    world.Step(Idle);
                    run = world.GetPopulation(ai) >= world.PopulationCap(ai) ? run + 1 : 0;
                    if (run > longest) longest = run;
                }
                Check(longest <= MostTicksAtCap,
                    faction + "'s solo AI stood at its population cap for " + longest + " ticks in a row");
            }
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        private delegate void EntryVisitor(Faction faction, Role role, int slot);

        /// <summary>Every entry every faction fields, roles None excluded.</summary>
        private static void ForEveryEntry(EntryVisitor visit)
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 1; r < FactionData.RoleCount; r++)
                {
                    for (int s = 0; s < FactionData.SlotCount((Faction)f, (Role)r); s++)
                    {
                        if (FactionData.Has((Faction)f, (Role)r, s)) visit((Faction)f, (Role)r, s);
                    }
                }
            }
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
