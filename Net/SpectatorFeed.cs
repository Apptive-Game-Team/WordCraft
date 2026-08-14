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
        /// The highest tick with no gap behind it, counting from the first frame
        /// this feed ever took. It never goes backwards, so it describes what
        /// arrived rather than what is still readable: a frame the ring has since
        /// overwritten was still delivered, and a frame that never came is a hole
        /// this number stops at for good.
        ///
        /// A feed filled from a local session is always contiguous and this is
        /// just LatestTick. A feed filled from a socket is not, which is what it
        /// is for: it is the only thing a watcher tells the publisher, a hint
        /// about what not to send again. Nothing may ever wait on it — see
        /// FeedPublisher, where it chooses bytes and never whether to step.
        /// </summary>
        public int ContiguousThrough { get; private set; } = -1;

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

            File(tick, block);
        }

        /// <summary>
        /// Files a frame that came from somewhere other than a local session: a
        /// datagram, which may arrive late, twice, or never. Returns false only
        /// for a frame the window has already moved past, and that is a fact
        /// about this reader alone — there is nobody to ask for it again, and
        /// asking is what would put a watcher's problem on the players.
        ///
        /// Out of order is normal here and costs nothing, because a frame is
        /// addressed by its own tick. That is the same property that makes an
        /// overwritten slot a detectable miss rather than wrong commands on the
        /// right tick, used from the other side.
        /// </summary>
        public bool Accept(int tick, IReadOnlyList<Command> commands)
        {
            if (tick < 0 || commands == null) return false;
            // Only a frame the ring can still hold. Writing an older one would
            // evict a newer frame from the slot they share.
            if (LatestTick >= 0 && tick <= LatestTick - Capacity) return false;

            int slot = tick % frames.Length;
            if (ticks[slot] == tick) return true; // a duplicate is delivery working, not a fault

            var block = new Command[commands.Count];
            for (int i = 0; i < block.Length; i++) block[i] = commands[i];

            File(tick, block);
            return true;
        }

        private void File(int tick, Command[] block)
        {
            int slot = tick % frames.Length;
            frames[slot] = block;
            ticks[slot] = tick;

            if (firstTick < 0 || tick < firstTick) firstTick = tick;
            if (tick > LatestTick) LatestTick = tick;

            // Advance over whatever the new frame completed. Held to what is
            // still readable, so a hole that fell out of the window stops this
            // permanently, which is exactly what it means.
            while (Holds(ContiguousThrough + 1)) ContiguousThrough++;
        }

        private bool Holds(int tick)
        {
            if (tick < firstTick || tick > LatestTick) return false;
            return ticks[tick % frames.Length] == tick;
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
