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
    public enum Terrain : byte
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
        public const int ProduceCost = 20;
        public const int ProduceTicks = 40;
        public const int MaxQueue = 5;

        // Population, per docs/FACTIONS.md. The hard cap is an absolute ceiling,
        // not a sum: enough supply buildings must not push a peer past it.
        public const int PopulationPerBase = 10;
        public const int PopulationPerSupply = 8;
        public const int PopulationHardCap = 200;

        public static readonly Fix InteractRange = Fix.FromInt(2);
        public static readonly Fix AcquireRange = Fix.FromInt(10);

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

        public Terrain TerrainAt(int cell) => (Terrain)terrain[cell];

        /// <summary>
        /// Scenario setup only, and only before the first tick. Terrain is hashed,
        /// so a peer that repainted a cell mid-match would desync every other peer.
        /// </summary>
        public void SetTerrain(int x, int y, Terrain kind) => terrain[y * GridSize + x] = (byte)kind;

        /// <summary>
        /// Whether a mover may occupy this cell. Terrain is a wall or it is
        /// nothing: there is no movement cost, so a route is decided by length
        /// alone and the pathfinder stays on integer costs.
        ///
        /// air is the seam docs/FACTION-MECHANICS.md asks for ("공중 유닛은 지형과
        /// 잔해를 무시하고 이동한다"). There are no air units yet, so every caller
        /// passes false; the parameter exists so the rule has one place to land
        /// rather than being retrofitted into every movement path later.
        /// </summary>
        public bool IsPassable(int cell, bool air) => air || terrain[cell] == (byte)Terrain.Open;

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void GrantResources(int peer, int amount) => resources[peer] += amount;

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void SetPeerFaction(int peer, Faction faction) => factions[peer] = faction;

        public World(ulong seed)
        {
            Random = new DetRandom(seed);
        }

        /// <summary>
        /// hpOverride exists for harnesses that need to seed a specific divergence;
        /// -1 means "take the roster value", which is what gameplay always passes.
        /// </summary>
        public int SpawnUnit(int owner, Role role, FixVec2 position, int hpOverride = -1)
        {
            UnitStats s = FactionData.Stats(role);
            return Add(EntityKind.Unit, owner, role, position, s.Speed, hpOverride < 0 ? s.Hp : hpOverride);
        }

        public int SpawnWorker(int owner, FixVec2 position)
        {
            UnitStats s = FactionData.Stats(Role.Worker);
            return Add(EntityKind.Worker, owner, Role.Worker, position, s.Speed, s.Hp);
        }

        public int SpawnResourceNode(FixVec2 position, int amount)
        {
            int id = Add(EntityKind.ResourceNode, -1, Role.None, position, Fix.Zero, NodeHp);
            Entity e = entities[id];
            e.Resource = amount;
            entities[id] = e;
            return id;
        }

        public int SpawnBuilding(int owner, Role role, FixVec2 position, bool complete)
        {
            int hp = FactionData.Stats(role).Hp;
            int id = Add(EntityKind.Building, owner, role, position, Fix.Zero, complete ? hp : 1);
            Entity e = entities[id];
            e.MaxHp = hp;
            e.BuildTicksLeft = complete ? 0 : FactionData.BuildTicks(role);
            entities[id] = e;
            return id;
        }

        private int Add(EntityKind kind, int owner, Role role, FixVec2 position, Fix speed, int hp)
        {
            var e = new Entity
            {
                Id = entities.Count,
                Owner = owner,
                Alive = true,
                Kind = kind,
                Role = role,
                Position = position,
                Target = position,
                Speed = speed,
                Hp = hp,
                MaxHp = hp,
                // Zero is a valid entity id, so "none" has to be -1.
                GatherNodeId = -1,
                DropOffId = -1,
                TargetId = -1,
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
                    // Arg names the building, the same way Produce names the unit.
                    // Out of range is malformed and refused rather than clamped.
                    if (c.Arg < 0 || c.Arg >= FactionData.RoleCount) return;
                    // Role.None is what a Build with no Arg carries, and the
                    // production building is what the client has always meant by it.
                    TryPlaceBuilding(c.PeerId, c.Arg == 0 ? Role.Production : (Role)c.Arg, c.Target);
                    break;

                case CommandType.Produce:
                    // Arg names the unit to build. Out of range is a malformed
                    // command, not a clamp: guessing what the player meant would
                    // have each peer guess for itself.
                    if (c.Arg < 0 || c.Arg >= FactionData.RoleCount) return;
                    // Role.None is what a Produce with no Arg carries, and the
                    // default fighter is what the client has always meant by it.
                    TryQueueUnit(c.PeerId, c.EntityId, c.Arg == 0 ? Role.Melee : (Role)c.Arg);
                    break;

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
            Pathfinder.FindPath(this, CellOf(e.Position), CellOf(destination), paths[id]);
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
                if (delta.Magnitude <= e.Speed)
                {
                    e.Position = goal;
                    if (e.PathIndex < path.Count) e.PathIndex++;
                }
                else
                {
                    e.Position = e.Position + delta.Normalized() * e.Speed;
                }
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
            // water that only one of them has. Eight cells to a word, so hashing an
            // immutable 4096-byte layer every tick stays off the tick budget.
            for (int c = 0; c < GridCells; c += 8)
            {
                ulong word = 0;
                for (int b = 0; b < 8; b++) word |= (ulong)terrain[c + b] << (b * 8);
                Mix(ref h, word);
            }
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                Mix(ref h, (ulong)e.Id);
                Mix(ref h, (ulong)e.Owner);
                Mix(ref h, e.Alive ? 1UL : 0UL);
                Mix(ref h, (ulong)e.Kind);
                Mix(ref h, (ulong)e.Role);
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
                Mix(ref h, (ulong)e.RallyPoint.X.Raw);
                Mix(ref h, (ulong)e.RallyPoint.Y.Raw);
                Mix(ref h, e.HasRallyPoint ? 1UL : 0UL);
                Mix(ref h, (ulong)e.AttackCooldown);
                Mix(ref h, (ulong)e.TargetId);
                Mix(ref h, (ulong)e.Mode);
                Mix(ref h, (ulong)e.OrderPoint.X.Raw);
                Mix(ref h, (ulong)e.OrderPoint.Y.Raw);
                Mix(ref h, (ulong)e.PathIndex);

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
