using System;

namespace WordCraft.Sim
{
    /// <summary>
    /// Deterministic Q16.16 fixed-point number. Every simulation value uses this.
    /// float and double are forbidden in this assembly.
    /// </summary>
    // ponytail: Q16.16 in a long. Precision 1/65536, safe magnitude ~ +-2^23 for
    // products. Move to Q32.32 with a 128-bit multiply only if map size or damage
    // math actually overflows.
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int FracBits = 16;
        public const long One = 1L << FracBits;

        public readonly long Raw;

        public Fix(long raw) { Raw = raw; }

        public static readonly Fix Zero = new Fix(0);
        public static readonly Fix OneF = new Fix(One);

        public static Fix FromInt(int v) => new Fix((long)v << FracBits);

        /// <summary>Exact rational construction. Use this instead of parsing decimals.</summary>
        public static Fix Ratio(int numerator, int denominator) =>
            new Fix(((long)numerator << FracBits) / denominator);

        public int ToInt() => (int)(Raw >> FracBits);

        public static Fix operator +(Fix a, Fix b) => new Fix(a.Raw + b.Raw);
        public static Fix operator -(Fix a, Fix b) => new Fix(a.Raw - b.Raw);
        public static Fix operator -(Fix a) => new Fix(-a.Raw);
        public static Fix operator *(Fix a, Fix b) => new Fix((a.Raw * b.Raw) >> FracBits);
        public static Fix operator /(Fix a, Fix b) => new Fix((a.Raw << FracBits) / b.Raw);
        public static Fix operator *(Fix a, int b) => new Fix(a.Raw * b);
        public static Fix operator /(Fix a, int b) => new Fix(a.Raw / b);

        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

        public static Fix Abs(Fix a) => new Fix(a.Raw < 0 ? -a.Raw : a.Raw);
        public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
        public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;

        /// <summary>Integer Newton square root. Exact and platform independent.</summary>
        public static Fix Sqrt(Fix a)
        {
            if (a.Raw <= 0) return Zero;
            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
            ulong n = (ulong)a.Raw << FracBits;
            ulong x = n;
            ulong y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return new Fix((long)x);
        }

        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix f && Raw == f.Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

        /// <summary>Diagnostics only. Never feed this back into the simulation.</summary>
        public override string ToString() => (Raw >> FracBits) + "+" + (Raw & (One - 1)) + "/65536";
    }
}
