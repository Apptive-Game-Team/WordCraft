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

        /// <summary>
        /// How many ticks a body of this row lives once it is spawned, or 0 for a
        /// body that lives until something kills it, which is every row but the
        /// five 멸종 슬라임 of 차원 유랑종 (docs/TECH-TREE.md M10, 묶음 6). Roster
        /// data rather than entity state for the reason <see cref="Air"/> is.
        /// What a body carries is the tick it lapses on, World's
        /// Entity.ExpireTick, which is hashed; this number only decides it.
        /// </summary>
        public int Lifetime;
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

        /// <summary>
        /// The entry a body has to be to grow into this one, docs/TECH-TREE.md
        /// M6: (Worker, 0) for the rows a 풀씨 정령 grows into, a unit entry for
        /// 폭풍 기수, 번개새 무리 and 거대 덩쿨.
        /// </summary>
        public Role FromRole;
        public int FromSlot;
    }

    /// <summary>
    /// One roster entry that emits free bodies on a clock: docs/TECH-TREE.md
    /// 데이터 모델 확장 M8. What it emits, how often, how many it keeps alive,
    /// and which research rows move the clock and the cap. Default on the
    /// struct has Spawns false, so an entry nobody wrote emits nothing.
    ///
    /// Static, like every table in FactionData. What a spawner has done so far
    /// is the state it already carries — SpawnCooldown, OffspringCount, and
    /// each child's ParentId — so a second spawner costs no hashed field.
    /// </summary>
    public struct SpawnerRow
    {
        public Faction Faction;
        public Role Role;
        public int Slot;

        /// <summary>The entry each emission is, owned by the spawner's owner.</summary>
        public Role ChildRole;
        public int ChildSlot;

        /// <summary>Whole ticks between one emission and the next.</summary>
        public int Ticks;

        /// <summary>Most children of one spawner alive at once.</summary>
        public int Cap;

        /// <summary>The research row that shortens Ticks, or None.</summary>
        public UpgradeEffect TicksEffect;
        public int TicksResearched;

        /// <summary>The research row that raises Cap, or None.</summary>
        public UpgradeEffect CapEffect;
        public int CapResearched;

        /// <summary>Whether this is a row at all. False on the default struct.</summary>
        public bool Spawns;
    }

    /// <summary>
    /// One roster entry that takes part in 차원 유랑종 도착: docs/TECH-TREE.md
    /// 데이터 모델 확장 M9. A 통로 is a building whose queue arrives at a point
    /// (Passage); an anchor is a building or a body that lends a radius around
    /// itself (Radius, in whole cells, 0 for none). Default on the struct is
    /// neither, so an entry nobody wrote takes no part.
    /// </summary>
    public struct ArrivalRow
    {
        public Faction Faction;
        public Role Role;
        public int Slot;

        /// <summary>Whether this building keeps an arrival point for its queue.</summary>
        public bool Passage;

        /// <summary>The radius this entry lends, in whole cells. 0 lends none.</summary>
        public int Radius;

        /// <summary>The research row that widens Radius, or None.</summary>
        public UpgradeEffect RadiusEffect;
        public int RadiusResearched;
    }

    /// <summary>
    /// A building whose queue is filled for free by deaths around it rather than
    /// by Produce: docs/TECH-TREE.md M10 FreeQueue, 세계수 정령 씨앗 요람. When one
    /// of its owner's units dies within Radius, one Give body joins its queue at
    /// no cost. A queue that was never paid for is never refunded, so
    /// CancelProduction is refused on it. Default on the struct has Free false.
    /// </summary>
    public struct FreeQueueRow
    {
        public Faction Faction;
        public Role Role;
        public int Slot;

        /// <summary>The entry each death puts on the queue.</summary>
        public Role GiveRole;
        public int GiveSlot;

        /// <summary>How far from the building a death still counts, in whole cells.</summary>
        public int Radius;

        /// <summary>The research row that widens Radius, or None.</summary>
        public UpgradeEffect RadiusEffect;
        public int RadiusResearched;

        /// <summary>Whether this is a row at all. False on the default struct.</summary>
        public bool Free;
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

        // docs/TECH-TREE.md 연구 행 배치, 묶음 4: one number of a mechanic
        // table row. Which row and what value is the table itself
        // (FactionData.spawners, arrivals, freeQueues).

        // 지옥불 군단: 잿불 새끼 상한 3 → 4.
        BroodCap = 35,

        // 차원 유랑종: 위상 닻 도착 반경 3 → 4칸.
        PhaseAnchorRadius = 36,

        // 세계수 정령: 씨앗 요람 반경 8 → 12칸.
        SeedCradleRadius = 37,

        // docs/TECH-TREE.md 연구 행 배치, 묶음 5: a row that unlocks an
        // ability or changes one of its numbers. Which ability and which
        // number is the ability table in FactionData (abilities), which names
        // these effects as its Unlock and Boost.

        // 세계수 정령: 휘감기 감속 40 → 60틱, 연쇄 번개 해금.
        EntangleDuration = 38,
        ChainLightning = 39,

        // 지옥불 군단: 화염탄 해금, 피 냄새 해금, 화상 오라 2 → 3, 집결 반경 5 → 7칸.
        FireShot = 40,
        BloodScent = 41,
        BurnAuraDamage = 42,
        RallyRadius = 43,

        // 물 슬라임: 회복의 비 2 → 3, 거품 벽 해금.
        HealingRainAmount = 44,
        BubbleWall = 45,

        // 돌 골렘 부족: 이끼 재생 3 → 5.
        MossRegrowthAmount = 46,

        // 인간 마법 문명: 연쇄 방전 해금, 증폭 반경 4 → 6칸, 표식 +1 → +2.
        ChainDischarge = 47,
        AmplifyRadius = 48,
        MarkAmount = 49,

        // docs/TECH-TREE.md 연구 행 배치, 묶음 6: summons that lapse and bursts.

        // 차원 유랑종: 멸종 슬라임 수명 600 → 800틱.
        SummonLifetime = 50,

        // 차원 유랑종: 층위 고정 반경 4 → 6칸.
        StrataHoldRadius = 51,

        // 세계수 정령: 전격 과부하 12 → 18.
        OverloadDamage = 52,

        // 차원 유랑종: 화염 분출 12 → 18.
        FireBurstDamage = 53,

        // docs/TECH-TREE.md 연구 행 배치, 묶음 7: autocasts and the water field.

        // 세계수 정령: 폭풍 기수 질풍 해금.
        Gale = 54,

        // 물 슬라임: 물 지대 방어 +1 → +2, 급류 슬라임 급류 해금.
        WaterFieldArmor = 55,
        Rapids = 56,

        // 돌 골렘 부족: 구르는 바위 굴러 들이받기 해금, 바위 술사 잔해 수리 해금,
        // 잔해 투척 피해 +8 → +12.
        RollCharge = 57,
        RemnantRepair = 58,
        RemnantThrowDamage = 59,

        // 차원 유랑종: 틈새 사수 정조준 해금.
        SteadyAim = 60,

        // 인간 마법 문명: 수리 4 → 6.
        RepairAmount = 61,

        // docs/TECH-TREE.md 연구 행 배치, 묶음 8: the actives Cast uses.

        // 세계수 정령: 바람 정령 칼바람 회오리 해금.
        RazorGale = 62,

        // 지옥불 군단: 운석 술사 유성우 해금.
        MeteorShower = 63,

        // 물 슬라임: 해일 인도자 해일 부름 해금, 물 지대 깔기 300 → 450틱.
        TideCall = 64,
        WaterFieldDuration = 65,

        // 돌 골렘 부족: 굳기 잔해 600 → 900틱, 깨우기 자원 40 → 25, 모래폭풍 반경 3 → 4칸.
        HardenDuration = 66,
        AwakenCost = 67,
        SandStormRadius = 68,

        // 인간 마법 문명: 견인 대포 전개 해금, 마도사 낙뢰 해금.
        Deploy = 69,
        LightningDrop = 70,
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
    /// What one row of the ability table does, docs/TECH-TREE.md M11. One value
    /// per behaviour rather than per ability, because two abilities that behave
    /// alike share the code that runs them: 연쇄 번개 and 연쇄 방전 are one
    /// ChainOnHit with different hop counts, and the four healing auras are one
    /// HealAura with different radii, amounts and targets. Not hashed: the
    /// table is static and an entity never carries which row it is running.
    /// </summary>
    public enum AbilityKind
    {
        None = 0,

        /// <summary>휘감기: a hit on a ground body sets its SlowTicksLeft to Ticks.</summary>
        SlowOnHit = 1,

        /// <summary>화염탄: a hit sets the target's BurnTicksLeft to Ticks.</summary>
        BurnOnHit = 2,

        /// <summary>연쇄 번개, 연쇄 방전: Limit hops of Radius cells, halving each hop.</summary>
        ChainOnHit = 3,

        /// <summary>피 냄새: the attack period is Amount while the target is under half hp.</summary>
        WoundedRate = 4,

        /// <summary>물막이: allied bodies of Targets within Radius take Amount more armor.</summary>
        ArmorAura = 5,

        /// <summary>거품 벽: Amount armor per other body of the same entry within Radius, at most Limit.</summary>
        ArmorPerAlly = 6,

        /// <summary>굳은 몸: Amount armor while the body is not walking.</summary>
        ArmorWhileStill = 7,

        /// <summary>표식: hostile bodies within Radius take Amount more from the owner's weapons.</summary>
        MarkAura = 8,

        /// <summary>집결, 증폭: allied Targets within Radius fire Amount ticks sooner, never under Limit.</summary>
        RateAura = 9,

        /// <summary>회복, 회복의 비, 이끼 재생, 생명 기록: Amount hp every Period ticks to allied Targets within Radius.</summary>
        HealAura = 10,

        /// <summary>화상 오라: Amount fixed damage every Period ticks to hostile Targets within Radius, per source.</summary>
        BurnAura = 11,

        /// <summary>과생장: allied growing bodies within Radius grow one tick more every Period ticks.</summary>
        GrowthAura = 12,

        /// <summary>덩굴 지대: hostile Targets within Radius walk at the slowed speed.</summary>
        SlowAura = 13,

        /// <summary>
        /// 층위 고정: every Period ticks, each allied body of Targets within
        /// Radius that lapses has its ExpireTick put back one tick, once however
        /// many sources reach it. With Period 2 the body lapses at half speed.
        /// </summary>
        ExpiryAura = 14,

        /// <summary>
        /// 전격 과부하, 화염 분출: when the body dies, Kill queues a burst of
        /// Amount fixed damage to hostile Targets within Radius of where it fell,
        /// and BurstSystem lands it later in the tick.
        /// </summary>
        DeathBurst = 15,

        /// <summary>
        /// 도착 방전: when the body comes off a queue, ProductionSystem queues a
        /// burst of Amount fixed damage to hostile Targets within Radius of where
        /// it stands, and BurstSystem lands it later in the tick.
        /// </summary>
        ArrivalBurst = 16,

        // docs/TECH-TREE.md 묶음 7: autocasts, area hits and the water field.

        /// <summary>
        /// 용암 폭발, 폭격: after a hit, every other hostile Targets the weapon can
        /// reach within Radius of where the first target stood takes half the
        /// hit's final damage (floor, at least 1).
        /// </summary>
        SplashOnHit = 17,

        /// <summary>
        /// 잔해 투척: a shot consumes the debris cell of lowest index whose centre
        /// is within World.RemnantUseReach of the shooter, adds Amount to that
        /// shot, and splashes half of it on hostile Targets within Radius.
        /// </summary>
        RemnantThrow = 18,

        /// <summary>질풍: every attack sets AbilityTicksLeft to Ticks, and the body walks Amount times as fast while it is above 0.</summary>
        GaleAfterAttack = 19,

        /// <summary>
        /// 굴러 들이받기: with the cooldown at 0 and the target World.RollMinDistance
        /// to RollMaxDistance cells off, the body rolls for up to Ticks at
        /// World.RollSpeed; the roll ends within RollEndDistance of the target or
        /// when the ticks run out, and then deals Amount fixed damage once to each
        /// hostile Targets within Radius. Cooldown from the start of the roll.
        /// </summary>
        RollCharge = 20,

        /// <summary>
        /// 잔해 수리: with the cooldown at 0, heals the allied Targets within
        /// Radius that has lost the most hp, at least Limit, by Amount, and
        /// consumes a debris cell within World.RemnantUseReach. Both or neither.
        /// </summary>
        RemnantRepair = 21,

        /// <summary>정조준: AbilityTicksLeft counts the ticks the body stands still up to Ticks, and at Ticks its reach is Amount cells longer.</summary>
        SteadyAim = 22,

        /// <summary>수리: every Period ticks, the allied Targets within Radius that has lost the most hp is healed by Amount.</summary>
        Repair = 23,

        /// <summary>
        /// 이동식 물 지대, 고정 물 지대: every Period ticks, the square of cells
        /// Radius either side of the holder's cell takes a water field lapsing
        /// Ticks from now.
        /// </summary>
        FieldEmitter = 24,

        /// <summary>급류: from a water field cell the body walks at World.RapidsSpeed.</summary>
        FieldSpeed = 25,

        /// <summary>물기둥 분수: from a water field cell the weapon reaches Amount cells further.</summary>
        FieldRange = 26,

        /// <summary>
        /// 잔해 만료 연장: every Period ticks, standing debris in the square of
        /// cells Radius either side of the holder's cell that would lapse within
        /// Ticks is put back to lapse Ticks from now.
        /// </summary>
        RemnantHold = 27,

        // docs/TECH-TREE.md 묶음 8: the actives a Cast uses. Every one of them
        // is aimed at a point within CastRange or at nothing; none names a body.

        /// <summary>
        /// 칼바람 회오리: a zone at the point for Ticks. Every tick the clock is
        /// counted down to a multiple of Period (0 included), hostile Targets
        /// within Radius of the point take Amount fixed.
        /// </summary>
        StrikeZone = 28,

        /// <summary>
        /// 유성우, 간헐천, 낙뢰: Ticks after the cast, a burst of Amount fixed on
        /// hostile Targets within Radius of the point, landed by BurstSystem.
        /// </summary>
        DelayedStrike = 29,

        /// <summary>
        /// 해일 부름: at once, a burst of Amount fixed on hostile Targets within
        /// Radius of the point, and a water field on the 3×3 cells round it
        /// lapsing Ticks from now.
        /// </summary>
        TideStrike = 30,

        /// <summary>물 지대 깔기: at once, a water field Radius cells either side of the caster's cell, lapsing Ticks from now.</summary>
        LayField = 31,

        /// <summary>굳기: the caster dies, and its cell and the four beside it take debris lapsing Ticks from now.</summary>
        Harden = 32,

        /// <summary>깨우기: consumes the debris on the point's cell and stands a 깨어난 바위 at its centre.</summary>
        Awaken = 33,

        /// <summary>
        /// 모래폭풍: a zone at the point for Ticks. Hostile Targets within Radius
        /// reach at most Amount cells and walk at World.SlowFactor.
        /// </summary>
        SandZone = 34,

        /// <summary>
        /// 전개: Ticks of transition, then Entity.Deployed flips. Deployed, the
        /// body does not walk, reaches Amount cells and splashes half its hit on
        /// Targets within Radius.
        /// </summary>
        Deploy = 35,
    }

    /// <summary>
    /// What a Cast of a row is aimed at, docs/TECH-TREE.md 능력 공통 규칙:
    /// nothing, or a point. Two point rows also ask what lies on the point's
    /// cell when the order is given, and never again after.
    /// </summary>
    public enum CastTarget
    {
        /// <summary>Not an active: no Cast names this row.</summary>
        None = 0,

        /// <summary>Target is ignored.</summary>
        NoTarget = 1,

        /// <summary>A point within CastRange of the caster.</summary>
        Point = 2,

        /// <summary>간헐천: a point within CastRange whose cell carries a water field.</summary>
        WaterFieldCell = 3,

        /// <summary>깨우기: a point within CastRange whose cell carries debris.</summary>
        RemnantCell = 4,
    }

    /// <summary>
    /// Which bodies an aura reaches, before the question of whose side they are
    /// on, which the kind answers. A body is a Unit or a Worker: 회복의 비 heals a
    /// worker and 화상 오라 burns one, because the documents say 유닛 and a worker
    /// walks the same field.
    /// </summary>
    public enum AbilityTargets
    {
        None = 0,
        Bodies = 1,
        BodiesAndBuildings = 2,
        RangedBodies = 3,
        DefenseBuildings = 4,
        GroundBodies = 5,

        /// <summary>Units, workers and buildings that do not fly: 용암 폭발 and 폭격, 지상만.</summary>
        Ground = 6,

        /// <summary>Units, workers and buildings: whatever the weapon itself can reach.</summary>
        Everything = 7,

        /// <summary>수리: finished buildings, and the units FactionData.IsDevice names.</summary>
        BuildingsAndDevices = 8,
    }

    /// <summary>
    /// One row of a faction's ability table, docs/TECH-TREE.md M11: the entry
    /// that holds it, what it does, the research that unlocks it, and its
    /// numbers. Static, like every other table here; what a level changes is
    /// read through World at the moment a system needs the number.
    ///
    /// Unlock and Boost are indexes into the faction's upgrade table, -1 for
    /// none. The table is written with the UpgradeEffect of the row and the
    /// static constructor resolves it, so an index can never point at a row
    /// that was moved. A boosted number of 0 is a number the boost leaves alone.
    /// </summary>
    public struct AbilityRow
    {
        /// <summary>View-only. Nothing in the simulation reads it.</summary>
        public string Name;

        public Faction Faction;
        public Role Role;
        public int Slot;

        public AbilityKind Kind;
        public AbilityTargets Targets;

        /// <summary>The upgrade row that unlocks this ability, or -1 when it works from the start.</summary>
        public int Unlock;

        /// <summary>Ticks between casts. Zero on every row but 잔해 수리 and 굴러 들이받기, the two that wait on AbilityCooldown.</summary>
        public int Cooldown;

        /// <summary>Cells. An aura's reach, or a chain's hop.</summary>
        public int Radius;

        /// <summary>Hp healed or burnt, armor or damage added, ticks taken off a period, or the period itself (WoundedRate).</summary>
        public int Amount;

        /// <summary>How long a status lasts once set.</summary>
        public int Ticks;

        /// <summary>Every how many ticks a periodic aura acts: on ticks where Tick % Period == 0.</summary>
        public int Period;

        /// <summary>Chain hops, the armor cap of 거품 벽, or the floor a rate aura stops at.</summary>
        public int Limit;

        /// <summary>The upgrade row that changes a number of this ability, or -1.</summary>
        public int Boost;

        public int BoostedRadius;
        public int BoostedAmount;
        public int BoostedTicks;

        /// <summary>What a Cast of this row is aimed at. None on every row that is not an active.</summary>
        public CastTarget CastTarget;

        /// <summary>How far from the caster a Cast may aim, in cells. Zero on a row that takes no point.</summary>
        public int CastRange;

        /// <summary>Resources a Cast takes. Zero on every active but 깨우기.</summary>
        public int Cost;

        /// <summary>The cost once the boost research lands, or 0 when the boost leaves it alone.</summary>
        public int BoostedCost;

        // What Unlock and Boost are written as. The static constructor turns
        // them into indexes and refuses one the faction cannot research.
        internal UpgradeEffect UnlockEffect;
        internal UpgradeEffect BoostEffect;
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
        /// opening fighters are the faction's first named, armed roster
        /// entries instead of Melee 0 and Ranged 0. A peer on 33 starts 인간
        /// with a nameless body that a peer on 34 replaces, and a site shot at
        /// stands at different hp on the next tick. 35 is docs/TECH-TREE.md 묶음
        /// 4: growth, spawners, arrival anchors, the 씨앗 요람 queue and debris
        /// read tables instead of entry 0 constants, and three research rows land.
        /// A peer on 34 refuses a Grow from 잎날 정령 into 폭풍 기수 that a peer on
        /// 35 charges 60 for, and the banks differ by 60 on the tick it is given.
        /// 36 is docs/TECH-TREE.md 묶음 5: statuses and auras. A 용암 갑각 악마
        /// burns enemy ground bodies within 2 cells for 2 every 20 ticks, so a
        /// peer on 35 and a peer on 36 disagree about a 잎날 정령's hp on the
        /// first twentieth tick the two stand that close. 37 is docs/TECH-TREE.md
        /// 묶음 6: the five 멸종 슬라임 lapse 600 ticks after they arrive, and
        /// 찌릿쥐, 멸종한 화염 슬라임 and 멸종한 번개 슬라임 burst. A peer on 36
        /// keeps a 멸종한 화염 슬라임 on the field that a peer on 37 removed on
        /// its 600th tick, and the population the two count differs by one.
        /// 38 is docs/TECH-TREE.md 묶음 7: autocasts, area hits and the water
        /// field. A 용암 투석 악마 shot splashes half its damage 2 cells round, so
        /// a peer on 37 and a peer on 38 disagree about the hp of every body
        /// standing beside the one it hits. 39 is docs/TECH-TREE.md 묶음 8: the
        /// Cast command and ten actives. A peer on 38 ignores a Cast that a
        /// peer on 39 answers with a water field, and the two disagree about
        /// AbilityCooldown on the tick the order runs.
        /// </summary>
        public const uint ContentVersion = 39;

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
            // what productionOverrides below says and World.SpawnerSystem does.
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
        /// 세계수 정령 성장: every roster entry a body of some faction may grow
        /// into, what it grows from, and what it costs. A whole table rather than a shared row and a
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
            (Faction.TreeSpirits, Role.Ranged, 1, Grow(30, 40, Role.Worker, 0)),
            // 고목 수호자. 50자원, 60틱.
            (Faction.TreeSpirits, Role.Melee, 1, Grow(50, 60, Role.Worker, 0)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 3.
            // 찌릿쥐. 15자원, 20틱.
            (Faction.TreeSpirits, Role.Melee, 2, Grow(15, 20, Role.Worker, 0)),
            // 도깨비불. 40자원, 50틱.
            (Faction.TreeSpirits, Role.Signature, 1, Grow(40, 50, Role.Worker, 0)),
            // 덩굴 군집. 45자원, 60틱.
            (Faction.TreeSpirits, Role.Signature, 2, Grow(45, 60, Role.Worker, 0)),

            // 세계수 정령, docs/TECH-TREE.md 묶음 4 (M6): a unit growing into a unit.
            // 잎날 정령 → 폭풍 기수. 60자원, 60틱.
            (Faction.TreeSpirits, Role.Melee, 3, Grow(60, 60, Role.Melee, 0)),
            // 번개 정령 → 번개새 무리. 60자원, 50틱.
            (Faction.TreeSpirits, Role.Ranged, 2, Grow(60, 50, Role.Ranged, 0)),
            // 덩쿨 정령 → 거대 덩쿨. 100자원, 120틱. Speed 0, so it roots where it grew.
            (Faction.TreeSpirits, Role.Ranged, 3, Grow(100, 120, Role.Ranged, 1)),
        };

        private static GrowthCost Grow(int resources, int ticks, Role fromRole, int fromSlot) =>
            new GrowthCost { Resources = resources, Ticks = ticks, Grown = true, FromRole = fromRole, FromSlot = fromSlot };

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
        /// Every roster entry that emits free bodies on a clock, docs/TECH-TREE.md
        /// M8. 지옥불 군단장 carries the numbers it has always had — 100 ticks and
        /// five 자손, the two World constants — and 산란 악마 is the second row:
        /// a 잿불 새끼 every 120 ticks, three alive. Walked in declaration order,
        /// never through a keyed collection, for the reason
        /// <see cref="growthTargets"/> is.
        ///
        /// Each row names its own research. 군단 확장 and 산란 가속 move the
        /// warlord's row and nothing else; 산란 상한 moves the 산란 악마's cap.
        /// </summary>
        private static readonly SpawnerRow[] spawners =
        {
            // 지옥불 군단장 → 자손. 100틱, 5기. 연구: 70틱, 7기.
            Spawner(Faction.Hellfire, Role.Signature, 0, Role.Ranged, 0,
                World.WarlordSpawnTicks, World.WarlordOffspring,
                UpgradeEffect.OffspringPeriod, World.WarlordSpawnTicksResearched,
                UpgradeEffect.OffspringCap, World.WarlordOffspringResearched),
            // 산란 악마 → 잿불 새끼. 120틱, 3기. 연구: 산란 상한 4기.
            Spawner(Faction.Hellfire, Role.Signature, 1, Role.Melee, 3,
                120, 3,
                UpgradeEffect.None, 0,
                UpgradeEffect.BroodCap, 4),
        };

        private static SpawnerRow Spawner(Faction faction, Role role, int slot, Role childRole, int childSlot,
            int ticks, int cap, UpgradeEffect ticksEffect, int ticksResearched, UpgradeEffect capEffect, int capResearched) =>
            new SpawnerRow
            {
                Faction = faction, Role = role, Slot = slot,
                ChildRole = childRole, ChildSlot = childSlot,
                Ticks = ticks, Cap = cap,
                TicksEffect = ticksEffect, TicksResearched = ticksResearched,
                CapEffect = capEffect, CapResearched = capResearched,
                Spawns = true,
            };

        /// <summary>
        /// The spawner row of one entry, or a default row with Spawns false. A
        /// linear scan in declaration order.
        /// </summary>
        public static SpawnerRow Spawner(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < spawners.Length; i++)
            {
                SpawnerRow row = spawners[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row;
            }
            return new SpawnerRow();
        }

        /// <summary>
        /// Every roster entry that is a 통로 or an anchor, docs/TECH-TREE.md M9 and
        /// 도착 규칙. 통로 and 깊은 통로 keep an arrival point; 굴절 기둥 and 경계
        /// 운반자 lend 6 cells (8 under 도착 반경, the #164 row, which reaches these
        /// two and no other), 위상 닻 3 (4 under its own row), 떠도는 섬 4, and 틈새
        /// 포대 is written down at 0 so the table says it lends nothing. Walked in
        /// declaration order, for the reason <see cref="growthTargets"/> is.
        /// </summary>
        private static readonly ArrivalRow[] arrivals =
        {
            // 통로
            Arrival(Faction.Driftworlds, Role.Production, 0, true, 0, UpgradeEffect.None, 0),
            // 깊은 통로
            Arrival(Faction.Driftworlds, Role.Production, 1, true, 0, UpgradeEffect.None, 0),
            // 굴절 기둥. 6칸, 도착 반경 8칸.
            Arrival(Faction.Driftworlds, Role.Defense, 0, false, 6, UpgradeEffect.ArrivalRadius, 8),
            // 경계 운반자. 이동식 6칸, 도착 반경 8칸.
            Arrival(Faction.Driftworlds, Role.Signature, 0, false, 6, UpgradeEffect.ArrivalRadius, 8),
            // 위상 닻. 3칸, 위상 닻 반경 4칸.
            Arrival(Faction.Driftworlds, Role.Defense, 1, false, 3, UpgradeEffect.PhaseAnchorRadius, 4),
            // 떠도는 섬. 공중 4칸.
            Arrival(Faction.Driftworlds, Role.Signature, 1, false, 4, UpgradeEffect.None, 0),
            // 틈새 포대. 도착 반경 없음.
            Arrival(Faction.Driftworlds, Role.Defense, 2, false, 0, UpgradeEffect.None, 0),
        };

        private static ArrivalRow Arrival(Faction faction, Role role, int slot, bool passage, int radius,
            UpgradeEffect radiusEffect, int radiusResearched) =>
            new ArrivalRow
            {
                Faction = faction, Role = role, Slot = slot,
                Passage = passage, Radius = radius,
                RadiusEffect = radiusEffect, RadiusResearched = radiusResearched,
            };

        /// <summary>
        /// The arrival row of one entry, or a default row that is neither a 통로
        /// nor an anchor. A linear scan in declaration order.
        /// </summary>
        public static ArrivalRow Arrival(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < arrivals.Length; i++)
            {
                ArrivalRow row = arrivals[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row;
            }
            return new ArrivalRow();
        }

        /// <summary>
        /// Every building with a free queue, docs/TECH-TREE.md M10 and 씨앗 귀환:
        /// 씨앗 요람 gives back a 풀씨 정령 for a 세계수 정령 unit that dies within 8
        /// cells, 12 under 씨앗 요람 반경. The body comes off the queue on 풀씨 정령's own production clock
        /// (30 ticks), the number the document's 씨앗 귀환 row gives. Walked in
        /// declaration order, for the reason <see cref="growthTargets"/> is.
        /// </summary>
        private static readonly FreeQueueRow[] freeQueues =
        {
            // 씨앗 요람 → 풀씨 정령. 반경 8칸.
            new FreeQueueRow
            {
                Faction = Faction.TreeSpirits, Role = Role.Production, Slot = 1,
                GiveRole = Role.Worker, GiveSlot = 0,
                Radius = 8, RadiusEffect = UpgradeEffect.SeedCradleRadius, RadiusResearched = 12,
                Free = true,
            },
        };

        /// <summary>
        /// The free queue row of one building entry, or a default row with Free
        /// false. A linear scan in declaration order.
        /// </summary>
        public static FreeQueueRow FreeQueue(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < freeQueues.Length; i++)
            {
                FreeQueueRow row = freeQueues[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row;
            }
            return new FreeQueueRow();
        }

        /// <summary>
        /// The cells a fallen 돌 골렘 unit leaves debris on, as offsets from the cell
        /// it fell on, for the entries that leave more than that one cell:
        /// docs/TECH-TREE.md 잔해 규칙. 고대 골렘 blocks its own cell and the cells
        /// either side of it on x (거대 잔해, 3칸). Every other entry is
        /// <see cref="OneRemnantCell"/>. Walked in declaration order.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, (int Dx, int Dy)[] Cells)[] remnantShapes =
        {
            // 고대 골렘: 자기 셀과 x−1, x+1.
            (Faction.RockGolems, Role.Signature, 0, new[] { (-1, 0), (0, 0), (1, 0) }),
        };

        private static readonly (int Dx, int Dy)[] OneRemnantCell = { (0, 0) };

        /// <summary>How many cells this entry's debris covers.</summary>
        public static int RemnantCellCount(Faction faction, Role role, int slot) =>
            RemnantShape(faction, role, slot).Length;

        /// <summary>One cell of this entry's debris, as an offset from the cell it fell on.</summary>
        public static (int Dx, int Dy) RemnantCell(Faction faction, Role role, int slot, int index) =>
            RemnantShape(faction, role, slot)[index];

        private static (int Dx, int Dy)[] RemnantShape(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < remnantShapes.Length; i++)
            {
                var row = remnantShapes[i];
                if (row.Faction == faction && row.Role == role && row.Slot == slot) return row.Cells;
            }
            return OneRemnantCell;
        }

        /// <summary>
        /// 한시 소환, docs/TECH-TREE.md 차원 유랑종 유닛: how long a 멸종 슬라임
        /// lives once it arrives, in ticks. Six hundred, thirty seconds.
        /// </summary>
        public const int SummonLifetime = 600;

        /// <summary>The lifetime once 소환 수명 is researched. See World.LifetimeFor.</summary>
        public const int SummonLifetimeResearched = 800;

        /// <summary>
        /// The rows that lapse, docs/TECH-TREE.md M10: the five 멸종 슬라임 of
        /// 차원 유랑종, each SummonLifetime. Written onto UnitStats.Lifetime by the
        /// static constructor, after the stat overrides, so three of the five can
        /// keep the shared row they fight from and still lapse. Every other row
        /// lives until it is killed.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot, int Ticks)[] lifetimes =
        {
            (Faction.Driftworlds, Role.Melee, 1, SummonLifetime), // 멸종한 화염 슬라임
            (Faction.Driftworlds, Role.Melee, 2, SummonLifetime), // 멸종한 바위 슬라임
            (Faction.Driftworlds, Role.Ranged, 1, SummonLifetime), // 멸종한 번개 슬라임
            (Faction.Driftworlds, Role.Ranged, 3, SummonLifetime), // 멸종한 바람 슬라임
            (Faction.Driftworlds, Role.Signature, 3, SummonLifetime), // 멸종한 자연 슬라임
        };

        /// <summary>
        /// The most rows any faction's upgrade table holds. Sizes the per-peer
        /// level array World hashes, so a faction given a ninth row has to move
        /// this too, and the type load refuses a table that outgrows it. Eight
        /// since docs/TECH-TREE.md 묶음 3: 세계수 정령, 차원 유랑종 and 인간 hold
        /// eight rows each. Nine since 묶음 4 (#197): 씨앗 요람 반경 and 위상 닻
        /// 반경 are the ninth rows of 세계수 정령 and 차원 유랑종. Twelve since 묶음 5
        /// (#200): 지옥불 군단 gains four rows (화염탄, 피 냄새, 화상 오라 강화, 집결
        /// 반경) on top of its eight. Still twelve after 묶음 6 (#202): 세계수 정령
        /// grows to twelve with 과부하 강화 and 차원 유랑종 to twelve with 소환 수명,
        /// 층위 고정 반경 and 화염 분출 강화. Thirteen since 묶음 7 (#203): 질풍 is
        /// 세계수 정령's thirteenth row and 정조준 차원 유랑종's. Fourteen since 묶음 8
        /// (#209): 칼바람 회오리 is 세계수 정령's fourteenth row, and 전개 장치 and
        /// 낙뢰 take 인간 to fourteen.
        /// </summary>
        public const int MaxUpgrades = 14;

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
            // docs/TECH-TREE.md 묶음 4. 씨앗 귀환 반경 8 → 12칸, read off the free
            // queue table. Needs a 씨앗 요람 standing.
            (Faction.TreeSpirits, ResearchRow("씨앗 요람 반경", 100, 160, 3, UpgradeEffect.SeedCradleRadius, (Role.Tech, 0), (Role.Production, 1))),

            // 지옥불 군단, docs/TECH-TREE.md 묶음 3.
            (Faction.Hellfire, ResearchRow("용암 투석 사거리", 150, 200, 3, UpgradeEffect.LavaCatapultRange, (Role.Tech, 0), NoBuilding)),
            (Faction.Hellfire, ResearchRow("재날개 공격 주기", 100, 160, 3, UpgradeEffect.AshWingAttackTicks, (Role.Tech, 0), NoBuilding)),
            (Faction.Hellfire, ResearchRow("갑각 경화", 150, 200, 3, UpgradeEffect.HardenedCarapace, (Role.Tech, 1), (Role.Tech, 0))),
            // docs/TECH-TREE.md 묶음 4. 잿불 새끼 상한 3 → 4, read off the spawner table.
            (Faction.Hellfire, ResearchRow("산란 상한", 100, 160, 2, UpgradeEffect.BroodCap, (Role.Tech, 1), NoBuilding)),

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
            // docs/TECH-TREE.md 묶음 4. 위상 닻 도착 반경 3 → 4칸, read off the arrival table.
            (Faction.Driftworlds, ResearchRow("위상 닻 반경", 100, 160, 2, UpgradeEffect.PhaseAnchorRadius, (Role.Tech, 1), NoBuilding)),

            // 인간 마법 문명, docs/TECH-TREE.md 묶음 3.
            (Faction.Humans, ResearchRow("석궁 사거리", 75, 120, 3, UpgradeEffect.CrossbowRange, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("마도 기수 속도", 100, 160, 3, UpgradeEffect.ArcaneRiderSpeed, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("결속 강화", 125, 200, 2, UpgradeEffect.BindingRange, (Role.Tech, 2), NoBuilding)),
            (Faction.Humans, ResearchRow("결속 갑주", 125, 200, 2, UpgradeEffect.BindingArmor, (Role.Tech, 2), NoBuilding)),

            // docs/TECH-TREE.md 묶음 5 (#200): the rows that unlock an ability or
            // change one of its numbers. Read through the ability table.
            (Faction.TreeSpirits, ResearchRow("휘감기 지속", 100, 160, 3, UpgradeEffect.EntangleDuration, (Role.Tech, 0), NoBuilding)),
            (Faction.TreeSpirits, ResearchRow("연쇄 번개", 150, 200, 3, UpgradeEffect.ChainLightning, (Role.Tech, 1), NoBuilding)),
            (Faction.Hellfire, ResearchRow("화염탄", 100, 160, 2, UpgradeEffect.FireShot, (Role.Tech, 1), NoBuilding)),
            (Faction.Hellfire, ResearchRow("피 냄새", 100, 160, 2, UpgradeEffect.BloodScent, (Role.Tech, 1), NoBuilding)),
            (Faction.Hellfire, ResearchRow("화상 오라 강화", 100, 160, 2, UpgradeEffect.BurnAuraDamage, (Role.Tech, 1), NoBuilding)),
            // 집결 반경 needs 집결의 횃불 standing, the building the aura comes from.
            (Faction.Hellfire, ResearchRow("집결 반경", 75, 120, 2, UpgradeEffect.RallyRadius, (Role.Tech, 1), (Role.Defense, 1))),
            (Faction.WaterSlimes, ResearchRow("회복의 비 강화", 100, 160, 3, UpgradeEffect.HealingRainAmount, (Role.Tech, 0), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("거품 벽", 100, 160, 2, UpgradeEffect.BubbleWall, (Role.Tech, 1), NoBuilding)),
            (Faction.RockGolems, ResearchRow("이끼 재생 강화", 100, 160, 2, UpgradeEffect.MossRegrowthAmount, (Role.Tech, 1), NoBuilding)),
            (Faction.Humans, ResearchRow("연쇄 방전", 125, 200, 2, UpgradeEffect.ChainDischarge, (Role.Tech, 1), NoBuilding)),
            (Faction.Humans, ResearchRow("증폭 반경", 100, 160, 2, UpgradeEffect.AmplifyRadius, (Role.Tech, 1), NoBuilding)),
            (Faction.Humans, ResearchRow("표식 강화", 100, 160, 2, UpgradeEffect.MarkAmount, (Role.Tech, 1), NoBuilding)),

            // docs/TECH-TREE.md 묶음 6 (#202): summons that lapse and bursts.
            (Faction.TreeSpirits, ResearchRow("과부하 강화", 75, 120, 2, UpgradeEffect.OverloadDamage, (Role.Tech, 2), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("소환 수명", 150, 200, 3, UpgradeEffect.SummonLifetime, (Role.Tech, 0), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("층위 고정 반경", 100, 160, 3, UpgradeEffect.StrataHoldRadius, (Role.Tech, 0), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("화염 분출 강화", 75, 120, 2, UpgradeEffect.FireBurstDamage, (Role.Tech, 1), NoBuilding)),

            // docs/TECH-TREE.md 묶음 7 (#203): autocasts and the water field, in
            // the order 연구 행 배치 lists them.
            (Faction.TreeSpirits, ResearchRow("질풍", 100, 160, 2, UpgradeEffect.Gale, (Role.Tech, 2), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("물 지대 방어", 150, 200, 3, UpgradeEffect.WaterFieldArmor, (Role.Tech, 0), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("급류", 100, 160, 2, UpgradeEffect.Rapids, (Role.Tech, 1), NoBuilding)),
            (Faction.RockGolems, ResearchRow("굴러 들이받기", 125, 200, 3, UpgradeEffect.RollCharge, (Role.Tech, 0), NoBuilding)),
            (Faction.RockGolems, ResearchRow("잔해 수리", 100, 160, 2, UpgradeEffect.RemnantRepair, (Role.Tech, 1), NoBuilding)),
            (Faction.RockGolems, ResearchRow("잔해 투척 강화", 100, 160, 2, UpgradeEffect.RemnantThrowDamage, (Role.Tech, 1), NoBuilding)),
            (Faction.Driftworlds, ResearchRow("정조준", 125, 200, 3, UpgradeEffect.SteadyAim, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("수리 강화", 100, 160, 3, UpgradeEffect.RepairAmount, (Role.Tech, 0), NoBuilding)),

            // docs/TECH-TREE.md 묶음 8 (#209): the actives, in the order 연구 행
            // 배치 lists them.
            (Faction.TreeSpirits, ResearchRow("칼바람 회오리", 150, 200, 3, UpgradeEffect.RazorGale, (Role.Tech, 1), NoBuilding)),
            (Faction.Hellfire, ResearchRow("유성우", 150, 200, 3, UpgradeEffect.MeteorShower, (Role.Tech, 0), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("해일 부름", 150, 200, 3, UpgradeEffect.TideCall, (Role.Tech, 0), NoBuilding)),
            (Faction.WaterSlimes, ResearchRow("물 지대 지속", 100, 160, 3, UpgradeEffect.WaterFieldDuration, (Role.Tech, 0), NoBuilding)),
            (Faction.RockGolems, ResearchRow("굳기 지속", 100, 160, 3, UpgradeEffect.HardenDuration, (Role.Tech, 0), NoBuilding)),
            (Faction.RockGolems, ResearchRow("깨우기 비용", 100, 160, 3, UpgradeEffect.AwakenCost, (Role.Tech, 0), NoBuilding)),
            (Faction.RockGolems, ResearchRow("모래폭풍 반경", 100, 160, 3, UpgradeEffect.SandStormRadius, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("전개 장치", 150, 200, 3, UpgradeEffect.Deploy, (Role.Tech, 0), NoBuilding)),
            (Faction.Humans, ResearchRow("낙뢰", 150, 200, 2, UpgradeEffect.LightningDrop, (Role.Tech, 1), NoBuilding)),
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

        /// <summary>
        /// Every faction's ability table, docs/TECH-TREE.md M11: the passive and
        /// aura rows 묶음 5 (#200) brings in, faction by faction. A faction's
        /// ability index is its row's position among that faction's rows, the
        /// way an upgrade index is. Walked in declaration order, never through a
        /// keyed collection, for the reason <see cref="growthTargets"/> is.
        ///
        /// Each row names the research that unlocks it (UnlockEffect) and the one
        /// that changes one of its numbers (BoostEffect) by effect; the static
        /// constructor resolves both into upgrade table indexes and refuses a row
        /// whose holder the roster does not field.
        /// </summary>
        private static readonly AbilityRow[] abilities =
        {
            // 세계수 정령
            // 휘감기: 덩쿨 정령, 지상 대상 40틱 감속. 휘감기 지속 60틱.
            Ability("휘감기", Faction.TreeSpirits, Role.Ranged, 1, AbilityKind.SlowOnHit, AbilityTargets.GroundBodies,
                UpgradeEffect.None, ticks: 40, boost: UpgradeEffect.EntangleDuration, boostedTicks: 60),
            // 연쇄 번개: 번개 정령, 2번 튐, 2칸.
            Ability("연쇄 번개", Faction.TreeSpirits, Role.Ranged, 0, AbilityKind.ChainOnHit, AbilityTargets.None,
                UpgradeEffect.ChainLightning, radius: 2, limit: 2),
            // 과생장: 도깨비불, 3칸, 짝수 틱.
            Ability("과생장", Faction.TreeSpirits, Role.Signature, 1, AbilityKind.GrowthAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 3, period: 2),
            // 덩굴 지대: 덩굴 군집, 3칸 적 지상.
            Ability("덩굴 지대", Faction.TreeSpirits, Role.Signature, 2, AbilityKind.SlowAura, AbilityTargets.GroundBodies,
                UpgradeEffect.None, radius: 3),
            // 회복: 회복 토템, 4칸 아군 유닛과 완공 건물, 20틱마다 +2.
            Ability("회복", Faction.TreeSpirits, Role.Defense, 1, AbilityKind.HealAura, AbilityTargets.BodiesAndBuildings,
                UpgradeEffect.None, radius: 4, amount: 2, period: 20),
            // 전격 과부하: 찌릿쥐, 죽을 때 2칸 적 지상·공중 고정 12. 과부하 강화 18. 묶음 6 (#202).
            Ability("전격 과부하", Faction.TreeSpirits, Role.Melee, 2, AbilityKind.DeathBurst, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 2, amount: 12, boost: UpgradeEffect.OverloadDamage, boostedAmount: 18),
            // 질풍: 폭풍 기수, 공격할 때마다 10틱 동안 속도 × 2. 묶음 7 (#203).
            Ability("질풍", Faction.TreeSpirits, Role.Melee, 3, AbilityKind.GaleAfterAttack, AbilityTargets.None,
                UpgradeEffect.Gale, amount: 2, ticks: 10),
            // 칼바람 회오리: 바람 정령, 6칸 안 지점, 반경 2칸 적 지상·공중 몸에 10틱마다 고정 3, 60틱. 쿨다운 400. 묶음 8 (#209).
            Cast("칼바람 회오리", Faction.TreeSpirits, Role.Signature, 0, AbilityKind.StrikeZone, AbilityTargets.Bodies,
                UpgradeEffect.RazorGale, CastTarget.Point, range: 6, cooldown: 400, radius: 2, amount: 3, ticks: 60, period: 10),

            // 지옥불 군단
            // 화상 오라: 용암 갑각 악마, 2칸 적 지상, 20틱마다 고정 2. 화상 오라 강화 3.
            Ability("화상 오라", Faction.Hellfire, Role.Melee, 0, AbilityKind.BurnAura, AbilityTargets.GroundBodies,
                UpgradeEffect.None, radius: 2, amount: 2, period: 20, boost: UpgradeEffect.BurnAuraDamage, boostedAmount: 3),
            // 화염탄: 균열 파수병, 화상 60틱.
            Ability("화염탄", Faction.Hellfire, Role.Ranged, 1, AbilityKind.BurnOnHit, AbilityTargets.None,
                UpgradeEffect.FireShot, ticks: 60),
            // 피 냄새: 균열 사냥개, 대상이 절반 아래면 주기 12 → 8.
            Ability("피 냄새", Faction.Hellfire, Role.Melee, 2, AbilityKind.WoundedRate, AbilityTargets.None,
                UpgradeEffect.BloodScent, amount: 8),
            // 집결: 집결의 횃불, 5칸 아군 유닛 주기 −3, 하한 5. 집결 반경 7칸.
            Ability("집결", Faction.Hellfire, Role.Defense, 1, AbilityKind.RateAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 5, amount: 3, limit: 5, boost: UpgradeEffect.RallyRadius, boostedRadius: 7),
            // 용암 폭발: 용암 투석 악마, 명중 시 2칸 광역, 절반 피해, 지상만. 묶음 7.
            Ability("용암 폭발", Faction.Hellfire, Role.Ranged, 2, AbilityKind.SplashOnHit, AbilityTargets.Ground,
                UpgradeEffect.None, radius: 2),
            // 유성우: 운석 술사, 8칸 안 지점, 40틱 뒤 반경 2칸 적 지상·공중·건물에 고정 30. 쿨다운 600. 묶음 8.
            Cast("유성우", Faction.Hellfire, Role.Signature, 2, AbilityKind.DelayedStrike, AbilityTargets.Everything,
                UpgradeEffect.MeteorShower, CastTarget.Point, range: 8, cooldown: 600, radius: 2, amount: 30, ticks: 40),

            // 물 슬라임
            // 물막이: 물막이 슬라임, 2칸 아군 Role.Ranged 유닛 방어 +1.
            Ability("물막이", Faction.WaterSlimes, Role.Melee, 1, AbilityKind.ArmorAura, AbilityTargets.RangedBodies,
                UpgradeEffect.None, radius: 2, amount: 1),
            // 거품 벽: 거품 정령, 2칸 다른 거품 정령 1기당 +1, 최대 +2.
            Ability("거품 벽", Faction.WaterSlimes, Role.Melee, 0, AbilityKind.ArmorPerAlly, AbilityTargets.None,
                UpgradeEffect.BubbleWall, radius: 2, amount: 1, limit: 2),
            // 회복의 비: 비구름, 3칸 아군 유닛, 20틱마다 +2. 회복의 비 강화 3.
            Ability("회복의 비", Faction.WaterSlimes, Role.Signature, 1, AbilityKind.HealAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 3, amount: 2, period: 20, boost: UpgradeEffect.HealingRainAmount, boostedAmount: 3),
            // 급류: 급류 슬라임, 물 지대 위 속도 1/2칸/틱. 묶음 7.
            Ability("급류", Faction.WaterSlimes, Role.Melee, 2, AbilityKind.FieldSpeed, AbilityTargets.None,
                UpgradeEffect.Rapids),
            // 물기둥 분수: 물 지대 위면 사거리 +1. 묶음 7.
            Ability("물 지대 사거리", Faction.WaterSlimes, Role.Defense, 0, AbilityKind.FieldRange, AbilityTargets.None,
                UpgradeEffect.None, amount: 1),
            // 이동식 물 지대: 샘물 운반자, 20틱마다 자기 셀 중심 3×3 셀 200틱. 묶음 7.
            Ability("이동식 물 지대", Faction.WaterSlimes, Role.Signature, 3, AbilityKind.FieldEmitter, AbilityTargets.None,
                UpgradeEffect.None, radius: 1, ticks: 200, period: 20),
            // 고정 물 지대: 솟는 샘, 40틱마다 중심 5×5 셀 80틱. 묶음 7.
            Ability("고정 물 지대", Faction.WaterSlimes, Role.Defense, 1, AbilityKind.FieldEmitter, AbilityTargets.None,
                UpgradeEffect.None, radius: 2, ticks: 80, period: 40),
            // 물 지대 깔기: 물방울 생존자, 자기 셀 중심 3×3 셀 300틱. 쿨다운 400. 물 지대 지속 450틱. 묶음 8.
            Cast("물 지대 깔기", Faction.WaterSlimes, Role.Worker, 0, AbilityKind.LayField, AbilityTargets.None,
                UpgradeEffect.None, CastTarget.NoTarget, cooldown: 400, radius: 1, ticks: 300,
                boost: UpgradeEffect.WaterFieldDuration, boostedTicks: 450),
            // 해일 부름: 해일 인도자, 7칸 안 지점, 반경 2칸 적 지상(몸과 건물)에 고정 20, 지점 3×3 셀 물 지대 300틱. 쿨다운 500. 묶음 8.
            // "적 지상" read as 낙뢰's is: everything on the ground, buildings included.
            Cast("해일 부름", Faction.WaterSlimes, Role.Ranged, 1, AbilityKind.TideStrike, AbilityTargets.Ground,
                UpgradeEffect.TideCall, CastTarget.Point, range: 7, cooldown: 500, radius: 2, amount: 20, ticks: 300),
            // 간헐천: 간헐천 술사, 8칸 안 물 지대 셀, 20틱 뒤 반경 2칸 적 지상·공중 몸에 고정 25. 쿨다운 300. 묶음 8.
            Cast("간헐천", Faction.WaterSlimes, Role.Signature, 2, AbilityKind.DelayedStrike, AbilityTargets.Bodies,
                UpgradeEffect.None, CastTarget.WaterFieldCell, range: 8, cooldown: 300, radius: 2, amount: 25, ticks: 20),

            // 돌 골렘 부족
            // 이끼 재생: 이끼 포자 무리, 3칸 아군 유닛, 20틱마다 +3. 이끼 재생 강화 5.
            Ability("이끼 재생", Faction.RockGolems, Role.Signature, 2, AbilityKind.HealAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 3, amount: 3, period: 20, boost: UpgradeEffect.MossRegrowthAmount, boostedAmount: 5),
            // 잔해 투척: 바위 술사, 2칸 안 잔해 하나를 소비하고 그 공격 +8, 반경 1칸 광역. 잔해 투척 강화 +12. 묶음 7.
            Ability("잔해 투척", Faction.RockGolems, Role.Ranged, 0, AbilityKind.RemnantThrow, AbilityTargets.Everything,
                UpgradeEffect.None, radius: 1, amount: 8, boost: UpgradeEffect.RemnantThrowDamage, boostedAmount: 12),
            // 잔해 수리: 바위 술사, 4칸 안 가장 많이 다친 아군(잃은 체력 30 이상) +60, 잔해 하나 소비. 쿨다운 100. 묶음 7.
            Ability("잔해 수리", Faction.RockGolems, Role.Ranged, 0, AbilityKind.RemnantRepair, AbilityTargets.Bodies,
                UpgradeEffect.RemnantRepair, cooldown: 100, radius: 4, amount: 60, limit: 30),
            // 굴러 들이받기: 구르는 바위, 대상 3~6칸이면 최대 12틱 1/2칸/틱, 끝날 때 1칸 적 지상 고정 15. 쿨다운 240. 묶음 7.
            Ability("굴러 들이받기", Faction.RockGolems, Role.Melee, 1, AbilityKind.RollCharge, AbilityTargets.GroundBodies,
                UpgradeEffect.RollCharge, cooldown: 240, radius: 1, amount: 15, ticks: 12),
            // 잔해 만료 연장: 잠듦의 고리, 20틱마다 9×9 셀의 잔해 만료를 Tick + 40까지. 묶음 7.
            Ability("잔해 만료 연장", Faction.RockGolems, Role.Defense, 2, AbilityKind.RemnantHold, AbilityTargets.None,
                UpgradeEffect.None, radius: 4, ticks: 40, period: 20),
            // 굳기: 성벽 골렘, 자신이 죽고 자기 셀과 상하좌우 4셀에 잔해 600틱. 굳기 지속 900틱. 묶음 8.
            Cast("굳기", Faction.RockGolems, Role.Melee, 2, AbilityKind.Harden, AbilityTargets.None,
                UpgradeEffect.None, CastTarget.NoTarget, ticks: 600,
                boost: UpgradeEffect.HardenDuration, boostedTicks: 900),
            // 깨우기: 이끼 현자, 6칸 안 잔해 셀, 자원 40, 잔해를 소비하고 셀 중심에 깨어난 바위. 쿨다운 200. 깨우기 비용 25. 묶음 8.
            Cast("깨우기", Faction.RockGolems, Role.Signature, 1, AbilityKind.Awaken, AbilityTargets.None,
                UpgradeEffect.None, CastTarget.RemnantCell, range: 6, cooldown: 200, cost: 40,
                boost: UpgradeEffect.AwakenCost, boostedCost: 25),
            // 모래폭풍: 모래 술사, 7칸 안 지점, 반경 3칸 100틱, 안의 적 몸 사거리 최대 3, 속도 × 1/2. 쿨다운 500. 모래폭풍 반경 4칸. 묶음 8.
            Cast("모래폭풍", Faction.RockGolems, Role.Ranged, 3, AbilityKind.SandZone, AbilityTargets.Bodies,
                UpgradeEffect.None, CastTarget.Point, range: 7, cooldown: 500, radius: 3, amount: 3, ticks: 100,
                boost: UpgradeEffect.SandStormRadius, boostedRadius: 4),

            // 차원 유랑종
            // 굳은 몸: 멸종한 바위 슬라임, 걷지 않는 동안 방어 +3.
            Ability("굳은 몸", Faction.Driftworlds, Role.Melee, 2, AbilityKind.ArmorWhileStill, AbilityTargets.None,
                UpgradeEffect.None, amount: 3),
            // 생명 기록: 멸종한 자연 슬라임, 3칸 아군 유닛, 20틱마다 +3.
            Ability("생명 기록", Faction.Driftworlds, Role.Signature, 3, AbilityKind.HealAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 3, amount: 3, period: 20),
            // 층위 고정: 층위 관측자, 4칸 아군 한시 소환, 짝수 틱마다 ExpireTick +1. 층위 고정 반경 6칸. 묶음 6 (#202).
            Ability("층위 고정", Faction.Driftworlds, Role.Signature, 2, AbilityKind.ExpiryAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 4, period: 2, boost: UpgradeEffect.StrataHoldRadius, boostedRadius: 6),
            // 화염 분출: 멸종한 화염 슬라임, 죽거나 만료될 때 2칸 적 지상 고정 12. 화염 분출 강화 18. 묶음 6.
            Ability("화염 분출", Faction.Driftworlds, Role.Melee, 1, AbilityKind.DeathBurst, AbilityTargets.GroundBodies,
                UpgradeEffect.None, radius: 2, amount: 12, boost: UpgradeEffect.FireBurstDamage, boostedAmount: 18),
            // 도착 방전: 멸종한 번개 슬라임, 도착한 자리 2칸 적 지상·공중 고정 10. 묶음 6.
            Ability("도착 방전", Faction.Driftworlds, Role.Ranged, 1, AbilityKind.ArrivalBurst, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 2, amount: 10),
            // 정조준: 틈새 사수, 40틱 걷지 않으면 사거리 +2. 묶음 7.
            Ability("정조준", Faction.Driftworlds, Role.Ranged, 0, AbilityKind.SteadyAim, AbilityTargets.None,
                UpgradeEffect.SteadyAim, amount: 2, ticks: 40),

            // 인간 마법 문명
            // 표식: 마도 정찰기, 5칸 적 유닛이 인간의 일반 공격에서 +1. 표식 강화 +2.
            Ability("표식", Faction.Humans, Role.Signature, 1, AbilityKind.MarkAura, AbilityTargets.Bodies,
                UpgradeEffect.None, radius: 5, amount: 1, boost: UpgradeEffect.MarkAmount, boostedAmount: 2),
            // 증폭: 마나 증폭기, 4칸 아군 완공 방어 건물 주기 −5, 하한 8. 증폭 반경 6칸.
            Ability("증폭", Faction.Humans, Role.Signature, 3, AbilityKind.RateAura, AbilityTargets.DefenseBuildings,
                UpgradeEffect.None, radius: 4, amount: 5, limit: 8, boost: UpgradeEffect.AmplifyRadius, boostedRadius: 6),
            // 연쇄 방전: 전기 타워, 1번 튐, 2칸.
            Ability("연쇄 방전", Faction.Humans, Role.Defense, 1, AbilityKind.ChainOnHit, AbilityTargets.None,
                UpgradeEffect.ChainDischarge, radius: 2, limit: 1),
            // 수리: 공방 기술자, 10틱마다 3칸 안 가장 많이 다친 아군 건물 또는 장치 유닛 +4. 수리 강화 6. 묶음 7.
            Ability("수리", Faction.Humans, Role.Signature, 2, AbilityKind.Repair, AbilityTargets.BuildingsAndDevices,
                UpgradeEffect.None, radius: 3, amount: 4, period: 10, boost: UpgradeEffect.RepairAmount, boostedAmount: 6),
            // 폭격: 마도 폭격선, 명중 시 2칸 광역, 절반 피해, 지상만. 묶음 7.
            Ability("폭격", Faction.Humans, Role.Ranged, 5, AbilityKind.SplashOnHit, AbilityTargets.Ground,
                UpgradeEffect.None, radius: 2),
            // 낙뢰: 마도사, 8칸 안 지점, 30틱 뒤 반경 2칸 적 지상 유닛·건물에 고정 22. 쿨다운 400. 묶음 8.
            Cast("낙뢰", Faction.Humans, Role.Ranged, 2, AbilityKind.DelayedStrike, AbilityTargets.Ground,
                UpgradeEffect.LightningDrop, CastTarget.Point, range: 8, cooldown: 400, radius: 2, amount: 22, ticks: 30),
            // 전개: 견인 대포, 목표 없음, 전환 40틱. 전개 중 속도 0, 사거리 10, 반경 1칸 광역(지상). 묶음 8.
            Cast("전개", Faction.Humans, Role.Ranged, 4, AbilityKind.Deploy, AbilityTargets.Ground,
                UpgradeEffect.Deploy, CastTarget.NoTarget, radius: 1, amount: 10, ticks: 40),
        };

        private static AbilityRow Ability(string name, Faction faction, Role role, int slot, AbilityKind kind,
            AbilityTargets targets, UpgradeEffect unlock, int radius = 0, int amount = 0, int ticks = 0,
            int period = 0, int limit = 0, UpgradeEffect boost = UpgradeEffect.None,
            int boostedRadius = 0, int boostedAmount = 0, int boostedTicks = 0, int cooldown = 0) =>
            new AbilityRow
            {
                Name = name,
                Faction = faction, Role = role, Slot = slot,
                Kind = kind, Targets = targets,
                Unlock = -1, Boost = -1,
                UnlockEffect = unlock, BoostEffect = boost,
                Radius = radius, Amount = amount, Ticks = ticks, Period = period, Limit = limit, Cooldown = cooldown,
                BoostedRadius = boostedRadius, BoostedAmount = boostedAmount, BoostedTicks = boostedTicks,
            };

        /// <summary>
        /// An active: an ability row a Cast names, docs/TECH-TREE.md 묶음 8. The
        /// same row as Ability writes, with what it is aimed at, how far, and what
        /// it costs.
        /// </summary>
        private static AbilityRow Cast(string name, Faction faction, Role role, int slot, AbilityKind kind,
            AbilityTargets targets, UpgradeEffect unlock, CastTarget target, int range = 0, int cooldown = 0,
            int cost = 0, int radius = 0, int amount = 0, int ticks = 0, int period = 0,
            UpgradeEffect boost = UpgradeEffect.None, int boostedRadius = 0, int boostedTicks = 0, int boostedCost = 0)
        {
            AbilityRow row = Ability(name, faction, role, slot, kind, targets, unlock, radius: radius, amount: amount,
                ticks: ticks, period: period, boost: boost, boostedRadius: boostedRadius, boostedTicks: boostedTicks,
                cooldown: cooldown);
            row.CastTarget = target;
            row.CastRange = range;
            row.Cost = cost;
            row.BoostedCost = boostedCost;
            return row;
        }

        /// <summary>
        /// The most hops any chain row takes. Sizes the hit list ChainOnHit keeps
        /// for one shot, and the type load refuses a row past it.
        /// </summary>
        public const int MaxChainHops = 2;

        /// <summary>
        /// Whether this kind is an aura: an effect a holder gives the bodies
        /// standing around it, read through World's aura source list, rather
        /// than one it lands with its own shot or carries on itself.
        /// </summary>
        public static bool IsAura(AbilityKind kind) =>
            kind == AbilityKind.ArmorAura || kind == AbilityKind.MarkAura || kind == AbilityKind.RateAura ||
            kind == AbilityKind.HealAura || kind == AbilityKind.BurnAura || kind == AbilityKind.GrowthAura ||
            kind == AbilityKind.SlowAura || kind == AbilityKind.ExpiryAura;

        /// <summary>
        /// Whether this kind keeps state on the body that holds it, in
        /// Entity.AbilityCooldown or Entity.AbilityTicksLeft. docs/TECH-TREE.md
        /// 능력 공통 규칙: a body holds at most one, and the type load refuses a
        /// second, because the two fields would then mean two things at once.
        /// </summary>
        public static bool IsStateful(AbilityKind kind) =>
            kind == AbilityKind.GaleAfterAttack || kind == AbilityKind.RollCharge ||
            kind == AbilityKind.RemnantRepair || kind == AbilityKind.SteadyAim ||
            (IsCast(kind) && kind != AbilityKind.Harden);

        /// <summary>
        /// The kinds that wait on AbilityCooldown, and so the only rows that carry
        /// a Cooldown: the two autocasts and every active but 굳기, which ends its
        /// caster, and 전개, which waits on its own transition instead.
        /// </summary>
        public static bool HasCooldown(AbilityKind kind) =>
            kind == AbilityKind.RollCharge || kind == AbilityKind.RemnantRepair ||
            (IsCast(kind) && kind != AbilityKind.Harden && kind != AbilityKind.Deploy);

        /// <summary>The kinds a Cast names: the actives of docs/TECH-TREE.md 묶음 8.</summary>
        public static bool IsCast(AbilityKind kind) =>
            kind >= AbilityKind.StrikeZone && kind <= AbilityKind.Deploy;

        /// <summary>
        /// The actives that run a clock in AbilityTicksLeft after the Cast: the
        /// delays, the zones and the transition. The instant ones are done inside
        /// Apply and leave the clock alone.
        /// </summary>
        public static bool CastRunsClock(AbilityKind kind) =>
            kind == AbilityKind.StrikeZone || kind == AbilityKind.DelayedStrike ||
            kind == AbilityKind.SandZone || kind == AbilityKind.Deploy;

        // One bit per AbilityKind, per faction: whether any row of the faction
        // holds the kind. Filled by ResolveAbilities. World asks it of a body
        // before it scans for a 모래폭풍 or a 전개 row, so a match with neither
        // faction in it pays one mask test and no scan.
        private static readonly ulong[] kindMasks = new ulong[FactionCount];

        /// <summary>Whether any row of this faction's ability table is of this kind.</summary>
        public static bool FactionHolds(Faction faction, AbilityKind kind) =>
            (kindMasks[(int)faction] & (1UL << (int)kind)) != 0;

        /// <summary>
        /// The one row with state this entry holds, or -1. An entry holds at
        /// most one, which the type load checks.
        /// </summary>
        public static int StatefulAbilityOf(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityRow row = abilities[i];
                if (IsStateful(row.Kind) && row.Faction == faction && row.Role == role && row.Slot == slot) return i;
            }
            return -1;
        }

        /// <summary>
        /// 수리's 장치 유닛, docs/TECH-TREE.md 인간 능력 표: Towerback, 석조 보행
        /// 포대, 견인 대포, 마도 폭격선, 마도 정찰기, 마나 증폭기. Units that 수리
        /// mends the way it mends a building. Walked in declaration order.
        /// </summary>
        private static readonly (Faction Faction, Role Role, int Slot)[] devices =
        {
            (Faction.Humans, Role.Signature, 0), // Towerback
            (Faction.Humans, Role.Ranged, 3),    // 석조 보행 포대
            (Faction.Humans, Role.Ranged, 4),    // 견인 대포
            (Faction.Humans, Role.Ranged, 5),    // 마도 폭격선
            (Faction.Humans, Role.Signature, 1), // 마도 정찰기
            (Faction.Humans, Role.Signature, 3), // 마나 증폭기
        };

        /// <summary>Whether this entry is a 장치 유닛 수리 mends.</summary>
        public static bool IsDevice(Faction faction, Role role, int slot)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].Faction == faction && devices[i].Role == role && devices[i].Slot == slot) return true;
            }
            return false;
        }

        /// <summary>How many entries the device table holds.</summary>
        public static int DeviceCount => devices.Length;

        /// <summary>One entry of the device table, in declaration order.</summary>
        public static (Faction Faction, Role Role, int Slot) Device(int index) => devices[index];

        /// <summary>How many rows this faction's ability table holds.</summary>
        public static int AbilityCount(Faction faction)
        {
            int count = 0;
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i].Faction == faction) count++;
            }
            return count;
        }

        /// <summary>
        /// One row of this faction's ability table, by index. An index outside
        /// the table answers a default row with Kind None, which does nothing.
        /// </summary>
        public static AbilityRow Ability(Faction faction, int index)
        {
            if (index >= 0)
            {
                for (int i = 0; i < abilities.Length; i++)
                {
                    if (abilities[i].Faction != faction) continue;
                    if (index-- == 0) return abilities[i];
                }
            }
            return new AbilityRow { Name = "", Unlock = -1, Boost = -1 };
        }

        /// <summary>How many rows the whole ability table holds, every faction together.</summary>
        public static int AbilityRowCount => abilities.Length;

        /// <summary>One row of the whole ability table, in declaration order.</summary>
        public static AbilityRow AbilityRowAt(int index) => abilities[index];

        /// <summary>
        /// The row of the whole table this entry holds with this kind, or -1. A
        /// linear scan in declaration order; an entry holds at most one row of a
        /// kind, which the type load checks.
        /// </summary>
        public static int AbilityOf(Faction faction, Role role, int slot, AbilityKind kind)
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityRow row = abilities[i];
                if (row.Kind == kind && row.Faction == faction && row.Role == role && row.Slot == slot) return i;
            }
            return -1;
        }

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

            // After the overrides, so a row that keeps the shared stats still
            // lapses. A body that lapses has to walk on the field and be spawned
            // through SpawnUnit, where its clock is set: a building or a worker
            // never reaches that line, and neither does a body that grows into
            // its row, which RequireGrowthSources refuses.
            for (int i = 0; i < lifetimes.Length; i++)
            {
                var (faction, role, slot, ticks) = lifetimes[i];
                RequireFielded(faction, role, slot, "lifetimes");
                Require(!IsBuilding(role) && role != Role.Worker, "lifetimes row " + i + " is not a combat body");
                Require(ticks > 0, "lifetimes row " + i + " lapses at once");
                if (slot == 0) stats[Index(faction, role)].Lifetime = ticks;
                else extraStats[ExtraIndex(faction, role, slot)].Lifetime = ticks;
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

            RequireMechanicTables();
            ResolveAbilities();
            for (int i = 0; i < devices.Length; i++)
            {
                RequireFielded(devices[i].Faction, devices[i].Role, devices[i].Slot, "devices");
                Require(!IsBuilding(devices[i].Role) && devices[i].Role != Role.Worker, "devices row " + i + " is not a unit");
            }
        }

        /// <summary>
        /// Turns each ability row's Unlock and Boost effects into upgrade table
        /// indexes, and refuses at type load a row whose holder the roster does
        /// not field, whose research the faction cannot do, whose numbers cannot
        /// act, or that gives one entry two rows of one kind.
        /// </summary>
        private static void ResolveAbilities()
        {
            for (int i = 0; i < abilities.Length; i++)
            {
                AbilityRow row = abilities[i];
                string where = "abilities row " + i + " (" + row.Name + ")";
                RequireFielded(row.Faction, row.Role, row.Slot, "abilities");
                Require(row.Kind != AbilityKind.None, where + " does nothing");
                Require(HasCooldown(row.Kind) == (row.Cooldown > 0),
                    where + " has a cooldown of " + row.Cooldown + " on a kind that " +
                    (HasCooldown(row.Kind) ? "waits on one" : "never counts one down"));
                abilities[i].Unlock = UpgradeIndexOf(row.Faction, row.UnlockEffect, where);
                abilities[i].Boost = UpgradeIndexOf(row.Faction, row.BoostEffect, where);
                Require((int)row.Kind < 64, where + " has a kind past the mask");
                kindMasks[(int)row.Faction] |= 1UL << (int)row.Kind;
                Require((row.BoostEffect == UpgradeEffect.None) ==
                        (row.BoostedRadius == 0 && row.BoostedAmount == 0 && row.BoostedTicks == 0 && row.BoostedCost == 0),
                    where + " has a boost that changes nothing, or a boosted number nothing raises");
                RequireCast(row, where);
                // An aura, a chain and 거품 벽 reach out from the holder, so each
                // needs a radius; an aura also says whom it reaches.
                // A burst goes off around where its holder stood, so it needs
                // both as well (묶음 6).
                // So does every 묶음 7 kind that lands on bodies or cells
                // around its holder, and each of those that lands on bodies
                // says which.
                bool burst = row.Kind == AbilityKind.DeathBurst || row.Kind == AbilityKind.ArrivalBurst;
                bool around = row.Kind == AbilityKind.SplashOnHit || row.Kind == AbilityKind.RemnantThrow ||
                              row.Kind == AbilityKind.RemnantRepair || row.Kind == AbilityKind.RollCharge ||
                              row.Kind == AbilityKind.Repair;
                bool cells = row.Kind == AbilityKind.FieldEmitter || row.Kind == AbilityKind.RemnantHold;
                bool reaches = IsAura(row.Kind) || burst || around || cells || row.Kind == AbilityKind.ChainOnHit ||
                               row.Kind == AbilityKind.ArmorPerAlly;
                Require(!reaches || row.Radius > 0, where + " reaches nothing");
                Require(!(IsAura(row.Kind) || burst || around) || row.Targets != AbilityTargets.None, where + " names no targets");
                // Every kind that adds, heals, burns or sets a period carries its
                // amount in the row. The exceptions carry it elsewhere: 과생장 and
                // 덩굴 지대 act by being in range, a chain halves the shot it
                // follows, and the slow and the burn a hit leaves are World
                // constants (SlowFactor, BurnDamage), because the body carrying
                // the clock does not remember which row set it.
                // 층위 고정 puts a clock back one tick by definition.
                // A splash halves the shot it follows, 급류 walks at a World
                // constant, and a field or a debris hold writes cells, not hp.
                // An active says what it needs in RequireCast.
                bool amountless = IsCast(row.Kind) || row.Kind == AbilityKind.GrowthAura || row.Kind == AbilityKind.SlowAura ||
                                  row.Kind == AbilityKind.ChainOnHit || row.Kind == AbilityKind.SlowOnHit ||
                                  row.Kind == AbilityKind.BurnOnHit || row.Kind == AbilityKind.ExpiryAura ||
                                  row.Kind == AbilityKind.SplashOnHit || row.Kind == AbilityKind.FieldSpeed ||
                                  row.Kind == AbilityKind.FieldEmitter || row.Kind == AbilityKind.RemnantHold;
                Require(amountless || row.Amount > 0, where + " has no amount");
                if (row.Kind == AbilityKind.SlowOnHit || row.Kind == AbilityKind.BurnOnHit ||
                    row.Kind == AbilityKind.GaleAfterAttack || row.Kind == AbilityKind.RollCharge ||
                    row.Kind == AbilityKind.SteadyAim || cells)
                    Require(row.Ticks > 0, where + " lasts no ticks");
                if (row.Kind == AbilityKind.RemnantRepair)
                    Require(row.Limit > 0, where + " mends a body that lost nothing");
                if (row.Kind == AbilityKind.ChainOnHit)
                    Require(row.Limit > 0 && row.Limit <= MaxChainHops, where + " hops " + row.Limit + " times");
                if (row.Kind == AbilityKind.RateAura || row.Kind == AbilityKind.ArmorPerAlly)
                    Require(row.Limit > 0, where + " has no floor or cap");
                if (row.Kind == AbilityKind.HealAura || row.Kind == AbilityKind.BurnAura || row.Kind == AbilityKind.GrowthAura ||
                    row.Kind == AbilityKind.ExpiryAura || row.Kind == AbilityKind.Repair || cells)
                    Require(row.Period > 0, where + " has no period");
                // A body that holds a ticking or cooling ability walks or shoots
                // with it, so it has to be a body on the field; a field or a debris
                // hold is laid from where its holder stands, either kind. A worker
                // counts since 묶음 8: 물방울 생존자 lays its field with a Cast and
                // waits on the cooldown like any other caster.
                if (IsStateful(row.Kind))
                    Require(!IsBuilding(row.Role) && (row.Role != Role.Worker || IsCast(row.Kind)),
                        where + " is held by no combat body");
                for (int j = 0; j < i; j++)
                {
                    Require(abilities[j].Faction != row.Faction || abilities[j].Role != row.Role ||
                            abilities[j].Slot != row.Slot || abilities[j].Kind != row.Kind,
                        where + " gives " + row.Faction + " " + row.Role + " " + row.Slot + " a second " + row.Kind);
                    Require(!IsStateful(row.Kind) || !IsStateful(abilities[j].Kind) || abilities[j].Faction != row.Faction ||
                            abilities[j].Role != row.Role || abilities[j].Slot != row.Slot,
                        where + " gives " + row.Faction + " " + row.Role + " " + row.Slot + " a second ability with state");
                }
            }
        }

        /// <summary>
        /// docs/TECH-TREE.md 묶음 8: what an active needs to be cast, and what a
        /// row that is not an active may not carry. Every active is aimed at
        /// nothing or at a point; a point row reaches some way (CastRange), and
        /// one that hits an area has a radius, an amount and the bodies it hits.
        /// The cooldown is HasCooldown's question, asked by the caller.
        /// </summary>
        private static void RequireCast(AbilityRow row, string where)
        {
            bool cast = IsCast(row.Kind);
            Require(cast == (row.CastTarget != CastTarget.None), where + (cast
                ? " is an active that says nothing about what it is aimed at"
                : " is aimed by a Cast but is not an active"));
            if (!cast)
            {
                Require(row.CastRange == 0 && row.Cost == 0 && row.BoostedCost == 0,
                    where + " has a cast range or a cost and no Cast");
                return;
            }
            bool point = row.CastTarget != CastTarget.NoTarget;
            Require(point == (row.CastRange > 0),
                where + (point ? " is aimed at a point no distance away" : " has a cast range and no point"));
            bool area = row.Kind == AbilityKind.StrikeZone || row.Kind == AbilityKind.DelayedStrike ||
                        row.Kind == AbilityKind.TideStrike || row.Kind == AbilityKind.SandZone;
            if (area)
            {
                Require(row.Radius > 0, where + " hits an area of no radius");
                Require(row.Targets != AbilityTargets.None, where + " hits an area and names no targets");
                Require(row.Amount > 0, where + " has no amount");
            }
            if (row.Kind == AbilityKind.StrikeZone) Require(row.Period > 0, where + " has no period");
            if (row.Kind == AbilityKind.LayField) Require(row.Radius > 0, where + " lays no cells");
            if (row.Kind == AbilityKind.Deploy)
                Require(row.Amount > 0 && row.Radius > 0 && row.Targets != AbilityTargets.None,
                    where + " deploys to no reach or no splash");
            if (row.Kind != AbilityKind.Awaken) Require(row.Ticks > 0, where + " lasts no ticks");
            Require((row.Kind == AbilityKind.Awaken) == (row.CastTarget == CastTarget.RemnantCell),
                where + " wakes debris it is not aimed at, or is aimed at debris it does not wake");
            Require(row.Cost >= 0 && row.BoostedCost >= 0, where + " pays resources back");
        }

        /// <summary>The index of this faction's upgrade row with this effect, -1 for None; refuses a missing row.</summary>
        private static int UpgradeIndexOf(Faction faction, UpgradeEffect effect, string where)
        {
            if (effect == UpgradeEffect.None) return -1;
            for (int u = 0; u < UpgradeCount(faction); u++)
            {
                if (Upgrade(faction, u).Effect == effect) return u;
            }
            Require(false, where + " names " + effect + ", which " + faction + " cannot research");
            return -1;
        }

        /// <summary>
        /// The mechanic tables of docs/TECH-TREE.md 묶음 4, checked the way the
        /// tech tree tables above are: every entry they name is fielded, and every
        /// research row they read is one the faction can research. Called from the
        /// static constructor.
        /// </summary>
        private static void RequireMechanicTables()
        {
            for (int i = 0; i < spawners.Length; i++)
            {
                SpawnerRow row = spawners[i];
                RequireFielded(row.Faction, row.Role, row.Slot, "spawners");
                RequireFielded(row.Faction, row.ChildRole, row.ChildSlot, "spawners");
                Require(!IsBuilding(row.Role) && !IsBuilding(row.ChildRole), "spawners row " + i + " names a building");
                Require(row.Ticks > 0 && row.Cap > 0, "spawners row " + i + " never emits");
                RequireResearchable(row.Faction, row.TicksEffect, "spawners");
                RequireResearchable(row.Faction, row.CapEffect, "spawners");
                for (int j = 0; j < i; j++)
                {
                    Require(spawners[j].Faction != row.Faction || spawners[j].Role != row.Role || spawners[j].Slot != row.Slot,
                        "spawners lists " + row.Faction + " " + row.Role + " " + row.Slot + " twice");
                }
            }
            RequireArrivals();
            RequireGrowthSources();
            RequireFreeQueues();
            for (int i = 0; i < remnantShapes.Length; i++)
            {
                var row = remnantShapes[i];
                RequireFielded(row.Faction, row.Role, row.Slot, "remnantShapes");
                Require(!IsBuilding(row.Role) && row.Cells.Length > 0, "remnantShapes row " + i + " drops nothing");
            }
        }

        private static void RequireGrowthSources()
        {
            for (int i = 0; i < growthTargets.Length; i++)
            {
                var (faction, role, slot, cost) = growthTargets[i];
                RequireFielded(faction, role, slot, "growthTargets");
                RequireFielded(faction, cost.FromRole, cost.FromSlot, "growthTargets");
                Require(!IsBuilding(role) && !IsBuilding(cost.FromRole), "growthTargets row " + i + " names a building");
                Require(cost.FromRole != role || cost.FromSlot != slot, "growthTargets row " + i + " grows into itself");
                // A spawner's clock is armed in SpawnUnit, which a growth never
                // passes through, so a body that grew into one would never emit.
                Require(!Spawner(faction, role, slot).Spawns, "growthTargets row " + i + " grows into a spawner");
                // ExpireTick is set in SpawnUnit too, so a body that grew into a
                // row that lapses would live for ever.
                Require(Stats(faction, role, slot).Lifetime == 0, "growthTargets row " + i + " grows into a summon that lapses");
            }
        }

        private static void RequireFreeQueues()
        {
            for (int i = 0; i < freeQueues.Length; i++)
            {
                FreeQueueRow row = freeQueues[i];
                RequireFielded(row.Faction, row.Role, row.Slot, "freeQueues");
                RequireFielded(row.Faction, row.GiveRole, row.GiveSlot, "freeQueues");
                Require(IsBuilding(row.Role) && !IsBuilding(row.GiveRole), "freeQueues row " + i + " is not a building giving a body");
                Require(Production(row.Faction, row.GiveRole, row.GiveSlot).Ticks > 0, "freeQueues row " + i + " gives an untimed body");
                Require(row.Radius > 0, "freeQueues row " + i + " has no radius");
                // Nothing may be produced at a free-queue building: its queue holds
                // only the free body, so a cancel never has a paid order to refund
                // and ReturnSeed never meets a queue of another entry.
                for (int r = 0; r < RoleCount; r++)
                {
                    var role = (Role)r;
                    if (IsBuilding(role)) continue;
                    for (int slot = 0; slot < SlotCount(row.Faction, role); slot++)
                    {
                        Require(!IsProducedAt(row.Faction, role, slot, row.Role, row.Slot),
                            "freeQueues row " + i + " is a building " + role + " " + slot + " is produced at");
                    }
                }
                RequireResearchable(row.Faction, row.RadiusEffect, "freeQueues");
            }
        }

        private static void RequireArrivals()
        {
            for (int i = 0; i < arrivals.Length; i++)
            {
                ArrivalRow row = arrivals[i];
                RequireFielded(row.Faction, row.Role, row.Slot, "arrivals");
                Require(!row.Passage || IsBuilding(row.Role), "arrivals makes a body a 통로 at row " + i);
                Require(!row.Passage || row.Radius == 0, "arrivals row " + i + " is a 통로 and an anchor");
                Require(row.Radius >= 0 && (row.RadiusEffect == UpgradeEffect.None || row.RadiusResearched > row.Radius),
                    "arrivals row " + i + " has a radius research that does not widen it");
                RequireResearchable(row.Faction, row.RadiusEffect, "arrivals");
                for (int j = 0; j < i; j++)
                {
                    Require(arrivals[j].Faction != row.Faction || arrivals[j].Role != row.Role || arrivals[j].Slot != row.Slot,
                        "arrivals lists " + row.Faction + " " + row.Role + " " + row.Slot + " twice");
                }
            }
        }

        private static void RequireResearchable(Faction faction, UpgradeEffect effect, string table)
        {
            if (effect == UpgradeEffect.None) return;
            bool listed = false;
            for (int u = 0; u < UpgradeCount(faction); u++)
            {
                if (Upgrade(faction, u).Effect == effect) listed = true;
            }
            Require(listed, table + " names " + effect + ", which " + faction + " cannot research");
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
