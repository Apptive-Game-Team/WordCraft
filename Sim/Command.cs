namespace WordCraft.Sim
{
    public enum CommandType
    {
        None = 0,
        Move = 1,
        Spawn = 2,
        Gather = 3,
        Build = 4,
        Produce = 5,
        Attack = 6,
        AttackMove = 7,
        Stop = 8,
        HoldPosition = 9,
        SetRallyPoint = 10,
        CancelProduction = 11,
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

        /// <summary>
        /// Second entity id, for commands that name two. Gather uses it for the
        /// node. The commands that name a roster entry instead of an entity pack
        /// it here through <see cref="RosterArg"/>.
        /// </summary>
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

        /// <summary>
        /// How many low bits of <see cref="Arg"/> the role occupies. Eight rather
        /// than the four RoleCount actually needs, so the field stays readable in
        /// hex and a role added later cannot quietly start eating slot numbers.
        /// </summary>
        private const int SlotShift = 8;

        private const int RoleMask = (1 << SlotShift) - 1;

        /// <summary>
        /// Packs a roster entry into <see cref="Arg"/>: the role in the low bits,
        /// which entry of that role's list in the high ones.
        ///
        /// Packed into the existing field rather than carried in a new one, and
        /// that is the whole of the decision. Command is serialized field by field
        /// in Net and in the replay file, and neither is this change's to edit; a
        /// field added here would travel as far as the sender's own Apply and be
        /// read back as zero everywhere else. That is not a compile error and not
        /// a dropped order — it is one peer producing 균열 파수병 while every other
        /// peer produces 자손 under the same entity id, which is a desync that
        /// surfaces ticks later with nothing pointing back here.
        ///
        /// The packing is also why nothing else had to move: entry 0 packs to the
        /// plain role number every existing log and every existing client already
        /// sends, so a replay recorded before this change decodes to exactly what
        /// it meant.
        /// </summary>
        public static int RosterArg(Role role, int slot) => (int)role | (slot << SlotShift);

        /// <summary>Which role a packed <see cref="RosterArg"/> names.</summary>
        public static Role RoleOf(int arg) => (Role)(arg & RoleMask);

        /// <summary>
        /// Which entry of that role a packed <see cref="RosterArg"/> names. A
        /// negative Arg yields a negative slot, which every caller refuses; the
        /// shift is arithmetic on purpose, so a malformed command cannot fold into
        /// a legal entry number.
        /// </summary>
        public static int SlotOf(int arg) => arg >> SlotShift;

        public static int CanonicalCompare(Command a, Command b)
        {
            if (a.PeerId != b.PeerId) return a.PeerId < b.PeerId ? -1 : 1;
            if (a.Seq != b.Seq) return a.Seq < b.Seq ? -1 : 1;
            return 0;
        }
    }
}
