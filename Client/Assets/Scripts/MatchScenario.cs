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
            PaintHeights(world);

            for (int peer = 0; peer < Peers; peer++)
            {
                world.SpawnBuilding(peer, Role.Base, At(peer, 8, 8), complete: true);
                world.SpawnWorker(peer, At(peer, 10, 8));
                world.SpawnWorker(peer, At(peer, 8, 10));
                // The opening fighters are what the roster says this faction
                // fields (issue #154), not Melee 0 and Ranged 0 for everyone:
                // 인간's melee entry 0 is a blank row with no name and no art.
                // 세계수 정령 and 지옥불 still resolve to those two rows, so the
                // ids ScriptedLog names are the same bodies.
                (Role role, int slot) first = FactionData.OpeningFighter(world.FactionOf(peer), 0);
                (Role role, int slot) second = FactionData.OpeningFighter(world.FactionOf(peer), 1);
                world.SpawnUnit(peer, first.role, first.slot, At(peer, 11, 11));
                world.SpawnUnit(peer, second.role, second.slot, At(peer, 9, 12));
                world.SpawnResourceNode(At(peer, 14, 14), NodeAmount);
                world.GrantResources(peer, StartingResources);
            }

            // This pair maps onto itself under the same rotation, so both nodes sit
            // the same distance from both starts. That is the reason to fight over them.
            world.SpawnResourceNode(At(0, 24, 39), NodeAmount);
            world.SpawnResourceNode(At(0, 39, 24), NodeAmount);

            // Spawned last, so the ids above keep the numbers ScriptedLog names.
            SpawnNeutralRocks(world);
            return world;
        }

        /// <summary>
        /// 인간 마법 문명 징발: the four neutral 꼬마돌 the map holds, laid out on
        /// the same two-part structure the resource nodes use — one per half, plus
        /// one pair that maps onto itself.
        ///
        /// Placement is the mechanic. docs/FACTION-MECHANICS.md makes the map the
        /// second contested point of the match, and it can only be that if both
        /// halves of the sentence are true: the 인간 player has to walk out for
        /// these, and the opponent has to be able to reach them and delete them.
        /// A 꼬마돌 in the base fails the second half — 인간 would get its only
        /// anti-air for free and nobody could contest it — and one in the middle
        /// of the barrier fails the first, because two units sent out at minute
        /// one would settle 인간's whole air matchup before a decision exists.
        ///
        /// So: one on each player's outer flank, past the lake that already
        /// narrows it, roughly forty-six cells from that player's own base and
        /// nearer fifty-seven from the other one by the only route there is. It is
        /// yours, but only if you go and take it and then keep it, and the raid
        /// that denies it never has to touch your base.
        ///
        /// And one in the mouth of each lane, on the anti-diagonal itself, which
        /// is where the map's only two crossings already decide who holds what.
        /// They sit at the inner shoulder with the contested node at the outer,
        /// so one corridor now holds a reason for both players to be in it and
        /// 인간's reason costs sixty ticks of a worker standing still in it.
        /// </summary>
        private static void SpawnNeutralRocks(World world)
        {
            for (int peer = 0; peer < Peers; peer++) world.SpawnNeutralRock(At(peer, 54, 6));

            // Named as peer 0's twice for the same reason the node pair above is:
            // the second call is the first one rotated, so the two are each other's
            // mirror rather than one per half.
            world.SpawnNeutralRock(At(0, 37, 26));
            world.SpawnNeutralRock(At(0, 26, 37));
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

        // Elevation levels the map uses. A step of one level is a slope anyone
        // walks; two is a cliff only a flier crosses (Sim/World.cs StepPassable).
        // So high ground that is only reachable by its ramp has to stand two
        // levels above the floor, and the ramp is the level between.
        private const int Floor = 0;
        private const int RampLevel = 1;
        private const int HighGround = 2;

        // The barrier's rock is impassable whatever its height. Lifting it to the
        // top level is for the view, which builds the spine into a mountain ridge
        // from this number rather than from a second rule of its own.
        private const int RidgeLevel = World.MaxHeight;

        // Lane-mouth high ground, in the same spine and lane coordinates the
        // barrier is written in: spine is the distance from the anti-diagonal,
        // lane the distance from the main diagonal. One plateau on each side of
        // each lane, on its outer shoulder, from the barrier's face out to
        // ShoulderFar. Its ramp is on the face away from the barrier, so the
        // player whose side it is climbs it from home, and a force coming out of
        // the lane has to walk round the cliff to reach the top.
        private const int ShoulderNear = 2;   // touches the barrier's rock at spine 1
        private const int ShoulderFar = 8;
        private const int ShoulderLaneNear = 19;
        private const int ShoulderLaneFar = 24;
        private const int ShoulderRampNear = 9;
        private const int ShoulderRampFar = 10;
        private const int ShoulderRampLaneNear = 20;
        private const int ShoulderRampLaneFar = 23;

        /// <summary>
        /// The map's elevation, painted right after its terrain and under the same
        /// two rules: no World.Random draws, and symmetric by construction. The
        /// barrier and the lane-mouth high ground read only |spine| and |lane|,
        /// which the rotation leaves alone; the flank mesa is a rectangle plus its
        /// rotation, the way the lake is, because it is a place and not a rule.
        ///
        /// What it adds to the match is three heights of ground:
        ///
        /// - The barrier's rock is the top level. Impassable already; the level
        ///   is what the view draws.
        /// - Beside every lane mouth, on the lane's outer shoulder, a plateau two
        ///   levels up with one ramp on the side facing home. Whoever stands on it
        ///   looks straight down the lane exit, and the only way to take it off
        ///   them is the ramp, so the ramp is the cell worth holding.
        /// - On each player's outer flank, beside that player's far 꼬마돌, a mesa
        ///   two levels up whose only way in is a ramp facing the 꼬마돌.
        ///
        /// Lakes stay at the floor, and so does everything else, including every
        /// start position: a base on a slope would cost its own workers a climb on
        /// every trip, which is a handicap one map cell decides.
        /// </summary>
        private static void PaintHeights(World world)
        {
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++)
                {
                    int spine = Abs(x + y - (MapSize - 1));
                    int lane = Abs(x - y);
                    int level = Floor;

                    if (spine <= 1 && (lane < LaneNear || lane > LaneFar)) level = RidgeLevel;
                    else if (InLake(x, y)) level = Floor;
                    else if (Within(spine, ShoulderNear, ShoulderFar) && Within(lane, ShoulderLaneNear, ShoulderLaneFar))
                        level = HighGround;
                    else if (Within(spine, ShoulderRampNear, ShoulderRampFar) &&
                             Within(lane, ShoulderRampLaneNear, ShoulderRampLaneFar))
                        level = RampLevel;
                    else level = Mesa(x, y);

                    world.SetHeight(x, y, level);
                }
            }
        }

        /// <summary>
        /// The flank mesa and its rotation: x 45..50, y 7..11 two levels up, and a
        /// two-cell ramp at x 51 on its east face, three cells from the far
        /// 꼬마돌 at (54, 6). The top overlooks the cell a capturing worker has to
        /// stand still on, and the only way up faces that worker. Every cell of
        /// both stays inside its own half: x + y is at most 61, and the barrier's
        /// rock starts at 62.
        /// </summary>
        private static int Mesa(int x, int y) =>
            Max(MesaRect(x, y), MesaRect(MapSize - 1 - x, MapSize - 1 - y));

        private static int MesaRect(int x, int y)
        {
            if (x >= 45 && x <= 50 && y >= 7 && y <= 11) return HighGround;
            if (x == 51 && y >= 8 && y <= 9) return RampLevel;
            return Floor;
        }

        private static bool Within(int v, int low, int high) => v >= low && v <= high;

        private static int Max(int a, int b) => a > b ? a : b;

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
