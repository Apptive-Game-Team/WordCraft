using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Issue #158's checks: elevation, the cliff rule, and the eight-direction
    /// pathfinder that reads both. Kept out of Program.cs the way the other
    /// per-issue files are, so Program.cs only carries the one line that calls
    /// Check().
    ///
    /// Three kinds of question. The rule itself on hand-built layouts small enough
    /// that the right answer is obvious: a cliff refuses a walker and not a
    /// flier, a slope is walked and charged, a diagonal never cuts a corner. Then
    /// the smoothing, which is where a unit would walk through a wall if it were
    /// wrong, checked against a supercover walk written here independently of the
    /// pathfinder's own. Then the map: symmetric, every open cell reachable from
    /// both bases at the same cost as its mirror, and its raised ground reachable
    /// only by the ramps.
    /// </summary>
    internal static class TerrainChecks
    {
        // Same value Replay/Program.cs seeds its own checks with, kept as this
        // file's own constant for the reason ReplayRoundTrip.cs gives.
        private const ulong Seed = 0xC0FFEE;

        private const int WalkTicks = 1500;

        public static void Check()
        {
            TheCliffRuleIsTheStepRule();
            ACliffStopsGroundButNotAir();
            ASlopeIsWalkedAndCharged();
            ADiagonalNeverCutsACorner();
            AStraightLineNeverSlipsThroughACorner();
            MapHeightsAreExactlySymmetric();
            EveryOpenCellIsReachableFromBothBasesAtMirroredCost();
            BothBasesReachBothContestedNodes();
            SmoothedPathsCrossOnlyWalkableSteps();
            AUnitSentUpACliffTakesTheRamp();
        }

        /// <summary>
        /// StepPassable as a table. One level is a slope both ways, two is a cliff
        /// both ways, air ignores all of it, and the cell being entered still has
        /// to be passable terrain whatever the heights say.
        /// </summary>
        private static void TheCliffRuleIsTheStepRule()
        {
            var world = new World(Seed);
            world.SetHeight(11, 10, 1);
            world.SetHeight(12, 10, 2);
            world.SetHeight(13, 10, 3);
            world.SetTerrain(14, 10, TileKind.Rock);
            int flat = Cell(10, 10), one = Cell(11, 10), two = Cell(12, 10), three = Cell(13, 10);

            Check(world.StepPassable(flat, one, air: false), "a one-level slope refused a walker going up");
            Check(world.StepPassable(one, flat, air: false), "a one-level slope refused a walker going down");
            Check(world.StepPassable(one, two, air: false), "a one-level slope between raised cells refused a walker");
            Check(!world.StepPassable(flat, two, air: false), "a two-level cliff let a walker climb it");
            Check(!world.StepPassable(two, flat, air: false), "a two-level cliff let a walker drop off it");
            Check(!world.StepPassable(one, three, air: false), "a two-level cliff between raised cells let a walker climb it");
            Check(world.StepPassable(flat, two, air: true), "a flier was refused a cliff");
            Check(world.StepPassable(two, flat, air: true), "a flier was refused the drop off a cliff");
            Check(!world.StepPassable(three, Cell(14, 10), air: false), "a walker stepped onto rock at its own level");

            world.SetHeight(15, 10, World.MaxHeight + 5);
            world.SetHeight(16, 10, -4);
            Check(world.HeightAt(Cell(15, 10)) == World.MaxHeight, "SetHeight let a level above MaxHeight through");
            Check(world.HeightAt(Cell(16, 10)) == 0, "SetHeight let a negative level through");

            // Heights are state: two worlds that differ in one cell's level have to
            // hash apart before anything moves.
            var raised = new World(Seed);
            var level = new World(Seed);
            raised.SetHeight(40, 40, 1);
            Check(raised.Hash() != level.Hash(), "a raised cell does not change the state hash");
        }

        /// <summary>
        /// A level-2 band clean across the map. Nothing on the ground has a route
        /// over it, so the walker is left on the straight-line fallback, and that
        /// fallback is what MoveSystem stops at the foot of the cliff. The flier
        /// takes the same order and arrives.
        /// </summary>
        private static void ACliffStopsGroundButNotAir()
        {
            var world = new World(Seed);
            world.SetPeerFaction(0, Faction.Hellfire);
            for (int y = 0; y < World.GridSize; y++)
            {
                world.SetHeight(32, y, 2);
                world.SetHeight(33, y, 2);
            }

            var path = new List<int>();
            Check(!Pathfinder.FindPath(world, Cell(30, 30), Cell(40, 30), path, air: false),
                "the pathfinder found a ground route over a two-level cliff");
            Check(Pathfinder.FindPath(world, Cell(30, 30), Cell(40, 30), path, air: true),
                "the pathfinder found no air route over a two-level cliff");

            // 지옥불's ranged slot 0 flies; melee walks.
            int flier = world.SpawnUnit(0, Role.Ranged, At(30, 30));
            int walker = world.SpawnUnit(0, Role.Melee, At(30, 34));
            var orders = new List<Command>
            {
                new Command(0, 0, 0, CommandType.Move, flier, At(40, 30)),
                new Command(0, 0, 1, CommandType.Move, walker, At(40, 34)),
            };
            bool climbed = false;
            for (int t = 0; t < 200; t++)
            {
                world.Step(t == 0 ? orders : new List<Command>());
                if (world.HeightAt(World.CellOf(world.GetEntity(walker).Position)) != 0) climbed = true;
            }

            Check(world.GetEntity(flier).Position.Equals(At(40, 30)), "the flier never crossed the cliff");
            Check(!climbed, "a walker with no route stood on top of a two-level cliff");
            Check(world.GetEntity(walker).Position.X < Fix.FromInt(32), "the walker crossed a two-level cliff");
            Check(world.GetEntity(walker).Position.X > Fix.FromInt(31),
                "the walker stopped short of the cliff, so the cliff is not what stopped it");
        }

        /// <summary>
        /// Three layouts along the same ten-cell walk, each answering one part.
        ///
        /// A level-1 band across the whole map: the only way over is up a slope,
        /// and the walker takes it. One level-1 bump on the line: climbing it costs
        /// ClimbCost, going round it costs two diagonals instead of two straights,
        /// 2 * (DiagonalCost - StraightCost) = 8 against 6, so the walker climbs.
        /// Five bumps on the line: 5 * 6 = 30 against the same 8, so the walker
        /// goes round and never stands on one. The pair pins the climb as charged
        /// and charged less than a detour, which is the whole of the cost rule.
        /// </summary>
        private static void ASlopeIsWalkedAndCharged()
        {
            var band = new World(Seed);
            for (int y = 0; y < World.GridSize; y++) band.SetHeight(15, y, 1);
            Walk(band, At(10, 10), At(20, 10), out bool arrived, out int highest, out _);
            Check(arrived, "a walker never crossed a one-level band");
            Check(highest == 1, "a walker crossed a one-level band without standing on it");

            var bump = new World(Seed);
            bump.SetHeight(15, 10, 1);
            Walk(bump, At(10, 10), At(20, 10), out arrived, out highest, out _);
            Check(arrived, "a walker never got past a single one-level bump");
            Check(highest == 1, "a walker went round one bump, so a climb costs more than a detour");

            var bumps = new World(Seed);
            foreach (int x in new[] { 11, 13, 15, 17, 19 }) bumps.SetHeight(x, 10, 1);
            Walk(bumps, At(10, 10), At(20, 10), out arrived, out highest, out _);
            Check(arrived, "a walker never got past a row of bumps");
            Check(highest == 0, "a walker climbed five bumps rather than step round them, so a climb costs nothing");

            // The control: flat ground is one straight run after the first step.
            var flat = new World(Seed);
            var path = new List<int>();
            Check(Pathfinder.FindPath(flat, Cell(10, 10), Cell(20, 10), path, air: false), "no path on flat ground");
            Check(path.Count == 2 && path[0] == Cell(11, 10) && path[1] == Cell(20, 10),
                "a straight walk on flat ground was not smoothed to one run: " + Describe(path));
        }

        /// <summary>
        /// The one-step diagonal from (10,10) to (11,11) with one side cell made a
        /// wall: first rock, then a cliff, then the cliff on the far side of the
        /// side cell, where the step into the side cell is a slope but the step out
        /// of it is not. Each time the route has to go round the other side, two
        /// orthogonal steps, because a body cutting that corner would pass through
        /// the side cell on the way.
        /// </summary>
        private static void ADiagonalNeverCutsACorner()
        {
            var open = new World(Seed);
            var path = new List<int>();
            Check(Pathfinder.FindPath(open, Cell(10, 10), Cell(11, 11), path, air: false) &&
                  path.Count == 1 && path[0] == Cell(11, 11),
                "on open ground the diagonal is not one step: " + Describe(path));

            var rock = new World(Seed);
            rock.SetTerrain(11, 10, TileKind.Rock);
            CornerGoesRound(rock, "rock");

            var cliff = new World(Seed);
            cliff.SetHeight(11, 10, 2);
            CornerGoesRound(cliff, "a cliff");

            // (10,10) at 1, side cell (11,10) at 0, target (11,11) at 2. Every step
            // the start can see is a slope, so only the stricter rule refuses it.
            var farCliff = new World(Seed);
            farCliff.SetHeight(10, 10, 1);
            farCliff.SetHeight(11, 11, 2);
            farCliff.SetHeight(10, 11, 1);
            path.Clear();
            Check(Pathfinder.FindPath(farCliff, Cell(10, 10), Cell(11, 11), path, air: false) &&
                  path.Count == 2 && path[0] == Cell(10, 11),
                "a diagonal crossed a side cell two levels below its target: " + Describe(path));
        }

        /// <summary>
        /// The same corner rule on the walk rather than the route. A walker sent
        /// at a lake has no route, so it walks the straight line, and the line
        /// from (10,10) to (14,14) runs exactly through the corner between (11,11)
        /// and (12,12), with rock on one side of it. Both ends of that step are
        /// open, so a follower that asked only the ends would slip through the
        /// corner and stop at the shore; MoveSystem stops it at the corner instead.
        /// </summary>
        private static void AStraightLineNeverSlipsThroughACorner()
        {
            var world = new World(Seed);
            world.SetTerrain(12, 11, TileKind.Rock);
            world.SetTerrain(14, 14, TileKind.Water);
            var path = new List<int>();
            Check(!Pathfinder.FindPath(world, Cell(10, 10), Cell(14, 14), path, air: false),
                "a route into a lake exists, so the walk below is not the straight-line fallback");

            Walk(world, At(10, 10), At(14, 14), out _, out _, out bool trespassed);
            Entity walker = world.GetEntity(0);
            Check(!trespassed, "the straight-line walker stood on rock or water");
            Check(World.CellOf(walker.Position) == Cell(11, 11),
                "the straight-line walker went through a rock corner and stopped at " +
                walker.Position + " rather than at the corner");
        }

        private static void CornerGoesRound(World world, string what)
        {
            var path = new List<int>();
            Check(Pathfinder.FindPath(world, Cell(10, 10), Cell(11, 11), path, air: false),
                "no route round a corner of " + what);
            Check(path.Count == 2 && path[0] == Cell(10, 11) && path[1] == Cell(11, 11),
                "a diagonal cut the corner of " + what + ": " + Describe(path));

            Walk(world, At(10, 10), At(11, 11), out bool arrived, out _, out bool trespassed);
            Check(arrived, "a walker never got round a corner of " + what);
            Check(!trespassed, "a walker stepped through the corner of " + what);
        }

        /// <summary>
        /// The elevation half of Program.cs's MapIsExactlySymmetric, and the
        /// assurance that there is elevation to be symmetric about: the barrier is
        /// at the top level, some ground is two levels up, and some is one.
        /// </summary>
        private static void MapHeightsAreExactlySymmetric()
        {
            World world = Map();
            int[] count = new int[World.MaxHeight + 1];
            for (int y = 0; y < World.GridSize; y++)
            {
                for (int x = 0; x < World.GridSize; x++)
                {
                    int level = world.HeightAt(Cell(x, y));
                    int mirror = world.HeightAt(Cell(World.GridSize - 1 - x, World.GridSize - 1 - y));
                    Check(level == mirror, "height at " + x + "," + y + " is " + level + " but its mirror is " + mirror);
                    count[level]++;
                    if (world.TerrainAt(Cell(x, y)) == TileKind.Water)
                        Check(level == 0, "a lake cell at " + x + "," + y + " stands at level " + level);
                    if (world.TerrainAt(Cell(x, y)) == TileKind.Rock)
                        Check(level == World.MaxHeight, "barrier rock at " + x + "," + y + " stands at level " + level);
                }
            }
            for (int level = 1; level <= World.MaxHeight; level++)
            {
                Check(count[level] > 0, "the map has no ground at level " + level + ", so the symmetry check proves less");
            }

            // Every body the scenario spawns stands on the floor.
            for (int i = 0; i < world.EntityCount; i++)
            {
                int cell = World.CellOf(world.GetEntity(i).Position);
                Check(world.HeightAt(cell) == 0, "entity " + i + " spawns at level " + world.HeightAt(cell));
            }
        }

        /// <summary>
        /// Dijkstra over the map written here from the rule rather than from the
        /// pathfinder: every open cell has a finite ground cost from both bases, so
        /// no raised ground is sealed off and no pocket exists that only one side
        /// can reach, and the cost from one base to a cell equals the cost from the
        /// other base to that cell's mirror, which is what a fair map means once
        /// steps stop costing the same.
        ///
        /// And the cliffs are real: somewhere a level-0 cell borders a level-2 cell,
        /// which is the ground nobody walks between except by a ramp.
        /// </summary>
        private static void EveryOpenCellIsReachableFromBothBasesAtMirroredCost()
        {
            World world = Map();
            int[] from0 = GroundCosts(world, Cell(8, 8));
            int[] from1 = GroundCosts(world, Cell(55, 55));

            int cliffs = 0;
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (world.TerrainAt(cell) != TileKind.Open) continue;
                int x = cell % World.GridSize, y = cell / World.GridSize;
                Check(from0[cell] < int.MaxValue, "open cell " + x + "," + y + " is unreachable from peer 0's base");
                Check(from1[cell] < int.MaxValue, "open cell " + x + "," + y + " is unreachable from peer 1's base");
                int mirror = Cell(World.GridSize - 1 - x, World.GridSize - 1 - y);
                Check(from0[cell] == from1[mirror],
                    "peer 0 reaches " + x + "," + y + " for " + from0[cell] +
                    " but peer 1 reaches its mirror for " + from1[mirror]);

                if (x + 1 < World.GridSize && world.TerrainAt(cell + 1) == TileKind.Open &&
                    Math.Abs(world.HeightAt(cell) - world.HeightAt(cell + 1)) >= 2) cliffs++;
                if (y + 1 < World.GridSize && world.TerrainAt(cell + World.GridSize) == TileKind.Open &&
                    Math.Abs(world.HeightAt(cell) - world.HeightAt(cell + World.GridSize)) >= 2) cliffs++;
            }
            Check(cliffs > 0, "the map has no two-level cliff between open cells");
        }

        /// <summary>
        /// The two contested nodes are the reason to walk the lanes, so both
        /// starts have to have a route to both, through the real pathfinder and
        /// over the real scenario with every building and node standing.
        /// </summary>
        private static void BothBasesReachBothContestedNodes()
        {
            World world = Map();
            var path = new List<int>();
            foreach (int start in new[] { Cell(8, 8), Cell(55, 55) })
            {
                foreach (int node in new[] { Cell(24, 39), Cell(39, 24) })
                {
                    Check(Pathfinder.FindPath(world, start, node, path, air: false),
                        "no ground route from base cell " + start + " to contested node cell " + node);
                }
            }
        }

        /// <summary>
        /// Every route from peer 0's base to a spread of destinations, walked
        /// segment by segment with a supercover written here: every cell each
        /// straight run touches, each step between them asked StepPassable, and a
        /// run through a cell corner made to pass both side cells. A smoothing
        /// that let a line clip a cliff or a lake shore fails here.
        ///
        /// And it has to have smoothed something, or this proves nothing: at least
        /// one route must hold fewer waypoints than the cells it crosses.
        /// </summary>
        private static void SmoothedPathsCrossOnlyWalkableSteps()
        {
            World world = Map();
            int start = Cell(10, 8);
            var path = new List<int>();
            bool anySmoothed = false;
            int routes = 0;
            for (int y = 2; y < World.GridSize; y += 5)
            {
                for (int x = 2; x < World.GridSize; x += 5)
                {
                    int goal = Cell(x, y);
                    if (!Pathfinder.FindPath(world, start, goal, path, air: false)) continue;
                    routes++;

                    int previous = start;
                    int crossed = 0;
                    foreach (int waypoint in path)
                    {
                        int cells = SupercoverWalkable(world, previous, waypoint);
                        Check(cells >= 0, "the route to " + x + "," + y + " runs from cell " + previous +
                            " to cell " + waypoint + " across a step nobody may take: " + Describe(path));
                        crossed += cells;
                        previous = waypoint;
                    }
                    Check(path[path.Count - 1] == goal, "the route to " + x + "," + y + " ends somewhere else");
                    if (path.Count < crossed) anySmoothed = true;
                }
            }
            Check(routes > 100, "only " + routes + " routes found, so the smoothing check covers too little");
            Check(anySmoothed, "no route was smoothed, so the smoothing check proves nothing");

            // Then walked, over a coarser spread and on the bare terrain, so nothing
            // but the ground decides the route. A route the line test passes but
            // MoveSystem refuses part of the way is a unit standing still for the
            // rest of the match, which no amount of checking the cells would show.
            int walked = 0;
            for (int y = 2; y < World.GridSize; y += 10)
            {
                for (int x = 2; x < World.GridSize; x += 10)
                {
                    if (!world.IsPassable(Cell(x, y), air: false)) continue;
                    walked++;
                    Walk(Copy(world), At(10, 8), At(x, y), out bool arrived, out _, out bool trespassed);
                    Check(arrived, "a walker sent along the smoothed route to " + x + "," + y + " never arrived");
                    Check(!trespassed, "a walker sent to " + x + "," + y + " crossed a cliff or impassable ground");
                }
            }
            Check(walked > 30, "only " + walked + " routes walked");
        }

        /// <summary>
        /// Sent from the foot of a cliff to the top of it, a walker goes the long
        /// way round to the ramp: it arrives, it stood on the ramp's level on the
        /// way, and no tick moved it between cells more than one level apart. Once
        /// for the flank mesa and once for a lane-mouth plateau, then the mesa walk
        /// again for its per-tick hashes, because a route this long is where an
        /// ordering difference between peers would show.
        /// </summary>
        private static void AUnitSentUpACliffTakesTheRamp()
        {
            World map = Map();

            // The flank mesa: (44,9) is the floor at its west face, (47,9) its top.
            ClimbsByTheRamp(map, At(44, 9), At(47, 9), "the flank mesa", out ulong[] first);
            ClimbsByTheRamp(map, At(44, 9), At(47, 9), "the flank mesa", out ulong[] second);
            for (int t = 0; t < first.Length; t++)
            {
                Check(first[t] == second[t], "the ramp walk hash drifted at tick " + t);
            }

            // Peer 0's plateau beside the lane at (39,24): (38,20) is the floor on
            // the lane side, (40,18) the top.
            ClimbsByTheRamp(map, At(38, 20), At(40, 18), "a lane-mouth plateau", out _);
        }

        private static void ClimbsByTheRamp(World map, FixVec2 foot, FixVec2 top, string what, out ulong[] hashes)
        {
            Check(map.HeightAt(World.CellOf(foot)) == 0, what + ": the foot is not on the floor");
            Check(map.HeightAt(World.CellOf(top)) == 2, what + ": the top is not two levels up");

            World world = Copy(map);
            hashes = Walk(world, foot, top, out bool arrived, out _, out bool trespassed, out bool rampTaken);
            Check(arrived, "a walker sent up " + what + " never arrived");
            Check(!trespassed, "a walker sent up " + what + " crossed a cliff or stood on impassable ground");
            Check(rampTaken, "a walker reached the top of " + what + " without standing on its ramp");
        }

        // ---------------------------------------------------------------------

        private static ulong[] Walk(World world, FixVec2 from, FixVec2 to,
            out bool arrived, out int highest, out bool trespassed) =>
            Walk(world, from, to, out arrived, out highest, out trespassed, out _);

        /// <summary>
        /// One melee unit, one move order, run until it arrives or WalkTicks run
        /// out. Trespass is any tick that puts it on impassable terrain or moves it
        /// between two cells more than one level apart.
        /// </summary>
        private static ulong[] Walk(World world, FixVec2 from, FixVec2 to,
            out bool arrived, out int highest, out bool trespassed, out bool rampTaken)
        {
            int unit = world.SpawnUnit(0, Role.Melee, from);
            var order = new List<Command> { new Command(0, 0, 0, CommandType.Move, unit, to) };
            var idle = new List<Command>();
            var hashes = new ulong[WalkTicks];

            arrived = false;
            trespassed = false;
            rampTaken = false;
            highest = 0;
            int previous = World.CellOf(from);
            for (int t = 0; t < WalkTicks; t++)
            {
                world.Step(t == 0 ? order : idle);
                hashes[t] = world.Hash();
                FixVec2 at = world.GetEntity(unit).Position;
                int cell = World.CellOf(at);
                int level = world.HeightAt(cell);
                if (level > highest) highest = level;
                if (level == 1) rampTaken = true;
                if (!world.IsPassable(cell, air: false)) trespassed = true;
                if (Math.Abs(level - world.HeightAt(previous)) > 1) trespassed = true;
                previous = cell;
                if (at.Equals(to)) { arrived = true; break; }
            }
            return hashes;
        }

        /// <summary>
        /// The supercover of the segment between two cell centres, walked in
        /// integer arithmetic: crossing the next vertical edge is at parameter
        /// (2i + 1) / 2|dx|, the next horizontal one at (2j + 1) / 2|dy|. Returns
        /// how many cell steps it took, or -1 at the first step nobody may take.
        /// Written apart from Pathfinder.LineOpen on purpose, and stricter only at
        /// exact corners, where both side cells have to be walkable both ways.
        /// </summary>
        private static int SupercoverWalkable(World world, int from, int to)
        {
            int x = from % World.GridSize, y = from / World.GridSize;
            int tx = to % World.GridSize, ty = to / World.GridSize;
            int sx = Math.Sign(tx - x), sy = Math.Sign(ty - y);
            int nx = Math.Abs(tx - x), ny = Math.Abs(ty - y);
            int i = 0, j = 0, steps = 0;
            while (i < nx || j < ny)
            {
                int here = Cell(x, y);
                long xFirst = i < nx ? (long)(2 * i + 1) * ny : long.MaxValue;
                long yFirst = j < ny ? (long)(2 * j + 1) * nx : long.MaxValue;
                if (i < nx && j < ny && xFirst == yFirst)
                {
                    int sideX = Cell(x + sx, y), sideY = Cell(x, y + sy), corner = Cell(x + sx, y + sy);
                    if (!world.StepPassable(here, sideX, false) || !world.StepPassable(sideX, corner, false) ||
                        !world.StepPassable(here, sideY, false) || !world.StepPassable(sideY, corner, false) ||
                        !world.StepPassable(here, corner, false)) return -1;
                    x += sx; y += sy; i++; j++;
                }
                else if (j >= ny || (i < nx && xFirst < yFirst))
                {
                    if (!world.StepPassable(here, Cell(x + sx, y), false)) return -1;
                    x += sx; i++;
                }
                else
                {
                    if (!world.StepPassable(here, Cell(x, y + sy), false)) return -1;
                    y += sy; j++;
                }
                steps++;
            }
            return steps;
        }

        /// <summary>
        /// Ground costs from one cell to every other, under the rule as issue #158
        /// writes it: straight 10, diagonal 14, plus 6 for a step up one level, no
        /// diagonal unless the whole two by two block it crosses is walkable.
        /// Terrain and elevation only, no entities. A plain O(n^2) Dijkstra; the
        /// map is 4096 cells and this runs twice.
        /// </summary>
        private static int[] GroundCosts(World world, int source)
        {
            var cost = new int[World.GridCells];
            var done = new bool[World.GridCells];
            for (int c = 0; c < cost.Length; c++) cost[c] = int.MaxValue;
            cost[source] = 0;

            int[] dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
            int[] dy = { -1, 0, 1, 0, -1, 1, 1, -1 };
            while (true)
            {
                int best = -1;
                for (int c = 0; c < cost.Length; c++)
                {
                    if (!done[c] && cost[c] < int.MaxValue && (best < 0 || cost[c] < cost[best])) best = c;
                }
                if (best < 0) break;
                done[best] = true;

                int x = best % World.GridSize, y = best / World.GridSize;
                for (int n = 0; n < 8; n++)
                {
                    int nx = x + dx[n], ny = y + dy[n];
                    if (nx < 0 || ny < 0 || nx >= World.GridSize || ny >= World.GridSize) continue;
                    int next = Cell(nx, ny);
                    if (!world.StepPassable(best, next, false)) continue;
                    if (n >= 4)
                    {
                        int sideX = Cell(nx, y), sideY = Cell(x, ny);
                        if (!world.StepPassable(best, sideX, false) || !world.StepPassable(sideX, next, false) ||
                            !world.StepPassable(best, sideY, false) || !world.StepPassable(sideY, next, false)) continue;
                    }
                    int step = n >= 4 ? 14 : 10;
                    if (world.HeightAt(next) > world.HeightAt(best)) step += 6;
                    if (cost[best] + step < cost[next]) cost[next] = cost[best] + step;
                }
            }
            return cost;
        }

        private static World Map() => MatchScenario.Build(Seed, Faction.TreeSpirits, Faction.TreeSpirits);

        /// <summary>The scenario's terrain and elevation with nobody standing on it.</summary>
        private static World Copy(World map)
        {
            var world = new World(Seed);
            for (int y = 0; y < World.GridSize; y++)
            {
                for (int x = 0; x < World.GridSize; x++)
                {
                    world.SetTerrain(x, y, map.TerrainAt(Cell(x, y)));
                    world.SetHeight(x, y, map.HeightAt(Cell(x, y)));
                }
            }
            return world;
        }

        private static int Cell(int x, int y) => y * World.GridSize + x;

        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }

        private static string Describe(List<int> path)
        {
            var parts = new List<string>();
            foreach (int c in path) parts.Add("(" + (c % World.GridSize) + "," + (c / World.GridSize) + ")");
            return "[" + string.Join(" ", parts) + "]";
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
