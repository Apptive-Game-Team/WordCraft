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
        private const int StartingResources = 200;

        public static World Build(ulong seed)
        {
            var world = new World(seed);

            for (int peer = 0; peer < Peers; peer++)
            {
                int bx = peer == 0 ? 8 : 52;
                int by = peer == 0 ? 8 : 52;
                int away = peer == 0 ? 1 : -1;

                world.SpawnBuilding(peer, At(bx, by), complete: true);
                world.SpawnWorker(peer, At(bx + 2 * away, by));
                world.SpawnWorker(peer, At(bx, by + 2 * away));
                world.SpawnUnit(peer, At(bx + 2 * away, by + 2 * away), World.UnitSpeed, World.UnitHp);
                world.SpawnResourceNode(At(bx + 6 * away, by + 6 * away), NodeAmount);
                world.GrantResources(peer, StartingResources);
            }

            // Contested nodes in the middle, so the map has a reason to fight over.
            world.SpawnResourceNode(At(28, 34), NodeAmount);
            world.SpawnResourceNode(At(34, 28), NodeAmount);
            return world;
        }

        /// <summary>Cell centre, so a spawned entity never straddles two cells.</summary>
        private static FixVec2 At(int x, int y)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(x) + half, Fix.FromInt(y) + half);
        }
    }
}
