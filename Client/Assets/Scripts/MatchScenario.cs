using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The starting world. Both peers build it from the seed alone, in this exact
    /// order, because entity ids are handed out in spawn order and every command
    /// names an entity by id. Reordering a single spawn renumbers everything and
    /// desyncs the match before the first tick runs.
    /// </summary>
    public static class MatchScenario
    {
        public const int Peers = 2;
        public const int MapSize = World.GridSize;

        private const int NodeAmount = 500;

        // Enough for the opening build order. Two workers bank about 20 mana every
        // four seconds, so a start that could not pay for a production building
        // would be several minutes of watching before the first decision.
        private const int StartingResources = 500;

        /// <summary>
        /// Both peers must pass the same two factions or their worlds hash apart
        /// from tick 0. In a networked match the handshake is what makes that
        /// true: LockstepSession writes both slots again once each peer's own
        /// choice has crossed the wire, so what is passed here is what the local
        /// player sees until then.
        /// </summary>
        public static World Build(ulong seed, Faction peer0, Faction peer1)
        {
            var world = new World(seed);
            world.SetPeerFaction(0, peer0);
            world.SetPeerFaction(1, peer1);
            // Before anything is spawned, so a start position that ever lands on
            // rock is a placement the harness catches rather than a unit that
            // cannot take its first step.
            PaintTerrain(world);

            for (int peer = 0; peer < Peers; peer++)
            {
                world.SpawnBuilding(peer, Role.Base, At(peer, 8, 8), complete: true);
                world.SpawnWorker(peer, At(peer, 10, 8));
                world.SpawnWorker(peer, At(peer, 8, 10));
                world.SpawnUnit(peer, Role.Melee, At(peer, 11, 11));
                world.SpawnUnit(peer, Role.Ranged, At(peer, 9, 12));
                world.SpawnResourceNode(At(peer, 14, 14), NodeAmount);
                world.GrantResources(peer, StartingResources);
            }

            // This pair maps onto itself under the same rotation, so both nodes sit
            // the same distance from both starts. That is the reason to fight over them.
            world.SpawnResourceNode(At(0, 24, 39), NodeAmount);
            world.SpawnResourceNode(At(0, 39, 24), NodeAmount);
            return world;
        }

        // The barrier is three cells thick along the anti-diagonal, which is the
        // one line the 180 degree rotation maps onto itself. Two lanes cut through
        // it, one either side of the centre, and each one opens onto a contested
        // resource node: the reason to take a lane is standing in its mouth.
        private const int LaneNear = 10;
        private const int LaneFar = 17;

        /// <summary>
        /// The map's terrain, painted before the first entity exists.
        ///
        /// Every rule below is written in sum and difference coordinates, because
        /// the rotation that mirrors this map sends (x + y) to 2*(MapSize-1) - (x + y)
        /// and (x - y) to -(x - y). A rule that only reads |x + y - (MapSize-1)| and
        /// |x - y| is therefore symmetric by construction rather than by a mirror
        /// pass someone has to remember to run. The harness asserts it anyway.
        ///
        /// No World.Random draws: the layout is a function of the grid alone, so
        /// two peers cannot generate different maps from the same content version,
        /// and the draw count the hash carries is untouched.
        /// </summary>
        private static void PaintTerrain(World world)
        {
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++)
                {
                    int spine = Abs(x + y - (MapSize - 1));
                    int lane = Abs(x - y);
                    if (spine <= 1 && (lane < LaneNear || lane > LaneFar))
                    {
                        world.SetTerrain(x, y, TileKind.Rock);
                    }
                    else if (InLake(x, y))
                    {
                        world.SetTerrain(x, y, TileKind.Water);
                    }
                }
            }
        }

        /// <summary>
        /// One lake per half, so each player's own flank is the one narrowed. Stated
        /// as a rectangle plus its rotation rather than a formula, because a lake is
        /// a place and not a rule.
        /// </summary>
        private static bool InLake(int x, int y) =>
            InLakeRect(x, y) || InLakeRect(MapSize - 1 - x, MapSize - 1 - y);

        private static bool InLakeRect(int x, int y) => x >= 16 && x <= 23 && y >= 4 && y <= 9;

        private static int Abs(int v) => v < 0 ? -v : v;

        /// <summary>
        /// Cell centre for peer 0, rotated 180 degrees about the grid centre for
        /// peer 1. A rotation maps the grid onto itself exactly, so the two halves
        /// are congruent and no start is nearer to anything than the other one.
        /// Cell centre, so a spawned entity never straddles two cells.
        /// </summary>
        private static FixVec2 At(int peer, int x, int y)
        {
            if (peer != 0)
            {
                x = MapSize - 1 - x;
                y = MapSize - 1 - y;
            }
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }
    }
}
