using System.Collections.Generic;

namespace WordCraft.Sim
{
    /// <summary>
    /// Eight-direction grid A* over a binary heap, followed by string-pulling.
    /// Deterministic by construction: integer costs only, obstacles read from
    /// entities in id order, and every tie broken by the lower cell index.
    /// </summary>
    public static class Pathfinder
    {
        /// <summary>Cost of a step to an orthogonal neighbour.</summary>
        public const int StraightCost = 10;

        /// <summary>Cost of a step to a diagonal neighbour: 10 * sqrt(2), rounded.</summary>
        public const int DiagonalCost = 14;

        /// <summary>
        /// Added to a ground step that climbs one level. Downhill adds nothing,
        /// so a route over a hill costs its climb once rather than twice. A step
        /// of two or more levels never gets here: World.StepPassable refuses it.
        /// </summary>
        public const int ClimbCost = 6;

        // Neighbour order is part of the network contract, not an implementation
        // detail: N, E, S, W, then NE, SE, SW, NW, with N at dy = -1. Orthogonals
        // come first so that, where the order is what decides, an equal-cost tie
        // goes to the orthogonal step. Today it decides nothing: cells leave the
        // heap in (f, cell) order whatever order they entered it, and a cell's
        // parent is the first popped cell to offer it a strictly lower g. The
        // order is pinned anyway, because the first change that breaks a tie by
        // insertion order would make it decide everything, silently, on one peer.
        private static readonly int[] NeighborDx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] NeighborDy = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private const int OrthogonalCount = 4;

        // A smoothed line that passes this close to a cell corner is treated as
        // crossing the corner itself, so both side cells have to be walkable. The
        // follower recomputes its heading every tick in Q16.16, and truncation
        // drifts it a few units of 2^-16 off the ideal line per tick; over a
        // segment the length of the map that stays far under 1/16 of a cell, so a
        // body can never end up on a side of a corner the line did not test.
        // Stated as the denominator of that fraction: 1/16 of a cell.
        private const int CornerSlackDenominator = 16;

        // Scratch reused between calls. The simulation is single threaded by rule,
        // and every slot is stamped with the current generation before it is read,
        // so nothing leaks from one query into the next. Nothing here outlives a
        // call as an answer: Replay runs two Worlds in one process, and a cache
        // kept across calls would carry one world's obstacles into the other's.
        private static readonly int[] gScore = new int[World.GridCells];
        private static readonly int[] cameFrom = new int[World.GridCells];
        private static readonly int[] stamp = new int[World.GridCells];
        private static readonly bool[] closed = new bool[World.GridCells];
        private static readonly int[] blockedStamp = new int[World.GridCells];
        private static readonly List<int> smoothed = new List<int>();
        private static int generation;

        // The open set: a binary min-heap of (f, cell), ordered by f and then by
        // the lower cell index. A cell is pushed again whenever its g improves and
        // the stale entry is skipped when it surfaces, so each cell is pushed at
        // most once per incoming neighbour: eight per cell bounds the heap.
        private static readonly int[] heapF = new int[World.GridCells * 8];
        private static readonly int[] heapCell = new int[World.GridCells * 8];
        private static int heapCount;

        /// <summary>
        /// Fills outPath with the cells to walk after leaving start, ending on goal.
        /// Returns false and leaves outPath empty when no route exists.
        ///
        /// The cells are waypoints, not neighbours: after the search the route is
        /// pulled straight across level ground wherever a straight line is as
        /// walkable as the cells it replaces (see LineOpen). The first waypoint is always the search's own first step, a
        /// neighbour of start, because the body may be standing anywhere inside
        /// start and only a line to a neighbour is walkable from every point of a
        /// cell; every later line runs from one cell centre to another, which is
        /// exactly the line the smoothing tested.
        ///
        /// air is passed straight to World.StepPassable. Terrain and elevation are
        /// tested per step rather than folded into the blocked grid, because the
        /// blocked grid forces start and goal open and a goal inside a lake has to
        /// stay unreachable.
        /// </summary>
        public static bool FindPath(World world, int start, int goal, List<int> outPath, bool air)
        {
            outPath.Clear();
            if (start == goal) return true;

            generation++;

            // ponytail: obstacles rebuilt from every entity on every query. Fine
            // for a few hundred entities; move to an incremental blocked grid owned
            // by the World (never a static one, see the scratch note above) if path
            // queries start showing up in the tick budget.
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive) continue;
                if (e.Kind != EntityKind.ResourceNode && e.Kind != EntityKind.Building) continue;
                blockedStamp[World.CellOf(e.Position)] = generation;
            }

            // Start and goal are always passable. A worker standing on a node, or
            // walking to one, must still get a path out of and into that cell.
            blockedStamp[start] = generation - 1;
            blockedStamp[goal] = generation - 1;

            Visit(start);
            gScore[start] = 0;
            cameFrom[start] = -1;

            heapCount = 0;
            Push(Heuristic(start, goal), start);

            while (heapCount > 0)
            {
                int current = Pop();
                if (closed[current]) continue;
                closed[current] = true;

                if (current == goal)
                {
                    Reconstruct(start, goal, outPath);
                    Smooth(world, start, outPath, air);
                    return true;
                }

                int cx = current % World.GridSize;
                int cy = current / World.GridSize;
                for (int n = 0; n < NeighborDx.Length; n++)
                {
                    int nx = cx + NeighborDx[n];
                    int ny = cy + NeighborDy[n];
                    if (nx < 0 || ny < 0 || nx >= World.GridSize || ny >= World.GridSize) continue;

                    int nc = ny * World.GridSize + nx;
                    bool diagonal = n >= OrthogonalCount;
                    // Terrain, debris and cliffs reject a neighbour exactly the way
                    // a building does, and at the same point in the loop.
                    if (diagonal ? !DiagonalOpen(world, current, nc, air) : !Open(world, current, nc, air)) continue;

                    Visit(nc);
                    if (closed[nc]) continue;

                    int step = diagonal ? DiagonalCost : StraightCost;
                    if (!air && world.HeightAt(nc) > world.HeightAt(current)) step += ClimbCost;

                    int tentative = gScore[current] + step;
                    if (tentative < gScore[nc])
                    {
                        gScore[nc] = tentative;
                        cameFrom[nc] = current;
                        Push(tentative + Heuristic(nc, goal), nc);
                    }
                }
            }

            return false;
        }

        /// <summary>Not a building, not a node, and walkable from the cell before it.</summary>
        private static bool Open(World world, int from, int to, bool air) =>
            blockedStamp[to] != generation && world.StepPassable(from, to, air);

        /// <summary>
        /// No corner cutting. A diagonal step is taken only when the whole two by
        /// two block it crosses is walkable in the direction of travel: the target
        /// from the start, both side cells from the start, and the target from
        /// both side cells.
        ///
        /// The last two are stricter than asking the side cells from the start
        /// alone, on purpose. A walking body does not cross a corner at a point;
        /// it crosses into one of the side cells for a moment, and which one
        /// depends on rounding. World.WalkableStep asks the step from whichever
        /// side it actually entered, so a side cell two levels below the target
        /// would stop the body dead on a route the pathfinder had promised.
        /// </summary>
        private static bool DiagonalOpen(World world, int from, int to, bool air)
        {
            int sideX = (from / World.GridSize) * World.GridSize + (to % World.GridSize);
            int sideY = (to / World.GridSize) * World.GridSize + (from % World.GridSize);
            return Open(world, from, to, air) &&
                   Open(world, from, sideX, air) && Open(world, sideX, to, air) &&
                   Open(world, from, sideY, air) && Open(world, sideY, to, air);
        }

        private static void Visit(int cell)
        {
            if (stamp[cell] == generation) return;
            stamp[cell] = generation;
            gScore[cell] = int.MaxValue;
            cameFrom[cell] = -1;
            closed[cell] = false;
        }

        /// <summary>
        /// Octile distance in the same units as the step costs. Admissible and
        /// consistent: it is the exact cost of the cheapest route on flat open
        /// ground, no step costs less than StraightCost, and a climb only adds.
        /// </summary>
        private static int Heuristic(int cell, int goal)
        {
            int dx = (cell % World.GridSize) - (goal % World.GridSize);
            int dy = (cell / World.GridSize) - (goal / World.GridSize);
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;
            int longer = dx > dy ? dx : dy;
            int shorter = dx > dy ? dy : dx;
            return StraightCost * longer + (DiagonalCost - StraightCost) * shorter;
        }

        private static void Reconstruct(int start, int goal, List<int> outPath)
        {
            for (int c = goal; c != start && c >= 0; c = cameFrom[c]) outPath.Add(c);
            outPath.Reverse();
        }

        /// <summary>
        /// String-pulling. From each anchor, keep the furthest later waypoint a
        /// straight line reaches without crossing anything the search would have
        /// refused, then make that waypoint the next anchor. The first waypoint is
        /// kept as it is; see FindPath for why.
        ///
        /// The result is then walked again end to end with the same line test. It
        /// cannot fail if the loop above is right, which is exactly why it is
        /// there: the cost of a wrong smoothing is a unit walking through a wall,
        /// and the fallback is the search's own route, which is always walkable.
        /// </summary>
        private static void Smooth(World world, int start, List<int> path, bool air)
        {
            if (path.Count < 3) return;

            smoothed.Clear();
            smoothed.Add(path[0]);
            int anchor = 0;
            while (anchor < path.Count - 1)
            {
                int furthest = anchor + 1;
                for (int j = anchor + 2; j < path.Count; j++)
                {
                    if (!LineOpen(world, path[anchor], path[j], air)) break;
                    furthest = j;
                }
                smoothed.Add(path[furthest]);
                anchor = furthest;
            }

            if (!LineOpen(world, start, smoothed[0], air)) return;
            for (int k = 0; k + 1 < smoothed.Count; k++)
            {
                if (!LineOpen(world, smoothed[k], smoothed[k + 1], air)) return;
            }

            path.Clear();
            path.AddRange(smoothed);
        }

        /// <summary>
        /// Whether a body walking the straight line from the centre of one cell to
        /// the centre of another stays on cells it may step between. Walks the
        /// line's supercover, every cell the segment touches, in integer
        /// arithmetic only: the segment crosses |dx| vertical and |dy| horizontal
        /// cell edges, and the next crossing of each is at parameter
        /// (2i + 1) / (2n) along it, so comparing (2ix + 1) * |dy| against
        /// (2iy + 1) * |dx| orders the two without a division.
        ///
        /// Each orthogonal crossing is asked the cliff rule the search asks. A
        /// crossing at, or within 1/CornerSlackDenominator of a cell of, a corner
        /// is taken as a diagonal step and asked DiagonalOpen. The distance from
        /// the corner is |D| / (2 * length) for the integer D the comparison
        /// already computes, so the slack test is D^2 * slack^2 &lt; 4 * length^2,
        /// still all integers.
        ///
        /// String-pulling knows nothing about cost, and a line that is walkable
        /// can still be dearer than the route it replaces: it can cut across a
        /// slope the search went round. So a line longer than one step is pulled
        /// only across ground at its starting cell's level, side cells of every
        /// corner included, and a climb or a descent keeps the search's own
        /// waypoints. A single step is the search's own step and is asked exactly
        /// what the search asked of it. Air has no levels and no such limit.
        /// </summary>
        private static bool LineOpen(World world, int from, int to, bool air)
        {
            int x = from % World.GridSize, y = from / World.GridSize;
            int dx = (to % World.GridSize) - x;
            int dy = (to / World.GridSize) - y;
            int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
            int nx = dx < 0 ? -dx : dx, ny = dy < 0 ? -dy : dy;
            long lengthSquared = (long)nx * nx + (long)ny * ny;
            long slack = (long)CornerSlackDenominator * CornerSlackDenominator;
            int level = air || (nx <= 1 && ny <= 1) ? -1 : world.HeightAt(from);

            int ix = 0, iy = 0;
            while (ix < nx || iy < ny)
            {
                int cell = y * World.GridSize + x;
                bool stepX, stepY;
                if (ix < nx && iy < ny)
                {
                    long d = (long)(2 * ix + 1) * ny - (long)(2 * iy + 1) * nx;
                    bool corner = d * d * slack < 4 * lengthSquared;
                    stepX = corner || d < 0;
                    stepY = corner || d > 0;
                }
                else
                {
                    stepX = ix < nx;
                    stepY = !stepX;
                }

                int next = (y + (stepY ? sy : 0)) * World.GridSize + (x + (stepX ? sx : 0));
                if (stepX && stepY)
                {
                    if (!DiagonalOpen(world, cell, next, air)) return false;
                    if (level >= 0 &&
                        (world.HeightAt(y * World.GridSize + x + sx) != level ||
                         world.HeightAt((y + sy) * World.GridSize + x) != level)) return false;
                }
                else if (!Open(world, cell, next, air)) return false;
                if (level >= 0 && world.HeightAt(next) != level) return false;

                if (stepX) { x += sx; ix++; }
                if (stepY) { y += sy; iy++; }
            }
            return true;
        }

        private static bool Before(int fa, int ca, int fb, int cb) => fa < fb || (fa == fb && ca < cb);

        private static void Push(int f, int cell)
        {
            int i = heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Before(f, cell, heapF[parent], heapCell[parent])) break;
                heapF[i] = heapF[parent];
                heapCell[i] = heapCell[parent];
                i = parent;
            }
            heapF[i] = f;
            heapCell[i] = cell;
        }

        /// <summary>
        /// Removes and returns the lowest (f, cell). Lower f wins; equal f breaks
        /// to the lower cell index, never to insertion order, which would depend on
        /// how the search got there.
        /// </summary>
        private static int Pop()
        {
            int top = heapCell[0];
            heapCount--;
            int f = heapF[heapCount];
            int cell = heapCell[heapCount];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= heapCount) break;
                if (child + 1 < heapCount && Before(heapF[child + 1], heapCell[child + 1], heapF[child], heapCell[child])) child++;
                if (!Before(heapF[child], heapCell[child], f, cell)) break;
                heapF[i] = heapF[child];
                heapCell[i] = heapCell[child];
                i = child;
            }
            if (heapCount > 0)
            {
                heapF[i] = f;
                heapCell[i] = cell;
            }
            return top;
        }
    }
}
