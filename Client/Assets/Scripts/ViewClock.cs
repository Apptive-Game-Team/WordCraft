using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// The one clock every animation on the field reads: unscaled time, so a
    /// paused or slowed match still breathes rather than freezing mid-stride.
    ///
    /// A seam rather than a parameter, because the screenshot harness is the
    /// only caller that ever wants a different time and every body, tracer and
    /// shard would otherwise carry a "now" through every call. The editor never
    /// ticks Time while a batch method runs, so ViewShots holds the clock at the
    /// moment it wants to draw and steps it itself; the player never holds it.
    /// </summary>
    public static class ViewClock
    {
        private static bool held;
        private static float heldNow;
        private static float heldDelta;

        /// <summary>Seconds since start, unscaled, or the held moment.</summary>
        public static float Now => held ? heldNow : Time.unscaledTime;

        /// <summary>Seconds since the last frame, unscaled, or the held step.</summary>
        public static float Delta => held ? heldDelta : Time.unscaledDeltaTime;

        /// <summary>Freezes the clock at <paramref name="now"/>, with <paramref name="delta"/> as the last step.</summary>
        public static void Hold(float now, float delta)
        {
            held = true;
            heldNow = now;
            heldDelta = delta;
        }

        /// <summary>Back to Unity's own unscaled time.</summary>
        public static void Release() => held = false;
    }
}
