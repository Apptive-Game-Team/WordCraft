using System;
using System.Collections.Generic;
using System.Reflection;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Determinism self-check. Run with `dotnet run --project Replay`.
    /// Everything the lockstep netcode assumes has to hold here first.
    /// </summary>
    internal static class Program
    {
        private const ulong Seed = 0xC0FFEE;
        private const int Ticks = 600; // 30 seconds at 20 Hz

        private static int Main()
        {
            try
            {
                FixedPointSanity();
                RandomSanity();
                SameLogSameHashes();
                CommandOrderDoesNotMatter();
                DivergenceIsDetected();
                ScriptedMatchSameHashes();
                ScriptedMatchDivergenceIsDetected();
                MapIsExactlySymmetric();
                MatchReachesTheWinCondition();
                DefenseBuildingsShoot();
                ProductionStopsAtThePopulationCap();
                TechTiersGateProduction();
                AttackOrderKillsWhatItNames();
                AttackMoveStopsForWhatItMeets();
                StopCancelsWhatIsRunning();
                HoldPositionNeverChases();
                ProducedUnitsWalkToTheRallyPoint();
                CancellingProductionRefundsInFull();
                ProducedWorkersCanGather();
                RosterSlotsAreAddressable();
                EveryBuildingRoleCanBePlaced();
                BuildRefusesWhatTheFactionDoesNotHave();
                BuildIsGatedByTheTechTier();
                BuildRefusesOccupiedAndOffMapCells();
                GroundRoutesAroundImpassableTerrain();
                BuildRefusesImpassableTerrain();
                MassedArchersHitHarderThanScatteredOnes();
                TheMassedBonusCaps();
                TheMassedBonusIsWaterSlimesOnly();
                ScriptedLogPlacesEveryBuilding();
                SoloMatchIsReproducible();
                TheAiPlaysARealGame();
                SoloMatchReachesTheWinCondition();
                ClientLogMatchesGoldenHash();
                SimAssemblyIsClean();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                return 1;
            }

            Console.WriteLine("OK: all determinism checks passed");
            return 0;
        }

        private static void FixedPointSanity()
        {
            Check(Fix.FromInt(3) + Fix.FromInt(4) == Fix.FromInt(7), "add");
            Check(Fix.FromInt(3) * Fix.FromInt(4) == Fix.FromInt(12), "mul");
            Check(Fix.FromInt(12) / Fix.FromInt(4) == Fix.FromInt(3), "div");
            Check(Fix.Sqrt(Fix.FromInt(144)) == Fix.FromInt(12), "sqrt exact");
            Check(Fix.Sqrt(Fix.Zero) == Fix.Zero, "sqrt zero");
            Check(Fix.Ratio(1, 2) + Fix.Ratio(1, 2) == Fix.OneF, "ratio");

            // A 3-4-5 triangle must land exactly on 5, or movement drifts.
            var v = new FixVec2(Fix.FromInt(3), Fix.FromInt(4));
            Check(v.Magnitude == Fix.FromInt(5), "magnitude 3-4-5");

            // Normalizing a zero vector must not divide by zero.
            Check(FixVec2.Zero.Normalized().Equals(FixVec2.Zero), "normalize zero");
        }

        private static void RandomSanity()
        {
            var a = new DetRandom(Seed);
            var b = new DetRandom(Seed);
            for (int i = 0; i < 1000; i++)
            {
                Check(a.NextULong() == b.NextULong(), "rng stream differs at draw " + i);
            }
            Check(a.DrawCount == b.DrawCount, "rng draw count differs");

            var bounded = new DetRandom(Seed);
            for (int i = 0; i < 1000; i++)
            {
                int r = bounded.NextInt(7);
                Check(r >= 0 && r < 7, "rng bound escaped: " + r);
            }
        }

        private static void SameLogSameHashes()
        {
            List<Command>[] log = BuildLog();
            ulong[] left = Run(log, mutateAtTick: -1);
            ulong[] right = Run(log, mutateAtTick: -1);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "hash drift at tick " + t);
            }
        }

        private static void CommandOrderDoesNotMatter()
        {
            List<Command>[] log = BuildLog();
            List<Command>[] shuffled = new List<Command>[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var copy = new List<Command>(log[t]);
                copy.Reverse(); // arrival order on the wire is not canonical order
                shuffled[t] = copy;
            }

            ulong[] ordered = Run(log, mutateAtTick: -1);
            ulong[] reversed = Run(shuffled, mutateAtTick: -1);

            for (int t = 0; t < ordered.Length; t++)
            {
                Check(ordered[t] == reversed[t], "arrival order changed the result at tick " + t);
            }
        }

        private static void DivergenceIsDetected()
        {
            List<Command>[] log = BuildLog();
            ulong[] clean = Run(log, mutateAtTick: -1);
            ulong[] tampered = Run(log, mutateAtTick: 100);

            int firstMismatch = FirstMismatch(clean, tampered);
            Check(firstMismatch >= 0, "a tampered run produced identical hashes");
            Check(firstMismatch <= 101, "divergence took too long to surface: tick " + firstMismatch);
        }

        /// <summary>
        /// The same shape as SameLogSameHashes, but over a world that gathers,
        /// builds, produces, fights, and carries all six order commands. A
        /// movement-only world would not exercise the state that actually desyncs,
        /// and an order tested only in its own fixture would never have to agree
        /// with the rest of the simulation running alongside it.
        /// </summary>
        private static void ScriptedMatchSameHashes()
        {
            List<Command>[] log = BuildMatchLog();
            ulong[] left = RunMatch(log, mutateAtTick: -1, out World world);
            ulong[] right = RunMatch(log, mutateAtTick: -1, out _);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "match hash drift at tick " + t);
            }

            // Without these the check could pass on a match where nothing happened.
            Check(world.GetEntity(MatchNode0).Resource < NodeAmount, "no resources were gathered");
            Check(world.GetResources(0) > 10, "peer 0 never banked a delivery");
            Check(world.GetEntity(MatchSite0).Kind == EntityKind.Building, "peer 0 building was not placed");
            Check(world.GetEntity(MatchSite0).BuildTicksLeft == 0, "peer 0 building never finished");
            Check(world.EntityCount > MatchSite1 + 1, "no units were produced");
            Check(!world.GetEntity(MatchFighter1).Alive, "combat never killed anything");

            // And that the order commands in the log were not silently refused: a
            // rejected command leaves state untouched, which is exactly what an
            // identical pair of hash runs looks like.
            Check(world.GetEntity(MatchBase0).HasRallyPoint, "the rally order never reached the base");
            Check(world.GetEntity(MatchWorker0B).GatherNodeId < 0, "the stop order never reached the worker");
            Check(world.GetEntity(MatchFighter0).Mode == OrderMode.AttackMove,
                "the attack-move order never reached the fighter");
        }

        private static void ScriptedMatchDivergenceIsDetected()
        {
            List<Command>[] log = BuildMatchLog();
            ulong[] clean = RunMatch(log, mutateAtTick: -1, out _);
            ulong[] tampered = RunMatch(log, mutateAtTick: 200, out _);

            int firstMismatch = FirstMismatch(clean, tampered);
            Check(firstMismatch >= 0, "a tampered match produced identical hashes");
            Check(firstMismatch <= 201, "match divergence took too long to surface: tick " + firstMismatch);
        }

        /// <summary>
        /// Every start position has a counterpart under a 180 degree rotation of
        /// the grid, with the same role and the same numbers, and so does every
        /// terrain cell. Eyeballing a layout is how a map ends up a cell out of true
        /// and one side ends up closer, and a wall is the easiest thing of all to
        /// get a cell wrong in.
        /// </summary>
        private static void MapIsExactlySymmetric()
        {
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);

            for (int y = 0; y < World.GridSize; y++)
            {
                for (int x = 0; x < World.GridSize; x++)
                {
                    int cell = y * World.GridSize + x;
                    int mirror = (World.GridSize - 1 - y) * World.GridSize + (World.GridSize - 1 - x);
                    Check(world.TerrainAt(cell) == world.TerrainAt(mirror),
                        "terrain at " + x + "," + y + " is " + world.TerrainAt(cell) +
                        " but its mirror is " + world.TerrainAt(mirror));
                }
            }

            // And that there is terrain to be symmetric about: an all-open map
            // passes the loop above without asserting anything at all.
            int impassable = 0;
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (world.TerrainAt(cell) != TileKind.Open) impassable++;
            }
            Check(impassable > 0, "the map has no terrain, so the symmetry check proves nothing");

            // Cell x sits at x + 1/2, its mirror at (GridSize - 1 - x) + 1/2, so a
            // mirrored pair's coordinates always sum to exactly GridSize.
            Fix span = Fix.FromInt(World.GridSize);

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity a = world.GetEntity(i);
                var mirrored = new FixVec2(span - a.Position.X, span - a.Position.Y);
                int wantOwner = a.Owner < 0 ? -1 : 1 - a.Owner;

                bool found = false;
                for (int j = 0; j < world.EntityCount && !found; j++)
                {
                    Entity b = world.GetEntity(j);
                    found = b.Owner == wantOwner && b.Kind == a.Kind && b.Role == a.Role &&
                            b.Hp == a.Hp && b.Resource == a.Resource && b.Position.Equals(mirrored);
                }
                Check(found, "entity " + i + " (" + a.Kind + " " + a.Role + ") has no mirror");
            }

            Check(world.GetResources(0) == world.GetResources(1), "peers start with different resources");
        }

        /// <summary>
        /// A match that actually ends. Run twice: a win condition that resolved on
        /// a different tick on the two peers would be a desync, not a victory.
        /// </summary>
        private static void MatchReachesTheWinCondition()
        {
            ulong[] first = RunSiege(out World world);
            ulong[] second = RunSiege(out _);

            Check(world.MatchOver, "the siege never reached a win condition");
            Check(world.Winner == 0, "wrong winner: " + world.Winner);
            Check(!world.GetEntity(1).Alive, "the match ended with the base still standing");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "siege hash drift at tick " + t);
            }
        }

        private const int SiegeTicks = 400;

        private static ulong[] RunSiege(out World world)
        {
            world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SetPeerFaction(1, Faction.Hellfire);

            world.SpawnBuilding(0, Role.Base, At(8, 8), complete: true);    // 0
            world.SpawnBuilding(1, Role.Base, At(50, 50), complete: true);  // 1
            // Already at the wall: what is under test is the rule that ends the
            // match, not how long an army takes to walk across the map.
            for (int i = 0; i < 8; i++) world.SpawnUnit(0, Role.Melee, At(49, 49 + i % 2));

            var idle = new List<Command>();
            var hashes = new ulong[SiegeTicks];
            for (int t = 0; t < SiegeTicks; t++)
            {
                world.Step(idle);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int TurretTicks = 400;
        private const int TurretId = 0;
        private const int TurretPrey = 1;

        /// <summary>
        /// A turret kills what walks into its range, and does it without moving.
        /// Run twice: a building that fights is a new attacker in the combat loop,
        /// so its per-tick hashes have to match as exactly as a unit's.
        /// </summary>
        private static void DefenseBuildingsShoot()
        {
            ulong[] first = RunTurret(out World world);
            ulong[] second = RunTurret(out _);

            Check(!world.GetEntity(TurretPrey).Alive, "the turret never killed what walked into range");
            Check(world.GetEntity(TurretId).Position.Equals(At(30, 30)), "the turret moved");
            Check(world.GetEntity(TurretId).Target.Equals(At(30, 30)), "the turret took a walk order from combat");
            Check(world.GetEntity(TurretId).Hp < world.GetEntity(TurretId).MaxHp, "the turret was never shot back at");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "turret hash drift at tick " + t);
            }
        }

        private static ulong[] RunTurret(out World world)
        {
            world = new World(Seed);
            world.SpawnBuilding(0, Role.Defense, At(30, 30), complete: true); // 0
            world.SpawnUnit(1, Role.Melee, At(40, 30));                       // 1

            // One move order, then nothing: the turret has to acquire on its own.
            var walkIn = new List<Command>
            {
                new Command(0, 1, 0, CommandType.Move, TurretPrey, At(31, 30))
            };
            var idle = new List<Command>();

            var hashes = new ulong[TurretTicks];
            for (int t = 0; t < TurretTicks; t++)
            {
                world.Step(t == 0 ? walkIn : idle);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        /// <summary>
        /// A produce order at the cap is refused, not deferred: nothing is spent
        /// and nothing is queued. A silent partial rejection is the version of this
        /// rule that costs a player a match.
        /// </summary>
        private static void ProductionStopsAtThePopulationCap()
        {
            var world = new World(Seed);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 1000);
            for (int i = 0; i < World.PopulationPerBase; i++) world.SpawnUnit(0, Role.Melee, At(20, 20 + i));

            Check(world.PopulationCap(0) == World.PopulationPerBase, "a base did not grant its population");
            Check(world.GetPopulation(0) == World.PopulationPerBase, "spawned units were not counted");

            int banked = world.GetResources(0);
            world.Step(Produce(0, 0, 0, Role.Melee));
            Check(world.GetResources(0) == banked, "a refused produce still spent resources");
            Check(world.GetEntity(0).QueueCount == 0, "a refused produce still queued a unit");

            // The same order under a supply building's headroom, so the check is
            // testing the cap and not just a produce that never worked.
            world.SpawnBuilding(0, Role.Supply, At(5, 8), complete: true); // 11
            Check(world.PopulationCap(0) == World.PopulationPerBase + World.PopulationPerSupply,
                "a supply building did not grant its population");

            world.Step(Produce(0, 0, 1, Role.Melee));
            Check(world.GetResources(0) == banked - World.ProduceCost, "produce under the cap spent nothing");
            Check(world.GetEntity(0).QueueCount == 1, "produce under the cap queued nothing");
        }

        private const int TechBase = 0;
        private const int TechBuilding = 1;
        private const int TechUnit = 10; // the one T3 unit this world ever finishes

        /// <summary>
        /// Tier 3 is refused before the tech building, accepted after it, and
        /// refused again once it falls. What it does not do is unbuild the unit it
        /// already opened, which is the half of the rule easy to get wrong.
        /// </summary>
        private static void TechTiersGateProduction()
        {
            var world = new World(Seed);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 1000);

            int banked = world.GetResources(0);
            world.Step(Produce(TechBase, 0, 0, Role.Signature));
            Check(world.GetResources(0) == banked, "tier 3 spent resources with no tech building");
            Check(world.GetEntity(TechBase).QueueCount == 0, "tier 3 queued with no tech building");

            // Far from the base, so the squad that comes for it later reaches
            // nothing else and the assertions stay about the tier rule.
            world.SpawnBuilding(0, Role.Tech, At(30, 30), complete: true); // 1
            world.Step(Produce(TechBase, 0, 1, Role.Signature));
            Check(world.GetResources(0) == banked - World.ProduceCost, "the tech building did not open tier 3");
            Check(world.GetEntity(TechBase).QueueCount == 1, "the tech building did not open tier 3");

            // Destroyed by an enemy squad rather than a test hook, so the rule is
            // exercised through the same path a match takes.
            for (int i = 0; i < 8; i++) world.SpawnUnit(1, Role.Melee, At(31, 29 + i % 3)); // 2..9

            var idle = new List<Command>();
            for (int t = 0; t < 400 && world.GetEntity(TechBuilding).Alive; t++) world.Step(idle);
            Check(!world.GetEntity(TechBuilding).Alive, "the squad never destroyed the tech building");

            Check(world.GetEntity(TechUnit).Role == Role.Signature, "the queued tier 3 unit never appeared");
            Check(world.GetEntity(TechUnit).Alive, "losing the tech building unbuilt a finished unit");

            banked = world.GetResources(0);
            int queued = world.GetEntity(TechBase).QueueCount;
            world.Step(Produce(TechBase, 0, 2, Role.Signature));
            Check(world.GetResources(0) == banked, "tier 3 spent resources after the tech building fell");
            Check(world.GetEntity(TechBase).QueueCount == queued, "tier 3 queued after the tech building fell");
        }

        // Order-command fixtures. Every one of them spawns its own tiny world, so
        // the entity ids below are the spawn order in the matching Run* method.
        private const int OrderTicks = 400;
        private const int Attacker = 0;
        private const int Decoy = 1;      // nearer than the named target, and harmless
        private const int Named = 2;

        /// <summary>
        /// An attack order names its victim. The decoy is what makes that provable:
        /// it stands nearer, so auto-acquisition would take it, and a run that
        /// leaves it untouched can only have honoured the order.
        /// </summary>
        private static void AttackOrderKillsWhatItNames()
        {
            ulong[] first = RunAttackOrder(out World world);
            ulong[] second = RunAttackOrder(out _);

            Check(!world.GetEntity(Named).Alive, "the attack order never killed what it named");
            Check(world.GetEntity(Decoy).Alive, "the attack order killed the decoy");
            Check(world.GetEntity(Decoy).Hp == world.GetEntity(Decoy).MaxHp, "the decoy was shot at");
            // The order is spent when its target dies rather than latching, or the
            // next acquisition would be made under an order that no longer exists.
            Check(world.GetEntity(Attacker).Mode == OrderMode.None, "the attack order outlived its target");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "attack order hash drift at tick " + t);
            }
        }

        private static ulong[] RunAttackOrder(out World world)
        {
            world = new World(Seed);
            world.SpawnUnit(0, Role.Melee, At(20, 20)); // 0
            // Workers, so neither shoots back and the run is about the order only.
            // The decoy sits behind the attacker: 5 away at the start against the
            // named target's 6, and 11 away once the attacker has closed, which is
            // outside AcquireRange so the finished order does not roll onto it.
            world.SpawnWorker(1, At(15, 20)); // 1
            world.SpawnWorker(1, At(26, 20)); // 2

            var order = new List<Command>
            {
                new Command(0, 0, 0, CommandType.Attack, Attacker, FixVec2.Zero, Named)
            };
            var idle = new List<Command>();

            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? order : idle);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int Marcher = 0;
        private const int Bystander = 1;

        /// <summary>
        /// An attack-move is both halves or it is nothing. A unit that only walked
        /// would pass the hostile with a couple of shots and arrive on time; a unit
        /// that only fought would never arrive. This asserts both: the hostile dies
        /// with the marcher still well short of the point, and the point is reached
        /// afterwards.
        /// </summary>
        private static void AttackMoveStopsForWhatItMeets()
        {
            ulong[] first = RunAttackMove(out World world, out Fix killedAtX);
            ulong[] second = RunAttackMove(out _, out _);

            Check(!world.GetEntity(Bystander).Alive, "the attack-move walked past the hostile");
            Check(killedAtX < Fix.FromInt(35), "the marcher was already at the point when it killed: x=" + killedAtX);
            Check(world.GetEntity(Marcher).Position.Equals(At(40, 10)), "the attack-move never reached its point");
            // Still under the order after arriving, so the next hostile to wander
            // in is engaged rather than ignored.
            Check(world.GetEntity(Marcher).Mode == OrderMode.AttackMove, "the attack-move order evaporated");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "attack-move hash drift at tick " + t);
            }
        }

        private static ulong[] RunAttackMove(out World world, out Fix killedAtX)
        {
            world = new World(Seed);
            world.SpawnUnit(0, Role.Melee, At(10, 10)); // 0
            // 20 out, so it starts beyond AcquireRange and the marcher has to walk
            // some of the route before it has anything to break off for. A worker,
            // so it neither shoots back nor moves.
            world.SpawnWorker(1, At(30, 10)); // 1

            var order = new List<Command>
            {
                new Command(0, 0, 0, CommandType.AttackMove, Marcher, At(40, 10))
            };
            var idle = new List<Command>();

            killedAtX = Fix.Zero;
            bool wasAlive = true;
            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? order : idle);
                if (wasAlive && !world.GetEntity(Bystander).Alive)
                {
                    killedAtX = world.GetEntity(Marcher).Position.X;
                    wasAlive = false;
                }
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int Walker = 0;
        private const int Digger = 1;
        private const int StopTick = 40;

        /// <summary>
        /// Stop has to cancel the walk and the gather loop both. A worker is in the
        /// world because the loop is the order that comes back on its own: it
        /// retargets itself every tick from GatherNodeId, so clearing the path
        /// alone would have it walking again on the next one.
        /// </summary>
        private static void StopCancelsWhatIsRunning()
        {
            ulong[] first = RunStop(out World world, out FixVec2 walkerAtStop, out FixVec2 diggerAtStop);
            ulong[] second = RunStop(out _, out _, out _);

            Check(!walkerAtStop.Equals(At(40, 10)), "the walker was already at its destination when stopped");
            Check(world.GetEntity(Walker).Position.Equals(walkerAtStop), "the walker kept going after Stop");
            Check(world.GetEntity(Digger).Position.Equals(diggerAtStop), "the worker kept going after Stop");
            Check(world.GetEntity(Digger).GatherNodeId < 0, "Stop left the gather loop running");
            Check(world.GetEntity(Digger).CarryAmount == 0, "the worker reached the node after Stop");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "stop hash drift at tick " + t);
            }
        }

        private static ulong[] RunStop(out World world, out FixVec2 walkerAtStop, out FixVec2 diggerAtStop)
        {
            world = new World(Seed);
            world.SpawnUnit(0, Role.Melee, At(10, 10));  // 0
            world.SpawnWorker(0, At(10, 12));            // 1
            world.SpawnResourceNode(At(40, 12), 200);    // 2

            var orders = new List<Command>
            {
                new Command(0, 0, 0, CommandType.Move, Walker, At(40, 10)),
                new Command(0, 0, 1, CommandType.Gather, Digger, FixVec2.Zero, 2)
            };
            var stop = new List<Command>
            {
                new Command(StopTick, 0, 2, CommandType.Stop, Walker, FixVec2.Zero),
                new Command(StopTick, 0, 3, CommandType.Stop, Digger, FixVec2.Zero)
            };
            var idle = new List<Command>();

            walkerAtStop = FixVec2.Zero;
            diggerAtStop = FixVec2.Zero;
            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? orders : t == StopTick ? stop : idle);
                if (t == StopTick)
                {
                    walkerAtStop = world.GetEntity(Walker).Position;
                    diggerAtStop = world.GetEntity(Digger).Position;
                }
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int Holder = 0;
        private const int HolderPrey = 1;
        private const int Chaser = 2;

        /// <summary>
        /// Both preys sit 5 away: inside AcquireRange, outside melee range. The
        /// unordered chaser closes on its own, which is what makes the holder's
        /// stillness mean something rather than just proving nothing was in range.
        /// </summary>
        private static void HoldPositionNeverChases()
        {
            ulong[] first = RunHold(out World world, out bool holderMoved);
            ulong[] second = RunHold(out _, out _);

            Check(!holderMoved, "the holder moved");
            Check(!world.GetEntity(Chaser).Position.Equals(At(20, 50)), "the control unit never chased either");
            Check(world.GetEntity(HolderPrey).Alive, "the holder somehow reached its prey");
            Check(world.GetEntity(HolderPrey).Hp == world.GetEntity(HolderPrey).MaxHp, "the holder got in range");
            Check(world.GetEntity(Holder).Mode == OrderMode.Hold, "the hold order lapsed");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "hold hash drift at tick " + t);
            }
        }

        private static ulong[] RunHold(out World world, out bool holderMoved)
        {
            world = new World(Seed);
            world.SpawnUnit(0, Role.Melee, At(20, 20)); // 0
            world.SpawnWorker(1, At(25, 20));           // 1
            // The control pair, 30 cells away so neither group can acquire into
            // the other and the two halves of the run stay independent.
            world.SpawnUnit(0, Role.Melee, At(20, 50)); // 2
            world.SpawnWorker(1, At(25, 50));           // 3

            var order = new List<Command>
            {
                new Command(0, 0, 0, CommandType.HoldPosition, Holder, FixVec2.Zero)
            };
            var idle = new List<Command>();

            holderMoved = false;
            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? order : idle);
                // Every tick, not just the last one: a unit that walked out and back
                // would pass an end-state check.
                if (!world.GetEntity(Holder).Position.Equals(At(20, 20))) holderMoved = true;
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int RallyBase = 0;
        private const int PlainBase = 1;
        private const int RalliedUnit = 2;
        private const int PlainUnit = 3;

        /// <summary>
        /// Two identical bases, one with a rally point. The plain one is the
        /// control: without it, a unit standing on its spawn tile and a unit that
        /// walked nowhere look the same.
        /// </summary>
        private static void ProducedUnitsWalkToTheRallyPoint()
        {
            ulong[] first = RunRally(out World world);
            ulong[] second = RunRally(out _);

            Check(world.GetEntity(RallyBase).HasRallyPoint, "the rally point was not stored");
            Check(world.GetEntity(RalliedUnit).Position.Equals(At(20, 20)),
                "the produced unit did not reach the rally point");
            Check(world.GetEntity(PlainUnit).Position.Equals(world.GetEntity(PlainBase).Position + World.RallyOffset),
                "the unit with no rally point walked off on its own");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "rally hash drift at tick " + t);
            }
        }

        private static ulong[] RunRally(out World world)
        {
            world = new World(Seed);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);   // 0
            world.SpawnBuilding(0, Role.Base, At(40, 40), complete: true); // 1
            world.GrantResources(0, 1000);

            var orders = new List<Command>
            {
                new Command(0, 0, 0, CommandType.SetRallyPoint, RallyBase, At(20, 20)),
                new Command(0, 0, 1, CommandType.Produce, RallyBase, FixVec2.Zero, (int)Role.Melee),
                new Command(0, 0, 2, CommandType.Produce, PlainBase, FixVec2.Zero, (int)Role.Melee)
            };
            var idle = new List<Command>();

            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? orders : idle);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private const int QueueBase = 0;

        /// <summary>
        /// One unit off the queue, the whole ProduceCost back. The refund is
        /// asserted as an exact number rather than "more than before", because a
        /// refund rule nobody can name is a rule that drifts.
        /// </summary>
        private static void CancellingProductionRefundsInFull()
        {
            var world = new World(Seed);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 1000);

            var queue = new List<Command>();
            for (int i = 0; i < 3; i++)
            {
                queue.Add(new Command(0, 0, i, CommandType.Produce, QueueBase, FixVec2.Zero, (int)Role.Melee));
            }
            int banked = world.GetResources(0);
            world.Step(queue);
            Check(world.GetEntity(QueueBase).QueueCount == 3, "three produce orders did not queue three units");
            Check(world.GetResources(0) == banked - 3 * World.ProduceCost, "queueing did not charge three units");

            banked = world.GetResources(0);
            world.Step(Cancel(QueueBase, 0, 3));
            Check(world.GetEntity(QueueBase).QueueCount == 2, "cancelling did not shorten the queue");
            Check(world.GetResources(0) == banked + World.ProduceCost, "cancelling did not refund in full");

            // Down to nothing, then one more: an empty queue has nothing to refund,
            // and a refused cancel must leave the bank exactly as it found it.
            world.Step(Cancel(QueueBase, 0, 4));
            world.Step(Cancel(QueueBase, 0, 5));
            Check(world.GetEntity(QueueBase).QueueCount == 0, "the queue did not empty");
            Check(world.GetEntity(QueueBase).ProduceTicksLeft == 0, "an emptied queue kept its timer running");

            banked = world.GetResources(0);
            world.Step(Cancel(QueueBase, 0, 6));
            Check(world.GetResources(0) == banked, "cancelling an empty queue paid out");
        }

        private const int WorkerBase = 0;
        private const int WorkerNode = 1;
        private const int MadeWorker = 2;

        /// <summary>
        /// A produced worker comes out a Worker. Kind is what the Gather command
        /// and the gather loop both test, so one that came off the queue as a plain
        /// Unit would stand beside a node it could never touch, carrying worker
        /// stats and no weapon.
        /// </summary>
        private static void ProducedWorkersCanGather()
        {
            var world = new World(Seed);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.SpawnResourceNode(At(9, 5), 200);                      // 1
            world.GrantResources(0, 1000);

            var idle = new List<Command>();
            world.Step(Produce(WorkerBase, 0, 0, Role.Worker));
            for (int t = 0; t < World.ProduceTicks + 5 && world.EntityCount == 2; t++) world.Step(idle);

            Check(world.EntityCount == 3, "the queued worker never appeared");
            Check(world.GetEntity(MadeWorker).Kind == EntityKind.Worker,
                "a produced worker came out a " + world.GetEntity(MadeWorker).Kind);

            int banked = world.GetResources(0);
            world.Step(new List<Command>
            {
                new Command(0, 0, 1, CommandType.Gather, MadeWorker, FixVec2.Zero, WorkerNode)
            });
            for (int t = 0; t < OrderTicks; t++) world.Step(idle);
            Check(world.GetResources(0) > banked, "a produced worker never delivered anything");
        }

        private static List<Command> Cancel(int building, int peer, int seq) =>
            new List<Command> { new Command(0, peer, seq, CommandType.CancelProduction, building, FixVec2.Zero) };

        private static List<Command> Produce(int building, int peer, int seq, Role role) =>
            new List<Command> { new Command(0, peer, seq, CommandType.Produce, building, FixVec2.Zero, (int)role) };

        /// <summary>
        /// Every entry of every slot has to be reachable by its index, or an asset
        /// added to the roster is present in the table and invisible everywhere
        /// else. Entry 0 has to keep matching the two-argument accessors, which is
        /// what the view calls and what decides which unit is drawn.
        /// </summary>
        private static void RosterSlotsAreAddressable()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var faction = (Faction)f;
                    var role = (Role)r;
                    string where = faction + "." + role;

                    Check(FactionData.SlotCount(faction, role) >= 1, "empty roster slot at " + where);
                    Check(FactionData.Name(faction, role, 0) == FactionData.Name(faction, role),
                        "entry 0 is not the drawn name at " + where);
                    Check(FactionData.Sprite(faction, role, 0) == FactionData.Sprite(faction, role),
                        "entry 0 is not the drawn sprite at " + where);

                    var seen = new List<string>();
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        string name = FactionData.Name(faction, role, s);
                        Check(s == 0 || name.Length > 0, "unreachable roster entry " + where + "[" + s + "]");
                        Check(!seen.Contains(name), "duplicate roster entry " + where + " -> " + name);
                        seen.Add(name);
                    }
                }
            }
        }

        /// <summary>
        /// The roles a Build may name, in the order the command card shows them.
        /// Every faction lists all five, so a build menu is never empty.
        /// </summary>
        private static readonly Role[] Buildings =
        {
            Role.Base, Role.Production, Role.Defense, Role.Supply, Role.Tech
        };

        /// <summary>
        /// Each building role placed by the command that names it. Before this,
        /// Build took no argument and four of the five buildings existed in the
        /// roster with no way to put one down.
        /// </summary>
        private static void EveryBuildingRoleCanBePlaced()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                foreach (Role role in Buildings)
                {
                    Check(FactionData.Has((Faction)f, role),
                        "faction " + (Faction)f + " cannot build a " + role);
                }
            }

            foreach (Role role in Buildings)
            {
                var world = new World(Seed);
                world.SetPeerFaction(0, Faction.TreeSpirits);
                world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);        // 0
                // Standing production, so tier 2 is open and this loop is testing
                // the role argument rather than the tier gate.
                world.SpawnBuilding(0, Role.Production, At(5, 9), complete: true);  // 1
                world.GrantResources(0, 5000);

                int banked = world.GetResources(0);
                world.Step(Build(0, 0, role, At(20, 20)));

                Check(world.EntityCount == 3, "a Build for " + role + " placed nothing");
                Entity site = world.GetEntity(2);
                Check(site.Kind == EntityKind.Building, "a Build for " + role + " placed a " + site.Kind);
                Check(site.Role == role, "a Build for " + role + " placed a " + site.Role);
                Check(site.BuildTicksLeft > 0, "a Build for " + role + " finished instantly");
                Check(world.GetResources(0) == banked - FactionData.BuildCost(role),
                    "a Build for " + role + " charged the wrong price");
            }
        }

        /// <summary>
        /// A roster slot the faction leaves empty cannot be built, and the refusal
        /// is whole: no cost taken, no site placed. The control afterwards is what
        /// proves the world was capable of the build it just refused.
        /// </summary>
        private static void BuildRefusesWhatTheFactionDoesNotHave()
        {
            // 인간 마법 문명 fields no melee unit; the slot is empty by design.
            Check(!FactionData.Has(Faction.Humans, Role.Melee),
                "the empty roster slot this check relies on has been filled");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Humans);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 5000);

            int banked = world.GetResources(0);
            world.Step(Build(0, 0, Role.Melee, At(20, 20)));
            Check(world.GetResources(0) == banked, "a Build the roster does not list spent resources");
            Check(world.EntityCount == 1, "a Build the roster does not list placed something");

            world.Step(Build(0, 1, Role.Supply, At(20, 20)));
            Check(world.GetResources(0) == banked - FactionData.BuildCost(Role.Supply),
                "the control build was refused too, so the check proves nothing");
            Check(world.GetEntity(1).Role == Role.Supply, "the control build placed the wrong thing");
        }

        /// <summary>
        /// The tech building needs the production building standing, not merely
        /// paid for. The half-built middle step is the one worth asserting: a site
        /// under construction opens nothing.
        /// </summary>
        private static void BuildIsGatedByTheTechTier()
        {
            Check(FactionData.Tier(Role.Tech) == 2, "the tech building is no longer tier gated");

            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 5000);

            int banked = world.GetResources(0);
            world.Step(Build(0, 0, Role.Tech, At(20, 20)));
            Check(world.GetResources(0) == banked, "tier 2 spent resources with no production building");
            Check(world.EntityCount == 1, "tier 2 placed a building with no production building");

            world.Step(Build(0, 1, Role.Production, At(30, 30)));
            Check(world.GetEntity(1).Role == Role.Production, "the prerequisite was not placed");

            // Still under construction, so it opens nothing yet.
            world.Step(Build(0, 2, Role.Tech, At(20, 20)));
            Check(world.EntityCount == 2, "an unfinished production building opened tier 2");

            var idle = new List<Command>();
            while (world.GetEntity(1).BuildTicksLeft > 0) world.Step(idle);

            banked = world.GetResources(0);
            world.Step(Build(0, 3, Role.Tech, At(20, 20)));
            Check(world.EntityCount == 3, "the finished production building did not open tier 2");
            Check(world.GetEntity(2).Role == Role.Tech, "the prerequisite opened the wrong role");
            Check(world.GetResources(0) == banked - FactionData.BuildCost(Role.Tech),
                "the tech building charged the wrong price");
        }

        /// <summary>
        /// Off the map and on top of something are both refusals, never a clamp:
        /// a Build quietly moved to a nearby cell is a building the player did not
        /// ask for, and two peers would not have to pick the same cell.
        /// </summary>
        private static void BuildRefusesOccupiedAndOffMapCells()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.SpawnResourceNode(At(14, 14), 200);                    // 1
            world.GrantResources(0, 5000);

            int banked = world.GetResources(0);
            int seq = 0;
            FixVec2[] refused =
            {
                At(5, 5),   // the base's own cell
                At(14, 14), // a resource node
                At(-2, 20), At(20, -2),
                At(World.GridSize, 20), At(20, World.GridSize + 5),
            };

            foreach (FixVec2 where in refused)
            {
                world.Step(Build(0, seq++, Role.Supply, where));
                Check(world.EntityCount == 2, "a Build at " + where.X + "," + where.Y + " placed something");
                Check(world.GetResources(0) == banked, "a refused Build still spent resources");
            }

            world.Step(Build(0, seq, Role.Supply, At(20, 20)));
            Check(world.EntityCount == 3, "the control build on a free cell was refused too");

            // And the cell it took is now occupied for the next one.
            world.Step(Build(0, seq + 1, Role.Supply, At(20, 20)));
            Check(world.EntityCount == 3, "two buildings were placed on one cell");
        }

        private const int Router = 0;   // ordered across the wall
        private const int Bumper = 1;   // ordered into it
        private const int WallX = 20;
        private const int WallEndY = 20; // the wall runs from the top edge down to here

        /// <summary>
        /// Two orders against one wall. The first is ordered to the far side and
        /// has to walk around the end of it; the second is ordered onto the wall
        /// itself, which is the order with no legal answer. Neither is allowed to
        /// stand on rock on any tick of the run, which is checked every tick rather
        /// than at the end: a unit that crossed the wall and came back off it would
        /// pass a final-state check.
        /// </summary>
        private static void GroundRoutesAroundImpassableTerrain()
        {
            ulong[] first = RunTerrainWalk(out World world, out bool trespassed, out bool wentRound);
            ulong[] second = RunTerrainWalk(out _, out _, out _);

            Check(!trespassed, "a ground unit stood on impassable terrain");
            Check(world.GetEntity(Router).Position.Equals(At(30, 10)),
                "the unit ordered across the wall never arrived");
            // Straight there is 20 cells of open ground; the only reason to be south
            // of the wall's end is that the route went around it.
            Check(wentRound, "the unit reached the far side without going around the wall");

            Check(!world.GetEntity(Bumper).Position.Equals(At(WallX, 10)),
                "the unit ordered onto the wall reached the cell it was sent to");
            Check(world.TerrainAt(World.CellOf(world.GetEntity(Bumper).Position)) == TileKind.Open,
                "the unit ordered onto the wall finished standing in it");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "terrain walk hash drift at tick " + t);
            }
        }

        private static ulong[] RunTerrainWalk(out World world, out bool trespassed, out bool wentRound)
        {
            world = new World(Seed);
            // A wall hung from the top edge, so there is exactly one way round it
            // and the route it forces is unambiguous.
            for (int y = 0; y <= WallEndY; y++) world.SetTerrain(WallX, y, TileKind.Rock);

            world.SpawnUnit(0, Role.Melee, At(10, 10)); // 0
            // Far enough south that its own walk never meets the other unit, so the
            // two halves of the run stay independent.
            world.SpawnUnit(0, Role.Melee, At(10, 40)); // 1

            var orders = new List<Command>
            {
                new Command(0, 0, 0, CommandType.Move, Router, At(30, 10)),
                new Command(0, 0, 1, CommandType.Move, Bumper, At(WallX, 10))
            };
            var idle = new List<Command>();

            trespassed = false;
            wentRound = false;
            var hashes = new ulong[OrderTicks];
            for (int t = 0; t < OrderTicks; t++)
            {
                world.Step(t == 0 ? orders : idle);
                foreach (int id in new[] { Router, Bumper })
                {
                    Entity e = world.GetEntity(id);
                    if (world.TerrainAt(World.CellOf(e.Position)) != TileKind.Open) trespassed = true;
                }
                if (world.GetEntity(Router).Position.Y > Fix.FromInt(WallEndY)) wentRound = true;
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        /// <summary>
        /// Water and rock refuse a building the same way an occupied cell does, and
        /// refuse it whole. Checked through CanBuild as well as through the command,
        /// because the client tints its placement ghost with the first and the
        /// simulation decides with the second; a ghost that disagreed would offer
        /// the player a cell the match then refuses.
        /// </summary>
        private static void BuildRefusesImpassableTerrain()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.TreeSpirits);
            world.SetTerrain(20, 20, TileKind.Rock);
            world.SetTerrain(25, 25, TileKind.Water);
            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true); // 0
            world.GrantResources(0, 5000);

            Check(!world.CanBuild(0, Role.Supply, At(20, 20)), "the ghost would allow a building on rock");
            Check(!world.CanBuild(0, Role.Supply, At(25, 25)), "the ghost would allow a building in water");

            int banked = world.GetResources(0);
            int seq = 0;
            foreach (FixVec2 where in new[] { At(20, 20), At(25, 25) })
            {
                world.Step(Build(0, seq++, Role.Supply, where));
                Check(world.EntityCount == 1, "a Build on impassable terrain placed something");
                Check(world.GetResources(0) == banked, "a refused Build still spent resources");
            }

            // The control: the same order one cell over, on open ground.
            world.Step(Build(0, seq, Role.Supply, At(21, 20)));
            Check(world.EntityCount == 2, "the control build on open ground was refused too");
        }

        // 물 슬라임 일제 사격. Ranged fires every AttackTicks and fires on the tick
        // it acquires, so a run of VolleyTicks lands exactly VolleyShots volleys.
        private const int VolleyTicks = 100;
        private const int VolleyShots = 6; // ticks 0, 18, 36, 54, 72, 90
        private const int VolleyDummy = 0;

        /// <summary>
        /// Eight archers packed inside the bonus radius, all within weapon range of
        /// the same target. Max pairwise distance is sqrt(8), comfortably inside 3.
        /// </summary>
        private static readonly FixVec2[] Massed8 =
        {
            At(27, 31), At(28, 31), At(29, 31),
            At(27, 32),             At(29, 32),
            At(27, 33), At(28, 33), At(29, 33),
        };

        /// <summary>
        /// The same eight archers on a 4-cell lattice around the target: still every
        /// one of them in weapon range, no two of them inside the bonus radius.
        /// </summary>
        private static readonly FixVec2[] Scattered8 =
        {
            At(28, 28), At(32, 28), At(36, 28),
            At(28, 32),             At(36, 32),
            At(28, 36), At(32, 36), At(36, 36),
        };

        /// <summary>Five archers, each seeing the other four: exactly at the cap.</summary>
        private static readonly FixVec2[] Massed5 =
        {
            At(28, 31),
            At(27, 32), At(28, 32), At(29, 32),
            At(28, 33),
        };

        /// <summary>Ten in the same blob. Every one of them sees more than the cap allows.</summary>
        private static readonly FixVec2[] Massed10 =
        {
            At(27, 31), At(28, 31), At(29, 31),
            At(27, 32), At(28, 32), At(29, 32), At(30, 32),
            At(27, 33), At(28, 33), At(29, 33),
        };

        /// <summary>
        /// Formation is firepower. Eight archers stacked have to out-damage eight
        /// spread out by exactly the bonus, not merely win eventually: a check that
        /// only asserted both squads killed something would pass with the mechanic
        /// deleted.
        /// </summary>
        private static void MassedArchersHitHarderThanScatteredOnes()
        {
            int shot = FactionData.Stats(Role.Ranged).Damage;
            int massed = VolleyDamage(Faction.WaterSlimes, Massed8);
            int scattered = VolleyDamage(Faction.WaterSlimes, Scattered8);

            Check(scattered == Scattered8.Length * shot * VolleyShots,
                "scattered archers dealt " + scattered + ", expected the unbuffed " +
                Scattered8.Length * shot * VolleyShots);
            Check(massed == Massed8.Length * (shot + World.VolleyMaxBonus) * VolleyShots,
                "massed archers dealt " + massed + ", expected " +
                Massed8.Length * (shot + World.VolleyMaxBonus) * VolleyShots);
        }

        /// <summary>
        /// The cap is a cap. Five archers already sit on it, so the sixth through
        /// the tenth buy nothing: per-archer output has to be identical, or the
        /// bonus is a stacking multiplier and a big enough ball wins on its own.
        /// </summary>
        private static void TheMassedBonusCaps()
        {
            int shot = FactionData.Stats(Role.Ranged).Damage;
            int five = VolleyDamage(Faction.WaterSlimes, Massed5);
            int ten = VolleyDamage(Faction.WaterSlimes, Massed10);

            int perArcher = (shot + World.VolleyMaxBonus) * VolleyShots;
            Check(five == Massed5.Length * perArcher,
                "five archers dealt " + five + ", expected " + Massed5.Length * perArcher);
            Check(ten == Massed10.Length * perArcher,
                "ten archers dealt " + ten + ", expected " + Massed10.Length * perArcher);
            Check(five / Massed5.Length == ten / Massed10.Length,
                "the tenth archer changed what each one hits for");
        }

        /// <summary>
        /// The bonus belongs to one faction. The same eight bodies in the same
        /// formation under another banner shoot for the roster number and nothing
        /// more, which is what makes the mechanic an identity rather than a rule
        /// about standing close together.
        /// </summary>
        private static void TheMassedBonusIsWaterSlimesOnly()
        {
            int shot = FactionData.Stats(Role.Ranged).Damage;
            int plain = VolleyDamage(Faction.RockGolems, Massed8);

            Check(plain == Massed8.Length * shot * VolleyShots,
                "another faction's massed archers dealt " + plain + ", expected the unbuffed " +
                Massed8.Length * shot * VolleyShots);
            Check(plain < VolleyDamage(Faction.WaterSlimes, Massed8),
                "the massed bonus is not faction specific");
        }

        /// <summary>
        /// What one formation takes off a target over VolleyTicks. The target is a
        /// Role.None unit: no speed, no weapon, no reach, and hp far past anything
        /// the run can spend, so the number that comes back is the archers' output
        /// and not a race to a kill.
        /// </summary>
        private static int VolleyDamage(Faction faction, FixVec2[] archers)
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, faction);
            world.SetPeerFaction(1, Faction.TreeSpirits);

            world.SpawnUnit(1, Role.None, At(32, 32), hpOverride: 1000000); // 0
            foreach (FixVec2 where in archers) world.SpawnUnit(0, Role.Ranged, where);

            var idle = new List<Command>();
            for (int t = 0; t < VolleyTicks; t++) world.Step(idle);

            Entity target = world.GetEntity(VolleyDummy);
            return target.MaxHp - target.Hp;
        }

        /// <summary>
        /// The client's log now places one of every building. Run twice for the
        /// per-tick hashes, and checked for the sites themselves: a Build the
        /// simulation refuses changes no state, so a pair of identical runs is
        /// exactly what a silently rejected log looks like.
        /// </summary>
        private static void ScriptedLogPlacesEveryBuilding()
        {
            List<Command>[] log = ScriptedLog.Build();
            ulong[] left = RunScriptedLog(log, out World world);
            ulong[] right = RunScriptedLog(log, out _);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "client log hash drift at tick " + t);
            }

            foreach (Role role in new[] { Role.Production, Role.Supply, Role.Tech, Role.Defense })
            {
                for (int peer = 0; peer < MatchScenario.Peers; peer++)
                {
                    Check(Built(world, peer, role), "peer " + peer + " never placed a " + role);
                }
            }
        }

        /// <summary>True when this peer holds a building of that role, finished or not.</summary>
        private static bool Built(World world, int peer, Role role)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Alive && e.Kind == EntityKind.Building && e.Owner == peer && e.Role == role) return true;
            }
            return false;
        }

        private static ulong[] RunScriptedLog(List<Command>[] log, out World world)
        {
            world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        // The solo match: one human peer that does nothing and one the simulation
        // plays. Peer 1 is the AI, so peer 0 is the passive opponent it has to
        // beat, and every assertion below is about what peer 1 did on its own.
        private const int SoloAi = 1;
        private const int SoloTicks = 600;
        // The AI takes the enemy base around tick 750. The budget is loose enough
        // that a slower opening is not a failure, and tight enough that an opponent
        // which stopped attacking altogether is.
        private const int SoloWinTicks = 1200;

        /// <summary>
        /// A solo match is reproducible. The opponent lives inside Step, so this is
        /// the check that its decisions are simulation state like everything else:
        /// two runs from one seed, and every tick has to agree.
        /// </summary>
        private static void SoloMatchIsReproducible()
        {
            ulong[] left = RunSolo(SoloTicks, out _, out _);
            ulong[] right = RunSolo(SoloTicks, out _, out _);

            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "solo hash drift at tick " + t);
            }
        }

        /// <summary>
        /// The opponent plays a game. Every clause here is one of the things it was
        /// asked to do, in the order it was asked to do them: gather, produce,
        /// build, and walk at the enemy. A check that only proved it did not crash
        /// would pass on an opponent that stood still for thirty seconds.
        /// </summary>
        private static void TheAiPlaysARealGame()
        {
            RunSolo(SoloTicks, out World world, out bool banked);

            // Against a world built from the same seed and never stepped, so what
            // the nodes started with is read rather than remembered.
            World untouched = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            Check(NodeTotal(world) < NodeTotal(untouched), "the AI never gathered from a node");
            // Resources only ever rise on a delivery, so a rise is a full trip:
            // walked out, mined, walked back. A drained node alone is not that.
            Check(banked, "the AI mined but never delivered");

            Check(CountOwned(world, SoloAi, EntityKind.Worker) > 2,
                "the AI never grew past the two workers it started with");
            Check(CountOwned(world, SoloAi, EntityKind.Unit) > 2,
                "the AI never produced a fighter");
            Check(CountOwned(world, SoloAi, EntityKind.Building) > 1,
                "the AI never built anything");
            Check(Built(world, SoloAi, Role.Production), "the AI never built a second production building");
            Check(Built(world, SoloAi, Role.Supply), "the AI never built supply");
            Check(world.GetPopulation(SoloAi) < world.PopulationCap(SoloAi),
                "the AI let itself hit the population cap");

            // Moved toward the enemy, not merely ordered to: a unit closer to the
            // enemy base than its own base is has crossed ground to get there.
            int enemyBase = FindBase(world, 1 - SoloAi);
            int homeBase = FindBase(world, SoloAi);
            Check(enemyBase >= 0 && homeBase >= 0, "a base went missing before the assertions");

            Fix start = (world.GetEntity(homeBase).Position - world.GetEntity(enemyBase).Position).SqrMagnitude;
            bool advanced = false;
            for (int i = 0; i < world.EntityCount && !advanced; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != SoloAi || e.Kind != EntityKind.Unit) continue;
                if (e.Mode != OrderMode.AttackMove) continue;
                advanced = (e.Position - world.GetEntity(enemyBase).Position).SqrMagnitude < start;
            }
            Check(advanced, "no AI unit ever moved toward the enemy");
        }

        /// <summary>
        /// The whole thing end to end: the opponent takes a match off a peer that
        /// never issues a command. Run twice, because a win that landed on a
        /// different tick on two runs would be a desync wearing a victory screen.
        /// </summary>
        private static void SoloMatchReachesTheWinCondition()
        {
            ulong[] first = RunSolo(SoloWinTicks, out World world, out _);
            ulong[] second = RunSolo(SoloWinTicks, out _, out _);

            Check(world.MatchOver, "the AI never finished the match");
            Check(world.Winner == SoloAi, "wrong winner: " + world.Winner);
            Check(!world.GetEntity(FindBase(world, 1 - SoloAi)).Alive,
                "the match ended with the enemy base still standing");

            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "solo win hash drift at tick " + t);
            }
        }

        /// <summary>
        /// The scenario the client builds, with peer 1 played by the simulation and
        /// peer 0 issuing nothing. banked reports whether the AI's bank ever rose,
        /// which only a delivery does.
        /// </summary>
        private static ulong[] RunSolo(int ticks, out World world, out bool banked)
        {
            world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            world.SetPeerAi(SoloAi, true);

            var idle = new List<Command>();
            var hashes = new ulong[ticks];
            banked = false;
            int last = world.GetResources(SoloAi);

            for (int t = 0; t < ticks; t++)
            {
                world.Step(idle);
                if (world.GetResources(SoloAi) > last) banked = true;
                last = world.GetResources(SoloAi);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        /// <summary>What is left in the ground, over every node on the map.</summary>
        private static int NodeTotal(World world)
        {
            int total = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Kind == EntityKind.ResourceNode) total += e.Resource;
            }
            return total;
        }

        private static int CountOwned(World world, int peer, EntityKind kind)
        {
            int n = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Alive && e.Owner == peer && e.Kind == kind) n++;
            }
            return n;
        }

        private static int FindBase(World world, int peer)
        {
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (e.Kind == EntityKind.Building && e.Role == Role.Base && e.Owner == peer) return i;
            }
            return -1;
        }

        private static List<Command> Build(int peer, int seq, Role role, FixVec2 where) =>
            new List<Command> { new Command(0, peer, seq, CommandType.Build, -1, where, (int)role) };

        /// <summary>
        /// The client's own input log, run under CoreCLR. Unity's Mono runtime
        /// asserts the same constant, so the two runtimes the game ships on are
        /// pinned to one another. A fixed-point or JIT difference between them
        /// would otherwise only appear as a desync between two players.
        /// </summary>
        private static void ClientLogMatchesGoldenHash()
        {
            ulong[] hashes = RunScriptedLog(ScriptedLog.Build(), out _);
            ulong final = hashes[hashes.Length - 1];
            Check(final == ScriptedLog.GoldenHash,
                "CoreCLR disagrees with the golden hash: got 0x" + final.ToString("X16") +
                ", expected 0x" + ScriptedLog.GoldenHash.ToString("X16"));
        }

        /// <summary>
        /// Reflection stand-in for a lint rule: the simulation assembly must not
        /// carry floating point or a Unity reference. Signatures only, which is
        /// enough to catch the mistake anyone actually makes.
        /// </summary>
        private static void SimAssemblyIsClean()
        {
            Assembly sim = typeof(World).Assembly;

            foreach (AssemblyName reference in sim.GetReferencedAssemblies())
            {
                Check(!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal),
                    "Sim references " + reference.Name);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.DeclaredOnly;

            foreach (Type type in sim.GetTypes())
            {
                foreach (FieldInfo f in type.GetFields(all))
                {
                    CheckNotFloating(f.FieldType, type.FullName + "." + f.Name);
                }
                foreach (MethodInfo m in type.GetMethods(all))
                {
                    CheckNotFloating(m.ReturnType, type.FullName + "." + m.Name + " return");
                    foreach (ParameterInfo p in m.GetParameters())
                    {
                        CheckNotFloating(p.ParameterType, type.FullName + "." + m.Name + " parameter " + p.Name);
                    }
                }
            }
        }

        private static void CheckNotFloating(Type t, string where)
        {
            Type bare = t.IsByRef || t.IsArray ? t.GetElementType() : t;
            Check(bare != typeof(float) && bare != typeof(double) && bare != typeof(decimal),
                "forbidden floating point type in Sim: " + where);
        }

        /// <summary>Two players, deterministic pseudo-random move orders.</summary>
        private static List<Command>[] BuildLog()
        {
            var scripted = new DetRandom(0xBADC0DE);
            var log = new List<Command>[Ticks];
            var seq = new int[2];

            for (int t = 0; t < Ticks; t++)
            {
                log[t] = new List<Command>();
                if (t % 17 != 0) continue;

                for (int peer = 0; peer < 2; peer++)
                {
                    int entity = peer * 3 + scripted.NextInt(3);
                    var target = new FixVec2(
                        Fix.FromInt(scripted.NextInt(64)),
                        Fix.FromInt(scripted.NextInt(64)));
                    log[t].Add(new Command(t, peer, seq[peer]++, CommandType.Move, entity, target));
                }
            }
            return log;
        }

        /// <summary>
        /// Runs the log in a fresh world and returns the per-tick hash.
        /// mutateAtTick injects a single state change to prove detection works.
        /// </summary>
        private static ulong[] Run(List<Command>[] log, int mutateAtTick)
        {
            var world = new World(Seed);
            for (int peer = 0; peer < 2; peer++)
            {
                for (int i = 0; i < 3; i++)
                {
                    world.SpawnUnit(peer, Role.Melee, new FixVec2(Fix.FromInt(peer * 40), Fix.FromInt(i * 5)));
                }
            }

            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var commands = log[t];
                if (t == mutateAtTick)
                {
                    // One extra command on one peer only: the classic desync.
                    commands = new List<Command>(commands)
                    {
                        new Command(t, 0, 9999, CommandType.Move, 0,
                            new FixVec2(Fix.FromInt(7), Fix.FromInt(7)))
                    };
                }
                world.Step(commands);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        // Entity ids of the scripted match. Ids are handed out in spawn order and
        // never reused, so they are stable enough to assert on.
        private const int MatchBase0 = 0;
        private const int MatchWorker0A = 1;
        private const int MatchWorker0B = 2;
        private const int MatchNode0 = 3;
        private const int MatchBase1 = 4;
        private const int MatchWorker1A = 5;
        private const int MatchWorker1B = 6;
        private const int MatchNode1 = 7;
        private const int MatchFighter0 = 8;
        private const int MatchFighter1 = 9;
        private const int MatchSite0 = 10; // placed at tick 150 by peer 0
        private const int MatchSite1 = 11; // placed at tick 150 by peer 1
        private const int NodeAmount = 200;

        private static World BuildMatchWorld()
        {
            var world = new World(Seed);
            Fix half = Fix.Ratio(1, 2);

            world.SpawnBuilding(0, Role.Base, At(5, 5), complete: true);   // 0
            world.SpawnWorker(0, At(7, 5));                          // 1
            world.SpawnWorker(0, At(7, 7));                          // 2
            world.SpawnResourceNode(At(14, 14), NodeAmount);         // 3

            world.SpawnBuilding(1, Role.Base, At(50, 50), complete: true); // 4
            world.SpawnWorker(1, At(48, 50));                        // 5
            world.SpawnWorker(1, At(48, 48));                        // 6
            world.SpawnResourceNode(At(42, 42), NodeAmount);         // 7

            // Two units placed in each other's face so combat resolves inside the
            // replay window instead of after it.
            world.SpawnUnit(0, Role.Melee, At(30, 30));              // 8
            world.SpawnUnit(1, Role.Melee,
                new FixVec2(Fix.FromInt(31) + half, Fix.FromInt(30) + half)); // 9

            // Enough to place the production building the log puts down at tick 150
            // without waiting on the gather loop, which is under test separately.
            world.GrantResources(0, 300);
            world.GrantResources(1, 300);
            return world;
        }

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        /// <summary>
        /// Gather, build, produce, in that order, for both peers, with all six
        /// order commands threaded through the same run. They share a world with
        /// the economy on purpose: an order that only ever ran in its own fixture
        /// has never had to agree with anything else moving.
        /// </summary>
        private static List<Command>[] BuildMatchLog()
        {
            var log = new List<Command>[Ticks];
            for (int t = 0; t < Ticks; t++) log[t] = new List<Command>();
            var seq = new int[2];

            log[2].Add(new Command(2, 0, seq[0]++, CommandType.Gather, MatchWorker0A, FixVec2.Zero, MatchNode0));
            log[2].Add(new Command(2, 0, seq[0]++, CommandType.Gather, MatchWorker0B, FixVec2.Zero, MatchNode0));
            log[2].Add(new Command(2, 1, seq[1]++, CommandType.Gather, MatchWorker1A, FixVec2.Zero, MatchNode1));
            log[2].Add(new Command(2, 1, seq[1]++, CommandType.Gather, MatchWorker1B, FixVec2.Zero, MatchNode1));

            // Named target rather than a walk order: the two fighters already stand
            // in each other's face, so this is the order deciding the kill.
            log[5].Add(new Command(5, 0, seq[0]++, CommandType.Attack, MatchFighter0, FixVec2.Zero, MatchFighter1));

            log[150].Add(new Command(150, 0, seq[0]++, CommandType.Build, -1, At(20, 20), (int)Role.Production));
            log[150].Add(new Command(150, 1, seq[1]++, CommandType.Build, -1, At(44, 48), (int)Role.Production));

            // One worker off the loop, the other left on it, so the run still banks
            // deliveries while a cancelled loop is in the same hash.
            log[200].Add(new Command(200, 0, seq[0]++, CommandType.Stop, MatchWorker0B, FixVec2.Zero));

            // By 250 the named target is down, so the fighter has an idle mode to
            // replace rather than an order still running.
            log[250].Add(new Command(250, 0, seq[0]++, CommandType.HoldPosition, MatchFighter0, FixVec2.Zero));

            log[260].Add(new Command(260, 0, seq[0]++, CommandType.SetRallyPoint, MatchBase0, At(10, 10)));
            log[260].Add(new Command(260, 1, seq[1]++, CommandType.SetRallyPoint, MatchBase1, At(46, 46)));

            foreach (int t in new[] { 300, 400 })
            {
                log[t].Add(new Command(t, 0, seq[0]++, CommandType.Produce, MatchBase0, FixVec2.Zero));
                log[t].Add(new Command(t, 1, seq[1]++, CommandType.Produce, MatchBase1, FixVec2.Zero));
            }

            // Queued, then cancelled ten ticks in, so the refund lands while the
            // unit is genuinely half built rather than on an idle building.
            log[450].Add(new Command(450, 0, seq[0]++, CommandType.Produce, MatchBase0, FixVec2.Zero));
            log[460].Add(new Command(460, 0, seq[0]++, CommandType.CancelProduction, MatchBase0, FixVec2.Zero));

            // Last, so the fighter ends the run under an order that is still live.
            log[500].Add(new Command(500, 0, seq[0]++, CommandType.AttackMove, MatchFighter0, At(40, 40)));

            return log;
        }

        private static ulong[] RunMatch(List<Command>[] log, int mutateAtTick, out World world)
        {
            world = BuildMatchWorld();
            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                var commands = log[t];
                if (t == mutateAtTick)
                {
                    commands = new List<Command>(commands)
                    {
                        new Command(t, 0, 9999, CommandType.Move, MatchWorker0A, At(3, 3))
                    };
                }
                world.Step(commands);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private static int FirstMismatch(ulong[] a, ulong[] b)
        {
            for (int t = 0; t < a.Length; t++)
            {
                if (a[t] != b[t]) return t;
            }
            return -1;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
