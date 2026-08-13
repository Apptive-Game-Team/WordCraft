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

        /// <summary>
        /// Flies. Terrain and debris do not stop it, and a weapon that cannot
        /// reach air cannot touch it. Roster data rather than entity state: it
        /// never changes for a given faction and role, so nothing hashes it and
        /// two peers cannot disagree about it.
        /// </summary>
        public bool Air;

        /// <summary>
        /// Can shoot something that flies. Default false on the struct, so every
        /// row that means "yes" says so; see sharedStats for why melee is the one
        /// row that leaves it off.
        /// </summary>
        public bool HitsAir;

        /// <summary>
        /// Whether this row carries a weapon at all. 지옥불 군단장 is 비전투 per
        /// docs/FACTION-MECHANICS.md and its row is the only one that answers no.
        ///
        /// Damage is the test rather than a flag of its own, because a flag would
        /// be a second place to say what the row already says, and the two are
        /// free to disagree: a row left at Damage = 0 with the flag unset is a
        /// unit that acquires a target, walks to it, and fires nothing, which is
        /// the exact defect this rule answers.
        ///
        /// Not AttackTicks either. Zero there is a rate — "fires every tick" — so
        /// reading it as disarmed would silently take the weapon off the fastest
        /// one anybody ever writes. Damage is what a weapon does to what it hits,
        /// and none of it is no weapon.
        /// </summary>
        public bool HasWeapon => Damage > 0;
    }

    /// <summary>
    /// What producing one entry of a slot costs its owner. Resources and ticks
    /// together rather than in two tables, because the two numbers are decided as
    /// a pair and a role priced in one place and timed in another is a role
    /// somebody retunes half of.
    /// </summary>
    public struct ProductionCost
    {
        /// <summary>Taken in full at queue time, refunded in full on cancel.</summary>
        public int Resources;

        /// <summary>Whole ticks the building spends on it. Never seconds.</summary>
        public int Ticks;

        /// <summary>
        /// Whether a Produce command may name this slot at all. Default false on
        /// the struct, so a row nobody wrote fails closed: a slot left out of the
        /// table cannot be produced rather than being produced for nothing.
        /// </summary>
        public bool Produced;
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
        public const uint ContentVersion = 18;

        public const int FactionCount = 6;
        public const int RoleCount = 10;

        /// <summary>
        /// The price and the clock every produced role starts from. Baseline, not
        /// a rule: what a given faction's unit actually costs comes out of
        /// <see cref="Production"/>, and 지옥불 군단장 already departs from it.
        /// </summary>
        public const int DefaultProduceCost = 20;
        public const int DefaultProduceTicks = 40;

        /// <summary>
        /// Stats for entry 0 of every slot, subscripted by <see cref="Index"/>
        /// exactly like names and sprites, so one index answers every roster
        /// question. Entries past the first live in <see cref="extraStats"/>.
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
        /// Stats for the entries past the first, parallel to <see cref="extras"/>
        /// and filled the same way <see cref="stats"/> is. Kept beside the extras
        /// table rather than widening <see cref="stats"/> to a fixed number of
        /// entries per slot, because a fixed number is a number somebody has to
        /// guess: sixty slots hold one entry each and eight hold more, so a table
        /// sized for the widest slot would be mostly rows nothing ever wrote, and
        /// a row nothing wrote reads as zero hp rather than as an error.
        /// </summary>
        private static readonly UnitStats[] extraStats;

        /// <summary>
        /// The row every faction starts from, indexed by Role. Balance past
        /// "matches finish" is an explicit non-goal until 4-1, and identical
        /// numbers are what keeps the mirrored map fair rather than merely
        /// symmetric.
        ///
        /// HitsAir is set on everything that shoots except melee, per
        /// docs/FACTION-MECHANICS.md 공중: a ranged weapon reaches air unless its
        /// row says otherwise, and reach is the one thing melee does not have.
        /// The exceptions the document names on top of that — 대포 ground only,
        /// Towerback air only — are the human faction's, and they arrive with it.
        /// </summary>
        private static readonly UnitStats[] sharedStats =
        {
            /* None       */ new UnitStats { Hp = 1 },
            /* Base       */ new UnitStats { Hp = 400 },
            /* Worker     */ new UnitStats { Hp = 60, Speed = Fix.Ratio(1, 4) },
            /* Production */ new UnitStats { Hp = 400 },
            /* Defense    */ new UnitStats { Hp = 300, Damage = 9, Range = Fix.FromInt(6), AttackTicks = 20, HitsAir = true },
            /* Melee      */ new UnitStats { Hp = 100, Speed = Fix.Ratio(1, 4), Damage = 7, Range = Fix.FromInt(2), AttackTicks = 15 },
            /* Ranged     */ new UnitStats { Hp = 70, Speed = Fix.Ratio(1, 4), Damage = 6, Range = Fix.FromInt(6), AttackTicks = 18, HitsAir = true },
            /* Signature  */ new UnitStats { Hp = 130, Speed = Fix.Ratio(3, 8), Damage = 10, Range = Fix.FromInt(3), AttackTicks = 22, HitsAir = true },
            /* Supply     */ new UnitStats { Hp = 200 },
            /* Tech       */ new UnitStats { Hp = 250 },
        };

        /// <summary>
        /// Where a faction departs from the shared row, per entry rather than per
        /// role. Nearly empty today: the six factions still field identical
        /// numbers, and an entry arrives with the mechanic that needs it rather
        /// than ahead of it.
        ///
        /// The key is Faction, Role and Slot together, and the slot is what makes
        /// the table able to say what it means. 지옥불's ranged list holds two
        /// units that are nothing alike — 자손 flies and is free, 균열 파수병 walks
        /// and is bought — and while this was keyed by role alone the second one
        /// could only ever inherit the first one's row. A slot that names no entry
        /// is an index out of range at type load, which is loud, immediate, and
        /// identical on every runtime; silently landing on entry 0 is none of those.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, UnitStats Stats)[] statOverrides =
        {
            // 지옥불 군단장. Flies, and carries no weapon at all: it kills nothing
            // itself and pays for itself by what it spawns. Numbers from
            // docs/FACTION-MECHANICS.md. Keeps the shared signature speed, because
            // a unit the player has to keep alive is a unit the player has to be
            // able to pull out.
            (Faction.Hellfire, Role.Signature, 0,
                new UnitStats { Hp = 300, Speed = Fix.Ratio(3, 8), Air = true }),

            // 지옥불 군단장의 자손. Airborne, shorter reach and lighter hit than the
            // shared ranged row. Free and spawned rather than produced, which is
            // what productionOverrides below says and World.WarlordSpawnSystem does.
            // Entry 0 only: 균열 파수병 is entry 1 and holds the ground on the
            // shared ranged row, which is the whole reason this key names a slot.
            (Faction.Hellfire, Role.Ranged, 0,
                new UnitStats
                {
                    Hp = 70,
                    Speed = Fix.Ratio(1, 4),
                    Damage = 5,
                    Range = Fix.FromInt(4),
                    AttackTicks = 18,
                    Air = true,
                    HitsAir = true,
                }),
        };

        /// <summary>
        /// What producing entry 0 of a slot costs, per faction and role,
        /// subscripted by <see cref="Index"/> like everything else. Filled from
        /// <see cref="sharedProduction"/> and <see cref="productionOverrides"/>
        /// for the same reason the stats table is.
        /// </summary>
        private static readonly ProductionCost[] production;

        /// <summary>
        /// What producing the entries past the first costs, parallel to
        /// <see cref="extras"/> exactly as <see cref="extraStats"/> is.
        /// </summary>
        private static readonly ProductionCost[] extraProduction;

        /// <summary>
        /// The production row every faction starts from, indexed by Role.
        ///
        /// Buildings are marked not produced: a building is placed by a Build
        /// command and paid for out of <see cref="buildCosts"/>, and a queue that
        /// accepted one would hand the peer a base walking around as a unit.
        /// </summary>
        private static readonly ProductionCost[] sharedProduction =
        {
            /* None       */ new ProductionCost(),
            /* Base       */ new ProductionCost(),
            /* Worker     */ Produce(DefaultProduceCost, DefaultProduceTicks),
            /* Production */ new ProductionCost(),
            /* Defense    */ new ProductionCost(),
            /* Melee      */ Produce(DefaultProduceCost, DefaultProduceTicks),
            /* Ranged     */ Produce(DefaultProduceCost, DefaultProduceTicks),
            /* Signature  */ Produce(DefaultProduceCost, DefaultProduceTicks),
            /* Supply     */ new ProductionCost(),
            /* Tech       */ new ProductionCost(),
        };

        /// <summary>
        /// Where a faction departs from the shared production row, keyed by entry
        /// rather than by role for the same reason <see cref="statOverrides"/> is.
        /// Balance past "matches finish" is still a non-goal, so an entry arrives
        /// with the mechanic that needs it: 지옥불 군단장 is the whole list today.
        ///
        /// Narrowing this key is what puts 균열 파수병 on the production list. The
        /// row below closes 지옥불's ranged slot because 자손 is spawned rather
        /// than bought; keyed by role it closed the second entry too, and a unit
        /// nothing can produce is a unit that is not in the game.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, ProductionCost Cost)[] productionOverrides =
        {
            // 지옥불 군단장. 220자원 140틱, per docs/FACTION-MECHANICS.md. The most
            // expensive unit in the game, and it kills nothing itself: it pays for
            // itself with what it spawns.
            (Faction.Hellfire, Role.Signature, 0, Produce(220, 140)),

            // 지옥불 군단장의 자손. Free, and off the production list altogether: it
            // comes out of a warlord every World.WarlordSpawnTicks and there is no
            // other way to get one. A price on it would be a second way in, and
            // then 무료 is only a discount on a unit you can also just buy. Entry 0
            // only: 균열 파수병 behind it is bought at the shared ranged price.
            (Faction.Hellfire, Role.Ranged, 0, new ProductionCost()),
        };

        private static ProductionCost Produce(int resources, int ticks) =>
            new ProductionCost { Resources = resources, Ticks = ticks, Produced = true };

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
            production = new ProductionCost[FactionCount * RoleCount];
            for (int faction = 0; faction < FactionCount; faction++)
            {
                for (int role = 0; role < RoleCount; role++)
                {
                    stats[Index((Faction)faction, (Role)role)] = sharedStats[role];
                    production[Index((Faction)faction, (Role)role)] = sharedProduction[role];
                }
            }

            // An entry past the first starts from the same shared row its slot's
            // entry 0 does, so adding a name to the extras table adds a unit that
            // already fights and already has a price. Where it is meant to differ
            // is an override row, which is the one place a difference is written.
            extraStats = new UnitStats[extras.Length];
            extraProduction = new ProductionCost[extras.Length];
            for (int i = 0; i < extras.Length; i++)
            {
                extraStats[i] = sharedStats[(int)extras[i].Role];
                extraProduction[i] = sharedProduction[(int)extras[i].Role];
            }

            // Entry 0 lands in the flat tables, everything else in the parallel
            // ones. ExtraIndex is subscripted rather than tested, so an override
            // naming an entry no faction fields is an index out of range at type
            // load rather than a silent write onto entry 0.
            for (int i = 0; i < statOverrides.Length; i++)
            {
                var (faction, role, slot, replacement) = statOverrides[i];
                if (slot == 0) stats[Index(faction, role)] = replacement;
                else extraStats[ExtraIndex(faction, role, slot)] = replacement;
            }

            for (int i = 0; i < productionOverrides.Length; i++)
            {
                var (faction, role, slot, replacement) = productionOverrides[i];
                if (slot == 0) production[Index(faction, role)] = replacement;
                else extraProduction[ExtraIndex(faction, role, slot)] = replacement;
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
        /// The roster numbers for entry 0 of one faction's take on a role.
        /// Faction is required rather than defaulted: a caller that cannot name
        /// one is a caller reading somebody else's unit.
        /// </summary>
        public static UnitStats Stats(Faction faction, Role role) => Stats(faction, role, 0);

        /// <summary>
        /// The roster numbers one entry fights from. An entity carries its slot,
        /// so this is what every combat and movement question resolves through:
        /// 지옥불's ranged entry 0 flies and its entry 1 does not, and a system
        /// that asked by role alone would fly both.
        ///
        /// A slot this faction does not field falls back to entry 0, which is a
        /// real row rather than a zero-hp default struct. Nothing can reach that
        /// path in play — <see cref="Production"/> refuses a slot that is not
        /// there before an entity carrying one can exist — and a floor is what
        /// keeps a mistake somewhere else from spawning a body that is already
        /// dead.
        /// </summary>
        public static UnitStats Stats(Faction faction, Role role, int slot)
        {
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? stats[Index(faction, role)] : extraStats[extra];
        }

        /// <summary>
        /// What producing entry 0 of this faction's take on this role costs and
        /// how long it takes. Faction is required for the same reason
        /// <see cref="Stats(Faction, Role)"/> requires it: 지옥불 군단장 is not
        /// priced like anyone else's signature.
        /// </summary>
        public static ProductionCost Production(Faction faction, Role role) =>
            Production(faction, role, 0);

        /// <summary>
        /// What producing one entry costs and how long it takes. A slot this
        /// faction does not field answers a default cost, which is not produced:
        /// the table fails closed, so refusing a Produce that names an entry
        /// nobody wrote is the same rule that refuses one naming a building, and
        /// there is no second place for it to be got wrong.
        /// </summary>
        public static ProductionCost Production(Faction faction, Role role, int slot)
        {
            if (slot == 0) return production[Index(faction, role)];
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? new ProductionCost() : extraProduction[extra];
        }

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
        /// True when this faction fields entry 0 of this slot at all. An empty
        /// name is a slot docs/FACTIONS.md leaves out on purpose, and a faction
        /// cannot build or produce what its roster does not list.
        /// </summary>
        public static bool Has(Faction faction, Role role) => Has(faction, role, 0);

        /// <summary>
        /// True when this faction fields this entry. The name is the test, for
        /// both entry 0 and the ones behind it: an entry with no name is one the
        /// roster does not list, whether it was left blank on purpose like 인간's
        /// melee row or asked for past the end of the list.
        /// </summary>
        public static bool Has(Faction faction, Role role, int slot) =>
            Name(faction, role, slot).Length > 0;

        /// <summary>
        /// How many entries this slot holds. Always at least one, so a caller that
        /// wants the whole list loops 0 to this and a caller that wants the unit
        /// the view draws today asks for entry 0 and ignores it.
        /// </summary>
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

        /// <summary>
        /// The name of one entry of a slot, where slot is 0 to SlotCount - 1, and
        /// "" for anything past the end. An entity carries its slot, so this is
        /// what the view labels a body with.
        /// </summary>
        public static string Name(Faction faction, Role role, int slot)
        {
            if (slot == 0) return names[Index(faction, role)];
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? "" : extras[extra].Name;
        }

        /// <summary>The sprite of one entry of a slot, where slot is 0 to SlotCount - 1.</summary>
        public static string Sprite(Faction faction, Role role, int slot)
        {
            if (slot == 0) return sprites[Index(faction, role)];
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? "" : extras[extra].Sprite;
        }

        /// <summary>
        /// Where this entry sits in <see cref="extras"/>, or -1 for entry 0 and
        /// for anything past the end of the list. Callers that must fail closed
        /// treat -1 as "not there"; callers that only need a row to read fall back
        /// to entry 0.
        ///
        /// A scan of a table this size costs less than the jagged arrays that
        /// would make it a subscript, and only entry 0 is on the view's
        /// per-entity path anyway.
        /// </summary>
        private static int ExtraIndex(Faction faction, Role role, int slot)
        {
            if (slot <= 0) return -1;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i].Faction == faction && extras[i].Role == role && --slot == 0) return i;
            }
            return -1;
        }

        private static int Index(Faction faction, Role role) => (int)faction * RoleCount + (int)role;
    }
}
