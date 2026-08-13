namespace WordCraft.View
{
    /// <summary>
    /// The cooldown and expiry Alert keeps, pulled out so they can be checked
    /// without Time.unscaledTime (issue #96). "Now" is a plain float handed in
    /// by the caller, not a read of the clock — Alert is the only place that
    /// still reads Time.unscaledTime; this is only the arithmetic once "now" is
    /// known, so Replay can drive it with numbers it picks itself.
    /// </summary>
    public readonly struct AlertWindow
    {
        /// <summary>Unscaled time the sentence stops showing.</summary>
        public readonly float Until;

        /// <summary>Unscaled time before which a new hit cannot arm the window again.</summary>
        public readonly float CooldownUntil;

        private AlertWindow(float until, float cooldownUntil)
        {
            Until = until;
            CooldownUntil = cooldownUntil;
        }

        /// <summary>Nothing has ever fired. -1 is before any "now" a caller will ever pass.</summary>
        public static readonly AlertWindow None = new AlertWindow(-1f, -1f);

        /// <summary>Whether the sentence is still on screen at <paramref name="now"/>.</summary>
        public bool Showing(float now) => now < Until;

        /// <summary>
        /// Called once a tick whether or not a new hit came in. Arms a fresh
        /// window only when there is a hit and the cooldown has elapsed;
        /// otherwise this window is returned unchanged, so a display already
        /// running, or a cooldown already counting down, is not reset by a tick
        /// with nothing new to report — the same rule Alert.Draw enforced
        /// inline before this split. <paramref name="armed"/> tells the caller
        /// whether this call is the one that should also record which point
        /// the alert points at, since that lives outside this struct.
        /// </summary>
        public AlertWindow Advance(bool hasHit, float now, float showSeconds, float cooldownSeconds, out bool armed)
        {
            if (hasHit && now >= CooldownUntil)
            {
                armed = true;
                return new AlertWindow(now + showSeconds, now + cooldownSeconds);
            }

            armed = false;
            return this;
        }
    }
}
