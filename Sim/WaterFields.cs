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
        /// <summary>What a 물 슬라임 body on a field cell adds to its armor against a normal attack.</summary>
        public const int WaterFieldArmor = 1;

        /// <summary>The same, once 물 지대 방어 is researched.</summary>
        public const int WaterFieldArmorResearched = 2;

        /// <summary>급류: how fast a 급류 슬라임 walks from a field cell, cells per tick.</summary>
        public static readonly Fix RapidsSpeed = Fix.Ratio(1, 2);

        // The tick each cell's field lapses on, or 0 for a cell that never had
        // one. Hashed, sparse, the way remnantExpiry is: a lapsed value is left
        // in place and reads as no field, because HasWaterField compares it
        // against the tick rather than against zero.
        private readonly int[] waterFieldExpiry = new int[GridCells];

        /// <summary>The tick this cell's field lapses on, or 0 when it never had one.</summary>
        public int WaterFieldExpiry(int cell) => waterFieldExpiry[cell];

        /// <summary>True while a field still lies on this cell.</summary>
        public bool HasWaterField(int cell) => waterFieldExpiry[cell] > Tick;

        /// <summary>Whether the cell this body stands on carries a field right now.</summary>
        private bool OnWaterField(Entity e) => HasWaterField(CellOf(e.Position));

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
        /// 이동식 물 지대 and 고정 물 지대, the last step of AbilitySystem but
        /// 잠듦의 고리: on a tick the row's period divides, every holder by
        /// ascending id lays a field on the square its row's radius spans around
        /// the cell it stands on, lapsing the row's ticks from now. A 솟는 샘 lays
        /// nothing until it is finished, and the cells it laid last lapse on
        /// their own once it falls.
        /// </summary>
        private void EmitWaterFields()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                int row = ActiveAbility(e, AbilityKind.FieldEmitter);
                if (row < 0) continue;
                AbilityRow r = FactionData.AbilityRowAt(row);
                if (Tick % r.Period != 0) continue;
                WriteWaterField(CellOf(e.Position), AbilityRadius(e.Owner, r), Tick + AbilityTicks(e.Owner, r));
            }
        }

        /// <summary>물 지대 방어: what a field adds to this peer's bodies.</summary>
        public int WaterFieldArmorFor(int peer) =>
            UpgradeLevelOf(peer, UpgradeEffect.WaterFieldArmor) > 0 ? WaterFieldArmorResearched : WaterFieldArmor;

        /// <summary>
        /// The armor a field gives this body against a normal attack: a 물 슬라임
        /// unit or worker standing on a field cell at the moment it is hit.
        /// Fixed damage never asks: bursts, burns and fixed ability damage hit
        /// through it, and so do a chain hop and a splash, which are fixed off
        /// the shot before them.
        /// </summary>
        private int FieldArmor(Entity t)
        {
            if (t.Owner < 0 || t.Owner >= MaxPeers) return 0;
            if (factions[t.Owner] != Faction.WaterSlimes) return 0;
            if (t.Kind != EntityKind.Unit && t.Kind != EntityKind.Worker) return 0;
            return OnWaterField(t) ? WaterFieldArmorFor(t.Owner) : 0;
        }

        /// <summary>급류: RapidsSpeed for a 급류 슬라임 whose step starts on a field cell, or this speed.</summary>
        private Fix RapidsSpeedFor(Entity e, Fix speed) =>
            ActiveAbility(e, AbilityKind.FieldSpeed) >= 0 && OnWaterField(e) ? RapidsSpeed : speed;

        /// <summary>물기둥 분수: the cells a field under this body adds to its reach.</summary>
        private Fix FieldRangeBonus(Entity e)
        {
            int row = ActiveAbility(e, AbilityKind.FieldRange);
            if (row < 0 || !OnWaterField(e)) return Fix.Zero;
            return Fix.FromInt(AbilityAmount(e.Owner, FactionData.AbilityRowAt(row)));
        }

        /// <summary>
        /// Scenario setup only. Never call this from a system. Lets a harness lay
        /// a field without the body that lays it, and put two worlds one cell's
        /// expiry apart.
        /// </summary>
        public void SetWaterFieldForScenario(int cell, int expiry) => waterFieldExpiry[cell] = expiry;
    }
}
