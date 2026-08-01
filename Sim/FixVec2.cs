using System;

namespace WordCraft.Sim
{
    public readonly struct FixVec2 : IEquatable<FixVec2>
    {
        public readonly Fix X;
        public readonly Fix Y;

        public FixVec2(Fix x, Fix y) { X = x; Y = y; }

        public static readonly FixVec2 Zero = new FixVec2(Fix.Zero, Fix.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator *(FixVec2 a, Fix s) => new FixVec2(a.X * s, a.Y * s);

        public Fix SqrMagnitude => X * X + Y * Y;
        public Fix Magnitude => Fix.Sqrt(SqrMagnitude);

        /// <summary>Returns Zero for a zero vector so callers never divide by zero.</summary>
        public FixVec2 Normalized()
        {
            Fix m = Magnitude;
            if (m.Raw == 0) return Zero;
            return new FixVec2(X / m, Y / m);
        }

        public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixVec2 v && Equals(v);
        public override int GetHashCode() => X.Raw.GetHashCode() ^ (Y.Raw.GetHashCode() << 1);
        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
