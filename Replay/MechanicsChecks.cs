using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #197's checks: docs/TECH-TREE.md 묶음 4, the four faction mechanics
    /// that stopped being entry 0 constants and became tables. Kept out of
    /// Program.cs the way the other per-issue files are.
    ///
    /// Every number here is written out from the document rather than read back
    /// from FactionData, because a check that read the table against itself
    /// would pin nothing: the three unit-to-unit growth prices, 산란 악마's 120
    /// ticks and three 잿불 새끼, the arrival radii 3 and 4, the 씨앗 요람 radius
    /// 8 and 12, 고대 골렘's three debris cells.
    /// </summary>
    internal static class MechanicsChecks
    {
        private const ulong Seed = 0xB0A7;

        public static void Check()
        {
            UnitGrowthsTakeTheDocumentedPrices();
            AGrowingUnitHoldsItsFire();
            TheGiantVineRootsWhereItGrew();
            SpawnersFollowTheirRows();
            TheBroodCapIsTheBroodDemonsResearch();
            AnchorsReachTheirOwnRadius();
            TheDeepPassageArrivesBodies();
            TheSeedCradleReturnsDeathsInsideItsRadius();
            TheSeedCradleTakesTheNearestAndDropsWhatItCannotHold();
            TheSeedCradleRadiusResearchReachesTwelve();
            TheAncientGolemLeavesThreeCells();
            DebrisKeepsTheLongerExpiry();
            ABlockedArrivalCellLandsAtThePassage();
            TwoWorldsAgree();
        }

        // ---------------------------------------------------------------------
        // M6: a unit grows into a unit

        private static readonly (Role FromRole, int FromSlot, Role Role, int Slot, string Name, int Resources, int Ticks)[] UnitGrowths =
        {
            (Role.Melee, 0, Role.Melee, 3, "폭풍 기수", 60, 60),
            (Role.Ranged, 0, Role.Ranged, 2, "번개새 무리", 60, 50),
            (Role.Ranged, 1, Role.Ranged, 3, "거대 덩쿨", 100, 120),
        };

        /// <summary>
        /// Each of the three rows grows from the entry the document names, at its
        /// price and on its clock to the tick, and is refused to a 풀씨 정령 and to
        /// a unit of another row's source.
        /// </summary>
        private static void UnitGrowthsTakeTheDocumentedPrices()
        {
            foreach (var g in UnitGrowths)
            {
                World world = TreeWorld();
                int body = world.SpawnUnit(0, g.FromRole, g.FromSlot, At(40, 40));
                int bank = world.GetResources(0);
                world.Step(Grow(body, 1, g.Role, g.Slot));
                Entity e = world.GetEntity(body);
                Check(bank - world.GetResources(0) == g.Resources,
                    g.Name + " cost " + (bank - world.GetResources(0)) + ", not " + g.Resources);
                Check(World.IsGrowing(e) && e.MorphTicksLeft == g.Ticks - 1,
                    g.Name + " has " + e.MorphTicksLeft + " ticks left after one, not " + (g.Ticks - 1));
                Run(world, g.Ticks - 2);
                Check(World.IsGrowing(world.GetEntity(body)), g.Name + " finished before " + g.Ticks + " ticks");
                Run(world, 1);
                e = world.GetEntity(body);
                Check(!World.IsGrowing(e) && e.Kind == EntityKind.Unit && e.Role == g.Role && e.Slot == g.Slot,
                    g.Name + " did not land on tick " + g.Ticks + ": " + e.Role + " " + e.Slot);

                World workers = TreeWorld();
                int worker = workers.SpawnWorker(0, At(40, 40));
                int before = workers.GetResources(0);
                workers.Step(Grow(worker, 1, g.Role, g.Slot));
                Check(!World.IsGrowing(workers.GetEntity(worker)) && workers.GetResources(0) == before,
                    "a 풀씨 정령 grew into " + g.Name);
            }

            // A source of one row is not a source of another: 잎날 정령 into 거대 덩쿨.
            World wrong = TreeWorld();
            int leaf = wrong.SpawnUnit(0, Role.Melee, 0, At(40, 40));
            wrong.Step(Grow(leaf, 1, Role.Ranged, 3));
            Check(!World.IsGrowing(wrong.GetEntity(leaf)), "잎날 정령 grew into 거대 덩쿨");
            // And a unit does not go back to a worker's rows: 잎날 정령 into 고목 수호자.
            wrong.Step(Grow(leaf, 2, Role.Melee, 1));
            Check(!World.IsGrowing(wrong.GetEntity(leaf)), "잎날 정령 grew into 고목 수호자");
        }

        /// <summary>
        /// 성장 중에는 공격하지 않는다, for a unit. An enemy building stands one cell
        /// from a 잎날 정령 that starts growing on tick 0: it takes nothing for the
        /// whole growth and is hit within one reload of the 폭풍 기수 landing.
        /// </summary>
        private static void AGrowingUnitHoldsItsFire()
        {
            World world = TreeWorld();
            int leaf = world.SpawnUnit(0, Role.Melee, 0, At(40, 40));
            int target = world.SpawnBuilding(1, Role.Supply, At(41, 40), complete: true);
            int full = world.GetEntity(target).Hp;
            world.Step(Grow(leaf, 1, Role.Melee, 3));
            // Steps 2 to 59: still growing. Step 60 lands it, and a body fights on
            // the tick it lands, so the building is untouched up to here only.
            for (int t = 1; t < 59; t++)
            {
                world.Step(Idle);
                Check(World.IsGrowing(world.GetEntity(leaf)), "폭풍 기수 landed early");
                Check(world.GetEntity(target).Hp == full, "a growing 잎날 정령 hit the building on tick " + world.Tick);
            }
            world.Step(Idle);
            Check(!World.IsGrowing(world.GetEntity(leaf)), "폭풍 기수 did not land in 60 ticks");
            Run(world, 16);
            Check(world.GetEntity(target).Hp < full, "the grown 폭풍 기수 never hit the building, so the check above is vacuous");
        }

        /// <summary>
        /// 거대 덩쿨 has no speed: grown from a walking 덩쿨 정령, it takes a Move
        /// and stays on the coordinate it grew on.
        /// </summary>
        private static void TheGiantVineRootsWhereItGrew()
        {
            World world = TreeWorld();
            int vine = world.SpawnUnit(0, Role.Ranged, 1, At(40, 40));
            world.Step(Grow(vine, 1, Role.Ranged, 3));
            Run(world, 119);
            Entity grown = world.GetEntity(vine);
            Check(grown.Role == Role.Ranged && grown.Slot == 3, "거대 덩쿨 did not land");
            Check(grown.Speed == Fix.Zero, "거대 덩쿨 stands up with speed " + grown.Speed.Raw);
            FixVec2 rooted = grown.Position;
            world.Step(new List<Command> { new Command(0, 0, 2, CommandType.Move, vine, At(50, 40)) });
            Run(world, 60);
            Check(world.GetEntity(vine).Position.Equals(rooted), "거대 덩쿨 walked off the cell it grew on");
        }

        // ---------------------------------------------------------------------
        // M8: the spawner table

        /// <summary>
        /// 산란 악마 emits a 잿불 새끼 every 120 ticks and keeps three; 지옥불 군단장
        /// beside it keeps its 100 ticks and five 자손, unchanged.
        /// </summary>
        private static void SpawnersFollowTheirRows()
        {
            World world = HellWorld(out _, out _);
            int brood = world.SpawnUnit(0, Role.Signature, 1, At(30, 30));
            int warlord = world.SpawnUnit(0, Role.Signature, 0, At(30, 45));

            Run(world, 100);
            Check(Children(world, warlord) == 1, "군단장 has " + Children(world, warlord) + " 자손 after 100 ticks, not 1");
            Run(world, 19);
            Check(Children(world, brood) == 0, "산란 악마 emitted before 120 ticks");
            Run(world, 1);
            Check(Children(world, brood) == 1, "산란 악마 has " + Children(world, brood) + " 잿불 새끼 after 120 ticks, not 1");
            Entity child = world.GetEntity(FirstChild(world, brood));
            Check(child.Kind == EntityKind.Unit && child.Role == Role.Melee && child.Slot == 3 && child.Owner == 0,
                "산란 악마 emitted " + child.Role + " " + child.Slot + ", not 잿불 새끼 (Melee 3)");

            Run(world, 600 - 120);
            Check(Children(world, brood) == 3 && world.GetEntity(brood).OffspringCount == 3,
                "산란 악마 keeps " + Children(world, brood) + " 잿불 새끼 after 600 ticks, not 3");
            Check(Children(world, warlord) == 5 && world.GetEntity(warlord).OffspringCount == 5,
                "군단장 keeps " + Children(world, warlord) + " 자손 after 600 ticks, not 5");
        }

        /// <summary>
        /// 산란 상한 raises 산란 악마's cap to 4, and the two warlord rows (군단 확장,
        /// 산란 가속) leave it at 3 and 120 ticks.
        /// </summary>
        private static void TheBroodCapIsTheBroodDemonsResearch()
        {
            World brood = HellWorld(out int tech, out int crater);
            ResearchToCompletion(brood, crater, RowOf(Faction.Hellfire, UpgradeEffect.BroodCap));
            int demon = brood.SpawnUnit(0, Role.Signature, 1, At(30, 30));
            Run(brood, 120 * 6);
            Check(Children(brood, demon) == 4, "researched 산란 악마 keeps " + Children(brood, demon) + " 잿불 새끼, not 4");

            World warlords = HellWorld(out tech, out _);
            ResearchToCompletion(warlords, tech, RowOf(Faction.Hellfire, UpgradeEffect.OffspringCap));
            ResearchToCompletion(warlords, tech, RowOf(Faction.Hellfire, UpgradeEffect.OffspringPeriod));
            Check(warlords.WarlordOffspringCap(0) == 7 && warlords.WarlordSpawnTicksFor(0) == 70,
                "the warlord research did not land on the warlord row");
            SpawnerRow row = FactionData.Spawner(Faction.Hellfire, Role.Signature, 1);
            Check(warlords.SpawnCapFor(0, row) == 3 && warlords.SpawnTicksFor(0, row) == 120,
                "the warlord research moved 산란 악마 to " + warlords.SpawnCapFor(0, row) + " every " + warlords.SpawnTicksFor(0, row));
            demon = warlords.SpawnUnit(0, Role.Signature, 1, At(30, 30));
            Run(warlords, 120 * 6);
            Check(Children(warlords, demon) == 3, "산란 악마 keeps " + Children(warlords, demon) + " under the warlord research, not 3");
        }

        // ---------------------------------------------------------------------
        // M9: the arrival table

        /// <summary>
        /// 위상 닻 lends 3 cells and 4 under 위상 닻 반경, and 도착 반경 does not
        /// widen it. 떠도는 섬 lends 4. 틈새 포대 lends none.
        /// </summary>
        private static void AnchorsReachTheirOwnRadius()
        {
            Radius("위상 닻", w => w.SpawnBuilding(0, Role.Defense, 1, At(30, 30), true), null, 3);
            Radius("위상 닻 under 위상 닻 반경", w => w.SpawnBuilding(0, Role.Defense, 1, At(30, 30), true),
                UpgradeEffect.PhaseAnchorRadius, 4);
            Radius("위상 닻 under 도착 반경", w => w.SpawnBuilding(0, Role.Defense, 1, At(30, 30), true),
                UpgradeEffect.ArrivalRadius, 3);
            Radius("떠도는 섬", w => w.SpawnUnit(0, Role.Signature, 1, At(30, 30)), null, 4);
            Radius("굴절 기둥 under 도착 반경", w => w.SpawnBuilding(0, Role.Defense, 0, At(30, 30), true),
                UpgradeEffect.ArrivalRadius, 8);

            World battery = DriftWorld(out _, out _);
            int id = battery.SpawnBuilding(0, Role.Defense, 2, At(30, 30), true);
            Check(!battery.ProvidesArrival(battery.GetEntity(id), 0), "틈새 포대 lends an arrival radius");
            Check(!battery.ArrivalValid(0, At(31, 30)), "a point one cell from 틈새 포대 is an arrival point");
            Check(battery.ArrivalRadiusOf(battery.GetEntity(id)) == Fix.Zero, "틈새 포대 has an arrival radius");
        }

        /// <summary>
        /// A point exactly radius cells east of the anchor is accepted and one
        /// sixteenth of a cell further is refused.
        /// </summary>
        private static void Radius(string what, Func<World, int> spawn, UpgradeEffect? research, int cells)
        {
            World world = DriftWorld(out int tech, out int yard);
            if (research.HasValue)
            {
                UpgradeEffect effect = research.Value;
                int at = FactionData.Upgrade(Faction.Driftworlds, RowOf(Faction.Driftworlds, effect)).ResearchAt.Slot == 1 ? yard : tech;
                ResearchToCompletion(world, at, RowOf(Faction.Driftworlds, effect));
            }
            int anchor = spawn(world);
            Check(world.ProvidesArrival(world.GetEntity(anchor), 0), what + " lends no radius");
            FixVec2 p = world.GetEntity(anchor).Position;
            FixVec2 on = new FixVec2(p.X + Fix.FromInt(cells), p.Y);
            FixVec2 past = new FixVec2(on.X + Fix.Ratio(1, 16), p.Y);
            Check(world.ArrivalValid(0, on), what + ": a point " + cells + " cells out is refused");
            Check(!world.ArrivalValid(0, past), what + ": a point past " + cells + " cells out is accepted");
        }

        /// <summary>
        /// 깊은 통로 is a passage once it stands and not before, and a 층위 관측자
        /// it makes stands up at the point the order named.
        /// </summary>
        private static void TheDeepPassageArrivesBodies()
        {
            World world = DriftWorld(out _, out _);
            int rising = world.SpawnBuilding(0, Role.Production, 1, At(20, 14), complete: false);
            Check(!world.IsPassage(world.GetEntity(rising)), "an unfinished 깊은 통로 is a passage");
            int deep = world.SpawnBuilding(0, Role.Production, 1, At(14, 14), complete: true);
            Check(world.IsPassage(world.GetEntity(deep)), "a standing 깊은 통로 is not a passage");
            world.SpawnBuilding(0, Role.Defense, 0, At(30, 30), true);

            FixVec2 aim = At(33, 30);
            int born = world.EntityCount;
            world.Step(new List<Command>
            {
                new Command(0, 0, 1, CommandType.Produce, deep, aim, Command.RosterArg(Role.Signature, 2))
            });
            Check(world.GetEntity(deep).HasArrivalPoint, "깊은 통로 did not keep a covered point");
            for (int t = 0; t < 200 && world.EntityCount == born; t++) world.Step(Idle);
            Check(world.EntityCount == born + 1, "깊은 통로 never produced 층위 관측자");
            Entity body = world.GetEntity(born);
            Check(body.Role == Role.Signature && body.Slot == 2 && body.Position.Equals(aim),
                "깊은 통로's body did not stand up at the point");
        }

        // ---------------------------------------------------------------------
        // 씨앗 요람

        /// <summary>
        /// A 잎날 정령 killed 8 cells from a 씨앗 요람 puts one free 풀씨 정령 on
        /// its queue, which comes off 30 ticks later; one killed 9 cells out puts
        /// nothing, and so does one killed beside an unfinished 요람 or beside
        /// another peer's. The queue refuses a CancelProduction. A dead worker puts
        /// nothing.
        /// </summary>
        private static void TheSeedCradleReturnsDeathsInsideItsRadius()
        {
            World world = TreeWorld();
            int cradle = world.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            int bank = world.GetResources(0);
            Victim(world, At(38, 30));
            world.Step(Idle);
            Entity c = world.GetEntity(cradle);
            Check(c.QueueCount == 1 && c.ProduceRole == Role.Worker && c.ProduceSlot == 0,
                "a death 8 cells from 씨앗 요람 queued " + c.QueueCount + " " + c.ProduceRole);
            Check(world.GetResources(0) == bank, "the free 풀씨 정령 was charged");

            world.Step(new List<Command> { new Command(0, 0, 1, CommandType.CancelProduction, cradle, FixVec2.Zero) });
            Check(world.GetEntity(cradle).QueueCount == 1 && world.GetResources(0) == bank,
                "CancelProduction on 씨앗 요람 was taken");

            int born = world.EntityCount;
            int ticks = 1;
            for (; ticks < 60 && world.EntityCount == born; ticks++) world.Step(Idle);
            Check(world.EntityCount == born + 1 && world.GetEntity(born).Kind == EntityKind.Worker &&
                  world.GetEntity(born).Owner == 0,
                "씨앗 요람 did not put out a 풀씨 정령");
            Check(ticks == 30, "the 풀씨 정령 came off the queue after " + ticks + " ticks, not 30");

            World outside = TreeWorld();
            int far = outside.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            Victim(outside, At(39, 30));
            outside.Step(Idle);
            Check(outside.GetEntity(far).QueueCount == 0, "a death 9 cells from 씨앗 요람 was queued");

            // An unfinished 요람 returns nothing, however close.
            World rising = TreeWorld();
            int site = rising.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: false);
            Victim(rising, At(34, 30));
            rising.Step(Idle);
            Check(rising.GetEntity(site).BuildTicksLeft > 0, "the 요람 finished, so the next line proves nothing");
            Check(rising.GetEntity(site).QueueCount == 0, "an unfinished 씨앗 요람 queued a body");

            // Another peer's 세계수 정령 dying beside this peer's 요람 returns nothing.
            // Peer 1 has a 생명의 나무, so its cap is not what drops the body.
            var mixed = new World(Seed);
            mixed.SetPeerFaction(0, Faction.TreeSpirits);
            mixed.SetPeerFaction(1, Faction.TreeSpirits);
            mixed.SetPeerFaction(2, Faction.Hellfire);
            mixed.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            mixed.SpawnBuilding(1, Role.Base, At(58, 58), complete: true);
            int theirs = mixed.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            int stranger = mixed.SpawnUnit(1, Role.Melee, 0, At(34, 30), 1);
            mixed.SpawnUnit(2, Role.Melee, 0, At(34, 31));
            mixed.Step(Idle);
            Check(!mixed.GetEntity(stranger).Alive, "peer 1's 잎날 정령 survived, so the next line proves nothing");
            Check(mixed.GetEntity(theirs).QueueCount == 0, "peer 0's 씨앗 요람 queued peer 1's dead 잎날 정령");

            World worker = TreeWorld();
            int quiet = worker.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            int seed = worker.SpawnWorker(0, At(34, 30));
            worker.SpawnUnit(1, Role.Melee, 0, At(35, 30));
            for (int t = 0; t < 200 && worker.GetEntity(seed).Alive; t++) worker.Step(Idle);
            Check(!worker.GetEntity(seed).Alive, "the worker never died, so the next line proves nothing");
            Check(worker.GetEntity(quiet).QueueCount == 0, "a dead 풀씨 정령 was queued at 씨앗 요람");
        }

        /// <summary>
        /// Two 요람 the same distance off: the lower id takes the body. A full
        /// queue of 5 drops the sixth. A death that leaves the peer under its cap is
        /// queued; one that leaves it at the cap is dropped.
        /// </summary>
        private static void TheSeedCradleTakesTheNearestAndDropsWhatItCannotHold()
        {
            World tie = TreeWorld();
            int west = tie.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            int east = tie.SpawnBuilding(0, Role.Production, 1, At(40, 30), complete: true);
            Victim(tie, At(35, 30));
            tie.Step(Idle);
            Check(tie.GetEntity(west).QueueCount == 1 && tie.GetEntity(east).QueueCount == 0,
                "a tie between two 씨앗 요람 did not go to the lower id");

            World near = TreeWorld();
            west = near.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            east = near.SpawnBuilding(0, Role.Production, 1, At(40, 30), complete: true);
            Victim(near, At(36, 30));
            near.Step(Idle);
            Check(near.GetEntity(east).QueueCount == 1 && near.GetEntity(west).QueueCount == 0,
                "the nearer 씨앗 요람 did not take the body");

            // Six deaths on one tick: six victims in a row, each with its own killer
            // one cell below, all of them first to fire.
            World full = TreeWorld();
            int cradle = full.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            var victims = new List<int>();
            for (int i = 0; i < 6; i++) victims.Add(full.SpawnUnit(0, Role.Melee, 0, At(26 + 2 * i, 33), 1));
            for (int i = 0; i < 6; i++) full.SpawnUnit(1, Role.Melee, 0, At(26 + 2 * i, 34));
            full.Step(Idle);
            foreach (int v in victims) Check(!full.GetEntity(v).Alive, "victim " + v + " survived the first tick");
            Check(full.GetEntity(cradle).QueueCount == World.MaxQueue,
                "six deaths left " + full.GetEntity(cradle).QueueCount + " on the queue, not " + World.MaxQueue);

            // The cap is 10 (one 생명의 나무). Nine workers and the victim are 10:
            // the death gives one back before the cap is asked, so 9 < 10 and the
            // body is queued. Ten workers and the victim are 11: after the death
            // the peer stands at exactly 10, which is the cap, and it is dropped.
            Check(QueuedAtPopulation(9) == 1,
                "a death at exactly the cap was dropped: the cap was asked before the body's population was given back");
            Check(QueuedAtPopulation(10) == 0,
                "씨앗 요람 queued a body with the peer at its population cap after the death");
        }

        /// <summary>
        /// One 잎날 정령 killed 4 cells from a 씨앗 요람 with this many 풀씨 정령
        /// standing beside it under a cap of 10; what the 요람's queue holds after.
        /// </summary>
        private static int QueuedAtPopulation(int workers)
        {
            World world = TreeWorld();
            int cradle = world.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            for (int i = 0; i < workers; i++) world.SpawnWorker(0, At(5 + i, 60));
            Victim(world, At(34, 30));
            Check(world.PopulationCap(0) == 10 && world.GetPopulation(0) == workers + 1,
                "the population fixture is " + world.GetPopulation(0) + " of " + world.PopulationCap(0));
            world.Step(Idle);
            return world.GetEntity(cradle).QueueCount;
        }

        /// <summary>씨앗 요람 반경: a death 12 cells out is returned only once researched.</summary>
        private static void TheSeedCradleRadiusResearchReachesTwelve()
        {
            foreach (bool researched in new[] { false, true })
            {
                World world = TreeWorld(out int tech);
                int cradle = world.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
                if (researched) ResearchToCompletion(world, tech, RowOf(Faction.TreeSpirits, UpgradeEffect.SeedCradleRadius));
                Victim(world, At(30, 42));
                world.Step(Idle);
                int queued = world.GetEntity(cradle).QueueCount;
                Check(queued == (researched ? 1 : 0),
                    "a death 12 cells out queued " + queued + (researched ? " under" : " without") + " 씨앗 요람 반경");
            }
        }

        // ---------------------------------------------------------------------
        // 잔해

        /// <summary>
        /// 고대 골렘 covers its own cell and x−1, x+1 and nothing above or below;
        /// a water cell beside it stays clear, and at the west edge the cell off
        /// the map is skipped. An 이끼바위 골렘 still covers one cell.
        /// </summary>
        private static void TheAncientGolemLeavesThreeCells()
        {
            World world = GolemWorld();
            world.SetTerrain(41, 20, TileKind.Water);
            KillAt(world, Role.Signature, 0, At(30, 30));
            KillAt(world, Role.Signature, 0, At(40, 20));
            KillAt(world, Role.Signature, 0, At(0, 50));
            KillAt(world, Role.Melee, 0, At(50, 10));
            world.Step(Idle);

            Check(world.HasRemnant(Cell(29, 30)) && world.HasRemnant(Cell(30, 30)) && world.HasRemnant(Cell(31, 30)),
                "고대 골렘 did not cover x-1, x and x+1");
            Check(!world.HasRemnant(Cell(30, 29)) && !world.HasRemnant(Cell(30, 31)) &&
                  !world.HasRemnant(Cell(28, 30)) && !world.HasRemnant(Cell(32, 30)),
                "고대 골렘 covered more than three cells");
            Check(world.HasRemnant(Cell(39, 20)) && world.HasRemnant(Cell(40, 20)) && world.RemnantExpiry(Cell(41, 20)) == 0,
                "고대 골렘 wrote debris onto water");
            Check(world.HasRemnant(Cell(0, 50)) && world.HasRemnant(Cell(1, 50)) && !world.HasRemnant(Cell(63, 49)),
                "고대 골렘 at the west edge wrote the wrong cells");
            Check(world.HasRemnant(Cell(50, 10)) && !world.HasRemnant(Cell(49, 10)) && !world.HasRemnant(Cell(51, 10)),
                "이끼바위 골렘 covered more than its own cell");
        }

        /// <summary>
        /// Two 돌 골렘 die on one cell on one tick, the 600-tick one first (peer 0,
        /// 오래 남는 잔해 researched) and the 400-tick one second (peer 1). The cell
        /// keeps 600: every write is a max.
        /// </summary>
        private static void DebrisKeepsTheLongerExpiry()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SetPeerFaction(1, Faction.RockGolems);
            world.GrantResources(0, 10000);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Production, At(9, 5), complete: true);
            int tech = world.SpawnBuilding(0, Role.Tech, At(13, 5), complete: true);
            ResearchToCompletion(world, tech, RowOf(Faction.RockGolems, UpgradeEffect.RemnantTicks));
            Check(world.RemnantTicksFor(0) == 600 && world.RemnantTicksFor(1) == 400, "the two peers do not drop 600 and 400");

            // Ids in this order: R (peer 1) kills P (peer 0) first, S (peer 0)
            // kills Q (peer 1) after it, both on the cell P and Q share.
            world.SpawnUnit(1, Role.Melee, 0, At(29, 30));                 // R
            int p = world.SpawnUnit(0, Role.Melee, 0, At(30, 30), 1);      // P
            int q = world.SpawnUnit(1, Role.Melee, 0, At(30, 30), 1);      // Q
            world.SpawnUnit(0, Role.Melee, 0, At(31, 30));                 // S
            int tick = world.Tick;
            world.Step(Idle);
            Check(!world.GetEntity(p).Alive && !world.GetEntity(q).Alive, "the two golems did not die on one tick");
            Check(world.RemnantExpiry(Cell(30, 30)) == tick + 600,
                "the cell expires on " + world.RemnantExpiry(Cell(30, 30)) + ", not " + (tick + 600) +
                ": the 400-tick debris cut the 600-tick debris short");
        }

        // ---------------------------------------------------------------------
        // The arrival cell

        /// <summary>
        /// A walking body aimed at water is refused at the order and stands up at
        /// the 통로; one aimed at a cell a building goes up on while it crosses
        /// lands at the 통로 too; a flying body aimed at the water lands on it.
        /// </summary>
        private static void ABlockedArrivalCellLandsAtThePassage()
        {
            FixVec2 water = At(32, 30);

            World wet = ArrivalWorld(out int passage);
            wet.SetTerrain(32, 30, TileKind.Water);
            Entity landed = Arrive(wet, passage, water, Role.Melee, 0, null);
            Check(landed.Position.Equals(wet.GetEntity(passage).Position + World.RallyOffset),
                "폭풍편 aimed at water did not stand up at the 통로");

            World built = ArrivalWorld(out passage);
            FixVec2 open = At(33, 30);
            landed = Arrive(built, passage, open, Role.Melee, 0,
                w => w.SpawnBuilding(0, Role.Supply, At(33, 30), complete: true));
            Check(landed.Position.Equals(built.GetEntity(passage).Position + World.RallyOffset),
                "폭풍편 stood up on a cell a building went up on while it crossed");

            World air = ArrivalWorld(out passage);
            air.SetTerrain(32, 30, TileKind.Water);
            landed = Arrive(air, passage, water, Role.Ranged, 3, null);
            Check(landed.Position.Equals(water), "멸종한 바람 슬라임 aimed at water did not land on it");
        }

        private static World ArrivalWorld(out int passage)
        {
            World world = DriftWorld(out _, out _);
            passage = world.SpawnBuilding(0, Role.Production, 0, At(14, 10), complete: true);
            world.SpawnBuilding(0, Role.Defense, 0, At(30, 30), true);
            return world;
        }

        /// <summary>
        /// Produces one body at the 통로 aimed at the point, runs the optional
        /// change once the order is taken, and returns the body that arrives.
        /// The order-time verdict must agree with where a body lands when nothing
        /// changes in between.
        /// </summary>
        private static Entity Arrive(World world, int passage, FixVec2 aim, Role role, int slot, Action<World> meanwhile)
        {
            int born = world.EntityCount;
            world.Step(new List<Command>
            {
                new Command(0, 0, 1, CommandType.Produce, passage, aim, Command.RosterArg(role, slot))
            });
            Check(world.GetEntity(passage).QueueCount == 1, role + " " + slot + " was not queued at the 통로");
            bool kept = world.GetEntity(passage).HasArrivalPoint;
            meanwhile?.Invoke(world);
            int extra = world.EntityCount - born;
            for (int t = 0; t < 200 && world.EntityCount == born + extra; t++) world.Step(Idle);
            Check(world.EntityCount == born + extra + 1, "the 통로 never produced " + role + " " + slot);
            Entity body = world.GetEntity(born + extra);
            if (meanwhile == null)
            {
                Check(kept == body.Position.Equals(aim),
                    "the order kept the point = " + kept + " but the body landed on it = " + body.Position.Equals(aim));
            }
            else
            {
                Check(kept, "the point was refused at the order, so the change during the crossing proves nothing");
            }
            return body;
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>
        /// Every mechanic above in one world, twice from scratch: growth, a
        /// spawner, a 씨앗 요람 return, debris and an arrival. Same hash at the end.
        /// </summary>
        private static void TwoWorldsAgree()
        {
            ulong a = Mixed(), b = Mixed();
            Check(a == b, "two runs of the batch 4 mechanics hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
        }

        private static ulong Mixed()
        {
            World world = TreeWorld(out _);
            world.SetPeerFaction(1, Faction.Hellfire);
            world.SpawnBuilding(0, Role.Production, 1, At(30, 30), complete: true);
            int leaf = world.SpawnUnit(0, Role.Melee, 0, At(20, 40));
            world.SpawnUnit(1, Role.Signature, 1, At(40, 50));
            Victim(world, At(36, 30));
            world.Step(Grow(leaf, 1, Role.Melee, 3));
            Run(world, 400);
            return world.Hash();
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        /// <summary>
        /// 세계수 정령 peer 0 with every building a growth row asks for, standing:
        /// 생명의 나무, 풀씨 둥지, 뿌리 회당, 바람 토템 and 광란의 토템. Peer 1 is
        /// 지옥불 and owns nothing.
        /// </summary>
        private static World TreeWorld() => TreeWorld(out _);

        private static World TreeWorld(out int tech)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SetPeerFaction(1, Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Production, 0, At(9, 5), complete: true);
            tech = world.SpawnBuilding(0, Role.Tech, 0, At(13, 5), complete: true);
            world.SpawnBuilding(0, Role.Tech, 1, At(17, 5), complete: true);
            world.SpawnBuilding(0, Role.Tech, 2, At(21, 5), complete: true);
            return world;
        }

        /// <summary>지옥불 peer 0 with 악마 산란장, 용암 도가니 and 재 분화구 standing.</summary>
        private static World HellWorld(out int tech, out int crater)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Hellfire);
            world.SetPeerFaction(1, Faction.TreeSpirits);
            world.GrantResources(0, 10000);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Production, 0, At(9, 5), complete: true);
            tech = world.SpawnBuilding(0, Role.Tech, 0, At(13, 5), complete: true);
            crater = world.SpawnBuilding(0, Role.Tech, 1, At(17, 5), complete: true);
            return world;
        }

        /// <summary>차원 유랑종 peer 0 with 정박한 세계, 층위 관측대 and 외피 연마장 standing.</summary>
        private static World DriftWorld(out int tech, out int yard)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Driftworlds);
            world.SetPeerFaction(1, Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            world.SpawnBuilding(0, Role.Production, 0, At(9, 5), complete: true);
            tech = world.SpawnBuilding(0, Role.Tech, 0, At(13, 5), complete: true);
            yard = world.SpawnBuilding(0, Role.Tech, 1, At(17, 5), complete: true);
            return world;
        }

        /// <summary>돌 골렘 peer 0, 지옥불 peer 1, nothing standing.</summary>
        private static World GolemWorld()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SetPeerFaction(1, Faction.Hellfire);
            return world;
        }

        /// <summary>
        /// A 잎날 정령 of peer 0 at one hp with a 용암 갑각 악마 of peer 1 one cell
        /// south, which kills it on the next tick's combat pass.
        /// </summary>
        private static void Victim(World world, FixVec2 at)
        {
            world.SpawnUnit(0, Role.Melee, 0, at, 1);
            world.SpawnUnit(1, Role.Melee, 0, new FixVec2(at.X, at.Y + Fix.FromInt(1)));
        }

        /// <summary>A peer 0 unit of this entry at one hp with a peer 1 killer one cell south.</summary>
        private static void KillAt(World world, Role role, int slot, FixVec2 at)
        {
            world.SpawnUnit(0, role, slot, at, 1);
            world.SpawnUnit(1, Role.Melee, 0, new FixVec2(at.X, at.Y + Fix.FromInt(1)));
        }

        private static int Children(World world, int parent)
        {
            int count = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Alive && e.ParentId == parent) count++;
            }
            return count;
        }

        private static int FirstChild(World world, int parent)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                if (world.GetEntity(i).ParentId == parent) return i;
            }
            return -1;
        }

        private static int RowOf(Faction faction, UpgradeEffect effect)
        {
            for (int i = 0; i < FactionData.UpgradeCount(faction); i++)
            {
                if (FactionData.Upgrade(faction, i).Effect == effect) return i;
            }
            throw new Exception(faction + " has no " + effect + " row");
        }

        /// <summary>Starts one level of this row at this building and steps until it lands.</summary>
        private static void ResearchToCompletion(World world, int building, int index)
        {
            Entity b = world.GetEntity(building);
            int level = world.UpgradeLevel(b.Owner, index);
            world.Step(new List<Command>
            {
                new Command(0, b.Owner, 100 + world.Tick, CommandType.Research, building, FixVec2.Zero, index)
            });
            Check(world.GetEntity(building).ResearchIndex == index, "research of row " + index + " was refused");
            Run(world, FactionData.Upgrade(world.FactionOf(b.Owner), index).Ticks - 1);
            Check(world.UpgradeLevel(b.Owner, index) == level + 1, "research of row " + index + " never landed");
        }

        private static List<Command> Grow(int body, int seq, Role role, int slot) =>
            new List<Command>
            {
                new Command(0, 0, seq, CommandType.Grow, body, FixVec2.Zero, Command.RosterArg(role, slot))
            };

        private static void Run(World world, int ticks)
        {
            for (int t = 0; t < ticks; t++) world.Step(Idle);
        }

        private static int Cell(int x, int y) => y * World.GridSize + x;

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
