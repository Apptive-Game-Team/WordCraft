namespace WordCraft.Sim
{
    /// <summary>The six factions of docs/FACTIONS.md. Values are hashed as entity state, so never renumber.</summary>
    public enum Faction
    {
        TreeSpirits = 0,  // 세계수 정령
        Hellfire = 1,     // 지옥불 군단
        WaterSlimes = 2,  // 물 슬라임
        RockGolems = 3,   // 돌 골렘 부족
        Driftworlds = 4,  // 차원 유랑종
        Humans = 5,       // 인간 마법 문명
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
        Tech = 9,
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
    /// The roster: stats per role, identity per faction and role. A role holds a
    /// list of entries rather than one, because the six factions do not field the
    /// same units. Plain constants on purpose. A parsed file or a ScriptableObject
    /// would put content behind an importer that can round or reorder differently
    /// on two machines, and the simulation reads these numbers on every tick.
    /// </summary>
    public static class FactionData
    {
        /// <summary>
        /// Bump on any change to the tables below, or to the map MatchScenario
        /// paints. Two peers running different content produce different results
        /// from the same input, so this travels in the handshake and a mismatch is
        /// a rejection before tick 0. Terrain counts: a peer generating a different
        /// map has to be turned away at the handshake rather than desync on tick 1.
        /// </summary>
        public const uint ContentVersion = 14;

        public const int FactionCount = 6;
        public const int RoleCount = 10;

        /// <summary>
        /// Stats per faction and role, subscripted by <see cref="Index"/> exactly
        /// like names and sprites, so one index answers every roster question.
        ///
        /// Filled from <see cref="sharedStats"/> and <see cref="statOverrides"/>
        /// rather than written out sixty times. Sixty rows that are meant to stay
        /// identical are sixty chances to mistype one, and a mistyped row is not a
        /// compile error: it is a mirrored map that is still symmetric but no
        /// longer fair. Where a faction departs from the shared row should read as
        /// a short list, not as a difference found by diffing six blocks.
        ///
        /// A loop rather than a parsed file. The rule this file keeps is that
        /// content stays plain constants, because an importer can round or reorder
        /// differently on two machines. Copying an integer row six times can do
        /// neither.
        /// </summary>
        private static readonly UnitStats[] stats;

        /// <summary>
        /// The row every faction starts from, indexed by Role. Balance past
        /// "matches finish" is an explicit non-goal until 4-1, and identical
        /// numbers are what keeps the mirrored map fair rather than merely
        /// symmetric.
        /// </summary>
        private static readonly UnitStats[] sharedStats =
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
            /* Tech       */ new UnitStats { Hp = 250 },
        };

        /// <summary>
        /// Where a faction departs from the shared row. Empty today: the six
        /// factions still field identical numbers, and an entry arrives with the
        /// mechanic that needs it rather than ahead of it. 지옥불 군단장 is the
        /// first one due, per docs/FACTION-MECHANICS.md.
        ///
        /// A slot's second entry cannot differ from its first here, because this
        /// is keyed by role and a role holds a list. That waits on the Produce
        /// command carrying a slot, which is a hashed field and belongs with that
        /// change.
        /// </summary>
        private static readonly (Faction Faction, Role Role, UnitStats Stats)[] statOverrides =
        {
        };

        /// <summary>
        /// Fills the stats table. A static constructor rather than a field
        /// initializer, because an initializer runs in declaration order and this
        /// reads two tables declared above it. A constructor runs after all of
        /// them, so reordering a field cannot turn the table into a null at
        /// type-load time.
        /// </summary>
        static FactionData()
        {
            stats = new UnitStats[FactionCount * RoleCount];
            for (int faction = 0; faction < FactionCount; faction++)
            {
                for (int role = 0; role < RoleCount; role++)
                {
                    stats[Index((Faction)faction, (Role)role)] = sharedStats[role];
                }
            }

            for (int i = 0; i < statOverrides.Length; i++)
            {
                var (faction, role, replacement) = statOverrides[i];
                stats[Index(faction, role)] = replacement;
            }
        }

        /// <summary>
        /// Entry 0 of every slot. Faction major, role minor. An empty sprite is a
        /// slot docs/FACTIONS.md marks as "신규 필요": stats exist, art does not,
        /// and the view falls back to a primitive until it does. Entries past the
        /// first live in <see cref="extras"/>.
        /// </summary>
        private static readonly string[] names =
        {
            // 세계수 정령
            "", "생명의 나무", "풀씨 정령", "풀씨 둥지", "정령 뇌우목", "고목 수호자", "번개 정령", "바람 정령", "묘목", "뿌리 회당",
            // 지옥불 군단
            "", "균열 제단", "잿불 악마", "악마 산란장", "용암 아가리", "용암 갑각 악마", "지옥불 군단장의 자손", "지옥불 군단장", "갈라진 틈", "용암 도가니",
            // 물 슬라임
            "", "수맥 웅덩이", "물방울 생존자", "거품 생성기", "물기둥 분수", "거품 정령", "물결 궁수", "구름 용", "물웅덩이", "조수 제단",
            // 돌 골렘 부족
            "", "이끼바위 성소", "꼬마돌", "각성 바위", "굴림바위 언덕", "이끼바위 골렘", "바위 술사", "고대 골렘", "선돌", "고대 이끼돌",
            // 차원 유랑종
            "", "정박한 세계", "화산편", "통로", "굴절 기둥", "폭풍편", "틈새 사수", "경계 운반자", "계류 닻", "층위 관측대",
            // 인간 마법 문명. Melee is empty by design, not by omission: the faction
            // holds the line with 대포 and 공수 특공대. The 마법 탑 is production and supply both.
            "", "마나 샘", "수습 마법생", "마법 탑", "대포", "", "공수 특공대", "Towerback", "마법 탑", "공방",
        };

        private static readonly string[] sprites =
        {
            // 세계수 정령. Defense art cannot be ElectricTower: that is a human artifact.
            "", "LifeTree", "SeedSpiritSwarm", "SeedNest", "SpiritStormtree", "TreeGolem", "ThunderSpirit", "WindSpirit", "Sapling", "RootHall",
            // 지옥불 군단
            "", "RiftAltar", "EmberSpiritSwarm", "SpawningPit", "LavaMaw", "MagmaSpirit", "FireChildSpirit", "FireLordSpirit", "WideningFissure", "LavaCrucible",
            // 물 슬라임
            "", "SpringheadPool", "WaterSlimeSwarm", "BubbleGenerator", "GeyserColumn", "BubbleSpirit", "AquaArcher", "CloudDragon", "StillPool", "TideAltar",
            // 돌 골렘 부족. RockTurret is human-made stonework and stays out of this faction.
            // The signature slot is deliberately empty: RockRemnant is a death state, not a unit.
            "", "MossrockSanctum", "MiniRockSwarm", "WakingStone", "RollingHill", "RockGolem", "RockMage", "AncientGolem", "StandingStone", "ElderMossstone",
            // 차원 유랑종. Thinnest permanent roster; the extinct slimes it summons are extras.
            "", "AnchoredWorld", "FireTadpole", "Passage", "RefractingPillar", "LightningTadpole", "RiftMarksman", "DimensionToad", "MooringClaw", "StrataObservatory",
            // 인간 마법 문명. The only faction whose buildings all exist already; the
            // worker and the 공방 are the holes. Melee is empty by design.
            "", "ManaWell", "ApprenticeMage", "Tower", "Cannon", "", "ChickenCommando", "Towerback", "Tower", "Workshop",
        };

        /// <summary>
        /// Roster entries past the first, tagged with the slot they belong to and
        /// listed in faction then role order. The tables above hold entry 0 of
        /// every slot; a faction that fields two ranged units puts the second one
        /// here. Kept as one flat table rather than nesting the tables above,
        /// because renumbering those would move sixty rows to add eight.
        /// </summary>
        private static readonly (Faction Faction, Role Role, string Name, string Sprite)[] extras =
        {
            // 지옥불 군단. 균열 파수병 holds the ground; 군단장의 자손 is airborne and free.
            (Faction.Hellfire, Role.Ranged, "균열 파수병", "RiftWarden"),
            // 물 슬라임
            (Faction.WaterSlimes, Role.Ranged, "해일 인도자", "TideHerald"),
            // 차원 유랑종. Temporary summons the 층위 관측대 unlocks, not standing units.
            (Faction.Driftworlds, Role.Melee, "멸종한 화염 슬라임", "ExtinctFireSlime"),
            (Faction.Driftworlds, Role.Melee, "멸종한 바위 슬라임", "ExtinctRockSlime"),
            (Faction.Driftworlds, Role.Ranged, "멸종한 번개 슬라임", "ExtinctLightningSlime"),
            // 인간 마법 문명. Three defensive structures, the only faction with more
            // than one. 마도 정찰기 is the last slot in the roster still short of art.
            (Faction.Humans, Role.Defense, "전기 타워", "ElectricTower"),
            (Faction.Humans, Role.Defense, "돌 포탑", "RockTurret"),
            (Faction.Humans, Role.Signature, "마도 정찰기", ""),
        };

        /// <summary>
        /// Tech tier per roster entry, indexed by Role. A tier is a roster number
        /// like hp, not a case in the command handler: the day a faction opens its
        /// signature unit one tier earlier, this table is the only thing that moves.
        /// </summary>
        private static readonly int[] tiers =
        {
            /* None       */ 1,
            /* Base       */ 1,
            /* Worker     */ 1,
            /* Production */ 1,
            /* Defense    */ 1,
            /* Melee      */ 1,
            /* Ranged     */ 2,
            /* Signature  */ 3,
            /* Supply     */ 1,
            /* Tech       */ 2, // the production building comes first, or T3 is one purchase away
        };

        /// <summary>
        /// What a building costs to place, indexed by Role. Zero on a role that is
        /// not a building; nothing may be placed on a zero, and IsBuilding is what
        /// says so. Data rather than a case in the command handler, so retuning a
        /// build order is one number here.
        /// </summary>
        private static readonly int[] buildCosts =
        {
            /* None       */ 0,
            /* Base       */ 400,
            /* Worker     */ 0,
            /* Production */ 150,
            /* Defense    */ 100,
            /* Melee      */ 0,
            /* Ranged     */ 0,
            /* Signature  */ 0,
            /* Supply     */ 75,
            /* Tech       */ 200,
        };

        /// <summary>
        /// How long a building takes to stand up, in whole ticks at 20 Hz. Never
        /// seconds: a duration converted from wall-clock time is a duration two
        /// peers can round differently.
        /// </summary>
        private static readonly int[] buildTicks =
        {
            /* None       */ 0,
            /* Base       */ 200,
            /* Worker     */ 0,
            /* Production */ 100,
            /* Defense    */ 60,
            /* Melee      */ 0,
            /* Ranged     */ 0,
            /* Signature  */ 0,
            /* Supply     */ 50,
            /* Tech       */ 120,
        };

        /// <summary>
        /// The roster numbers for one faction's take on a role. Faction is
        /// required rather than defaulted: a caller that cannot name one is a
        /// caller reading somebody else's unit.
        /// </summary>
        public static UnitStats Stats(Faction faction, Role role) => stats[Index(faction, role)];

        /// <summary>What the owner's tier must reach before this role can be produced.</summary>
        public static int Tier(Role role) => tiers[(int)role];

        /// <summary>What placing this building costs. Zero on anything not a building.</summary>
        public static int BuildCost(Role role) => buildCosts[(int)role];

        /// <summary>How many whole ticks this building spends under construction.</summary>
        public static int BuildTicks(Role role) => buildTicks[(int)role];

        /// <summary>
        /// The roles a Build command may name. Listed here rather than derived from
        /// EntityKind, because a role's kind is only known once an entity exists and
        /// a Build is validated before one does.
        /// </summary>
        public static bool IsBuilding(Role role) =>
            role == Role.Base || role == Role.Production || role == Role.Defense ||
            role == Role.Supply || role == Role.Tech;

        /// <summary>
        /// True when this faction fields this slot at all. An empty name is a slot
        /// docs/FACTIONS.md leaves out on purpose, and a faction cannot build or
        /// produce what its roster does not list.
        /// </summary>
        public static bool Has(Faction faction, Role role) => names[Index(faction, role)].Length > 0;

        /// <summary>
        /// How many entries this slot holds. Always at least one, so a caller that
        /// wants the whole list loops 0 to this and a caller that wants the unit
        /// the view draws today asks for entry 0 and ignores it.
        /// </summary>
        // ponytail: entries past 0 are content nothing can produce. An entity
        // stores its role, so Produce names a role and always gets entry 0. Give
        // the command a slot argument the day a faction needs to build its second
        // ranged unit; that is a hashed field and belongs with that change.
        public static int SlotCount(Faction faction, Role role)
        {
            int count = 1;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i].Faction == faction && extras[i].Role == role) count++;
            }
            return count;
        }

        /// <summary>View-only metadata. Nothing in the simulation reads it, so it is not hashed.</summary>
        public static string Name(Faction faction, Role role) => Name(faction, role, 0);

        /// <summary>Sprite file name under Resources/Art/Sprites, or "" when the slot has no art yet.</summary>
        public static string Sprite(Faction faction, Role role) => Sprite(faction, role, 0);

        /// <summary>The name of one entry of a slot, where slot is 0 to SlotCount - 1.</summary>
        public static string Name(Faction faction, Role role, int slot) =>
            slot == 0 ? names[Index(faction, role)] : Extra(faction, role, slot).Name;

        /// <summary>The sprite of one entry of a slot, where slot is 0 to SlotCount - 1.</summary>
        public static string Sprite(Faction faction, Role role, int slot) =>
            slot == 0 ? sprites[Index(faction, role)] : Extra(faction, role, slot).Sprite;

        /// <summary>
        /// The slot-th extra entry, or an empty one past the end. Empty reads as
        /// "no art", which is what an entry that is not there has. A scan of a
        /// table this size costs less than the jagged arrays that would make it a
        /// subscript, and only entry 0 is on the view's per-entity path anyway.
        /// </summary>
        private static (Faction Faction, Role Role, string Name, string Sprite) Extra(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i].Faction == faction && extras[i].Role == role && --slot == 0) return extras[i];
            }
            return (faction, role, "", "");
        }

        private static int Index(Faction faction, Role role) => (int)faction * RoleCount + (int)role;
    }
}
