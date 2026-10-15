using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #164's checks: a 기술 건물 researches upgrades, a finished level
    /// changes damage, armor and each faction's own mechanic numbers, and every
    /// piece of research state is hashed. Kept out of Program.cs the way the
    /// other per-issue files are.
    ///
    /// Five kinds of question. The table: four rows per faction, shaped as the
    /// issue says. The command: what it costs, when the level lands, and every
    /// refusal. The two ends of a research: a death that loses it, a cancel that
    /// refunds it. The effects: 공격력 and 방어력 in a real hit, and each of the
    /// twelve faction rows moving its own number and no other. And the AI: it
    /// researches in the solo match on its own.
    /// </summary>
    internal static class UpgradeChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            EveryFactionHasAttackArmorAndTwoOwnRows();
            ResearchSpendsAndRaisesTheLevelAfterItsTicks();
            AttackLevelsRaiseDamage();
            ArmorLevelsLowerDamageButNeverBelowOne();
            ResearchRefusesWhatItCannotStart();
            ADestroyedBuildingLosesItsResearch();
            CancelRefundsResearchInFull();
            OneLevelApartIsADifferentHash();
            EachFactionRowMovesItsOwnNumber();
            ResearchedGrowthIsShorter();
            ResearchedGrowthRampsTwiceAsFast();
            ResearchedWarlordsKeepSevenOffspring();
            ResearchedWarlordsSpawnEverySeventyTicks();
            ResearchedPassageArrivesInFortyTicks();
            ResearchedCaptureTakesFortyTicks();
            ResearchedGolemsHitAtThreeCells();
            ResearchedCannonsHitAtEightCells();
            ResearchedDebrisStillBlocksAtTick599();
            ResearchedPillarsAcceptAPointSevenCellsOut();
            ResearchedVolleysHitHarder();
            TheAiResearchesInTheSoloMatch();
            ScriptedLogResearches();
        }

        // ---------------------------------------------------------------------
        // The table

        /// <summary>
        /// Row 0 is 공격력 at three levels, row 1 방어력 at three, rows 2 and 3 are
        /// one level each and the faction's own. The twelve own rows are twelve
        /// different effects: two factions sharing one would be one mechanic
        /// researched twice, and the check below that each row moves its own
        /// number could not tell them apart.
        /// </summary>
        private static void EveryFactionHasAttackArmorAndTwoOwnRows()
        {
            var seen = new List<UpgradeEffect>();
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                int count = FactionData.UpgradeCount(faction);
                // Four rows from #164, and the 묶음 3 rows behind them, which
                // RosterChecks pins one by one.
                Check(count >= 4, faction + " has " + count + " upgrade rows, fewer than four");
                Check(count <= FactionData.MaxUpgrades, faction + " outgrows MaxUpgrades");

                UpgradeRow attack = FactionData.Upgrade(faction, 0);
                UpgradeRow armor = FactionData.Upgrade(faction, 1);
                Check(attack.Effect == UpgradeEffect.Attack && attack.Name == "공격력" && attack.MaxLevel == 3,
                    faction + " row 0 is not 공격력 at three levels");
                Check(armor.Effect == UpgradeEffect.Armor && armor.Name == "방어력" && armor.MaxLevel == 3,
                    faction + " row 1 is not 방어력 at three levels");

                for (int i = 0; i < count; i++)
                {
                    UpgradeRow row = FactionData.Upgrade(faction, i);
                    string where = faction + " row " + i;
                    Check(row.Name.Length > 0, "a nameless upgrade at " + where);
                    Check(row.Resources > 0 && row.Ticks > 0, "a free or instant upgrade at " + where);
                    Check(row.Tier >= 1 && row.Tier <= 3, "an unreachable tier at " + where);
                    if (i < 2) continue;
                    Check(row.MaxLevel == 1, where + " is a faction row with more than one level");
                    Check(!seen.Contains(row.Effect) && row.Effect != UpgradeEffect.Attack &&
                          row.Effect != UpgradeEffect.Armor && row.Effect != UpgradeEffect.None,
                        where + " repeats an effect another row already has: " + row.Effect);
                    seen.Add(row.Effect);
                }

                Check(FactionData.Upgrade(faction, count).MaxLevel == 0, faction + ": a row past the end can be researched");
                Check(FactionData.Upgrade(faction, -1).MaxLevel == 0, faction + ": row -1 can be researched");
            }
            Check(seen.Count == 48, "the six factions own " + seen.Count + " rows, not 48 (twelve from #164, twenty from 묶음 3, three from 묶음 4, twelve from 묶음 5, one from 묶음 6)");
        }

        // ---------------------------------------------------------------------
        // The command

        /// <summary>
        /// The price leaves the bank on the tick the order is accepted, the level
        /// lands exactly Ticks steps later and not one step sooner, and each next
        /// level costs the base price times the level being bought.
        /// </summary>
        private static void ResearchSpendsAndRaisesTheLevelAfterItsTicks()
        {
            World world = TechWorld(Faction.RockGolems, out int tech);
            UpgradeRow row = FactionData.Upgrade(Faction.RockGolems, 0);

            for (int level = 1; level <= row.MaxLevel; level++)
            {
                int banked = world.GetResources(0);
                Check(world.ResearchCost(0, 0) == row.Resources * level,
                    "level " + level + " is priced " + world.ResearchCost(0, 0) + ", not " + row.Resources * level);

                world.Step(Research(0, level, tech, 0));
                Check(world.GetResources(0) == banked - row.Resources * level, "research did not charge level " + level);
                Check(world.GetEntity(tech).ResearchIndex == 0, "the 기술 건물 is not researching row 0");

                Run(world, row.Ticks - 2);
                Check(world.UpgradeLevel(0, 0) == level - 1, "level " + level + " landed early");
                world.Step(Idle);
                Check(world.UpgradeLevel(0, 0) == level, "level " + level + " did not land after " + row.Ticks + " ticks");
                Check(world.GetEntity(tech).ResearchIndex == -1 && world.GetEntity(tech).ResearchTicksLeft == 0,
                    "a finished research left its clock on the building");
            }
            Check(world.AttackBonus(0) == 3 * World.AttackPerLevel, "three levels of 공격력 add " + world.AttackBonus(0));
        }

        /// <summary>
        /// A melee unit hits a target that cannot hit back. Every 공격력 level is
        /// one more damage per hit, measured in the fight rather than read off
        /// AttackBonus.
        /// </summary>
        private static void AttackLevelsRaiseDamage()
        {
            int unresearched = FirstHit(attackLevels: 0, armorLevels: 0);
            Check(unresearched == FactionData.Stats(Faction.RockGolems, Role.Melee).Damage,
                "an unresearched hit took " + unresearched);
            for (int level = 1; level <= 3; level++)
            {
                int dealt = FirstHit(level, 0);
                Check(dealt == unresearched + level * World.AttackPerLevel,
                    "공격력 " + level + " hit for " + dealt + ", not " + (unresearched + level));
            }
        }

        /// <summary>
        /// 방어력 on the target's owner takes one damage per level off the same
        /// hit. The floor of one is pinned on the formula itself: no roster weapon
        /// is light enough for three levels to reach it in a fight.
        /// </summary>
        private static void ArmorLevelsLowerDamageButNeverBelowOne()
        {
            int unresearched = FirstHit(0, 0);
            for (int level = 1; level <= 3; level++)
            {
                int dealt = FirstHit(0, level);
                Check(dealt == unresearched - level * World.ArmorPerLevel,
                    "방어력 " + level + " took " + dealt + ", not " + (unresearched - level));
            }
            Check(FirstHit(2, 2) == unresearched, "공격력 2 against 방어력 2 is not an even trade");

            Check(World.DamageAfterArmor(7, 0, 0, 3) == 4, "7 against 방어력 3 is not 4");
            Check(World.DamageAfterArmor(5, 0, 0, 5) == 1, "a hit equal to the armor did nothing");
            Check(World.DamageAfterArmor(2, 0, 1, 3) == 1, "a hit under the armor did nothing");
            Check(World.DamageAfterArmor(5, 2, 1, 0) == 6, "공격력 and roster armor do not combine");
        }

        /// <summary>
        /// Every refusal the issue names, and each of them leaves the bank and the
        /// building exactly as it found them.
        /// </summary>
        private static void ResearchRefusesWhatItCannotStart()
        {
            // No 기술 건물: the order names the base.
            World world = TechWorld(Faction.Humans, out int tech);
            int baseId = world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);
            Refused(world, baseId, 0, "a base took a research order");

            // Somebody else's 기술 건물.
            int theirs = world.SpawnBuilding(1, Role.Tech, At(40, 40), complete: true);
            Refused(world, theirs, 0, "research ran in the other peer's 기술 건물");

            // A 기술 건물 still going up: not finished, and with nothing else
            // standing it is not tier 3 either.
            World site = new World(Seed);
            site.SetPeerFaction(0, Faction.Humans);
            site.SetPeerFaction(1, Faction.Hellfire);
            site.GrantResources(0, 10000);
            int rising = site.SpawnBuilding(0, Role.Tech, At(20, 20), complete: false);
            Refused(site, rising, 0, "research ran in a 기술 건물 still under construction");

            // A row outside the table, either end.
            Refused(world, tech, FactionData.UpgradeCount(Faction.Humans), "research ran a row past the table");
            Refused(world, tech, -1, "research ran row -1");

            // No resources.
            World poor = TechWorld(Faction.Humans, out int poorTech, resources: FactionData.StatUpgradeCost - 1);
            Refused(poor, poorTech, 0, "research ran without the price in the bank");

            // Already researching: the same building, another row.
            world.Step(Research(0, 0, tech, 0));
            Check(world.GetEntity(tech).ResearchIndex == 0, "the first research was refused");
            Refused(world, tech, 1, "a busy 기술 건물 took a second research");

            // Another 기술 건물 of the same peer, the same row.
            int second = world.SpawnBuilding(0, Role.Tech, At(24, 20), complete: true);
            Refused(world, second, 0, "two 기술 건물 researched one row at once");
            int banked = world.GetResources(0);
            world.Step(Research(0, 1, second, 1));
            Check(world.GetResources(0) == banked - FactionData.StatUpgradeCost, "a second 기술 건물 refused another row");

            // The last level.
            World full = TechWorld(Faction.Humans, out int fullTech);
            ResearchToCompletion(full, fullTech, 2);
            Refused(full, fullTech, 2, "a one-level row was researched twice");
        }

        /// <summary>
        /// A 기술 건물 killed mid-research takes the research with it: the level
        /// never lands and nothing is paid back.
        /// </summary>
        private static void ADestroyedBuildingLosesItsResearch()
        {
            World world = TechWorld(Faction.WaterSlimes, out int tech);
            world.Step(Research(0, 0, tech, 0));
            int afterPaying = world.GetResources(0);

            for (int i = 0; i < 12; i++) world.SpawnUnit(1, Role.Melee, At(19 + i % 3, 18 + i / 3));
            int ticks = 0;
            while (world.GetEntity(tech).Alive && ticks++ < FactionData.StatUpgradeTicks - 10) world.Step(Idle);

            Entity dead = world.GetEntity(tech);
            Check(!dead.Alive, "the squad never destroyed the 기술 건물 before the research finished");
            Check(dead.ResearchIndex == -1 && dead.ResearchTicksLeft == 0, "a destroyed 기술 건물 kept its research");
            Run(world, FactionData.StatUpgradeTicks);
            Check(world.UpgradeLevel(0, 0) == 0, "research landed from a destroyed building");
            Check(world.GetResources(0) == afterPaying, "a destroyed building's research was refunded");
        }

        /// <summary>
        /// CancelProduction on a researching 기술 건물 with an empty queue stops
        /// the research and pays back exactly what the level cost, at level 1 and
        /// at level 2.
        /// </summary>
        private static void CancelRefundsResearchInFull()
        {
            World world = TechWorld(Faction.Driftworlds, out int tech);
            int banked = world.GetResources(0);

            world.Step(Research(0, 0, tech, 0));
            Run(world, 20);
            world.Step(Cancel(0, 1, tech));
            Check(world.GetResources(0) == banked, "cancelling level 1 did not refund in full");
            Check(world.GetEntity(tech).ResearchIndex == -1, "cancelling left the research running");
            Run(world, FactionData.StatUpgradeTicks);
            Check(world.UpgradeLevel(0, 0) == 0, "a cancelled research landed anyway");

            ResearchToCompletion(world, tech, 0);
            banked = world.GetResources(0);
            world.Step(Research(0, 9, tech, 0));
            Check(world.GetResources(0) == banked - 2 * FactionData.StatUpgradeCost, "level 2 was not charged double");
            world.Step(Cancel(0, 10, tech));
            Check(world.GetResources(0) == banked, "cancelling level 2 did not refund what it cost");

            // An idle 기술 건물 has nothing to cancel and pays nothing.
            world.Step(Cancel(0, 11, tech));
            Check(world.GetResources(0) == banked, "cancelling nothing paid out");
        }

        /// <summary>
        /// Two worlds that spent the same, waited the same and differ only in
        /// which row went up a level. Every entity is identical, so the only thing
        /// that can tell them apart is the per-peer level array.
        /// </summary>
        private static void OneLevelApartIsADifferentHash()
        {
            World attack = TechWorld(Faction.Hellfire, out int a);
            World armor = TechWorld(Faction.Hellfire, out int b);
            ResearchToCompletion(attack, a, 0);
            ResearchToCompletion(armor, b, 1);

            Check(attack.GetResources(0) == armor.GetResources(0), "the two worlds spent differently");
            for (int i = 0; i < attack.EntityCount; i++)
            {
                Entity x = attack.GetEntity(i), y = armor.GetEntity(i);
                Check(x.ResearchIndex == y.ResearchIndex && x.ResearchTicksLeft == y.ResearchTicksLeft && x.Hp == y.Hp,
                    "entity " + i + " differs, so the hash check proves nothing");
            }
            Check(attack.Hash() != armor.Hash(), "one level apart hashed the same");

            // And the research fields on the building, while it runs.
            World running = TechWorld(Faction.Hellfire, out int r);
            World idle = TechWorld(Faction.Hellfire, out _);
            running.Step(Research(0, 0, r, 0));
            idle.Step(Idle);
            idle.GrantResources(0, -FactionData.StatUpgradeCost);
            Check(running.GetResources(0) == idle.GetResources(0), "the running and idle worlds banked differently");
            Check(running.Hash() != idle.Hash(), "a running research hashed the same as none");
        }

        // ---------------------------------------------------------------------
        // The faction rows

        /// <summary>
        /// Every number a faction row can move, read for one peer. Index order is
        /// the order of UpgradeEffect from GrowthTime on, so the row that changed
        /// can be named by its effect.
        /// </summary>
        private static int[] Readings(World world, int peer, int golem, int cannon) => new[]
        {
            world.GrowthTicksFor(peer, World.GuardianRole, World.GuardianSlot),
            world.GrowthRampFor(peer),
            world.WarlordOffspringCap(peer),
            world.WarlordSpawnTicksFor(peer),
            world.VolleyMaxBonusFor(peer),
            world.VolleyRadiusFor(peer).ToInt(),
            world.RemnantTicksFor(peer),
            world.RangeFor(world.GetEntity(golem)).ToInt(),
            world.ArrivalRadiusFor(peer).ToInt(),
            world.ArrivalTicksFor(peer),
            world.CaptureTicksFor(peer),
            world.RangeFor(world.GetEntity(cannon)).ToInt(),
        };

        /// <summary>What each reading becomes once its own row is researched.</summary>
        private static readonly (UpgradeEffect Effect, int Base, int Researched)[] Expected =
        {
            (UpgradeEffect.GrowthTime, 60, 45),
            (UpgradeEffect.GrowthHpRamp, 1, 2),
            (UpgradeEffect.OffspringCap, 5, 7),
            (UpgradeEffect.OffspringPeriod, 100, 70),
            (UpgradeEffect.VolleyCap, 4, 6),
            (UpgradeEffect.VolleyRadius, 3, 4),
            (UpgradeEffect.RemnantTicks, 400, 600),
            (UpgradeEffect.GolemMeleeRange, 2, 3),
            (UpgradeEffect.ArrivalRadius, 6, 8),
            (UpgradeEffect.ArrivalTicks, 60, 40),
            (UpgradeEffect.CaptureTicks, 60, 40),
            (UpgradeEffect.CannonRange, 7, 8),
        };

        /// <summary>
        /// For each of the twelve faction rows: researching it moves its own
        /// number from the documented base to the documented value, and leaves
        /// the other eleven readings, and 공격력 and 방어력, where they were.
        ///
        /// The growth reading is taken for a 세계수 정령 peer and the others for
        /// the row's own faction, because a reading is only meaningful for the
        /// faction that has the mechanic: 고목 수호자's growth clock means
        /// nothing for 인간.
        /// </summary>
        private static void EachFactionRowMovesItsOwnNumber()
        {
            int checkedRows = 0;
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                var faction = (Faction)f;
                // Rows 2 and 3, the faction's own two from #164. The 묶음 3
                // rows behind them are researched elsewhere than the 기술 건물
                // this world holds, and RosterChecks moves them one by one.
                for (int index = 2; index < 4; index++)
                {
                    UpgradeEffect effect = FactionData.Upgrade(faction, index).Effect;
                    World world = TechWorld(faction, out int tech);
                    int golem = world.SpawnUnit(0, Role.Melee, At(30, 30));
                    int cannon = world.SpawnBuilding(0, Role.Defense, At(34, 30), complete: true);

                    int[] before = Readings(world, 0, golem, cannon);
                    ResearchToCompletion(world, tech, index);
                    int[] after = Readings(world, 0, golem, cannon);

                    for (int k = 0; k < Expected.Length; k++)
                    {
                        string what = faction + " " + FactionData.Upgrade(faction, index).Name + " reading " + Expected[k].Effect;
                        if (Expected[k].Effect == effect)
                        {
                            Check(before[k] == Expected[k].Base, what + " started at " + before[k] + ", not " + Expected[k].Base);
                            Check(after[k] == Expected[k].Researched, what + " is " + after[k] + ", not " + Expected[k].Researched);
                        }
                        else
                        {
                            Check(after[k] == before[k], what + " moved from " + before[k] + " to " + after[k]);
                        }
                    }
                    Check(world.AttackBonus(0) == 0 && world.ArmorBonus(0) == 0, faction + " " + effect + " moved 공격력 or 방어력");
                    checkedRows++;
                }
            }
            Check(checkedRows == 12, "checked " + checkedRows + " faction rows, not twelve");
        }

        /// <summary>성장 가속, in a growth rather than a reading: 덩쿨 정령 in 30 ticks, not 40.</summary>
        private static void ResearchedGrowthIsShorter()
        {
            World world = TechWorld(Faction.TreeSpirits, out int tech);
            ResearchToCompletion(world, tech, 2);
            int worker = world.SpawnWorker(0, At(30, 30));
            int arg = Command.RosterArg(World.VineRole, World.VineSlot);

            world.Step(One(0, 50, CommandType.Grow, worker, arg));
            Run(world, 28);
            Check(world.GetEntity(worker).Kind == EntityKind.Worker, "the researched growth finished early");
            world.Step(Idle);
            Entity grown = world.GetEntity(worker);
            Check(grown.Kind == EntityKind.Unit && grown.Role == World.VineRole, "the researched growth did not finish in 30 ticks");
        }

        /// <summary>
        /// 성장 체력: the hp ceiling reaches the target at the halfway tick, where
        /// the unresearched ramp is only halfway up the line.
        /// </summary>
        private static void ResearchedGrowthRampsTwiceAsFast()
        {
            int target = FactionData.Stats(Faction.TreeSpirits, World.VineRole, World.VineSlot).Hp;
            int start = FactionData.Stats(Faction.TreeSpirits, Role.Worker).Hp;
            int half = FactionData.Growth(Faction.TreeSpirits, World.VineRole, World.VineSlot).Ticks / 2;

            Check(CeilingAfter(researched: true, half) == target, "성장 체력 did not reach the target at the halfway tick");
            Check(CeilingAfter(researched: false, half) == start + (target - start) / 2,
                "the unresearched ramp is not halfway at the halfway tick");
        }

        private static int CeilingAfter(bool researched, int ticks)
        {
            World world = TechWorld(Faction.TreeSpirits, out int tech);
            if (researched) ResearchToCompletion(world, tech, 3);
            int worker = world.SpawnWorker(0, At(30, 30));
            world.Step(One(0, 50, CommandType.Grow, worker, Command.RosterArg(World.VineRole, World.VineSlot)));
            Run(world, ticks - 1);
            Check(World.IsGrowing(world.GetEntity(worker)), "the growth ended before the halfway tick");
            return world.GetEntity(worker).MaxHp;
        }

        /// <summary>군단 확장: a warlord fills seven slots and stops there.</summary>
        private static void ResearchedWarlordsKeepSevenOffspring()
        {
            World world = TechWorld(Faction.Hellfire, out int tech);
            ResearchToCompletion(world, tech, 2);
            int warlord = world.SpawnUnit(0, World.WarlordRole, At(30, 30));
            Run(world, World.WarlordSpawnTicks * 9);
            Check(world.GetEntity(warlord).OffspringCount == World.WarlordOffspringResearched,
                "a researched warlord keeps " + world.GetEntity(warlord).OffspringCount + " offspring, not 7");
        }

        /// <summary>산란 가속: the first offspring is due 70 ticks after the warlord lands.</summary>
        private static void ResearchedWarlordsSpawnEverySeventyTicks()
        {
            World world = TechWorld(Faction.Hellfire, out int tech);
            ResearchToCompletion(world, tech, 3);
            world.SpawnUnit(0, World.WarlordRole, At(30, 30));
            int born = world.EntityCount;
            Run(world, World.WarlordSpawnTicksResearched - 1);
            Check(world.EntityCount == born, "a researched warlord emitted early");
            world.Step(Idle);
            Check(world.EntityCount == born + 1, "a researched warlord did not emit at 70 ticks");
        }

        /// <summary>
        /// 도착 가속: 틈새 사수 crosses a 통로 in 40 ticks rather than 60, and a body
        /// the table times on its own clock — 폭풍편, 50 — is not touched.
        /// </summary>
        private static void ResearchedPassageArrivesInFortyTicks()
        {
            Check(ArrivalTicks(Role.Ranged, researched: false) == FactionData.PassageArrivalTicks,
                "틈새 사수 does not cross in 60 ticks unresearched");
            Check(ArrivalTicks(Role.Ranged, researched: true) == World.ArrivalTicksResearched,
                "도착 가속 did not bring 틈새 사수 across in 40 ticks");
            int storm = FactionData.Production(Faction.Driftworlds, Role.Melee).Ticks;
            Check(ArrivalTicks(Role.Melee, researched: true) == storm, "도착 가속 sped up 폭풍편 too");
        }

        private static int ArrivalTicks(Role role, bool researched)
        {
            World world = TechWorld(Faction.Driftworlds, out int tech);
            if (researched) ResearchToCompletion(world, tech, 3);
            world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);
            int passage = world.SpawnBuilding(0, World.PassageRole, At(14, 10), complete: true);
            world.Step(new List<Command>
            {
                new Command(0, 0, 50, CommandType.Produce, passage, FixVec2.Zero, Command.RosterArg(role, 0))
            });
            int born = world.EntityCount;
            int ticks = 1;
            while (world.EntityCount == born && ticks < 200) { world.Step(Idle); ticks++; }
            return ticks;
        }

        /// <summary>포획 가속: a 꼬마돌 changes hands after 40 ticks beside it, not 60.</summary>
        private static void ResearchedCaptureTakesFortyTicks()
        {
            Check(CaptureTicks(researched: false) == World.CaptureTicks, "an unresearched capture did not take 60 ticks");
            Check(CaptureTicks(researched: true) == World.CaptureTicksResearched, "포획 가속 did not take 40 ticks");
        }

        private static int CaptureTicks(bool researched)
        {
            World world = TechWorld(Faction.Humans, out int tech);
            if (researched) ResearchToCompletion(world, tech, 2);
            int worker = world.SpawnWorker(0, At(30, 30));
            int rock = world.SpawnNeutralRock(At(31, 30));
            world.Step(One(0, 50, CommandType.Capture, worker, rock));
            int ticks = 1;
            while (world.GetEntity(rock).Owner < 0 && ticks < 200) { world.Step(Idle); ticks++; }
            return ticks;
        }

        /// <summary>
        /// 골렘 사거리 in a fight: a 돌 골렘 melee unit held three cells from a
        /// target lands a hit once researched and never does without it. Hold, so
        /// the control cannot close the gap and hit anyway.
        /// </summary>
        private static void ResearchedGolemsHitAtThreeCells()
        {
            Check(!HitsAtDistance(Faction.RockGolems, 3, researched: false, cannon: false, 3),
                "an unresearched 돌 골렘 melee unit hit at three cells");
            Check(HitsAtDistance(Faction.RockGolems, 3, researched: true, cannon: false, 3),
                "골렘 사거리 did not let a 돌 골렘 melee unit hit at three cells");
        }

        /// <summary>대포 사거리 in a fight: 대포 reaches eight cells once researched, seven without.</summary>
        private static void ResearchedCannonsHitAtEightCells()
        {
            Check(!HitsAtDistance(Faction.Humans, 3, researched: false, cannon: true, 8),
                "an unresearched 대포 hit at eight cells");
            Check(HitsAtDistance(Faction.Humans, 3, researched: true, cannon: true, 8),
                "대포 사거리 did not let 대포 hit at eight cells");
        }

        /// <summary>
        /// Whether peer 0's melee unit, held in place, or its 대포 lands any hit
        /// in 60 ticks on a 지옥불 supply building standing exactly this many
        /// cells east of it.
        /// </summary>
        private static bool HitsAtDistance(Faction faction, int index, bool researched, bool cannon, int cells)
        {
            World world = TechWorld(faction, out int tech);
            if (researched) ResearchToCompletion(world, tech, index);
            int shooter = cannon
                ? world.SpawnBuilding(0, Role.Defense, At(30, 30), complete: true)
                : world.SpawnUnit(0, Role.Melee, At(30, 30));
            int target = world.SpawnBuilding(1, Role.Supply, At(30 + cells, 30), complete: true);
            if (!cannon) world.Step(One(0, 60, CommandType.HoldPosition, shooter, 0));
            int full = world.GetEntity(target).Hp;
            Run(world, 60);
            return world.GetEntity(target).Hp < full;
        }

        /// <summary>
        /// 오래 남는 잔해 in a fight: a 돌 골렘 body killed by combat leaves debris
        /// that still stands 599 ticks after the tick it fell and is gone on the
        /// 600th. The control, unresearched, is gone at 400.
        /// </summary>
        private static void ResearchedDebrisStillBlocksAtTick599()
        {
            Check(DebrisLifetime(researched: false) == World.RemnantTicks, "unresearched debris did not last 400 ticks");
            Check(DebrisLifetime(researched: true) == World.RemnantTicksResearched,
                "오래 남는 잔해 debris did not last 600 ticks");
        }

        /// <summary>How many ticks after the fall the debris cell stays blocked.</summary>
        private static int DebrisLifetime(bool researched)
        {
            World world = TechWorld(Faction.RockGolems, out int tech);
            if (researched) ResearchToCompletion(world, tech, 2);
            int golem = world.SpawnUnit(0, Role.Melee, At(40, 40), hpOverride: 1);
            world.SpawnUnit(1, Role.Melee, At(41, 40));
            int cell = World.CellOf(At(40, 40));

            int fell = -1;
            for (int t = 0; t < 60 && fell < 0; t++)
            {
                int tick = world.Tick;
                world.Step(Idle);
                if (!world.GetEntity(golem).Alive) fell = tick;
            }
            Check(fell >= 0, "the 돌 골렘 body never died");

            // Walked tick by tick, asking the ground rule itself, so the last
            // blocked tick is read rather than inferred from the expiry number.
            int lifetime = 0;
            while (world.HasRemnant(cell) && lifetime < 1000)
            {
                Check(!world.IsPassable(cell, air: false), "debris stood on tick " + world.Tick + " but did not block");
                world.Step(Idle);
                lifetime = world.Tick - fell;
            }
            Check(world.IsPassable(cell, air: false), "the cell stayed blocked after its debris lapsed");
            return lifetime;
        }

        /// <summary>
        /// 도착 반경 in play: a Produce aimed seven cells from a 굴절 기둥 is kept
        /// and the body stands up there once researched. Unresearched, the same
        /// point is refused and the body comes out at the 통로.
        /// </summary>
        private static void ResearchedPillarsAcceptAPointSevenCellsOut()
        {
            FixVec2 aim = At(37, 30);
            Check(!ArrivesAt(aim, researched: false), "an unresearched 굴절 기둥 accepted a point seven cells out");
            Check(ArrivesAt(aim, researched: true), "도착 반경 did not accept a point seven cells out");
        }

        private static bool ArrivesAt(FixVec2 aim, bool researched)
        {
            World world = TechWorld(Faction.Driftworlds, out int tech);
            if (researched) ResearchToCompletion(world, tech, 2);
            world.SpawnBuilding(0, Role.Base, At(10, 10), complete: true);
            int passage = world.SpawnBuilding(0, World.PassageRole, At(14, 10), complete: true);
            world.SpawnBuilding(0, World.PillarRole, World.PillarSlot, At(30, 30), complete: true);
            world.Step(new List<Command>
            {
                new Command(0, 0, 70, CommandType.Produce, passage, aim, Command.RosterArg(Role.Melee, 0))
            });
            bool kept = world.GetEntity(passage).HasArrivalPoint;
            int born = world.EntityCount;
            for (int t = 0; t < 100 && world.EntityCount == born; t++) world.Step(Idle);
            Check(world.EntityCount == born + 1, "the 통로 never produced");
            bool landed = world.GetEntity(born).Position.Equals(aim);
            Check(kept == landed, "the point was " + (kept ? "kept" : "refused") + " but the body " +
                (landed ? "landed on it" : "did not land on it"));
            return landed;
        }

        /// <summary>
        /// 밀집 강화 and 밀집 반경 in a fight, measured on the first volley.
        /// Eight archers packed in a 3x3 block each have seven allies within
        /// three cells, so the cap is what limits them: 4 unresearched, 6
        /// researched. Two archers exactly four cells apart count each other only
        /// under 밀집 반경.
        /// </summary>
        private static void ResearchedVolleysHitHarder()
        {
            int shot = FactionData.Stats(Faction.WaterSlimes, Role.Ranged).Damage;

            var block = new List<FixVec2>();
            for (int y = 31; y <= 33; y++)
            {
                for (int x = 27; x <= 29; x++)
                {
                    if (x != 28 || y != 32) block.Add(At(x, y));
                }
            }
            Check(FirstVolley(block, -1) == 8 * (shot + World.VolleyMaxBonus), "the packed block is not capped at +4");
            Check(FirstVolley(block, 2) == 8 * (shot + World.VolleyMaxBonusResearched),
                "밀집 강화 did not raise the packed block to +6");

            var pair = new List<FixVec2> { At(28, 30), At(28, 34) };
            Check(FirstVolley(pair, -1) == 2 * shot, "two archers four cells apart counted each other unresearched");
            Check(FirstVolley(pair, 3) == 2 * (shot + 1), "밀집 반경 did not let two archers four cells apart count each other");
        }

        /// <summary>
        /// Damage the first tick of fire does to a dummy at (32, 32), with the
        /// given row researched first, or none for -1. Every archer spawns with
        /// its cooldown at zero and the dummy in reach, so all of them fire on
        /// that tick from where they were placed.
        /// </summary>
        private static int FirstVolley(List<FixVec2> archers, int index)
        {
            World world = TechWorld(Faction.WaterSlimes, out int tech);
            if (index >= 0) ResearchToCompletion(world, tech, index);
            int dummy = world.SpawnUnit(1, Role.None, At(32, 32), hpOverride: 1000000);
            foreach (FixVec2 where in archers) world.SpawnUnit(0, Role.Ranged, where);
            world.Step(Idle);
            Entity hit = world.GetEntity(dummy);
            return hit.MaxHp - hit.Hp;
        }

        // ---------------------------------------------------------------------
        // The AI

        /// <summary>
        /// The solo match the other AI checks play: peer 1 is the simulation and
        /// peer 0 does nothing. Somewhere before the match is decided the AI has
        /// to have started a research on its own.
        /// </summary>
        private static void TheAiResearchesInTheSoloMatch()
        {
            const int ai = 1;
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            world.SetPeerAi(ai, true);

            bool researched = false;
            for (int t = 0; t < 1200 && !world.MatchOver && !researched; t++)
            {
                world.Step(Idle);
                for (int i = 0; i < world.EntityCount && !researched; i++)
                {
                    Entity e = world.GetEntity(i);
                    researched = e.Alive && e.Owner == ai && e.ResearchIndex >= 0;
                }
            }
            Check(researched, "the AI never started a research in the solo match");
        }

        /// <summary>
        /// The client's scripted log carries one Research, so the golden hash both
        /// runtimes assert covers the command, the clock, the level landing and a
        /// hit carrying the bonus. A refused order changes no state, and a log
        /// whose research was silently refused, or whose level landed after the
        /// last shot, would hash identically on both runtimes and cover none of
        /// that. So each piece is checked to have happened inside the log: the
        /// named entity is peer 0's 기술 건물, the order was taken, 공격력 reached
        /// level 1, and after that a peer 1 body lost exactly what peer 0's archer
        /// hits for plus one.
        /// </summary>
        private static void ScriptedLogResearches()
        {
            List<Command>[] log = ScriptedLog.Build();
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            int researched = FactionData.Stats(ScriptedLog.Peer0Faction, Role.Ranged).Damage + World.AttackPerLevel;

            int tech = -1;
            bool taken = false, bonusHit = false;
            var hp = new List<int>();
            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                foreach (Command c in log[t])
                {
                    if (c.Type != CommandType.Research) continue;
                    tech = c.EntityId;
                    taken = world.GetEntity(tech).ResearchIndex == c.Arg;
                }

                for (int i = 0; i < world.EntityCount; i++)
                {
                    Entity e = world.GetEntity(i);
                    if (i == hp.Count) hp.Add(e.Hp);
                    if (e.Owner == 1 && hp[i] - e.Hp == researched && world.UpgradeLevel(0, 0) > 0) bonusHit = true;
                    hp[i] = e.Hp;
                }
            }

            Check(tech >= 0, "the scripted log carries no Research");
            Entity b = world.GetEntity(tech);
            Check(b.Alive && b.Owner == 0 && b.Role == Role.Tech, "the scripted log's Research names entity " + tech +
                ", which is not peer 0's 기술 건물");
            Check(taken, "the scripted log's Research was refused");
            Check(world.UpgradeLevel(0, 0) == 1, "the scripted log's research never landed inside the log");
            Check(bonusHit, "no hit inside the scripted log carried the researched 공격력");
        }

        // ---------------------------------------------------------------------

        private static readonly List<Command> Idle = new List<Command>();

        /// <summary>
        /// Peer 0 of the named faction with a finished 기술 건물 and a deep bank.
        /// Peer 1 is another faction and owns nothing, so nothing fights unless a
        /// check puts something on the map.
        /// </summary>
        private static World TechWorld(Faction faction, out int tech, int resources = 10000)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, faction);
            world.SetPeerFaction(1, faction == Faction.Hellfire ? Faction.TreeSpirits : Faction.Hellfire);
            world.GrantResources(0, resources);
            world.GrantResources(1, 10000);
            tech = world.SpawnBuilding(0, Role.Tech, At(20, 20), complete: true);
            return world;
        }

        /// <summary>Starts one level of this row and steps until it lands.</summary>
        private static void ResearchToCompletion(World world, int tech, int index)
        {
            Entity b = world.GetEntity(tech);
            int level = world.UpgradeLevel(b.Owner, index);
            world.Step(Research(b.Owner, 100 + world.Tick, tech, index));
            Check(world.GetEntity(tech).ResearchIndex == index, "research of row " + index + " was refused");
            Run(world, FactionData.Upgrade(world.FactionOf(b.Owner), index).Ticks - 1);
            Check(world.UpgradeLevel(b.Owner, index) == level + 1, "research of row " + index + " never landed");
        }

        /// <summary>
        /// One order, and a check that it changed nothing: the bank and every
        /// research field on the named building are what they were.
        /// </summary>
        private static void Refused(World world, int building, int index, string message)
        {
            int banked0 = world.GetResources(0), banked1 = world.GetResources(1);
            Entity before = world.GetEntity(building);
            world.Step(Research(0, 200 + world.Tick, building, index));
            Entity after = world.GetEntity(building);
            Check(world.GetResources(0) == banked0 && world.GetResources(1) == banked1, message + " (resources moved)");
            Check(after.ResearchIndex == before.ResearchIndex && after.ResearchTicksLeft <= before.ResearchTicksLeft,
                message + " (the building took it)");
        }

        /// <summary>
        /// Damage of the first hit a 돌 골렘 melee unit lands on a 지옥불 supply
        /// building, with the attacker's peer at one 공격력 level and the target's
        /// at one 방어력 level. A building because it never fights back and never
        /// moves; 돌 골렘 because its melee row is the shared one.
        /// </summary>
        private static int FirstHit(int attackLevels, int armorLevels)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SetPeerFaction(1, Faction.Hellfire);
            world.GrantResources(0, 10000);
            world.GrantResources(1, 10000);
            int mine = world.SpawnBuilding(0, Role.Tech, At(10, 10), complete: true);
            int theirs = world.SpawnBuilding(1, Role.Tech, At(50, 50), complete: true);
            for (int i = 0; i < attackLevels; i++) ResearchToCompletion(world, mine, 0);
            for (int i = 0; i < armorLevels; i++) ResearchToCompletion(world, theirs, 1);

            int target = world.SpawnBuilding(1, Role.Supply, At(30, 30), complete: true);
            world.SpawnUnit(0, Role.Melee, At(31, 30));
            int full = world.GetEntity(target).Hp;
            for (int t = 0; t < 60 && world.GetEntity(target).Hp == full; t++) world.Step(Idle);
            return full - world.GetEntity(target).Hp;
        }

        private static List<Command> Research(int peer, int seq, int building, int index) =>
            One(peer, seq, CommandType.Research, building, index);

        private static List<Command> Cancel(int peer, int seq, int building) =>
            One(peer, seq, CommandType.CancelProduction, building, 0);

        private static List<Command> One(int peer, int seq, CommandType type, int entity, int arg) =>
            new List<Command> { new Command(0, peer, seq, type, entity, FixVec2.Zero, arg) };

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
