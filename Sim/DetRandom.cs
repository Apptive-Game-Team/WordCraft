namespace WordCraft.Sim
{
    /// <summary>
    /// xorshift64*. Deterministic across platforms because it is pure integer math.
    /// The draw count is part of simulation state, so every peer must call it the
    /// same number of times in the same order.
    /// </summary>
    public sealed class DetRandom
    {
        public ulong State;
        public ulong DrawCount;

        public DetRandom(ulong seed)
        {
            // A zero state would lock xorshift at zero forever.
            State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextULong()
        {
            DrawCount++;
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform in [0, maxExclusive). Rejection sampling keeps it unbiased.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            ulong bound = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound) - 1;
            ulong v;
            do { v = NextULong(); } while (v > limit);
            return (int)(v % bound);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public Fix NextFix() => new Fix((long)(NextULong() & 0xFFFF));
    }
}
