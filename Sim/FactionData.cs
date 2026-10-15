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
        /// What this body takes off every hit it receives, before the floor of one
        /// that <see cref="World.DamageAfterArmor"/> keeps. Zero on every row
        /// today: the field exists so that 방어력 research has something to add
        /// to, and a roster number is where a body that is born tougher would say
        /// so. Roster data rather than entity state for the reason
        /// <see cref="Air"/> is: it never changes for a given row, so nothing
        /// hashes it. What the owner has researched on top of it is hashed, as an
        /// upgrade level, and read through World.ArmorBonus.
        /// </summary>
        public int Armor;

        /// <summary>
        /// How much ground this body claims, as a radius in cells. Two walkers
        /// closer than the sum of their footprints push each other apart; see
        /// World.SeparationSystem. Roster data rather than entity state for the
        /// reason <see cref="Air"/> is: it never changes for a given row, so
        /// nothing hashes it and two peers cannot disagree about it.
        ///
        /// Zero on everything that never moves, and on nothing that does: a
        /// walker with no footprint is a walker nothing pushes and that pushes
        /// nothing, which is the overlap this field exists to end, so the harness
        /// refuses a row with a Speed and no Footprint.
        /// </summary>
        public Fix Footprint;

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
        /// Can shoot something that walks. The pair of <see cref="HitsAir"/>, and
        /// the two together are what docs/FACTION-MECHANICS.md 공중 asks for once
        /// 대포 is 지상 전용 and Towerback is 공중 전용: reach into the air and
        /// reach along the ground are separate facts about a weapon, so a row can
        /// say either, both, or — see the harness — neither by mistake.
        ///
        /// Two bools rather than one, and not a flag derived from something else,
        /// because there is nothing else in the row that already says it. That is
        /// the line <see cref="HasWeapon"/> draws: a fact the row states once is
        /// read back, and a fact the row does not state gets a field. What keeps
        /// the pair honest is not the encoding but the invariant beside it — an
        /// armed row that reaches neither is a unit that acquires, closes, and
        /// fires nothing, and EveryWeaponReachesSomething refuses one.
        /// </summary>
        public bool HitsGround;

        /// <summary>
        /// Whether this row carries a weapon at all. 지옥불 군단장, 경계 운반자
        /// and 마도 정찰기 are 비전투 per the documents, and their rows are the
        /// combat-slot rows that answer no.
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
    /// What growing one entry of a slot costs its owner, and how long the body
    /// spends unable to do anything while it happens. The pair of
    /// <see cref="ProductionCost"/>, and separate from it on purpose: production
    /// makes a new body out of a building's queue, growth changes what an existing
    /// body is. A slot can be on one list, the other, both, or neither, and the two
    /// prices are decided apart — 세계수 정령's 고목 수호자 is 50 to grow into and
    /// the shared 20 to produce, which is two numbers about two different acts.
    /// </summary>
    public struct GrowthCost
    {
        /// <summary>Taken in full when the growth starts. Never refunded: 환불 없다.</summary>
        public int Resources;

        /// <summary>Whole ticks the body spends growing. Never seconds.</summary>
        public int Ticks;

        /// <summary>
        /// Whether a Grow command may name this entry at all. Default false on the
        /// struct, so a slot nobody wrote fails closed: five factions out of six
        /// have no growth at all and every one of them is refused by this field
        /// rather than by a faction test written somewhere else.
        /// </summary>
        public bool Grown;
    }

    /// <summary>
    /// What one row of the upgrade table changes. Not hashed — a level is hashed,
    /// and the effect is read back off the row the level belongs to — but kept
    /// in a fixed order anyway, because the harness names rows by it.
    ///
    /// One value per mechanic number rather than a generic "stat plus amount",
    /// because each of these is read by exactly one system and that system is
    /// the only place that knows what a level of it means: a second remnant
    /// level is not a thing, and a generic amount would let a row say it.
    /// </summary>
    public enum UpgradeEffect
    {
        None = 0,
        Attack = 1,
        Armor = 2,

        // 세계수 정령
        GrowthTime = 3,
        GrowthHpRamp = 4,

        // 지옥불 군단
        OffspringCap = 5,
        OffspringPeriod = 6,

        // 물 슬라임
        VolleyCap = 7,
        VolleyRadius = 8,

        // 돌 골렘 부족
        RemnantTicks = 9,
        GolemMeleeRange = 10,

        // 차원 유랑종
        ArrivalRadius = 11,
        ArrivalTicks = 12,

        // 인간 마법 문명
        CaptureTicks = 13,
        CannonRange = 14,

        // docs/TECH-TREE.md 연구 행 배치, 묶음 3: one roster entry's reach,
        // rate, speed or armor. Which entry and what value is the
        // entryUpgrades table in FactionData, read by World.RangeFor,
        // AttackTicksFor, SpeedFor and ArmorFor.

        // 세계수 정령
        VineRange = 15,
        GiantVineRange = 16,
        ThunderBirdAttackTicks = 17,
        LeafbladeSpeed = 18,

        // 지옥불 군단
        LavaCatapultRange = 19,
        AshWingAttackTicks = 20,
        HardenedCarapace = 21,

        // 물 슬라임
        ArcherRange = 22,
        SpitterRange = 23,
        WaterCannonRange = 24,

        // 돌 골렘 부족
        SlingerRange = 25,
        FloatingStoneRange = 26,

        // 차원 유랑종
        StormShardAttackTicks = 27,
        VineShardRange = 28,
        RiftBatteryRange = 29,
        FleeSpeed = 30,

        // 인간 마법 문명
        CrossbowRange = 31,
        ArcaneRiderSpeed = 32,
        BindingRange = 33,
        BindingArmor = 34,
    }

    /// <summary>
    /// Which number of one roster entry a row of the entry upgrade table
    /// replaces. Range, AttackTicks and Speed give the researched value
    /// outright, the way docs/TECH-TREE.md writes them (사거리 5 → 6); Armor
    /// is added to the row's own armor (방어 +2). FleeSpeed is the speed of a
    /// body that is fleeing and only then: 도주 가속.
    /// </summary>
    public enum UpgradedStat
    {
        None = 0,
        Range = 1,
        AttackTicks = 2,
        Speed = 3,
        Armor = 4,
        FleeSpeed = 5,
    }

    /// <summary>
    /// One thing a 기술 건물 can research. The price is the price of level 1;
    /// level n costs n times it, on the same clock. Default on the struct is a
    /// row with MaxLevel 0, so a row nobody wrote — an index past the end of a
    /// faction's list — fails closed: there is no level it can be raised to.
    /// </summary>
    public struct UpgradeRow
    {
        /// <summary>View-only. Nothing in the simulation reads it.</summary>
        public string Name;

        /// <summary>What level 1 costs. Taken in full when the research starts.</summary>
        public int Resources;

        /// <summary>Whole ticks one level takes. The same for every level.</summary>
        public int Ticks;

        /// <summary>How many times this row can be researched. Zero is a row that does not exist.</summary>
        public int MaxLevel;

        /// <summary>What the owner's tier must reach before this row can be started.</summary>
        public int Tier;

        public UpgradeEffect Effect;

        /// <summary>
        /// The building entry that runs this row. A Research names a building, and
        /// only one whose role and slot are these takes it. (Role.Tech, 0) on every
        /// row today, the 기술 건물 each faction already has. docs/TECH-TREE.md
        /// moves rows onto a second research building (Tech 1 and after) once
        /// those buildings exist, and on that day this field changes and no rule does.
        /// </summary>
        public (Role Role, int Slot) ResearchAt;

        /// <summary>
        /// A building entry other than the one researching that has to stand,
        /// finished, before this row can start. Role.None is no such building,
        /// which is every row today. Asked through World.HasStanding, the same
        /// question a unit's prerequisite asks, so the two cannot answer apart.
        /// </summary>
        public (Role Role, int Slot) Requires;
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
        /// Bump on any change to the tables below, to the map MatchScenario paints,
        /// or to a rule a tick system applies. Two peers running different content
        /// produce different results from the same input, so this travels in the
        /// handshake and a mismatch is a rejection before tick 0. Terrain counts: a
        /// peer generating a different map has to be turned away at the handshake
        /// rather than desync on tick 1. So does a rule: 차원 유랑종 통로 moved this
        /// to 23 without touching a table, because a peer that arrives a body at a
        /// 굴절 기둥 and a peer that puts it down at the 통로 have diverged on tick 0
        /// of the order with nothing in the roster to say why. 24 is the plainer
        /// case: 차원 유랑종's own numbers landed in the tables, and a 경계 운반자
        /// that carries a weapon on one peer and none on the other is a body that
        /// kills something on one screen and nothing on the other. 25 is a rule
        /// again: 경계 운반자 도주 touches no table at all, and a peer that walks
        /// the body out of a fight while the other leaves it standing has diverged
        /// on the tick something came into range, with nothing in the roster to
        /// say why. 26 is a table again, and the plainest kind: building prices
        /// stopped being one shared row. A peer charged 70 for a 굴절 기둥 and a
        /// peer charged 100 for the same cell have disagreed about a banked total
        /// on the tick it was placed, and every later purchase is decided off that
        /// number. 27 is a rule once more: Produce began refusing a slot the roster
        /// leaves blank. A peer that puts 인간's nameless melee body on the field
        /// and a peer that spends nothing have diverged on the tick the order was
        /// given, and the roster is the same on both — what differs is whether it
        /// is consulted. 28 is the map and a rule together: cells carry an
        /// elevation that MatchScenario paints, and the pathfinder walks eight
        /// directions with a climb cost and smooths what it finds. A peer on 27
        /// routes a unit one cell at a time through a lane a peer on 28 cuts
        /// diagonally, and the two have diverged on the first tick anything moves.
        /// 29 is separation: walkers that share ground push each other apart by
        /// the footprint their roster row carries. A peer on 28 stacks ten bodies
        /// on one point where a peer on 29 spreads them, and every range test
        /// after that reads different positions. 30 is research: a 기술 건물 takes
        /// a Research order and a finished level changes damage, armor and six
        /// factions' mechanic numbers, so a peer on 29 refuses an order a peer on
        /// 30 charges for, and the two banks differ from that tick on. 31 is the
        /// tech tree: tier, prerequisite and producing building are asked of the
        /// entry rather than the role. A peer on 30 queues a 잎날 정령 at the 생명의
        /// 나무 that a peer on 31 refuses, and the two banks differ by twenty on the
        /// tick the order was given. 32 is the roster catching up with its
        /// documents: the rows docs/TECH-TREE.md marks † took the numbers
        /// docs/FACTION-MECHANICS.md gives them, 바람 정령 and 구름 용 fly, 용암
        /// 아가리 and 굴림바위 언덕 stop reaching the air, and 마도 정찰기 loses its
        /// weapon. A peer on 31 sells a 용암 갑각 악마 for 20 that a peer on 32
        /// charges 90 for, and the banks differ by 70 on the tick the order is
        /// given. 33 is the tech tree's rows: sixty-five entries docs/TECH-TREE.md
        /// 묶음 3 adds behind the existing ones, twenty research rows that change
        /// one entry's reach, rate, speed or armor, and second production
        /// buildings for five existing entries. A peer on 32 refuses a Build of a
        /// 석조 병영 that a peer on 33 charges 120 for, and the banks differ by 120
        /// on the tick the order is given. 34 is two rules the map and the clock
        /// read: a building under construction keeps the damage it takes instead
        /// of having its hp rewritten from progress every tick, and the two
        /// opening fighters are the faction's first armed produced roster
        /// entries instead of Melee 0 and Ranged 0. A peer on 33 starts 인간
        /// with a nameless body that a peer on 34 replaces, and a site shot at
        /// stands at different hp on the next tick.
        /// </summary>
        public const uint ContentVersion = 34;

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
        /// 차원 유랑종 도착: 통로에서 60틱, per docs/FACTION-MECHANICS.md. A named
        /// constant rather than a number in the 틈새 사수 row below, because
        /// World.ArrivalTicksFor answers it for a peer without 도착 가속, and the
        /// two have to be the same number.
        /// </summary>
        public const int PassageArrivalTicks = 60;

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
        /// HitsGround is set on everything that shoots at all, melee included,
        /// which is what makes the exceptions the documents name — 대포, 용암
        /// 아가리 and 굴림바위 언덕 지상 전용, Towerback 공중 전용 — the only rows
        /// that turn one of them off.
        /// </summary>
        /// <summary>
        /// The footprint every walking row starts from: three eighths of a cell,
        /// so two bodies stand three quarters of a cell apart and four fit across
        /// a three-cell lane. Under half a cell on purpose — a push is clamped to
        /// half a Speed and every Speed is under a cell, so a body is never pushed
        /// further than one cell edge in a tick, which is the step WalkableStep
        /// already knows how to ask about.
        /// </summary>
        public static readonly Fix BodyRadius = Fix.Ratio(3, 8);

        // The four shapes a docs/TECH-TREE.md 묶음 3 row takes, so that a row
        // reads as the document's columns in the document's order: 체력, 방어,
        // 속도, then 피해, 사거리, 주기 and 대공/대지 for a body that carries a
        // weapon. Footprint is BodyRadius on anything with a speed and zero on
        // anything without one, per docs/TECH-TREE.md 읽는 법, so it is never
        // a column a row can get wrong.
        private const bool Walks = false;
        private const bool Flies = true;

        private static UnitStats Fighter(bool air, int hp, int armor, Fix speed,
            int damage, int range, int attackTicks, bool hitsAir, bool hitsGround) =>
            new UnitStats
            {
                Hp = hp,
                Armor = armor,
                Speed = speed,
                Footprint = speed.Raw != 0 ? BodyRadius : Fix.Zero,
                Damage = damage,
                Range = Fix.FromInt(range),
                AttackTicks = attackTicks,
                Air = air,
                HitsAir = hitsAir,
                HitsGround = hitsGround,
            };

        /// <summary>A body with no weapon: 비전투 in the document's 역할 column.</summary>
        private static UnitStats Support(bool air, int hp, int armor, Fix speed) =>
            new UnitStats { Hp = hp, Armor = armor, Speed = speed, Footprint = BodyRadius, Air = air };

        /// <summary>A building that shoots: the 기능 column's 피해, 사거리, 주기 and reach.</summary>
        private static UnitStats Turret(int hp, int damage, int range, int attackTicks, bool hitsAir, bool hitsGround) =>
            new UnitStats
            {
                Hp = hp,
                Damage = damage,
                Range = Fix.FromInt(range),
                AttackTicks = attackTicks,
                HitsAir = hitsAir,
                HitsGround = hitsGround,
            };

        /// <summary>A building that does not shoot: hp and nothing else.</summary>
        private static UnitStats Structure(int hp) => new UnitStats { Hp = hp };

        private static readonly UnitStats[] sharedStats =
        {
            /* None       */ new UnitStats { Hp = 1 },
            /* Base       */ new UnitStats { Hp = 400 },
            /* Worker     */ new UnitStats { Hp = 60, Speed = Fix.Ratio(1, 4), Footprint = BodyRadius },
            /* Production */ new UnitStats { Hp = 400 },
            /* Defense    */ new UnitStats { Hp = 300, Damage = 9, Range = Fix.FromInt(6), AttackTicks = 20, HitsAir = true, HitsGround = true },
            /* Melee      */ new UnitStats { Hp = 100, Speed = Fix.Ratio(1, 4), Footprint = BodyRadius, Damage = 7, Range = Fix.FromInt(2), AttackTicks = 15, HitsGround = true },
            /* Ranged     */ new UnitStats { Hp = 70, Speed = Fix.Ratio(1, 4), Footprint = BodyRadius, Damage = 6, Range = Fix.FromInt(6), AttackTicks = 18, HitsAir = true, HitsGround = true },
            /* Signature  */ new UnitStats { Hp = 130, Speed = Fix.Ratio(3, 8), Footprint = BodyRadius, Damage = 10, Range = Fix.FromInt(3), AttackTicks = 22, HitsAir = true, HitsGround = true },
            /* Supply     */ new UnitStats { Hp = 200 },
            /* Tech       */ new UnitStats { Hp = 250 },
        };

        /// <summary>
        /// Where a faction departs from the shared row, per entry rather than per
        /// role. Every row is a number or a flag a document gives:
        /// docs/FACTION-MECHANICS.md's faction tables, the 공중 rule of
        /// docs/FACTIONS.md, and the rows docs/TECH-TREE.md marks † — and nothing
        /// here is a number the documents do not name.
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
            // 세계수 정령 고목 수호자, the melee half of 성장. Numbers from
            // docs/FACTION-MECHANICS.md, which names hp and damage and calls it
            // 근접. The shared melee reach, rate and speed are written out here
            // rather than inherited, for the reason the Towerback row writes out a
            // speed: what a body a player spent a worker on walks and swings at is
            // a decision, and this row is the one place a reader looks for it.
            //
            // Entry 1 of the slot. 잎날 정령 holds entry 0 and keeps the shared row,
            // which is what 세계수 정령 produces from a building; entry 1 is what a
            // 풀씨 정령 grows into and nothing can buy. That split is the same one
            // 지옥불's ranged slot has between 자손 and 균열 파수병, and it is the
            // reason both keys name a slot.
            (Faction.TreeSpirits, Role.Melee, 1,
                new UnitStats
                {
                    Hp = 220,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 12,
                    Range = Fix.FromInt(2),
                    AttackTicks = 15,
                    HitsGround = true,
                }),

            // 세계수 정령 덩쿨 정령, the ranged half, and entry 1 for the same
            // reason 고목 수호자 is: 번개 정령 holds entry 0 and is what the faction
            // buys. Reach 5 and damage 6, both from the document, which is a
            // shorter reach than the shared ranged row for a unit that costs a
            // worker rather than mana.
            (Faction.TreeSpirits, Role.Ranged, 1,
                new UnitStats
                {
                    Hp = 90,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 6,
                    Range = Fix.FromInt(5),
                    AttackTicks = 18,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 세계수 정령 바람 정령. 공중, per docs/FACTIONS.md, which lists it with
            // the faction's fliers; docs/TECH-TREE.md follows it and wins over
            // docs/FACTION-MECHANICS.md 공중, which leaves it off. Every number is
            // the shared signature row's, written out because the row has to
            // exist to carry the flag.
            (Faction.TreeSpirits, Role.Signature, 0,
                new UnitStats
                {
                    Hp = 130,
                    Speed = Fix.Ratio(3, 8),
                    Footprint = BodyRadius,
                    Damage = 10,
                    Range = Fix.FromInt(3),
                    AttackTicks = 22,
                    Air = true,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 지옥불 용암 갑각 악마. 체력 260, 근접, 피해 14, 속도 1/6칸/틱, per
            // docs/FACTION-MECHANICS.md 지옥불 표: 개체가 적고 하나하나가 크다,
            // written into the one body the faction buys to hold the line. The
            // shared melee reach and rate are written out for the reason 고목
            // 수호자's row writes them out.
            (Faction.Hellfire, Role.Melee, 0,
                new UnitStats
                {
                    Hp = 260,
                    Speed = Fix.Ratio(1, 6),
                    Footprint = BodyRadius,
                    Damage = 14,
                    Range = Fix.FromInt(2),
                    AttackTicks = 15,
                    HitsGround = true,
                }),

            // 지옥불 용암 아가리. 지상 전용, per docs/FACTIONS.md 공중 규칙, which
            // names it with 대포 and 굴림바위 언덕. The shared defense weapon is
            // kept and written out; only the air reach is gone.
            (Faction.Hellfire, Role.Defense, 0,
                new UnitStats
                {
                    Hp = 300,
                    Damage = 9,
                    Range = Fix.FromInt(6),
                    AttackTicks = 20,
                    HitsGround = true,
                }),

            // 지옥불 군단장. Flies, and carries no weapon at all: it kills nothing
            // itself and pays for itself by what it spawns. Numbers from
            // docs/FACTION-MECHANICS.md. Keeps the shared signature speed, because
            // a unit the player has to keep alive is a unit the player has to be
            // able to pull out.
            (Faction.Hellfire, Role.Signature, 0,
                new UnitStats { Hp = 300, Speed = Fix.Ratio(3, 8), Footprint = BodyRadius, Air = true }),

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
                    Footprint = BodyRadius,
                    Damage = 5,
                    Range = Fix.FromInt(4),
                    AttackTicks = 18,
                    Air = true,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 물 슬라임 거품 정령. 체력 130, 근접, 피해 6, per
            // docs/FACTION-MECHANICS.md 물 슬라임 표: the wall the archers stand
            // behind, tougher than the shared melee row and lighter in the hand.
            (Faction.WaterSlimes, Role.Melee, 0,
                new UnitStats
                {
                    Hp = 130,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 6,
                    Range = Fix.FromInt(2),
                    AttackTicks = 15,
                    HitsGround = true,
                }),

            // 물 슬라임 물결 궁수. 체력 70, 사거리 6, 기본 피해 4, per
            // docs/FACTION-MECHANICS.md. 기본 is the word that matters: 일제 사격
            // adds up to 4 on top of this, so one archer alone hits for 4 and a
            // massed one for 8.
            (Faction.WaterSlimes, Role.Ranged, 0,
                new UnitStats
                {
                    Hp = 70,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 4,
                    Range = Fix.FromInt(6),
                    AttackTicks = 18,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 물 슬라임 구름 용. 공중, per docs/FACTIONS.md and
            // docs/FACTION-MECHANICS.md 공중, which both name it a flier. The
            // shared signature numbers, written out for the reason 바람 정령's are.
            (Faction.WaterSlimes, Role.Signature, 0,
                new UnitStats
                {
                    Hp = 130,
                    Speed = Fix.Ratio(3, 8),
                    Footprint = BodyRadius,
                    Damage = 10,
                    Range = Fix.FromInt(3),
                    AttackTicks = 22,
                    Air = true,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 돌 골렘 꼬마돌. 체력 80, per docs/FACTION-MECHANICS.md 돌 골렘 표,
            // the same 80 the neutral 꼬마돌 인간 captures carries as
            // World.NeutralRockHp, which is the same creature on no side.
            (Faction.RockGolems, Role.Worker, 0,
                new UnitStats { Hp = 80, Speed = Fix.Ratio(1, 4), Footprint = BodyRadius }),

            // 돌 골렘 이끼바위 골렘. 체력 300, 근접, 피해 9, 속도 1/6칸/틱, per
            // docs/FACTION-MECHANICS.md. The shared melee reach and rate are kept
            // and written out; 골렘 사거리 adds to the reach in World.RangeFor.
            (Faction.RockGolems, Role.Melee, 0,
                new UnitStats
                {
                    Hp = 300,
                    Speed = Fix.Ratio(1, 6),
                    Footprint = BodyRadius,
                    Damage = 9,
                    Range = Fix.FromInt(2),
                    AttackTicks = 15,
                    HitsGround = true,
                }),

            // 돌 골렘 바위 술사. 체력 120, 사거리 5, 피해 7, per
            // docs/FACTION-MECHANICS.md. A ranged row, so it reaches both layers.
            (Faction.RockGolems, Role.Ranged, 0,
                new UnitStats
                {
                    Hp = 120,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 7,
                    Range = Fix.FromInt(5),
                    AttackTicks = 18,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 돌 골렘 굴림바위 언덕. 지상 전용, per docs/FACTIONS.md 공중 규칙, for
            // the reason 용암 아가리 is: the shared defense weapon without its
            // air reach.
            (Faction.RockGolems, Role.Defense, 0,
                new UnitStats
                {
                    Hp = 300,
                    Damage = 9,
                    Range = Fix.FromInt(6),
                    AttackTicks = 20,
                    HitsGround = true,
                }),

            // 차원 유랑종 폭풍편. 체력 110, 근접, 피해 8, per
            // docs/FACTION-MECHANICS.md. The shared melee reach, rate and speed are
            // written out here rather than inherited, for the reason 고목 수호자's
            // row writes them out: what a body a player paid 60 for walks and swings
            // at is a decision, and this row is the one place a reader looks for it.
            // Entry 0 of the slot; the two 멸종 슬라임 behind it keep the shared row
            // until 한시적 소환 gives them numbers of their own.
            (Faction.Driftworlds, Role.Melee, 0,
                new UnitStats
                {
                    Hp = 110,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 8,
                    Range = Fix.FromInt(2),
                    AttackTicks = 15,
                    HitsGround = true,
                }),

            // 차원 유랑종 굴절 기둥. 체력 200, per docs/FACTION-MECHANICS.md, and a
            // third of the shared defense row's 300 — the one number in this table
            // the 통로 mechanic actually reads back, because 기둥이 부서지면 그
            // 지역에 대한 접근이 통째로 끊긴다 and a pillar that dies faster is a
            // region that is lost sooner. The shared defense weapon is kept and
            // written out: the document gives it no damage, reach or rate of its
            // own, and a 방어 building that shot nothing would be a second, silent
            // departure hidden inside a hp change.
            (Faction.Driftworlds, Role.Defense, 0,
                new UnitStats
                {
                    Hp = 200,
                    Damage = 9,
                    Range = Fix.FromInt(6),
                    AttackTicks = 20,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 차원 유랑종 경계 운반자. 체력 240 and 비전투, per
            // docs/FACTION-MECHANICS.md; docs/FACTIONS.md says the same of the
            // original — 직접 공격하지 않는 개체다 — and asks that it not be turned
            // into a combat unit. No Damage, so HasWeapon is false and CanAttack
            // refuses it every path into a fight, exactly as it refuses 지옥불
            // 군단장. What it is worth instead is the arrival radius it walks
            // around with; see World.ProvidesArrival.
            //
            // The shared signature speed is written out rather than inherited, for
            // the reason 지옥불 군단장's row writes out the same number: a body the
            // player has to keep alive is a body the player has to be able to pull
            // out, and how fast it walks is the whole of that.
            //
            // 도주 is not here either, and this time because there is nothing to
            // put here. It is a rule rather than a number: Sim/Fleeing.cs holds the
            // whole of it, and the only number it reads is World.AcquireRange,
            // which already exists and belongs to nobody's roster row. A speed or a
            // radius of its own written into this row would be a second number
            // meaning what that one already means.
            (Faction.Driftworlds, Role.Signature, 0,
                new UnitStats { Hp = 240, Speed = Fix.Ratio(3, 8), Footprint = BodyRadius }),

            // 인간 수습 마법생. 체력 55, per docs/FACTION-MECHANICS.md 인간 표: five
            // under the shared worker row, for the one worker that also captures.
            (Faction.Humans, Role.Worker, 0,
                new UnitStats { Hp = 55, Speed = Fix.Ratio(1, 4), Footprint = BodyRadius }),

            // 인간 마법 탑, the 생산 one. 체력 220, per docs/FACTION-MECHANICS.md 인간
            // 표, which prices it at 60자원 80틱 in the same row; buildOverrides
            // carries that half. The supply slot's 마법 탑 is another entry and
            // keeps the shared 200, because no document gives it a number.
            (Faction.Humans, Role.Production, 0, new UnitStats { Hp = 220 }),

            // 인간 공수 특공대. 체력 90, 사거리 5, 피해 7, per
            // docs/FACTION-MECHANICS.md. Still reaches both layers: docs/TECH-TREE.md
            // proposes it ground-only and leaves that an open decision, and this
            // row does not take it.
            (Faction.Humans, Role.Ranged, 0,
                new UnitStats
                {
                    Hp = 90,
                    Speed = Fix.Ratio(1, 4),
                    Footprint = BodyRadius,
                    Damage = 7,
                    Range = Fix.FromInt(5),
                    AttackTicks = 18,
                    HitsAir = true,
                    HitsGround = true,
                }),

            // 인간 대포. 지상 전용, per docs/FACTION-MECHANICS.md 공중, which is
            // the whole reason this row exists: everything else about it could
            // have stayed on the shared defense row. Entry 0 of the defense slot;
            // 전기 타워 and 돌 포탑 sit behind it and keep the shared row, which
            // reaches both, until M4 gives the three of them numbers apart.
            (Faction.Humans, Role.Defense, 0,
                new UnitStats
                {
                    Hp = 180,
                    Damage = 10,
                    Range = Fix.FromInt(7),
                    AttackTicks = 20,
                    HitsGround = true,
                }),

            // 인간 Towerback. 공중 전용, and the only body in the game that arrives
            // by 징발 rather than by production. Numbers from
            // docs/FACTION-MECHANICS.md, which names hp, reach and damage and no
            // speed at all: the shared signature speed is written out here rather
            // than inherited, the way 지옥불 군단장's row writes out the same
            // number, because what a captured body walks at is a decision and not
            // a blank. Entry 0 of the slot; 마도 정찰기 sits behind it.
            (Faction.Humans, Role.Signature, 0,
                new UnitStats
                {
                    Hp = 200,
                    Speed = Fix.Ratio(3, 8),
                    Footprint = BodyRadius,
                    Damage = 8,
                    Range = Fix.FromInt(6),
                    AttackTicks = 22,
                    HitsAir = true,
                }),

            // 인간 마도 정찰기. 비전투, per docs/FACTIONS.md (정찰, 비전투): no
            // Damage, so HasWeapon is false and CanAttack refuses it every path
            // into a fight, as it refuses 지옥불 군단장. Still walks:
            // docs/TECH-TREE.md proposes it 공중 and marks that a proposal, and
            // this row does not take it. Entry 1 of the slot, behind Towerback.
            (Faction.Humans, Role.Signature, 1,
                new UnitStats { Hp = 130, Speed = Fix.Ratio(3, 8), Footprint = BodyRadius }),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 찌릿쥐
            (Faction.TreeSpirits, Role.Melee, 2, Fighter(Walks, 45, 0, Fix.Ratio(3, 8), 5, 2, 10, false, true)),
            // 폭풍 기수
            (Faction.TreeSpirits, Role.Melee, 3, Fighter(Walks, 160, 1, Fix.Ratio(3, 8), 11, 2, 15, false, true)),
            // 번개새 무리
            (Faction.TreeSpirits, Role.Ranged, 2, Fighter(Flies, 120, 0, Fix.Ratio(3, 8), 4, 4, 10, true, true)),
            // 거대 덩쿨
            (Faction.TreeSpirits, Role.Ranged, 3, Fighter(Walks, 350, 2, Fix.Zero, 20, 9, 40, false, true)),
            // 도깨비불
            (Faction.TreeSpirits, Role.Signature, 1, Support(Flies, 60, 0, Fix.Ratio(3, 8))),
            // 덩굴 군집
            (Faction.TreeSpirits, Role.Signature, 2, Support(Walks, 150, 1, Fix.Ratio(1, 8))),
            // 씨앗 요람
            (Faction.TreeSpirits, Role.Production, 1, Structure(300)),
            // 회복 토템
            (Faction.TreeSpirits, Role.Defense, 1, Structure(250)),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 화염 악마
            (Faction.Hellfire, Role.Melee, 1, Fighter(Walks, 140, 1, Fix.Ratio(1, 4), 9, 2, 15, false, true)),
            // 균열 사냥개
            (Faction.Hellfire, Role.Melee, 2, Fighter(Walks, 110, 0, Fix.Ratio(3, 8), 8, 2, 12, false, true)),
            // 잿불 새끼
            (Faction.Hellfire, Role.Melee, 3, Fighter(Walks, 45, 0, Fix.Ratio(1, 4), 5, 2, 15, false, true)),
            // 용암 투석 악마
            (Faction.Hellfire, Role.Ranged, 2, Fighter(Walks, 200, 2, Fix.Ratio(1, 6), 16, 9, 45, false, true)),
            // 재날개 악마
            (Faction.Hellfire, Role.Ranged, 3, Fighter(Flies, 140, 1, Fix.Ratio(3, 8), 9, 6, 15, true, false)),
            // 산란 악마
            (Faction.Hellfire, Role.Signature, 1, Fighter(Walks, 320, 2, Fix.Ratio(1, 6), 6, 2, 20, false, true)),
            // 운석 술사
            (Faction.Hellfire, Role.Signature, 2, Support(Walks, 160, 1, Fix.Ratio(1, 4))),
            // 큰 균열
            (Faction.Hellfire, Role.Production, 1, Structure(450)),
            // 집결의 횃불
            (Faction.Hellfire, Role.Defense, 1, Structure(200)),
            // 화염 첨탑
            (Faction.Hellfire, Role.Defense, 2, Turret(260, 12, 7, 20, true, false)),

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 물막이 슬라임
            (Faction.WaterSlimes, Role.Melee, 1, Fighter(Walks, 240, 2, Fix.Ratio(1, 5), 5, 2, 18, false, true)),
            // 급류 슬라임
            (Faction.WaterSlimes, Role.Melee, 2, Fighter(Walks, 100, 0, Fix.Ratio(5, 16), 7, 2, 12, false, true)),
            // 침 뱉는 슬라임
            (Faction.WaterSlimes, Role.Ranged, 2, Fighter(Walks, 45, 0, Fix.Ratio(1, 4), 4, 4, 15, true, true)),
            // 물대포 슬라임
            (Faction.WaterSlimes, Role.Ranged, 3, Fighter(Walks, 160, 1, Fix.Ratio(1, 6), 14, 8, 40, false, true)),
            // 비구름
            (Faction.WaterSlimes, Role.Signature, 1, Support(Flies, 90, 0, Fix.Ratio(1, 4))),
            // 간헐천 술사
            (Faction.WaterSlimes, Role.Signature, 2, Support(Walks, 100, 0, Fix.Ratio(1, 4))),
            // 샘물 운반자
            (Faction.WaterSlimes, Role.Signature, 3, Support(Walks, 140, 1, Fix.Ratio(1, 5))),
            // 구름 둥지
            (Faction.WaterSlimes, Role.Production, 1, Structure(350)),
            // 솟는 샘
            (Faction.WaterSlimes, Role.Defense, 1, Structure(220)),
            // 거품 장벽
            (Faction.WaterSlimes, Role.Defense, 2, Structure(500)),

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 구르는 바위
            (Faction.RockGolems, Role.Melee, 1, Fighter(Walks, 180, 2, Fix.Ratio(1, 4), 10, 2, 20, false, true)),
            // 성벽 골렘
            (Faction.RockGolems, Role.Melee, 2, Fighter(Walks, 200, 2, Fix.Ratio(1, 6), 6, 2, 20, false, true)),
            // 깨어난 바위
            (Faction.RockGolems, Role.Melee, 3, Fighter(Walks, 150, 1, Fix.Ratio(1, 5), 8, 2, 15, false, true)),
            // 돌팔매 꼬마돌
            (Faction.RockGolems, Role.Ranged, 1, Fighter(Walks, 70, 0, Fix.Ratio(1, 4), 5, 5, 18, true, true)),
            // 부유석 파수
            (Faction.RockGolems, Role.Ranged, 2, Fighter(Flies, 220, 2, Fix.Ratio(1, 6), 10, 6, 22, true, true)),
            // 모래 술사
            (Faction.RockGolems, Role.Ranged, 3, Fighter(Walks, 110, 0, Fix.Ratio(1, 4), 5, 5, 20, true, true)),
            // 이끼 현자
            (Faction.RockGolems, Role.Signature, 1, Support(Walks, 140, 1, Fix.Ratio(1, 5))),
            // 이끼 포자 무리
            (Faction.RockGolems, Role.Signature, 2, Support(Flies, 80, 0, Fix.Ratio(1, 4))),
            // 각성의 원
            (Faction.RockGolems, Role.Production, 1, Structure(500)),
            // 부유석 첨탑
            (Faction.RockGolems, Role.Defense, 1, Turret(320, 12, 7, 20, true, false)),
            // 잠듦의 고리
            (Faction.RockGolems, Role.Defense, 2, Structure(300)),

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 덩쿨편
            (Faction.Driftworlds, Role.Ranged, 2, Fighter(Walks, 80, 0, Fix.Ratio(1, 4), 6, 5, 18, true, true)),
            // 멸종한 바람 슬라임
            (Faction.Driftworlds, Role.Ranged, 3, Fighter(Flies, 90, 0, Fix.Ratio(3, 8), 7, 5, 15, true, true)),
            // 떠도는 섬
            (Faction.Driftworlds, Role.Signature, 1, Support(Flies, 260, 2, Fix.Ratio(1, 6))),
            // 층위 관측자
            (Faction.Driftworlds, Role.Signature, 2, Support(Walks, 110, 0, Fix.Ratio(1, 4))),
            // 멸종한 자연 슬라임
            (Faction.Driftworlds, Role.Signature, 3, Support(Walks, 100, 0, Fix.Ratio(1, 4))),
            // 위상 닻
            (Faction.Driftworlds, Role.Defense, 1, Structure(80)),
            // 틈새 포대
            (Faction.Driftworlds, Role.Defense, 2, Turret(260, 12, 7, 20, true, true)),

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            // Fighter: 이동, 체력, 방어, 속도, 피해, 사거리, 주기, 대공, 대지. Support: 이동, 체력, 방어, 속도.
            // 석궁 사수
            (Faction.Humans, Role.Ranged, 1, Fighter(Walks, 70, 0, Fix.Ratio(1, 4), 6, 5, 18, false, true)),
            // 마도사
            (Faction.Humans, Role.Ranged, 2, Fighter(Walks, 80, 0, Fix.Ratio(1, 4), 8, 6, 22, false, true)),
            // 석조 보행 포대
            (Faction.Humans, Role.Ranged, 3, Fighter(Walks, 260, 3, Fix.Ratio(1, 6), 11, 6, 22, false, true)),
            // 견인 대포
            (Faction.Humans, Role.Ranged, 4, Fighter(Walks, 180, 1, Fix.Ratio(1, 6), 15, 7, 40, false, true)),
            // 마도 폭격선
            (Faction.Humans, Role.Ranged, 5, Fighter(Flies, 200, 1, Fix.Ratio(1, 4), 14, 4, 40, false, true)),
            // 마도 기수
            (Faction.Humans, Role.Ranged, 6, Fighter(Walks, 110, 1, Fix.Ratio(3, 8), 6, 4, 14, false, true)),
            // 공방 기술자
            (Faction.Humans, Role.Signature, 2, Support(Walks, 80, 0, Fix.Ratio(1, 4))),
            // 마나 증폭기
            (Faction.Humans, Role.Signature, 3, Support(Walks, 150, 1, Fix.Ratio(1, 5))),
            // 마도 공창
            (Faction.Humans, Role.Production, 2, Structure(450)),
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
        /// What placing entry 0 of a building slot costs, per faction and role,
        /// subscripted by <see cref="Index"/> like everything else. Filled from
        /// <see cref="sharedBuildCosts"/> and <see cref="buildOverrides"/> for the
        /// same reason the stats and production tables are: five factions pay the
        /// shared price for most of what they place, and where one does not should
        /// read as a short list rather than as a difference found by diffing six
        /// blocks.
        /// </summary>
        private static readonly int[] buildCosts;

        /// <summary>
        /// How long entry 0 of a building slot takes to stand up, resolved the same
        /// way <see cref="buildCosts"/> is and filled in the same loop. The two are
        /// a pair: a building priced in one place and timed in another is a building
        /// somebody retunes half of.
        /// </summary>
        private static readonly int[] buildTicks;

        /// <summary>
        /// The price of the building entries past the first, parallel to
        /// <see cref="extras"/> exactly as <see cref="extraProduction"/> is. 인간's
        /// defense slot is the only one that has any today — 전기 타워 and 돌 포탑
        /// behind 대포 — and this array is why pricing 대포 apart leaves the two of
        /// them where they were. An override keyed by role alone would have moved
        /// all three, which is the defect #110 found in the production table.
        /// </summary>
        private static readonly int[] extraBuildCosts;

        /// <summary>The clock half of <see cref="extraBuildCosts"/>.</summary>
        private static readonly int[] extraBuildTicks;

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
        /// Balance past "matches finish" is still a non-goal, so every row here is
        /// a number a document gives: docs/FACTION-MECHANICS.md's faction tables,
        /// marked † in docs/TECH-TREE.md.
        ///
        /// Narrowing this key is what puts 균열 파수병 on the production list. The
        /// row below closes 지옥불's ranged slot because 자손 is spawned rather
        /// than bought; keyed by role it closed the second entry too, and a unit
        /// nothing can produce is a unit that is not in the game.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, ProductionCost Cost)[] productionOverrides =
        {
            // 세계수 정령's two 성장 products. Off the production list altogether,
            // for the reason 자손 and the Towerback are: 일꾼이 자란다 is the whole
            // of the faction, and a price on either of these would be a second way
            // in — one that costs no worker, which is the only thing growth
            // actually spends. Entry 1 of each slot only: 잎날 정령 and 번개 정령
            // hold entry 0 and are bought at the shared price, so the faction still
            // has something a building can make and the opponent's AI ladder still
            // has a unit to ask for.
            (Faction.TreeSpirits, Role.Melee, 1, new ProductionCost()),
            (Faction.TreeSpirits, Role.Ranged, 1, new ProductionCost()),

            // 세계수 정령 풀씨 정령. 40자원 30틱, per docs/FACTION-MECHANICS.md 세계수
            // 표. Twice the shared worker price, because this worker is also every
            // combat body the faction will ever have.
            (Faction.TreeSpirits, Role.Worker, 0, Produce(40, 30)),

            // 지옥불 군단장. 220자원 140틱, per docs/FACTION-MECHANICS.md. The most
            // expensive unit in the game, and it kills nothing itself: it pays for
            // itself with what it spawns.
            (Faction.Hellfire, Role.Signature, 0, Produce(220, 140)),

            // 지옥불 용암 갑각 악마. 90자원 80틱, per docs/FACTION-MECHANICS.md.
            (Faction.Hellfire, Role.Melee, 0, Produce(90, 80)),

            // 지옥불 군단장의 자손. Free, and off the production list altogether: it
            // comes out of a warlord every World.WarlordSpawnTicks and there is no
            // other way to get one. A price on it would be a second way in, and
            // then 무료 is only a discount on a unit you can also just buy. Entry 0
            // only: 균열 파수병 behind it is bought at the shared ranged price.
            (Faction.Hellfire, Role.Ranged, 0, new ProductionCost()),

            // 물 슬라임, per docs/FACTION-MECHANICS.md 물 슬라임 표. 거품 정령
            // 35자원 30틱, 물결 궁수 55자원 45틱.
            (Faction.WaterSlimes, Role.Melee, 0, Produce(35, 30)),
            (Faction.WaterSlimes, Role.Ranged, 0, Produce(55, 45)),

            // 돌 골렘 부족, per docs/FACTION-MECHANICS.md 돌 골렘 표. 꼬마돌
            // 40자원 35틱, 이끼바위 골렘 80자원 70틱, 바위 술사 70자원 60틱.
            (Faction.RockGolems, Role.Worker, 0, Produce(40, 35)),
            (Faction.RockGolems, Role.Melee, 0, Produce(80, 70)),
            (Faction.RockGolems, Role.Ranged, 0, Produce(70, 60)),

            // 차원 유랑종. The faction that does not make units but lets them cross,
            // and until now every one of them crossed at the shared 20자원 40틱.
            // Numbers from docs/FACTION-MECHANICS.md.
            //
            // 화산편 40자원 35틱. Its 체력 60 is already the shared worker row, so
            // there is no stat override above it: the document and the table agree
            // on that number, and a row written to restate it would be a second
            // place for the two to drift apart.
            (Faction.Driftworlds, Role.Worker, 0, Produce(40, 35)),
            // 폭풍편 60자원 50틱.
            (Faction.Driftworlds, Role.Melee, 0, Produce(60, 50)),
            // 틈새 사수, at the 통로's own clock. The document times three bodies by
            // name and then gives 도착 one number — 통로에서 60틱 — which is what
            // anything else crossing takes. 틈새 사수 is the only entry that falls
            // under it today: the three 멸종 슬라임 behind it are 한시적 소환 the
            // 층위 관측대 unlocks, and this table's own rule is that a number
            // arrives with the mechanic that needs it rather than ahead of it.
            // Priced at the shared cost, because the document gives it none.
            (Faction.Driftworlds, Role.Ranged, 0, Produce(DefaultProduceCost, PassageArrivalTicks)),
            // 경계 운반자 130자원 90틱. The second most expensive body in the game
            // and it kills nothing, which is the same bargain 지옥불 군단장 is: what
            // it is worth is the 반경 6칸 that walks with it.
            (Faction.Driftworlds, Role.Signature, 0, Produce(130, 90)),

            // 인간 Towerback. Off the production list for the same reason 자손 is,
            // and it matters more here: 포획으로만 획득한다 is the mechanic, so a
            // price on it would be a second way in and 징발 would be a discount on
            // a unit 인간 could simply buy — and then killing the neutral 꼬마돌
            // would cost the opponent nothing. Entry 0 only: 마도 정찰기 behind it
            // is bought at the shared signature price.
            (Faction.Humans, Role.Signature, 0, new ProductionCost()),

            // 인간, per docs/FACTION-MECHANICS.md 인간 표. 수습 마법생 45자원 35틱,
            // 공수 특공대 65자원 55틱.
            (Faction.Humans, Role.Worker, 0, Produce(45, 35)),
            (Faction.Humans, Role.Ranged, 0, Produce(65, 55)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, Role.Melee, 2, new ProductionCost()), // 찌릿쥐
            (Faction.TreeSpirits, Role.Melee, 3, new ProductionCost()), // 폭풍 기수
            (Faction.TreeSpirits, Role.Ranged, 2, new ProductionCost()), // 번개새 무리
            (Faction.TreeSpirits, Role.Ranged, 3, new ProductionCost()), // 거대 덩쿨
            (Faction.TreeSpirits, Role.Signature, 1, new ProductionCost()), // 도깨비불
            (Faction.TreeSpirits, Role.Signature, 2, new ProductionCost()), // 덩굴 군집

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Melee, 1, Produce(50, 50)), // 화염 악마
            (Faction.Hellfire, Role.Melee, 2, Produce(60, 50)), // 균열 사냥개
            (Faction.Hellfire, Role.Melee, 3, new ProductionCost()), // 잿불 새끼
            (Faction.Hellfire, Role.Ranged, 2, Produce(150, 120)), // 용암 투석 악마
            (Faction.Hellfire, Role.Ranged, 3, Produce(110, 90)), // 재날개 악마
            (Faction.Hellfire, Role.Signature, 1, Produce(160, 120)), // 산란 악마
            (Faction.Hellfire, Role.Signature, 2, Produce(180, 130)), // 운석 술사

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Melee, 1, Produce(70, 60)), // 물막이 슬라임
            (Faction.WaterSlimes, Role.Melee, 2, Produce(50, 40)), // 급류 슬라임
            (Faction.WaterSlimes, Role.Ranged, 2, Produce(25, 25)), // 침 뱉는 슬라임
            (Faction.WaterSlimes, Role.Ranged, 3, Produce(130, 110)), // 물대포 슬라임
            (Faction.WaterSlimes, Role.Signature, 1, Produce(75, 60)), // 비구름
            (Faction.WaterSlimes, Role.Signature, 2, Produce(120, 100)), // 간헐천 술사
            (Faction.WaterSlimes, Role.Signature, 3, Produce(80, 70)), // 샘물 운반자

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Melee, 1, Produce(75, 60)), // 구르는 바위
            (Faction.RockGolems, Role.Melee, 2, Produce(60, 50)), // 성벽 골렘
            (Faction.RockGolems, Role.Melee, 3, new ProductionCost()), // 깨어난 바위
            (Faction.RockGolems, Role.Ranged, 1, Produce(35, 35)), // 돌팔매 꼬마돌
            (Faction.RockGolems, Role.Ranged, 2, Produce(140, 120)), // 부유석 파수
            (Faction.RockGolems, Role.Ranged, 3, Produce(130, 110)), // 모래 술사
            (Faction.RockGolems, Role.Signature, 1, Produce(150, 120)), // 이끼 현자
            (Faction.RockGolems, Role.Signature, 2, Produce(70, 60)), // 이끼 포자 무리

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Ranged, 2, Produce(55, 50)), // 덩쿨편
            (Faction.Driftworlds, Role.Ranged, 3, Produce(45, 30)), // 멸종한 바람 슬라임
            (Faction.Driftworlds, Role.Signature, 1, Produce(200, 140)), // 떠도는 섬
            (Faction.Driftworlds, Role.Signature, 2, Produce(120, 100)), // 층위 관측자
            (Faction.Driftworlds, Role.Signature, 3, Produce(40, 30)), // 멸종한 자연 슬라임

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Ranged, 1, Produce(40, 35)), // 석궁 사수
            (Faction.Humans, Role.Ranged, 2, Produce(90, 70)), // 마도사
            (Faction.Humans, Role.Ranged, 3, Produce(120, 90)), // 석조 보행 포대
            (Faction.Humans, Role.Ranged, 4, Produce(150, 120)), // 견인 대포
            (Faction.Humans, Role.Ranged, 5, Produce(160, 120)), // 마도 폭격선
            (Faction.Humans, Role.Ranged, 6, Produce(70, 55)), // 마도 기수
            (Faction.Humans, Role.Signature, 2, Produce(60, 50)), // 공방 기술자
            (Faction.Humans, Role.Signature, 3, Produce(110, 90)), // 마나 증폭기
        };

        private static ProductionCost Produce(int resources, int ticks) =>
            new ProductionCost { Resources = resources, Ticks = ticks, Produced = true };

        /// <summary>
        /// 세계수 정령 성장: every roster entry a worker of some faction may grow
        /// into, and what it costs. A whole table rather than a shared row and a
        /// list of overrides, because growth is not a thing every faction does a
        /// little differently — it is a thing one faction does at all. A shared row
        /// would have to say "no" fifty-eight times to say "yes" twice.
        ///
        /// Keyed by Faction, Role and Slot, which is the same key
        /// <see cref="statOverrides"/> uses and the same pair a Grow command names
        /// through <see cref="Command.RosterArg"/>. Numbers from
        /// docs/FACTION-MECHANICS.md.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, GrowthCost Cost)[] growthTargets =
        {
            // 덩쿨 정령. 30자원, 40틱.
            (Faction.TreeSpirits, Role.Ranged, 1, Grow(30, 40)),
            // 고목 수호자. 50자원, 60틱.
            (Faction.TreeSpirits, Role.Melee, 1, Grow(50, 60)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            // 찌릿쥐. 15자원, 20틱.
            (Faction.TreeSpirits, Role.Melee, 2, Grow(15, 20)),
            // 도깨비불. 40자원, 50틱.
            (Faction.TreeSpirits, Role.Signature, 1, Grow(40, 50)),
            // 덩굴 군집. 45자원, 60틱.
            (Faction.TreeSpirits, Role.Signature, 2, Grow(45, 60)),
        };

        private static GrowthCost Grow(int resources, int ticks) =>
            new GrowthCost { Resources = resources, Ticks = ticks, Grown = true };

        /// <summary>
        /// What growing into one entry costs and how long it takes. An entry no
        /// faction grows into answers a default cost, which is not grown: the table
        /// fails closed, so refusing a Grow that names a 지옥불 unit is the same
        /// line that refuses one naming a building, and there is no second place
        /// for it to be got wrong.
        ///
        /// A linear scan of a two-row table, walked in declaration order. Never a
        /// keyed collection: this answers a question a tick system asks, and the
        /// order it is walked in has to be the same everywhere.
        /// </summary>
        public static GrowthCost Growth(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < growthTargets.Length; i++)
            {
                var row = growthTargets[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row.Cost;
            }
            return new GrowthCost();
        }

        /// <summary>
        /// How many entries the growth table holds, so a harness can walk every one
        /// of them without a copy of the list of its own.
        /// </summary>
        public static int GrowthTargetCount => growthTargets.Length;

        /// <summary>One entry of the growth table, in declaration order.</summary>
        public static (Faction Faction, Role Role, int Slot, GrowthCost Cost) GrowthTarget(int index) =>
            growthTargets[index];

        /// <summary>
        /// The most rows any faction's upgrade table holds. Sizes the per-peer
        /// level array World hashes, so a faction given a ninth row has to move
        /// this too, and the type load refuses a table that outgrows it. Eight
        /// since docs/TECH-TREE.md 묶음 3: 세계수 정령, 차원 유랑종 and 인간 hold
        /// eight rows each.
        /// </summary>
        public const int MaxUpgrades = 8;

        /// <summary>
        /// What level 1 of 공격력 and 방어력 costs and how long a level takes. The
        /// two shared rows every faction starts its table with, priced alike on
        /// purpose: neither is a faction's identity, so neither is where one
        /// faction should find a discount.
        ///
        /// Fifty, two and a half shared units. The solo match's AI never banks
        /// more than about ninety once its 기술 건물 stands, because its ladder
        /// spends on bodies every think; at a hundred the first level was a row
        /// the opponent could never reach, which is a mechanic nobody playing
        /// against it would ever see.
        /// </summary>
        public const int StatUpgradeCost = 50;
        public const int StatUpgradeTicks = 160;
        public const int StatUpgradeLevels = 3;

        /// <summary>What a faction-unique row costs and takes. One level each.</summary>
        public const int UniqueUpgradeCost = 100;
        public const int UniqueUpgradeTicks = 200;

        /// <summary>
        /// Every faction's research, in index order: 공격력, 방어력, the two rows
        /// that are the faction's own, then the docs/TECH-TREE.md 묶음 3 rows
        /// appended behind them. Keyed by faction and walked in declaration
        /// order, never through a keyed collection, for the reason
        /// <see cref="growthTargets"/> is. The first four rows carry the starting
        /// values issue #164 names, written down in docs/FACTION-MECHANICS.md
        /// 업그레이드; the 묶음 3 rows carry the document's own prices.
        ///
        /// The Tier column is 2 or 3. The first four rows are tier 3, which a
        /// finished 기술 건물 already is. A 묶음 3 row researched in a building
        /// that opens no tier says its tier here: 잎날 속도 at the 광란의 토템 opens
        /// at tier 2, 번개새 공격 주기 at the 바람 토템 at tier 3, and the command
        /// checks it in the order Produce checks its own tier.
        /// </summary>
        private static readonly (Faction Faction, UpgradeRow Row)[] upgrades =
        {
            (Faction.TreeSpirits, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.TreeSpirits, StatRow("방어력", UpgradeEffect.Armor)),
            // 성장 시간 25% 단축: 덩쿨 정령 40 → 30, 고목 수호자 60 → 45틱.
            (Faction.TreeSpirits, UniqueRow("성장 가속", UpgradeEffect.GrowthTime)),
            // 성장 중 체력 상승 폭 2배: the ramp reaches the target at half time.
            (Faction.TreeSpirits, UniqueRow("성장 체력", UpgradeEffect.GrowthHpRamp)),

            (Faction.Hellfire, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.Hellfire, StatRow("방어력", UpgradeEffect.Armor)),
            // 군단장 자손 상한 5 → 7.
            (Faction.Hellfire, UniqueRow("군단 확장", UpgradeEffect.OffspringCap)),
            // 자손 방출 주기 100 → 70틱.
            (Faction.Hellfire, UniqueRow("산란 가속", UpgradeEffect.OffspringPeriod)),

            (Faction.WaterSlimes, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.WaterSlimes, StatRow("방어력", UpgradeEffect.Armor)),
            // 밀집 보너스 상한 4 → 6.
            (Faction.WaterSlimes, UniqueRow("밀집 강화", UpgradeEffect.VolleyCap)),
            // 밀집 반경 3 → 4칸.
            (Faction.WaterSlimes, UniqueRow("밀집 반경", UpgradeEffect.VolleyRadius)),

            (Faction.RockGolems, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.RockGolems, StatRow("방어력", UpgradeEffect.Armor)),
            // 잔해 지속 400 → 600틱.
            (Faction.RockGolems, UniqueRow("오래 남는 잔해", UpgradeEffect.RemnantTicks)),
            // 골렘 근접 사거리 +1.
            (Faction.RockGolems, UniqueRow("골렘 사거리", UpgradeEffect.GolemMeleeRange)),

            (Faction.Driftworlds, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.Driftworlds, StatRow("방어력", UpgradeEffect.Armor)),
            // 도착 반경 6 → 8칸.
            (Faction.Driftworlds, UniqueRow("도착 반경", UpgradeEffect.ArrivalRadius)),
            // 통로 도착 시간 60 → 40틱.
            (Faction.Driftworlds, UniqueRow("도착 가속", UpgradeEffect.ArrivalTicks)),

            (Faction.Humans, StatRow("공격력", UpgradeEffect.Attack)),
            (Faction.Humans, StatRow("방어력", UpgradeEffect.Armor)),
            // 포획 시간 60 → 40틱.
            (Faction.Humans, UniqueRow("포획 가속", UpgradeEffect.CaptureTicks)),
            // 대포 사거리 +1.
            (Faction.Humans, UniqueRow("대포 사거리", UpgradeEffect.CannonRange)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, ResearchRow("덩쿨 사거리", 100, 160, 3, UpgradeEffect.VineRange, (Role.Tech, 0), NoBuilding)),
            (Faction.TreeSpirits, ResearchRow("거대 덩쿨 사거리", 150, 200, 3, UpgradeEffect.GiantVineRange, (Role.Tech, 0), NoBuilding)),
            (Faction.TreeSpirits, ResearchRow("번개새 공격 주기", 100, 160, 3, UpgradeEffect.ThunderBirdAttackTicks, (Role.Tech, 1), NoBuilding)),
            (Faction.TreeSpirits, ResearchRow("잎날 속도", 100, 160, 2, UpgradeEffect.LeafbladeSpeed, (Role.Tech, 2), NoBuilding)),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, ResearchRow("용암 투석 사거리", 150, 200, 3, UpgradeEffect.LavaCatapultRange, (Role.Tech, 0), NoBuilding)),
            (Faction.Hellfire, ResearchRow("재날개 공격 주기", 100, 160, 3, UpgradeEffect.AshWingAttackTicks, (Role.Tech, 0), NoBuilding)),
            (Faction.Hellfire, ResearchRow("갑각 경화", 150, 200, 3, UpgradeEffect.HardenedCarapace, (Role.Tech, 1), (Role.Tech, 0))),

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, ResearchRow("물결 궁수 사거리", 125, 200, 2, UpgradeEffect.ArcherRange, (Role.Tech, 1), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("침 사거리", 75, 120, 2, UpgradeEffect.SpitterRange, (Role.Tech, 1), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("물대포 사거리", 150, 200, 3, UpgradeEffect.WaterCannonRange, (Role.Tech, 1), NoBuilding)),

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, ResearchRow("돌팔매 사거리", 75, 120, 2, UpgradeEffect.SlingerRange, (Role.Tech, 1), NoBuilding)),
            (Faction.RockGolems, ResearchRow("부유석 사거리", 150, 200, 3, UpgradeEffect.FloatingStoneRange, (Role.Tech, 1), NoBuilding)),

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, ResearchRow("폭풍편 공격 주기", 100, 160, 2, UpgradeEffect.StormShardAttackTicks, (Role.Tech, 1), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("덩쿨편 사거리", 75, 120, 2, UpgradeEffect.VineShardRange, (Role.Tech, 1), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("틈새 포대 사거리", 100, 160, 2, UpgradeEffect.RiftBatteryRange, (Role.Tech, 1), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("도주 가속", 100, 160, 2, UpgradeEffect.FleeSpeed, (Role.Tech, 1), NoBuilding)),

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, ResearchRow("석궁 사거리", 75, 120, 3, UpgradeEffect.CrossbowRange, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("마도 기수 속도", 100, 160, 3, UpgradeEffect.ArcaneRiderSpeed, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("결속 강화", 125, 200, 2, UpgradeEffect.BindingRange, (Role.Tech, 2), NoBuilding)),
            (Faction.Humans, ResearchRow("결속 갑주", 125, 200, 2, UpgradeEffect.BindingArmor, (Role.Tech, 2), NoBuilding)),
        };

        /// <summary>
        /// Where the four #164 rows of every faction are researched: the 기술
        /// 건물, entry 0 of the Tech slot, per docs/TECH-TREE.md 업그레이드
        /// (연구 건물 열). The 묶음 3 rows name their own building in ResearchRow.
        ///
        /// A property rather than a readonly field, and that is load-bearing: the
        /// upgrade table above is a field initializer that calls StatRow, and
        /// initializers run in declaration order, so a field declared down here
        /// would still read (None, 0) when the table was built.
        /// </summary>
        private static (Role Role, int Slot) TechBuilding => (Role.Tech, 0);

        /// <summary>
        /// No building: what a prerequisite, a Requires or a producer slot that
        /// names nothing answers. A property for the reason TechBuilding is.
        /// </summary>
        public static (Role Role, int Slot) NoBuilding => (Role.None, 0);

        private static UpgradeRow StatRow(string name, UpgradeEffect effect) =>
            new UpgradeRow
            {
                Name = name,
                Resources = StatUpgradeCost,
                Ticks = StatUpgradeTicks,
                MaxLevel = StatUpgradeLevels,
                Tier = 3,
                Effect = effect,
                ResearchAt = TechBuilding,
                Requires = NoBuilding,
            };

        private static UpgradeRow UniqueRow(string name, UpgradeEffect effect) =>
            new UpgradeRow
            {
                Name = name,
                Resources = UniqueUpgradeCost,
                Ticks = UniqueUpgradeTicks,
                MaxLevel = 1,
                Tier = 3,
                Effect = effect,
                ResearchAt = TechBuilding,
                Requires = NoBuilding,
            };

        /// <summary>
        /// One row of docs/TECH-TREE.md 업그레이드 written out whole: price, clock,
        /// tier, the building that researches it and the one it requires. One
        /// level, as every row past the first two is. The rows that use this are
        /// the ones the document prices one by one, so there is no shared price to
        /// start from.
        /// </summary>
        private static UpgradeRow ResearchRow(string name, int resources, int ticks, int tier,
            UpgradeEffect effect, (Role Role, int Slot) at, (Role Role, int Slot) requires) =>
            new UpgradeRow
            {
                Name = name,
                Resources = resources,
                Ticks = ticks,
                MaxLevel = 1,
                Tier = tier,
                Effect = effect,
                ResearchAt = at,
                Requires = requires,
            };

        /// <summary>
        /// What the 묶음 3 research rows change, per roster entry: the entry, the
        /// row that changes it, which number, and the researched value. A table
        /// rather than a case per row in the four lookups, so that adding a row
        /// is one line here and the lookups do not change. Walked in declaration
        /// order, never through a keyed collection, for the reason
        /// <see cref="growthTargets"/> is.
        ///
        /// One row can name two entries — 갑각 경화 hardens 용암 갑각 악마 and 산란
        /// 악마 both — and one entry can carry two rows, as Towerback does under
        /// 결속 강화 and 결속 갑주. What a row may not do is give one entry two
        /// values for the same number, and the type load refuses it.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, UpgradeEffect Effect, UpgradedStat Stat, Fix Value)[] entryUpgrades =
        {
            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            // 덩쿨 정령 사거리 5 → 6.
            (Faction.TreeSpirits, Role.Ranged, 1, UpgradeEffect.VineRange, UpgradedStat.Range, Fix.FromInt(6)),
            // 거대 덩쿨 사거리 9 → 11.
            (Faction.TreeSpirits, Role.Ranged, 3, UpgradeEffect.GiantVineRange, UpgradedStat.Range, Fix.FromInt(11)),
            // 번개새 무리 주기 10 → 8틱.
            (Faction.TreeSpirits, Role.Ranged, 2, UpgradeEffect.ThunderBirdAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(8)),
            // 잎날 정령 속도 1/4 → 5/16.
            (Faction.TreeSpirits, Role.Melee, 0, UpgradeEffect.LeafbladeSpeed, UpgradedStat.Speed, Fix.Ratio(5, 16)),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            // 용암 투석 악마 사거리 9 → 10.
            (Faction.Hellfire, Role.Ranged, 2, UpgradeEffect.LavaCatapultRange, UpgradedStat.Range, Fix.FromInt(10)),
            // 재날개 악마 주기 15 → 12틱.
            (Faction.Hellfire, Role.Ranged, 3, UpgradeEffect.AshWingAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(12)),
            // 용암 갑각 악마 방어 +2.
            (Faction.Hellfire, Role.Melee, 0, UpgradeEffect.HardenedCarapace, UpgradedStat.Armor, Fix.FromInt(2)),
            // 산란 악마 방어 +2.
            (Faction.Hellfire, Role.Signature, 1, UpgradeEffect.HardenedCarapace, UpgradedStat.Armor, Fix.FromInt(2)),

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            // 물결 궁수 사거리 6 → 7.
            (Faction.WaterSlimes, Role.Ranged, 0, UpgradeEffect.ArcherRange, UpgradedStat.Range, Fix.FromInt(7)),
            // 침 뱉는 슬라임 사거리 4 → 5.
            (Faction.WaterSlimes, Role.Ranged, 2, UpgradeEffect.SpitterRange, UpgradedStat.Range, Fix.FromInt(5)),
            // 물대포 슬라임 사거리 8 → 9.
            (Faction.WaterSlimes, Role.Ranged, 3, UpgradeEffect.WaterCannonRange, UpgradedStat.Range, Fix.FromInt(9)),

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            // 돌팔매 꼬마돌 사거리 5 → 6.
            (Faction.RockGolems, Role.Ranged, 1, UpgradeEffect.SlingerRange, UpgradedStat.Range, Fix.FromInt(6)),
            // 부유석 파수 사거리 6 → 7.
            (Faction.RockGolems, Role.Ranged, 2, UpgradeEffect.FloatingStoneRange, UpgradedStat.Range, Fix.FromInt(7)),

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            // 폭풍편 주기 15 → 12틱.
            (Faction.Driftworlds, Role.Melee, 0, UpgradeEffect.StormShardAttackTicks, UpgradedStat.AttackTicks, Fix.FromInt(12)),
            // 덩쿨편 사거리 5 → 6.
            (Faction.Driftworlds, Role.Ranged, 2, UpgradeEffect.VineShardRange, UpgradedStat.Range, Fix.FromInt(6)),
            // 틈새 포대 사거리 7 → 8.
            (Faction.Driftworlds, Role.Defense, 2, UpgradeEffect.RiftBatteryRange, UpgradedStat.Range, Fix.FromInt(8)),
            // 경계 운반자 도주 중 속도 3/8 → 1/2.
            (Faction.Driftworlds, Role.Signature, 0, UpgradeEffect.FleeSpeed, UpgradedStat.FleeSpeed, Fix.Ratio(1, 2)),

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            // 석궁 사수 사거리 5 → 6.
            (Faction.Humans, Role.Ranged, 1, UpgradeEffect.CrossbowRange, UpgradedStat.Range, Fix.FromInt(6)),
            // 마도 기수 속도 3/8 → 1/2.
            (Faction.Humans, Role.Ranged, 6, UpgradeEffect.ArcaneRiderSpeed, UpgradedStat.Speed, Fix.Ratio(1, 2)),
            // Towerback 사거리 6 → 8.
            (Faction.Humans, Role.Signature, 0, UpgradeEffect.BindingRange, UpgradedStat.Range, Fix.FromInt(8)),
            // Towerback 방어 +2.
            (Faction.Humans, Role.Signature, 0, UpgradeEffect.BindingArmor, UpgradedStat.Armor, Fix.FromInt(2)),
        };

        /// <summary>How many rows the entry upgrade table holds, for the harness.</summary>
        public static int EntryUpgradeCount => entryUpgrades.Length;

        /// <summary>One row of the entry upgrade table, in declaration order.</summary>
        public static (Faction Faction, Role Role, int Slot, UpgradeEffect Effect, UpgradedStat Stat, Fix Value) EntryUpgrade(int index) =>
            entryUpgrades[index];

        /// <summary>How many rows this faction's upgrade table holds.</summary>
        public static int UpgradeCount(Faction faction)
        {
            int count = 0;
            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i].Faction == faction) count++;
            }
            return count;
        }

        /// <summary>
        /// One row of this faction's upgrade table, by index. An index outside the
        /// table answers a default row, which has MaxLevel 0 and so cannot be
        /// researched: the table fails closed, the way Production does for an
        /// entry nobody wrote, and a Research naming one is refused by the same
        /// line that refuses a row already at its last level.
        /// </summary>
        public static UpgradeRow Upgrade(Faction faction, int index)
        {
            if (index < 0) return new UpgradeRow { Name = "" };
            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i].Faction != faction) continue;
                if (index-- == 0) return upgrades[i].Row;
            }
            return new UpgradeRow { Name = "" };
        }

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
            buildCosts = new int[FactionCount * RoleCount];
            buildTicks = new int[FactionCount * RoleCount];
            tiers = new int[FactionCount * RoleCount];
            for (int faction = 0; faction < FactionCount; faction++)
            {
                for (int role = 0; role < RoleCount; role++)
                {
                    stats[Index((Faction)faction, (Role)role)] = sharedStats[role];
                    production[Index((Faction)faction, (Role)role)] = sharedProduction[role];
                    buildCosts[Index((Faction)faction, (Role)role)] = sharedBuildCosts[role];
                    buildTicks[Index((Faction)faction, (Role)role)] = sharedBuildTicks[role];
                    tiers[Index((Faction)faction, (Role)role)] = sharedTiers[role];
                }
            }

            // An entry past the first starts from the same shared row its slot's
            // entry 0 does, so adding a name to the extras table adds a unit that
            // already fights and already has a price. Where it is meant to differ
            // is an override row, which is the one place a difference is written.
            extraStats = new UnitStats[extras.Length];
            extraProduction = new ProductionCost[extras.Length];
            extraBuildCosts = new int[extras.Length];
            extraBuildTicks = new int[extras.Length];
            extraTiers = new int[extras.Length];
            for (int i = 0; i < extras.Length; i++)
            {
                extraStats[i] = sharedStats[(int)extras[i].Role];
                extraProduction[i] = sharedProduction[(int)extras[i].Role];
                extraBuildCosts[i] = sharedBuildCosts[(int)extras[i].Role];
                extraBuildTicks[i] = sharedBuildTicks[(int)extras[i].Role];
                extraTiers[i] = sharedTiers[(int)extras[i].Role];
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

            // Same split, same reason: an override naming an entry no faction
            // fields subscripts extraBuildCosts with -1 and fails at type load,
            // rather than silently writing onto entry 0 of the slot.
            for (int i = 0; i < buildOverrides.Length; i++)
            {
                var (faction, role, slot, resources, ticks) = buildOverrides[i];
                if (slot == 0)
                {
                    buildCosts[Index(faction, role)] = resources;
                    buildTicks[Index(faction, role)] = ticks;
                }
                else
                {
                    int extra = ExtraIndex(faction, role, slot);
                    extraBuildCosts[extra] = resources;
                    extraBuildTicks[extra] = ticks;
                }
            }

            // The tech tree tables are newer than the ones above and check more
            // than the subscript does: entry 0 of a slot the faction leaves blank
            // is a real index, so the subscript alone would let a row land on
            // 인간's nameless melee entry without a word. Every row is asked
            // whether the roster fields what it names, on both ends, and a row
            // that does not stops the type from loading — loud, immediate, and
            // the same on every runtime.
            for (int i = 0; i < tierOverrides.Length; i++)
            {
                var (faction, role, slot, tier) = tierOverrides[i];
                RequireFielded(faction, role, slot, "tierOverrides");
                Require(tier >= 1 && tier <= MaxTier, "tierOverrides gives " + faction + " " + role + " " + slot + " tier " + tier);
                if (slot == 0) tiers[Index(faction, role)] = tier;
                else extraTiers[ExtraIndex(faction, role, slot)] = tier;
            }

            for (int i = 0; i < prerequisites.Length; i++)
            {
                var (faction, role, slot, needs) = prerequisites[i];
                RequireFielded(faction, role, slot, "prerequisites");
                RequireBuilding(faction, needs, "prerequisites");
            }

            for (int i = 0; i < producerOverrides.Length; i++)
            {
                var (faction, role, slot, first, second) = producerOverrides[i];
                RequireFielded(faction, role, slot, "producerOverrides");
                Require(Production(faction, role, slot).Produced,
                    "producerOverrides names " + faction + " " + role + " " + slot + ", which nothing produces");
                RequireBuilding(faction, first, "producerOverrides");
                if (second.Role != Role.None) RequireBuilding(faction, second, "producerOverrides");
            }

            for (int i = 0; i < upgrades.Length; i++)
            {
                var (faction, row) = upgrades[i];
                RequireBuilding(faction, row.ResearchAt, "upgrades");
                if (row.Requires.Role != Role.None) RequireBuilding(faction, row.Requires, "upgrades");
            }

            for (int f = 0; f < FactionCount; f++)
            {
                Require(UpgradeCount((Faction)f) <= MaxUpgrades,
                    "upgrades gives " + (Faction)f + " more rows than MaxUpgrades");
            }

            // Every entry upgrade names an entry the roster fields and a row its
            // faction's table holds, and no entry is given two values for one
            // number: the lookups take the first match, so a second one would be
            // a row that silently does nothing.
            for (int i = 0; i < entryUpgrades.Length; i++)
            {
                var (faction, role, slot, effect, stat, _) = entryUpgrades[i];
                RequireFielded(faction, role, slot, "entryUpgrades");
                Require(stat != UpgradedStat.None, "entryUpgrades row " + i + " changes nothing");
                bool listed = false;
                for (int u = 0; u < UpgradeCount(faction); u++)
                {
                    if (Upgrade(faction, u).Effect == effect) listed = true;
                }
                Require(listed, "entryUpgrades names " + effect + ", which " + faction + " cannot research");
                for (int j = 0; j < i; j++)
                {
                    var other = entryUpgrades[j];
                    Require(other.Faction != faction || other.Role != role || other.Slot != slot || other.Stat != stat,
                        "entryUpgrades gives " + faction + " " + role + " " + slot + " two " + stat + " values");
                }
            }
        }

        private static void Require(bool holds, string what)
        {
            if (!holds) throw new System.InvalidOperationException("FactionData: " + what);
        }

        private static void RequireFielded(Faction faction, Role role, int slot, string table) =>
            Require(Has(faction, role, slot),
                table + " names " + faction + " " + role + " " + slot + ", which the roster does not field");

        private static void RequireBuilding(Faction faction, (Role Role, int Slot) building, string table)
        {
            RequireFielded(faction, building.Role, building.Slot, table);
            Require(IsBuilding(building.Role),
                table + " names " + faction + " " + building.Role + " " + building.Slot + " as a building");
        }

        /// <summary>
        /// Entry 0 of every slot. Faction major, role minor. An empty sprite is a
        /// slot docs/FACTIONS.md marks as "신규 필요": stats exist, art does not,
        /// and the view falls back to a primitive until it does. Entries past the
        /// first live in <see cref="extras"/>.
        /// </summary>
        private static readonly string[] names =
        {
            // 세계수 정령. Entry 0 of each combat slot is what the faction buys;
            // what a 풀씨 정령 grows into is entry 1 of the same slot, in extras.
            "", "생명의 나무", "풀씨 정령", "풀씨 둥지", "정령 뇌우목", "잎날 정령", "번개 정령", "바람 정령", "묘목", "뿌리 회당",
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
            "", "LifeTree", "SeedSpiritSwarm", "SeedNest", "SpiritStormtree", "Leafair", "ThunderSpirit", "WindSpirit", "Sapling", "RootHall",
            // 지옥불 군단
            "", "RiftAltar", "EmberSpiritSwarm", "SpawningPit", "LavaMaw", "MagmaSpirit", "FireChildSpirit", "FireLordSpirit", "WideningFissure", "LavaCrucible",
            // 물 슬라임
            "", "SpringheadPool", "WaterSlimeSwarm", "BubbleGenerator", "GeyserColumn", "BubbleSpirit", "AquaArcher", "CloudDragon", "StillPool", "TideAltar",
            // 돌 골렘 부족. RockTurret is human-made stonework and stays out of this faction.
            // RockRemnant is not here: it is a death state, not a unit. The signature slot is 고대 골렘.
            "", "MossrockSanctum", "MiniRockSwarm", "WakingStone", "RollingHill", "RockGolem", "RockMage", "AncientGolem", "StandingStone", "ElderMossstone",
            // 차원 유랑종. Thinnest permanent roster; the extinct slimes it summons are extras.
            "", "AnchoredWorld", "FireTadpole", "Passage", "RefractingPillar", "LightningTadpole", "RiftMarksman", "DimensionToad", "MooringClaw", "StrataObservatory",
            // 인간 마법 문명. Every entry 0 it fields has art, ApprenticeMage and
            // Workshop included. Melee is empty by design.
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
            // 세계수 정령. The two 성장 products, one per combat slot, each sitting
            // behind the entry the faction buys. Both are listed in
            // docs/FACTIONS.md as units of their own; what this table decides is
            // that they are the entries a worker becomes rather than the entries a
            // 풀씨 둥지 makes, which is what productionOverrides below says.
            (Faction.TreeSpirits, Role.Melee, "고목 수호자", "TreeGolem"),
            (Faction.TreeSpirits, Role.Ranged, "덩쿨 정령", "VineSpirit"),
            // 지옥불 군단. 균열 파수병 holds the ground; 군단장의 자손 is airborne and free.
            (Faction.Hellfire, Role.Ranged, "균열 파수병", "RiftWarden"),
            // 물 슬라임
            (Faction.WaterSlimes, Role.Ranged, "해일 인도자", "TideHerald"),
            // 차원 유랑종. Temporary summons the 층위 관측대 unlocks, not standing units.
            (Faction.Driftworlds, Role.Melee, "멸종한 화염 슬라임", "ExtinctFireSlime"),
            (Faction.Driftworlds, Role.Melee, "멸종한 바위 슬라임", "ExtinctRockSlime"),
            (Faction.Driftworlds, Role.Ranged, "멸종한 번개 슬라임", "ExtinctLightningSlime"),
            // 인간 마법 문명. Three defensive structures, the only faction with more
            // than one. 마도 정찰기's art is ArcaneScout in .art/TECH-TREE-ART.md.
            (Faction.Humans, Role.Defense, "전기 타워", "ElectricTower"),
            (Faction.Humans, Role.Defense, "돌 포탑", "RockTurret"),
            (Faction.Humans, Role.Signature, "마도 정찰기", "ArcaneScout"),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, Role.Production, "씨앗 요람", "SeedCradle"),
            (Faction.TreeSpirits, Role.Defense, "회복 토템", "HealingTotem"),
            (Faction.TreeSpirits, Role.Melee, "찌릿쥐", "ZapMouse"),
            (Faction.TreeSpirits, Role.Melee, "폭풍 기수", "StormRider"),
            (Faction.TreeSpirits, Role.Ranged, "번개새 무리", "ThunderBirdSwarm"),
            (Faction.TreeSpirits, Role.Ranged, "거대 덩쿨", "GiantVine"),
            (Faction.TreeSpirits, Role.Signature, "도깨비불", "WillOWisp"),
            (Faction.TreeSpirits, Role.Signature, "덩굴 군집", "VineColony"),
            (Faction.TreeSpirits, Role.Tech, "바람 토템", "WindTotem"),
            (Faction.TreeSpirits, Role.Tech, "광란의 토템", "FrenzyTotem"),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Production, "큰 균열", "GrandRift"),
            (Faction.Hellfire, Role.Defense, "집결의 횃불", "RallyingTorch"),
            (Faction.Hellfire, Role.Defense, "화염 첨탑", "FlameSpire"),
            (Faction.Hellfire, Role.Melee, "화염 악마", "FireSpirit"),
            (Faction.Hellfire, Role.Melee, "균열 사냥개", "RiftHound"),
            (Faction.Hellfire, Role.Melee, "잿불 새끼", "EmberWhelp"),
            (Faction.Hellfire, Role.Ranged, "용암 투석 악마", "LavaCatapultDemon"),
            (Faction.Hellfire, Role.Ranged, "재날개 악마", "AshWingDemon"),
            (Faction.Hellfire, Role.Signature, "산란 악마", "BroodDemon"),
            (Faction.Hellfire, Role.Signature, "운석 술사", "MeteorInvoker"),
            (Faction.Hellfire, Role.Tech, "재 분화구", "AshCaldera"),

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Production, "구름 둥지", "CloudNest"),
            (Faction.WaterSlimes, Role.Defense, "솟는 샘", "RisingSpring"),
            (Faction.WaterSlimes, Role.Defense, "거품 장벽", "BubbleMound"),
            (Faction.WaterSlimes, Role.Melee, "물막이 슬라임", "WaterScreenSlime"),
            (Faction.WaterSlimes, Role.Melee, "급류 슬라임", "RiptideSlime"),
            (Faction.WaterSlimes, Role.Ranged, "침 뱉는 슬라임", "WaterSlimeAttackSpit"),
            (Faction.WaterSlimes, Role.Ranged, "물대포 슬라임", "WaterCannonSlime"),
            (Faction.WaterSlimes, Role.Signature, "비구름", "RainCloud"),
            (Faction.WaterSlimes, Role.Signature, "간헐천 술사", "GeyserMage"),
            (Faction.WaterSlimes, Role.Signature, "샘물 운반자", "SpringCarrier"),
            (Faction.WaterSlimes, Role.Tech, "물결 수련장", "WaveTrainingPool"),

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Production, "각성의 원", "AwakeningCircle"),
            (Faction.RockGolems, Role.Defense, "부유석 첨탑", "HoverstoneSpire"),
            (Faction.RockGolems, Role.Defense, "잠듦의 고리", "DormancyRing"),
            (Faction.RockGolems, Role.Melee, "구르는 바위", "RockRolling"),
            (Faction.RockGolems, Role.Melee, "성벽 골렘", "WallGolem"),
            (Faction.RockGolems, Role.Melee, "깨어난 바위", "AwakenedStone"),
            (Faction.RockGolems, Role.Ranged, "돌팔매 꼬마돌", "PebbleSlinger"),
            (Faction.RockGolems, Role.Ranged, "부유석 파수", "FloatingStoneSentinel"),
            (Faction.RockGolems, Role.Ranged, "모래 술사", "SandShaper"),
            (Faction.RockGolems, Role.Signature, "이끼 현자", "MossSage"),
            (Faction.RockGolems, Role.Signature, "이끼 포자 무리", "MossSporeCluster"),
            (Faction.RockGolems, Role.Tech, "이끼 정원", "MossGarden"),

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Production, "깊은 통로", "DeepPassage"),
            (Faction.Driftworlds, Role.Defense, "위상 닻", "PhaseAnchor"),
            (Faction.Driftworlds, Role.Defense, "틈새 포대", "RiftBattery"),
            (Faction.Driftworlds, Role.Ranged, "덩쿨편", "VineShard"),
            (Faction.Driftworlds, Role.Ranged, "멸종한 바람 슬라임", "ExtinctWindSlime"),
            (Faction.Driftworlds, Role.Signature, "떠도는 섬", "WanderingIsland"),
            (Faction.Driftworlds, Role.Signature, "층위 관측자", "StrataObserver"),
            (Faction.Driftworlds, Role.Signature, "멸종한 자연 슬라임", "ExtinctNatureSlime"),
            (Faction.Driftworlds, Role.Tech, "외피 연마장", "ShellPolishingYard"),

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Production, "석조 병영", "StoneBarracks"),
            (Faction.Humans, Role.Production, "마도 공창", "ArcaneFoundry"),
            (Faction.Humans, Role.Ranged, "석궁 사수", "CrossbowMarksman"),
            (Faction.Humans, Role.Ranged, "마도사", "ArcaneMage"),
            (Faction.Humans, Role.Ranged, "석조 보행 포대", "StoneWalkerBattery"),
            (Faction.Humans, Role.Ranged, "견인 대포", "TowedCannon"),
            (Faction.Humans, Role.Ranged, "마도 폭격선", "ArcaneBomber"),
            (Faction.Humans, Role.Ranged, "마도 기수", "ArcaneRider"),
            (Faction.Humans, Role.Signature, "공방 기술자", "WorkshopEngineer"),
            (Faction.Humans, Role.Signature, "마나 증폭기", "ManaAmplifier"),
            (Faction.Humans, Role.Tech, "마도 학당", "ArcaneAcademy"),
            (Faction.Humans, Role.Tech, "결속대", "BindingPlatform"),
        };

        /// <summary>
        /// The highest tier there is. T1 is the base, T2 the production building,
        /// T3 the 기술 건물, per docs/FACTIONS.md and docs/TECH-TREE.md 기술 단계.
        /// </summary>
        public const int MaxTier = 3;

        /// <summary>
        /// The tier every entry of a role starts from, indexed by Role. A tier is a
        /// roster number like hp, not a case in the command handler. Keyed by role
        /// because it is a baseline, for the reason <see cref="sharedBuildCosts"/>
        /// is: it says nothing about any entry in particular. What one entry
        /// actually opens at comes out of <see cref="Tier(Faction, Role, int)"/>,
        /// and where an entry departs from this row is <see cref="tierOverrides"/>.
        /// </summary>
        private static readonly int[] sharedTiers =
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
        /// The tier entry 0 of every slot opens at, subscripted by
        /// <see cref="Index"/>, and the tier of the entries past the first,
        /// parallel to <see cref="extras"/>. Filled from <see cref="sharedTiers"/>
        /// and <see cref="tierOverrides"/> the way the stats tables are.
        /// </summary>
        private static readonly int[] tiers;

        private static readonly int[] extraTiers;

        /// <summary>
        /// Where one entry opens at a tier other than its role's row, keyed by
        /// Faction, Role and Slot for the reason <see cref="statOverrides"/> is.
        /// Every value is the 단계 column of docs/TECH-TREE.md, which is the
        /// source for the tier of every existing entry; the rows below are the
        /// entries where that column and the role's row disagree.
        ///
        /// While this was keyed by role the two entries of one slot had to open
        /// together. 세계수 정령's ranged slot holds 번개 정령, a tier-3 unit the
        /// 풀씨 둥지 makes once 뿌리 회당 stands, and 덩쿨 정령, which a worker
        /// grows into from tier 1. One number for both was wrong about one of them.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, int Tier)[] tierOverrides =
        {
            // 세계수 정령. 잎날 정령 is what the 풀씨 둥지 makes, so it opens with
            // the building that makes it. 고목 수호자 is a growth target and opens
            // with the 풀씨 둥지 as well. 번개 정령 waits for 뿌리 회당. 덩쿨 정령
            // is the one combat body the faction has at tier 1.
            (Faction.TreeSpirits, Role.Melee, 0, 2),
            (Faction.TreeSpirits, Role.Melee, 1, 2),
            (Faction.TreeSpirits, Role.Ranged, 0, 3),
            (Faction.TreeSpirits, Role.Ranged, 1, 1),

            // 지옥불 군단. 용암 갑각 악마 is the 악마 산란장's unit. 자손 is never
            // produced, so its tier only says where it sits in the tree.
            (Faction.Hellfire, Role.Melee, 0, 2),
            (Faction.Hellfire, Role.Ranged, 0, 3),

            // 물 슬라임 해일 인도자, behind the 조수 제단. 물결 궁수 in front of it
            // keeps the ranged row's tier 2.
            (Faction.WaterSlimes, Role.Ranged, 1, 3),

            // 차원 유랑종. The three 멸종 슬라임 and 틈새 사수 are what the 층위
            // 관측대 unlocks, so they open at tier 3. 경계 운반자 opens a tier
            // earlier than the signature row, with the 통로 it comes out of.
            (Faction.Driftworlds, Role.Melee, 1, 3),
            (Faction.Driftworlds, Role.Melee, 2, 3),
            (Faction.Driftworlds, Role.Ranged, 0, 3),
            (Faction.Driftworlds, Role.Ranged, 1, 3),
            (Faction.Driftworlds, Role.Signature, 0, 2),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, Role.Production, 1, 3), // 씨앗 요람
            (Faction.TreeSpirits, Role.Defense, 1, 2), // 회복 토템
            (Faction.TreeSpirits, Role.Melee, 3, 2), // 폭풍 기수
            (Faction.TreeSpirits, Role.Ranged, 2, 3), // 번개새 무리
            (Faction.TreeSpirits, Role.Ranged, 3, 3), // 거대 덩쿨
            (Faction.TreeSpirits, Role.Signature, 1, 2), // 도깨비불
            (Faction.TreeSpirits, Role.Signature, 2, 2), // 덩굴 군집
            (Faction.TreeSpirits, Role.Tech, 1, 3), // 바람 토템

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Production, 1, 3), // 큰 균열
            (Faction.Hellfire, Role.Defense, 1, 2), // 집결의 횃불
            (Faction.Hellfire, Role.Defense, 2, 2), // 화염 첨탑
            (Faction.Hellfire, Role.Melee, 2, 2), // 균열 사냥개
            (Faction.Hellfire, Role.Melee, 3, 2), // 잿불 새끼
            (Faction.Hellfire, Role.Ranged, 2, 3), // 용암 투석 악마
            (Faction.Hellfire, Role.Ranged, 3, 3), // 재날개 악마
            (Faction.Hellfire, Role.Signature, 1, 2), // 산란 악마

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Production, 1, 2), // 구름 둥지
            (Faction.WaterSlimes, Role.Defense, 1, 2), // 솟는 샘
            (Faction.WaterSlimes, Role.Melee, 1, 2), // 물막이 슬라임
            (Faction.WaterSlimes, Role.Melee, 2, 2), // 급류 슬라임
            (Faction.WaterSlimes, Role.Ranged, 2, 1), // 침 뱉는 슬라임
            (Faction.WaterSlimes, Role.Ranged, 3, 3), // 물대포 슬라임
            (Faction.WaterSlimes, Role.Signature, 1, 2), // 비구름
            (Faction.WaterSlimes, Role.Signature, 3, 2), // 샘물 운반자

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Production, 1, 3), // 각성의 원
            (Faction.RockGolems, Role.Defense, 1, 2), // 부유석 첨탑
            (Faction.RockGolems, Role.Defense, 2, 3), // 잠듦의 고리
            (Faction.RockGolems, Role.Melee, 1, 2), // 구르는 바위
            (Faction.RockGolems, Role.Melee, 2, 2), // 성벽 골렘
            (Faction.RockGolems, Role.Melee, 3, 3), // 깨어난 바위
            (Faction.RockGolems, Role.Ranged, 1, 1), // 돌팔매 꼬마돌
            (Faction.RockGolems, Role.Ranged, 2, 3), // 부유석 파수
            (Faction.RockGolems, Role.Ranged, 3, 3), // 모래 술사
            (Faction.RockGolems, Role.Signature, 2, 2), // 이끼 포자 무리

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Production, 1, 3), // 깊은 통로
            (Faction.Driftworlds, Role.Defense, 1, 2), // 위상 닻
            (Faction.Driftworlds, Role.Defense, 2, 2), // 틈새 포대
            (Faction.Driftworlds, Role.Ranged, 3, 3), // 멸종한 바람 슬라임

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Production, 2, 2), // 마도 공창
            (Faction.Humans, Role.Ranged, 1, 1), // 석궁 사수
            (Faction.Humans, Role.Ranged, 4, 3), // 견인 대포
            (Faction.Humans, Role.Ranged, 5, 3), // 마도 폭격선
            (Faction.Humans, Role.Signature, 2, 2), // 공방 기술자
        };

        /// <summary>
        /// The building one entry needs standing, finished, before it can be
        /// produced, placed, grown into or researched, per the 선행 column of
        /// docs/TECH-TREE.md. An entry not listed needs nothing beyond its tier.
        ///
        /// A tier and a prerequisite are two different questions and an entry
        /// passes both. A tier is what a peer has reached and a building that
        /// grants it can be any of several; a prerequisite is one named building,
        /// and only that one answers. They already part in play: tier is derived
        /// from what stands, and a finished 기술 건물 holds tier 3 by itself, so a
        /// peer whose production building is gone is still at tier 3 and is
        /// refused a second 기술 건물 and a 고목 수호자 by the prerequisite alone.
        /// They part further the day docs/TECH-TREE.md adds a second research
        /// building that opens no tier and is still what a unit needs.
        ///
        /// Keyed by Faction, Role and Slot, and walked in declaration order, never
        /// through a keyed collection, for the reason <see cref="growthTargets"/> is.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, (Role Role, int Slot) Needs)[] prerequisites =
        {
            // Every faction's 기술 건물 needs its production building: T2, and
            // that building by name.
            (Faction.TreeSpirits, Role.Tech, 0, (Role.Production, 0)),
            (Faction.Hellfire, Role.Tech, 0, (Role.Production, 0)),
            (Faction.WaterSlimes, Role.Tech, 0, (Role.Production, 0)),
            (Faction.RockGolems, Role.Tech, 0, (Role.Production, 0)),
            (Faction.Driftworlds, Role.Tech, 0, (Role.Production, 0)),
            (Faction.Humans, Role.Tech, 0, (Role.Production, 0)),

            // 세계수 정령. 고목 수호자 grows only beside a 풀씨 둥지; 번개 정령 needs
            // 뿌리 회당.
            (Faction.TreeSpirits, Role.Melee, 1, (Role.Production, 0)),
            (Faction.TreeSpirits, Role.Ranged, 0, (Role.Tech, 0)),
            // 바람 정령 needs the 바람 토템.
            (Faction.TreeSpirits, Role.Signature, 0, (Role.Tech, 1)),

            // 지옥불 군단장 needs the 용암 도가니.
            (Faction.Hellfire, Role.Signature, 0, (Role.Tech, 0)),

            // 물 슬라임 해일 인도자 and 구름 용 need the 조수 제단.
            (Faction.WaterSlimes, Role.Ranged, 1, (Role.Tech, 0)),
            (Faction.WaterSlimes, Role.Signature, 0, (Role.Tech, 0)),

            // 돌 골렘 고대 골렘 needs the 고대 이끼돌.
            (Faction.RockGolems, Role.Signature, 0, (Role.Tech, 0)),

            // 차원 유랑종. The 층위 관측대 is what unlocks the summons and 틈새 사수.
            (Faction.Driftworlds, Role.Melee, 1, (Role.Tech, 0)),
            (Faction.Driftworlds, Role.Melee, 2, (Role.Tech, 0)),
            (Faction.Driftworlds, Role.Ranged, 0, (Role.Tech, 0)),
            (Faction.Driftworlds, Role.Ranged, 1, (Role.Tech, 0)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, Role.Production, 1, (Role.Tech, 0)), // 씨앗 요람
            (Faction.TreeSpirits, Role.Defense, 1, (Role.Production, 0)), // 회복 토템
            (Faction.TreeSpirits, Role.Melee, 3, (Role.Tech, 2)), // 폭풍 기수
            (Faction.TreeSpirits, Role.Ranged, 2, (Role.Tech, 1)), // 번개새 무리
            (Faction.TreeSpirits, Role.Ranged, 3, (Role.Tech, 0)), // 거대 덩쿨
            (Faction.TreeSpirits, Role.Signature, 1, (Role.Defense, 1)), // 도깨비불
            (Faction.TreeSpirits, Role.Signature, 2, (Role.Production, 0)), // 덩굴 군집
            (Faction.TreeSpirits, Role.Tech, 1, (Role.Tech, 0)), // 바람 토템
            (Faction.TreeSpirits, Role.Tech, 2, (Role.Production, 0)), // 광란의 토템

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Production, 1, (Role.Tech, 0)), // 큰 균열
            (Faction.Hellfire, Role.Defense, 1, (Role.Production, 0)), // 집결의 횃불
            (Faction.Hellfire, Role.Defense, 2, (Role.Production, 0)), // 화염 첨탑
            (Faction.Hellfire, Role.Signature, 1, (Role.Tech, 1)), // 산란 악마
            (Faction.Hellfire, Role.Tech, 1, (Role.Production, 0)), // 재 분화구

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Production, 1, (Role.Production, 0)), // 구름 둥지
            (Faction.WaterSlimes, Role.Defense, 1, (Role.Production, 0)), // 솟는 샘
            (Faction.WaterSlimes, Role.Ranged, 3, (Role.Tech, 1)), // 물대포 슬라임
            (Faction.WaterSlimes, Role.Signature, 2, (Role.Tech, 0)), // 간헐천 술사
            (Faction.WaterSlimes, Role.Tech, 1, (Role.Production, 0)), // 물결 수련장

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Production, 1, (Role.Tech, 0)), // 각성의 원
            (Faction.RockGolems, Role.Defense, 1, (Role.Production, 0)), // 부유석 첨탑
            (Faction.RockGolems, Role.Defense, 2, (Role.Tech, 0)), // 잠듦의 고리
            (Faction.RockGolems, Role.Ranged, 2, (Role.Tech, 1)), // 부유석 파수
            (Faction.RockGolems, Role.Signature, 2, (Role.Tech, 1)), // 이끼 포자 무리
            (Faction.RockGolems, Role.Tech, 1, (Role.Production, 0)), // 이끼 정원

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Production, 1, (Role.Tech, 0)), // 깊은 통로
            (Faction.Driftworlds, Role.Defense, 1, (Role.Production, 0)), // 위상 닻
            (Faction.Driftworlds, Role.Defense, 2, (Role.Production, 0)), // 틈새 포대
            (Faction.Driftworlds, Role.Ranged, 3, (Role.Tech, 0)), // 멸종한 바람 슬라임
            (Faction.Driftworlds, Role.Signature, 3, (Role.Tech, 0)), // 멸종한 자연 슬라임
            (Faction.Driftworlds, Role.Tech, 1, (Role.Production, 0)), // 외피 연마장

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Production, 2, (Role.Production, 0)), // 마도 공창
            (Faction.Humans, Role.Ranged, 2, (Role.Tech, 1)), // 마도사
            (Faction.Humans, Role.Tech, 1, (Role.Production, 0)), // 마도 학당
            (Faction.Humans, Role.Tech, 2, (Role.Production, 0)), // 결속대
        };

        /// <summary>
        /// How many buildings one unit entry may come out of. Two, per
        /// docs/TECH-TREE.md 데이터 모델 M4: no entry there names more.
        /// </summary>
        public const int MaxProducers = 2;

        /// <summary>
        /// Where a unit entry is made, for the entries docs/TECH-TREE.md lists
        /// under more than the default building. Every other produced entry comes
        /// out of one building by rule: a worker out of the base, anything else out
        /// of the production building. That rule is what the document's tables say
        /// for every entry not listed here, so writing it out sixty times would be
        /// sixty rows that are meant to stay identical.
        ///
        /// The first four rows are the existing entries the document puts on the
        /// base as well as on the production building. The 묶음 3 rows after them
        /// name the second production buildings that batch added: 지옥불 군단장,
        /// 구름 용 and 고대 골렘 also come out of the 큰 균열, the 구름 둥지 and
        /// the 각성의 원, 공수 특공대 out of the 석조 병영, and 마도 정찰기 out of
        /// the 마도 공창 alone. Second is NoBuilding where an entry has only one.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, (Role Role, int Slot) First, (Role Role, int Slot) Second)[] producerOverrides =
        {
            // 세계수 정령 풀씨 정령: 생명의 나무 and 풀씨 둥지.
            (Faction.TreeSpirits, Role.Worker, 0, (Role.Base, 0), (Role.Production, 0)),
            // 물 슬라임 거품 정령: 수맥 웅덩이 and 거품 생성기.
            (Faction.WaterSlimes, Role.Melee, 0, (Role.Base, 0), (Role.Production, 0)),
            // 돌 골렘 이끼바위 골렘: 이끼바위 성소 and 각성 바위.
            (Faction.RockGolems, Role.Melee, 0, (Role.Base, 0), (Role.Production, 0)),
            // 차원 유랑종 폭풍편: 정박한 세계 and 통로.
            (Faction.Driftworlds, Role.Melee, 0, (Role.Base, 0), (Role.Production, 0)),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Signature, 0, (Role.Production, 0), (Role.Production, 1)), // 지옥불 군단장
            (Faction.Hellfire, Role.Melee, 1, (Role.Base, 0), (Role.Production, 0)), // 화염 악마
            (Faction.Hellfire, Role.Ranged, 2, (Role.Production, 1), NoBuilding), // 용암 투석 악마
            (Faction.Hellfire, Role.Ranged, 3, (Role.Production, 1), NoBuilding), // 재날개 악마
            (Faction.Hellfire, Role.Signature, 2, (Role.Production, 1), NoBuilding), // 운석 술사

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Signature, 0, (Role.Production, 0), (Role.Production, 1)), // 구름 용
            (Faction.WaterSlimes, Role.Ranged, 2, (Role.Base, 0), (Role.Production, 0)), // 침 뱉는 슬라임
            (Faction.WaterSlimes, Role.Signature, 1, (Role.Production, 1), NoBuilding), // 비구름

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Signature, 0, (Role.Production, 0), (Role.Production, 1)), // 고대 골렘
            (Faction.RockGolems, Role.Ranged, 1, (Role.Base, 0), (Role.Production, 0)), // 돌팔매 꼬마돌
            (Faction.RockGolems, Role.Ranged, 2, (Role.Production, 1), NoBuilding), // 부유석 파수
            (Faction.RockGolems, Role.Ranged, 3, (Role.Production, 1), NoBuilding), // 모래 술사
            (Faction.RockGolems, Role.Signature, 1, (Role.Production, 1), NoBuilding), // 이끼 현자

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Signature, 1, (Role.Production, 1), NoBuilding), // 떠도는 섬
            (Faction.Driftworlds, Role.Signature, 2, (Role.Production, 1), NoBuilding), // 층위 관측자

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Ranged, 0, (Role.Production, 0), (Role.Production, 1)), // 공수 특공대
            (Faction.Humans, Role.Signature, 1, (Role.Production, 2), NoBuilding), // 마도 정찰기
            (Faction.Humans, Role.Ranged, 1, (Role.Base, 0), (Role.Production, 1)), // 석궁 사수
            (Faction.Humans, Role.Ranged, 3, (Role.Production, 2), NoBuilding), // 석조 보행 포대
            (Faction.Humans, Role.Ranged, 4, (Role.Production, 2), NoBuilding), // 견인 대포
            (Faction.Humans, Role.Ranged, 5, (Role.Production, 2), NoBuilding), // 마도 폭격선
            (Faction.Humans, Role.Ranged, 6, (Role.Production, 1), NoBuilding), // 마도 기수
            (Faction.Humans, Role.Signature, 3, (Role.Production, 2), NoBuilding), // 마나 증폭기
        };

        /// <summary>
        /// What a building costs to place, indexed by Role. Zero on a role that is
        /// not a building; nothing may be placed on a zero, and IsBuilding is what
        /// says so. Data rather than a case in the command handler, so retuning a
        /// build order is one number here.
        ///
        /// The shared row every entry of the slot starts from, which is what keeps
        /// it keyed by role rather than by entry. It is the counterpart of
        /// <see cref="sharedProduction"/>, not of <see cref="buildOverrides"/>:
        /// an override is a statement about one named unit and a role key would
        /// spread it over the whole list, which is the defect #110 found, but a
        /// baseline says nothing about any entry in particular and a role key is
        /// exactly what it means. A faction or an entry that departs from it takes
        /// an override row keyed by Faction, Role and Slot the way statOverrides is,
        /// and <see cref="BuildCost(Faction, Role, int)"/> already takes the entry
        /// so that adding one moves the table and not its callers.
        /// </summary>
        private static readonly int[] sharedBuildCosts =
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
        ///
        /// The shared row, keyed by role for the reason <see cref="sharedBuildCosts"/>
        /// is, and decided with it: the two numbers are a pair and a building priced
        /// in one place and timed in another is a building somebody retunes half of.
        /// </summary>
        private static readonly int[] sharedBuildTicks =
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
        /// Where one faction's building departs from the shared row, keyed by
        /// Faction, Role and Slot for the reason <see cref="statOverrides"/> and
        /// <see cref="productionOverrides"/> are: an override is a statement about
        /// one named building, and a role key would spread it over every entry of
        /// the slot — 인간's 전기 타워 and 돌 포탑 sit behind 대포 and are not what
        /// the document prices.
        ///
        /// Price and clock travel in one row on purpose. They are the pair the
        /// shared tables are decided as, and two override tables would be two
        /// places to retune half a building.
        ///
        /// Numbers from docs/FACTION-MECHANICS.md, which is the only place any of
        /// them is written down. Balance past "matches finish" is still a non-goal,
        /// so nothing is here that the document does not name.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, int Resources, int Ticks)[] buildOverrides =
        {
            // 차원 유랑종 굴절 기둥. 70자원 50틱, docs/FACTION-MECHANICS.md 통로 표.
            // The one building in the game that is not really a building: what it
            // is bought for is the 반경 6칸 of arrival ground it lends, so it is
            // priced above the shared defense row and stands up slower than one.
            // Its 체력 200 already sits in statOverrides; this is the other half of
            // the same document row, and the half that had nowhere to go until now.
            (Faction.Driftworlds, Role.Defense, 0, 70, 50),

            // 인간 마법 탑, the 생산 one. 60자원 80틱, docs/FACTION-MECHANICS.md 인간
            // 표. Cheaper than the shared 150 and slower than the shared 100, which
            // is the trade the faction is written around: it opens tier 2 early and
            // pays for it in the eighty ticks it is not yet standing.
            //
            // The 생산 one only. 인간's supply slot carries the same name — the
            // document's roster calls the 마법 탑 생산·인구 both — but the mechanics
            // table prices exactly one row and marks it (생산), so the supply entry
            // stays on the shared 75자원 50틱. A number the document does not give
            // is not a number this table may invent.
            (Faction.Humans, Role.Production, 0, 60, 80),

            // 인간 대포. 55자원 50틱, docs/FACTION-MECHANICS.md 인간 표. Entry 0 of
            // the defense slot; 전기 타워 and 돌 포탑 behind it keep the shared row,
            // which is the whole reason this table is keyed by entry.
            (Faction.Humans, Role.Defense, 0, 55, 50),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            (Faction.TreeSpirits, Role.Tech, 1, 150, 100), // 바람 토템
            (Faction.TreeSpirits, Role.Tech, 2, 125, 80), // 광란의 토템

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, Role.Production, 1, 250, 140), // 큰 균열
            (Faction.Hellfire, Role.Defense, 1, 90, 50), // 집결의 횃불
            (Faction.Hellfire, Role.Defense, 2, 110, 70), // 화염 첨탑
            (Faction.Hellfire, Role.Tech, 1, 150, 100), // 재 분화구

            // 물 슬라임, docs/TECH-TREE.md 묶음 3.
            (Faction.WaterSlimes, Role.Defense, 1, 80, 50), // 솟는 샘
            (Faction.WaterSlimes, Role.Defense, 2, 50, 40), // 거품 장벽
            (Faction.WaterSlimes, Role.Tech, 1, 150, 100), // 물결 수련장

            // 돌 골렘 부족, docs/TECH-TREE.md 묶음 3.
            (Faction.RockGolems, Role.Production, 1, 200, 120), // 각성의 원
            (Faction.RockGolems, Role.Defense, 1, 110, 70), // 부유석 첨탑
            (Faction.RockGolems, Role.Defense, 2, 120, 80), // 잠듦의 고리
            (Faction.RockGolems, Role.Tech, 1, 150, 100), // 이끼 정원

            // 차원 유랑종, docs/TECH-TREE.md 묶음 3.
            (Faction.Driftworlds, Role.Production, 1, 220, 130), // 깊은 통로
            (Faction.Driftworlds, Role.Defense, 1, 40, 30), // 위상 닻
            (Faction.Driftworlds, Role.Defense, 2, 120, 70), // 틈새 포대
            (Faction.Driftworlds, Role.Tech, 1, 150, 100), // 외피 연마장

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, Role.Production, 1, 120, 80), // 석조 병영
            (Faction.Humans, Role.Production, 2, 180, 120), // 마도 공창
            (Faction.Humans, Role.Tech, 1, 150, 100), // 마도 학당
            (Faction.Humans, Role.Tech, 2, 100, 80), // 결속대
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

        /// <summary>
        /// What the owner's tier must reach before this entry can be produced,
        /// placed or grown into. An entry this faction does not field, entry 0 of
        /// a slot it leaves blank included, answers a tier above
        /// <see cref="MaxTier"/>, which no peer reaches: the gate fails closed,
        /// the way Production does for an entry nobody wrote.
        ///
        /// There is deliberately no role-keyed overload. The one there was answered
        /// one tier for every entry of a role, and docs/TECH-TREE.md opens two
        /// entries of one role two tiers apart; a caller that cannot name the
        /// entry is a caller asking about somebody else's unit.
        /// </summary>
        public static int Tier(Faction faction, Role role, int slot)
        {
            if (!Has(faction, role, slot)) return MaxTier + 1;
            if (slot == 0) return tiers[Index(faction, role)];
            return extraTiers[ExtraIndex(faction, role, slot)];
        }

        /// <summary>
        /// The building entry that has to stand, finished, before this entry can
        /// be produced, placed, grown into or researched, or
        /// <see cref="NoBuilding"/> when it needs none. A linear scan of a short
        /// table in declaration order, never a keyed lookup.
        /// </summary>
        public static (Role Role, int Slot) Prerequisite(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < prerequisites.Length; i++)
            {
                var row = prerequisites[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row.Needs;
            }
            return NoBuilding;
        }

        /// <summary>
        /// The tier this building entry opens once it stands finished: 2 for entry
        /// 0 of the production slot, 3 for entry 0 of the Tech slot, and 0 for
        /// everything else, per docs/TECH-TREE.md 기술 단계. A rule rather than a
        /// table, because it is the same rule for all six factions and no entry
        /// departs from it.
        ///
        /// The slot is the point. While TierOf asked the role, any finished
        /// production or tech building opened a tier, and the second research
        /// building docs/TECH-TREE.md gives each faction (Tech 1, a T2 building)
        /// would have opened tier 3 on the tick it stood.
        /// </summary>
        public static int GrantsTier(Faction faction, Role role, int slot)
        {
            if (slot != 0 || !Has(faction, role, slot)) return 0;
            if (role == Role.Production) return 2;
            if (role == Role.Tech) return MaxTier;
            return 0;
        }

        /// <summary>
        /// One of the buildings this unit entry comes out of, index 0 to
        /// <see cref="MaxProducers"/> - 1, or <see cref="NoBuilding"/> past the
        /// end of its list. An entry nothing produces — a building, 지옥불 자손,
        /// the Towerback, a growth target, a slot the faction does not field — has
        /// an empty list, so the producer test refuses it on its own and does not
        /// lean on the production test in front of it.
        ///
        /// By default a worker comes out of the base and anything else out of the
        /// production building; <see cref="producerOverrides"/> is where an entry
        /// has a list of its own.
        /// </summary>
        public static (Role Role, int Slot) ProducedAt(Faction faction, Role role, int slot, int index)
        {
            if (index < 0 || index >= MaxProducers) return NoBuilding;
            if (!Has(faction, role, slot) || !Production(faction, role, slot).Produced) return NoBuilding;
            for (int i = 0; i < producerOverrides.Length; i++)
            {
                var row = producerOverrides[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot)
                    return index == 0 ? row.First : row.Second;
            }
            if (index > 0) return NoBuilding;
            return role == Role.Worker ? (Role.Base, 0) : (Role.Production, 0);
        }

        /// <summary>
        /// True when a building of this entry is on this unit entry's producer
        /// list. The question TryQueueUnit asks of the building a Produce names.
        /// </summary>
        public static bool IsProducedAt(Faction faction, Role role, int slot, Role buildingRole, int buildingSlot)
        {
            if (buildingRole == Role.None) return false;
            for (int i = 0; i < MaxProducers; i++)
            {
                var at = ProducedAt(faction, role, slot, i);
                if (at.Role == buildingRole && at.Slot == buildingSlot) return true;
            }
            return false;
        }

        /// <summary>
        /// What placing entry 0 of this faction's take on this building costs.
        /// Faction is required for the same reason <see cref="Stats(Faction, Role)"/>
        /// requires it: 굴절 기둥 is not priced like anyone else's 방어 building.
        /// </summary>
        public static int BuildCost(Faction faction, Role role) => BuildCost(faction, role, 0);

        /// <summary>
        /// What placing one entry of a building slot costs. An entry this faction
        /// does not field costs nothing, which is the table failing closed rather
        /// than a free building: <see cref="Has(Faction, Role, int)"/> is the one
        /// gate, CanBuild asks it before a price is ever taken, and answering the
        /// shared row here would leave a second opinion for the two to drift apart on.
        ///
        /// There is deliberately no faction-free overload of this, and #147 deleted
        /// the one there was. It answered the shared row, which was harmless only
        /// for as long as every faction paid it; the day <see cref="buildOverrides"/>
        /// got its first row it became a function that returns a price nobody is
        /// charged. Its one remaining caller was the Unity build menu's label, so
        /// the shared row was what the card printed on the button — which is to say
        /// it would have lied to exactly the player being charged the other number.
        /// Deleting it rather than documenting it is the choice that makes the
        /// compiler ask the next caller for the faction it has to have anyway; the
        /// four callers there were all held one and passed it in one line each.
        /// </summary>
        public static int BuildCost(Faction faction, Role role, int slot)
        {
            if (!Has(faction, role, slot)) return 0;
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? buildCosts[Index(faction, role)] : extraBuildCosts[extra];
        }

        /// <summary>
        /// How many whole ticks entry 0 of this faction's take on this building
        /// spends under construction.
        /// </summary>
        public static int BuildTicks(Faction faction, Role role) => BuildTicks(faction, role, 0);

        /// <summary>
        /// How many whole ticks one entry spends under construction. Zero for an
        /// entry the faction does not field, for the same reason
        /// <see cref="BuildCost(Faction, Role, int)"/> is, and faction-keyed for
        /// the same reason too — 굴절 기둥 stands up in 50 ticks and the shared
        /// 방어 row takes 60.
        ///
        /// Zero is also what keeps the construction ramp safe: the ramp divides by
        /// this, and a site that answered zero here was placed with no ticks to
        /// count down, so ConstructionSystem's own BuildTicksLeft test skips it
        /// before the division is reached.
        /// </summary>
        public static int BuildTicks(Faction faction, Role role, int slot)
        {
            if (!Has(faction, role, slot)) return 0;
            int extra = ExtraIndex(faction, role, slot);
            return extra < 0 ? buildTicks[Index(faction, role)] : extraBuildTicks[extra];
        }

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

        private static readonly Role[] OpeningMeleeFirst = { Role.Melee, Role.Ranged, Role.Signature };
        private static readonly Role[] OpeningRangedFirst = { Role.Ranged, Role.Melee, Role.Signature };

        /// <summary>
        /// The body the map places for one of the two opening fighters of this
        /// faction, asked of the roster instead of assumed to be entry 0 of Melee
        /// (issue #154). Index 0 is the melee-first opener and index 1 the
        /// ranged-first one. Each takes the first entry of its role order that
        /// the faction fields and that carries a weapon, walking Melee, Ranged,
        /// Signature for index 0 and Ranged, Melee, Signature for index 1,
        /// lowest slot first within a role.
        ///
        /// Named and armed are the two skips. A blank row, which is what 인간's
        /// melee entry 0 is, has no name and no art, and an unarmed row such as
        /// 지옥불 군단장 would start the match with a body that never fights.
        ///
        /// Whether a building produces the row is deliberately not asked. The
        /// only unproduced row that is also first in its role is 지옥불 자손,
        /// Ranged entry 0, which is free and spawned rather than produced and is
        /// the ranged body 지옥불 has always opened with. Skipping it would hand
        /// 지옥불 a 균열 파수병 instead and move an opening the roster already
        /// spells out. Every other unproduced armed row sits behind a produced
        /// entry 0 of its role, so a produced-only rule and this one answer the
        /// same for the five other factions.
        ///
        /// 세계수 정령 and 지옥불 resolve to Melee 0 and Ranged 0, the two rows the
        /// map spawned before this existed, so ScriptedLog's entity ids keep
        /// their bodies. Nothing here is state: a pure function of the faction
        /// and the index, the same on every peer. A faction with no armed named
        /// entry at all answers Melee 0, a real row rather than a dead one, and
        /// the harness is what asserts that never happens.
        /// </summary>
        public static (Role Role, int Slot) OpeningFighter(Faction faction, int index)
        {
            Role[] order = index == 0 ? OpeningMeleeFirst : OpeningRangedFirst;
            for (int r = 0; r < order.Length; r++)
            {
                int count = SlotCount(faction, order[r]);
                for (int s = 0; s < count; s++)
                {
                    if (!Has(faction, order[r], s)) continue;
                    if (!Stats(faction, order[r], s).HasWeapon) continue;
                    return (order[r], s);
                }
            }
            return (Role.Melee, 0);
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
