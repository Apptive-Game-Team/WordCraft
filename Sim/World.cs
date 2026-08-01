using System.Collections.Generic;

namespace WordCraft.Sim
{
    public struct Entity
    {
        public int Id;
        public int Owner;
        public bool Alive;
        public FixVec2 Position;
        public FixVec2 Target;
        public Fix Speed;
        public int Hp;
    }

    /// <summary>
    /// The whole simulation. Advances only through Tick(), only by whole ticks,
    /// and only from integer inputs. Nothing here may touch wall-clock time,
    /// floating point, unordered collections, or UnityEngine.
    /// </summary>
    public sealed class World
    {
        public const int TicksPerSecond = 20;

        public int Tick { get; private set; }
        public readonly DetRandom Random;

        // Append-only, indexed by id. Dead entities keep their slot so ids never
        // get reused and iteration order stays stable on every peer.
        private readonly List<Entity> entities = new List<Entity>();
        private readonly List<Command> tickCommands = new List<Command>();

        public int EntityCount => entities.Count;
        public Entity GetEntity(int id) => entities[id];

        public World(ulong seed)
        {
            Random = new DetRandom(seed);
        }

        public int SpawnUnit(int owner, FixVec2 position, Fix speed, int hp)
        {
            var e = new Entity
            {
                Id = entities.Count,
                Owner = owner,
                Alive = true,
                Position = position,
                Target = position,
                Speed = speed,
                Hp = hp,
            };
            entities.Add(e);
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
                    if (c.EntityId < 0 || c.EntityId >= entities.Count) return;
                    var e = entities[c.EntityId];
                    if (!e.Alive || e.Owner != c.PeerId) return;
                    e.Target = c.Target;
                    entities[c.EntityId] = e;
                    break;

                case CommandType.Spawn:
                    SpawnUnit(c.PeerId, c.Target, Fix.Ratio(1, 4), 100);
                    break;
            }
        }

        private void MoveSystem()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (!e.Alive) continue;

                FixVec2 delta = e.Target - e.Position;
                Fix distance = delta.Magnitude;
                if (distance <= e.Speed)
                {
                    e.Position = e.Target;
                }
                else
                {
                    e.Position = e.Position + delta.Normalized() * e.Speed;
                }
                entities[i] = e;
            }
        }

        /// <summary>
        /// FNV-1a over every state field in id order. Peers compare this instead
        /// of trusting each other; a mismatch means the simulations diverged.
        /// </summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)Tick);
            Mix(ref h, Random.State);
            Mix(ref h, Random.DrawCount);
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                Mix(ref h, (ulong)e.Id);
                Mix(ref h, (ulong)e.Owner);
                Mix(ref h, e.Alive ? 1UL : 0UL);
                Mix(ref h, (ulong)e.Position.X.Raw);
                Mix(ref h, (ulong)e.Position.Y.Raw);
                Mix(ref h, (ulong)e.Target.X.Raw);
                Mix(ref h, (ulong)e.Target.Y.Raw);
                Mix(ref h, (ulong)e.Speed.Raw);
                Mix(ref h, (ulong)e.Hp);
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
