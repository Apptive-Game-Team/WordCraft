using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// What MatchView divides a debris pile's remaining life by to get its alpha.
    /// Pulled out on its own, free of UnityEngine, for the reason
    /// <see cref="CaptureOrder"/> and <see cref="ArrivalOrder"/> were: whether the
    /// number moves with research is a thing Replay/RemnantFadeChecks.cs can hold
    /// to, and a graphics device cannot.
    /// </summary>
    public static class RemnantFade
    {
        /// <summary>
        /// The longest a pile of debris can stand in this world.
        ///
        /// A cell stores only when it lapses (World.RemnantExpiry), not how long it
        /// was given, and the debris has no owner to ask World.RemnantTicksFor
        /// about. So this takes the largest answer over every peer: 400 until
        /// anyone researches 오래 남는 잔해, 600 after. Dividing by 400 once a pile
        /// can last 600 would leave it fully opaque for its first 200 ticks;
        /// dividing by 600 has it fade from the first tick. The cost is that a
        /// pile dropped before the research, which lasts 400, starts with a fade
        /// fraction of 400/600, about 0.67, so an alpha of about 0.83 (UiStyle.RemnantFade
        /// 0.5 lerped toward 1) rather than 1, and a peer that never researched but
        /// whose rival did sees the same. Both are a lower start for a pile, never an
        /// opaque one that is about to vanish.
        /// </summary>
        public static int LongestTicks(World world)
        {
            int longest = World.RemnantTicks;
            for (int peer = 0; peer < World.MaxPeers; peer++)
            {
                int ticks = world.RemnantTicksFor(peer);
                if (ticks > longest) longest = ticks;
            }
            return longest;
        }
    }
}
