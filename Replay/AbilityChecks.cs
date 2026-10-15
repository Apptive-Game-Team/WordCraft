using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #203's checks: docs/TECH-TREE.md 묶음 7, the autocasts and the area
    /// hits. The ability rows themselves are pinned in StatusChecks' table
    /// beside the 묶음 5 and 6 rows; what is checked here is what they do. The
    /// water field has its own file, WaterFieldChecks.
    ///
    /// Every step taken here goes through <see cref="Step"/>, which asserts the
    /// burst queue is empty when the step returns, as SummonChecks does: 굴러
    /// 들이받기 and the area hits kill inside AbilitySystem and CombatSystem,
    /// and a 찌릿쥐 they kill has to burst in the same tick.
    /// </summary>
    internal static class AbilityChecks
    {
        private const ulong Seed = 0x5749;

        public static void Check()
        {
            EveryEntryHoldsOneAbilityWithStateAtMost();
            GaleRunsTenTicksFromEveryAttack();
            GaleIsOffBeforeItsResearch();
            ARollStartsThreeToSixCellsOut();
            ARollEndsAtTwoCellsOrTwelveTicks();
            ARollEndsWithAFixedHitAroundTheRoller();
            RollChargeIsOffBeforeItsResearch();
            AreaHitsSplashHalfOnTheGround();
            BombardmentSplashesLikeTheCatapult();
            RemnantThrowConsumesTheLowestCellWithinTwo();
            RemnantThrowResearchRaisesTheBonus();
            RemnantRepairMendsTheMostWoundedAndConsumes();
            RemnantRepairNeedsBothAWoundAndDebris();
            SteadyAimAddsTwoAfterFortyStillTicks();
            RepairMendsTheMostWoundedBuildingOrDevice();
            TheRingHoldsDebrisInItsSquare();
            KillClearsTheAbilityClocks();
            TwoWorldsAgree();
            TheHashSeesAbilityCooldown();
            TheHashSeesAbilityTicksLeft();
        }

        // ---------------------------------------------------------------------
        // The table

        /// <summary>
        /// docs/TECH-TREE.md 능력 공통 규칙: an entry holds at most one ability
        /// with state, and only the two kinds that wait on AbilityCooldown carry
        /// a cooldown: 잔해 수리 100 and 굴러 들이받기 240.
        /// </summary>
        private static void EveryEntryHoldsOneAbilityWithStateAtMost()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                for (int r = 1; r < FactionData.RoleCount; r++)
                {
                    var role = (Role)r;
                    for (int slot = 0; slot < FactionData.SlotCount(faction, role); slot++)
                    {
                        int stateful = 0;
                        for (int i = 0; i < FactionData.AbilityCount(faction); i++)
                        {
                            AbilityRow row = FactionData.Ability(faction, i);
                            if (row.Role == role && row.Slot == slot && FactionData.IsStateful(row.Kind)) stateful++;
                        }
                        Check(stateful <= 1, faction + " " + role + " " + slot + " holds " + stateful + " abilities with state");
                    }
                }
            }
            for (int i = 0; i < FactionData.AbilityRowCount; i++)
            {
                AbilityRow row = FactionData.AbilityRowAt(i);
                Check(FactionData.HasCooldown(row.Kind) == (row.Cooldown > 0), row.Name + " has a cooldown of " + row.Cooldown);
            }
        }

        // ---------------------------------------------------------------------
        // 질풍

        /// <summary>
        /// A 폭풍 기수 under 질풍 beside an enemy worker: on every tick it
        /// attacks, AbilityTicksLeft is 10, and it is back to 0 ten ticks later,
        /// before the next attack 15 ticks on. Ordered away on the tick after an
        /// attack, it walks 3/4 a tick for nine ticks and then 3/8 (but for the
        /// tick that lands on the route's first waypoint).
        /// </summary>
        private static void GaleRunsTenTicksFromEveryAttack()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.Gale);
            int rider = world.SpawnUnit(0, Role.Melee, 3, At(30, 30));
            world.SpawnWorker(1, At(31, 30));
            int attacks = 0;
            int sinceAttack = -1;
            for (int t = 0; t < 60; t++)
            {
                Step(world);
                Entity e = world.GetEntity(rider);
                if (e.AttackCooldown == 15)
                {
                    Check(e.AbilityTicksLeft == 10, "질풍 is " + e.AbilityTicksLeft + " on the tick of an attack, not 10");
                    attacks++;
                    sinceAttack = 0;
                    continue;
                }
                if (sinceAttack < 0) continue;
                sinceAttack++;
                int want = sinceAttack >= 10 ? 0 : 10 - sinceAttack;
                Check(e.AbilityTicksLeft == want,
                    "질풍 is " + e.AbilityTicksLeft + " " + sinceAttack + " ticks after an attack, not " + want);
            }
            Check(attacks >= 3, "the rider attacked " + attacks + " times in 60 ticks, so the check proves little");

            World walk = Two(Faction.TreeSpirits, Faction.Hellfire);
            Research(walk, 0, UpgradeEffect.Gale);
            int fast = walk.SpawnUnit(0, Role.Melee, 3, At(30, 30));
            walk.SpawnWorker(1, At(31, 30));
            while (walk.GetEntity(fast).AttackCooldown != 15) Step(walk);
            var steps = new List<long>();
            FixVec2 from = walk.GetEntity(fast).Position;
            Step(walk, Order(walk, 0, CommandType.Move, fast, At(10, 30)));
            steps.Add((walk.GetEntity(fast).Position - from).Magnitude.Raw);
            steps.AddRange(Steps(walk, fast, 10));
            long galloping = Fix.Ratio(3, 4).Raw, trotting = Fix.Ratio(3, 8).Raw;
            // Step 1 ends on the route's first waypoint, the neighbouring cell
            // the pathfinder always keeps, and is cut short there.
            for (int i = 0; i < 9; i++)
                if (i != 1) Check(steps[i] == galloping, "질풍 step " + i + " covered " + steps[i] + " raw, not 3/4 (" + galloping + ")");
            Check(steps[9] == trotting, "the step after 질풍 ran out covered " + steps[9] + " raw, not 3/8 (" + trotting + ")");
        }

        /// <summary>The same rider without the research: the clock never leaves 0.</summary>
        private static void GaleIsOffBeforeItsResearch()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            int rider = world.SpawnUnit(0, Role.Melee, 3, At(30, 30));
            world.SpawnWorker(1, At(31, 30));
            bool attacked = false;
            for (int t = 0; t < 40; t++)
            {
                Step(world);
                Entity e = world.GetEntity(rider);
                attacked |= e.AttackCooldown == 15;
                Check(e.AbilityTicksLeft == 0, "질풍 ran before its research");
            }
            Check(attacked, "the rider never attacked, so the check proves nothing");
        }

        // ---------------------------------------------------------------------
        // 굴러 들이받기

        /// <summary>
        /// A 구르는 바위 under the research chasing an enemy worker eight cells
        /// off starts its roll on the first tick the worker is within 6 cells,
        /// with the cooldown at 240 and 12 ticks on the clock; a worker 2.5
        /// cells off never starts one.
        /// </summary>
        private static void ARollStartsThreeToSixCellsOut()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.RollCharge);
            int roller = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            int prey = world.SpawnWorker(1, At(38, 30));
            int started = -1;
            for (int t = 0; t < 40 && started < 0; t++)
            {
                Fix before = Distance(world, roller, prey);
                Step(world);
                Entity e = world.GetEntity(roller);
                if (e.AbilityTicksLeft == 0) continue;
                started = t;
                Check(before <= Fix.FromInt(6) && before > Fix.Ratio(11, 2),
                    "the roll started " + before + " cells out, not on the first tick within 6");
                Check(e.AbilityTicksLeft == 12, "the roll started with " + e.AbilityTicksLeft + " ticks, not 12");
                Check(e.AbilityCooldown == 240, "the roll started a cooldown of " + e.AbilityCooldown + ", not 240");
            }
            Check(started >= 0, "the roller never rolled at a worker closing from eight cells");

            World near = Two(Faction.RockGolems, Faction.Hellfire);
            Research(near, 0, UpgradeEffect.RollCharge);
            int close = near.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            near.SpawnWorker(1, new FixVec2(Fix.FromInt(33), Fix.Ratio(61, 2)));
            for (int t = 0; t < 30; t++)
            {
                Step(near);
                Check(near.GetEntity(close).AbilityTicksLeft == 0, "the roller rolled at a worker under 3 cells off");
            }
        }

        /// <summary>
        /// Mid-roll, the roller walks 1/2 a tick, and the roll ends on the
        /// first tick its target is within 2 cells. A roll at a target nine
        /// cells off never gets that close and ends after twelve ticks, six
        /// cells on.
        /// </summary>
        private static void ARollEndsAtTwoCellsOrTwelveTicks()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.RollCharge);
            int roller = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            int prey = world.SpawnWorker(1, At(35, 30));
            Step(world);
            Step(world);
            Check(world.GetEntity(roller).AbilityTicksLeft == 12, "the roll did not start at a worker five cells off");
            long rolling = Fix.Ratio(1, 2).Raw;
            for (int t = 0; t < 12; t++)
            {
                bool within = Distance(world, roller, prey) <= Fix.FromInt(2);
                FixVec2 from = world.GetEntity(roller).Position;
                Step(world);
                Entity e = world.GetEntity(roller);
                if (within)
                {
                    Check(e.AbilityTicksLeft == 0, "the roll went on with its target within 2 cells");
                    break;
                }
                Check(e.AbilityTicksLeft > 0 || t == 11, "the roll ended with its target " + Distance(world, roller, prey) + " cells off");
                Check(e.AbilityTicksLeft == 0 || (e.Position - from).Magnitude.Raw >= rolling - 2,
                    "a rolling step covered " + (e.Position - from).Magnitude.Raw + " raw, not 1/2 (" + rolling + ")");
            }
            Check(world.GetEntity(roller).AbilityTicksLeft == 0, "the roll outlasted its target coming within 2 cells");

            World far = Two(Faction.RockGolems, Faction.Hellfire);
            Research(far, 0, UpgradeEffect.RollCharge);
            int long_ = far.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            far.SpawnWorker(1, At(39, 30));
            Step(far);
            far.SetAbilityForScenario(long_, 200, 12);
            FixVec2 start = far.GetEntity(long_).Position;
            for (int t = 0; t < 11; t++)
            {
                Step(far);
                Check(far.GetEntity(long_).AbilityTicksLeft == 11 - t, "the long roll has " + far.GetEntity(long_).AbilityTicksLeft + " ticks after " + (t + 1));
            }
            Fix covered = (far.GetEntity(long_).Position - start).Magnitude;
            Check(covered >= Fix.Ratio(11, 2) - Fix.Ratio(1, 64) && covered <= Fix.Ratio(11, 2) + Fix.Ratio(1, 64),
                "eleven ticks of rolling covered " + covered + " cells, not 11/2");
            Step(far);
            Check(far.GetEntity(long_).AbilityTicksLeft == 0, "the roll outlived twelve ticks");
        }

        /// <summary>
        /// The tick a roll ends, every enemy ground body within 1 cell of the
        /// roller takes 15 once, through armor (a 꼬마돌 worker has armor 1),
        /// and nothing else does: a worker 1.5 cells off, an allied worker, an
        /// enemy flier. The roll here ends because it has no target.
        /// </summary>
        private static void ARollEndsWithAFixedHitAroundTheRoller()
        {
            World world = Two(Faction.RockGolems, Faction.RockGolems);
            Research(world, 0, UpgradeEffect.RollCharge);
            int roller = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            FixVec2 at = world.GetEntity(roller).Position;
            int nearest = world.SpawnWorker(1, at + new FixVec2(Fix.Ratio(1, 2), Fix.Zero));
            int beside = world.SpawnWorker(1, at + new FixVec2(Fix.Zero, Fix.Ratio(9, 10)));
            int outside = world.SpawnWorker(1, at + new FixVec2(-Fix.Ratio(3, 2), Fix.Zero));
            int ally = world.SpawnWorker(0, at + new FixVec2(Fix.Zero, -Fix.Ratio(9, 10)));
            int flier = world.SpawnUnit(1, Role.Ranged, 2, at + new FixVec2(-Fix.Ratio(1, 2), Fix.Ratio(1, 2)));
            Check(world.Flies(world.GetEntity(flier)), "부유석 파수 does not fly, so the check proves nothing");
            world.SetAbilityForScenario(roller, 200, 5);
            int[] ids = { beside, outside, ally, flier };
            var hp = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++) hp[i] = world.GetEntity(ids[i]).Hp;
            Step(world);
            Check(world.GetEntity(roller).AbilityTicksLeft == 0, "a roll with no target went on");
            Check(world.GetEntity(beside).Hp == hp[0] - 15,
                "the armored worker 9/10 cells off lost " + (hp[0] - world.GetEntity(beside).Hp) + ", not 15");
            Check(world.GetEntity(outside).Hp == hp[1], "the impact reached 3/2 cells");
            Check(world.GetEntity(ally).Hp == hp[2], "the impact hit an ally");
            Check(world.GetEntity(flier).Hp == hp[3], "the impact hit a flier");
            Check(world.GetEntity(nearest).Hp <= world.GetEntity(nearest).MaxHp - 15, "the nearest worker escaped the impact");
        }

        /// <summary>Without the research the roller walks at its own pace and never rolls.</summary>
        private static void RollChargeIsOffBeforeItsResearch()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            int roller = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            world.SpawnWorker(1, At(35, 30));
            for (int t = 0; t < 30; t++)
            {
                Step(world);
                Check(world.GetEntity(roller).AbilityTicksLeft == 0 && world.GetEntity(roller).AbilityCooldown == 0,
                    "굴러 들이받기 ran before its research");
            }
        }

        // ---------------------------------------------------------------------
        // 광역

        /// <summary>
        /// A 용암 투석 악마's first shot (16, armor 0) at a worker 8 cells off:
        /// the worker loses 16, a worker 1 cell from it and a worker and a
        /// finished building 2 cells from it lose 8, a worker sqrt 5 off loses
        /// nothing, and neither an allied worker nor an enemy flier beside the
        /// target is touched.
        /// </summary>
        private static void AreaHitsSplashHalfOnTheGround()
        {
            World world = Two(Faction.Hellfire, Faction.TreeSpirits);
            world.SpawnUnit(0, Role.Ranged, 2, At(30, 30));
            int target = world.SpawnWorker(1, At(38, 30));
            int one = world.SpawnWorker(1, At(39, 30));
            int two = world.SpawnWorker(1, At(38, 32));
            int building = world.SpawnBuilding(1, Role.Supply, 0, At(38, 28), complete: true);
            int outside = world.SpawnWorker(1, At(40, 31));
            int ally = world.SpawnWorker(0, At(39, 31));
            int flier = world.SpawnUnit(1, Role.Signature, 1, At(37, 31));
            Check(world.Flies(world.GetEntity(flier)), "도깨비불 does not fly, so the check proves nothing");
            int[] ids = { target, one, two, building, outside, ally, flier };
            int[] want = { 16, 8, 8, 8, 0, 0, 0 };
            Losses(world, ids, want, "용암 폭발");
        }

        /// <summary>
        /// A 마도 폭격선 (14, ground only) at a worker 3 cells off: 14 on the
        /// worker, 7 on a worker beside it, nothing on an enemy flier beside it
        /// or a worker 3 cells from it.
        /// </summary>
        private static void BombardmentSplashesLikeTheCatapult()
        {
            World world = Two(Faction.Humans, Faction.TreeSpirits);
            world.SpawnUnit(0, Role.Ranged, 5, At(30, 30));
            int target = world.SpawnWorker(1, At(33, 30));
            int beside = world.SpawnWorker(1, At(34, 31));
            int flier = world.SpawnUnit(1, Role.Signature, 1, At(33, 31));
            int far = world.SpawnWorker(1, At(36, 30));
            Losses(world, new[] { target, beside, flier, far }, new[] { 14, 7, 0, 0 }, "폭격");
        }

        // ---------------------------------------------------------------------
        // 잔해 투척

        /// <summary>
        /// A 바위 술사 (7) shooting an enemy worker 4 cells off, debris on two
        /// cells within 2 of it and on a lower-index cell 3 off. The first shot
        /// consumes the lower of the two near cells, deals 15 and splashes 7 on
        /// a worker 1 cell from the target and nothing on one 2 cells from it;
        /// the second consumes the other; the third finds none within 2, deals
        /// 7 and splashes nothing, and the cell 3 off still stands.
        /// </summary>
        private static void RemnantThrowConsumesTheLowestCellWithinTwo()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            int mage = world.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int target = world.SpawnWorker(1, At(34, 30));
            int beside = world.SpawnWorker(1, At(34, 31));
            int apart = world.SpawnWorker(1, At(34, 32));
            int low = Cell(30, 29), high = Cell(31, 30), far = Cell(30, 27);
            foreach (int c in new[] { low, high, far }) world.SetRemnantForScenario(c, 5000);

            Losses(world, new[] { target, beside, apart }, new[] { 15, 7, 0 }, "the first 잔해 투척");
            Check(world.RemnantExpiry(low) == 0, "the first throw left the lowest cell");
            Check(world.RemnantExpiry(high) == 5000, "the first throw took a second cell");
            Check(world.RemnantExpiry(far) == 5000, "the throw took debris 3 cells off");

            ShotFrom(world, mage);
            Check(world.RemnantExpiry(high) == 0, "the second throw left the other near cell");
            Check(world.GetEntity(beside).Hp == world.GetEntity(beside).MaxHp - 14, "the second throw did not splash 7");

            int before = world.GetEntity(target).Hp, besideBefore = world.GetEntity(beside).Hp;
            ShotFrom(world, mage);
            Check(before - world.GetEntity(target).Hp == 7, "a shot with no debris near dealt " + (before - world.GetEntity(target).Hp) + ", not 7");
            Check(world.GetEntity(beside).Hp == besideBefore, "a shot with no debris near splashed");
            Check(world.RemnantExpiry(far) == 5000, "a shot took debris 3 cells off");
            Check(world.GetEntity(mage).Alive, "the mage fell, so the check proves nothing");
        }

        /// <summary>잔해 투척 강화: the shot deals 19 and splashes 9.</summary>
        private static void RemnantThrowResearchRaisesTheBonus()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.RemnantThrowDamage);
            world.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int target = world.SpawnWorker(1, At(34, 30));
            int beside = world.SpawnWorker(1, At(34, 31));
            world.SetRemnantForScenario(Cell(31, 30), world.Tick + 5000);
            Losses(world, new[] { target, beside }, new[] { 19, 9 }, "잔해 투척 강화");
        }

        // ---------------------------------------------------------------------
        // 잔해 수리

        /// <summary>
        /// A 바위 술사 under the research, debris a cell off, three allied
        /// golems: one 40 hp down 3 cells off, one 50 down 3 cells off, one 80
        /// down 5 cells off. On the first tick the 50-down golem is healed 60 up
        /// to its MaxHp, the debris is gone and the cooldown is 100; the 40-down
        /// golem is next, and only on the tick the cooldown is back at 0.
        /// </summary>
        private static void RemnantRepairMendsTheMostWoundedAndConsumes()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.RemnantRepair);
            int mage = world.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int forty = world.SpawnUnit(0, Role.Melee, 0, At(33, 30));
            int fifty = world.SpawnUnit(0, Role.Melee, 0, At(30, 33));
            int eighty = world.SpawnUnit(0, Role.Melee, 0, At(35, 30));
            world.SetHpForScenario(forty, world.GetEntity(forty).MaxHp - 40);
            world.SetHpForScenario(fifty, world.GetEntity(fifty).MaxHp - 50);
            world.SetHpForScenario(eighty, world.GetEntity(eighty).MaxHp - 80);
            world.SetRemnantForScenario(Cell(29, 30), world.Tick + 5000);
            world.SetRemnantForScenario(Cell(29, 31), world.Tick + 5000);

            Step(world);
            Check(world.GetEntity(fifty).Hp == world.GetEntity(fifty).MaxHp, "the most wounded golem was not healed to full");
            Check(world.GetEntity(forty).Hp == world.GetEntity(forty).MaxHp - 40, "a second golem was healed in the same cast");
            Check(world.GetEntity(eighty).Hp == world.GetEntity(eighty).MaxHp - 80, "a golem 5 cells off was healed");
            Check(world.RemnantExpiry(Cell(29, 30)) == 0, "the cast left the lower debris cell");
            Check(world.RemnantExpiry(Cell(29, 31)) > world.Tick, "the cast took two debris cells");
            Check(world.GetEntity(mage).AbilityCooldown == 100, "the cast set a cooldown of " + world.GetEntity(mage).AbilityCooldown);

            for (int t = 0; t < 99; t++)
            {
                Step(world);
                Check(world.GetEntity(forty).Hp == world.GetEntity(forty).MaxHp - 40, "잔해 수리 cast again " + (t + 1) + " ticks after the first");
            }
            Step(world);
            Check(world.GetEntity(forty).Hp == world.GetEntity(forty).MaxHp, "잔해 수리 did not cast 100 ticks after the first");
            Check(world.GetEntity(mage).AbilityCooldown == 100, "the second cast set no cooldown");
        }

        /// <summary>
        /// No cast, no cooldown and no debris taken: before the research, with
        /// the only golem down 29 hp, and with no debris within 2 cells.
        /// </summary>
        private static void RemnantRepairNeedsBothAWoundAndDebris()
        {
            Check(!Repairs(research: false, lost: 50, debrisAt: 1), "잔해 수리 worked before its research");
            Check(!Repairs(research: true, lost: 29, debrisAt: 1), "잔해 수리 mended a golem down 29");
            Check(!Repairs(research: true, lost: 50, debrisAt: 3), "잔해 수리 used debris 3 cells off");
            Check(Repairs(research: true, lost: 30, debrisAt: 2), "잔해 수리 did not mend a golem down 30 with debris 2 cells off");
        }

        private static bool Repairs(bool research, int lost, int debrisAt)
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            if (research) Research(world, 0, UpgradeEffect.RemnantRepair);
            int mage = world.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int golem = world.SpawnUnit(0, Role.Melee, 0, At(33, 30));
            world.SetHpForScenario(golem, world.GetEntity(golem).MaxHp - lost);
            int cell = Cell(30 - debrisAt, 30);
            world.SetRemnantForScenario(cell, world.Tick + 5000);
            Step(world);
            bool healed = world.GetEntity(golem).Hp > world.GetEntity(golem).MaxHp - lost;
            bool consumed = world.RemnantExpiry(cell) == 0;
            bool cooled = world.GetEntity(mage).AbilityCooldown > 0;
            Check(healed == consumed && healed == cooled, "잔해 수리 did one of heal, consume and cool without the others");
            return healed;
        }

        // ---------------------------------------------------------------------
        // 정조준

        /// <summary>
        /// A 틈새 사수 under the research, held in place, an enemy worker 7 cells
        /// off: 39 still ticks reach 6 and the worker is untouched, the 40th
        /// reaches 8 and the worker is shot. The count stops at 40, and a Move
        /// puts it back to 0 and the reach to 6. Without the research the count
        /// never leaves 0.
        /// </summary>
        private static void SteadyAimAddsTwoAfterFortyStillTicks()
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.SteadyAim);
            int marksman = world.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int prey = world.SpawnWorker(1, At(37, 30));
            Step(world, Order(world, 0, CommandType.HoldPosition, marksman, FixVec2.Zero));
            int hp = world.GetEntity(prey).Hp;
            while (world.GetEntity(marksman).AbilityTicksLeft < 39) Step(world);
            Check(world.RangeFor(world.GetEntity(marksman)) == Fix.FromInt(6), "39 still ticks already reach " + world.RangeFor(world.GetEntity(marksman)));
            Check(world.GetEntity(prey).Hp == hp, "the marksman shot 7 cells before 40 still ticks");
            Step(world);
            Check(world.GetEntity(marksman).AbilityTicksLeft == 40, "the count did not reach 40");
            Check(world.RangeFor(world.GetEntity(marksman)) == Fix.FromInt(8), "40 still ticks reach " + world.RangeFor(world.GetEntity(marksman)) + ", not 8");
            Run(world, 5);
            Check(world.GetEntity(prey).Hp < hp, "the marksman never shot 7 cells with its reach at 8");
            Check(world.GetEntity(marksman).AbilityTicksLeft == 40, "the count went past 40");

            Step(world, Order(world, 0, CommandType.Move, marksman, At(30, 40)));
            Step(world);
            Check(world.GetEntity(marksman).AbilityTicksLeft == 0, "walking left the count at " + world.GetEntity(marksman).AbilityTicksLeft);
            Check(world.RangeFor(world.GetEntity(marksman)) == Fix.FromInt(6), "a walking marksman reaches " + world.RangeFor(world.GetEntity(marksman)));

            World locked = Two(Faction.Driftworlds, Faction.Hellfire);
            int plain = locked.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            Run(locked, 60);
            Check(locked.GetEntity(plain).AbilityTicksLeft == 0, "정조준 counted before its research");
            Check(locked.RangeFor(locked.GetEntity(plain)) == Fix.FromInt(6), "정조준 reached further before its research");
        }

        // ---------------------------------------------------------------------
        // 수리

        /// <summary>
        /// A 공방 기술자 among a 대포 down 20, a 석조 보행 포대 down 30, a 석궁
        /// 사수 down 50 (no device), a 견인 대포 down 60 four cells off and a site
        /// under construction. On the tick 10 divides, the 석조 보행 포대 alone
        /// gains 4; on the nine ticks after, nobody; after 수리 강화, 6. The site
        /// matches a world with no technician.
        /// </summary>
        private static void RepairMendsTheMostWoundedBuildingOrDevice()
        {
            Check(RepairStep(research: false) == 4, "수리 did not mend 4");
            Check(RepairStep(research: true) == 6, "수리 강화 did not mend 6");
        }

        private static int RepairStep(bool research)
        {
            World world = Two(Faction.Humans, Faction.Hellfire);
            World control = Two(Faction.Humans, Faction.Hellfire);
            if (research)
            {
                Research(world, 0, UpgradeEffect.RepairAmount);
                Research(control, 0, UpgradeEffect.RepairAmount);
            }
            int[] ids = null, same = null;
            foreach (World w in new[] { world, control })
            {
                int cannon = w.SpawnBuilding(0, Role.Defense, 0, At(32, 30), complete: true);
                int walker = w.SpawnUnit(0, Role.Ranged, 3, At(30, 32));
                int crossbow = w.SpawnUnit(0, Role.Ranged, 1, At(29, 30));
                int towed = w.SpawnUnit(0, Role.Ranged, 4, At(34, 30));
                int site = w.SpawnBuilding(0, Role.Supply, 0, At(31, 28), complete: false);
                w.SetHpForScenario(cannon, w.GetEntity(cannon).MaxHp - 20);
                w.SetHpForScenario(walker, w.GetEntity(walker).MaxHp - 30);
                w.SetHpForScenario(crossbow, w.GetEntity(crossbow).MaxHp - 50);
                w.SetHpForScenario(towed, w.GetEntity(towed).MaxHp - 60);
                if (w == world) ids = new[] { cannon, walker, crossbow, towed, site };
                else same = new[] { cannon, walker, crossbow, towed, site };
            }
            world.SpawnUnit(0, Role.Signature, 2, At(30, 30));
            while (world.Tick % 10 != 0) { Step(world); Step(control); }
            var hp = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++) hp[i] = world.GetEntity(ids[i]).Hp;
            Step(world);
            Step(control);
            int mended = world.GetEntity(ids[1]).Hp - hp[1];
            Check(world.GetEntity(ids[0]).Hp == hp[0], "수리 mended the less wounded 대포 too");
            Check(world.GetEntity(ids[2]).Hp == hp[2], "수리 mended a 석궁 사수, which is no device");
            Check(world.GetEntity(ids[3]).Hp == hp[3], "수리 reached a device 4 cells off");
            Check(world.GetEntity(ids[4]).Hp == control.GetEntity(same[4]).Hp, "수리 mended a site under construction");
            for (int t = 0; t < 9; t++)
            {
                Step(world);
                Step(control);
                Check(world.GetEntity(ids[1]).Hp == hp[1] + mended, "수리 mended off its period");
            }
            return mended;
        }

        // ---------------------------------------------------------------------
        // 잠듦의 고리

        /// <summary>
        /// A finished 잠듦의 고리 at (30, 30), on a tick 20 divides: debris 4
        /// cells off lapsing in 10 ticks is put back to lapse in 40; debris 5
        /// cells off is left alone and lapses; debris inside lapsing in 100 is
        /// left alone; a cell whose debris already lapsed is not revived. Held
        /// every 20 ticks, the near debris is still standing 300 ticks on. A
        /// ring still under construction holds nothing.
        /// </summary>
        private static void TheRingHoldsDebrisInItsSquare()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            world.SpawnBuilding(0, Role.Defense, 2, At(30, 30), complete: true);
            while (world.Tick % 20 != 0) Step(world);
            int t0 = world.Tick;
            int inside = Cell(34, 26), outside = Cell(35, 30), lasting = Cell(26, 34), lapsed = Cell(30, 34);
            world.SetRemnantForScenario(inside, t0 + 10);
            world.SetRemnantForScenario(outside, t0 + 10);
            world.SetRemnantForScenario(lasting, t0 + 100);
            world.SetRemnantForScenario(lapsed, t0);
            Step(world);
            Check(world.RemnantExpiry(inside) == t0 + 40, "the ring put debris 4 cells off to " + (world.RemnantExpiry(inside) - t0) + ", not 40");
            Check(world.RemnantExpiry(outside) == t0 + 10, "the ring reached 5 cells");
            Check(world.RemnantExpiry(lasting) == t0 + 100, "the ring shortened debris that lasted longer");
            Check(world.RemnantExpiry(lapsed) == t0, "the ring revived lapsed debris");
            Run(world, 300);
            Check(world.HasRemnant(inside), "held debris lapsed under the ring");
            Check(!world.HasRemnant(outside), "debris outside the ring did not lapse");

            World site = Two(Faction.RockGolems, Faction.Hellfire);
            site.SpawnBuilding(0, Role.Defense, 2, At(30, 30), complete: false);
            while (site.Tick % 20 != 0) Step(site);
            site.SetRemnantForScenario(inside, site.Tick + 10);
            Step(site);
            Check(site.RemnantExpiry(inside) == site.Tick + 9, "a ring under construction held debris");
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>A body with both clocks set, burnt to death: Kill writes both to 0.</summary>
        private static void KillClearsTheAbilityClocks()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            int roller = world.SpawnUnit(0, Role.Melee, 1, At(30, 30));
            world.SetAbilityForScenario(roller, 120, 7);
            world.SetHpForScenario(roller, 1);
            world.SetStatusForScenario(roller, 0, 1);
            Step(world);
            Entity e = world.GetEntity(roller);
            Check(!e.Alive, "the burn did not kill the roller, so the check proves nothing");
            Check(e.AbilityCooldown == 0 && e.AbilityTicksLeft == 0,
                "a dead body still carries AbilityCooldown " + e.AbilityCooldown + " and AbilityTicksLeft " + e.AbilityTicksLeft);
        }

        /// <summary>The mixed match twice from scratch: the same hash on every tick.</summary>
        private static void TwoWorldsAgree()
        {
            ulong a = Mixed(), b = Mixed();
            Check(a == b, "two runs of the batch 7 abilities hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
        }

        /// <summary>
        /// 돌 골렘 peer 0 with every batch 7 research of its own, two 바위 술사,
        /// two 구르는 바위, golems, a 잠듦의 고리 and debris; 지옥불 peer 1 with
        /// two 용암 투석 악마 and fighters, 600 ticks. A golem that falls drops
        /// debris the mages then throw and mend with.
        /// </summary>
        private static ulong Mixed()
        {
            World world = Two(Faction.RockGolems, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.RollCharge);
            Research(world, 0, UpgradeEffect.RemnantRepair);
            world.SpawnBuilding(0, Role.Defense, 2, At(30, 30), complete: true);
            for (int i = 0; i < 2; i++)
            {
                world.SpawnUnit(0, Role.Ranged, 0, At(28, 28 + 4 * i));
                world.SpawnUnit(0, Role.Melee, 1, At(31, 27 + 6 * i));
                world.SpawnUnit(0, Role.Melee, 0, At(33, 29 + 2 * i));
                world.SpawnUnit(1, Role.Ranged, 2, At(44, 28 + 4 * i));
                world.SpawnUnit(1, Role.Melee, 1, At(40, 29 + 2 * i));
            }
            for (int x = 29; x < 32; x++) world.SetRemnantForScenario(Cell(x, 33), world.Tick + 60);
            return EveryTickHashed(world, 600);
        }

        /// <summary>Two worlds one body's AbilityCooldown apart hash apart.</summary>
        private static void TheHashSeesAbilityCooldown()
        {
            World plain = Two(Faction.RockGolems, Faction.Hellfire);
            World moved = Two(Faction.RockGolems, Faction.Hellfire);
            int a = plain.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            int b = moved.SpawnUnit(0, Role.Ranged, 0, At(30, 30));
            moved.SetAbilityForScenario(b, 1, 0);
            Check(plain.GetEntity(a).Hp == moved.GetEntity(b).Hp, "the worlds differ in hp, so the check proves nothing");
            Check(plain.Hash() != moved.Hash(), "AbilityCooldown is not hashed");
        }

        /// <summary>Two worlds one body's AbilityTicksLeft apart hash apart.</summary>
        private static void TheHashSeesAbilityTicksLeft()
        {
            World plain = Two(Faction.TreeSpirits, Faction.Hellfire);
            World moved = Two(Faction.TreeSpirits, Faction.Hellfire);
            int a = plain.SpawnUnit(0, Role.Melee, 3, At(30, 30));
            int b = moved.SpawnUnit(0, Role.Melee, 3, At(30, 30));
            moved.SetAbilityForScenario(b, 0, 1);
            Check(plain.GetEntity(a).AbilityCooldown == moved.GetEntity(b).AbilityCooldown, "the worlds differ in cooldown, so the check proves nothing");
            Check(plain.Hash() != moved.Hash(), "AbilityTicksLeft is not hashed");
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        /// <summary>One step, then the rule the burst queue is never hashed on: it is empty.</summary>
        private static void Step(World world, List<Command> commands = null)
        {
            world.Step(commands ?? Idle);
            Check(world.PendingBurstCount == 0,
                world.PendingBurstCount + " bursts were still queued at the end of tick " + (world.Tick - 1));
        }

        /// <summary>
        /// Runs this many ticks and folds every tick's hash into one number, so
        /// two runs that part for a few ticks and meet again still differ: a
        /// clock off by one tick that has run out by the end leaves the last
        /// hash alone.
        /// </summary>
        private static ulong EveryTickHashed(World world, int ticks)
        {
            ulong folded = 0;
            for (int t = 0; t < ticks; t++)
            {
                Step(world);
                folded = folded * 1099511628211UL ^ world.Hash();
            }
            return folded;
        }

        private static void Run(World world, int ticks)
        {
            for (int t = 0; t < ticks; t++) Step(world);
        }

        private static List<Command> Order(World world, int peer, CommandType type, int id, FixVec2 target) =>
            new List<Command> { new Command(world.Tick, peer, 2000 + world.Tick, type, id, target) };

        /// <summary>One step, then what each body lost against what it should have.</summary>
        private static void Losses(World world, int[] ids, int[] want, string what)
        {
            var hp = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++) hp[i] = world.GetEntity(ids[i]).Hp;
            Step(world);
            for (int i = 0; i < ids.Length; i++)
            {
                int lost = hp[i] - world.GetEntity(ids[i]).Hp;
                Check(lost == want[i], what + ": body " + i + " lost " + lost + ", not " + want[i]);
            }
        }

        /// <summary>Steps until this body fires again (its cooldown set to its period this tick).</summary>
        private static void ShotFrom(World world, int id)
        {
            int period = world.GetEntity(id).AttackCooldown;
            for (int t = 0; t < 60; t++)
            {
                Step(world);
                if (world.GetEntity(id).AttackCooldown > period) return;
                period = world.GetEntity(id).AttackCooldown;
            }
            Check(false, "the body never fired again");
        }

        /// <summary>How far, in raw Q16.16 units, this body moved on each of the next ticks.</summary>
        private static List<long> Steps(World world, int id, int ticks)
        {
            var steps = new List<long>();
            for (int t = 0; t < ticks; t++)
            {
                FixVec2 from = world.GetEntity(id).Position;
                Step(world);
                steps.Add((world.GetEntity(id).Position - from).Magnitude.Raw);
            }
            return steps;
        }

        private static Fix Distance(World world, int a, int b) =>
            (world.GetEntity(a).Position - world.GetEntity(b).Position).Magnitude;

        private static int Cell(int x, int y) => y * World.GridSize + x;

        /// <summary>Peer 0 and peer 1 of these factions, nothing standing.</summary>
        private static World Two(Faction zero, Faction one)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, zero);
            world.SetPeerFaction(1, one);
            return world;
        }

        /// <summary>
        /// Raises this peer's row with this effect to level 1 in a corner of the
        /// map, the way SummonChecks does. Bodies under test go far from it.
        /// </summary>
        internal static void Research(World world, int peer, UpgradeEffect effect)
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
            world.Step(new List<Command>
            {
                new Command(0, peer, 1000 + world.Tick, CommandType.Research, at, FixVec2.Zero, index)
            });
            Check(world.GetEntity(at).ResearchIndex == index, effect + " research was refused");
            for (int t = 0; t < row.Ticks - 1; t++) world.Step(Idle);
            Check(world.UpgradeLevel(peer, index) == 1, effect + " research never landed");
        }

        internal static FixVec2 At(int x, int y)
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
