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

        // Combat. All timing in whole ticks.
        public int AttackCooldown;
        public int TargetId;

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

        // Balance constants. Required to be stable, not good.
        public const int WorkerHp = 60;
        public const int UnitHp = 100;
        public const int BuildingHp = 400;
        public const int NodeHp = 1;
        public const int BuildTicks = 60;

        public static readonly Fix UnitSpeed = Fix.Ratio(1, 4);

        public int Tick { get; private set; }
        public readonly DetRandom Random;

        // Append-only, indexed by id. Dead entities keep their slot so ids never
        // get reused and iteration order stays stable on every peer. paths is
        // parallel to entities and grows with it.
        private readonly List<Entity> entities = new List<Entity>();
        private readonly List<List<int>> paths = new List<List<int>>();
        private readonly int[] resources = new int[MaxPeers];
        private readonly List<Command> tickCommands = new List<Command>();

        public int EntityCount => entities.Count;
        public Entity GetEntity(int id) => entities[id];
        public int GetResources(int peer) => resources[peer];

        /// <summary>Scenario setup only. Never call this from a system.</summary>
        public void GrantResources(int peer, int amount) => resources[peer] += amount;

        public World(ulong seed)
        {
            Random = new DetRandom(seed);
        }

        public int SpawnUnit(int owner, FixVec2 position, Fix speed, int hp)
        {
            return Add(EntityKind.Unit, owner, position, speed, hp);
        }

        public int SpawnWorker(int owner, FixVec2 position)
        {
            return Add(EntityKind.Worker, owner, position, UnitSpeed, WorkerHp);
        }

        public int SpawnResourceNode(FixVec2 position, int amount)
        {
            int id = Add(EntityKind.ResourceNode, -1, position, Fix.Zero, NodeHp);
            Entity e = entities[id];
            e.Resource = amount;
            entities[id] = e;
            return id;
        }

        public int SpawnBuilding(int owner, FixVec2 position, bool complete)
        {
            int id = Add(EntityKind.Building, owner, position, Fix.Zero, complete ? BuildingHp : 1);
            Entity e = entities[id];
            e.MaxHp = BuildingHp;
            e.BuildTicksLeft = complete ? 0 : BuildTicks;
            entities[id] = e;
            return id;
        }

        private int Add(EntityKind kind, int owner, FixVec2 position, Fix speed, int hp)
        {
            var e = new Entity
            {
                Id = entities.Count,
                Owner = owner,
                Alive = true,
                Kind = kind,
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
            return e.Id;
        }

        /// <summary>
        /// Advances exactly one tick. Commands may arrive in any order; they are
        /// canonicalized here so every peer executes the same sequence.
        /// </summary>
        public void Step(IReadOnlyList<Command> commands)
        {
            tickCommands.Clear();
            for (int i = 0; i < commands.Count; i++) tickCommands.Add(commands[i]);
            tickCommands.Sort(Command.CanonicalCompare);

            for (int i = 0; i < tickCommands.Count; i++) Apply(tickCommands[i]);

            MoveSystem();
            Tick++;
        }

        private void Apply(Command c)
        {
            switch (c.Type)
            {
                case CommandType.Move:
                {
                    if (!OwnedAndAlive(c.EntityId, c.PeerId)) return;
                    SetDestination(c.EntityId, c.Target);
                    break;
                }

                case CommandType.Spawn:
                    SpawnUnit(c.PeerId, c.Target, UnitSpeed, UnitHp);
                    break;
            }
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

        private void MoveSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                if (!e.Alive || e.Speed.Raw == 0) continue;

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

        /// <summary>
        /// FNV-1a over every state field in id order. Peers compare this instead
        /// of trusting each other; a mismatch means the simulations diverged.
        /// Anything a system reads back on a later tick belongs here, paths included.
        /// </summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)Tick);
            Mix(ref h, Random.State);
            Mix(ref h, Random.DrawCount);
            for (int p = 0; p < MaxPeers; p++) Mix(ref h, (ulong)resources[p]);
            for (int i = 0; i < entities.Count; i++)
            {
                Entity e = entities[i];
                Mix(ref h, (ulong)e.Id);
                Mix(ref h, (ulong)e.Owner);
                Mix(ref h, e.Alive ? 1UL : 0UL);
                Mix(ref h, (ulong)e.Kind);
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
                Mix(ref h, (ulong)e.AttackCooldown);
                Mix(ref h, (ulong)e.TargetId);
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
