namespace WordCraft.Sim
{
    /// <summary>
    /// 물 지대, docs/TECH-TREE.md 물 슬라임 물 지대 규칙 and 묶음 7. One number
    /// per cell is state: the tick that cell's field lapses on. Nothing about a
    /// field blocks a step; what it does is read off the grid at the moment a
    /// system asks — armor when a body is hit, speed when it walks, reach when a
    /// 물기둥 분수 measures a shot.
    ///
    /// The grid carries no owner. Every 물 슬라임 body on a field cell takes it,
    /// and in a mirror match so does the opponent's: an owner per cell would be a
    /// second hashed array that decides nothing the faction check does not.
    /// </summary>
    public sealed partial class World
    {
        // The tick each cell's field lapses on, or 0 for a cell that never had
        // one. Hashed, sparse, the way remnantExpiry is: a lapsed value is left
        // in place and reads as no field, because HasWaterField compares it
        // against the tick rather than against zero.
        private readonly int[] waterFieldExpiry = new int[GridCells];

        /// <summary>The tick this cell's field lapses on, or 0 when it never had one.</summary>
        public int WaterFieldExpiry(int cell) => waterFieldExpiry[cell];

        /// <summary>True while a field still lies on this cell.</summary>
        public bool HasWaterField(int cell) => waterFieldExpiry[cell] > Tick;

        /// <summary>
        /// Lays a field on the square of cells within half cells of this one in
        /// x and in y (3×3 for 1, 5×5 for 2), lapsing on expiry. The one write
        /// into the grid: off-map cells are skipped, only open terrain takes a
        /// field, and a cell keeps the later of the expiry it has and this one.
        /// docs/TECH-TREE.md names four writers; 샘물 운반자 and 솟는 샘 call this
        /// from AbilitySystem, and 물 지대 깔기 and 해일 부름 come with Cast (묶음 8).
        /// </summary>
        private void WriteWaterField(int center, int half, int expiry)
        {
            int cx = center % GridSize, cy = center / GridSize;
            for (int y = cy - half; y <= cy + half; y++)
            {
                if (y < 0 || y >= GridSize) continue;
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || x >= GridSize) continue;
                    int cell = y * GridSize + x;
                    if (terrain[cell] != (byte)TileKind.Open) continue;
                    if (waterFieldExpiry[cell] < expiry) waterFieldExpiry[cell] = expiry;
                }
            }
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness lay
        /// a field without the body that lays it, and put two worlds one cell's
        /// expiry apart.
        /// </summary>
        public void SetWaterFieldForScenario(int cell, int expiry) => waterFieldExpiry[cell] = expiry;
    }
}
