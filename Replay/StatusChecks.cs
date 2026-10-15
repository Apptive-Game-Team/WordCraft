using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #200's checks: docs/TECH-TREE.md 묶음 5, statuses and auras. Kept
    /// out of Program.cs the way the other per-issue files are.
    ///
    /// The ability numbers are written out from the document rather than read
    /// back from FactionData, because a check that read the table against
    /// itself would pin nothing. What is read from the roster is the number an
    /// ability changes (a weapon's damage, a body's period), so the check
    /// measures the change and not the starting value.
    /// </summary>
    internal static class StatusChecks
    {
        private const ulong Seed = 0x5747;

        public static void Check()
        {
            TheAbilityTableIsTheDocumentedOne();
            EntangleSlowsGroundBodiesOnly();
            EntangleRefreshesAndNeverStacks();
            ASlowedBodyWalksAtHalfSpeed();
            TheVineFieldSlowsHostileGroundBodiesOnce();
            NoSpeedReachesTheCap();
            FireShotNeedsItsResearch();
            ABurnDealsSixOverSixtyTicks();
            ABurnRefreshesAndNeverStacks();
            ChainLightningHopsTwiceAndHalves();
            ChainDischargeHopsOnce();
            TheMarkAddsOnceToTheMarkingSide();
            TheWaterScreenArmorsRangedAlliesOnce();
            TheBubbleWallCountsOtherBubblesUpToTwo();
            TheStillBodyArmorsOnlyWhileStill();
            BloodScentShortensThePeriodAgainstTheWounded();
            TheRallyTorchShortensAlliedPeriodsOnce();
            TheAmplifierShortensDefensePeriods();
            HealingAurasHealOnceAndNeverPastMaxHp();
            HealingResearchRaisesTheAmount();
            TheBurnAuraStacksPerSource();
            OvergrowthSpeedsAlliedGrowthOnce();
            TwoWorldsAgree();
            TheHashSeesBothClocks();
        }

        // ---------------------------------------------------------------------
        // The table

        private struct Want
        {
            public Faction Faction;
            public string Name;
            public Role Role;
            public int Slot;
            public AbilityKind Kind;
            public UpgradeEffect Unlock;
            public int Radius, Amount, Ticks, Period, Limit;
            public UpgradeEffect Boost;
            public int BoostedRadius, BoostedAmount, BoostedTicks;
        }

        private static Want Row(Faction faction, string name, Role role, int slot, AbilityKind kind, UpgradeEffect unlock,
            int radius = 0, int amount = 0, int ticks = 0, int period = 0, int limit = 0,
            UpgradeEffect boost = UpgradeEffect.None, int boostedRadius = 0, int boostedAmount = 0, int boostedTicks = 0) =>
            new Want
            {
                Faction = faction, Name = name, Role = role, Slot = slot, Kind = kind, Unlock = unlock,
                Radius = radius, Amount = amount, Ticks = ticks, Period = period, Limit = limit,
                Boost = boost, BoostedRadius = boostedRadius, BoostedAmount = boostedAmount, BoostedTicks = boostedTicks,
            };

        /// <summary>docs/TECH-TREE.md 진영별 능력 표, the rows 묶음 5 and 6 bring in, with the holder's roster name.</summary>
        private static readonly (Want Row, string Holder)[] Documented =
        {
            (Row(Faction.TreeSpirits, "휘감기", Role.Ranged, 1, AbilityKind.SlowOnHit, UpgradeEffect.None,
                ticks: 40, boost: UpgradeEffect.EntangleDuration, boostedTicks: 60), "덩쿨 정령"),
            (Row(Faction.TreeSpirits, "연쇄 번개", Role.Ranged, 0, AbilityKind.ChainOnHit, UpgradeEffect.ChainLightning,
                radius: 2, limit: 2), "번개 정령"),
            (Row(Faction.TreeSpirits, "과생장", Role.Signature, 1, AbilityKind.GrowthAura, UpgradeEffect.None,
                radius: 3, period: 2), "도깨비불"),
            (Row(Faction.TreeSpirits, "덩굴 지대", Role.Signature, 2, AbilityKind.SlowAura, UpgradeEffect.None,
                radius: 3), "덩굴 군집"),
            (Row(Faction.TreeSpirits, "회복", Role.Defense, 1, AbilityKind.HealAura, UpgradeEffect.None,
                radius: 4, amount: 2, period: 20), "회복 토템"),
            // 묶음 6 (#202), behind the faction's 묶음 5 rows.
            (Row(Faction.TreeSpirits, "전격 과부하", Role.Melee, 2, AbilityKind.DeathBurst, UpgradeEffect.None,
                radius: 2, amount: 12, boost: UpgradeEffect.OverloadDamage, boostedAmount: 18), "찌릿쥐"),
            (Row(Faction.Hellfire, "화상 오라", Role.Melee, 0, AbilityKind.BurnAura, UpgradeEffect.None,
                radius: 2, amount: 2, period: 20, boost: UpgradeEffect.BurnAuraDamage, boostedAmount: 3), "용암 갑각 악마"),
            (Row(Faction.Hellfire, "화염탄", Role.Ranged, 1, AbilityKind.BurnOnHit, UpgradeEffect.FireShot,
                ticks: 60), "균열 파수병"),
            (Row(Faction.Hellfire, "피 냄새", Role.Melee, 2, AbilityKind.WoundedRate, UpgradeEffect.BloodScent,
                amount: 8), "균열 사냥개"),
            (Row(Faction.Hellfire, "집결", Role.Defense, 1, AbilityKind.RateAura, UpgradeEffect.None,
                radius: 5, amount: 3, limit: 5, boost: UpgradeEffect.RallyRadius, boostedRadius: 7), "집결의 횃불"),
            (Row(Faction.WaterSlimes, "물막이", Role.Melee, 1, AbilityKind.ArmorAura, UpgradeEffect.None,
                radius: 2, amount: 1), "물막이 슬라임"),
            (Row(Faction.WaterSlimes, "거품 벽", Role.Melee, 0, AbilityKind.ArmorPerAlly, UpgradeEffect.BubbleWall,
                radius: 2, amount: 1, limit: 2), "거품 정령"),
            (Row(Faction.WaterSlimes, "회복의 비", Role.Signature, 1, AbilityKind.HealAura, UpgradeEffect.None,
                radius: 3, amount: 2, period: 20, boost: UpgradeEffect.HealingRainAmount, boostedAmount: 3), "비구름"),
            (Row(Faction.RockGolems, "이끼 재생", Role.Signature, 2, AbilityKind.HealAura, UpgradeEffect.None,
                radius: 3, amount: 3, period: 20, boost: UpgradeEffect.MossRegrowthAmount, boostedAmount: 5), "이끼 포자 무리"),
            (Row(Faction.Driftworlds, "굳은 몸", Role.Melee, 2, AbilityKind.ArmorWhileStill, UpgradeEffect.None,
                amount: 3), "멸종한 바위 슬라임"),
            (Row(Faction.Driftworlds, "생명 기록", Role.Signature, 3, AbilityKind.HealAura, UpgradeEffect.None,
                radius: 3, amount: 3, period: 20), "멸종한 자연 슬라임"),
            // 묶음 6 (#202), behind the faction's 묶음 5 rows. Pinned here so
            // the whole table is one list; Replay/SummonChecks.cs runs them.
            (Row(Faction.Driftworlds, "층위 고정", Role.Signature, 2, AbilityKind.ExpiryAura, UpgradeEffect.None,
                radius: 4, period: 2, boost: UpgradeEffect.StrataHoldRadius, boostedRadius: 6), "층위 관측자"),
            (Row(Faction.Driftworlds, "화염 분출", Role.Melee, 1, AbilityKind.DeathBurst, UpgradeEffect.None,
                radius: 2, amount: 12, boost: UpgradeEffect.FireBurstDamage, boostedAmount: 18), "멸종한 화염 슬라임"),
            (Row(Faction.Driftworlds, "도착 방전", Role.Ranged, 1, AbilityKind.ArrivalBurst, UpgradeEffect.None,
                radius: 2, amount: 10), "멸종한 번개 슬라임"),
            (Row(Faction.Humans, "표식", Role.Signature, 1, AbilityKind.MarkAura, UpgradeEffect.None,
                radius: 5, amount: 1, boost: UpgradeEffect.MarkAmount, boostedAmount: 2), "마도 정찰기"),
            (Row(Faction.Humans, "증폭", Role.Signature, 3, AbilityKind.RateAura, UpgradeEffect.None,
                radius: 4, amount: 5, limit: 8, boost: UpgradeEffect.AmplifyRadius, boostedRadius: 6), "마나 증폭기"),
            (Row(Faction.Humans, "연쇄 방전", Role.Defense, 1, AbilityKind.ChainOnHit, UpgradeEffect.ChainDischarge,
                radius: 2, limit: 1), "전기 타워"),
        };

        /// <summary>
        /// Every row the document gives 묶음 5, in each faction's order, holder by
        /// roster name, its unlock and boost resolved to the faction's research
        /// row of that effect, every number as written, no cooldown, and nothing
        /// past the end of a table.
        /// </summary>
        private static void TheAbilityTableIsTheDocumentedOne()
        {
            int total = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int index = 0;
                foreach (var (want, holder) in Documented)
                {
                    if (want.Faction != faction) continue;
                    AbilityRow row = FactionData.Ability(faction, index);
                    string where = faction + " ability " + index + " " + want.Name;
                    Check(row.Name == want.Name, where + " is named " + row.Name);
                    Check(row.Role == want.Role && row.Slot == want.Slot, where + " is held by " + row.Role + " " + row.Slot);
                    Check(FactionData.Name(faction, row.Role, row.Slot) == holder,
                        where + " is held by " + FactionData.Name(faction, row.Role, row.Slot) + ", not " + holder);
                    Check(row.Kind == want.Kind, where + " is a " + row.Kind);
                    Check(Effect(faction, row.Unlock) == want.Unlock, where + " is unlocked by " + Effect(faction, row.Unlock));
                    Check(Effect(faction, row.Boost) == want.Boost, where + " is boosted by " + Effect(faction, row.Boost));
                    Check(row.Radius == want.Radius && row.Amount == want.Amount && row.Ticks == want.Ticks &&
                          row.Period == want.Period && row.Limit == want.Limit,
                        where + " has numbers " + row.Radius + "/" + row.Amount + "/" + row.Ticks + "/" + row.Period + "/" + row.Limit);
                    Check(row.BoostedRadius == want.BoostedRadius && row.BoostedAmount == want.BoostedAmount &&
                          row.BoostedTicks == want.BoostedTicks, where + " has boosted numbers off the document");
                    Check(row.Cooldown == 0, where + " has a cooldown");
                    index++;
                    total++;
                }
                Check(FactionData.AbilityCount(faction) == index,
                    faction + " holds " + FactionData.AbilityCount(faction) + " abilities, not " + index);
                Check(FactionData.Ability(faction, index).Kind == AbilityKind.None, faction + ": an ability past the end does something");
                Check(FactionData.Ability(faction, -1).Kind == AbilityKind.None, faction + ": ability -1 does something");
            }
            Check(FactionData.AbilityRowCount == total, "the table holds " + FactionData.AbilityRowCount + " rows, not " + total);
        }

        private static UpgradeEffect Effect(Faction faction, int index) =>
            index < 0 ? UpgradeEffect.None : FactionData.Upgrade(faction, index).Effect;

        // ---------------------------------------------------------------------
        // 휘감기 and 덩굴 지대

        /// <summary>
        /// A 덩쿨 정령's first shot leaves a ground body at 39 ticks (40 set, one
        /// taken off by StatusSystem the same tick), and leaves a flier and a
        /// building at 0 although it hits both.
        /// </summary>
        private static void EntangleSlowsGroundBodiesOnly()
        {
            foreach (var (name, spawn, slowed) in new (string, Func<World, int>, bool)[]
            {
                ("a ground worker", w => w.SpawnWorker(1, At(24, 40)), true),
                ("a flying 자손", w => w.SpawnUnit(1, Role.Ranged, 0, At(24, 40)), false),
                ("a building", w => w.SpawnBuilding(1, Role.Supply, At(24, 40), complete: true), false),
            })
            {
                World world = Two(Faction.TreeSpirits, Faction.Hellfire);
                world.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
                int target = spawn(world);
                int full = world.GetEntity(target).Hp;
                world.Step(Idle);
                Entity t = world.GetEntity(target);
                Check(t.Hp < full, "the 덩쿨 정령 never hit " + name + ", so the check proves nothing");
                Check(t.SlowTicksLeft == (slowed ? 39 : 0), name + " is slowed for " + t.SlowTicksLeft + " ticks after one hit");
            }
        }

        /// <summary>
        /// Every hit puts the clock back to 40 at most: over 120 ticks of a 덩쿨
        /// 정령 shooting every 18 the clock never reads above 39. A clock already
        /// longer is kept, not cut: 55 hit becomes 54. 휘감기 지속 makes it 60.
        /// </summary>
        private static void EntangleRefreshesAndNeverStacks()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            world.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int body = world.SpawnWorker(1, At(24, 40));
            int most = 0, hits = 0, hp = world.GetEntity(body).Hp;
            for (int t = 0; t < 120; t++)
            {
                world.Step(Idle);
                Entity e = world.GetEntity(body);
                if (!e.Alive) break;
                if (e.Hp < hp) hits++;
                hp = e.Hp;
                if (e.SlowTicksLeft > most) most = e.SlowTicksLeft;
            }
            Check(hits >= 2, "the 덩쿨 정령 hit " + hits + " times, so the check proves nothing");
            Check(most == 39, "the slow clock reached " + most + " under repeated hits, not 39");

            World longer = Two(Faction.TreeSpirits, Faction.Hellfire);
            longer.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int slowed = longer.SpawnWorker(1, At(24, 40));
            longer.SetStatusForScenario(slowed, 55, 0);
            longer.Step(Idle);
            Check(longer.GetEntity(slowed).SlowTicksLeft == 54, "a hit cut a 55-tick slow to " + longer.GetEntity(slowed).SlowTicksLeft);

            World researched = Two(Faction.TreeSpirits, Faction.Hellfire);
            Research(researched, 0, UpgradeEffect.EntangleDuration);
            researched.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int worker = researched.SpawnWorker(1, At(24, 40));
            researched.Step(Idle);
            Check(researched.GetEntity(worker).SlowTicksLeft == 59,
                "휘감기 지속 left " + researched.GetEntity(worker).SlowTicksLeft + " ticks, not 59");
        }

        /// <summary>Two workers walk east for 21 ticks; the slowed one covers half the ground the other does.</summary>
        private static void ASlowedBodyWalksAtHalfSpeed()
        {
            World world = Two(Faction.Hellfire, Faction.TreeSpirits);
            int slow = world.SpawnWorker(0, At(10, 30));
            int free = world.SpawnWorker(0, At(10, 34));
            world.SetStatusForScenario(slow, 100, 0);
            world.Step(new List<Command> { Move(0, 1, slow, At(40, 30)), Move(0, 2, free, At(40, 34)) });
            Run(world, 20);
            Fix slowRun = world.GetEntity(slow).Position.X - At(10, 30).X;
            Fix freeRun = world.GetEntity(free).Position.X - At(10, 34).X;
            Check(freeRun > Fix.Zero && slowRun * 2 == freeRun,
                "the slowed worker walked " + slowRun.Raw + " raw while the free one walked " + freeRun.Raw);
        }

        /// <summary>
        /// A 덩굴 군집 halves a hostile ground body's walk within 3 cells, leaves
        /// an allied body and a hostile flier alone, and does not halve a body
        /// that 휘감기 has already slowed a second time.
        /// </summary>
        private static void TheVineFieldSlowsHostileGroundBodiesOnce()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            world.SpawnUnit(0, Role.Signature, 2, At(30, 40));
            int inside = world.SpawnWorker(1, At(31, 40));
            int both = world.SpawnWorker(1, At(29, 38));
            int outside = world.SpawnWorker(1, At(31, 50));
            int ally = world.SpawnWorker(0, At(30, 42));
            world.SetStatusForScenario(both, 100, 0);
            world.Step(new List<Command>
            {
                Move(1, 1, inside, At(50, 40)),
                Move(1, 2, both, At(10, 38)),
                Move(1, 3, outside, At(50, 50)),
                Move(0, 1, ally, At(30, 60)),
            });
            Run(world, 4);
            Fix full = world.GetEntity(outside).Position.X - At(31, 50).X;
            Fix half = world.GetEntity(inside).Position.X - At(31, 40).X;
            Fix once = At(29, 38).X - world.GetEntity(both).Position.X;
            Fix allied = world.GetEntity(ally).Position.Y - At(30, 42).Y;
            Check(full > Fix.Zero && half * 2 == full, "inside the field a worker walked " + half.Raw + " raw against " + full.Raw);
            Check(once == half, "slowed twice a worker walked " + once.Raw + " raw against " + half.Raw);
            Check(allied == full, "an allied worker inside the field walked " + allied.Raw + " raw against " + full.Raw);

            // A flier is not slowed: 자손 against an enemy 덩굴 군집, cell for cell.
            World air = Two(Faction.TreeSpirits, Faction.Hellfire);
            air.SpawnUnit(0, Role.Signature, 2, At(30, 40));
            int flier = air.SpawnUnit(1, Role.Ranged, 0, At(31, 40));
            int far = air.SpawnUnit(1, Role.Ranged, 0, At(31, 52));
            air.Step(new List<Command> { Move(1, 1, flier, At(50, 40)), Move(1, 2, far, At(50, 52)) });
            Run(air, 4);
            Fix near = air.GetEntity(flier).Position.X - At(31, 40).X;
            Fix away = air.GetEntity(far).Position.X - At(31, 52).X;
            Check(away > Fix.Zero && near == away, "a flier inside the field walked " + near.Raw + " raw against " + away.Raw);
        }

        /// <summary>
        /// The cap is 3/4 cell per tick, and no roster row nor researched speed
        /// reaches it today, so the cap never cuts a walk: a body walks exactly
        /// SpeedFor when it is not slowed (ASlowedBodyWalksAtHalfSpeed measures it).
        /// </summary>
        private static void NoSpeedReachesTheCap()
        {
            Check(World.MaxMoveSpeed == Fix.Ratio(3, 4), "the speed cap is " + World.MaxMoveSpeed.Raw + " raw, not 3/4");
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    for (int s = 0; s < FactionData.SlotCount((Faction)f, (Role)r); s++)
                    {
                        Fix speed = FactionData.Stats((Faction)f, (Role)r, s).Speed;
                        Check(speed <= World.MaxMoveSpeed, (Faction)f + " " + (Role)r + " " + s + " walks past the cap");
                    }
                }
            }
            for (int i = 0; i < FactionData.EntryUpgradeCount; i++)
            {
                var row = FactionData.EntryUpgrade(i);
                if (row.Stat != UpgradedStat.Speed && row.Stat != UpgradedStat.FleeSpeed) continue;
                Check(row.Value <= World.MaxMoveSpeed, row.Effect + " walks past the cap");
            }
        }

        // ---------------------------------------------------------------------
        // 화염탄

        /// <summary>A 균열 파수병 sets nothing alight before 화염탄 and 60 ticks of burn (59 after the tick) after it.</summary>
        private static void FireShotNeedsItsResearch()
        {
            World before = Two(Faction.Hellfire, Faction.TreeSpirits);
            before.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int a = before.SpawnBuilding(1, Role.Supply, At(23, 40), complete: true);
            int full = before.GetEntity(a).Hp;
            before.Step(Idle);
            Check(before.GetEntity(a).Hp < full, "the 균열 파수병 never hit, so the check proves nothing");
            Check(before.GetEntity(a).BurnTicksLeft == 0, "화염탄 burned before its research");

            World after = Two(Faction.Hellfire, Faction.TreeSpirits);
            Research(after, 0, UpgradeEffect.FireShot);
            after.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int b = after.SpawnBuilding(1, Role.Supply, At(23, 40), complete: true);
            after.Step(Idle);
            Check(after.GetEntity(b).BurnTicksLeft == 59, "화염탄 left " + after.GetEntity(b).BurnTicksLeft + " ticks, not 59");
        }

        /// <summary>
        /// Sixty ticks of burn take 6 hp, one on each tick the clock lands on
        /// 50, 40, 30, 20, 10 and 0, and nothing after.
        /// </summary>
        private static void ABurnDealsSixOverSixtyTicks()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            int body = world.SpawnWorker(0, At(30, 30));
            int full = world.GetEntity(body).Hp;
            world.SetStatusForScenario(body, 0, 60);
            var burnt = new List<int>();
            for (int t = 0; t < 80; t++)
            {
                int hp = world.GetEntity(body).Hp;
                world.Step(Idle);
                Entity e = world.GetEntity(body);
                if (e.Hp < hp)
                {
                    Check(hp - e.Hp == World.BurnDamage, "a burn tick took " + (hp - e.Hp));
                    burnt.Add(e.BurnTicksLeft);
                }
            }
            Check(full - world.GetEntity(body).Hp == 6, "sixty ticks of burn took " + (full - world.GetEntity(body).Hp) + ", not 6");
            Check(string.Join(",", burnt) == "50,40,30,20,10,0", "the burn landed on clocks " + string.Join(",", burnt));
        }

        /// <summary>A hit puts the clock back to 60 at most and keeps a longer one: 100 hit is 99.</summary>
        private static void ABurnRefreshesAndNeverStacks()
        {
            World world = Two(Faction.Hellfire, Faction.TreeSpirits);
            Research(world, 0, UpgradeEffect.FireShot);
            world.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            int target = world.SpawnBuilding(1, Role.Supply, At(23, 40), complete: true);
            world.SetStatusForScenario(target, 0, 100);
            world.Step(Idle);
            Check(world.GetEntity(target).BurnTicksLeft == 99, "a hit cut a 100-tick burn to " + world.GetEntity(target).BurnTicksLeft);
            Run(world, 60);
            int most = 0;
            for (int t = 0; t < 80; t++)
            {
                world.Step(Idle);
                int left = world.GetEntity(target).BurnTicksLeft;
                if (left > most) most = left;
            }
            Check(most == 59, "the burn clock reached " + most + " under repeated hits, not 59");
        }

        // ---------------------------------------------------------------------
        // 연쇄

        /// <summary>
        /// Four unarmed buildings: P two cells from the 번개 정령 (the nearest, so
        /// the first target), B two past P, C two past B, and D two from P off
        /// the line, spawned after B so B wins the tie. Before 연쇄 번개 only P is
        /// hit. After it, P loses d, B d/2 and C d/4 (each at least 1), D nothing,
        /// and P is not hit a second time although it is as close to B as C is.
        /// </summary>
        private static void ChainLightningHopsTwiceAndHalves()
        {
            int[] before = ChainLosses(Faction.TreeSpirits, Role.Ranged, 0, false, UpgradeEffect.ChainLightning);
            Check(before[0] > 0 && before[1] == 0 && before[2] == 0 && before[3] == 0,
                "before 연쇄 번개 the losses were " + string.Join(",", before));
            int[] after = ChainLosses(Faction.TreeSpirits, Role.Ranged, 0, true, UpgradeEffect.ChainLightning);
            int d = after[0];
            Check(d == before[0], "the first target lost " + d + " with the chain, " + before[0] + " without: hit twice");
            Check(d / 2 >= 2, "the first hit is too light (" + d + ") for halving to show");
            Check(after[1] == Math.Max(1, d / 2) && after[2] == Math.Max(1, d / 2 / 2) && after[3] == 0,
                "연쇄 번개 losses were " + string.Join(",", after) + " from a first hit of " + d);
        }

        /// <summary>전기 타워 hops once: P and B, and not C.</summary>
        private static void ChainDischargeHopsOnce()
        {
            int[] before = ChainLosses(Faction.Humans, Role.Defense, 1, false, UpgradeEffect.ChainDischarge);
            Check(before[0] > 0 && before[1] == 0, "before 연쇄 방전 the losses were " + string.Join(",", before));
            int[] after = ChainLosses(Faction.Humans, Role.Defense, 1, true, UpgradeEffect.ChainDischarge);
            Check(after[0] == before[0] && after[1] == Math.Max(1, after[0] / 2) && after[2] == 0 && after[3] == 0,
                "연쇄 방전 losses were " + string.Join(",", after));
        }

        private static int[] ChainLosses(Faction faction, Role role, int slot, bool unlocked, UpgradeEffect unlock)
        {
            World world = Two(faction, Faction.Hellfire);
            if (unlocked) Research(world, 0, unlock);
            if (role == Role.Defense) world.SpawnBuilding(0, role, slot, At(20, 40), complete: true);
            else world.SpawnUnit(0, role, slot, At(20, 40));
            int p = world.SpawnBuilding(1, Role.Supply, At(22, 40), complete: true);
            int b = world.SpawnBuilding(1, Role.Supply, At(24, 40), complete: true);
            int c = world.SpawnBuilding(1, Role.Supply, At(26, 40), complete: true);
            int dd = world.SpawnBuilding(1, Role.Supply, At(22, 42), complete: true);
            int[] ids = { p, b, c, dd };
            int[] full = new int[4];
            for (int i = 0; i < 4; i++) full[i] = world.GetEntity(ids[i]).Hp;
            world.Step(Idle);
            int[] lost = new int[4];
            for (int i = 0; i < 4; i++) lost[i] = full[i] - world.GetEntity(ids[i]).Hp;
            return lost;
        }

        // ---------------------------------------------------------------------
        // Damage auras

        /// <summary>
        /// 표식: a 대포 hits a worker 1 harder while a 마도 정찰기 of the cannon's
        /// owner stands within 5 cells of it, 1 harder with two, 0 at 6 cells,
        /// and 2 harder once 표식 강화 lands.
        /// </summary>
        private static void TheMarkAddsOnceToTheMarkingSide()
        {
            int plain = MarkedLoss(new FixVec2[0], false);
            Check(MarkedLoss(new[] { At(22, 44) }, false) == plain + 1, "one 정찰기 did not add 1");
            Check(MarkedLoss(new[] { At(22, 44), At(22, 36) }, false) == plain + 1, "two 정찰기 did not add 1 once");
            Check(MarkedLoss(new[] { At(22, 46) }, false) == plain, "a 정찰기 six cells off added to the shot");
            Check(MarkedLoss(new[] { At(22, 44) }, true) == plain + 2, "표식 강화 did not make it 2");
        }

        private static int MarkedLoss(FixVec2[] scouts, bool researched)
        {
            World world = Two(Faction.Humans, Faction.Hellfire);
            if (researched) Research(world, 0, UpgradeEffect.MarkAmount);
            world.SpawnBuilding(0, Role.Defense, 0, At(20, 40), complete: true);
            int target = world.SpawnWorker(1, At(22, 40));
            foreach (FixVec2 at in scouts) world.SpawnUnit(0, Role.Signature, 1, at);
            return OneStepLoss(world, target);
        }

        /// <summary>
        /// 물막이: a 용암 아가리 hits a 물결 궁수 1 lighter while a 물막이 슬라임
        /// stands within 2 cells, 1 lighter with two, and a 거품 정령 in the same
        /// place not lighter at all: the aura is for ranged bodies.
        /// </summary>
        private static void TheWaterScreenArmorsRangedAlliesOnce()
        {
            int archer = ScreenedLoss(Role.Ranged, 0);
            Check(ScreenedLoss(Role.Ranged, 1) == archer - 1, "one 물막이 슬라임 did not take 1 off");
            Check(ScreenedLoss(Role.Ranged, 2) == archer - 1, "two 물막이 슬라임 did not take 1 off once");
            int bubble = ScreenedLoss(Role.Melee, 0);
            Check(ScreenedLoss(Role.Melee, 1) == bubble, "물막이 armored a melee body");
        }

        private static int ScreenedLoss(Role role, int screens)
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SpawnBuilding(1, Role.Defense, 0, At(20, 40), complete: true);
            int target = world.SpawnUnit(0, role, 0, At(22, 40));
            if (screens >= 1) world.SpawnUnit(0, Role.Melee, 1, At(24, 40));
            if (screens >= 2) world.SpawnUnit(0, Role.Melee, 1, At(23, 42));
            return OneStepLoss(world, target);
        }

        /// <summary>
        /// 거품 벽: nothing before its research; after it, one armor per other
        /// 거품 정령 within 2 cells, two at most, so three others are worth two.
        /// </summary>
        private static void TheBubbleWallCountsOtherBubblesUpToTwo()
        {
            int alone = WalledLoss(0, true);
            Check(WalledLoss(3, false) == alone, "거품 벽 armored before its research");
            Check(WalledLoss(1, true) == alone - 1, "one other 거품 정령 did not take 1 off");
            Check(WalledLoss(2, true) == alone - 2, "two other 거품 정령 did not take 2 off");
            Check(WalledLoss(3, true) == alone - 2, "three other 거품 정령 took more than 2 off");
        }

        private static int WalledLoss(int others, bool researched)
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            if (researched) Research(world, 0, UpgradeEffect.BubbleWall);
            world.SpawnBuilding(1, Role.Defense, 0, At(20, 40), complete: true);
            int target = world.SpawnUnit(0, Role.Melee, 0, At(22, 40));
            FixVec2[] spots = { At(24, 40), At(22, 42), At(22, 38) };
            for (int i = 0; i < others; i++) world.SpawnUnit(0, Role.Melee, 0, spots[i]);
            return OneStepLoss(world, target);
        }

        /// <summary>굳은 몸: a 멸종한 바위 슬라임 standing takes 3 less than one ordered to walk on the same tick.</summary>
        private static void TheStillBodyArmorsOnlyWhileStill()
        {
            int still = StillLoss(false);
            int walking = StillLoss(true);
            Check(walking - still == 3, "standing took " + still + " and walking " + walking + ": not 3 apart");
        }

        private static int StillLoss(bool walk)
        {
            World world = Two(Faction.Driftworlds, Faction.Hellfire);
            world.SpawnBuilding(1, Role.Defense, 0, At(20, 40), complete: true);
            int target = world.SpawnUnit(0, Role.Melee, 2, At(22, 40));
            int hp = world.GetEntity(target).Hp;
            world.Step(walk ? new List<Command> { Move(0, 1, target, At(22, 50)) } : Idle);
            return hp - world.GetEntity(target).Hp;
        }

        // ---------------------------------------------------------------------
        // Period auras

        /// <summary>
        /// 피 냄새: a 균열 사냥개 waits 12 after hitting a target at full hp, and 8
        /// after one under half (a site still building, at 1 hp of its MaxHp)
        /// once its research lands, never before.
        /// </summary>
        private static void BloodScentShortensThePeriodAgainstTheWounded()
        {
            int period = FactionData.Stats(Faction.Hellfire, Role.Melee, 2).AttackTicks;
            Check(period == 12, "균열 사냥개 fires every " + period + ", not 12");
            Check(HoundCooldown(true, false, 0) == 12, "피 냄새 worked before its research");
            Check(HoundCooldown(false, true, 0) == 12, "피 냄새 took a target at full hp");
            Check(HoundCooldown(true, true, 0) == 8, "피 냄새 did not make it 8");
        }

        /// <summary>
        /// 집결: a hellfire body within 5 cells of a finished 집결의 횃불 fires 3
        /// sooner, once however many torches, not at 6 cells or from a site still
        /// building, 7 cells once 집결 반경 lands, and 피 냄새's 8 goes to its floor 5.
        /// </summary>
        private static void TheRallyTorchShortensAlliedPeriodsOnce()
        {
            Check(HoundCooldown(false, false, 1) == 9, "one torch did not take 3 off");
            Check(HoundCooldown(false, false, 2) == 9, "two torches did not take 3 off once");
            Check(HoundCooldown(false, false, 3) == 12, "a torch six cells off took ticks off");
            Check(HoundCooldown(false, false, 4) == 12, "a torch still building took ticks off");
            Check(HoundCooldown(false, true, 3) == 9, "집결 반경 did not reach six cells");
            Check(HoundCooldown(true, true, 1) == 5, "피 냄새 under 집결 is not 5");
        }

        /// <summary>
        /// The cooldown a 균열 사냥개 is left with after its first hit on an
        /// unarmed enemy 묘목: finished at full hp, or wounded, a site at 1 hp.
        /// Torches: 0 none, 1 one at 3 cells, 2 two, 3 one at 6 cells, 4 one at
        /// 3 cells still building. Researched means both 피 냄새 and 집결 반경.
        /// </summary>
        private static int HoundCooldown(bool wounded, bool researched, int torches)
        {
            World world = Two(Faction.Hellfire, Faction.TreeSpirits);
            if (researched)
            {
                Research(world, 0, UpgradeEffect.BloodScent);
                Research(world, 0, UpgradeEffect.RallyRadius);
            }
            int hound = world.SpawnUnit(0, Role.Melee, 2, At(20, 40));
            world.SpawnBuilding(1, Role.Supply, At(21, 40), complete: !wounded);
            if (torches == 1 || torches == 2) world.SpawnBuilding(0, Role.Defense, 1, At(17, 40), complete: true);
            if (torches == 2) world.SpawnBuilding(0, Role.Defense, 1, At(20, 37), complete: true);
            if (torches == 3) world.SpawnBuilding(0, Role.Defense, 1, At(14, 40), complete: true);
            if (torches == 4) world.SpawnBuilding(0, Role.Defense, 1, At(17, 40), complete: false);
            world.Step(Idle);
            return world.GetEntity(hound).AttackCooldown;
        }

        /// <summary>
        /// 증폭: a finished 대포 within 4 cells of a 마나 증폭기 fires 5 sooner (20
        /// → 15), not at 5 cells until 증폭 반경 lands, and a 마나 증폭기 does
        /// nothing for a unit.
        /// </summary>
        private static void TheAmplifierShortensDefensePeriods()
        {
            Check(CannonCooldown(At(20, 43), false) == 15, "an amplifier three cells off did not take 5 off");
            Check(CannonCooldown(At(20, 45), false) == 20, "an amplifier five cells off took ticks off");
            Check(CannonCooldown(At(20, 45), true) == 15, "증폭 반경 did not reach five cells");

            World world = Two(Faction.Humans, Faction.Hellfire);
            int crossbow = world.SpawnUnit(0, Role.Ranged, 1, At(20, 40));
            world.SpawnWorker(1, At(22, 40));
            world.SpawnUnit(0, Role.Signature, 3, At(20, 42));
            world.Step(Idle);
            int period = FactionData.Stats(Faction.Humans, Role.Ranged, 1).AttackTicks;
            Check(world.GetEntity(crossbow).AttackCooldown == period, "증폭 sped up a unit");
        }

        private static int CannonCooldown(FixVec2 amplifier, bool researched)
        {
            World world = Two(Faction.Humans, Faction.Hellfire);
            if (researched) Research(world, 0, UpgradeEffect.AmplifyRadius);
            int cannon = world.SpawnBuilding(0, Role.Defense, 0, At(20, 40), complete: true);
            world.SpawnWorker(1, At(22, 40));
            world.SpawnUnit(0, Role.Signature, 3, amplifier);
            world.Step(Idle);
            return world.GetEntity(cannon).AttackCooldown;
        }

        // ---------------------------------------------------------------------
        // Healing and burning auras

        /// <summary>
        /// 회복: on a tick divisible by 20 a 회복 토템 heals an allied body within
        /// 4 cells by 2, once with two totems, not at 5 cells, not between, not an
        /// enemy; it heals a finished building to its MaxHp and no further, and
        /// leaves a site under construction exactly where its ramp puts it.
        /// </summary>
        private static void HealingAurasHealOnceAndNeverPastMaxHp()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            // Wounded by a scenario burn, before any totem stands: two workers
            // ten hp down, an enemy worker and a building one hp down.
            int near = world.SpawnWorker(0, At(30, 33));
            int far = world.SpawnWorker(0, At(30, 35));
            int enemy = world.SpawnWorker(1, At(27, 30));
            int building = world.SpawnBuilding(0, Role.Supply, At(33, 30), complete: true);
            int full = world.GetEntity(near).MaxHp;
            int max = world.GetEntity(building).MaxHp;
            Wound(world, 10, near, far);
            Wound(world, 1, enemy, building);
            int enemyHp = world.GetEntity(enemy).Hp;
            Check(world.GetEntity(near).Hp == full - 10 && world.GetEntity(far).Hp == full - 10, "the burn did not wound the workers by 10");
            Check(world.GetEntity(building).Hp == max - 1, "the burn did not take the building to MaxHp - 1");
            AlignTo(world, 20);
            world.SpawnBuilding(0, Role.Defense, 1, At(30, 30), complete: true);
            world.SpawnBuilding(0, Role.Defense, 1, At(30, 29), complete: true);
            world.Step(Idle);
            Check(world.GetEntity(near).Hp == full - 8, "two 회복 토템 healed a worker to " + world.GetEntity(near).Hp + ", not " + (full - 8));
            Check(world.GetEntity(far).Hp == full - 10, "a 회복 토템 healed a worker five cells off");
            Check(world.GetEntity(enemy).Hp == enemyHp, "a 회복 토템 healed an enemy");
            Check(world.GetEntity(building).Hp == max, "the building healed to " + world.GetEntity(building).Hp + ", not its MaxHp " + max);
            Run(world, 19);
            Check(world.GetEntity(near).Hp == full - 8, "a 회복 토템 healed between its ticks");
            world.Step(Idle);
            Check(world.GetEntity(near).Hp == full - 6, "the second heal did not land on the twentieth tick");

            World plain = Two(Faction.TreeSpirits, Faction.Hellfire);
            World totem = Two(Faction.TreeSpirits, Faction.Hellfire);
            int a = plain.SpawnBuilding(0, Role.Supply, At(33, 30), complete: false);
            int b = totem.SpawnBuilding(0, Role.Supply, At(33, 30), complete: false);
            totem.SpawnBuilding(0, Role.Defense, 1, At(30, 30), complete: true);
            Run(plain, 5);
            Run(totem, 5);
            Check(plain.GetEntity(a).Hp == totem.GetEntity(b).Hp, "a 회복 토템 healed a site under construction");
        }

        /// <summary>
        /// 회복의 비 2 → 3, 이끼 재생 3 → 5, 생명 기록 3, each on a worker ten hp
        /// down one cell from the source, and each researched row heals more.
        /// </summary>
        private static void HealingResearchRaisesTheAmount()
        {
            Check(Healed(Faction.WaterSlimes, Role.Signature, 1, UpgradeEffect.None) == 2, "회복의 비 is not 2");
            Check(Healed(Faction.WaterSlimes, Role.Signature, 1, UpgradeEffect.HealingRainAmount) == 3, "회복의 비 강화 is not 3");
            Check(Healed(Faction.RockGolems, Role.Signature, 2, UpgradeEffect.None) == 3, "이끼 재생 is not 3");
            Check(Healed(Faction.RockGolems, Role.Signature, 2, UpgradeEffect.MossRegrowthAmount) == 5, "이끼 재생 강화 is not 5");
            Check(Healed(Faction.Driftworlds, Role.Signature, 3, UpgradeEffect.None) == 3, "생명 기록 is not 3");
        }

        private static int Healed(Faction faction, Role role, int slot, UpgradeEffect research)
        {
            World world = Two(faction, Faction.Hellfire);
            if (research != UpgradeEffect.None) Research(world, 0, research);
            int body = world.SpawnWorker(0, At(31, 30));
            Wound(world, 10, body);
            int hp = world.GetEntity(body).Hp;
            AlignTo(world, 20);
            world.SpawnUnit(0, role, slot, At(30, 30));
            world.Step(Idle);
            return world.GetEntity(body).Hp - hp;
        }

        /// <summary>
        /// 화상 오라: every 20 ticks each 용암 갑각 악마 burns an enemy ground
        /// body within 2 cells for 2, so two burn it for 4: the one aura that
        /// stacks. 3 each under 화상 오라 강화. Not a flier, not a building.
        /// The 악마 are ordered onto a far building so their weapons stay off
        /// the bodies the aura is measured on.
        /// </summary>
        private static void TheBurnAuraStacksPerSource()
        {
            Check(Burnt(1, false, Body.Worker) == 2, "one 용암 갑각 악마 did not burn 2");
            Check(Burnt(2, false, Body.Worker) == 4, "two 용암 갑각 악마 did not burn 4");
            Check(Burnt(1, true, Body.Worker) == 3, "화상 오라 강화 did not make it 3");
            Check(Burnt(1, false, Body.Flier) == 0, "화상 오라 burned a flier");
            Check(Burnt(1, false, Body.Building) == 0, "화상 오라 burned a building");
        }

        private enum Body { Worker, Flier, Building }

        private static int Burnt(int demons, bool researched, Body kind)
        {
            World world = Two(Faction.Hellfire, Faction.WaterSlimes);
            if (researched) Research(world, 0, UpgradeEffect.BurnAuraDamage);
            AlignTo(world, 20);
            int target = kind == Body.Worker ? world.SpawnWorker(1, At(30, 40))
                : kind == Body.Flier ? world.SpawnUnit(1, Role.Signature, 1, At(30, 40))
                : world.SpawnBuilding(1, Role.Supply, At(30, 40), complete: true);
            int far = world.SpawnBuilding(1, Role.Base, At(60, 60), complete: true);
            var orders = new List<Command>();
            for (int i = 0; i < demons; i++)
            {
                int demon = world.SpawnUnit(0, Role.Melee, 0, i == 0 ? At(31, 40) : At(30, 41));
                orders.Add(new Command(0, 0, 1 + i, CommandType.Attack, demon, FixVec2.Zero, far));
            }
            int hp = world.GetEntity(target).Hp;
            world.Step(orders);
            return hp - world.GetEntity(target).Hp;
        }

        // ---------------------------------------------------------------------
        // 과생장

        /// <summary>
        /// A 풀씨 정령 growing into 덩쿨 정령 takes 40 ticks alone. With an allied
        /// 도깨비불 two cells off it grows one tick more on every even tick, so it
        /// lands on the 27th step; two 도깨비불 are no faster, and an enemy one
        /// changes nothing.
        /// </summary>
        private static void OvergrowthSpeedsAlliedGrowthOnce()
        {
            Check(GrowthSteps(0, -1) == 40, "a growth with no 도깨비불 took " + GrowthSteps(0, -1) + " steps");
            Check(GrowthSteps(1, 0) == 27, "a growth beside a 도깨비불 took " + GrowthSteps(1, 0) + " steps, not 27");
            Check(GrowthSteps(2, 0) == 27, "two 도깨비불 grew it faster than one");
            Check(GrowthSteps(1, 1) == 40, "an enemy 도깨비불 sped the growth");
        }

        private static int GrowthSteps(int wisps, int wispOwner)
        {
            World world = Two(Faction.TreeSpirits, Faction.TreeSpirits);
            world.GrantResources(0, 1000);
            int worker = world.SpawnWorker(0, At(30, 30));
            for (int i = 0; i < wisps; i++) world.SpawnUnit(wispOwner, Role.Signature, 1, At(32, 30 + i));
            world.Step(new List<Command>
            {
                new Command(0, 0, 1, CommandType.Grow, worker, FixVec2.Zero, Command.RosterArg(Role.Ranged, 1))
            });
            Check(World.IsGrowing(world.GetEntity(worker)), "the growth was refused");
            int steps = 1;
            while (World.IsGrowing(world.GetEntity(worker)) && steps < 100)
            {
                world.Step(Idle);
                steps++;
            }
            Entity e = world.GetEntity(worker);
            Check(e.Role == Role.Ranged && e.Slot == 1 && e.Hp == e.MaxHp && e.MaxHp == 90,
                "the grown 덩쿨 정령 stands at " + e.Hp + "/" + e.MaxHp);
            return steps;
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>Every status and aura above in one world, twice from scratch. Same hash at the end.</summary>
        private static void TwoWorldsAgree()
        {
            ulong a = Mixed(), b = Mixed();
            Check(a == b, "two runs of the batch 5 statuses hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
        }

        private static ulong Mixed()
        {
            World world = Two(Faction.TreeSpirits, Faction.Hellfire);
            Research(world, 0, UpgradeEffect.ChainLightning);
            Research(world, 1, UpgradeEffect.FireShot);
            world.SpawnUnit(0, Role.Ranged, 1, At(28, 40));
            world.SpawnUnit(0, Role.Ranged, 0, At(28, 42));
            world.SpawnUnit(0, Role.Signature, 2, At(27, 41));
            world.SpawnBuilding(0, Role.Defense, 1, At(25, 41), complete: true);
            world.SpawnUnit(1, Role.Melee, 0, At(36, 40));
            world.SpawnUnit(1, Role.Ranged, 1, At(36, 42));
            world.SpawnUnit(1, Role.Melee, 2, At(37, 41));
            world.SpawnBuilding(1, Role.Defense, 1, At(39, 41), complete: true);
            Run(world, 400);
            return world.Hash();
        }

        /// <summary>
        /// Two worlds identical but for one status clock on one body hash apart,
        /// once for each clock: both fields are in World.Hash().
        /// </summary>
        private static void TheHashSeesBothClocks()
        {
            World plain = Two(Faction.TreeSpirits, Faction.Hellfire);
            World slow = Two(Faction.TreeSpirits, Faction.Hellfire);
            World burn = Two(Faction.TreeSpirits, Faction.Hellfire);
            int a = plain.SpawnWorker(0, At(30, 30));
            int b = slow.SpawnWorker(0, At(30, 30));
            int c = burn.SpawnWorker(0, At(30, 30));
            slow.SetStatusForScenario(b, 5, 0);
            burn.SetStatusForScenario(c, 0, 5);
            Check(plain.GetEntity(a).Hp == slow.GetEntity(b).Hp && plain.GetEntity(a).Hp == burn.GetEntity(c).Hp,
                "the three worlds differ in hp, so the check proves nothing");
            Check(slow.Hash() != plain.Hash(), "SlowTicksLeft is not hashed");
            Check(burn.Hash() != plain.Hash(), "BurnTicksLeft is not hashed");
            Check(slow.Hash() != burn.Hash(), "a slow and a burn of the same length hash alike");
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

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
        /// map: the base, production 0, tech 0, the row's research building and
        /// the building it requires, all finished, then the research run to its
        /// end. Bodies under test are spawned after, far from the corner.
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
            world.Step(new List<Command>
            {
                new Command(0, peer, 1000 + world.Tick, CommandType.Research, at, FixVec2.Zero, index)
            });
            Check(world.GetEntity(at).ResearchIndex == index, effect + " research was refused");
            Run(world, row.Ticks - 1);
            Check(world.UpgradeLevel(peer, index) == 1, effect + " research never landed");
        }

        /// <summary>
        /// Takes this much hp off each body with a scenario burn and runs it out:
        /// one hp per ten ticks, the only way to wound a body without a second
        /// body that keeps fighting. Spawn aura sources after this.
        /// </summary>
        private static void Wound(World world, int hp, params int[] bodies)
        {
            foreach (int id in bodies) world.SetStatusForScenario(id, 0, hp * World.BurnPeriod);
            Run(world, hp * World.BurnPeriod);
        }

        /// <summary>Steps until the next step runs on a tick this period divides.</summary>
        private static void AlignTo(World world, int period)
        {
            while (world.Tick % period != 0) world.Step(Idle);
        }

        private static int OneStepLoss(World world, int target)
        {
            int hp = world.GetEntity(target).Hp;
            world.Step(Idle);
            int lost = hp - world.GetEntity(target).Hp;
            Check(lost > 0, "the target was not hit, so the check proves nothing");
            return lost;
        }

        private static Command Move(int peer, int seq, int id, FixVec2 to) =>
            new Command(0, peer, seq, CommandType.Move, id, to);

        private static void Run(World world, int ticks)
        {
            for (int t = 0; t < ticks; t++) world.Step(Idle);
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
