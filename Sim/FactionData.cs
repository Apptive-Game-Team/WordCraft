namespace WordCraft.Sim
{
    /// <summary>The five factions of docs/FACTIONS.md. Values are hashed as entity state, so never renumber.</summary>
    public enum Faction
    {
        TreeSpirits = 0,  // 세계수 정령
        Hellfire = 1,     // 지옥불 군단
        WaterSlimes = 2,  // 물 슬라임
        RockGolems = 3,   // 돌 골렘 부족
        Driftworlds = 4,  // 차원 유랑종
    }

    /// <summary>
    /// The role structure every faction shares. None is 0 so a default Entity is
    /// roleless rather than a base. Values are hashed; never renumber them.
    /// </summary>
    public enum Role
    {
        None = 0,
        Base = 1,
        Worker = 2,
        Production = 3,
        Defense = 4,
        Melee = 5,
        Ranged = 6,
        Signature = 7,
        Supply = 8,
    }

    public struct UnitStats
    {
        public int Hp;
        public Fix Speed;
        public int Damage;
        public Fix Range;
        public int AttackTicks;
    }

    /// <summary>
    /// The roster: stats per role, identity per faction and role. Plain constants
    /// on purpose. A parsed file or a ScriptableObject would put content behind an
    /// importer that can round or reorder differently on two machines, and the
    /// simulation reads these numbers on every tick.
    /// </summary>
    public static class FactionData
    {
        /// <summary>
        /// Bump on any change to the tables below. Two peers running different
        /// content produce different results from the same input, so this travels
        /// in the handshake and a mismatch is a rejection before tick 0.
        /// </summary>
        public const uint ContentVersion = 5;

        public const int FactionCount = 5;
        public const int RoleCount = 9;

        /// <summary>
        /// Indexed by Role. One block per role, shared by every faction: balance
        /// past "matches finish" is an explicit non-goal, and identical numbers are
        /// what keeps the mirrored map fair rather than merely symmetric.
        /// </summary>
        // ponytail: faction choice is cosmetic while every faction reads the same
        // row. Widen this to [faction, role] the day balance stops being a non-goal;
        // the mirrored map stops being fair on the same day.
        private static readonly UnitStats[] stats =
        {
            /* None       */ new UnitStats { Hp = 1 },
            /* Base       */ new UnitStats { Hp = 400 },
            /* Worker     */ new UnitStats { Hp = 60, Speed = Fix.Ratio(1, 4) },
            /* Production */ new UnitStats { Hp = 400 },
            /* Defense    */ new UnitStats { Hp = 300, Damage = 9, Range = Fix.FromInt(6), AttackTicks = 20 },
            /* Melee      */ new UnitStats { Hp = 100, Speed = Fix.Ratio(1, 4), Damage = 7, Range = Fix.FromInt(2), AttackTicks = 15 },
            /* Ranged     */ new UnitStats { Hp = 70, Speed = Fix.Ratio(1, 4), Damage = 6, Range = Fix.FromInt(6), AttackTicks = 18 },
            /* Signature  */ new UnitStats { Hp = 130, Speed = Fix.Ratio(3, 8), Damage = 10, Range = Fix.FromInt(3), AttackTicks = 22 },
            /* Supply     */ new UnitStats { Hp = 200 },
        };

        /// <summary>
        /// Faction major, role minor. An empty sprite is a slot docs/FACTIONS.md
        /// marks as "신규 필요": stats exist, art does not, and the view falls back
        /// to a primitive until it does.
        /// </summary>
        private static readonly string[] names =
        {
            // 세계수 정령
            "", "생명의 나무", "풀씨 정령", "풀씨 둥지", "정령 뇌우목", "고목 수호자", "번개 정령", "바람 정령", "묘목",
            // 지옥불 군단
            "", "균열 제단", "잿불 악마", "악마 산란장", "용암 아가리", "용암 갑각 악마", "지옥불 군단장의 자손", "지옥불 군단장", "갈라진 틈",
            // 물 슬라임
            "", "수맥 웅덩이", "물방울 생존자", "거품 생성기", "물기둥 분수", "거품 정령", "물결 궁수", "구름 용", "물웅덩이",
            // 돌 골렘 부족
            "", "이끼바위 성소", "꼬마돌", "각성 바위", "굴림바위 언덕", "이끼바위 골렘", "바위 술사", "", "선돌",
            // 차원 유랑종
            "", "정박한 세계", "화산편", "통로", "굴절 기둥", "폭풍편", "", "경계 운반자", "계류 닻",
        };

        private static readonly string[] sprites =
        {
            // 세계수 정령. Defense art cannot be ElectricTower: that is a human artifact.
            "", "LifeTree", "SeedSpiritSwarm", "SeedNest", "SpiritStormtree", "TreeGolem", "ThunderSpirit", "WindSpirit", "Sapling",
            // 지옥불 군단
            "", "RiftAltar", "EmberSpiritSwarm", "SpawningPit", "LavaMaw", "MagmaSpirit", "FireChildSpirit", "FireLordSpirit", "WideningFissure",
            // 물 슬라임
            "", "SpringheadPool", "WaterSlimeSwarm", "BubbleGenerator", "GeyserColumn", "BubbleSpirit", "AquaArcher", "CloudDragon", "StillPool",
            // 돌 골렘 부족. RockTurret is human-made stonework and stays out of this faction.
            // The signature slot is deliberately empty: RockRemnant is a death state, not a unit.
            "", "MossrockSanctum", "MiniRockSwarm", "WakingStone", "RollingHill", "RockGolem", "RockMage", "", "StandingStone",
            // 차원 유랑종. Thinnest roster; the ranged slot has neither art nor concept.
            "", "AnchoredWorld", "FireTadpole", "Passage", "RefractingPillar", "LightningTadpole", "", "DimensionToad", "MooringClaw",
        };

        public static UnitStats Stats(Role role) => stats[(int)role];

        /// <summary>View-only metadata. Nothing in the simulation reads it, so it is not hashed.</summary>
        public static string Name(Faction faction, Role role) => names[Index(faction, role)];

        /// <summary>Sprite file name under Resources/Art/Sprites, or "" when the slot has no art yet.</summary>
        public static string Sprite(Faction faction, Role role) => sprites[Index(faction, role)];

        private static int Index(Faction faction, Role role) => (int)faction * RoleCount + (int)role;
    }
}
