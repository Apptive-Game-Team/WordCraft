using System.Collections.Generic;
using WordCraft.Replay; // ReplayLog and ReplayHeader, compiled in from Replay; see Host.csproj
using WordCraft.Sim;

namespace WordCraft.Host
{
    /// <summary>
    /// Two saved matches, side by side. Both peers of a networked match record
    /// the confirmed batch, so their files are the same file; when they are not,
    /// the peers executed different input and the place they first part company
    /// is where the desync started. A byte comparison answers whether, this
    /// answers where.
    /// </summary>
    internal static class ReplayComparison
    {
        /// <summary>
        /// The first way two saved matches differ, or null when they are the same
        /// match. A file that will not load is reported as the difference rather
        /// than thrown, so comparing a corrupted file reads like comparing any
        /// other pair.
        /// </summary>
        public static string FirstDifference(string pathA, string pathB)
        {
            if (!ReplayLog.TryRead(pathA, out ReplayHeader headerA, out List<Command>[] logA, out string refusalA))
            {
                return pathA + " was refused: " + refusalA;
            }
            if (!ReplayLog.TryRead(pathB, out ReplayHeader headerB, out List<Command>[] logB, out string refusalB))
            {
                return pathB + " was refused: " + refusalB;
            }
            return FirstDifference(headerA, logA, headerB, logB);
        }

        private static string FirstDifference(ReplayHeader a, IReadOnlyList<Command>[] logA,
            ReplayHeader b, IReadOnlyList<Command>[] logB)
        {
            // The header first: a seed or a faction that disagrees means the two
            // worlds were never the same one, and every command difference below
            // it would be a consequence rather than the cause.
            if (a.Seed != b.Seed) return "seed 0x" + a.Seed.ToString("X") + " against 0x" + b.Seed.ToString("X");
            if (a.ContentVersion != b.ContentVersion)
            {
                return "content version " + a.ContentVersion + " against " + b.ContentVersion;
            }
            if (a.Peer0Faction != b.Peer0Faction) return "peer 0 faction " + a.Peer0Faction + " against " + b.Peer0Faction;
            if (a.Peer1Faction != b.Peer1Faction) return "peer 1 faction " + a.Peer1Faction + " against " + b.Peer1Faction;
            if (a.AiPeers != b.AiPeers) return "ai peers 0x" + a.AiPeers.ToString("X2") + " against 0x" + b.AiPeers.ToString("X2");

            int shared = logA.Length < logB.Length ? logA.Length : logB.Length;
            for (int t = 0; t < shared; t++)
            {
                string difference = FirstDifference(t, logA[t], logB[t]);
                if (difference != null) return difference;
            }

            if (logA.Length != logB.Length)
            {
                return "the first " + shared + " ticks agree, then one log ends: " +
                       logA.Length + " ticks against " + logB.Length;
            }
            return null;
        }

        private static string FirstDifference(int tick, IReadOnlyList<Command> a, IReadOnlyList<Command> b)
        {
            string where = "tick " + tick;
            if (a.Count != b.Count) return where + ": " + a.Count + " commands against " + b.Count;

            for (int i = 0; i < a.Count; i++)
            {
                Command x = a[i], y = b[i];
                if (x.PeerId != y.PeerId) return Field(where, i, "peer", x.PeerId, y.PeerId);
                if (x.Seq != y.Seq) return Field(where, i, "seq", x.Seq, y.Seq);
                if (x.Type != y.Type) return where + " command " + i + ": type " + x.Type + " against " + y.Type;
                if (x.EntityId != y.EntityId) return Field(where, i, "entity", x.EntityId, y.EntityId);
                if (x.Target.X.Raw != y.Target.X.Raw) return Field(where, i, "target x raw", x.Target.X.Raw, y.Target.X.Raw);
                if (x.Target.Y.Raw != y.Target.Y.Raw) return Field(where, i, "target y raw", x.Target.Y.Raw, y.Target.Y.Raw);
                if (x.Arg != y.Arg) return Field(where, i, "arg", x.Arg, y.Arg);
            }
            return null;
        }

        private static string Field(string where, int index, string name, long a, long b) =>
            where + " command " + index + ": " + name + " " + a + " against " + b;
    }
}
