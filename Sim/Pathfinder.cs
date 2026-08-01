using System.Collections.Generic;

namespace WordCraft.Sim
{
    /// <summary>
    /// Grid A*. Deterministic by construction: integer costs only, obstacles read
    /// from entities in id order, and every tie broken by the lower cell index.
    /// </summary>
    public static class Pathfinder
    {
        // Neighbor order is part of the network contract, not an implementation
        // detail. It decides which of several equal-cost paths wins, so a peer
        // that reorders this array desyncs. North, east, south, west, always.
        private static readonly int[] NeighborDx = { 0, 1, 0, -1 };
        private static readonly int[] NeighborDy = { -1, 0, 1, 0 };

        // Scratch reused between calls. The simulation is single threaded by rule,
        // and every slot is stamped with the current generation before it is read,
        // so nothing leaks from one query into the next.
        private static readonly int[] gScore = new int[World.GridCells];
        private static readonly int[] cameFrom = new int[World.GridCells];
        private static readonly int[] stamp = new int[World.GridCells];
        private static readonly bool[] closed = new bool[World.GridCells];
        private static readonly int[] blockedStamp = new int[World.GridCells];
        private static readonly List<int> open = new List<int>();
        private static int generation;

        /// <summary>
        /// Fills outPath with the cells to walk after leaving start, ending on goal.
        /// Returns false and leaves outPath empty when no route exists.
        /// </summary>
        public static bool FindPath(World world, int start, int goal, List<int> outPath)
        {
            outPath.Clear();
            if (start == goal) return true;

            generation++;

            // ponytail: obstacles rebuilt from every entity on every query, and the
            // open set is a linearly scanned List. Fine for a few hundred entities;
            // move to an incremental blocked grid and a binary heap if path queries
            // start showing up in the tick budget.
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

            open.Clear();
            open.Add(start);

            while (open.Count > 0)
            {
                int bestIdx = 0;
                int bestF = gScore[open[0]] + Heuristic(open[0], goal);
                for (int i = 1; i < open.Count; i++)
                {
                    int cell = open[i];
                    int f = gScore[cell] + Heuristic(cell, goal);
                    // Lower f wins. Equal f breaks to the lower cell index, never to
                    // insertion order, which would depend on how we got here.
                    if (f < bestF || (f == bestF && cell < open[bestIdx]))
                    {
                        bestIdx = i;
                        bestF = f;
                    }
                }

                int current = open[bestIdx];
                open.RemoveAt(bestIdx);
                if (closed[current]) continue;
                closed[current] = true;

                if (current == goal)
                {
                    Reconstruct(start, goal, outPath);
                    return true;
                }

                int cx = current % World.GridSize;
                int cy = current / World.GridSize;
                for (int n = 0; n < 4; n++)
                {
                    int nx = cx + NeighborDx[n];
                    int ny = cy + NeighborDy[n];
                    if (nx < 0 || ny < 0 || nx >= World.GridSize || ny >= World.GridSize) continue;

                    int nc = ny * World.GridSize + nx;
                    if (blockedStamp[nc] == generation) continue;

                    Visit(nc);
                    if (closed[nc]) continue;

                    int tentative = gScore[current] + 1;
                    if (tentative < gScore[nc])
                    {
                        gScore[nc] = tentative;
                        cameFrom[nc] = current;
                        open.Add(nc);
                    }
                }
            }

            return false;
        }

        private static void Visit(int cell)
        {
            if (stamp[cell] == generation) return;
            stamp[cell] = generation;
            gScore[cell] = int.MaxValue;
            cameFrom[cell] = -1;
            closed[cell] = false;
        }

        private static int Heuristic(int cell, int goal)
        {
            int dx = (cell % World.GridSize) - (goal % World.GridSize);
            int dy = (cell / World.GridSize) - (goal / World.GridSize);
            return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy);
        }

        private static void Reconstruct(int start, int goal, List<int> outPath)
        {
            for (int c = goal; c != start && c >= 0; c = cameFrom[c]) outPath.Add(c);
            outPath.Reverse();
        }
    }
}
