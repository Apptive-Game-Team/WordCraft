using System;
using System.Collections.Generic;
using System.Diagnostics;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #162's checks: walkers that share ground push each other apart, and
    /// the push obeys every rule the walk obeys. Kept out of Program.cs the way
    /// the other per-issue files are.
    ///
    /// Four kinds of question. The push itself, on two bodies and on a stack:
    /// they part, they stop parting, and a held body is not moved. The ground
    /// rule: a push never carries a body over a cliff, into rock, water or a
    /// building, however hard its neighbours press. Determinism: two worlds run
    /// the same fixture to the same hash on every tick. And cost: two hundred
    /// bodies on a few cells step inside the budget.
    /// </summary>
    internal static class SeparationChecks
    {
        private const ulong Seed = 0xC0FFEE;

        /// <summary>Ticks every fixture is run for. Long enough for a stack of ten to settle.</summary>
        private const int SettleTicks = 200;

        public static void Check()
        {
            EveryWalkerHasAFootprint();
            TwoBodiesOnOnePointPartAndThenStandStill();
            AStackOfTenLeavesThePoint();
            AHeldBodyIsNotPushed();
            APushNeverCrossesACliffOrEntersWaterOrRock();
            APushNeverEntersABuildingOrANode();
            SeparationIsTheSameOnBothPeers();
            TwoHundredBodiesStepInsideTheBudget();
        }

        /// <summary>
        /// A row with a Speed has a Footprint and a row without one has none. The
        /// first half is what separation runs on; the second keeps a building from
        /// ever being pushed should a roster row grow a Speed by mistake.
        /// </summary>
        private static void EveryWalkerHasAFootprint()
        {
            for (int f = 0; f < FactionData.FactionCount; f++)
            {
                for (int r = 0; r < FactionData.RoleCount; r++)
                {
                    var faction = (Faction)f;
                    var role = (Role)r;
                    for (int s = 0; s < FactionData.SlotCount(faction, role); s++)
                    {
                        UnitStats stats = FactionData.Stats(faction, role, s);
                        string where = faction + "." + role + "[" + s + "]";
                        Check((stats.Speed.Raw > 0) == (stats.Footprint.Raw > 0),
                            "a row with " + (stats.Speed.Raw > 0 ? "a Speed and no Footprint" : "a Footprint and no Speed") + ": " + where);
                        Check(stats.Footprint < Fix.Ratio(1, 2),
                            "a footprint of half a cell or more at " + where + "; see FactionData.BodyRadius for the bound");
                    }
                }
            }
        }

        /// <summary>
        /// Two melee units spawned on one point end up a footprint sum apart, and
        /// once they are, nothing moves: the push stops at the footprint and the
        /// walk does not pull them back, because each accepted the spot it was
        /// pushed to. A pair that jittered would hash differently every tick.
        /// </summary>
        private static void TwoBodiesOnOnePointPartAndThenStandStill()
        {
            World world = Flat();
            int a = world.SpawnUnit(0, Role.Melee, At(20, 20));
            int b = world.SpawnUnit(0, Role.Melee, At(20, 20));
            Fix reach = world.Footprint(world.GetEntity(a)) + world.Footprint(world.GetEntity(b));
            Check(reach == Fix.Ratio(3, 4), "two shared melee rows do not reach three quarters of a cell");

            Run(world, 40);
            Fix apart = Distance(world, a, b);
            Check(apart >= reach, "two bodies on one point are still " + apart + " apart after 40 ticks");
            Check(apart < reach + Fix.Ratio(1, 4), "two bodies parted further than their footprints: " + apart);
            Check(Distance(world.GetEntity(a).Position, At(20, 20)) < Fix.OneF &&
                  Distance(world.GetEntity(b).Position, At(20, 20)) < Fix.OneF,
                "a parted body wandered more than a cell from where it was spawned");

            FixVec2 restA = world.GetEntity(a).Position, restB = world.GetEntity(b).Position;
            Run(world, 20);
            Check(world.GetEntity(a).Position.Equals(restA) && world.GetEntity(b).Position.Equals(restB),
                "two parted bodies kept moving");
            Check(world.GetEntity(a).Target.Equals(world.GetEntity(a).Position),
                "a parted idle body did not accept the spot it was pushed to");
        }

        /// <summary>
        /// Ten bodies on one point: every one of them leaves it, and no two of
        /// them end up closer than half a footprint sum. A full footprint is not
        /// asked of a stack, because ten bodies pushing in a ring push against
        /// each other and the clamp keeps any one of them from winning outright.
        /// </summary>
        private static void AStackOfTenLeavesThePoint()
        {
            World world = Flat();
            var ids = new List<int>();
            for (int n = 0; n < 10; n++) ids.Add(world.SpawnUnit(0, Role.Melee, At(30, 30)));
            Run(world, SettleTicks);

            Fix reach = Fix.Ratio(3, 4);
            Fix closest = Fix.FromInt(100);
            foreach (int i in ids)
            {
                foreach (int j in ids)
                {
                    if (j <= i) continue;
                    Fix d = Distance(world, i, j);
                    if (d < closest) closest = d;
                }
                Check(!world.GetEntity(i).Position.Equals(At(30, 30)), "a body of the stack is still on the point");
            }
            Check(closest >= reach / 2, "two bodies of a settled stack are only " + closest + " apart");
        }

        /// <summary>
        /// Hold is "do not move for any reason". A body spawned on top of a held
        /// one does all the moving.
        /// </summary>
        private static void AHeldBodyIsNotPushed()
        {
            World world = Flat();
            int held = world.SpawnUnit(0, Role.Melee, At(20, 20));
            int other = world.SpawnUnit(0, Role.Melee, At(20, 20));
            var hold = new List<Command> { new Command(0, 0, 0, CommandType.HoldPosition, held, FixVec2.Zero) };
            world.Step(hold);
            Run(world, 40);

            Check(world.GetEntity(held).Position.Equals(At(20, 20)), "a held body was pushed off its point");
            Check(Distance(world, held, other) >= Fix.Ratio(3, 4), "the body on top of a held one did not leave");
        }

        /// <summary>
        /// Two corridors one cell high, rock above and water below, with a
        /// two-level cliff across the middle of them: ten bodies stacked at the
        /// foot of the cliff and ten on its lip, and a walker driven into the
        /// lower stack. Nothing ever stands a level away from where it started,
        /// on rock, or in water, on any tick.
        /// </summary>
        private static void APushNeverCrossesACliffOrEntersWaterOrRock()
        {
            World world = Cliffs();
            var ids = new List<int>();
            for (int n = 0; n < 10; n++) ids.Add(world.SpawnUnit(0, Role.Melee, At(29, 20)));
            for (int n = 0; n < 10; n++) ids.Add(world.SpawnUnit(0, Role.Melee, At(30, 20)));
            int walker = world.SpawnUnit(0, Role.Melee, At(20, 20));
            ids.Add(walker);
            var order = new List<Command> { new Command(0, 0, 0, CommandType.Move, walker, At(29, 20)) };

            var level = new int[ids.Count];
            for (int n = 0; n < ids.Count; n++) level[n] = world.HeightAt(World.CellOf(world.GetEntity(ids[n]).Position));

            for (int t = 0; t < SettleTicks; t++)
            {
                world.Step(t == 0 ? order : new List<Command>());
                for (int n = 0; n < ids.Count; n++)
                {
                    int cell = World.CellOf(world.GetEntity(ids[n]).Position);
                    Check(world.HeightAt(cell) == level[n],
                        "a push moved a body from level " + level[n] + " to level " + world.HeightAt(cell) + " on tick " + t);
                    Check(world.IsPassable(cell, air: false), "a push put a body on impassable ground on tick " + t);
                }
            }

            // The stacks did spread, along the only axis the corridor leaves them.
            for (int n = 0; n < 20; n++)
            {
                Check(!world.GetEntity(ids[n]).Position.Equals(At(n < 10 ? 29 : 30, 20)),
                    "a body of a corridor stack never left its point");
            }
        }

        /// <summary>
        /// Eight bodies on the cell between a base and a resource node. They
        /// spread, and none of them into either cell: the pathfinder never routes
        /// through those, and a push must not either.
        /// </summary>
        private static void APushNeverEntersABuildingOrANode()
        {
            World world = Flat();
            int building = world.SpawnBuilding(0, Role.Base, At(25, 25), complete: true);
            int node = world.SpawnResourceNode(At(23, 25), 100);
            int buildingCell = World.CellOf(world.GetEntity(building).Position);
            int nodeCell = World.CellOf(world.GetEntity(node).Position);
            var ids = new List<int>();
            for (int n = 0; n < 8; n++) ids.Add(world.SpawnUnit(0, Role.Melee, At(24, 25)));

            for (int t = 0; t < SettleTicks; t++)
            {
                world.Step(new List<Command>());
                foreach (int i in ids)
                {
                    int cell = World.CellOf(world.GetEntity(i).Position);
                    Check(cell != buildingCell, "a push put a body inside a building on tick " + t);
                    Check(cell != nodeCell, "a push put a body on a resource node on tick " + t);
                }
            }
            foreach (int i in ids)
            {
                Check(!world.GetEntity(i).Position.Equals(At(24, 25)), "a body between a base and a node never left its point");
            }
        }

        /// <summary>
        /// The cliff fixture twice, hash for hash. Separation reads positions of
        /// other bodies, which is the first system to do so in the movement step,
        /// and this is what says it reads them in the same order on both peers.
        /// </summary>
        private static void SeparationIsTheSameOnBothPeers()
        {
            ulong[] left = RunCliffFixture();
            ulong[] right = RunCliffFixture();
            for (int t = 0; t < left.Length; t++)
            {
                Check(left[t] == right[t], "separation hash drift at tick " + t);
            }
        }

        private static ulong[] RunCliffFixture()
        {
            World world = Cliffs();
            for (int n = 0; n < 10; n++) world.SpawnUnit(0, Role.Melee, At(29, 20));
            for (int n = 0; n < 10; n++) world.SpawnUnit(1, Role.Worker, At(30, 20));
            int walker = world.SpawnUnit(0, Role.Melee, At(20, 20));
            var order = new List<Command> { new Command(0, 0, 0, CommandType.Move, walker, At(29, 20)) };
            var hashes = new ulong[SettleTicks];
            for (int t = 0; t < SettleTicks; t++)
            {
                world.Step(t == 0 ? order : new List<Command>());
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        /// <summary>
        /// Two hundred melee units, the population hard cap, packed onto ten
        /// cells so every one of them has a full neighbourhood to test against,
        /// against the same two hundred spread two cells apart so none of them
        /// has anyone in reach. The difference is what separation costs, and it
        /// has to stay under a millisecond a tick.
        ///
        /// The difference rather than the packed tick itself, because the rest
        /// of Step is not this issue's: target acquisition alone is a scan of
        /// every entity per unit, and under a Debug build that puts the spread
        /// tick near a millisecond before a single body is pushed. Wall-clock,
        /// the best of three runs each, so this is the one check in the harness
        /// that a slow enough machine can fail; the budget is several times what
        /// the pass takes under Debug on a laptop and twenty times under Release.
        /// </summary>
        private static void TwoHundredBodiesStepInsideTheBudget()
        {
            double packed = BestOfThree(spacing: Fix.Ratio(1, 5));
            double spread = BestOfThree(spacing: Fix.FromInt(2));
            double cost = packed - spread;
            Console.WriteLine("separation: " + World.PopulationHardCap + " bodies, " +
                              packed.ToString("F3") + " ms per tick packed, " +
                              spread.ToString("F3") + " ms spread, " +
                              cost.ToString("F3") + " ms for the pass");
            Check(cost < 1.0, "separation over " + World.PopulationHardCap + " packed bodies took " + cost.ToString("F3") + " ms a tick");
        }

        /// <summary>
        /// Milliseconds per tick over SettleTicks ticks, after a warm-up, with
        /// the cap's worth of melee units on a 25 by 8 lattice at the given
        /// spacing. The fastest of three, which is the measure least moved by
        /// whatever else the machine was doing.
        /// </summary>
        private static double BestOfThree(Fix spacing)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                World world = Flat();
                for (int n = 0; n < World.PopulationHardCap; n++)
                {
                    world.SpawnUnit(0, Role.Melee, new FixVec2(
                        Fix.FromInt(5) + spacing * (n % 25),
                        Fix.FromInt(5) + spacing * (n / 25)));
                }
                Run(world, 20);
                var clock = Stopwatch.StartNew();
                Run(world, SettleTicks);
                clock.Stop();
                double perTick = clock.Elapsed.TotalMilliseconds / SettleTicks;
                if (perTick < best) best = perTick;
            }
            return best;
        }

        // ---------------------------------------------------------------------

        private static World Flat()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.RockGolems);
            world.SetPeerFaction(1, Faction.Humans);
            return world;
        }

        /// <summary>
        /// Level 2 everywhere east of x = 30, a rock row at y = 19 and a water row
        /// at y = 21 across the whole map, so the y = 20 row is two corridors
        /// with a cliff between them.
        /// </summary>
        private static World Cliffs()
        {
            World world = Flat();
            for (int y = 0; y < World.GridSize; y++)
            {
                for (int x = 30; x < World.GridSize; x++) world.SetHeight(x, y, 2);
            }
            for (int x = 0; x < World.GridSize; x++)
            {
                world.SetTerrain(x, 19, TileKind.Rock);
                world.SetTerrain(x, 21, TileKind.Water);
            }
            return world;
        }

        private static void Run(World world, int ticks)
        {
            var idle = new List<Command>();
            for (int t = 0; t < ticks; t++) world.Step(idle);
        }

        private static Fix Distance(World world, int a, int b) =>
            Distance(world.GetEntity(a).Position, world.GetEntity(b).Position);

        private static Fix Distance(FixVec2 a, FixVec2 b) => (a - b).Magnitude;

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
