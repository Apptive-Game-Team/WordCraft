namespace WordCraft.Sim
{
    public enum CommandType
    {
        None = 0,
        Move = 1,
        Spawn = 2,
        Gather = 3,
    }

    /// <summary>
    /// One player action. Canonical execution order inside a tick is
    /// PeerId, then Seq. Peers must agree on both.
    /// </summary>
    public readonly struct Command
    {
        public readonly int Tick;
        public readonly int PeerId;
        public readonly int Seq;
        public readonly CommandType Type;
        public readonly int EntityId;
        public readonly FixVec2 Target;

        /// <summary>Second entity id, for commands that name two. Gather uses it for the node.</summary>
        public readonly int Arg;

        public Command(int tick, int peerId, int seq, CommandType type, int entityId, FixVec2 target, int arg = 0)
        {
            Tick = tick;
            PeerId = peerId;
            Seq = seq;
            Type = type;
            EntityId = entityId;
            Target = target;
            Arg = arg;
        }

        public static int CanonicalCompare(Command a, Command b)
        {
            if (a.PeerId != b.PeerId) return a.PeerId < b.PeerId ? -1 : 1;
            if (a.Seq != b.Seq) return a.Seq < b.Seq ? -1 : 1;
            return 0;
        }
    }
}
