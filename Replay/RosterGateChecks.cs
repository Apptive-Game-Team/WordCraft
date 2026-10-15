using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #178's checks: the tech tree is asked of the roster entry and not of
    /// the role. Kept out of Program.cs the way the other per-issue files are.
    ///
    /// Five kinds of question. The tables: every existing entry opens at the
    /// tier docs/TECH-TREE.md gives it, every prerequisite, producer and research
    /// building names something the faction fields, and no prerequisite chain
    /// loops. The gates in play: two entries of one role open apart, a
    /// prerequisite refuses until it stands finished and refuses again once it
    /// falls, a building makes only what is on its list, a row is researched only
    /// in its building, and growth asks the same two questions production does.
    /// The predicates: 군단장 and 일제 사격 belong to entry 0. The AI: it orders
    /// only what a building makes, and builds the 기술 건물 to make what is behind
    /// it. And the scripted log: its fighters come out of the production building.
    /// </summary>
    internal static class RosterGateChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            EveryExistingEntryOpensAtItsDocumentedTier();
            OnlyEntryZeroOfProductionAndTechGrantsATier();
            EveryNamedBuildingIsFieldedAndNoChainLoops();
            TwoEntriesOfOneRoleOpenAtDifferentTiers();
            APrerequisiteGatesUntilItStandsAndAgainOnceItFalls();
            APrerequisiteRefusesWhereTheTierWouldAllow();
            AnUnfinishedPrerequisiteOpensNothing();
            ABuildingProducesOnlyWhatIsOnItsList();
            ResearchRunsOnlyInTheRowsBuilding();
            GrowthAsksTheTierAndThePrerequisite();
            OnlyEntryZeroOfTheSignatureSlotIsAWarlord();
            TheTideHeraldIsNoArcher();
            TheAiOrdersOnlyFromTheListAndBuildsTowardTierThree();
            ScriptedLogProducesAtTheProductionBuilding();
        }

        // ---------------------------------------------------------------------
        // The tables

        /// <summary>
        /// The 단계 column of docs/TECH-TREE.md for every entry the roster fields
        /// today, written out rather than read back off the table: a check that
        /// compares the table with itself pins nothing. The Towerback has no tier
        /// in the document — it is captured, never produced — and is left out.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, int Tier)[] DocumentedTiers =
        {
            (Faction.TreeSpirits, Role.Worker, 0, 1),
            (Faction.TreeSpirits, Role.Melee, 0, 2),
            (Faction.TreeSpirits, Role.Melee, 1, 2),
            (Faction.TreeSpirits, Role.Ranged, 0, 3),
            (Faction.TreeSpirits, Role.Ranged, 1, 1),
            (Faction.TreeSpirits, Role.Signature, 0, 3),

            (Faction.Hellfire, Role.Worker, 0, 1),
            (Faction.Hellfire, Role.Melee, 0, 2),
            (Faction.Hellfire, Role.Ranged, 0, 3),
            (Faction.Hellfire, Role.Ranged, 1, 2),
            (Faction.Hellfire, Role.Signature, 0, 3),

            (Faction.WaterSlimes, Role.Worker, 0, 1),
            (Faction.WaterSlimes, Role.Melee, 0, 1),
            (Faction.WaterSlimes, Role.Ranged, 0, 2),
            (Faction.WaterSlimes, Role.Ranged, 1, 3),
            (Faction.WaterSlimes, Role.Signature, 0, 3),

            (Faction.RockGolems, Role.Worker, 0, 1),
            (Faction.RockGolems, Role.Melee, 0, 1),
            (Faction.RockGolems, Role.Ranged, 0, 2),
            (Faction.RockGolems, Role.Signature, 0, 3),

            (Faction.Driftworlds, Role.Worker, 0, 1),
            (Faction.Driftworlds, Role.Melee, 0, 1),
            (Faction.Driftworlds, Role.Melee, 1, 3),
            (Faction.Driftworlds, Role.Melee, 2, 3),
            (Faction.Driftworlds, Role.Ranged, 0, 3),
            (Faction.Driftworlds, Role.Ranged, 1, 3),
            (Faction.Driftworlds, Role.Signature, 0, 2),

            (Faction.Humans, Role.Worker, 0, 1),
            (Faction.Humans, Role.Ranged, 0, 2),
            (Faction.Humans, Role.Signature, 1, 3),
        };

        /// <summary>
        /// Every documented tier, and every building at the tier its 선행 column
        /// implies: the 기술 건물 at 2 and everything else at 1.
        /// </summary>
        private static void EveryExistingEntryOpensAtItsDocumentedTier()
        {
            foreach (var (faction, role, slot, tier) in DocumentedTiers)
            {
                Check(FactionData.Has(faction, role, slot), Where(faction, role, slot) + " is not in the roster");
                Check(FactionData.Tier(faction, role, slot) == tier,
                    Where(faction, role, slot) + " opens at tier " + FactionData.Tier(faction, role, slot) +
                    ", docs/TECH-TREE.md says " + tier);
            }

            ForEveryEntry((faction, role, slot) =>
            {
                if (!FactionData.IsBuilding(role)) return;
                int expected = role == Role.Tech ? 2 : 1;
                Check(FactionData.Tier(faction, role, slot) == expected,
                    Where(faction, role, slot) + " opens at tier " + FactionData.Tier(faction, role, slot));
            });

            // An entry the faction does not field opens at no tier anybody reaches.
            Check(FactionData.Tier(Faction.Hellfire, Role.Ranged, 9) > FactionData.MaxTier,
                "an entry past the end of a list answers a reachable tier");
        }

        /// <summary>
        /// Entry 0 of the production slot grants 2, entry 0 of the Tech slot 3,
        /// and nothing else grants anything — the second research building
        /// docs/TECH-TREE.md adds is a Tech entry past the first, and this is the
        /// line it has to keep. Played as well as read: 인간's 전기 타워 and 돌
        /// 포탑 are entries past the first of a building slot and leave the tier
        /// where the base put it.
        /// </summary>
        private static void OnlyEntryZeroOfProductionAndTechGrantsATier()
        {
            ForEveryEntry((faction, role, slot) =>
            {
                int expected = slot == 0 && role == Role.Production ? 2
                    : slot == 0 && role == Role.Tech ? 3
                    : 0;
                Check(FactionData.GrantsTier(faction, role, slot) == expected,
                    Where(faction, role, slot) + " grants tier " + FactionData.GrantsTier(faction, role, slot));
            });
            // Past the end of the Tech list, which is where the second research
            // building will land: nothing there opens tier 3 by accident.
            Check(FactionData.GrantsTier(Faction.TreeSpirits, Role.Tech, 1) == 0,
                "an entry past the first of the Tech slot grants a tier");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Humans);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Defense, 1, At(9, 5), complete: true);
            world.SpawnBuilding(0, Role.Defense, 2, At(5, 9), complete: true);
            Check(world.TierOf(0) == 1, "인간's 전기 타워 and 돌 포탑 raised the tier to " + world.TierOf(0));
            world.SpawnBuilding(0, Role.Production, At(9, 9), complete: true);
            Check(world.TierOf(0) == 2, "the 마법 탑 did not open tier 2");
            world.SpawnBuilding(0, Role.Tech, At(12, 9), complete: true);
            Check(world.TierOf(0) == 3, "the 공방 did not open tier 3");
        }

        /// <summary>
        /// Every building any table names is a building the same faction fields:
        /// prerequisites, producers, research buildings and Requires. And no
        /// prerequisite chain comes back on itself: walked from every entry, each
        /// one reaches an entry that needs nothing within as many steps as there
        /// are building roles. A loop would be a pair of entries neither of which
        /// can ever be built.
        /// </summary>
        private static void EveryNamedBuildingIsFieldedAndNoChainLoops()
        {
            ForEveryEntry((faction, role, slot) =>
            {
                var at = (Role: role, Slot: slot);
                for (int step = 0; ; step++)
                {
                    var needs = FactionData.Prerequisite(faction, at.Role, at.Slot);
                    if (needs.Role == Role.None) break;
                    Check(FactionData.Has(faction, needs.Role, needs.Slot) && FactionData.IsBuilding(needs.Role),
                        Where(faction, at.Role, at.Slot) + " needs " + Where(faction, needs.Role, needs.Slot) +
                        ", which the faction does not field as a building");
                    Check(step < FactionData.RoleCount,
                        "the prerequisite chain from " + Where(faction, role, slot) + " loops");
                    at = needs;
                }

                bool produced = FactionData.Production(faction, role, slot).Produced;
                int producers = 0;
                for (int i = 0; i < FactionData.MaxProducers; i++)
                {
                    var by = FactionData.ProducedAt(faction, role, slot, i);
                    if (by.Role == Role.None) continue;
                    producers++;
                    Check(FactionData.Has(faction, by.Role, by.Slot) && FactionData.IsBuilding(by.Role),
                        Where(faction, role, slot) + " is produced at " + Where(faction, by.Role, by.Slot) +
                        ", which the faction does not field as a building");
                }
                Check(produced == (producers > 0),
                    Where(faction, role, slot) + (produced ? " is on the production list and no building makes it"
                        : " is off the production list and a building makes it"));
            });

            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int i = 0; i < FactionData.UpgradeCount(faction); i++)
                {
                    UpgradeRow row = FactionData.Upgrade(faction, i);
                    Check(row.ResearchAt.Role == Role.Tech && row.ResearchAt.Slot == 0,
                        faction + " row " + i + " is researched at " + Where(faction, row.ResearchAt.Role, row.ResearchAt.Slot) +
                        ", docs/TECH-TREE.md puts every existing row in the 기술 건물");
                    Check(row.Requires.Role == Role.None,
                        faction + " row " + i + " requires " + Where(faction, row.Requires.Role, row.Requires.Slot));
                }
            }
        }

        // ---------------------------------------------------------------------
        // The gates in play

        /// <summary>
        /// 차원 유랑종's melee slot opens 폭풍편 at tier 1 and the 멸종 슬라임 behind
        /// it at tier 3. At one 통로 with tier 2 standing, the first is taken and
        /// the second refused; the 층위 관측대 opens the second. Asked by role, the
        /// two would open together.
        /// </summary>
        private static void TwoEntriesOfOneRoleOpenAtDifferentTiers()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Driftworlds);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);                 // 0
            int passage = world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true); // 1
            world.GrantResources(0, 2000);

            int banked = world.GetResources(0);
            world.Step(Produce(passage, 0, 0, Role.Melee, 1));
            Check(world.GetResources(0) == banked, "멸종한 화염 슬라임 was taken at tier 2");
            world.Step(Produce(passage, 0, 1, Role.Melee, 0));
            Check(world.GetEntity(passage).QueueCount == 1 && world.GetEntity(passage).ProduceSlot == 0,
                "폭풍편 was refused at tier 2, so the refusal above proves nothing");
            world.Step(Cancel(passage, 0, 2));

            world.SpawnBuilding(0, Role.Tech, At(5, 9), complete: true);
            banked = world.GetResources(0);
            world.Step(Produce(passage, 0, 3, Role.Melee, 1));
            Check(world.GetResources(0) == banked - FactionData.Production(Faction.Driftworlds, Role.Melee, 1).Resources,
                "the 층위 관측대 did not open 멸종한 화염 슬라임");
        }

        /// <summary>
        /// 지옥불 군단장 needs the 용암 도가니: refused before it, taken while it
        /// stands, refused again on the tick after it falls. HasStanding is pinned
        /// directly as well. The 용암 도가니 is also what opens 군단장's tier, so
        /// here the two refusals land together; the check after this one is the
        /// world where they do not.
        /// </summary>
        private static void APrerequisiteGatesUntilItStandsAndAgainOnceItFalls()
        {
            var needs = FactionData.Prerequisite(Faction.Hellfire, Role.Signature, 0);
            Check(needs.Role == Role.Tech && needs.Slot == 0, "지옥불 군단장 no longer needs the 용암 도가니");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Hellfire);
            world.SetPeerFaction(1, Faction.TreeSpirits);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);                     // 0
            int pit = world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);      // 1
            world.GrantResources(0, 2000);

            int banked = world.GetResources(0);
            world.Step(Produce(pit, 0, 0, Role.Signature, 0));
            Check(world.GetResources(0) == banked, "지옥불 군단장 was taken with no 용암 도가니");
            Check(!world.HasStanding(0, Role.Tech, 0), "HasStanding answered yes with nothing standing");

            // Far from everything else, so the squad that comes for it later
            // reaches nothing but it.
            int crucible = world.SpawnBuilding(0, Role.Tech, At(30, 30), complete: true);
            Check(world.HasStanding(0, Role.Tech, 0), "HasStanding missed a finished 용암 도가니");
            Check(!world.HasStanding(1, Role.Tech, 0), "HasStanding answered for the other peer");
            Check(!world.HasStanding(0, Role.Tech, 1), "HasStanding answered for another entry of the slot");
            world.Step(Produce(pit, 0, 1, Role.Signature, 0));
            Check(world.GetResources(0) == banked - 220, "the 용암 도가니 did not open 지옥불 군단장");
            world.Step(Cancel(pit, 0, 2));

            for (int i = 0; i < 8; i++) world.SpawnUnit(1, Role.Melee, At(31, 29 + i % 3));
            var idle = new List<Command>();
            for (int t = 0; t < 400 && world.GetEntity(crucible).Alive; t++) world.Step(idle);
            Check(!world.GetEntity(crucible).Alive, "the squad never destroyed the 용암 도가니");
            Check(!world.HasStanding(0, Role.Tech, 0), "HasStanding answered yes for a dead building");

            banked = world.GetResources(0);
            world.Step(Produce(pit, 0, 3, Role.Signature, 0));
            Check(world.GetResources(0) == banked, "지옥불 군단장 was taken after the 용암 도가니 fell");
        }

        /// <summary>
        /// The prerequisite refuses on its own, at a tier that would allow the
        /// entry. A 세계수 정령 peer whose 뿌리 회당 stands and whose 풀씨 둥지
        /// does not is at tier 3, past what a second 뿌리 회당 (tier 2) and a 고목
        /// 수호자 (tier 2) ask for, and both are refused because the 풀씨 둥지 they
        /// name is missing. Once one stands, both are taken. With the prerequisite
        /// test removed from the gate this check is the one that fails: every
        /// other prerequisite in play is also the building that opens the tier.
        ///
        /// Played twice: once with the 풀씨 둥지 never built, once with it built
        /// and then destroyed by an enemy squad, which is how a match gets here.
        /// </summary>
        private static void APrerequisiteRefusesWhereTheTierWouldAllow()
        {
            // Never built.
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Tech, At(9, 5), complete: true);
            int worker = world.SpawnWorker(0, At(20, 20));
            world.GrantResources(0, 2000);
            RefusedWithoutTheNest(world, worker, "with no 풀씨 둥지 ever built");
            world.SpawnBuilding(0, Role.Production, At(5, 9), complete: true);
            TakenWithTheNest(world, worker, 1);

            // Built, then lost.
            var lost = new World(Seed);
            lost.SetPeerFaction(0, Faction.TreeSpirits);
            lost.SetPeerFaction(1, Faction.Hellfire);
            lost.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            lost.SpawnBuilding(0, Role.Tech, At(9, 5), complete: true);
            int nest = lost.SpawnBuilding(0, Role.Production, At(40, 40), complete: true);
            int spirit = lost.SpawnWorker(0, At(20, 20));
            lost.GrantResources(0, 2000);
            for (int i = 0; i < 8; i++) lost.SpawnUnit(1, Role.Melee, At(41, 39 + i % 3));
            var idle = new List<Command>();
            for (int t = 0; t < 400 && lost.GetEntity(nest).Alive; t++) lost.Step(idle);
            Check(!lost.GetEntity(nest).Alive, "the squad never destroyed the 풀씨 둥지");
            RefusedWithoutTheNest(lost, spirit, "after the 풀씨 둥지 fell");
            lost.SpawnBuilding(0, Role.Production, At(5, 9), complete: true);
            TakenWithTheNest(lost, spirit, 1);
        }

        private static void RefusedWithoutTheNest(World world, int worker, string when)
        {
            Check(world.TierOf(0) == FactionData.MaxTier,
                "the 뿌리 회당 alone holds tier " + world.TierOf(0) + " " + when + ", so this proves nothing about the prerequisite");
            Check(FactionData.Tier(Faction.TreeSpirits, Role.Tech, 0) < FactionData.MaxTier &&
                  FactionData.Tier(Faction.TreeSpirits, World.GuardianRole, World.GuardianSlot) < FactionData.MaxTier,
                "the 뿌리 회당 or 고목 수호자 opens at tier 3, so the tier and not the prerequisite refuses below");
            Check(!world.CanBuild(0, Role.Tech, 0, At(30, 30)), "a second 뿌리 회당 could be placed " + when);
            int banked = world.GetResources(0);
            world.Step(Grow(worker, 0, 0, World.GuardianRole, World.GuardianSlot));
            Check(!World.IsGrowing(world.GetEntity(worker)) && world.GetResources(0) == banked,
                "고목 수호자 grew " + when);
        }

        private static void TakenWithTheNest(World world, int worker, int seq)
        {
            Check(world.CanBuild(0, Role.Tech, 0, At(30, 30)), "the 풀씨 둥지 did not let a 뿌리 회당 be placed");
            world.Step(Grow(worker, 0, seq, World.GuardianRole, World.GuardianSlot));
            Check(World.IsGrowing(world.GetEntity(worker)), "the 풀씨 둥지 did not open 고목 수호자");
        }

        /// <summary>
        /// A prerequisite under construction is not standing. Asked through the
        /// two gates a half-built building can be the answer to here, a Produce
        /// and a Build, each before the site finishes and on the tick it does.
        /// No Research row names a Requires today, so there is no third.
        /// </summary>
        private static void AnUnfinishedPrerequisiteOpensNothing()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.WaterSlimes);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);                       // 0
            int works = world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);      // 1
            int altar = world.SpawnBuilding(0, Role.Tech, At(5, 9), complete: false);           // 2
            world.GrantResources(0, 2000);

            Check(!world.HasStanding(0, Role.Tech, 0), "HasStanding counted a site under construction");
            Check(!world.CanProduce(0, works, Role.Ranged, 1), "a half-built 조수 제단 opened 해일 인도자");

            var idle = new List<Command>();
            while (world.GetEntity(altar).BuildTicksLeft > 0) world.Step(idle);
            Check(world.HasStanding(0, Role.Tech, 0), "the finished 조수 제단 does not stand");
            Check(world.CanProduce(0, works, Role.Ranged, 1), "the finished 조수 제단 did not open 해일 인도자");

            // The Build side: the 기술 건물 needs the production building finished.
            var site = new World(Seed);
            site.SetPeerFaction(0, Faction.WaterSlimes);
            site.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            int generator = site.SpawnBuilding(0, Role.Production, At(9, 5), complete: false);
            site.GrantResources(0, 2000);
            Check(!site.CanBuild(0, Role.Tech, 0, At(20, 20)), "a half-built 거품 생성기 let the 조수 제단 be placed");
            while (site.GetEntity(generator).BuildTicksLeft > 0) site.Step(idle);
            Check(site.CanBuild(0, Role.Tech, 0, At(20, 20)), "the finished 거품 생성기 did not let the 조수 제단 be placed");
        }

        /// <summary>
        /// A building makes what is on its list and nothing else. 세계수 정령's
        /// 풀씨 둥지 makes 풀씨 정령 as well as fighters; 지옥불's 악마 산란장
        /// makes no worker, its base makes no fighter, and its 기술 건물 makes
        /// nothing at all. Every order is a real Produce, read back off the bank.
        /// </summary>
        private static void ABuildingProducesOnlyWhatIsOnItsList()
        {
            var spirits = new World(Seed);
            spirits.SetPeerFaction(0, Faction.TreeSpirits);
            spirits.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            int nest = spirits.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);
            spirits.GrantResources(0, 1000);
            spirits.Step(Produce(nest, 0, 0, Role.Worker, 0));
            Check(spirits.GetEntity(nest).QueueCount == 1, "the 풀씨 둥지 refused a 풀씨 정령, which its list carries");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Hellfire);
            int altar = world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            int pit = world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);
            int crucible = world.SpawnBuilding(0, Role.Tech, At(5, 9), complete: true);
            world.GrantResources(0, 1000);

            int banked = world.GetResources(0);
            int seq = 0;
            world.Step(Produce(pit, 0, seq++, Role.Worker, 0));
            world.Step(Produce(altar, 0, seq++, Role.Melee, 0));
            world.Step(Produce(crucible, 0, seq++, Role.Melee, 0));
            world.Step(Produce(crucible, 0, seq++, Role.Worker, 0));
            Check(world.GetResources(0) == banked, "a building took an order for something off its list");
            Check(world.GetEntity(pit).QueueCount == 0 && world.GetEntity(altar).QueueCount == 0 &&
                  world.GetEntity(crucible).QueueCount == 0, "a building queued something off its list");

            // The controls: the same two entries at the buildings that make them.
            world.Step(Produce(altar, 0, seq++, Role.Worker, 0));
            world.Step(Produce(pit, 0, seq, Role.Melee, 0));
            Check(world.GetEntity(altar).QueueCount == 1 && world.GetEntity(pit).QueueCount == 1,
                "the 균열 제단 or the 악마 산란장 refused what it makes, so the refusals prove nothing");
        }

        /// <summary>
        /// A row is researched in the building it names and nowhere else. The
        /// production building is a finished building of the same peer at the
        /// same tier, so the only thing refusing it is that it is not the row's.
        /// </summary>
        private static void ResearchRunsOnlyInTheRowsBuilding()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            int stone = world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);
            int moss = world.SpawnBuilding(0, Role.Tech, At(5, 9), complete: true);
            world.GrantResources(0, 1000);

            int banked = world.GetResources(0);
            world.Step(new List<Command> { new Command(0, 0, 0, CommandType.Research, stone, FixVec2.Zero, 0) });
            Check(world.GetResources(0) == banked && world.GetEntity(stone).ResearchIndex < 0,
                "the 각성 바위 ran a row the 고대 이끼돌 owns");
            world.Step(new List<Command> { new Command(0, 0, 1, CommandType.Research, moss, FixVec2.Zero, 0) });
            Check(world.GetEntity(moss).ResearchIndex == 0, "the 고대 이끼돌 refused its own row");
        }

        /// <summary>
        /// 고목 수호자 is tier 2 behind the 풀씨 둥지: refused with nothing
        /// standing, refused beside a half-built one, taken once it stands. 덩쿨
        /// 정령 is tier 1 and needs nothing, so it is taken in the bare world —
        /// the gate is per entry, not a growth switch.
        /// </summary>
        private static void GrowthAsksTheTierAndThePrerequisite()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            int worker = world.SpawnWorker(0, At(30, 30));
            int vine = world.SpawnWorker(0, At(34, 30));
            world.GrantResources(0, 1000);

            world.Step(Grow(worker, 0, 0, World.GuardianRole, World.GuardianSlot));
            Check(!World.IsGrowing(world.GetEntity(worker)), "고목 수호자 grew with no 풀씨 둥지");

            int nest = world.SpawnBuilding(0, Role.Production, At(5, 5), complete: false);
            world.Step(Grow(worker, 0, 1, World.GuardianRole, World.GuardianSlot));
            Check(!World.IsGrowing(world.GetEntity(worker)), "고목 수호자 grew beside a half-built 풀씨 둥지");

            world.Step(Grow(vine, 0, 2, World.VineRole, World.VineSlot));
            Check(World.IsGrowing(world.GetEntity(vine)), "덩쿨 정령 was refused at tier 1");

            var idle = new List<Command>();
            while (world.GetEntity(nest).BuildTicksLeft > 0) world.Step(idle);
            world.Step(Grow(worker, 0, 3, World.GuardianRole, World.GuardianSlot));
            Check(World.IsGrowing(world.GetEntity(worker)), "the finished 풀씨 둥지 did not open 고목 수호자");
        }

        // ---------------------------------------------------------------------
        // The predicates

        /// <summary>
        /// The warlord predicate asks the entry. A 지옥불 signature body on any
        /// other entry — the slot docs/TECH-TREE.md fills with 산란 악마 and 운석
        /// 술사 — arms no spawn clock and emits nothing.
        /// </summary>
        private static void OnlyEntryZeroOfTheSignatureSlotIsAWarlord()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Hellfire);
            int warlord = world.SpawnUnit(0, World.WarlordRole, World.WarlordSlot, At(20, 20));
            int other = world.SpawnUnit(0, World.WarlordRole, World.WarlordSlot + 1, At(40, 40));
            Check(world.IsWarlord(world.GetEntity(warlord)), "entry 0 of 지옥불's signature slot is not a warlord");
            Check(!world.IsWarlord(world.GetEntity(other)), "entry 1 of 지옥불's signature slot is a warlord");

            var idle = new List<Command>();
            for (int t = 0; t < World.WarlordSpawnTicks + 1; t++) world.Step(idle);
            Check(world.GetEntity(warlord).OffspringCount == 1, "the warlord never spawned, so the control proves nothing");
            Check(world.GetEntity(other).OffspringCount == 0 && world.EntityCount == 3,
                "a signature body past entry 0 spawned 자손");
        }

        /// <summary>
        /// 일제 사격 is 물결 궁수's. One archer in the middle of seven 해일 인도자
        /// shoots unbuffed, and so does every herald: eight bodies that deal the
        /// shared ranged hit and nothing more. Before the predicate asked the
        /// entry, the same blob was eight archers at the cap.
        /// </summary>
        private static void TheTideHeraldIsNoArcher()
        {
            int archerShot = FactionData.Stats(Faction.WaterSlimes, World.ArcherRole, World.ArcherSlot).Damage;
            int heraldShot = FactionData.Stats(Faction.WaterSlimes, Role.Ranged, 1).Damage;
            Check(FactionData.Stats(Faction.WaterSlimes, Role.Ranged, 1).AttackTicks ==
                  FactionData.Stats(Faction.WaterSlimes, World.ArcherRole, World.ArcherSlot).AttackTicks,
                "해일 인도자 fires on another clock, so the shot count below is wrong");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.WaterSlimes);
            world.SetPeerFaction(1, Faction.TreeSpirits);
            int dummy = world.SpawnUnit(1, Role.None, At(32, 32), hpOverride: 1000000);
            FixVec2[] blob =
            {
                At(27, 31), At(28, 31), At(29, 31),
                At(27, 32), At(28, 32), At(29, 32),
                At(27, 33), At(28, 33),
            };
            for (int i = 0; i < blob.Length; i++)
            {
                // The archer in the middle, the heralds around it.
                world.SpawnUnit(0, Role.Ranged, i == 4 ? World.ArcherSlot : 1, blob[i]);
            }

            var idle = new List<Command>();
            for (int t = 0; t < VolleyTicks; t++) world.Step(idle);
            Entity target = world.GetEntity(dummy);
            int dealt = target.MaxHp - target.Hp;
            int expected = (archerShot + 7 * heraldShot) * VolleyShots;
            Check(dealt == expected,
                "an archer among 해일 인도자 dealt " + dealt + " with them, expected the unbuffed " + expected);
        }

        // Fires every AttackTicks and on the tick it acquires, so a run of
        // VolleyTicks lands exactly VolleyShots volleys: ticks 0, 18, 36, 54, 72, 90.
        private const int VolleyTicks = 100;
        private const int VolleyShots = 6;

        // ---------------------------------------------------------------------
        // The AI

        private const int AiTicks = 2000;

        /// <summary>
        /// Every faction against itself, played by the simulation. On every tick,
        /// every queue holds an entry its building is on the list for, so no
        /// fighter is ever ordered at a base that makes only workers.
        ///
        /// And the ladder climbs the tree: in every mirror but 지옥불's, each peer
        /// builds its 기술 건물, and in every mirror but 지옥불's and 인간's it then
        /// produces a body that needs one standing (번개 정령, 해일 인도자, 고대
        /// 골렘, a 멸종 슬라임). 지옥불's only such body is 군단장 at 220, which the
        /// opponent never banks, so it keeps buying 용암 갑각 악마 and that is the
        /// AI being right. 인간's only such body is the unarmed 마도 정찰기, which the
        /// ladder skips for 공수 특공대; RosterNumbersChecks pins that.
        /// </summary>
        private static void TheAiOrdersOnlyFromTheListAndBuildsTowardTierThree()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                World world = MatchScenario.Build(Seed, faction, faction);
                int opening = world.EntityCount;
                world.SetPeerAi(0, true);
                world.SetPeerAi(1, true);

                var idle = new List<Command>();
                for (int t = 0; t < AiTicks && !world.MatchOver; t++)
                {
                    world.Step(idle);
                    for (int i = 0; i < world.EntityCount; i++)
                    {
                        Entity b = world.GetEntity(i);
                        if (!b.Alive || b.Kind != EntityKind.Building || b.QueueCount == 0) continue;
                        Check(FactionData.IsProducedAt(faction, b.ProduceRole, b.ProduceSlot, b.Role, b.Slot),
                            faction + " AI queued " + Where(faction, b.ProduceRole, b.ProduceSlot) + " at " +
                            Where(faction, b.Role, b.Slot) + " on tick " + t);
                    }
                }

                if (faction == Faction.Hellfire) continue;
                for (int peer = 0; peer < MatchScenario.Peers; peer++)
                {
                    // Ids are handed out in order and never reused, so a body with
                    // a higher id than the peer's first 기술 건물 was made after it.
                    int tech = -1, above = -1;
                    for (int i = opening; i < world.EntityCount; i++)
                    {
                        Entity e = world.GetEntity(i);
                        if (e.Owner != peer) continue;
                        if (tech < 0 && e.Kind == EntityKind.Building && e.Role == Role.Tech && e.Slot == 0) tech = i;
                        var needs = FactionData.Prerequisite(faction, e.Role, e.Slot);
                        if (e.Kind == EntityKind.Unit && needs.Role == Role.Tech && needs.Slot == 0 && above < 0) above = i;
                    }
                    Check(tech >= 0, faction + " AI peer " + peer + " never built its 기술 건물");
                    if (faction == Faction.Humans) continue;
                    Check(above > tech, faction + " AI peer " + peer + " never produced a body behind its 기술 건물");
                }
            }
        }

        // ---------------------------------------------------------------------
        // The scripted log

        /// <summary>
        /// The log the golden hash is taken over queues its fighters at the
        /// production buildings, and every one of those orders is taken. A refused
        /// order changes nothing, so a log whose Produce had kept naming the base
        /// would hash the same on both runtimes and cover no production at all.
        /// </summary>
        private static void ScriptedLogProducesAtTheProductionBuilding()
        {
            List<Command>[] log = ScriptedLog.Build();
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            int orders = 0;
            for (int t = 0; t < log.Length; t++)
            {
                var before = new Dictionary<int, int>();
                foreach (Command c in log[t])
                {
                    if (c.Type == CommandType.Produce) before[c.EntityId] = world.GetEntity(c.EntityId).QueueCount;
                }
                world.Step(log[t]);
                foreach (Command c in log[t])
                {
                    if (c.Type != CommandType.Produce) continue;
                    orders++;
                    Entity b = world.GetEntity(c.EntityId);
                    Check(b.Role == Role.Production, "the scripted log produces at " + b.Role + " entity " + c.EntityId);
                    Check(b.QueueCount > before[c.EntityId] || b.ProduceTicksLeft > 0,
                        "the scripted log's Produce at entity " + c.EntityId + " on tick " + t + " was refused");
                }
            }
            Check(orders > 0, "the scripted log carries no Produce");
        }

        // ---------------------------------------------------------------------

        private delegate void EntryVisitor(Faction faction, Role role, int slot);

        /// <summary>Every entry every faction fields, faction then role then slot.</summary>
        private static void ForEveryEntry(EntryVisitor visit)
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var faction = (Faction)f;
                    var role = (Role)r;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        if (FactionData.Has(faction, role, s)) visit(faction, role, s);
                    }
                }
            }
        }

        private static string Where(Faction faction, Role role, int slot) =>
            faction + "." + role + "[" + slot + "] " + FactionData.Name(faction, role, slot);

        private static List<Command> Produce(int building, int peer, int seq, Role role, int slot) =>
            new List<Command>
            {
                new Command(0, peer, seq, CommandType.Produce, building, FixVec2.Zero, Command.RosterArg(role, slot))
            };

        private static List<Command> Cancel(int building, int peer, int seq) =>
            new List<Command> { new Command(0, peer, seq, CommandType.CancelProduction, building, FixVec2.Zero) };

        private static List<Command> Grow(int worker, int peer, int seq, Role role, int slot) =>
            new List<Command>
            {
                new Command(0, peer, seq, CommandType.Grow, worker, FixVec2.Zero, Command.RosterArg(role, slot))
            };

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
