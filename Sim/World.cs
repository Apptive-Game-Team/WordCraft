using System.Collections.Generic;

namespace WordCraft.Sim
{
    public enum EntityKind
    {
        Unit = 0,
        Worker = 1,
        ResourceNode = 2,
        Building = 3,
    }

    /// <summary>
    /// The standing order an entity is under. Exactly one at a time: issuing any
    /// order clears the rest, so a stale attack target can never outlive the order
    /// that replaced it. Values are hashed as entity state; never renumber them.
    /// </summary>
    public enum OrderMode
    {
        /// <summary>No standing order. Combat acquires and chases on its own.</summary>
        None = 0,

        /// <summary>Kill TargetId, wherever it goes, until it dies or a new order arrives.</summary>
        Attack = 1,

        /// <summary>Walk to OrderPoint, break off for anything hostile found on the way.</summary>
        AttackMove = 2,

        /// <summary>Do not move for any reason. Shoot whatever comes inside weapon range.</summary>
        Hold = 3,
    }

    /// <summary>
    /// What one map cell is made of. One byte per cell, fixed for the whole match
    /// and decided when the map is built. Values are hashed as world state; never
    /// renumber them. Open is 0 so a world nobody painted is all open ground,
    /// which is what every harness fixture relies on.
    /// </summary>
    // ponytail: passable or not, and nothing else. No movement cost, no elevation,
    // no vision blocking. Adding a cost is the one that reaches furthest: the
    // pathfinder's uniform step cost of 1 and its Manhattan heuristic are only
    // admissible while every open cell costs the same.
    public enum TileKind : byte
    {
        Open = 0,
        Water = 1,
        Rock = 2,
    }

    /// <summary>
    /// One flat struct for every kind of entity. A worker leaves the building
    /// fields at zero and vice versa; that costs a few bytes and saves a type
    /// hierarchy whose fields would have to be hashed one by one anyway.
    /// </summary>
    public struct Entity
    {
        public int Id;
        public int Owner;
        public bool Alive;
        public EntityKind Kind;

        /// <summary>Roster slot. Every stat this entity fights with is looked up from it.</summary>
        public Role Role;

        /// <summary>
        /// Which entry of that slot's list this body is. Zero on everything the
        /// roster lists once, which is most of it. Carried rather than derived,
        /// because there is nothing to derive it from: two 지옥불 ranged units
        /// stand on the same map under the same role and one of them flies.
        ///
        /// Hashed, and it has to be. Two peers that disagreed here would run the
        /// same entity id with different hp, different reach, and different
        /// terrain rules, and every tick after that is a different match.
        /// </summary>
        public int Slot;

        public FixVec2 Position;
        public FixVec2 Target;
        public Fix Speed;
        public int Hp;
        public int MaxHp;

        // Economy. Resource is the amount left in a node; the rest is worker state.
        public int Resource;
        public int CarryAmount;
        public int GatherNodeId;
        public int DropOffId;
        public int GatherTicksLeft;

        // Building.
        public int BuildTicksLeft;
        public int ProduceTicksLeft;
        public int QueueCount;

        /// <summary>What the queue is building. Set at queue time, read at spawn time.</summary>
        public Role ProduceRole;

        /// <summary>
        /// Which entry of ProduceRole the queue is building. Set and read beside
        /// ProduceRole, and hashed for the same reason: it decides what comes off
        /// the queue and what a cancel refunds, and the two numbers only mean
        /// anything together.
        /// </summary>
        public int ProduceSlot;

        /// <summary>Where a unit this building finishes walks to. Only read when HasRallyPoint.</summary>
        public FixVec2 RallyPoint;

        /// <summary>
        /// Explicit rather than a sentinel coordinate, because every point on the
        /// map is a point a player can legitimately rally to.
        /// </summary>
        public bool HasRallyPoint;

        // Combat. All timing in whole ticks.
        public int AttackCooldown;
        public int TargetId;

        /// <summary>Which standing order the tick systems read this entity under.</summary>
        public OrderMode Mode;

        /// <summary>
        /// Where an AttackMove is headed. Kept apart from Target because pursuit
        /// overwrites Target, and the order has to remember what to resume toward.
        /// </summary>
        public FixVec2 OrderPoint;

        // Cursor into this entity's path. At or past the path length means the
        // entity walks straight at Target instead.
        public int PathIndex;

        // 지옥불 군단장 주기 소환. Zero on everything that is not a warlord or one
        // of its offspring, which is every entity in five factions out of six.

        /// <summary>Ticks left before this warlord emits its next offspring.</summary>
        public int SpawnCooldown;

        /// <summary>
        /// How many of this warlord's offspring are still standing. Carried rather
        /// than counted, because counting means a scan of every entity per warlord
        /// per tick and a scan is a place iteration order can reach the result.
        /// </summary>
        public int OffspringCount;

        /// <summary>
        /// The warlord that emitted this body, or -1 for everything that was not
        /// emitted by one. It is what lets a death give its slot back without
        /// looking for the parent.
        /// </summary>
        public int ParentId;
    }

    /// <summary>
    /// The whole simulation. Advances only through Tick(), only by whole ticks,
    /// and only from integer inputs. Nothing here may touch wall-clock time,
    /// floating point, unordered collections, or UnityEngine.
    /// </summary>
    public sealed partial class World
    {
        public const int TicksPerSecond = 20;

        // The map is a fixed grid so pathfinding scratch is sized once and cell
        // indices hash as plain integers.
        public const int GridSize = 64;
        public const int GridCells = GridSize * GridSize;
        public const int MaxPeers = 8;

        // Economy constants. Per-entity numbers live in FactionData instead.
        public const int NodeHp = 1;
        public const int GatherTicks = 20;
        public const int CarryCapacity = 10;
        // The baseline production row, and only that. What a given faction's unit
        // costs comes from FactionData.Production; these two are what a roster row
        // that says nothing else falls back to.
        public const int ProduceCost = FactionData.DefaultProduceCost;
        public const int ProduceTicks = FactionData.DefaultProduceTicks;
        public const int MaxQueue = 5;

        // Population, per docs/FACTIONS.md. The hard cap is an absolute ceiling,
        // not a sum: enough supply buildings must not push a peer past it.
        public const int PopulationPerBase = 10;
        public const int PopulationPerSupply = 8;
        public const int PopulationHardCap = 200;

        public static readonly Fix InteractRange = Fix.FromInt(2);
        public static readonly Fix AcquireRange = Fix.FromInt(10);

        /// <summary>
        /// 돌 골렘 부족 잔해: how long the debris a fallen golem leaves blocks the
        /// ground, in whole ticks. Twenty seconds, per docs/FACTION-MECHANICS.md.
        /// </summary>
        public const int RemnantTicks = 400;

        /// <summary>
        /// 지옥불 군단장: whole ticks between one offspring and the next, per
        /// docs/FACTION-MECHANICS.md. The clock runs whether or not there is room,
        /// so killing an offspring buys the attacker up to this long before the
        /// slot is filled again.
        /// </summary>
        public const int WarlordSpawnTicks = 100;

        /// <summary>Most offspring one 지옥불 군단장 keeps alive at once.</summary>
        public const int WarlordOffspring = 5;

        /// <summary>Where a finished unit appears, relative to its building. Fixed, so peers agree.</summary>
        public static readonly FixVec2 RallyOffset = new FixVec2(Fix.FromInt(2), Fix.Zero);

        public int Tick { get; private set; }

        /// <summary>True once a peer has lost its last base. Both peers see it on the same tick.</summary>
        public bool MatchOver { get; private set; }

        /// <summary>Winning peer, or -1 while the match runs and on a mutual kill.</summary>
        public int Winner { get; private set; } = -1;

        public readonly DetRandom Random;

        // Append-only, indexed by id. Dead entities keep their slot so ids never
        // get reused and iteration order stays stable on every peer. paths is
        // parallel to entities and grows with it.
        private readonly List<Entity> entities = new List<Entity>();
        private readonly List<List<int>> paths = new List<List<int>>();
        private readonly int[] resources = new int[MaxPeers];
        private readonly Faction[] factions = new Faction[MaxPeers];

        // Population used. Carried as state rather than recounted each tick, so it
        // is hashed and has to be released exactly where a body dies.
        private readonly int[] population = new int[MaxPeers];

        private readonly List<Command> tickCommands = new List<Command>();

        // The terrain layer. A byte per cell rather than a tile object: it is read
        // on every pathfinding step and hashed on every tick, and neither wants an
        // indirection. Written only while the map is being built.
        private readonly byte[] terrain = new byte[GridCells];

        // 돌 골렘 부족 잔해. The tick each cell's debris lapses on, or 0 for a cell
        // that never had any. Hashed: it is what every path is computed against,
        // so two peers that disagreed here would route two different armies.
        //
        // A grid rather than an entity because debris is not a body: it has no hp,
        // nothing may target it, and an entity would have to be skipped by target
        // acquisition, the victory scan, and the population count alike. A grid is
        // also what the pathfinder already asks, through IsPassable, so neither its
        // neighbour order nor its tie-breaking has to move.
        private readonly int[] remnantExpiry = new int[GridCells];

        public int EntityCount => entities.Count;
        public Entity GetEntity(int id) => entities[id];
        public int GetResources(int peer) => resources[peer];
        public Faction FactionOf(int peer) => factions[peer];
        public int GetPopulation(int peer) => population[peer];

        /// <summary>
        /// What this peer's standing buildings support right now. Derived from
        /// entity state on demand, so losing a supply building lowers the cap on
        /// the tick it falls without a second copy of the number to keep in step.
        /// </summary>
        public int PopulationCap(int peer)
        {
            int cap = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Kind != EntityKind.Building || e.Owner != peer) continue;
                if (e.BuildTicksLeft > 0) continue; // a site under construction supports nobody
                if (e.Role == Role.Base) cap += PopulationPerBase;
                else if (e.Role == Role.Supply) cap += PopulationPerSupply;
            }
            return cap > PopulationHardCap ? PopulationHardCap : cap;
        }

        /// <summary>Workers count against the cap; buildings and resource nodes do not.</summary>
        private static bool CountsAgainstPopulation(EntityKind kind) =>
            kind == EntityKind.Unit || kind == EntityKind.Worker;

        /// <summary>Called wherever a body stops existing, which is the only place combat kills one.</summary>
        private void ReleasePopulation(Entity e)
        {
            if (!CountsAgainstPopulation(e.Kind)) return;
            if (e.Owner < 0 || e.Owner >= MaxPeers) return;
            population[e.Owner]--;
        }

        /// <summary>
        /// The one place a body stops existing. Everything that has to happen
        /// exactly once per death goes through here, so a second way to kill
        /// something cannot get one of them wrong.
        /// </summary>
        private void Kill(ref Entity e)
        {
            e.Hp = 0;
            e.Alive = false;
            ReleasePopulation(e);
            ReleaseOffspringSlot(e);
            DropRemnant(e);
        }

        /// <summary>
        /// 돌 골렘 부족 잔해: a fallen 돌 골렘 combat unit leaves rock debris on the
        /// cell it fell on, blocking the ground for RemnantTicks. It blocks its own
        /// side too. That is the mechanic, not a bug: where a 돌 골렘 player chooses
        /// to fight is a heavier decision than it is for anyone else, because the
        /// terrain the fight leaves behind was made of their own bodies.
        ///
        /// Dropped from inside the combat system's pass, which is where deaths
        /// happen, so creation and expiry both land at one fixed point in the
        /// system order and two peers repath against the same map on the same tick.
        /// </summary>
        private void DropRemnant(Entity e)
        {
            if (e.Kind != EntityKind.Unit) return; // workers and buildings leave nothing
            if (e.Owner < 0 || e.Owner >= MaxPeers) return;
            if (factions[e.Owner] != Faction.RockGolems) return;
            // Overwrites rather than stacks: a second body on one cell resets the
            // clock, and a count nobody can see is a count nobody can play around.
            remnantExpiry[CellOf(e.Position)] = Tick + RemnantTicks;
        }

        public TileKind TerrainAt(int cell) => (TileKind)terrain[cell];

        /// <summary>
        /// Scenario setup only, and only before the first tick. Terrain is hashed,
        /// so a peer that repainted a cell mid-match would desync every other peer.
        /// </summary>
        public void SetTerrain(int x, int y, TileKind kind) => terrain[y * GridSize + x] = (byte)kind;

        /// <summary>
        /// Whether a mover may occupy this cell. Terrain is a wall or it is
        /// nothing: there is no movement cost, so a route is decided by length
        /// alone and the pathfinder stays on integer costs.
        ///
        /// air is the seam docs/FACTION-MECHANICS.md asks for ("공중 유닛은 지형과
        /// 잔해를 무시하고 이동한다"). There are no air units yet, so every caller
        /// passes false; the parameter exists so the rule has one place to land
        /// rather than being retrofitted into every movement path later.
        ///
        /// Debris is tested here, in the one place both the pathfinder and the
        /// mover already ask, so a route and the step that walks it can never
        /// disagree about what is standing in the way.
        /// </summary>
        public bool IsPassable(int cell, bool air) =>
            air || (terrain[cell] == (byte)TileKind.Open && remnantExpiry[cell] <= Tick);

        /// <summary>
        /// The roster row this body fights from: its owner's faction, its role,
        /// and its own entry of that role. One accessor rather than the lookup
        /// written out at each call site, so no system can ask by role alone and
        /// quietly get entry 0's numbers for entry 1's body.
        ///
        /// Owner is the caller's to check. Every caller here already does, because
        /// a resource node has no faction to look anything up in.
        /// </summary>
        private UnitStats RosterStats(Entity e) => FactionData.Stats(factions[e.Owner], e.Role, e.Slot);

        /// <summary>
        /// True when this entity flies. Read off the roster rather than carried on
        /// the entity: it never changes for a given faction, role and entry, so
        /// making it state would add a hashed field that can only ever hold one
        /// value and give two peers something new to disagree about.
        ///
        /// The entry is what makes that true. 지옥불's ranged entry 0 is 자손 and
        /// flies; entry 1 is 균열 파수병 and walks, and terrain stops it.
        /// </summary>
        public bool Flies(Entity e) =>
            e.Owner >= 0 && e.Owner < MaxPeers && RosterStats(e).Air;

        /// <summary>The tick this cell's debris lapses on, or 0 when it never had any.</summary>
        public int RemnantExpiry(int cell) => remnantExpiry[cell];

        /// <summary>True while debris still stands on this cell.</summary>
        public bool HasRemnant(int cell) => remnantExpiry[cell] > Tick;

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void GrantResources(int peer, int amount) => resources[peer] += amount;

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void SetPeerFaction(int peer, Faction faction) => factions[peer] = faction;

        public World(ulong seed)
        {
            Random = new DetRandom(seed);
        }

        /// <summary>
        /// Entry 0 of the role, which is what a scenario laying out a starting
        /// army means and what every faction with a single-entry slot has.
        ///
        /// hpOverride exists for harnesses that need to seed a specific divergence;
        /// -1 means "take the roster value", which is what gameplay always passes.
        /// </summary>
        public int SpawnUnit(int owner, Role role, FixVec2 position, int hpOverride = -1) =>
            SpawnUnit(owner, role, 0, position, hpOverride);

        /// <summary>
        /// One named entry of a role. The slot is written onto the body, so what
        /// this unit fights with, what it is called, and what is drawn for it all
        /// resolve through it for the rest of its life.
        /// </summary>
        public int SpawnUnit(int owner, Role role, int slot, FixVec2 position, int hpOverride = -1)
        {
            UnitStats s = FactionData.Stats(factions[owner], role, slot);
            int id = Add(EntityKind.Unit, owner, role, slot, position, s.Speed, hpOverride < 0 ? s.Hp : hpOverride);
            ArmWarlord(id);
            return id;
        }

        public int SpawnWorker(int owner, FixVec2 position)
        {
            UnitStats s = FactionData.Stats(factions[owner], Role.Worker);
            return Add(EntityKind.Worker, owner, Role.Worker, 0, position, s.Speed, s.Hp);
        }

        public int SpawnResourceNode(FixVec2 position, int amount)
        {
            int id = Add(EntityKind.ResourceNode, -1, Role.None, 0, position, Fix.Zero, NodeHp);
            Entity e = entities[id];
            e.Resource = amount;
            entities[id] = e;
            return id;
        }

        /// <summary>
        /// Entry 0 of the building slot, which is what a scenario laying out a
        /// starting base means and what five factions out of six have of every
        /// building role.
        /// </summary>
        public int SpawnBuilding(int owner, Role role, FixVec2 position, bool complete) =>
            SpawnBuilding(owner, role, 0, position, complete);

        /// <summary>
        /// One named entry of a building slot. The entry is written onto the body
        /// exactly as it is for a unit, so the hp it stands up with, the clock it
        /// stands up on, what it is called, and what is drawn for it all resolve
        /// through it for the rest of its life. 인간's defense slot holds three
        /// entries and this is what tells 전기 타워 from 대포 once both are down.
        /// </summary>
        public int SpawnBuilding(int owner, Role role, int slot, FixVec2 position, bool complete)
        {
            int hp = FactionData.Stats(factions[owner], role, slot).Hp;
            int id = Add(EntityKind.Building, owner, role, slot, position, Fix.Zero, complete ? hp : 1);
            Entity e = entities[id];
            e.MaxHp = hp;
            e.BuildTicksLeft = complete ? 0 : FactionData.BuildTicks(factions[owner], role, slot);
            entities[id] = e;
            return id;
        }

        private int Add(EntityKind kind, int owner, Role role, int slot, FixVec2 position, Fix speed, int hp)
        {
            var e = new Entity
            {
                Id = entities.Count,
                Owner = owner,
                Alive = true,
                Kind = kind,
                Role = role,
                Slot = slot,
                Position = position,
                Target = position,
                Speed = speed,
                Hp = hp,
                MaxHp = hp,
                // Zero is a valid entity id, so "none" has to be -1.
                GatherNodeId = -1,
                DropOffId = -1,
                TargetId = -1,
                ParentId = -1,
            };
            entities.Add(e);
            paths.Add(new List<int>());
            // Counted here rather than in each Spawn* method, so every body that
            // ever enters the world is counted exactly once.
            if (CountsAgainstPopulation(kind) && owner >= 0 && owner < MaxPeers) population[owner]++;
            return e.Id;
        }

        /// <summary>
        /// Advances exactly one tick. Commands may arrive in any order; they are
        /// canonicalized here so every peer executes the same sequence. The system
        /// order below is part of the contract too: each system is deterministic
        /// on its own, but reordering them changes the result.
        /// </summary>
        public void Step(IReadOnlyList<Command> commands)
        {
            // A decided match still advances its tick, so the lockstep barrier and
            // the hash exchange keep running instead of stalling on a dead world.
            if (!MatchOver)
            {
                tickCommands.Clear();
                for (int i = 0; i < commands.Count; i++) tickCommands.Add(commands[i]);
                // A simulated peer's input is computed rather than received, so it
                // is appended before the sort and ordered against a human peer's by
                // the same PeerId, Seq rule. Every peer computes the same commands
                // from the same world, so none of this crosses the wire.
                AiSystem();
                tickCommands.Sort(Command.CanonicalCompare);

                for (int i = 0; i < tickCommands.Count; i++) Apply(tickCommands[i]);

                GatherSystem();
                ConstructionSystem();
                ProductionSystem();
                WarlordSpawnSystem();
                CombatSystem();
                MoveSystem();
                VictorySystem();
            }
            Tick++;
        }

        /// <summary>
        /// Losing your last base loses the match. Decided here rather than by a
        /// client, so both peers name the same winner on the same tick. Runs after
        /// combat, so a base destroyed this tick counts this tick.
        /// </summary>
        private void VictorySystem()
        {
            // Dead entities keep their slot, so a peer that ever owned a base is
            // still visible in this scan; that is what tells a real match apart
            // from a harness world that never had one.
            var hadBase = new bool[MaxPeers];
            var holdsBase = new bool[MaxPeers];
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (e.Kind != EntityKind.Building || e.Role != Role.Base) continue;
                if (e.Owner < 0 || e.Owner >= MaxPeers) continue;
                hadBase[e.Owner] = true;
                if (e.Alive) holdsBase[e.Owner] = true;
            }

            // Scanned by peer id, never by a keyed collection, so "the surviving
            // peer" cannot depend on enumeration order.
            int contenders = 0, standing = 0, survivor = -1;
            for (int p = 0; p < MaxPeers; p++)
            {
                if (!hadBase[p]) continue;
                contenders++;
                if (!holdsBase[p]) continue;
                standing++;
                if (survivor < 0) survivor = p;
            }

            if (contenders < 2 || standing > 1) return;

            MatchOver = true;
            Winner = standing == 1 ? survivor : -1; // two bases falling on one tick is a draw
        }

        private void Apply(Command c)
        {
            switch (c.Type)
            {
                case CommandType.Move:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    Entity e = entities[c.EntityId];
                    ClearOrders(ref e);
                    entities[c.EntityId] = e;
                    SetDestination(c.EntityId, c.Target);
                    break;
                }

                case CommandType.Attack:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    // Arg names the victim. Out of range, dead, or friendly is a
                    // malformed order and is refused whole: picking a substitute
                    // would have each peer pick one for itself.
                    if (c.Arg < 0 || c.Arg >= entities.Count) return;
                    Entity victim = entities[c.Arg];
                    if (!victim.Alive || victim.Owner == c.PeerId) return;

                    Entity e = entities[c.EntityId];
                    if (!CanAttack(e)) return;
                    ClearOrders(ref e);
                    e.Mode = OrderMode.Attack;
                    e.TargetId = c.Arg;
                    entities[c.EntityId] = e;
                    // Walk into range. A turret has no walk to give, and its Target
                    // is hashed state, so it takes the order without moving.
                    if (e.Speed.Raw != 0) SetDestination(c.EntityId, victim.Position);
                    break;
                }

                case CommandType.AttackMove:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    Entity e = entities[c.EntityId];
                    // Has to walk and has to shoot. On anything else this order is a
                    // plain Move wearing another name, and accepting it would leave
                    // an entity in a mode whose systems never touch it.
                    if (e.Speed.Raw == 0 || !CanAttack(e)) return;
                    ClearOrders(ref e);
                    e.Mode = OrderMode.AttackMove;
                    e.OrderPoint = c.Target;
                    entities[c.EntityId] = e;
                    SetDestination(c.EntityId, c.Target);
                    break;
                }

                case CommandType.Stop:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    Entity e = entities[c.EntityId];
                    ClearOrders(ref e);
                    Halt(c.EntityId, ref e);
                    entities[c.EntityId] = e;
                    break;
                }

                case CommandType.HoldPosition:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    Entity e = entities[c.EntityId];
                    ClearOrders(ref e);
                    Halt(c.EntityId, ref e);
                    e.Mode = OrderMode.Hold;
                    entities[c.EntityId] = e;
                    break;
                }

                case CommandType.SetRallyPoint:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    Entity b = entities[c.EntityId];
                    if (b.Kind != EntityKind.Building) return;
                    // Not routed through ClearOrders: a rally point is the
                    // building's, not the unit's, and it survives every order the
                    // units it makes are later given.
                    b.RallyPoint = c.Target;
                    b.HasRallyPoint = true;
                    entities[c.EntityId] = b;
                    break;
                }

                case CommandType.Spawn:
                    SpawnUnit(c.PeerId, Role.Melee, c.Target);
                    break;

                case CommandType.Gather:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    if (c.Arg < 0 || c.Arg >= entities.Count) return;
                    Entity node = entities[c.Arg];
                    if (!node.Alive || node.Kind != EntityKind.ResourceNode) return;

                    Entity w = entities[c.EntityId];
                    if (w.Kind != EntityKind.Worker) return;
                    ClearOrders(ref w);
                    w.GatherNodeId = c.Arg;
                    w.GatherTicksLeft = 0;
                    entities[c.EntityId] = w;
                    // A loaded worker finishes its delivery first; GatherSystem
                    // routes it back to the new node afterwards.
                    if (w.CarryAmount == 0) SetDestination(c.EntityId, node.Position);
                    break;
                }

                case CommandType.Build:
                {
                    // Arg names the building the way Produce names a unit: the role
                    // in the low bits, which entry of that role's roster list in
                    // the high ones. One packing for both, so a client that learned
                    // to address an entry addresses it the same way everywhere.
                    //
                    // The role is range-tested after unpacking rather than before.
                    // The old test was on the whole Arg, so it refused every packed
                    // argument outright, and that failing-closed is what let #110
                    // ship Produce alone; the same test moved behind the unpacking
                    // is what replaces it.
                    //
                    // It is defence in depth here rather than the only gate. Every
                    // table CanBuild subscripts by role sits behind IsBuilding,
                    // which is a chain of equality comparisons and already refuses
                    // anything outside the five building roles. That is a fact about
                    // how IsBuilding happens to be written, not a contract, and the
                    // day it becomes a table of its own this line is what stands
                    // between a malformed argument and a read off the end of it. On
                    // Produce the same test is load-bearing today: nothing there
                    // filters the role before Production subscripts by it.
                    //
                    // Whether the faction fields the entry named is not tested here.
                    // CanBuild asks the roster, which is the one place that knows how
                    // long each list is, and a range test of its own here would be a
                    // second opinion for the two to drift apart on.
                    if (c.Arg < 0) return;
                    Role role = Command.RoleOf(c.Arg);
                    if ((int)role >= FactionData.RoleCount) return;
                    // Role.None is what a Build with no Arg carries, and the
                    // production building is what the client has always meant by it.
                    // An entry number rides along untouched, so a log recorded before
                    // this change still means entry 0 of the role it named.
                    TryPlaceBuilding(c.PeerId, role == Role.None ? Role.Production : role,
                        Command.SlotOf(c.Arg), c.Target);
                    break;
                }

                case CommandType.Produce:
                {
                    // Arg names the unit to build: the role in the low bits, which
                    // entry of that role's roster list in the high ones. Out of
                    // range is a malformed command, not a clamp: guessing what the
                    // player meant would have each peer guess for itself.
                    if (c.Arg < 0) return;
                    Role role = Command.RoleOf(c.Arg);
                    if ((int)role >= FactionData.RoleCount) return;
                    // Role.None is what a Produce with no Arg carries, and the
                    // default fighter is what the client has always meant by it.
                    // An entry number rides along untouched, so an old client's
                    // bare Produce still means entry 0 of the default fighter.
                    TryQueueUnit(c.PeerId, c.EntityId, role == Role.None ? Role.Melee : role,
                        Command.SlotOf(c.Arg));
                    break;
                }

                case CommandType.CancelProduction:
                    TryCancelQueuedUnit(c.PeerId, c.EntityId);
                    break;
            }
        }

        /// <summary>
        /// Puts an entity back in the no-order state. Every order routes through
        /// here before setting its own, so an entity is in exactly one mode and a
        /// stale gather loop or attack target cannot survive the order that
        /// replaced it. Two peers disagreeing about which order is live is a
        /// desync that surfaces ticks later, far from this line.
        /// </summary>
        private static void ClearOrders(ref Entity e)
        {
            e.Mode = OrderMode.None;
            e.GatherNodeId = -1;
            e.TargetId = -1;
            e.OrderPoint = FixVec2.Zero;
        }

        /// <summary>
        /// Drops the walk itself: destination, route, and cursor. Separate from
        /// ClearOrders because most orders replace the walk rather than cancel it,
        /// and a half-cleared path is a unit that resumes an order already gone.
        /// </summary>
        private void Halt(int id, ref Entity e)
        {
            e.Target = e.Position;
            e.PathIndex = 0;
            paths[id].Clear();
        }

        private bool OwnedAndAlive(int id, int peer)
        {
            if (id < 0 || id >= entities.Count) return false;
            Entity e = entities[id];
            return e.Alive && e.Owner == peer;
        }

        /// <summary>Sets a walk goal and paths to it. A failed path falls back to the straight line.</summary>
        private void SetDestination(int id, FixVec2 destination)
        {
            Entity e = entities[id];
            e.Target = destination;
            e.PathIndex = 0;
            entities[id] = e;
            Pathfinder.FindPath(this, CellOf(e.Position), CellOf(destination), paths[id], Flies(e));
        }

        /// <summary>True when the entity has walked its whole path and holds no new order.</summary>
        private bool PathDone(int id) => entities[id].PathIndex >= paths[id].Count;

        private void MoveSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Speed.Raw == 0) continue;
                // "Any reason" includes a chase combat would otherwise have
                // written. Enforced here as well as in CombatSystem because this
                // is the only line that decides whether an entity moves at all.
                if (e.Mode == OrderMode.Hold) continue;

                List<int> path = paths[i];
                FixVec2 goal = e.PathIndex < path.Count ? CellCenter(path[e.PathIndex]) : e.Target;

                FixVec2 delta = goal - e.Position;
                bool arrives = delta.Magnitude <= e.Speed;
                FixVec2 next = arrives ? goal : e.Position + delta.Normalized() * e.Speed;

                // The last word on where a body may stand. The pathfinder already
                // routes around terrain, so this only ever catches the straight
                // line an entity walks when no route exists, which is exactly the
                // case that would otherwise put a unit in the middle of a lake.
                // ponytail: it stops dead rather than sliding along the edge, so a
                // unit with no route parks against the shore and stays there. Give
                // it a slide the day units are expected to find their own way round
                // something the path did not know about.
                if (!IsPassable(CellOf(next), Flies(e))) continue;

                e.Position = next;
                if (arrives && e.PathIndex < path.Count) e.PathIndex++;
                entities[i] = e;
            }
        }

        public static int CellOf(FixVec2 p)
        {
            int x = Clamp(p.X.ToInt(), 0, GridSize - 1);
            int y = Clamp(p.Y.ToInt(), 0, GridSize - 1);
            return y * GridSize + x;
        }

        public static FixVec2 CellCenter(int cell)
        {
            Fix half = Fix.Ratio(1, 2);
            return new FixVec2(Fix.FromInt(cell % GridSize) + half, Fix.FromInt(cell / GridSize) + half);
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static bool WithinRange(FixVec2 a, FixVec2 b, Fix range) =>
            (a - b).SqrMagnitude <= range * range;

        /// <summary>
        /// FNV-1a over every state field in id order. Peers compare this instead
        /// of trusting each other; a mismatch means the simulations diverged.
        /// Anything a system reads back on a later tick belongs here, paths included.
        /// </summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)Tick);
            Mix(ref h, MatchOver ? 1UL : 0UL);
            Mix(ref h, (ulong)Winner);
            Mix(ref h, Random.State);
            Mix(ref h, Random.DrawCount);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, (ulong)resources[p]);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, (ulong)factions[p]);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, (ulong)population[p]);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, aiPeers[p] ? 1UL : 0UL);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, (ulong)aiSeq[p]);
            // Terrain never changes after the map is built, but it is hashed every
            // tick anyway: two peers that generated different maps have to diverge
            // on tick 1, not twenty seconds later when a unit first walks into
            // water that only one of them has.
            // ponytail: eight cells to a word is the whole optimisation, so this
            // still walks 512 words per tick over a layer that never changes. Fold
            // it to a digest taken once when the map is built if Hash() ever shows
            // up in a profile.
            for (int c = 0; c < GridCells; c += 8)
            {
                ulong word = 0;
                for (int b = 0; b < 8; b++) word |= (ulong)terrain[c + b] << (b * 8);
                Mix(ref h, word);
            }
            // Debris expiry, the one field the faction mechanics add. Sparse, so
            // only the occupied cells are mixed, each with its own index: two
            // different cells can never fold to the same pair, and a match where
            // nothing has died yet pays nothing but the scan.
            // ponytail: a full grid walk per tick for a handful of cells, and a
            // lapsed entry is left in the array rather than swept. Both are the
            // same fix, a list of live cells, and neither is worth it until this
            // shows up in a profile.
            for (int c = 0; c < GridCells; c++)
            {
                if (remnantExpiry[c] == 0) continue;
                Mix(ref h, (ulong)c);
                Mix(ref h, (ulong)remnantExpiry[c]);
            }
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                Mix(ref h, (ulong)e.Id);
                Mix(ref h, (ulong)e.Owner);
                Mix(ref h, e.Alive ? 1UL : 0UL);
                Mix(ref h, (ulong)e.Kind);
                Mix(ref h, (ulong)e.Role);
                Mix(ref h, (ulong)e.Slot);
                Mix(ref h, (ulong)e.Position.X.Raw);
                Mix(ref h, (ulong)e.Position.Y.Raw);
                Mix(ref h, (ulong)e.Target.X.Raw);
                Mix(ref h, (ulong)e.Target.Y.Raw);
                Mix(ref h, (ulong)e.Speed.Raw);
                Mix(ref h, (ulong)e.Hp);
                Mix(ref h, (ulong)e.MaxHp);
                Mix(ref h, (ulong)e.Resource);
                Mix(ref h, (ulong)e.CarryAmount);
                Mix(ref h, (ulong)e.GatherNodeId);
                Mix(ref h, (ulong)e.DropOffId);
                Mix(ref h, (ulong)e.GatherTicksLeft);
                Mix(ref h, (ulong)e.BuildTicksLeft);
                Mix(ref h, (ulong)e.ProduceTicksLeft);
                Mix(ref h, (ulong)e.QueueCount);
                Mix(ref h, (ulong)e.ProduceRole);
                Mix(ref h, (ulong)e.ProduceSlot);
                Mix(ref h, (ulong)e.RallyPoint.X.Raw);
                Mix(ref h, (ulong)e.RallyPoint.Y.Raw);
                Mix(ref h, e.HasRallyPoint ? 1UL : 0UL);
                Mix(ref h, (ulong)e.AttackCooldown);
                Mix(ref h, (ulong)e.TargetId);
                Mix(ref h, (ulong)e.Mode);
                Mix(ref h, (ulong)e.OrderPoint.X.Raw);
                Mix(ref h, (ulong)e.OrderPoint.Y.Raw);
                Mix(ref h, (ulong)e.PathIndex);
                Mix(ref h, (ulong)e.SpawnCooldown);
                Mix(ref h, (ulong)e.OffspringCount);
                Mix(ref h, (ulong)e.ParentId);

                List<int> path = paths[i];
                Mix(ref h, (ulong)path.Count);
                for (int c = 0; c < path.Count; c++) Mix(ref h, (ulong)path[c]);
            }
            return h;
        }

        private static void Mix(ref ulong h, ulong value)
        {
            for (int b = 0; b < 8; b++)
            {
                h ^= (value >> (b * 8)) & 0xFF;
                h *= 1099511628211UL;
            }
        }
    }
}
