using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;
using static WordCraft.Replay.AbilityChecks;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #209's checks: docs/TECH-TREE.md 묶음 8, the Cast command and the
    /// ten actives. The rows themselves are pinned in StatusChecks' table; what
    /// a Cast of them reads (target, range, cost) is pinned here, and then what
    /// they do.
    ///
    /// A refusal is checked as a refusal whole: the world the refused Cast ran
    /// in hashes the same as a twin that was given no order at all. A check of
    /// one field would pass on a Cast that took the cost and then stopped.
    /// Every step goes through AbilityChecks.Step, which asserts the burst
    /// queue is empty when it returns: 해일 부름 and the delayed hits land
    /// through it.
    /// </summary>
    internal static class CastChecks
    {
        private static int seq = 5000;

        public static void Check()
        {
            TheActivesAreAimedAsDocumented();
            EveryActiveRefusesEachRule();
            TheEdgeOfTheRangeIsInRange();
            RazorGaleStrikesSixTimesInSixtyTicks();
            MeteorShowerFallsFortyTicksLater();
            GeyserBurstsTwentyTicksLaterOnAFieldCell();
            LightningDropFallsThirtyTicksLaterOnTheGround();
            ADeadCasterDropsNothing();
            TideCallHitsAndLaysAFieldAtOnce();
            LayingAFieldLeavesTheGatherAlone();
            HardenKillsTheCasterAndWallsFiveCells();
            AwakenConsumesDebrisAndStandsARock();
            SandStormCapsReachAndSlowsForAHundredTicks();
            DeployTakesFortyTicksEachWay();
            ADeployedCannonReachesTenAndSplashes();
            TheCooldownLetsTheNextCastInOnTime();
            KillClearsThePointAndTheDeployment();
            CastOrderDoesNotMatter();
            TwoWorldsAgree();
            TheHashSeesAbilityPoint();
            TheHashSeesDeployed();
            TheScriptedLogCarriesOneCast();
        }

        // ---------------------------------------------------------------------
        // The table

        private struct Aim
        {
            public Faction Faction;
            public string Name;
            public CastTarget Target;
            public int Range, Cost, BoostedCost;
        }

        private static Aim A(Faction faction, string name, CastTarget target, int range, int cost = 0, int boostedCost = 0) =>
            new Aim { Faction = faction, Name = name, Target = target, Range = range, Cost = cost, BoostedCost = boostedCost };

        /// <summary>docs/TECH-TREE.md 진영별 능력 표, the 종류·목표 column of every active.</summary>
        private static readonly Aim[] Actives =
        {
            A(Faction.TreeSpirits, "칼바람 회오리", CastTarget.Point, 6),
            A(Faction.Hellfire, "유성우", CastTarget.Point, 8),
            A(Faction.WaterSlimes, "물 지대 깔기", CastTarget.NoTarget, 0),
            A(Faction.WaterSlimes, "해일 부름", CastTarget.Point, 7),
            A(Faction.WaterSlimes, "간헐천", CastTarget.WaterFieldCell, 8),
            A(Faction.RockGolems, "굳기", CastTarget.NoTarget, 0),
            A(Faction.RockGolems, "깨우기", CastTarget.RemnantCell, 6, 40, 25),
            A(Faction.RockGolems, "모래폭풍", CastTarget.Point, 7),
            A(Faction.Humans, "낙뢰", CastTarget.Point, 8),
            A(Faction.Humans, "전개", CastTarget.NoTarget, 0),
        };

        /// <summary>
        /// Every active is aimed as the document says, costs what it says, and
        /// no other row of the table is a Cast's to name: ten actives, no more.
        /// </summary>
        private static void TheActivesAreAimedAsDocumented()
        {
            foreach (Aim want in Actives)
            {
                AbilityRow row = Row(want.Faction, want.Name);
                Check(FactionData.IsCast(row.Kind), want.Name + " is not an active");
                Check(row.CastTarget == want.Target, want.Name + " is aimed at " + row.CastTarget + ", not " + want.Target);
                Check(row.CastRange == want.Range, want.Name + " reaches " + row.CastRange + ", not " + want.Range);
                Check(row.Cost == want.Cost && row.BoostedCost == want.BoostedCost,
                    want.Name + " costs " + row.Cost + "/" + row.BoostedCost + ", not " + want.Cost + "/" + want.BoostedCost);
            }
            int actives = 0;
            for (int i = 0; i < FactionData.AbilityRowCount; i++)
            {
                AbilityRow row = FactionData.AbilityRowAt(i);
                if (FactionData.IsCast(row.Kind)) actives++;
                Check(FactionData.IsCast(row.Kind) == (row.CastTarget != CastTarget.None), row.Name + " is half an active");
            }
            Check(actives == Actives.Length, "the table holds " + actives + " actives, not " + Actives.Length);
            Check(World.AwakenedRole == Role.Melee && World.AwakenedSlot == 3 &&
                  FactionData.Name(Faction.RockGolems, World.AwakenedRole, World.AwakenedSlot) == "깨어난 바위",
                "깨우기 does not stand up 깨어난 바위");
        }

        // ---------------------------------------------------------------------
        // Refusals

        /// <summary>
        /// docs/TECH-TREE.md 능력 공통 규칙's six rules, each on every active it
        /// applies to, each refused whole. A positive control first, so a
        /// harness that could never get a Cast accepted cannot pass by refusing
        /// everything: the same world and the same order are accepted.
        ///
        /// Not every rule applies to every row, and that is counted rather than
        /// skipped silently: the range rule has no point to test on 물 지대
        /// 깔기, 굳기 and 전개, the unlock rule nothing to test on a 기본 row,
        /// and the cost rule only on 깨우기.
        /// </summary>
        private static void EveryActiveRefusesEachRule()
        {
            int refusals = 0;
            foreach (Aim aim in Actives)
            {
                AbilityRow row = Row(aim.Faction, aim.Name);
                int index = Index(aim.Faction, aim.Name);
                FixVec2 point = Point(aim.Faction, aim.Name);
                Func<World> ready = () =>
                {
                    World r = Ready(aim.Faction, aim.Name, unlocked: true, out _);
                    Prepare(r, row, point);
                    return r;
                };

                Accepted(aim.Name, ready, w => CastBy(w, 0, Caster(w), point, index));

                // 1. 소유·생존: another peer's order, and an id no body has.
                Refused(aim.Name + " from another peer", ready, w => CastBy(w, 1, Caster(w), point, index));
                Refused(aim.Name + " by no body", ready, w => CastBy(w, 0, w.EntityCount + 3, point, index));
                refusals += 2;

                // 2. 그 능력을 가진 행: another row of the table, an index past
                // its end, and the right row cast by a body that does not hold it.
                int other = index == 0 ? 1 : 0;
                Refused(aim.Name + " naming row " + other, ready, w => CastBy(w, 0, Caster(w), point, other));
                Refused(aim.Name + " naming a row past the end", ready,
                    w => CastBy(w, 0, Caster(w), point, FactionData.AbilityCount(aim.Faction)));
                Refused(aim.Name + " naming row -1", ready, w => CastBy(w, 0, Caster(w), point, -1));
                Refused(aim.Name + " by a body that does not hold it", () =>
                {
                    World w = ready();
                    // A worker for every row but the one a worker holds, and a
                    // 거품 정령 for that one.
                    if (row.Role == Role.Worker) w.SpawnUnit(0, Role.Melee, 0, At(26, 26));
                    else w.SpawnWorker(0, At(26, 26));
                    return w;
                }, w => CastBy(w, 0, w.EntityCount - 1, point, index));
                refusals += 4;

                // 3. 해금 연구 완료.
                if (row.Unlock >= 0)
                {
                    Refused(aim.Name + " before its research", () =>
                    {
                        World r = Ready(aim.Faction, aim.Name, unlocked: false, out _);
                        Prepare(r, row, point);
                        return r;
                    }, w => CastBy(w, 0, Caster(w), point, index));
                    refusals++;
                }

                // 4. AbilityCooldown == 0, and for 전개 no transition running.
                Refused(aim.Name + " one tick short of its cooldown", () =>
                {
                    World w = ready();
                    w.SetAbilityForScenario(Caster(w), 1, 0);
                    return w;
                }, w => CastBy(w, 0, Caster(w), point, index));
                refusals++;
                if (FactionData.CastRunsClock(row.Kind))
                {
                    Refused(aim.Name + " with its clock running", () =>
                    {
                        World w = ready();
                        w.SetAbilityForScenario(Caster(w), 0, 5);
                        return w;
                    }, w => CastBy(w, 0, Caster(w), point, index));
                    refusals++;
                }

                // 5. 사거리 안의 지점: just past the range, and for the two cell
                // rows a point in range on a cell without what the row needs.
                if (row.CastTarget != CastTarget.NoTarget)
                {
                    FixVec2 far = Beside(Origin, row.CastRange) + new FixVec2(Fix.Ratio(1, 16), Fix.Zero);
                    Refused(aim.Name + " past its range", () =>
                    {
                        World w = ready();
                        Prepare(w, row, far);
                        return w;
                    }, w => CastBy(w, 0, Caster(w), far, index));
                    refusals++;
                    // A point 65536 cells east: its squared distance overflows
                    // a long and wraps to 0, and its cell clamps onto the edge
                    // column, which is prepared here so the cell rows have what
                    // they ask for there.
                    FixVec2 wrapped = Origin + new FixVec2(Fix.FromInt(65536), Fix.Zero);
                    Refused(aim.Name + " 65536 cells off the map", () =>
                    {
                        World w = ready();
                        Prepare(w, row, wrapped);
                        return w;
                    }, w => CastBy(w, 0, Caster(w), wrapped, index));
                    refusals++;
                    if (row.CastTarget != CastTarget.Point)
                    {
                        FixVec2 bare = Beside(Origin, 3) + new FixVec2(Fix.Zero, Fix.FromInt(2));
                        Refused(aim.Name + " on a cell without " + row.CastTarget, ready, w => CastBy(w, 0, Caster(w), bare, index));
                        refusals++;
                    }
                }

                // 6. 비용이 있으면 자원, and 깨우기's population cap beside it.
                if (row.Cost > 0)
                {
                    Refused(aim.Name + " one resource short", () =>
                    {
                        World w = ready();
                        w.GrantResources(0, row.Cost - 1 - w.GetResources(0));
                        return w;
                    }, w => CastBy(w, 0, Caster(w), point, index));
                    Refused(aim.Name + " at the population cap", () =>
                    {
                        World w = ready();
                        while (w.GetPopulation(0) < w.PopulationCap(0)) w.SpawnWorker(0, At(4, 20));
                        return w;
                    }, w => CastBy(w, 0, Caster(w), point, index));
                    refusals += 2;
                }
            }
            // 10 actives × 7 always, 5 unlocks, 6 running clocks, 7 point
            // ranges, 7 points off the map and 2 cells, 2 for 깨우기's cost
            // and population.
            Check(refusals == 70 + 5 + 6 + 7 + 7 + 2 + 2, "checked " + refusals + " refusals, not 99");
        }

        /// <summary>A point exactly CastRange cells off is in range: the test is WithinRange, a square compare.</summary>
        private static void TheEdgeOfTheRangeIsInRange()
        {
            foreach (Aim aim in Actives)
            {
                if (aim.Target != CastTarget.Point) continue;
                FixVec2 edge = Beside(Origin, aim.Range);
                Accepted(aim.Name + " at the edge of its range", () => Ready(aim.Faction, aim.Name, true, out _),
                    w => CastBy(w, 0, Caster(w), edge, Index(aim.Faction, aim.Name)));
            }
        }

        // ---------------------------------------------------------------------
        // What each one does

        /// <summary>
        /// 칼바람 회오리 at a point 5.5 cells off (the 바람 정령 reaches 3, so it
        /// shoots nothing): the clock reads 60 after the cast tick and one less
        /// each tick after; on the ticks it lands on 50, 40, 30, 20, 10 and 0
        /// every enemy body within 2 cells of the point, the flier too, loses 3,
        /// 18 in all. A body 2.5 cells off, an ally and an enemy building lose
        /// nothing.
        /// </summary>
        private static void RazorGaleStrikesSixTimesInSixtyTicks()
        {
            World w = Ready(Faction.TreeSpirits, "칼바람 회오리", true, out int caster);
            FixVec2 p = Point(Faction.TreeSpirits, "칼바람 회오리");
            int[] hit = { w.SpawnWorker(1, p), w.SpawnWorker(1, p + V(3, 0, 2)), w.SpawnWorker(1, p + V(0, 3, 2)),
                          w.SpawnUnit(1, Role.Signature, 1, p + V(1, 1, 2)) };
            int[] missed = { w.SpawnWorker(1, p + V(5, 0, 2)), w.SpawnWorker(0, p + V(0, -1, 1)),
                             w.SpawnBuilding(1, Role.Supply, 0, p + V(1, 1, 1), complete: true) };
            Check(w.Flies(w.GetEntity(hit[3])), "도깨비불 does not fly, so the check proves nothing");
            int[] hpHit = Hps(w, hit), hpMissed = Hps(w, missed);

            Step(w, Cast(w, caster, p, "칼바람 회오리"));
            Entity e = w.GetEntity(caster);
            Check(e.AbilityTicksLeft == 60 && e.AbilityCooldown == 399 && e.AbilityPoint.Equals(p),
                "칼바람 회오리 left clock " + e.AbilityTicksLeft + ", cooldown " + e.AbilityCooldown + " after the cast tick");
            for (int k = 1; k <= 70; k++)
            {
                Step(w);
                int strikes = Math.Min(k / 10, 6);
                Check(w.GetEntity(caster).AbilityTicksLeft == Math.Max(0, 60 - k),
                    "칼바람 회오리 reads " + w.GetEntity(caster).AbilityTicksLeft + " " + k + " ticks after the cast");
                for (int i = 0; i < hit.Length; i++)
                    Check(w.GetEntity(hit[i]).Hp == hpHit[i] - 3 * strikes,
                        "body " + i + " lost " + (hpHit[i] - w.GetEntity(hit[i]).Hp) + " " + k + " ticks after 칼바람 회오리, not " + 3 * strikes);
            }
            for (int i = 0; i < missed.Length; i++)
                Check(w.GetEntity(missed[i]).Hp == hpMissed[i], "칼바람 회오리 hit body " + i + " it should have missed");
        }

        /// <summary>
        /// 유성우 eight cells off, near the edge of its reach: nothing on the 39
        /// ticks after the cast, then 30 on every enemy within 2 cells of the
        /// point on the 40th, ground, air and building alike. Nothing 2.5 cells
        /// off and no ally.
        /// </summary>
        private static void MeteorShowerFallsFortyTicksLater() =>
            Delayed(Faction.Hellfire, "유성우", 40, 30, flier: true, building: true);

        /// <summary>
        /// 간헐천 at a water field cell whose field lapses ten ticks after the
        /// cast: the point is asked when the order is given and never again, so
        /// the burst still comes 20 ticks later. Bodies, not buildings.
        /// </summary>
        private static void GeyserBurstsTwentyTicksLaterOnAFieldCell() =>
            Delayed(Faction.WaterSlimes, "간헐천", 20, 25, flier: true, building: false);

        /// <summary>낙뢰: 30 ticks, 22, ground and buildings, and never what flies.</summary>
        private static void LightningDropFallsThirtyTicksLaterOnTheGround() =>
            Delayed(Faction.Humans, "낙뢰", 30, 22, flier: false, building: true);

        private static void Delayed(Faction faction, string name, int delay, int damage, bool flier, bool building)
        {
            World w = Ready(faction, name, true, out int caster);
            FixVec2 p = Point(faction, name);
            Prepare(w, Row(faction, name), p, lapse: 10);
            int[] ids =
            {
                w.SpawnWorker(1, p),
                w.SpawnUnit(1, Role.Signature, 1, p + V(1, 1, 2)),
                w.SpawnBuilding(1, Role.Supply, 0, p + V(1, 1, 1), complete: true),
                w.SpawnWorker(1, p + V(5, 0, 2)),
                w.SpawnWorker(0, p + V(0, -1, 1)),
            };
            int[] want = { damage, flier ? damage : 0, building ? damage : 0, 0, 0 };
            int[] hp = Hps(w, ids);
            Step(w, Cast(w, caster, p, name));
            Check(w.GetEntity(caster).AbilityTicksLeft == delay, name + " set a delay of " + w.GetEntity(caster).AbilityTicksLeft);
            for (int k = 1; k < delay; k++)
            {
                Step(w);
                for (int i = 0; i < ids.Length; i++)
                    Check(w.GetEntity(ids[i]).Hp == hp[i], name + " landed on body " + i + " " + k + " ticks after the cast");
            }
            Step(w);
            for (int i = 0; i < ids.Length; i++)
                Check(hp[i] - w.GetEntity(ids[i]).Hp == want[i],
                    name + ": body " + i + " lost " + (hp[i] - w.GetEntity(ids[i]).Hp) + " " + delay + " ticks after the cast, not " + want[i]);
            Check(w.GetEntity(caster).AbilityTicksLeft == 0, name + " left its clock running");
        }

        /// <summary>
        /// A 운석 술사 that dies the tick after its cast takes the meteor with
        /// it: nothing lands, and the corpse holds no clock and no point.
        /// docs/TECH-TREE.md 동점과 경계.
        /// </summary>
        private static void ADeadCasterDropsNothing()
        {
            World w = Ready(Faction.Hellfire, "유성우", true, out int caster);
            FixVec2 p = Point(Faction.Hellfire, "유성우");
            int target = w.SpawnWorker(1, p);
            int hp = w.GetEntity(target).Hp;
            Step(w, Cast(w, caster, p, "유성우"));
            w.SetHpForScenario(caster, 1);
            w.SetStatusForScenario(caster, 0, 1);
            Step(w);
            Check(!w.GetEntity(caster).Alive, "the burn did not kill the 운석 술사, so the check proves nothing");
            Entity corpse = w.GetEntity(caster);
            Check(corpse.AbilityTicksLeft == 0 && corpse.AbilityPoint.Equals(FixVec2.Zero),
                "the dead 운석 술사 still holds clock " + corpse.AbilityTicksLeft + " at its point");
            Run(w, 50);
            Check(w.GetEntity(target).Hp == hp, "a dead caster's meteor still fell");
        }

        /// <summary>
        /// 해일 부름 at once: on the cast tick an enemy ground body at the point
        /// and one 1.5 cells off and an enemy building lose 20, an enemy flier nothing,
        /// and the 3×3 cells round the point carry a field to the cast tick
        /// plus 300; the cells two off carry none.
        /// </summary>
        private static void TideCallHitsAndLaysAFieldAtOnce()
        {
            World w = Ready(Faction.WaterSlimes, "해일 부름", true, out int caster);
            FixVec2 p = Point(Faction.WaterSlimes, "해일 부름");
            int[] ids =
            {
                w.SpawnWorker(1, p), w.SpawnWorker(1, p + V(0, 3, 2)),
                w.SpawnUnit(1, Role.Signature, 1, p + V(1, 1, 2)),
                w.SpawnBuilding(1, Role.Supply, 0, p + V(1, 1, 1), complete: true),
                w.SpawnWorker(1, p + V(5, 0, 2)),
            };
            int[] want = { 20, 20, 0, 20, 0 };
            int[] hp = Hps(w, ids);
            int tick = w.Tick;
            Step(w, Cast(w, caster, p, "해일 부름"));
            for (int i = 0; i < ids.Length; i++)
                Check(hp[i] - w.GetEntity(ids[i]).Hp == want[i],
                    "해일 부름: body " + i + " lost " + (hp[i] - w.GetEntity(ids[i]).Hp) + ", not " + want[i]);
            int c = World.CellOf(p), cx = c % World.GridSize, cy = c / World.GridSize;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    bool inside = Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1;
                    int expiry = w.WaterFieldExpiry(Cell(cx + dx, cy + dy));
                    Check(expiry == (inside ? tick + 300 : 0),
                        "해일 부름 left cell " + dx + "," + dy + " lapsing on " + expiry);
                }
            Check(w.GetEntity(caster).AbilityCooldown == 499, "해일 부름 started a cooldown of " + (w.GetEntity(caster).AbilityCooldown + 1));
        }

        /// <summary>
        /// 물 지대 깔기 by a gathering 물방울 생존자: the 3×3 cells round the
        /// cell it stood on carry a field to the cast tick plus 300, or 450
        /// once 물 지대 지속 lands, and its gather order and load are what they
        /// were.
        /// </summary>
        private static void LayingAFieldLeavesTheGatherAlone()
        {
            foreach (bool boosted in new[] { false, true })
            {
                World w = Ready(Faction.WaterSlimes, "물 지대 깔기", true, out int caster, hold: false);
                if (boosted) Research(w, 0, UpgradeEffect.WaterFieldDuration);
                int node = w.SpawnResourceNode(At(36, 30), 500);
                Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.Gather, caster, FixVec2.Zero, node) });
                Run(w, 3);
                Entity before = w.GetEntity(caster);
                int c = World.CellOf(before.Position), cx = c % World.GridSize, cy = c / World.GridSize;
                int tick = w.Tick;
                Step(w, Cast(w, caster, Origin, "물 지대 깔기"));
                Entity after = w.GetEntity(caster);
                int ticks = boosted ? 450 : 300;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        Check(w.WaterFieldExpiry(Cell(cx + dx, cy + dy)) == tick + ticks,
                            "물 지대 깔기 left cell " + dx + "," + dy + " lapsing on " + w.WaterFieldExpiry(Cell(cx + dx, cy + dy)) +
                            ", not " + (tick + ticks));
                Check(w.WaterFieldExpiry(Cell(cx + 2, cy)) == 0, "물 지대 깔기 reached two cells");
                Check(after.GatherNodeId == node && after.GatherNodeId == before.GatherNodeId && after.CarryAmount == before.CarryAmount,
                    "물 지대 깔기 touched the gather order");
                Check(after.AbilityCooldown == 399, "물 지대 깔기 started a cooldown of " + (after.AbilityCooldown + 1));
            }
        }

        /// <summary>
        /// 굳기: the 성벽 골렘 dies, its population is returned, and its cell
        /// and the four beside it carry debris to the cast tick plus 600 (900
        /// once 굳기 지속 lands); the diagonal does not, and neither does a
        /// water cell beside it. An ally standing on one of the five walks off.
        /// At the corner of the map only the cells on it are written.
        /// </summary>
        private static void HardenKillsTheCasterAndWallsFiveCells()
        {
            foreach (bool boosted in new[] { false, true })
            {
                World w = Ready(Faction.RockGolems, "굳기", true, out int caster);
                if (boosted) Research(w, 0, UpgradeEffect.HardenDuration);
                w.SetTerrain(30, 31, TileKind.Water);
                int ally = w.SpawnWorker(0, At(31, 30));
                int population = w.GetPopulation(0);
                int tick = w.Tick;
                Step(w, Cast(w, caster, Origin, "굳기"));
                Check(!w.GetEntity(caster).Alive, "굳기 left the 성벽 골렘 standing");
                Check(w.GetPopulation(0) == population - 1, "굳기 kept the 성벽 골렘's population");
                int expiry = tick + (boosted ? 900 : 600);
                foreach (var (x, y) in new[] { (30, 30), (30, 29), (29, 30), (31, 30) })
                    Check(w.RemnantExpiry(Cell(x, y)) == expiry,
                        "굳기 left cell " + x + "," + y + " lapsing on " + w.RemnantExpiry(Cell(x, y)) + ", not " + expiry);
                Check(w.RemnantExpiry(Cell(30, 31)) == 0, "굳기 wrote debris on water");
                Check(w.RemnantExpiry(Cell(31, 31)) == 0, "굳기 reached the diagonal");
                FixVec2 from = w.GetEntity(ally).Position;
                Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.Move, ally, At(36, 30)) });
                Run(w, 5);
                Check(w.GetEntity(ally).Position.X > from.X, "the ally on the hardened cell could not walk off it");
            }

            World corner = Ready(Faction.RockGolems, "굳기", true, out _);
            int cornered = corner.SpawnUnit(0, Role.Melee, 2, new FixVec2(Fix.Ratio(1, 2), Fix.Ratio(1, 2)));
            int t0 = corner.Tick;
            Step(corner, Cast(corner, cornered, Origin, "굳기"));
            Check(!corner.GetEntity(cornered).Alive, "the cornered 성벽 골렘 did not harden");
            Check(corner.RemnantExpiry(Cell(0, 0)) == t0 + 600 && corner.RemnantExpiry(Cell(1, 0)) == t0 + 600 &&
                  corner.RemnantExpiry(Cell(0, 1)) == t0 + 600, "굳기 in the corner missed a cell on the map");
        }

        /// <summary>
        /// 깨우기 at debris four cells off: 40 resources, 25 once 깨우기 비용
        /// lands; the debris is gone that tick; a 깨어난 바위 of the caster's
        /// stands at the cell's centre, counted against the population; the
        /// cooldown is 200.
        /// </summary>
        private static void AwakenConsumesDebrisAndStandsARock()
        {
            foreach (bool boosted in new[] { false, true })
            {
                World w = Ready(Faction.RockGolems, "깨우기", true, out int caster);
                if (boosted) Research(w, 0, UpgradeEffect.AwakenCost);
                FixVec2 p = Point(Faction.RockGolems, "깨우기");
                Prepare(w, Row(Faction.RockGolems, "깨우기"), p);
                int cell = World.CellOf(p);
                int bank = w.GetResources(0), population = w.GetPopulation(0), next = w.EntityCount;
                Step(w, Cast(w, caster, p, "깨우기"));
                int cost = boosted ? 25 : 40;
                Check(bank - w.GetResources(0) == cost, "깨우기 cost " + (bank - w.GetResources(0)) + ", not " + cost);
                Check(w.RemnantExpiry(cell) == 0, "깨우기 left the debris standing");
                Check(w.EntityCount == next + 1, "깨우기 stood up " + (w.EntityCount - next) + " bodies");
                Entity rock = w.GetEntity(next);
                Check(rock.Owner == 0 && rock.Kind == EntityKind.Unit && rock.Role == World.AwakenedRole &&
                      rock.Slot == World.AwakenedSlot, "깨우기 stood up the wrong body");
                Check(rock.Position.Equals(World.CellCenter(cell)),
                    "the 깨어난 바위 stands at " + rock.Position.X + "," + rock.Position.Y + ", not the debris cell's centre");
                Check(w.GetPopulation(0) == population + 1, "the 깨어난 바위 is not counted");
                Check(w.GetEntity(caster).AbilityCooldown == 199, "깨우기 started a cooldown of " + (w.GetEntity(caster).AbilityCooldown + 1));
            }
        }

        /// <summary>
        /// 모래폭풍 6.5 cells off: for the hundred ticks the clock runs, an
        /// enemy 덩쿨 정령 (reach 5) inside reaches 3, an enemy melee body still
        /// reaches 2 and walks at full speed, an enemy worker inside walks at
        /// half speed, and an enemy
        /// 3.5 cells from the point and an ally inside are untouched. On the
        /// hundred-and-first the reach is 5 again. 모래폭풍 반경 takes the
        /// zone to 4 cells. A caster that dies ends the zone.
        /// </summary>
        private static void SandStormCapsReachAndSlowsForAHundredTicks()
        {
            World w = Ready(Faction.RockGolems, "모래폭풍", true, out int caster);
            FixVec2 p = Point(Faction.RockGolems, "모래폭풍");
            int vine = w.SpawnUnit(1, Role.Ranged, 1, p);
            int melee = w.SpawnUnit(1, Role.Melee, 0, p + V(0, 1, 1));
            int outside = w.SpawnUnit(1, Role.Ranged, 1, p + V(7, 0, 2));
            int ally = w.SpawnUnit(0, Role.Ranged, 1, p + V(0, -1, 1));
            Hold(w, 1, vine, melee, outside);
            Fix five = Fix.FromInt(5);
            Check(w.RangeFor(w.GetEntity(vine)) == five, "덩쿨 정령 does not reach 5, so the check proves nothing");
            Fix allyReach = w.RangeFor(w.GetEntity(ally));
            Fix meleeReach = w.RangeFor(w.GetEntity(melee));
            Check(meleeReach <= Fix.FromInt(3), "the melee body reaches past 3, so the check proves nothing");

            Step(w, Cast(w, caster, p, "모래폭풍"));
            for (int k = 0; k < 100; k++)
            {
                Check(w.RangeFor(w.GetEntity(vine)) == Fix.FromInt(3),
                    "덩쿨 정령 reaches " + w.RangeFor(w.GetEntity(vine)) + " " + k + " ticks into 모래폭풍");
                Check(w.RangeFor(w.GetEntity(melee)) == meleeReach, "모래폭풍 changed a melee reach");
                Check(w.RangeFor(w.GetEntity(outside)) == five, "모래폭풍 reached 3.5 cells");
                Check(w.RangeFor(w.GetEntity(ally)) == allyReach, "모래폭풍 capped an ally");
                if (k < 99) Step(w);
            }
            Step(w);
            Check(w.RangeFor(w.GetEntity(vine)) == five, "모래폭풍 outlived its hundred ticks");

            // Speed: an enemy worker walking out of the zone.
            World s = Ready(Faction.RockGolems, "모래폭풍", true, out int shaper);
            int walker = s.SpawnWorker(1, p);
            int free = s.SpawnWorker(1, p + V(0, 10, 0));
            // And an enemy melee body (reach 2), which 사거리 2 근접은 영향 없음
            // leaves alone: it walks inside the zone as fast as outside it.
            int blade = s.SpawnUnit(1, Role.Melee, 0, p + V(3, 0, 2));
            int bladeFree = s.SpawnUnit(1, Role.Melee, 0, p + V(3, 20, 2));
            Step(s, Cast(s, shaper, p, "모래폭풍"));
            var move = new List<Command>
            {
                new Command(s.Tick, 1, seq++, CommandType.Move, walker, p + V(0, 2, 0)),
                new Command(s.Tick, 1, seq++, CommandType.Move, free, p + V(0, 12, 0)),
                new Command(s.Tick, 1, seq++, CommandType.Move, blade, p + V(3, 4, 2)),
                new Command(s.Tick, 1, seq++, CommandType.Move, bladeFree, p + V(3, 24, 2)),
            };
            Step(s, move);
            Step(s);
            FixVec2 a0 = s.GetEntity(walker).Position, b0 = s.GetEntity(free).Position;
            FixVec2 c0 = s.GetEntity(blade).Position, d0 = s.GetEntity(bladeFree).Position;
            Step(s);
            long slow = (s.GetEntity(walker).Position - a0).Magnitude.Raw;
            long fast = (s.GetEntity(free).Position - b0).Magnitude.Raw;
            Check(fast > 0 && Math.Abs(slow * 2 - fast) <= 16,
                "a walker in 모래폭풍 stepped " + slow + " raw against " + fast + " outside, not half");
            long bladeIn = (s.GetEntity(blade).Position - c0).Magnitude.Raw;
            long bladeOut = (s.GetEntity(bladeFree).Position - d0).Magnitude.Raw;
            Check(bladeOut > 0 && Math.Abs(bladeIn - bladeOut) <= 16,
                "a melee body in 모래폭풍 stepped " + bladeIn + " raw against " + bladeOut + " outside, not the same");

            // The research reaches 4 cells.
            World r = Ready(Faction.RockGolems, "모래폭풍", true, out int boosted);
            Research(r, 0, UpgradeEffect.SandStormRadius);
            int far = r.SpawnUnit(1, Role.Ranged, 1, p + V(7, 0, 2));
            Hold(r, 1, far);
            Step(r, Cast(r, boosted, p, "모래폭풍"));
            Check(r.RangeFor(r.GetEntity(far)) == Fix.FromInt(3), "모래폭풍 반경 did not reach 3.5 cells");

            // The caster dies, the zone ends.
            World d = Ready(Faction.RockGolems, "모래폭풍", true, out int doomed);
            int held = d.SpawnUnit(1, Role.Ranged, 1, p);
            Hold(d, 1, held);
            Step(d, Cast(d, doomed, p, "모래폭풍"));
            Check(d.RangeFor(d.GetEntity(held)) == Fix.FromInt(3), "모래폭풍 never stood");
            d.SetHpForScenario(doomed, 1);
            d.SetStatusForScenario(doomed, 0, 1);
            Step(d);
            Check(!d.GetEntity(doomed).Alive, "the burn did not kill the 모래 술사, so the check proves nothing");
            Check(d.RangeFor(d.GetEntity(held)) == five, "모래폭풍 outlived its caster");
        }

        /// <summary>
        /// 전개: the clock reads 40 after the cast tick, Deployed flips on the
        /// 40th tick after, and the cannon fires at nothing and walks nowhere
        /// in between; a Move and an AttackMove are refused throughout and
        /// while deployed. Packing up takes 40 ticks too, and a Move is taken
        /// once it is done.
        /// </summary>
        private static void DeployTakesFortyTicksEachWay()
        {
            World w = Ready(Faction.Humans, "전개", true, out int cannon);
            Step(w, Cast(w, cannon, Origin, "전개"));
            Check(w.GetEntity(cannon).AbilityTicksLeft == 40 && !w.GetEntity(cannon).Deployed,
                "전개 began with clock " + w.GetEntity(cannon).AbilityTicksLeft);
            int prey = w.SpawnWorker(1, Beside(Origin, 6));
            int hp = w.GetEntity(prey).Hp;
            FixVec2 at = w.GetEntity(cannon).Position;
            for (int k = 1; k < 40; k++)
            {
                if (k == 5) RefusedNow(w, "a Move mid-transition", new Command(w.Tick, 0, seq++, CommandType.Move, cannon, At(10, 10)));
                else if (k == 6) RefusedNow(w, "an AttackMove mid-transition", new Command(w.Tick, 0, seq++, CommandType.AttackMove, cannon, At(10, 10)));
                else if (k == 7) RefusedNow(w, "an Attack mid-transition", new Command(w.Tick, 0, seq++, CommandType.Attack, cannon, FixVec2.Zero, prey));
                else Step(w);
                Check(!w.GetEntity(cannon).Deployed, "the cannon deployed " + k + " ticks into a 40-tick transition");
                Check(w.GetEntity(prey).Hp == hp, "the cannon fired mid-transition");
                Check(w.GetEntity(cannon).Position.Equals(at), "the cannon moved mid-transition");
            }
            Step(w);
            Check(w.GetEntity(cannon).Deployed && w.GetEntity(cannon).AbilityTicksLeft == 0, "the cannon was not deployed 40 ticks after the cast");
            RefusedNow(w, "a Move while deployed", new Command(w.Tick, 0, seq++, CommandType.Move, cannon, At(10, 10)));
            RefusedNow(w, "an AttackMove while deployed", new Command(w.Tick, 0, seq++, CommandType.AttackMove, cannon, At(10, 10)));
            // A body shoved against it does not shove it.
            w.SpawnWorker(0, w.GetEntity(cannon).Position + V(1, 1, 8));
            Run(w, 3);
            Check(w.GetEntity(cannon).Position.Equals(at), "separation pushed a deployed cannon");

            Step(w, Cast(w, cannon, Origin, "전개"));
            for (int k = 1; k < 40; k++)
            {
                Step(w);
                Check(w.GetEntity(cannon).Deployed, "the cannon packed up " + k + " ticks into a 40-tick transition");
            }
            Step(w);
            Check(!w.GetEntity(cannon).Deployed, "the cannon was still deployed 40 ticks after packing up");
            Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.Move, cannon, At(10, 30)) });
            Check(w.GetEntity(cannon).Target.Equals(At(10, 30)), "a packed-up cannon refused a Move");
        }

        /// <summary>
        /// Deployed, the 견인 대포 reaches 10 rather than 7 and its hit (15,
        /// armor 0) splashes 7 on a ground body 0.8 cells from the one it hits;
        /// an enemy flier there takes nothing. Undeployed it never touches a
        /// body 9 cells off.
        /// </summary>
        private static void ADeployedCannonReachesTenAndSplashes()
        {
            World w = Ready(Faction.Humans, "전개", true, out int cannon);
            Fix undeployed = w.RangeFor(w.GetEntity(cannon));
            Check(undeployed == Fix.FromInt(7), "the 견인 대포 reaches " + undeployed + " undeployed, not 7");
            FixVec2 aim = Beside(Origin, 9);
            int target = w.SpawnWorker(1, aim);
            int beside = w.SpawnWorker(1, aim + new FixVec2(Fix.Ratio(1, 2), Fix.Ratio(3, 5)));
            int flier = w.SpawnUnit(1, Role.Signature, 1, aim + new FixVec2(Fix.Zero, -Fix.Ratio(1, 2)));
            int hpT = w.GetEntity(target).Hp, hpB = w.GetEntity(beside).Hp, hpF = w.GetEntity(flier).Hp;
            Run(w, 60);
            Check(w.GetEntity(target).Hp == hpT, "an undeployed cannon hit a body 9 cells off");
            Step(w, Cast(w, cannon, Origin, "전개"));
            Run(w, 40);
            Check(w.GetEntity(cannon).Deployed, "the cannon never deployed");
            Check(w.RangeFor(w.GetEntity(cannon)) == Fix.FromInt(10), "a deployed cannon reaches " + w.RangeFor(w.GetEntity(cannon)));
            for (int t = 0; t < 60 && w.GetEntity(target).Hp == hpT; t++) Step(w);
            Check(hpT - w.GetEntity(target).Hp == 15, "the deployed shot took " + (hpT - w.GetEntity(target).Hp) + ", not 15");
            Check(hpB - w.GetEntity(beside).Hp == 7, "the deployed splash took " + (hpB - w.GetEntity(beside).Hp) + ", not 7");
            Check(w.GetEntity(flier).Hp == hpF, "the deployed splash reached a flier");
        }

        /// <summary>
        /// Every active with a cooldown: a Cast on the tick before the cooldown
        /// runs out is refused, and one on the tick it does is accepted. So a
        /// cooldown of N lets the next Cast in exactly N ticks after the first.
        /// </summary>
        private static void TheCooldownLetsTheNextCastInOnTime()
        {
            foreach (Aim aim in Actives)
            {
                AbilityRow row = Row(aim.Faction, aim.Name);
                if (row.Cooldown == 0) continue;
                World w = Ready(aim.Faction, aim.Name, true, out int caster);
                FixVec2 p = Point(aim.Faction, aim.Name);
                Prepare(w, row, p, lapse: 2000);
                int start = w.Tick;
                Step(w, Cast(w, caster, p, aim.Name));
                if (row.CastTarget == CastTarget.RemnantCell) Prepare(w, row, p, lapse: 2000);
                while (w.Tick < start + row.Cooldown - 1) Step(w);
                Step(w, Cast(w, caster, p, aim.Name));
                Check(w.GetEntity(caster).AbilityCooldown == 0,
                    aim.Name + " was cast again " + (row.Cooldown - 1) + " ticks after the first");
                Step(w, Cast(w, caster, p, aim.Name));
                Check(w.GetEntity(caster).AbilityCooldown == row.Cooldown - 1,
                    aim.Name + " was refused " + row.Cooldown + " ticks after the first");
            }
        }

        /// <summary>A body that dies with a point aimed and deployed carries neither.</summary>
        private static void KillClearsThePointAndTheDeployment()
        {
            World w = Two(Faction.Humans, Faction.TreeSpirits);
            int cannon = w.SpawnUnit(0, Role.Ranged, 4, At(30, 30));
            w.SetCastForScenario(cannon, At(33, 33), deployed: true);
            w.SetHpForScenario(cannon, 1);
            w.SetStatusForScenario(cannon, 0, 1);
            Step(w);
            Entity e = w.GetEntity(cannon);
            Check(!e.Alive, "the burn did not kill the cannon, so the check proves nothing");
            Check(e.AbilityPoint.Equals(FixVec2.Zero) && !e.Deployed,
                "a dead body still carries AbilityPoint " + e.AbilityPoint.X + "," + e.AbilityPoint.Y + " and Deployed " + e.Deployed);
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>
        /// The mixed match's commands, every tick's list reversed: the same hash
        /// on every tick. Two peers cast on one tick, and one caster is given
        /// two Casts on one tick so that which one is accepted depends on Seq,
        /// not on arrival. The Cast form of Program's CommandOrderDoesNotMatter.
        /// </summary>
        private static void CastOrderDoesNotMatter()
        {
            List<Command>[] log = MixedLog(out World first, out int accepted);
            Check(accepted >= 8, "only " + accepted + " Casts of the mixed log were accepted, so the check proves little");
            World second = Mixed(out _);
            for (int t = 0; t < log.Length; t++)
            {
                Step(first, log[t]);
                var reversed = new List<Command>(log[t]);
                reversed.Reverse();
                Step(second, reversed);
                Check(first.Hash() == second.Hash(), "arrival order changed the result of a Cast at tick " + t);
            }
        }

        /// <summary>The mixed match twice from scratch: every tick's hash folded into one, the same both times.</summary>
        private static void TwoWorldsAgree()
        {
            ulong a = FoldedMixed(), b = FoldedMixed();
            Check(a == b, "two runs of the batch 8 actives hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
        }

        private static ulong FoldedMixed()
        {
            List<Command>[] log = MixedLog(out World world, out _);
            ulong folded = 0;
            for (int t = 0; t < log.Length; t++)
            {
                Step(world, log[t]);
                folded = folded * 1099511628211UL ^ world.Hash();
            }
            return folded;
        }

        /// <summary>
        /// Five peers, five factions, every unlock researched: 인간 (peer 0) with a
        /// 마도사 and a 견인 대포, 돌 골렘 (1) with a 성벽 골렘, an 이끼 현자 and a
        /// 모래 술사 by debris, 물 슬라임 (2) with a 물방울 생존자, a 해일 인도자
        /// and a 간헐천 술사, 세계수 (3) with a 바람 정령, 지옥불 (4) with a 운석
        /// 술사, and fighters on every side, 400 ticks. Every active is cast at
        /// least once.
        /// </summary>
        private static World Mixed(out Dictionary<string, int> casters)
        {
            var w = new World(0x209);
            Faction[] factions = { Faction.Humans, Faction.RockGolems, Faction.WaterSlimes, Faction.TreeSpirits, Faction.Hellfire };
            for (int p = 0; p < factions.Length; p++) w.SetPeerFaction(p, factions[p]);
            Research(w, 0, UpgradeEffect.Deploy);
            Research(w, 0, UpgradeEffect.LightningDrop);
            Research(w, 2, UpgradeEffect.TideCall);
            Research(w, 3, UpgradeEffect.RazorGale);
            Research(w, 4, UpgradeEffect.MeteorShower);
            casters = new Dictionary<string, int>
            {
                ["낙뢰"] = w.SpawnUnit(0, Role.Ranged, 2, At(26, 30)),
                ["전개"] = w.SpawnUnit(0, Role.Ranged, 4, At(26, 34)),
                ["굳기"] = w.SpawnUnit(1, Role.Melee, 2, At(38, 30)),
                ["깨우기"] = w.SpawnUnit(1, Role.Signature, 1, At(38, 34)),
                ["모래폭풍"] = w.SpawnUnit(1, Role.Ranged, 3, At(38, 26)),
                ["물 지대 깔기"] = w.SpawnWorker(2, At(32, 40)),
                ["해일 부름"] = w.SpawnUnit(2, Role.Ranged, 1, At(30, 42)),
                ["간헐천"] = w.SpawnUnit(2, Role.Signature, 2, At(34, 42)),
                ["칼바람 회오리"] = w.SpawnUnit(3, Role.Signature, 0, At(32, 22)),
                ["유성우"] = w.SpawnUnit(4, Role.Signature, 2, At(30, 20)),
            };
            for (int i = 0; i < 3; i++)
            {
                w.SpawnUnit(0, Role.Ranged, 1, At(28, 28 + 3 * i));
                w.SpawnUnit(1, Role.Melee, 0, At(36, 28 + 3 * i));
                w.SpawnUnit(2, Role.Ranged, 0, At(30 + 3 * i, 38));
                w.SpawnUnit(3, Role.Ranged, 1, At(30 + 3 * i, 24));
            }
            for (int x = 36; x < 39; x++) w.SetRemnantForScenario(Cell(x, 36), w.Tick + 500);
            return w;
        }

        private static List<Command>[] MixedLog(out World world, out int accepted)
        {
            World w = Mixed(out Dictionary<string, int> c);
            world = w;
            var log = new List<Command>[400];
            for (int t = 0; t < log.Length; t++) log[t] = new List<Command>();
            int start = w.Tick;
            void At_(int tick, int peer, string name, FixVec2 point) =>
                log[tick].Add(new Command(start + tick, peer, seq++, CommandType.Cast, c[name], point, Index(w.FactionOf(peer), name)));
            At_(5, 0, "낙뢰", At(33, 30));
            At_(5, 1, "모래폭풍", At(32, 28));
            At_(5, 3, "칼바람 회오리", At(33, 27));
            At_(5, 4, "유성우", At(32, 27));
            At_(6, 2, "물 지대 깔기", FixVec2.Zero);
            // Two Casts by one caster on one tick: the lower Seq is accepted
            // and the other refused on the cooldown, whatever order they arrive in.
            At_(10, 2, "해일 부름", At(33, 37));
            At_(10, 2, "해일 부름", At(28, 37));
            At_(12, 2, "간헐천", At(33, 37));
            At_(12, 1, "깨우기", At(37, 36));
            At_(15, 0, "전개", FixVec2.Zero);
            At_(80, 1, "굳기", FixVec2.Zero);
            At_(120, 0, "전개", FixVec2.Zero);
            At_(300, 0, "낙뢰", At(33, 30));
            accepted = 0;
            World probe = Mixed(out _);
            for (int t = 0; t < log.Length; t++)
            {
                var seen = new HashSet<int>();
                foreach (Command cmd in log[t])
                    if (seen.Add(cmd.EntityId) && probe.CanCast(cmd.PeerId, cmd.EntityId, cmd.Arg, cmd.Target)) accepted++;
                Step(probe, log[t]);
            }
            return log;
        }

        /// <summary>Two worlds one body's AbilityPoint apart, in either axis, hash apart.</summary>
        private static void TheHashSeesAbilityPoint()
        {
            foreach (FixVec2 moved in new[] { new FixVec2(Fix.FromInt(1), Fix.Zero), new FixVec2(Fix.Zero, Fix.FromInt(1)) })
            {
                World plain = Two(Faction.Humans, Faction.TreeSpirits);
                World shifted = Two(Faction.Humans, Faction.TreeSpirits);
                int a = plain.SpawnUnit(0, Role.Ranged, 2, At(30, 30));
                int b = shifted.SpawnUnit(0, Role.Ranged, 2, At(30, 30));
                shifted.SetCastForScenario(b, moved, deployed: false);
                Check(plain.GetEntity(a).Deployed == shifted.GetEntity(b).Deployed, "the worlds differ in Deployed, so the check proves nothing");
                Check(plain.Hash() != shifted.Hash(), "AbilityPoint." + (moved.X.Raw != 0 ? "X" : "Y") + " is not hashed");
            }
        }

        /// <summary>Two worlds one body's Deployed apart hash apart.</summary>
        private static void TheHashSeesDeployed()
        {
            World plain = Two(Faction.Humans, Faction.TreeSpirits);
            World deployed = Two(Faction.Humans, Faction.TreeSpirits);
            int a = plain.SpawnUnit(0, Role.Ranged, 4, At(30, 30));
            int b = deployed.SpawnUnit(0, Role.Ranged, 4, At(30, 30));
            deployed.SetCastForScenario(b, FixVec2.Zero, deployed: true);
            Check(plain.GetEntity(a).AbilityPoint.Equals(deployed.GetEntity(b).AbilityPoint), "the worlds differ in AbilityPoint, so the check proves nothing");
            Check(plain.Hash() != deployed.Hash(), "Deployed is not hashed");
        }

        /// <summary>
        /// The client's log carries exactly one Cast, by a 세계수 정령 body, and
        /// it is refused on the row rule: no 세계수 정령 or 지옥불 body the log
        /// can field inside 600 ticks holds an active (칼바람 회오리 needs a 바람
        /// 토템, its research and a 바람 정령, which land past tick 670 at the
        /// earliest). So the golden hash covers the Cast's decoding and its
        /// refusal on both runtimes; this pins that it is refused whole, the
        /// log with it and without it hashing alike on every tick.
        /// </summary>
        private static void TheScriptedLogCarriesOneCast()
        {
            List<Command>[] log = ScriptedLog.Build();
            List<Command>[] without = ScriptedLog.Build();
            int casts = 0, at = -1;
            for (int t = 0; t < log.Length; t++)
            {
                foreach (Command cmd in log[t])
                {
                    if (cmd.Type != CommandType.Cast) continue;
                    casts++;
                    at = t;
                    Check(cmd.PeerId == 0, "the log's Cast is not 세계수 정령's");
                }
                without[t].RemoveAll(cmd => cmd.Type == CommandType.Cast);
            }
            Check(casts == 1, "the log carries " + casts + " Casts, not 1");
            World a = MatchScenario.Build(0xC0FFEE, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            World b = MatchScenario.Build(0xC0FFEE, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            for (int t = 0; t < log.Length; t++)
            {
                if (t == at)
                {
                    Command cast = log[t].Find(cmd => cmd.Type == CommandType.Cast);
                    string why = a.CastRefusal(cast.PeerId, cast.EntityId, cast.Arg, cast.Target, out _);
                    Check(why == "row", "the log's Cast is refused on " + (why ?? "nothing") + ", not the row rule");
                }
                Step(a, log[t]);
                Step(b, without[t]);
                Check(a.Hash() == b.Hash(), "the log's Cast changed the match at tick " + t);
            }
        }

        // ---------------------------------------------------------------------

        /// <summary>Where every caster stands.</summary>
        private static readonly FixVec2 Origin = At(30, 30);

        /// <summary>
        /// Where each active is aimed in these checks: half a cell inside its
        /// range, east of the caster, so every body set within 2 cells of it on
        /// the east, north or south side is further from the caster than its
        /// own weapon reaches (6 for the 마도사 and 해일 인도자, 5 for the 모래
        /// 술사, 3 for the 바람 정령). 깨우기's point is on a debris cell four off.
        /// </summary>
        private static FixVec2 Point(Faction faction, string name)
        {
            AbilityRow row = Row(faction, name);
            if (row.CastTarget == CastTarget.NoTarget) return Origin;
            if (row.CastTarget == CastTarget.RemnantCell) return Beside(Origin, 4);
            return Beside(Origin, row.CastRange) - new FixVec2(Fix.Ratio(1, 2), Fix.Zero);
        }

        private static FixVec2 Beside(FixVec2 from, int cells) => from + new FixVec2(Fix.FromInt(cells), Fix.Zero);

        /// <summary>A vector of x/d, y/d cells.</summary>
        private static FixVec2 V(int x, int y, int d) =>
            d == 0 ? new FixVec2(Fix.FromInt(x), Fix.FromInt(y)) : new FixVec2(Fix.Ratio(x, d), Fix.Ratio(y, d));

        /// <summary>Lays what a cell row needs on the point's cell: a field for 간헐천, debris for 깨우기.</summary>
        private static void Prepare(World w, AbilityRow row, FixVec2 point, int lapse = 100)
        {
            int cell = World.CellOf(point);
            if (row.CastTarget == CastTarget.WaterFieldCell) w.SetWaterFieldForScenario(cell, w.Tick + lapse);
            if (row.CastTarget == CastTarget.RemnantCell) w.SetRemnantForScenario(cell, w.Tick + lapse);
        }

        /// <summary>
        /// Peer 0 of this faction with the row's unlock researched (or not), a
        /// base and money, and the caster at Origin under Hold, so it walks to
        /// nothing and shoots only what its own weapon reaches. Peer 1 is
        /// 세계수 정령, whose workers and 도깨비불 are the targets: none of them
        /// fights back. The caster is the last body spawned.
        /// </summary>
        private static World Ready(Faction faction, string name, bool unlocked, out int caster, bool hold = true)
        {
            AbilityRow row = Row(faction, name);
            World w = Two(faction, Faction.TreeSpirits);
            if (row.Unlock >= 0 && unlocked) Research(w, 0, FactionData.Upgrade(faction, row.Unlock).Effect);
            else
            {
                w.GrantResources(0, 10000);
                w.SpawnBuilding(0, Role.Base, At(3, 3), complete: true);
            }
            caster = row.Role == Role.Worker ? w.SpawnWorker(0, Origin) : w.SpawnUnit(0, row.Role, row.Slot, Origin);
            if (hold) Step(w, new List<Command> { new Command(w.Tick, 0, seq++, CommandType.HoldPosition, caster, FixVec2.Zero) });
            casterIds[w] = caster;
            return w;
        }

        private static readonly Dictionary<World, int> casterIds = new Dictionary<World, int>();

        private static int Caster(World w) => casterIds[w];

        private static void Hold(World w, int peer, params int[] ids)
        {
            var orders = new List<Command>();
            foreach (int id in ids) orders.Add(new Command(w.Tick, peer, seq++, CommandType.HoldPosition, id, FixVec2.Zero));
            Step(w, orders);
        }

        private static List<Command> Cast(World w, int caster, FixVec2 point, string name) =>
            new List<Command> { CastBy(w, w.GetEntity(caster).Owner, caster, point, Index(w.FactionOf(w.GetEntity(caster).Owner), name)) };

        private static Command CastBy(World w, int peer, int caster, FixVec2 point, int index) =>
            new Command(w.Tick, peer, seq++, CommandType.Cast, caster, point, index);

        /// <summary>The order run in one world and nothing in its twin: the two hash alike.</summary>
        private static void Refused(string what, Func<World> build, Func<World, Command> order)
        {
            World idle = build(), given = build();
            Command c = order(given);
            Step(idle);
            Step(given, new List<Command> { c });
            Check(idle.Hash() == given.Hash(), what + " was not refused whole");
        }

        /// <summary>The same, and the two hash apart: the order did something.</summary>
        private static void Accepted(string what, Func<World> build, Func<World, Command> order)
        {
            World idle = build(), given = build();
            Command c = order(given);
            Step(idle);
            Step(given, new List<Command> { c });
            Check(idle.Hash() != given.Hash(), what + " was refused");
        }

        /// <summary>This order, refused right now in this world: one step with it hashes as one step without it.</summary>
        private static void RefusedNow(World w, string what, Command c)
        {
            Check(w.Tick == c.Tick, "the order is for the wrong tick");
            Entity before = w.GetEntity(c.EntityId);
            Step(w, new List<Command> { c });
            Entity after = w.GetEntity(c.EntityId);
            Check(after.Target.Equals(before.Target) && after.Mode == before.Mode && after.TargetId == before.TargetId,
                what + " was taken");
        }

        private static int[] Hps(World w, int[] ids)
        {
            var hp = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++) hp[i] = w.GetEntity(ids[i]).Hp;
            return hp;
        }

        private static AbilityRow Row(Faction faction, string name) => FactionData.Ability(faction, Index(faction, name));

        private static int Index(Faction faction, string name)
        {
            for (int i = 0; i < FactionData.AbilityCount(faction); i++)
                if (FactionData.Ability(faction, i).Name == name) return i;
            throw new Exception(faction + " has no ability named " + name);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
