namespace WordCraft.Sim
{
    /// <summary>
    /// Separation: walkers that share ground push each other apart. Two bodies
    /// closer than the sum of their footprints each move half of the overlap
    /// away from the other, no further than half a Speed in a tick, and never
    /// anywhere the walk itself could not go.
    ///
    /// A second pass over the same entities MoveSystem just walked, not a system
    /// of its own in Step. The walk and the push both write Position, and a
    /// system between them that read it would see a body half way through its
    /// tick. Running inside MoveSystem keeps "where everything stands" a single
    /// moment per tick, which is the moment CombatSystem reads on the next one.
    ///
    /// Every push is computed from the positions the walk left and only then
    /// applied, so the result does not depend on which body was pushed first.
    /// The accumulation is integer addition in a fixed order besides, so nothing
    /// is left to associativity either.
    ///
    /// Neighbours come out of a grid of cell buckets. A bucket is a linked list
    /// threaded through bucketNext and headed in bucketHead, and bodies are
    /// pushed onto their bucket in descending id order, so walking a bucket from
    /// its head visits them in ascending id order: the iteration order rule,
    /// kept without a sort. Both arrays are scratch. They are rebuilt from
    /// entity state every tick and read by nothing after the pass, which is why
    /// neither is hashed; a push itself lands in Position and Target, which are.
    ///
    /// The last word on where a pushed body may stand is WalkableStep, the same
    /// test the walk answers to. A push that would cross a cliff, enter water,
    /// rock or debris, or step into a cell a building or a node stands on is
    /// tried along one axis at a time instead, and dropped if neither axis is
    /// open. That is the whole of the local avoidance: a body pinned against a
    /// cliff slides along it rather than through it.
    /// </summary>
    public sealed partial class World
    {
        // The eight compass directions two bodies on one point part along,
        // chosen by (i + j) & 7 so a stack of bodies spawned on one spot spreads
        // in a ring rather than a line. Normalized at type load: the integer
        // square root is exact and platform independent.
        private static readonly FixVec2[] partingDirections =
        {
            new FixVec2(Fix.OneF, Fix.Zero).Normalized(),
            new FixVec2(Fix.OneF, Fix.OneF).Normalized(),
            new FixVec2(Fix.Zero, Fix.OneF).Normalized(),
            new FixVec2(-Fix.OneF, Fix.OneF).Normalized(),
            new FixVec2(-Fix.OneF, Fix.Zero).Normalized(),
            new FixVec2(-Fix.OneF, -Fix.OneF).Normalized(),
            new FixVec2(Fix.Zero, -Fix.OneF).Normalized(),
            new FixVec2(Fix.OneF, -Fix.OneF).Normalized(),
        };

        // Scratch, per world rather than static: Replay runs two Worlds in one
        // process, and a bucket grid shared between them would carry one world's
        // bodies into the other's pass.
        private readonly int[] bucketHead = NewBucketHeads();
        private int[] bucketNext = new int[0];
        private FixVec2[] pushes = new FixVec2[0];
        private bool[] settled = new bool[0];

        // Which cells a building or a resource node stands on this tick, as the
        // tick number they were last seen on. A push never carries a walker into
        // one of them; the pathfinder already routes round them, and a push that
        // undid that would leave a body inside a wall it can never walk out of.
        private readonly int[] occupiedStamp = new int[GridCells];

        private static int[] NewBucketHeads()
        {
            var heads = new int[GridCells];
            for (int c = 0; c < heads.Length; c++) heads[c] = -1;
            return heads;
        }

        /// <summary>The radius this body claims on the ground, from its roster row.</summary>
        public Fix Footprint(Entity e) =>
            e.Owner >= 0 && e.Owner < MaxPeers ? RosterStats(e).Footprint : Fix.Zero;

        /// <summary>
        /// A body separation moves: a walker on the ground. Air passes over
        /// everything, so nothing on the ground is in its way and it is in
        /// nobody's.
        /// </summary>
        private bool Separates(Entity e) =>
            e.Alive && (e.Kind == EntityKind.Unit || e.Kind == EntityKind.Worker) &&
            e.Speed.Raw != 0 && !Flies(e);

        /// <summary>
        /// Whether a body separation may move. Hold is "do not move for any
        /// reason" and a push is a reason; a growing body cannot move either.
        /// Both still push others: a held unit is a rock the rest flow round.
        /// </summary>
        private bool Pushable(Entity e) => e.Mode != OrderMode.Hold && !IsGrowing(e) && !Anchored(e);

        private void SeparationSystem()
        {
            int count = entities.Count;
            if (bucketNext.Length < count)
            {
                bucketNext = new int[count];
                pushes = new FixVec2[count];
                settled = new bool[count];
            }

            // Stamp the occupied cells and fill the buckets. Descending id, so
            // each bucket reads back ascending. A body is bucketed by the cell it
            // stands in; its footprint is under half a cell, so the eight cells
            // around that one hold everything it can overlap.
            int stamp = Tick + 1; // never 0, which is what a fresh array holds
            for (int i = count - 1; i >= 0; i--)
            {
                Entity e = entities[i];
                if (!e.Alive) continue;
                if (e.Kind == EntityKind.Building || e.Kind == EntityKind.ResourceNode)
                {
                    occupiedStamp[CellOf(e.Position)] = stamp;
                    continue;
                }
                if (!Separates(e)) continue;
                int cell = CellOf(e.Position);
                bucketNext[i] = bucketHead[cell];
                bucketHead[cell] = i;
            }

            // Pass one: every push, from the positions the walk left.
            for (int i = 0; i < count; i++)
            {
                Entity e = entities[i];
                pushes[i] = FixVec2.Zero;
                settled[i] = false;
                if (!Separates(e) || !Pushable(e)) continue;

                settled[i] = PathDone(i) && e.Position.Equals(e.Target);
                Fix mine = Footprint(e);
                int cx = CellOf(e.Position) % GridSize;
                int cy = CellOf(e.Position) / GridSize;
                FixVec2 push = FixVec2.Zero;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= GridSize) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= GridSize) continue;
                        for (int j = bucketHead[y * GridSize + x]; j >= 0; j = bucketNext[j])
                        {
                            if (j == i) continue;
                            Entity other = entities[j];
                            Fix reach = mine + Footprint(other);
                            FixVec2 delta = e.Position - other.Position;
                            // Squared first. Most of a crowd is in reach of the
                            // bucket walk and out of reach of the footprint, and
                            // the square root is the expensive part of the pass.
                            Fix squared = delta.SqrMagnitude;
                            if (squared >= reach * reach) continue;
                            Fix distance = Fix.Sqrt(squared);
                            FixVec2 away;
                            if (distance.Raw == 0)
                            {
                                away = partingDirections[(i + j) & 7];
                                if (i > j) away = away * -Fix.OneF;
                            }
                            else
                            {
                                // One root for both: the test above and the
                                // direction share it, where Normalized would take
                                // a second.
                                away = new FixVec2(delta.X / distance, delta.Y / distance);
                            }
                            push += away * ((reach - distance) / 2);
                        }
                    }
                }

                // No further than half a step in a tick. A walker keeps walking
                // while it is being pushed, and a push that outran the walk would
                // be a body carried off by its neighbours rather than nudged.
                Fix limit = e.Speed / 2;
                if (push.SqrMagnitude > limit * limit) push = push.Normalized() * limit;
                pushes[i] = push;
            }

            // Pass two: apply, in id order, each against the same ground test the
            // walk answered to. The bucket heads are put back as they were found
            // on the way, from the cell each body was bucketed in and before it
            // moves: only the cells this pass used, so the next pass pays for its
            // bodies and not for the whole grid.
            for (int i = 0; i < count; i++)
            {
                Entity e = entities[i];
                if (!Separates(e)) continue;
                bucketHead[CellOf(e.Position)] = -1;

                FixVec2 push = pushes[i];
                if (push.X.Raw == 0 && push.Y.Raw == 0) continue;

                FixVec2 moved;
                if (!TryPush(e.Position, push, stamp, out moved) &&
                    !TryPush(e.Position, new FixVec2(push.X, Fix.Zero), stamp, out moved) &&
                    !TryPush(e.Position, new FixVec2(Fix.Zero, push.Y), stamp, out moved)) continue;

                e.Position = moved;
                // A body that was standing where it meant to stand now means to
                // stand here. Without this it would walk back next tick and be
                // pushed again, every tick, for as long as its neighbour stays.
                if (settled[i]) e.Target = moved;
                entities[i] = e;
            }
        }

        /// <summary>
        /// One candidate landing for a push: on the map, walkable from where the
        /// body stands by the cliff rule, and not inside a cell something is
        /// built on unless the body is already in that cell.
        /// </summary>
        private bool TryPush(FixVec2 from, FixVec2 push, int stamp, out FixVec2 to)
        {
            to = from + push;
            if (push.X.Raw == 0 && push.Y.Raw == 0) return false;
            if (to.X.Raw < 0 || to.Y.Raw < 0 ||
                to.X >= Fix.FromInt(GridSize) || to.Y >= Fix.FromInt(GridSize)) return false;
            int fromCell = CellOf(from), toCell = CellOf(to);
            if (toCell != fromCell && occupiedStamp[toCell] == stamp) return false;
            return WalkableStep(from, to, air: false);
        }
    }
}
