using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Net
{
    /// <summary>
    /// A world driven from confirmed frames instead of from a peer's input.
    ///
    /// It holds no session, sends nothing, and is acknowledged by nobody. The
    /// commands it steps are the ones the players already agreed on, in the
    /// canonical order they already ran in, so its per-tick hashes are the
    /// players' hashes — the same reason a replay of the same log produces them.
    /// Being a consumer rather than a third peer is what keeps it out of the
    /// barrier, and it is also why nothing here can be lifted into one: there is
    /// no local input, no ack, and no seal.
    ///
    /// Falling behind is the spectator's own problem to have. <see cref="Follow"/>
    /// steps as far as the feed allows and returns; when the feed has moved past
    /// the tick this world needs, the spectator drops itself and says by how
    /// much. Rejoining would need a state transfer that does not exist yet, so
    /// the honest end is a stated one rather than a silent gap in the match
    /// being watched.
    /// </summary>
    public sealed class Spectator
    {
        private readonly World world;
        private readonly SpectatorFeed feed;

        /// <summary>
        /// The world must be built exactly as the players' were, factions
        /// included: those are hashed state, so a spectator that skipped them
        /// would diverge on tick 1 with every frame it received correct.
        /// </summary>
        public Spectator(World world, SpectatorFeed feed)
        {
            this.world = world;
            this.feed = feed;
        }

        public World World => world;

        /// <summary>Ticks this spectator has executed, which is how far behind it is allowed to be.</summary>
        public int Tick => world.Tick;

        public bool Dropped => DropReason != null;

        /// <summary>Why this spectator stopped, or null while it is still watching.</summary>
        public string DropReason { get; private set; }

        /// <summary>
        /// Runs up to maxTicks confirmed frames and returns how many it ran.
        /// Zero means caught up — the players have not confirmed the next tick
        /// yet — which is the normal state and never a fault. A caller that
        /// wants per-tick output reads it between calls of one.
        /// </summary>
        public int Follow(int maxTicks)
        {
            if (DropReason != null) return 0;

            int ran = 0;
            while (ran < maxTicks)
            {
                int want = world.Tick;
                if (want > feed.LatestTick) return ran; // caught up, wait for the players

                if (!feed.TryFrame(want, out IReadOnlyList<Command> commands))
                {
                    DropReason = "tick " + want + " has already left the feed, which holds " +
                                 feed.OldestTick + ".." + feed.LatestTick +
                                 " (" + feed.Capacity + " frames)";
                    return ran;
                }

                world.Step(commands);
                ran++;
            }
            return ran;
        }
    }
}
