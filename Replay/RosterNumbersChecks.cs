using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #179's checks: the existing roster rows carry the numbers and flags
    /// the documents give them. Kept out of Program.cs the way the other
    /// per-issue files are.
    ///
    /// Five kinds of question. The numbers: every row docs/TECH-TREE.md marks †,
    /// and every row this issue gave a flag, is written out here column by column
    /// and compared with FactionData, because a check that read the table back
    /// against itself would pin nothing. The fliers: 바람 정령 and 구름 용 cross
    /// rock and a cliff that stop a walker of their own faction. The ground-only
    /// defenses: 용암 아가리 and 굴림바위 언덕 neither take an attack order at a
    /// flier nor acquire one, while a defense that reaches the air shoots the same
    /// flier. 마도 정찰기: it refuses both attack orders and never acquires. And
    /// the AI: with tier 3 open, 인간 still buys an armed body over the scout.
    /// </summary>
    internal static class RosterNumbersChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            EveryDocumentedUnitNumberIsInTheTable();
            EveryDocumentedBuildingNumberIsInTheTable();
            EveryDocumentedFlagIsInTheTable();
            TheNewFliersCrossRockAndCliffs();
            TheGroundOnlyDefensesRefuseAir();
            TheScoutNeverFights();
            TheAiBuysAnArmedBodyOverTheScout();
        }

        // ---------------------------------------------------------------------
        // The numbers

        /// <summary>
        /// One unit row of docs/TECH-TREE.md: 자원, 틱, 체력, 피해, 사거리, 주기 and
        /// 속도 as the table gives them. 없음 is written as 0, which is what the
        /// roster holds for a body with no weapon. Speed is a fraction of a cell
        /// per tick, numerator and denominator, because the table writes it so.
        /// </summary>
        private struct UnitRow
        {
            public Faction Faction;
            public Role Role;
            public int Slot;
            public string Name;
            public int Resources, Ticks, Hp, Damage, Range, AttackTicks, SpeedNum, SpeedDen;
        }

        private static UnitRow Unit(Faction faction, Role role, int slot, string name,
            int resources, int ticks, int hp, int damage, int range, int attackTicks,
            int speedNum, int speedDen) =>
            new UnitRow
            {
                Faction = faction, Role = role, Slot = slot, Name = name,
                Resources = resources, Ticks = ticks, Hp = hp, Damage = damage, Range = range,
                AttackTicks = attackTicks, SpeedNum = speedNum, SpeedDen = speedDen,
            };

        /// <summary>
        /// Every unit row with a † in docs/TECH-TREE.md, and the three rows this
        /// issue gave a flag. The † columns are the docs/FACTION-MECHANICS.md
        /// values; the rest of each row is the shared row the table quotes, and is
        /// pinned too, because an override row restates the whole row and can
        /// mistype any column of it.
        /// </summary>
        private static readonly UnitRow[] DocumentedUnits =
        {
            //                                                               자원 틱  체력 피해 사거리 주기 속도
            Unit(Faction.TreeSpirits, Role.Worker, 0, "풀씨 정령",           40, 30, 60, 0, 0, 0, 1, 4),
            Unit(Faction.TreeSpirits, Role.Signature, 0, "바람 정령",        20, 40, 130, 10, 3, 22, 3, 8),
            Unit(Faction.Hellfire, Role.Melee, 0, "용암 갑각 악마",          90, 80, 260, 14, 2, 15, 1, 6),
            Unit(Faction.WaterSlimes, Role.Melee, 0, "거품 정령",            35, 30, 130, 6, 2, 15, 1, 4),
            Unit(Faction.WaterSlimes, Role.Ranged, 0, "물결 궁수",           55, 45, 70, 4, 6, 18, 1, 4),
            Unit(Faction.WaterSlimes, Role.Signature, 0, "구름 용",          20, 40, 130, 10, 3, 22, 3, 8),
            Unit(Faction.RockGolems, Role.Worker, 0, "꼬마돌",               40, 35, 80, 0, 0, 0, 1, 4),
            Unit(Faction.RockGolems, Role.Melee, 0, "이끼바위 골렘",         80, 70, 300, 9, 2, 15, 1, 6),
            Unit(Faction.RockGolems, Role.Ranged, 0, "바위 술사",            70, 60, 120, 7, 5, 18, 1, 4),
            Unit(Faction.Humans, Role.Worker, 0, "수습 마법생",              45, 35, 55, 0, 0, 0, 1, 4),
            Unit(Faction.Humans, Role.Ranged, 0, "공수 특공대",              65, 55, 90, 7, 5, 18, 1, 4),
            Unit(Faction.Humans, Role.Signature, 1, "마도 정찰기",           20, 40, 130, 0, 0, 0, 3, 8),
        };

        private static void EveryDocumentedUnitNumberIsInTheTable()
        {
            foreach (UnitRow row in DocumentedUnits)
            {
                Check(FactionData.Has(row.Faction, row.Role, row.Slot),
                    row.Name + " is not fielded at " + Where(row.Faction, row.Role, row.Slot));
                Check(FactionData.Name(row.Faction, row.Role, row.Slot) == row.Name,
                    Where(row.Faction, row.Role, row.Slot) + " is " +
                    FactionData.Name(row.Faction, row.Role, row.Slot) + ", not " + row.Name);

                ProductionCost cost = FactionData.Production(row.Faction, row.Role, row.Slot);
                Check(cost.Produced, row.Name + " is off the production list");
                Check(cost.Resources == row.Resources,
                    row.Name + " costs " + cost.Resources + ", the document says " + row.Resources);
                Check(cost.Ticks == row.Ticks,
                    row.Name + " takes " + cost.Ticks + " ticks, the document says " + row.Ticks);

                UnitStats s = FactionData.Stats(row.Faction, row.Role, row.Slot);
                Check(s.Hp == row.Hp, row.Name + " has " + s.Hp + " hp, the document says " + row.Hp);
                Check(s.Damage == row.Damage,
                    row.Name + " hits for " + s.Damage + ", the document says " + row.Damage);
                Check(s.Range == Fix.FromInt(row.Range),
                    row.Name + " reaches " + s.Range.ToInt() + " cells, the document says " + row.Range);
                Check(s.AttackTicks == row.AttackTicks,
                    row.Name + " fires every " + s.AttackTicks + " ticks, the document says " + row.AttackTicks);
                Check(s.Speed == Fix.Ratio(row.SpeedNum, row.SpeedDen),
                    row.Name + " walks at raw " + s.Speed.Raw + ", the document says " +
                    row.SpeedNum + "/" + row.SpeedDen);
                Check(s.Footprint == FactionData.BodyRadius,
                    row.Name + " walks without the shared footprint");
            }
        }

        /// <summary>One building row of docs/TECH-TREE.md, with its weapon if it has one.</summary>
        private struct BuildingRow
        {
            public Faction Faction;
            public Role Role;
            public int Slot;
            public string Name;
            public int Resources, Ticks, Hp, Damage, Range, AttackTicks;
        }

        private static BuildingRow Building(Faction faction, Role role, int slot, string name,
            int resources, int ticks, int hp, int damage, int range, int attackTicks) =>
            new BuildingRow
            {
                Faction = faction, Role = role, Slot = slot, Name = name,
                Resources = resources, Ticks = ticks, Hp = hp, Damage = damage, Range = range,
                AttackTicks = attackTicks,
            };

        /// <summary>
        /// The production 마법 탑, whose 체력 220 is the one † in a building table,
        /// and the two defenses this issue made ground-only.
        /// </summary>
        private static readonly BuildingRow[] DocumentedBuildings =
        {
            //                                                                    자원 틱 체력 피해 사거리 주기
            Building(Faction.Humans, Role.Production, 0, "마법 탑",               60, 80, 220, 0, 0, 0),
            Building(Faction.Hellfire, Role.Defense, 0, "용암 아가리",            100, 60, 300, 9, 6, 20),
            Building(Faction.RockGolems, Role.Defense, 0, "굴림바위 언덕",        100, 60, 300, 9, 6, 20),
        };

        private static void EveryDocumentedBuildingNumberIsInTheTable()
        {
            foreach (BuildingRow row in DocumentedBuildings)
            {
                Check(FactionData.Name(row.Faction, row.Role, row.Slot) == row.Name,
                    Where(row.Faction, row.Role, row.Slot) + " is " +
                    FactionData.Name(row.Faction, row.Role, row.Slot) + ", not " + row.Name);
                Check(FactionData.BuildCost(row.Faction, row.Role, row.Slot) == row.Resources,
                    row.Name + " costs " + FactionData.BuildCost(row.Faction, row.Role, row.Slot) +
                    ", the document says " + row.Resources);
                Check(FactionData.BuildTicks(row.Faction, row.Role, row.Slot) == row.Ticks,
                    row.Name + " takes " + FactionData.BuildTicks(row.Faction, row.Role, row.Slot) +
                    " ticks, the document says " + row.Ticks);

                UnitStats s = FactionData.Stats(row.Faction, row.Role, row.Slot);
                Check(s.Hp == row.Hp, row.Name + " has " + s.Hp + " hp, the document says " + row.Hp);
                Check(s.Damage == row.Damage,
                    row.Name + " hits for " + s.Damage + ", the document says " + row.Damage);
                Check(s.Range == Fix.FromInt(row.Range),
                    row.Name + " reaches " + s.Range.ToInt() + " cells, the document says " + row.Range);
                Check(s.AttackTicks == row.AttackTicks,
                    row.Name + " fires every " + s.AttackTicks + " ticks, the document says " + row.AttackTicks);
            }

            // The supply 마법 탑 is another entry with the same name, and no
            // document gives it a number: the 220 must not have reached it.
            Check(FactionData.Stats(Faction.Humans, Role.Supply, 0).Hp == 200,
                "the supply 마법 탑 took the production one's 220");
        }

        /// <summary>
        /// The 이동 and 대공/대지 columns for every row this issue touched, and for
        /// the three rows docs/TECH-TREE.md proposes changing and this issue did not:
        /// 공수 특공대 and 돌 포탑 still reach the air, and 마도 정찰기 still walks.
        /// Those three are pinned so a later change to them has to be made on
        /// purpose, here, rather than slipped in with something else.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, string Name, bool Air, bool HitsAir, bool HitsGround)[]
            DocumentedFlags =
        {
            (Faction.TreeSpirits, Role.Signature, 0, "바람 정령", true, true, true),
            (Faction.WaterSlimes, Role.Signature, 0, "구름 용", true, true, true),
            (Faction.Hellfire, Role.Defense, 0, "용암 아가리", false, false, true),
            (Faction.RockGolems, Role.Defense, 0, "굴림바위 언덕", false, false, true),
            (Faction.Humans, Role.Signature, 1, "마도 정찰기", false, false, false),
            (Faction.Humans, Role.Ranged, 0, "공수 특공대", false, true, true),
            (Faction.Humans, Role.Defense, 2, "돌 포탑", false, true, true),
        };

        private static void EveryDocumentedFlagIsInTheTable()
        {
            foreach (var row in DocumentedFlags)
            {
                Check(FactionData.Name(row.Faction, row.Role, row.Slot) == row.Name,
                    Where(row.Faction, row.Role, row.Slot) + " is " +
                    FactionData.Name(row.Faction, row.Role, row.Slot) + ", not " + row.Name);
                UnitStats s = FactionData.Stats(row.Faction, row.Role, row.Slot);
                Check(s.Air == row.Air, row.Name + (s.Air ? " flies" : " walks"));
                Check(s.HitsAir == row.HitsAir,
                    row.Name + (s.HitsAir ? " reaches" : " does not reach") + " the air");
                Check(s.HitsGround == row.HitsGround,
                    row.Name + (s.HitsGround ? " reaches" : " does not reach") + " the ground");
            }
        }

        // ---------------------------------------------------------------------
        // The fliers

        private const int CrossTicks = 120;

        /// <summary>
        /// 공중 유닛은 지형과 잔해를 무시하고 이동한다, asked of the two new fliers.
        /// The two rules are asked straight first, IsPassable for a rock cell and
        /// StepPassable for a climb of two levels, with the walker's answer as the
        /// control. Then each flier is ordered across a rock wall and, in a second
        /// world, across a cliff that runs the height of the map, beside a melee
        /// unit of its own faction given the same order. The walker stopping is
        /// what makes the flier crossing mean something.
        /// </summary>
        private static void TheNewFliersCrossRockAndCliffs()
        {
            foreach (Faction faction in new[] { Faction.TreeSpirits, Faction.WaterSlimes })
            {
                string name = FactionData.Name(faction, Role.Signature, 0);
                foreach (bool cliff in new[] { false, true })
                {
                    string wall = cliff ? "a cliff" : "a rock wall";
                    var world = new World(Seed);
                    world.SetPeerFaction(0, faction);
                    for (int y = 0; y < World.GridSize; y++)
                    {
                        if (cliff)
                        {
                            for (int x = 32; x < World.GridSize; x++) world.SetHeight(x, y, 2);
                        }
                        else
                        {
                            world.SetTerrain(32, y, TileKind.Rock);
                        }
                    }

                    int flier = world.SpawnUnit(0, Role.Signature, 0, At(30, 30));
                    int walker = world.SpawnUnit(0, Role.Melee, 0, At(30, 34));
                    bool air = world.Flies(world.GetEntity(flier));
                    Check(air, name + " does not fly");
                    Check(!world.Flies(world.GetEntity(walker)), "the control walker flies");

                    int low = World.CellOf(At(31, 30));
                    int high = World.CellOf(At(32, 30));
                    if (cliff)
                    {
                        Check(world.StepPassable(low, high, air), name + " cannot climb " + wall);
                        Check(!world.StepPassable(low, high, false),
                            "a walker climbs " + wall + ", so it proves nothing");
                    }
                    else
                    {
                        Check(world.IsPassable(high, air), name + " cannot enter " + wall);
                        Check(!world.IsPassable(high, false),
                            "a walker enters " + wall + ", so it proves nothing");
                    }

                    var orders = new List<Command>
                    {
                        new Command(0, 0, 0, CommandType.Move, flier, At(40, 30)),
                        new Command(0, 0, 1, CommandType.Move, walker, At(40, 34)),
                    };
                    for (int t = 0; t < CrossTicks; t++) world.Step(t == 0 ? orders : new List<Command>());

                    Check(world.GetEntity(flier).Position.X > Fix.FromInt(33),
                        name + " stopped at " + wall);
                    Check(world.GetEntity(walker).Position.X < Fix.FromInt(32),
                        "the walker crossed " + wall + ", so it proves nothing");
                }
            }
        }

        // ---------------------------------------------------------------------
        // The ground-only defenses

        private const int TurretTicks = 200;

        /// <summary>
        /// 용암 아가리 and 굴림바위 언덕 are 지상 전용 per docs/FACTIONS.md 공중
        /// 규칙. Each stands with a 구름 용 three cells off and a 거품 정령 four
        /// cells off, so the flier is the nearer body and only the reach rule
        /// explains a turret that picks the walker. An attack order naming the
        /// flier is dropped on the tick it is given; one naming the walker holds.
        /// Then nothing is ordered and the turret has to leave the flier whole and
        /// hurt the walker.
        ///
        /// The control is 세계수 정령's 정령 뇌우목, the shared defense row that
        /// reaches both, in the same world: it has to hurt the same flier, or a 구름
        /// 용 nothing could touch would pass every line above.
        /// </summary>
        private static void TheGroundOnlyDefensesRefuseAir()
        {
            foreach (Faction faction in new[] { Faction.Hellfire, Faction.RockGolems, Faction.TreeSpirits })
            {
                bool groundOnly = faction != Faction.TreeSpirits;
                string name = FactionData.Name(faction, Role.Defense, 0);

                var world = new World(Seed);
                world.SetPeerFaction(0, faction);
                world.SetPeerFaction(1, Faction.WaterSlimes);
                int turret = world.SpawnBuilding(0, Role.Defense, 0, At(30, 30), complete: true);
                int flier = world.SpawnUnit(1, Role.Signature, 0, At(33, 30)); // 구름 용
                int walker = world.SpawnUnit(1, Role.Melee, 0, At(26, 30));    // 거품 정령
                Check(world.Flies(world.GetEntity(flier)), "the 구름 용 does not fly");

                world.Step(new List<Command>
                {
                    new Command(0, 0, 0, CommandType.Attack, turret, FixVec2.Zero, flier),
                });
                Entity t0 = world.GetEntity(turret);
                if (groundOnly)
                {
                    Check(!(t0.Mode == OrderMode.Attack && t0.TargetId == flier),
                        name + " took an attack order at a flier");
                }
                else
                {
                    Check(t0.Mode == OrderMode.Attack && t0.TargetId == flier,
                        name + " refused an attack order at a flier, so the control proves nothing");
                }

                if (groundOnly)
                {
                    world.Step(new List<Command>
                    {
                        new Command(1, 0, 1, CommandType.Attack, turret, FixVec2.Zero, walker),
                    });
                    Entity t1 = world.GetEntity(turret);
                    Check(t1.Mode == OrderMode.Attack && t1.TargetId == walker,
                        name + " refused an attack order at a walker too, so the refusal proves nothing");
                    world.Step(new List<Command>
                    {
                        new Command(2, 0, 2, CommandType.Stop, turret, FixVec2.Zero, 0),
                    });
                }

                bool tookFlier = false;
                var idle = new List<Command>();
                for (int t = 0; t < TurretTicks; t++)
                {
                    world.Step(idle);
                    if (world.GetEntity(turret).TargetId == flier) tookFlier = true;
                }

                Entity air = world.GetEntity(flier);
                Entity ground = world.GetEntity(walker);
                if (groundOnly)
                {
                    Check(!tookFlier, name + " acquired the flier");
                    Check(air.Alive && air.Hp == air.MaxHp,
                        name + " reached the air: the 구름 용 is on " + air.Hp + " of " + air.MaxHp);
                    Check(!ground.Alive || ground.Hp < ground.MaxHp, name + " never reached the ground either");
                }
                else
                {
                    Check(!air.Alive || air.Hp < air.MaxHp, name + " never reached the 구름 용");
                }
            }
        }

        // ---------------------------------------------------------------------
        // 마도 정찰기

        private const int ScoutTicks = 200;

        /// <summary>
        /// 마도 정찰기 is 정찰, 비전투 per docs/FACTIONS.md. An attack order and an
        /// attack-move are both refused, and left to itself beside an enemy worker
        /// it acquires nothing and the worker stays whole. The control is a 공수
        /// 특공대 twenty cells south with a worker of its own: it has to take the
        /// same attack order and draw blood, or a world where 인간 could not fight
        /// at all would pass.
        /// </summary>
        private static void TheScoutNeverFights()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Humans);
            world.SetPeerFaction(1, Faction.TreeSpirits);
            int scout = world.SpawnUnit(0, Role.Signature, 1, At(30, 30));
            int prey = world.SpawnWorker(1, At(32, 30));
            int commando = world.SpawnUnit(0, Role.Ranged, 0, At(30, 50));
            int commandoPrey = world.SpawnWorker(1, At(32, 50));

            Check(!world.Armed(world.GetEntity(scout)), "마도 정찰기 is armed in the world");

            world.Step(new List<Command>
            {
                new Command(0, 0, 0, CommandType.Attack, scout, FixVec2.Zero, prey),
                new Command(0, 0, 1, CommandType.Attack, commando, FixVec2.Zero, commandoPrey),
            });
            Check(world.GetEntity(scout).Mode != OrderMode.Attack,
                "an attack order put 마도 정찰기 in " + world.GetEntity(scout).Mode);
            Check(world.GetEntity(scout).TargetId < 0,
                "an attack order gave 마도 정찰기 victim " + world.GetEntity(scout).TargetId);
            Check(world.GetEntity(commando).Mode == OrderMode.Attack,
                "the 공수 특공대 refused the attack order too, so the check proves nothing");

            world.Step(new List<Command>
            {
                new Command(1, 0, 2, CommandType.AttackMove, scout, At(32, 30)),
            });
            Check(world.GetEntity(scout).Mode != OrderMode.AttackMove,
                "an attack-move put 마도 정찰기 in " + world.GetEntity(scout).Mode);

            var idle = new List<Command>();
            for (int t = 0; t < ScoutTicks; t++)
            {
                world.Step(idle);
                Check(world.GetEntity(scout).TargetId < 0,
                    "마도 정찰기 acquired " + world.GetEntity(scout).TargetId + " at tick " + t);
            }
            Entity p = world.GetEntity(prey);
            Check(p.Alive && p.Hp == p.MaxHp, "마도 정찰기 hurt the worker beside it");
            Entity cp = world.GetEntity(commandoPrey);
            Check(!cp.Alive || cp.Hp < cp.MaxHp, "the 공수 특공대 never got a shot off");
        }

        // ---------------------------------------------------------------------
        // The AI

        private const int AiTicks = 400;
        private const int ShortTicks = 30;
        private const int ShortBank = 40;

        /// <summary>
        /// An 인간 AI with a finished 공방 has tier 3 open, and the highest-tier
        /// entry its 마법 탑 makes is the unarmed 마도 정찰기. The ladder has to
        /// buy 공수 특공대 instead, on every think, because a body it will never
        /// send to attack is a body that wins nothing. Six workers stand from the
        /// start so the ladder goes straight to fighters.
        ///
        /// Two banks. First 40, which pays for the 20자원 scout and not for the
        /// 65자원 공수 특공대: the ladder has to wait rather than take the scout as
        /// the only thing it can afford. Then 5000, where both are affordable and
        /// the weapon rule alone keeps the scout off the queue.
        /// </summary>
        private static void TheAiBuysAnArmedBodyOverTheScout()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Humans);
            world.SetPeerFaction(1, Faction.TreeSpirits);
            world.SpawnBuilding(0, Role.Base, At(8, 8), complete: true);
            int tower = world.SpawnBuilding(0, Role.Production, 0, At(8, 13), complete: true);
            world.SpawnBuilding(0, Role.Tech, 0, At(13, 8), complete: true);
            for (int i = 0; i < World.AiWorkerTarget; i++) world.SpawnWorker(0, At(10 + i, 11));
            world.SpawnBuilding(1, Role.Base, At(55, 55), complete: true);
            world.GrantResources(0, ShortBank - world.GetResources(0));
            world.SetPeerAi(0, true);

            Check(world.TierOf(0) == FactionData.MaxTier, "the 공방 did not open tier 3, so this proves nothing");
            Check(FactionData.IsProducedAt(Faction.Humans, Role.Signature, 1, Role.Production, 0),
                "the 마법 탑 does not make 마도 정찰기, so this proves nothing");

            int armed = 0;
            var idle = new List<Command>();
            // Over the scout's price and under the 공수 특공대's when the short
            // phase starts.
            Check(FactionData.Production(Faction.Humans, Role.Signature, 1).Resources <= ShortBank &&
                  FactionData.Production(Faction.Humans, Role.Ranged, 0).Resources > ShortBank,
                "the short bank does not sit between the two prices, so this proves nothing");
            for (int t = 0; t < AiTicks; t++)
            {
                if (t == ShortTicks) world.GrantResources(0, 5000);
                world.Step(idle);
                Entity b = world.GetEntity(tower);
                if (b.QueueCount == 0) continue;
                Check(!(b.ProduceRole == Role.Signature && b.ProduceSlot == 1),
                    "the 인간 AI queued 마도 정찰기 on tick " + t);
                if (b.ProduceRole == Role.Ranged && b.ProduceSlot == 0) armed++;
            }
            Check(armed > 0, "the 인간 AI never queued 공수 특공대");
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                Check(!(e.Owner == 0 && e.Role == Role.Signature && e.Slot == 1),
                    "the 인간 AI fielded 마도 정찰기 as entity " + i);
            }
        }

        // ---------------------------------------------------------------------

        private static string Where(Faction faction, Role role, int slot) =>
            faction + "." + role + "[" + slot + "]";

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
