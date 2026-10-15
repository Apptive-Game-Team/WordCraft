using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #203's water field checks: docs/TECH-TREE.md 물 지대 규칙. Who lays
    /// a field (샘물 운반자, 솟는 샘), how long it lies, and the three things it
    /// does to the 물 슬라임 standing on it: armor against a normal attack and
    /// never against fixed damage, 급류 speed, and 물기둥 분수 reach.
    /// 물 지대 깔기 and 해일 부름 lay fields too, and come with Cast (묶음 8).
    /// </summary>
    internal static class WaterFieldChecks
    {
        public static void Check()
        {
            ACarrierLaysThreeByThreeEveryTwentyTicks();
            AFieldIsLaidOnOpenGroundInsideTheMap();
            AWriteNeverShortensAField();
            ASpringKeepsFiveByFiveWhileItStands();
            AFieldLapsesOnItsTick();
            RapidsRunAtHalfACellOnAField();
            TheFieldArmorsAgainstNormalAttacksOnly();
            FieldArmorResearchRaisesItToTwo();
            FixedDamageIgnoresTheFieldArmor();
            TheFountainReachesOneFurtherFromAField();
            TwoWorldsAgree();
            TheHashSeesTheWaterField();
        }

        // ---------------------------------------------------------------------
        // Laying

        /// <summary>
        /// A 샘물 운반자 at (30, 30), on a tick 20 divides: the nine cells around
        /// it lapse 200 ticks on and the cells two off have nothing. The ticks
        /// between leave the grid alone, and the next period moves the nine 20
        /// ticks later.
        /// </summary>
        private static void ACarrierLaysThreeByThreeEveryTwentyTicks()
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SpawnUnit(0, Role.Signature, 3, At(30, 30));
            while (world.Tick % 20 != 0) Step(world);
            int t0 = world.Tick;
            Step(world);
            for (int y = 28; y <= 32; y++)
            {
                for (int x = 28; x <= 32; x++)
                {
                    bool inside = x >= 29 && x <= 31 && y >= 29 && y <= 31;
                    int want = inside ? t0 + 200 : 0;
                    Check(world.WaterFieldExpiry(Cell(x, y)) == want,
                        "cell (" + x + ", " + y + ") lapses on " + world.WaterFieldExpiry(Cell(x, y)) + ", not " + want);
                }
            }
            for (int t = 1; t < 20; t++)
            {
                Step(world);
                Check(world.WaterFieldExpiry(Cell(30, 30)) == t0 + 200, "the carrier laid a field " + t + " ticks after its period");
            }
            Step(world);
            Check(world.WaterFieldExpiry(Cell(31, 31)) == t0 + 220, "the next period did not lay the field again");
        }

        /// <summary>
        /// A carrier in the corner lays only the four cells inside the map, and
        /// a water or rock cell beside it takes no field.
        /// </summary>
        private static void AFieldIsLaidOnOpenGroundInsideTheMap()
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SetTerrain(31, 30, TileKind.Water);
            world.SetTerrain(29, 30, TileKind.Rock);
            world.SpawnUnit(0, Role.Signature, 3, At(0, 0));
            world.SpawnUnit(0, Role.Signature, 3, At(30, 30));
            while (world.Tick % 20 != 0) Step(world);
            Step(world);
            int laid = 0;
            for (int c = 0; c < World.GridCells; c++) if (world.WaterFieldExpiry(c) != 0) laid++;
            Check(laid == 4 + 7, laid + " cells took a field, not the corner's 4 and the open 7 of the other nine");
            Check(world.WaterFieldExpiry(Cell(31, 30)) == 0 && world.WaterFieldExpiry(Cell(29, 30)) == 0,
                "a water or rock cell took a field");
        }

        /// <summary>A cell already lapsing 500 ticks on keeps that when a carrier lays its 200.</summary>
        private static void AWriteNeverShortensAField()
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SpawnUnit(0, Role.Signature, 3, At(30, 30));
            while (world.Tick % 20 != 0) Step(world);
            world.SetWaterFieldForScenario(Cell(30, 31), world.Tick + 500);
            int longer = world.Tick + 500;
            Step(world);
            Check(world.WaterFieldExpiry(Cell(30, 31)) == longer, "the carrier shortened a longer field");
        }

        /// <summary>
        /// A finished 솟는 샘 at (30, 30), on a tick 40 divides: the 25 cells
        /// within 2 lapse 80 ticks on, the cells 3 off have nothing. A spring
        /// still under construction lays nothing.
        /// </summary>
        private static void ASpringKeepsFiveByFiveWhileItStands()
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SpawnBuilding(0, Role.Defense, 1, At(30, 30), complete: true);
            while (world.Tick % 40 != 0) Step(world);
            int t0 = world.Tick;
            Step(world);
            int laid = 0;
            for (int c = 0; c < World.GridCells; c++) if (world.WaterFieldExpiry(c) != 0) laid++;
            Check(laid == 25, "the spring laid " + laid + " cells, not 25");
            Check(world.WaterFieldExpiry(Cell(28, 32)) == t0 + 80, "the spring's corner lapses on " + world.WaterFieldExpiry(Cell(28, 32)));
            Check(world.WaterFieldExpiry(Cell(33, 30)) == 0, "the spring reached 3 cells");
            Run(world, 200);
            Check(world.HasWaterField(Cell(30, 30)), "the field under a standing spring lapsed");

            World site = Two(Faction.WaterSlimes, Faction.Hellfire);
            site.SpawnBuilding(0, Role.Defense, 1, At(30, 30), complete: false);
            while (site.Tick % 40 != 0) Step(site);
            Step(site);
            Check(site.WaterFieldExpiry(Cell(30, 30)) == 0, "a spring under construction laid a field");
        }

        /// <summary>
        /// A field lapsing on tick t is there on tick t - 1 and gone on t, and
        /// the value stays in the grid, the way debris does.
        /// </summary>
        private static void AFieldLapsesOnItsTick()
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            int cell = Cell(30, 30);
            world.SetWaterFieldForScenario(cell, 5);
            Run(world, 4);
            Check(world.HasWaterField(cell), "the field lapsed before its tick");
            Step(world);
            Check(!world.HasWaterField(cell), "the field outlived its tick");
            Check(world.WaterFieldExpiry(cell) == 5, "a lapsed field was swept from the grid");
        }

        // ---------------------------------------------------------------------
        // Reading

        /// <summary>
        /// A 급류 슬라임 walking east along a field: 1/2 a tick with 급류, 5/16
        /// without the research, and 5/16 off the field with it.
        /// </summary>
        private static void RapidsRunAtHalfACellOnAField()
        {
            long half = Fix.Ratio(1, 2).Raw, own = Fix.Ratio(5, 16).Raw;
            Check(RapidsStep(research: true, field: true) == half, "급류 on a field did not walk 1/2");
            Check(RapidsStep(research: false, field: true) == own, "급류 worked before its research");
            Check(RapidsStep(research: true, field: false) == own, "급류 worked off a field");
        }

        /// <summary>The third step of a walk east, past the route's first waypoint.</summary>
        private static long RapidsStep(bool research, bool field)
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            if (research) AbilityChecks.Research(world, 0, UpgradeEffect.Rapids);
            int slime = world.SpawnUnit(0, Role.Melee, 2, At(30, 30));
            if (field)
                for (int x = 28; x < 50; x++) world.SetWaterFieldForScenario(Cell(x, 30), world.Tick + 1000);
            Step(world, new List<Command> { new Command(world.Tick, 0, 3000, CommandType.Move, slime, At(45, 30)) });
            Step(world);
            FixVec2 from = world.GetEntity(slime).Position;
            Step(world);
            return (world.GetEntity(slime).Position - from).Magnitude.Raw;
        }

        /// <summary>
        /// A held 거품 정령 shot by a 균열 파수병 (6) loses one less on a field
        /// than off it. In a mirror match it is the same one less: the field asks
        /// the faction of the body on it and nothing about who laid it.
        /// </summary>
        private static void TheFieldArmorsAgainstNormalAttacksOnly()
        {
            int off = ShotTaken(Faction.Hellfire, research: false, field: false);
            Check(off == 6 - FactionData.Stats(Faction.WaterSlimes, Role.Melee, 0).Armor,
                "the shot off the field took " + off + ", not 6 less the 거품 정령's armor");
            Check(ShotTaken(Faction.Hellfire, research: false, field: true) == off - 1, "the field did not take 1 off the shot");
            Check(ShotTaken(Faction.WaterSlimes, research: false, field: true) == off - 1, "the field armored only one side of a mirror match");
        }

        /// <summary>물 지대 방어: two less on a field, nothing less off it.</summary>
        private static void FieldArmorResearchRaisesItToTwo()
        {
            int off = ShotTaken(Faction.Hellfire, research: false, field: false);
            Check(ShotTaken(Faction.Hellfire, research: true, field: true) == off - 2, "물 지대 방어 did not take 2 off the shot");
            Check(ShotTaken(Faction.Hellfire, research: true, field: false) == off, "물 지대 방어 armored a body off the field");
        }

        /// <summary>
        /// What a 거품 정령 held on (30, 30) loses to the first shot of the
        /// shooter's Ranged 1 standing 5 cells east: 균열 파수병 or, in a mirror
        /// match, 해일 인도자, both 6.
        /// </summary>
        private static int ShotTaken(Faction shooter, bool research, bool field)
        {
            World world = Two(Faction.WaterSlimes, shooter);
            if (research) AbilityChecks.Research(world, 0, UpgradeEffect.WaterFieldArmor);
            int bubble = world.SpawnUnit(0, Role.Melee, 0, At(30, 30));
            Step(world, new List<Command> { new Command(world.Tick, 0, 3000, CommandType.HoldPosition, bubble, FixVec2.Zero) });
            if (field) world.SetWaterFieldForScenario(Cell(30, 30), world.Tick + 1000);
            int gun = world.SpawnUnit(1, Role.Ranged, 1, At(35, 30));
            int hp = world.GetEntity(bubble).Hp;
            for (int t = 0; t < 30 && world.GetEntity(bubble).Hp == hp; t++) Step(world);
            Check(world.GetEntity(gun).Alive, "the shooter fell, so the check proves nothing");
            return hp - world.GetEntity(bubble).Hp;
        }

        /// <summary>
        /// Two 물 슬라임 workers on a field beside a 용암 갑각 악마: the one it
        /// does not swing at loses exactly 2 to 화상 오라 on a tick 20 divides,
        /// field or no field. A 용암 투석 악마 shot (16) at a worker on a field
        /// deals 15 and splashes 7 on the worker beside it, also on a field:
        /// half the shot, not asked for that worker's armor again.
        /// </summary>
        private static void FixedDamageIgnoresTheFieldArmor()
        {
            Check(AuraTaken(field: true) == 2, "화상 오라 on a field did not take 2");
            Check(AuraTaken(field: false) == 2, "화상 오라 off a field did not take 2");

            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            int target = world.SpawnWorker(0, At(38, 30));
            int beside = world.SpawnWorker(0, At(39, 30));
            world.SetWaterFieldForScenario(Cell(38, 30), 1000);
            world.SetWaterFieldForScenario(Cell(39, 30), 1000);
            world.SpawnUnit(1, Role.Ranged, 2, At(30, 30));
            int hp = world.GetEntity(target).Hp, besideHp = world.GetEntity(beside).Hp;
            Step(world);
            Check(hp - world.GetEntity(target).Hp == 15, "the shot at a worker on a field dealt " + (hp - world.GetEntity(target).Hp) + ", not 15");
            Check(besideHp - world.GetEntity(beside).Hp == 7, "the splash on a worker on a field dealt " + (besideHp - world.GetEntity(beside).Hp) + ", not 7");
        }

        private static int AuraTaken(bool field)
        {
            World world = Two(Faction.WaterSlimes, Faction.Hellfire);
            world.SpawnWorker(0, At(31, 30));
            int far = world.SpawnWorker(0, At(30, 32));
            if (field)
                for (int x = 29; x <= 32; x++)
                    for (int y = 29; y <= 33; y++) world.SetWaterFieldForScenario(Cell(x, y), 5000);
            world.SpawnUnit(1, Role.Melee, 0, At(30, 30));
            while (world.Tick % 20 != 0) Step(world);
            int hp = world.GetEntity(far).Hp;
            Step(world);
            return hp - world.GetEntity(far).Hp;
        }

        /// <summary>
        /// A finished 물기둥 분수 (reach 6) reaches 7 standing on a field, and
        /// shoots an enemy worker 7 cells off only then.
        /// </summary>
        private static void TheFountainReachesOneFurtherFromAField()
        {
            foreach (bool field in new[] { false, true })
            {
                World world = Two(Faction.WaterSlimes, Faction.Hellfire);
                int fountain = world.SpawnBuilding(0, Role.Defense, 0, At(30, 30), complete: true);
                if (field) world.SetWaterFieldForScenario(Cell(30, 30), 1000);
                int prey = world.SpawnWorker(1, At(37, 30));
                Fix want = Fix.FromInt(field ? 7 : 6);
                Check(world.RangeFor(world.GetEntity(fountain)) == want,
                    "the fountain " + (field ? "on" : "off") + " a field reaches " + world.RangeFor(world.GetEntity(fountain)));
                int hp = world.GetEntity(prey).Hp;
                Run(world, 3);
                Check((world.GetEntity(prey).Hp < hp) == field,
                    "the fountain " + (field ? "on a field did not shoot" : "off a field shot") + " 7 cells");
            }
        }

        // ---------------------------------------------------------------------
        // Determinism

        /// <summary>
        /// The mixed water match twice from scratch: the same hash on every tick,
        /// and a run that laid fields and mended the 대포, or it proves nothing.
        /// </summary>
        private static void TwoWorldsAgree()
        {
            ulong a = Mixed(out int laid, out int mended), b = Mixed(out _, out _);
            Check(a == b, "two runs of the water field hash apart: 0x" + a.ToString("X16") + " against 0x" + b.ToString("X16"));
            Check(laid > 0, "no cell took a field in the mixed match");
            Check(mended > 0, "수리 mended nothing in the mixed match");
        }

        /// <summary>
        /// 물 슬라임 peer 0 with 급류 researched, two 샘물 운반자 walking with
        /// 급류 슬라임, a 솟는 샘 and a 물기둥 분수; 인간 peer 1 with a 마도
        /// 폭격선, crossbows and a 공방 기술자 beside a damaged 대포, 600 ticks.
        /// </summary>
        private static ulong Mixed(out int laid, out int mended)
        {
            World world = Two(Faction.WaterSlimes, Faction.Humans);
            AbilityChecks.Research(world, 0, UpgradeEffect.Rapids);
            world.SpawnBuilding(0, Role.Defense, 1, At(28, 30), complete: true);
            world.SpawnBuilding(0, Role.Defense, 0, At(30, 30), complete: true);
            var orders = new List<Command>();
            for (int i = 0; i < 2; i++)
            {
                int carrier = world.SpawnUnit(0, Role.Signature, 3, At(26, 28 + 4 * i));
                int rapid = world.SpawnUnit(0, Role.Melee, 2, At(27, 28 + 4 * i));
                orders.Add(new Command(world.Tick, 0, 3000 + 2 * i, CommandType.Move, carrier, At(38, 28 + 4 * i)));
                orders.Add(new Command(world.Tick, 0, 3001 + 2 * i, CommandType.AttackMove, rapid, At(44, 28 + 4 * i)));
                world.SpawnUnit(1, Role.Ranged, 1, At(44, 29 + 2 * i));
            }
            world.SpawnUnit(1, Role.Ranged, 5, At(42, 30));
            int cannon = world.SpawnBuilding(1, Role.Defense, 0, At(46, 30), complete: true);
            world.SetHpForScenario(cannon, 100);
            world.SpawnUnit(1, Role.Signature, 2, At(47, 31));
            Step(world, orders);
            int cannonHp = world.GetEntity(cannon).Hp;
            ulong folded = AbilityChecks.EveryTickHashed(world, 600);
            laid = 0;
            for (int c = 0; c < World.GridCells; c++) if (world.WaterFieldExpiry(c) != 0) laid++;
            mended = world.GetEntity(cannon).Hp - cannonHp;
            return folded;
        }

        /// <summary>
        /// Two worlds one cell's field expiry apart hash apart, and so do one
        /// with a field and one without, before any field has lapsed.
        /// </summary>
        private static void TheHashSeesTheWaterField()
        {
            World plain = Two(Faction.WaterSlimes, Faction.Hellfire);
            World laid = Two(Faction.WaterSlimes, Faction.Hellfire);
            World later = Two(Faction.WaterSlimes, Faction.Hellfire);
            laid.SetWaterFieldForScenario(Cell(30, 30), 100);
            later.SetWaterFieldForScenario(Cell(30, 30), 101);
            Check(plain.Hash() != laid.Hash(), "a field is not hashed");
            Check(laid.Hash() != later.Hash(), "a field's expiry is not hashed");
            World elsewhere = Two(Faction.WaterSlimes, Faction.Hellfire);
            elsewhere.SetWaterFieldForScenario(Cell(31, 30), 100);
            Check(laid.Hash() != elsewhere.Hash(), "a field's cell is not hashed");
        }

        // ---------------------------------------------------------------------

        private static void Step(World world, List<Command> commands = null) => AbilityChecks.Step(world, commands);

        private static void Run(World world, int ticks) => AbilityChecks.Run(world, ticks);

        private static int Cell(int x, int y) => AbilityChecks.Cell(x, y);

        private static FixVec2 At(int x, int y) => AbilityChecks.At(x, y);

        private static World Two(Faction zero, Faction one) => AbilityChecks.Two(zero, one);

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
