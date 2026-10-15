using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #202's checks: docs/TECH-TREE.md 묶음 6, summons that lapse and
    /// bursts. Kept out of Program.cs the way the other per-issue files are.
    /// The ability rows themselves are pinned in StatusChecks' table, beside the
    /// 묶음 5 rows; what is checked here is what they do.
    ///
    /// Every step taken here goes through <see cref="Step"/>, which asserts the
    /// burst queue is empty when the step returns: docs/TECH-TREE.md 능력 공통
    /// 규칙 says it must be at every tick end, and that is why it is not hashed.
    /// </summary>
    internal static class SummonChecks
    {
        private const ulong Seed = 0x5748;

        /// <summary>docs/TECH-TREE.md 차원 유랑종 유닛: 수명 600틱.</summary>
        private const int Lifetime = 600;

        public static void Check()
        {
            OnlyTheFiveExtinctSlimesLapse();
            ATimedSummonLapsesSixHundredTicksAfterItArrives();
            SummonLifetimeIsReadAtSpawn();
            StrataHoldHalvesTheClock();
            StrataHoldReachesAlliedTimedBodiesOnce();
            ADeathBurstLandsInBurstSystemNotInKill();
            ALapseBurstsLikeADeath();
            ABurstIgnoresArmor();
            ChainedDeathsLandInTheSameTick();
            AnArrivalDischargesWhereTheBodyStands();
            BurstResearchRaisesTheAmount();
            TheQueueIsEmptyAtEveryTickEnd();
            TwoWorldsAgree();
            TheHashSeesExpireTick();
        }

        // ---------------------------------------------------------------------
        // 한시 소환

        /// <summary>docs/TECH-TREE.md 차원 유랑종 유닛, the rows whose 역할 column says 수명 600틱.</summary>
        private static readonly (Faction Faction, Role Role, int Slot, string Name)[] Lapsing =
        {
            (Faction.Driftworlds, Role.Melee, 1, "멸종한 화염 슬라임"),
            (Faction.Driftworlds, Role.Melee, 2, "멸종한 바위 슬라임"),
            (Faction.Driftworlds, Role.Ranged, 1, "멸종한 번개 슬라임"),
            (Faction.Driftworlds, Role.Ranged, 3, "멸종한 바람 슬라임"),
            (Faction.Driftworlds, Role.Signature, 3, "멸종한 자연 슬라임"),
        };

        /// <summary>
        /// Every fielded entry of every faction: the five 멸종 슬라임 carry
        /// Lifetime 600 and are spawned with ExpireTick 600 on tick 0, and every
        /// other body, worker and building has Lifetime 0 and ExpireTick 0.
        /// </summary>
        private static void OnlyTheFiveExtinctSlimesLapse()
        {
            int lapsing = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int r = 1; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    for (int slot = 0; slot < FactionData.SlotCount(faction, role); slot++)
                    {
                        if (!FactionData.Has(faction, role, slot)) continue;
                        string name = FactionData.Name(faction, role, slot);
                        int want = 0;
                        foreach (var row in Lapsing)
                        {
                            if (row.Faction == faction && row.Role == role && row.Slot == slot)
                            {
                                Check(name == row.Name, faction + " " + role + " " + slot + " is " + name + ", not " + row.Name);
                                want = Lifetime;
                                lapsing++;
                            }
                        }
                        int lifetime = FactionData.Stats(faction, role, slot).Lifetime;
                        Check(lifetime == want, name + " lives " + lifetime + " ticks, not " + want);

                        World world = Two(faction, Faction.Hellfire);
                        int id = role == Role.Worker ? world.SpawnWorker(0, At(30, 30))
                            : FactionData.IsBuilding(role) ? world.SpawnBuilding(0, role, slot, At(30, 30), complete: true)
                            : world.SpawnUnit(0, role, slot, At(30, 30));
                        Check(world.GetEntity(id).ExpireTick == want,
                            name + " was spawned on tick 0 with ExpireTick " + world.GetEntity(id).ExpireTick + ", not " + want);
                    }
                }
            }
            Check(lapsing == Lapsing.Length, "the roster fields " + lapsing + " of the five 멸종 슬라임");
        }

        /// <summary>
        /// A 멸종한 화염 슬라임 off a 통로 gets ExpireTick = its arrival tick + 600,
        /// stands through tick arrival + 599, and is gone after tick arrival + 600
        /// with its population given back. The five spawned on tick 0 by a
        /// scenario do the same: standing after 600 steps, gone after 601.
        /// </summary>
        private static void ATimedSummonLapsesSixHundredTicksAfterItArrives()
        {
            World world = DriftBase(out int passage);
            int born = world.EntityCount;
            Step(world, new List<Command>
            {
                new Command(0, 0, 1, CommandType.Produce, passage, FixVec2.Zero, Command.RosterArg(Role.Melee, 1))
            });
            for (int t = 0; t < 200 && world.EntityCount == born; t++) Step(world);
            Check(world.EntityCount == born + 1, "the 통로 never let the 멸종한 화염 슬라임 through");
            int arrived = world.Tick - 1;
            int slime = born;
            Check(world.GetEntity(slime).ExpireTick == arrived + Lifetime,
                "the slime that arrived on tick " + arrived + " lapses on " + world.GetEntity(slime).ExpireTick);
            int population = world.GetPopulation(0);
            while (world.Tick < arrived + Lifetime) Step(world);
            Check(world.GetEntity(slime).Alive, "the slime lapsed before tick " + (arrived + Lifetime));
            Step(world);
            Check(!world.GetEntity(slime).Alive, "the slime outlived tick " + (arrived + Lifetime));
            Check(world.GetEntity(slime).ExpireTick == 0, "a lapsed body still carries an ExpireTick");
            Check(world.GetPopulation(0) == population - 1, "the lapse kept the population slot");

            World five = Two(Faction.Driftworlds, Faction.Hellfire);
            var ids = new List<int>();
            for (int i = 0; i < Lapsing.Length; i++)
                ids.Add(five.SpawnUnit(0, Lapsing[i].Role, Lapsing[i].Slot, At(20 + 2 * i, 30)));
            Run(five, Lifetime);
            foreach (int id in ids) Check(five.GetEntity(id).Alive, "a 멸종 슬라임 lapsed within 600 ticks");
            Step(five);
            foreach (int id in ids) Check(!five.GetEntity(id).Alive, "a 멸종 슬라임 outlived 600 ticks");
        }

        /// <summary>
        /// 소환 수명: a slime spawned after the research lands lives 800 ticks,
        /// and one spawned before keeps the 600 it was given.
        /// </summary>
        private static void SummonLifetimeIsReadAtSpawn()
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            int early = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            Research(world, 0, UpgradeEffect.SummonLifetime);
            Check(world.GetEntity(early).ExpireTick == Lifetime, "소환 수명 rewrote a clock already running");
            int late = world.SpawnUnit(0, Role.Melee, 1, At(30, 34));
            Check(world.GetEntity(late).ExpireTick == world.Tick + 800,
                "소환 수명 gave " + (world.GetEntity(late).ExpireTick - world.Tick) + " ticks, not 800");
        }

        /// <summary>
        /// A 멸종한 화염 슬라임 spawned on tick 0 lapses on tick 600 alone, and
        /// on tick 1201 with a 층위 관측자 three cells off: ExpireTick +1 on
        /// every even tick before the check, so it stands after 1201 steps and
        /// is gone after 1202.
        /// </summary>
        private static void StrataHoldHalvesTheClock()
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            int slime = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            world.SpawnUnit(0, Role.Signature, 2, At(33, 30));
            Run(world, 1201);
            Check(world.GetEntity(slime).Alive, "under 층위 고정 the slime lapsed before tick 1201");
            Check(world.GetEntity(slime).ExpireTick == 1201,
                "under 층위 고정 the slime lapses on " + world.GetEntity(slime).ExpireTick + ", not 1201");
            Step(world);
            Check(!world.GetEntity(slime).Alive, "under 층위 고정 the slime outlived tick 1201");
        }

        /// <summary>
        /// Over 100 steps from an even tick, a held slime gains 50 ticks. Two
        /// observers give the same 50, not 100. An observer five cells off, an
        /// enemy observer, and a permanent body give nothing. 층위 고정 반경
        /// reaches the slime five cells off.
        /// </summary>
        private static void StrataHoldReachesAlliedTimedBodiesOnce()
        {
            Check(Held(observers: new[] { 3 }, enemy: false, research: false) == 50, "one observer did not hold the clock 50 ticks in 100");
            Check(Held(observers: new[] { 3, -3 }, enemy: false, research: false) == 50, "two observers held the clock twice");
            Check(Held(observers: new[] { 5 }, enemy: false, research: false) == 0, "an observer five cells off held the clock");
            Check(Held(observers: new[] { 3 }, enemy: true, research: false) == 0, "an enemy observer held the clock");
            Check(Held(observers: new[] { 5 }, enemy: false, research: true) == 50, "층위 고정 반경 did not reach five cells");
            Check(Held(observers: new[] { 7 }, enemy: false, research: true) == 0, "층위 고정 반경 reached seven cells");

            World world = Two(Faction.Driftworlds, Faction.Driftworlds);
            int storm = world.SpawnUnit(0, Role.Melee, 0, At(30, 30));
            world.SpawnUnit(0, Role.Signature, 2, At(32, 30));
            Run(world, 10);
            Check(world.GetEntity(storm).ExpireTick == 0, "층위 고정 gave a permanent 폭풍편 an ExpireTick");
        }

        /// <summary>How far 100 steps from an even tick push a slime's ExpireTick, with observers this many cells east.</summary>
        private static int Held(int[] observers, bool enemy, bool research)
        {
            World world = Two(Faction.Driftworlds, Faction.Driftworlds);
            if (research) Research(world, 0, UpgradeEffect.StrataHoldRadius);
            while (world.Tick % 2 != 0) Step(world);
            int slime = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            foreach (int dx in observers) world.SpawnUnit(enemy ? 1 : 0, Role.Signature, 2, At(30 + dx, 30));
            int start = world.GetEntity(slime).ExpireTick;
            Run(world, 100);
            Check(world.GetEntity(slime).Alive, "the slime under test died, so the check proves nothing");
            return world.GetEntity(slime).ExpireTick - start;
        }

        // ---------------------------------------------------------------------
        // Bursts

        /// <summary>
        /// A 균열 파수병 (id 0) kills a 찌릿쥐 (id 1, 1 hp) in CombatSystem. The
        /// 화염 악마 beside the 찌릿쥐 (id 2, 5 hp) still takes its own turn in
        /// that pass and hits the worker behind it, and only then dies to the
        /// burst: had Kill landed the burst, it would have died before its turn.
        /// The burst takes exactly 12 off a hostile worker and a hostile flier
        /// within 2 cells, and nothing off the shooter 4 cells off, an allied
        /// body, or a hostile building.
        /// </summary>
        private static void ADeathBurstLandsInBurstSystemNotInKill()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            int shooter = world.SpawnUnit(1, Role.Ranged, 1, At(26, 30));
            int zap = world.SpawnUnit(0, Role.Melee, 2, At(30, 30), hpOverride: 1);
            int demon = world.SpawnUnit(1, Role.Melee, 1, At(31, 30), hpOverride: 5);
            int ally = world.SpawnWorker(0, At(32, 30));
            int worker = world.SpawnWorker(1, At(30, 31));
            int flier = world.SpawnUnit(1, Role.Signature, 0, At(30, 29));
            int building = world.SpawnBuilding(1, Role.Supply, At(29, 31), complete: true);
            int allyHp = world.GetEntity(ally).Hp, workerHp = world.GetEntity(worker).Hp;
            int flierHp = world.GetEntity(flier).Hp, buildingHp = world.GetEntity(building).Hp;

            Step(world);
            Check(!world.GetEntity(zap).Alive, "the 균열 파수병 never killed the 찌릿쥐, so the check proves nothing");
            Check(world.GetEntity(ally).Hp < allyHp, "the 화염 악마 beside the 찌릿쥐 died before its own turn: the burst landed inside Kill");
            Check(!world.GetEntity(demon).Alive, "the burst did not kill the 5 hp 화염 악마 2 cells off");
            Check(workerHp - world.GetEntity(worker).Hp == 12, "the burst took " + (workerHp - world.GetEntity(worker).Hp) + " off a hostile worker, not 12");
            Check(flierHp - world.GetEntity(flier).Hp == 12, "the burst took " + (flierHp - world.GetEntity(flier).Hp) + " off a hostile flier, not 12");
            Check(world.GetEntity(building).Hp == buildingHp, "the burst hit a building");
            Check(world.GetEntity(shooter).Hp == world.GetEntity(shooter).MaxHp, "the burst reached 4 cells");
        }

        /// <summary>
        /// A 멸종한 화염 슬라임 that lapses bursts the way a killed one does,
        /// before any weapon fires that tick: 12 off a hostile ground worker a
        /// cell off, nothing off a hostile flier (화염 분출 is 지상), an allied
        /// worker, or a hostile worker three cells off.
        /// </summary>
        private static void ALapseBurstsLikeADeath()
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            Step(world);
            int slime = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            int near = world.SpawnWorker(1, At(31, 30));
            int far = world.SpawnWorker(1, At(33, 30));
            int flier = world.SpawnUnit(1, Role.Signature, 0, At(30, 31));
            int ally = world.SpawnWorker(0, At(29, 30));
            world.SetExpireTickForScenario(slime, world.Tick);
            int nearHp = world.GetEntity(near).Hp;

            Step(world);
            Check(!world.GetEntity(slime).Alive, "the slime did not lapse on its tick");
            Check(nearHp - world.GetEntity(near).Hp == 12, "화염 분출 took " + (nearHp - world.GetEntity(near).Hp) + " off a ground body a cell off, not 12");
            Check(world.GetEntity(far).Hp == world.GetEntity(far).MaxHp, "화염 분출 reached three cells");
            Check(world.GetEntity(flier).Hp == world.GetEntity(flier).MaxHp, "화염 분출 hit a flier");
            Check(world.GetEntity(ally).Hp == world.GetEntity(ally).MaxHp, "화염 분출 hit an ally");
        }

        /// <summary>
        /// docs/TECH-TREE.md 피해 계산: a burst is fixed damage. A 멸종한 바위
        /// 슬라임 standing still (굳은 몸 +3) under 방어력 1 loses the whole 12.
        /// </summary>
        private static void ABurstIgnoresArmor()
        {
            World world = Two(Faction.TreeSpirits, Faction.Driftworlds);
            Research(world, 1, UpgradeEffect.Armor);
            int zap = world.SpawnUnit(0, Role.Melee, 2, At(30, 30));
            int rock = world.SpawnUnit(1, Role.Melee, 2, At(31, 30));
            world.SetExpireTickForScenario(zap, world.Tick);
            int hp = world.GetEntity(rock).Hp;
            Check(world.ArmorBonus(1) > 0, "방어력 did not land, so the check proves nothing");
            Step(world);
            Check(!world.GetEntity(zap).Alive, "the 찌릿쥐 did not lapse on its tick");
            Check(hp - world.GetEntity(rock).Hp == 12, "armor took the burst on a still 바위 슬라임 down to " + (hp - world.GetEntity(rock).Hp));
        }

        /// <summary>
        /// Four 찌릿쥐 on a line two cells apart, sides alternating, the first at
        /// 1 hp and the rest at 11. The first dies in combat; its burst kills the
        /// second, whose burst kills the third, whose burst kills the fourth, all
        /// inside the one step. With the first at 100 hp the same combat kills
        /// nobody, so every death above is the chain's.
        /// </summary>
        private static void ChainedDeathsLandInTheSameTick()
        {
            int[] chain = Line(firstHp: 1, out World world);
            Step(world);
            for (int i = 0; i < chain.Length; i++)
                Check(!world.GetEntity(chain[i]).Alive, "찌릿쥐 " + i + " of the chain stands after the step");

            int[] control = Line(firstHp: 100, out World calm);
            Step(calm);
            for (int i = 0; i < control.Length; i++)
                Check(calm.GetEntity(control[i]).Alive, "찌릿쥐 " + i + " died to combat alone, so the chain check proves nothing");
        }

        private static int[] Line(int firstHp, out World world)
        {
            world = Two(Faction.TreeSpirits, Faction.TreeSpirits);
            var ids = new int[4];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = world.SpawnUnit(i % 2, Role.Melee, 2, At(30 + 2 * i, 30), hpOverride: i == 0 ? firstHp : 11);
            return ids;
        }

        /// <summary>
        /// A 멸종한 번개 슬라임 off a 통로 aimed at no anchor stands at the 통로
        /// and bursts there on its arrival tick. Against the same layout with a
        /// 틈새 사수 coming off instead (the same weapon, no burst), every hostile
        /// body within 2 cells of where it stands, flier included, loses 10 more
        /// on the arrival tick, and nothing else loses anything more.
        /// </summary>
        private static void AnArrivalDischargesWhereTheBodyStands()
        {
            int[] slime = ArrivalLosses(Role.Ranged, 1, out FixVec2 stood);
            int[] marksman = ArrivalLosses(Role.Ranged, 0, out FixVec2 stoodToo);
            Check(stood.Equals(stoodToo), "the two bodies did not stand on the same spot");
            int[] want = { 10, 10, 0, 0 };
            for (int i = 0; i < want.Length; i++)
                Check(slime[i] - marksman[i] == want[i],
                    "도착 방전 took " + (slime[i] - marksman[i]) + " more off body " + i + ", not " + want[i]);
        }

        /// <summary>
        /// The hp each of four bodies lost on the tick this entry came off the
        /// 통로: a hostile worker and a hostile flier a cell from the rally spot,
        /// a hostile worker four cells off, an allied worker a cell off.
        /// </summary>
        private static int[] ArrivalLosses(Role role, int slot, out FixVec2 stood)
        {
            World world = DriftBase(out int passage);
            FixVec2 spot = world.GetEntity(passage).Position + World.RallyOffset;
            Fix one = Fix.FromInt(1);
            var bodies = new[]
            {
                world.SpawnWorker(1, new FixVec2(spot.X, spot.Y + one)),
                world.SpawnUnit(1, Role.Signature, 0, new FixVec2(spot.X + one, spot.Y)),
                world.SpawnWorker(1, new FixVec2(spot.X + Fix.FromInt(4), spot.Y)),
                world.SpawnWorker(0, new FixVec2(spot.X, spot.Y - one)),
            };
            int born = world.EntityCount;
            Step(world, new List<Command>
            {
                new Command(0, 0, 1, CommandType.Produce, passage, FixVec2.Zero, Command.RosterArg(role, slot))
            });
            Check(world.GetEntity(passage).QueueCount == 1, role + " " + slot + " was not queued");
            var before = new int[bodies.Length];
            while (world.EntityCount == born && world.Tick < 400)
            {
                for (int i = 0; i < bodies.Length; i++) before[i] = world.GetEntity(bodies[i]).Hp;
                Step(world);
            }
            Check(world.EntityCount == born + 1, role + " " + slot + " never came off the 통로");
            stood = world.GetEntity(born).Position;
            Check(stood.Equals(spot), "the body aimed at no anchor did not stand at the 통로");
            var lost = new int[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) lost[i] = before[i] - world.GetEntity(bodies[i]).Hp;
            return lost;
        }

        /// <summary>과부하 강화 and 화염 분출 강화 take 18 off where 12 was taken.</summary>
        private static void BurstResearchRaisesTheAmount()
        {
            Check(BurstLoss(Faction.TreeSpirits, Role.Melee, 2, UpgradeEffect.OverloadDamage) == 18, "과부하 강화 did not make 전격 과부하 18");
            Check(BurstLoss(Faction.Driftworlds, Role.Melee, 1, UpgradeEffect.FireBurstDamage) == 18, "화염 분출 강화 did not make 화염 분출 18");
            Check(BurstLoss(Faction.TreeSpirits, Role.Melee, 2, UpgradeEffect.None) == 12, "전격 과부하 is not 12 unresearched");
        }

        private static int BurstLoss(Faction faction, Role role, int slot, UpgradeEffect research)
        {
            World world = Two(faction, Faction.Hellfire);
            if (research != UpgradeEffect.None) Research(world, 0, research);
            else Step(world);
            int source = world.SpawnUnit(0, role, slot, At(30, 30));
            int target = world.SpawnWorker(1, At(31, 30));
            world.SetExpireTickForScenario(source, world.Tick);
            int hp = world.GetEntity(target).Hp;
            Step(world);
            Check(!world.GetEntity(source).Alive, "the burst source did not lapse on its tick");
            return hp - world.GetEntity(target).Hp;
        }

        /// <summary>
        /// The mixed match below, 800 ticks: Step asserts the queue is empty
        /// after every one of them, and the match has to have had bursts and
        /// lapses for that to mean anything.
        /// </summary>
        private static void TheQueueIsEmptyAtEveryTickEnd()
        {
            World world = MixedWorld(out int zaps, out int slimes);
            Run(world, 800);
            int dead = 0;
            for (int i = zaps; i < zaps + 6; i++) if (!world.GetEntity(i).Alive) dead++;
            Check(dead > 0, "no 찌릿쥐 died in the mixed match, so the check proves nothing");
            for (int i = slimes; i < slimes + 3; i++)
                Check(!world.GetEntity(i).Alive, "a 멸종 슬라임 outlived the mixed match");
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>The mixed match twice from scratch. Same hash at the end.</summary>
        private static void TwoWorldsAgree()
        {
            ulong a = Mixed(), b = Mixed();
            Check(a == b, "two runs of the batch 6 summons and bursts hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
        }

        private static ulong Mixed()
        {
            World world = MixedWorld(out _, out _);
            Run(world, 800);
            return world.Hash();
        }

        /// <summary>
        /// 세계수 정령 peer 0 with six 찌릿쥐, 차원 유랑종 peer 1 with a 통로 that
        /// lets a 멸종한 번개 슬라임 through, two 멸종한 화염 슬라임, a 멸종한
        /// 바위 슬라임 and a 층위 관측자 beside them, close enough to fight.
        /// </summary>
        private static World MixedWorld(out int zaps, out int slimes)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SetPeerFaction(1, Faction.Driftworlds);
            world.GrantResources(1, 10000);
            world.SpawnBuilding(1, Role.Base, At(50, 30), complete: true);
            int passage = world.SpawnBuilding(1, Role.Production, 0, At(40, 30), complete: true);
            world.SpawnBuilding(1, Role.Tech, 0, At(50, 34), complete: true);
            zaps = world.EntityCount;
            for (int i = 0; i < 6; i++) world.SpawnUnit(0, Role.Melee, 2, At(30, 26 + 2 * i));
            slimes = world.EntityCount;
            world.SpawnUnit(1, Role.Melee, 1, At(36, 29));
            world.SpawnUnit(1, Role.Melee, 1, At(36, 31));
            world.SpawnUnit(1, Role.Melee, 2, At(36, 33));
            world.SpawnUnit(1, Role.Signature, 2, At(38, 31));
            Step(world, new List<Command>
            {
                new Command(0, 1, 1, CommandType.Produce, passage, FixVec2.Zero, Command.RosterArg(Role.Ranged, 1))
            });
            return world;
        }

        /// <summary>
        /// Two worlds identical but for one body's ExpireTick hash apart, and so
        /// do two identical but for a body that lapses and one that does not.
        /// </summary>
        private static void TheHashSeesExpireTick()
        {
            World plain = Two(Faction.Driftworlds, Faction.Hellfire);
            World moved = Two(Faction.Driftworlds, Faction.Hellfire);
            World permanent = Two(Faction.Driftworlds, Faction.Hellfire);
            int a = plain.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            int b = moved.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            int c = permanent.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            moved.SetExpireTickForScenario(b, Lifetime + 1);
            permanent.SetExpireTickForScenario(c, 0);
            Check(plain.GetEntity(a).Hp == moved.GetEntity(b).Hp, "the worlds differ in hp, so the check proves nothing");
            Check(plain.Hash() != moved.Hash(), "ExpireTick is not hashed");
            Check(plain.Hash() != permanent.Hash(), "a body that lapses hashes like one that does not");
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        /// <summary>One step, then the rule the queue is never hashed on: it is empty.</summary>
        private static void Step(World world, List<Command> commands = null)
        {
            world.Step(commands ?? Idle);
            Check(world.PendingBurstCount == 0,
                world.PendingBurstCount + " bursts were still queued at the end of tick " + (world.Tick - 1));
        }

        private static void Run(World world, int ticks)
        {
            for (int t = 0; t < ticks; t++) Step(world);
        }

        /// <summary>Peer 0 and peer 1 of these factions, nothing standing.</summary>
        private static World Two(Faction zero, Faction one)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, zero);
            world.SetPeerFaction(1, one);
            return world;
        }

        /// <summary>
        /// 차원 유랑종 peer 0 with a base, a 통로 and a 층위 관측대 standing, which
        /// is what lets the 멸종 슬라임 through, and the bank to pay for them. 지옥불
        /// peer 1 with nothing.
        /// </summary>
        private static World DriftBase(out int passage)
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);
            passage = world.SpawnBuilding(0, Role.Production, 0, At(20, 20), complete: true);
            world.SpawnBuilding(0, Role.Tech, 0, At(13, 5), complete: true);
            return world;
        }

        /// <summary>
        /// Raises this peer's row with this effect to level 1 in a corner of the
        /// map, the way StatusChecks does: the base, production 0, tech 0, the
        /// row's research building and the building it requires, all finished,
        /// then the research run to its end. Bodies under test go far from it.
        /// </summary>
        private static void Research(World world, int peer, UpgradeEffect effect)
        {
            Faction faction = world.FactionOf(peer);
            int index = -1;
            for (int i = 0; i < FactionData.UpgradeCount(faction); i++)
            {
                if (FactionData.Upgrade(faction, i).Effect == effect) index = i;
            }
            Check(index >= 0, faction + " has no " + effect + " row");
            UpgradeRow row = FactionData.Upgrade(faction, index);
            int y = 3 + peer * 4;
            world.GrantResources(peer, 10000);
            world.SpawnBuilding(peer, Role.Base, At(3, y), complete: true);
            world.SpawnBuilding(peer, Role.Production, 0, At(7, y), complete: true);
            world.SpawnBuilding(peer, Role.Tech, 0, At(11, y), complete: true);
            if (row.Requires.Role != Role.None)
                world.SpawnBuilding(peer, row.Requires.Role, row.Requires.Slot, At(19, y), complete: true);
            int at = world.SpawnBuilding(peer, row.ResearchAt.Role, row.ResearchAt.Slot, At(15, y), complete: true);
            Step(world, new List<Command>
            {
                new Command(0, peer, 1000 + world.Tick, CommandType.Research, at, FixVec2.Zero, index)
            });
            Check(world.GetEntity(at).ResearchIndex == index, effect + " research was refused");
            Run(world, row.Ticks - 1);
            Check(world.UpgradeLevel(peer, index) == 1, effect + " research never landed");
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
