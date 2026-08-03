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
                RosterSlotsAreAddressable();
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
        /// the grid, with the same role and the same numbers. Eyeballing a layout
        /// is how a map ends up a cell out of true and one side ends up closer.
        /// </summary>
        private static void MapIsExactlySymmetric()
        {
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
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
        /// The client's own input log, run under CoreCLR. Unity's Mono runtime
        /// asserts the same constant, so the two runtimes the game ships on are
        /// pinned to one another. A fixed-point or JIT difference between them
        /// would otherwise only appear as a desync between two players.
        /// </summary>
        private static void ClientLogMatchesGoldenHash()
        {
            List<Command>[] log = ScriptedLog.Build();
            World world = MatchScenario.Build(Seed, ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            for (int t = 0; t < log.Length; t++) world.Step(log[t]);

            ulong final = world.Hash();
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

            world.GrantResources(0, 100);
            world.GrantResources(1, 100);
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

            log[150].Add(new Command(150, 0, seq[0]++, CommandType.Build, -1, At(20, 20)));
            log[150].Add(new Command(150, 1, seq[1]++, CommandType.Build, -1, At(44, 48)));

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
