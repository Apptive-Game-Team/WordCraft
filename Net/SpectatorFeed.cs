using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Net
{
    /// <summary>
    /// A bounded window of confirmed frames, written by a peer that is playing
    /// and read by whoever is watching.
    ///
    /// The peer publishes and never waits. That is the whole design: a spectator
    /// is a consumer of what the match already decided, not a participant in
    /// deciding it, so nothing a spectator does — running slow, running out of
    /// memory, walking away — can reach the barrier in LockstepSession.TryStep.
    /// A feed nobody reads is a ring buffer being overwritten, and a match that
    /// is unaware it is being watched.
    ///
    /// The window is bounded on purpose. LockstepSession.ConfirmedCommands holds
    /// one tick and the next TryStep overwrites it, so something has to keep a
    /// copy; keeping every tick would be an unbounded buffer that a spectator
    /// which vanished mid-match grows forever, and the players would pay for it.
    /// So the buffer is fixed and old frames fall out of it. The cost lands on
    /// the spectator alone: one that falls further behind than Capacity cannot
    /// catch up and is dropped by <see cref="Spectator"/>, because there is no
    /// state transfer here to restart it from. Capacity is therefore how long a
    /// watcher may stall, in ticks, and nothing more subtle than that.
    /// </summary>
    public sealed class SpectatorFeed
    {
        /// <summary>256 ticks is nearly 13 seconds at 20 Hz — a long hitch, not a disconnect.</summary>
        public const int DefaultCapacity = 256;

        // Indexed by tick modulo capacity, with the tick stored alongside, so a
        // slot answers "is this still the frame I was asked for" by itself. That
        // is what makes an overwritten frame a detectable miss rather than the
        // wrong commands executed on the right tick.
        private readonly Command[][] frames;
        private readonly int[] ticks;

        private int firstTick = -1;

        public SpectatorFeed(int capacity = DefaultCapacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            frames = new Command[capacity][];
            ticks = new int[capacity];
            for (int i = 0; i < ticks.Length; i++) ticks[i] = -1;
        }

        public int Capacity => frames.Length;

        /// <summary>The newest tick published, or -1 before the first one.</summary>
        public int LatestTick { get; private set; } = -1;

        /// <summary>The oldest tick still readable, or -1 before the first publish.</summary>
        public int OldestTick =>
            LatestTick < 0 ? -1 : Math.Max(firstTick, LatestTick - Capacity + 1);

        /// <summary>
        /// Copies the batch the session just executed, the same list and at the
        /// same moment MatchRecorder captures it. Call it after every successful
        /// TryStep; calling it twice for one tick, or not at all, costs nothing
        /// here beyond a frame a reader will report as missing, because frames
        /// are addressed by their own tick rather than by arrival order.
        ///
        /// This never blocks and never fails. A publisher that could fail because
        /// of a reader would be the barrier this class exists to stay out of.
        /// </summary>
        public void Publish(LockstepSession session)
        {
            int tick = session.ConfirmedTick;
            if (tick < 0 || tick <= LatestTick) return; // nothing newly confirmed

            IReadOnlyList<Command> confirmed = session.ConfirmedCommands;
            var block = new Command[confirmed.Count];
            for (int i = 0; i < block.Length; i++) block[i] = confirmed[i];

            int slot = tick % frames.Length;
            frames[slot] = block;
            ticks[slot] = tick;
            if (firstTick < 0) firstTick = tick;
            LatestTick = tick;
        }

        /// <summary>
        /// The frame for one tick, or false when it has not been published yet or
        /// has already been overwritten. The caller distinguishes the two by
        /// comparing against <see cref="LatestTick"/>: past it is "not yet".
        /// </summary>
        public bool TryFrame(int tick, out IReadOnlyList<Command> commands)
        {
            commands = null;
            if (tick < 0 || tick > LatestTick) return false;

            int slot = tick % frames.Length;
            if (ticks[slot] != tick) return false;

            commands = frames[slot];
            return true;
        }
    }
}
