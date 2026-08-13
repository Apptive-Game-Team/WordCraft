using System;
using System.Collections.Generic;
using System.IO;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track C's write scope is Host/ and new files under Replay/
    /// (.plan/general/2026-08-11-parallel-milestones.md). Replay/Program.cs
    /// belongs to track A, which adds a check per simulation mechanic; this file
    /// holds the one this issue adds, and Program.cs only carries the single line
    /// that calls Check().
    ///
    /// Writes the client's own scripted match to a file, reads it back, and steps
    /// a fresh world through what was read. The per-tick hashes have to match a
    /// run that never touched disk, or the file format is lossy in a way no
    /// single field's round trip would show. Then the two refusals the format
    /// exists for: a byte flipped in the file, and a replay recorded under
    /// different content.
    /// </summary>
    internal static class ReplayRoundTrip
    {
        // Same value Replay/Program.cs seeds its own checks with. Kept as this
        // file's own constant rather than a reference to Program.cs's, because
        // the check body here is not allowed to depend on the file it is called
        // from — track A owns that file's one line and nothing else.
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            string path = Path.Combine(Path.GetTempPath(), "wordcraft-replay-roundtrip.bin");
            try
            {
                LogRoundTripsToTheSameHashes(path);
                CorruptedFileIsRefused(path);
                ContentVersionMismatchIsRefused(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// Write, read, replay, compare. Every field of the header is checked
        /// against what went in rather than only the hashes, because a header
        /// that silently dropped the seed but still replayed correctly by luck
        /// of a shared default would pass a hash-only version of this check.
        /// </summary>
        private static void LogRoundTripsToTheSameHashes(string path)
        {
            List<Command>[] log = ScriptedLog.Build();
            var header = new ReplayHeader(FactionData.ContentVersion, Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            ulong[] original = Run(header, log);

            ReplayLog.Write(path, header, log);

            bool ok = ReplayLog.TryRead(path, out ReplayHeader read, out List<Command>[] replayed, out string refusal);
            Check(ok, "a freshly written replay was refused: " + refusal);
            Check(read.ContentVersion == header.ContentVersion, "content version did not round trip");
            Check(read.Seed == header.Seed, "seed did not round trip");
            Check(read.Peer0Faction == header.Peer0Faction && read.Peer1Faction == header.Peer1Faction,
                "faction did not round trip");
            Check(replayed.Length == log.Length, "tick count did not round trip");

            ulong[] fromFile = Run(read, replayed);
            for (int t = 0; t < original.Length; t++)
            {
                Check(original[t] == fromFile[t], "replay hash drift at tick " + t);
            }

            // Ties this check to the same number Program.cs's
            // ClientLogMatchesGoldenHash pins, so a passing round trip is
            // compared against an external value and not only against itself.
            Check(fromFile[fromFile.Length - 1] == ScriptedLog.GoldenHash,
                "replayed match disagrees with the golden hash");
        }

        /// <summary>
        /// A bit flipped in the middle of an otherwise well formed file, and a
        /// file cut in half. Different failures — one still has the right length
        /// and passes every bounds check, the other runs out of bytes partway
        /// through a field — and both have to end the same way: TryRead returns
        /// false with a reason, never an exception and never a partial log.
        /// </summary>
        private static void CorruptedFileIsRefused(string path)
        {
            var header = new ReplayHeader(FactionData.ContentVersion, Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            ReplayLog.Write(path, header, ScriptedLog.Build());

            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            bool flippedOk = ReplayLog.TryRead(path, out _, out _, out string flippedRefusal);
            Check(!flippedOk, "a replay with a flipped bit was accepted");
            Check(!string.IsNullOrEmpty(flippedRefusal), "a flipped-bit replay was refused with no reason given");

            byte[] truncated = new byte[bytes.Length / 2];
            Array.Copy(bytes, truncated, truncated.Length);
            File.WriteAllBytes(path, truncated);

            bool truncatedOk = ReplayLog.TryRead(path, out _, out _, out string truncatedRefusal);
            Check(!truncatedOk, "a truncated replay was accepted");
            Check(!string.IsNullOrEmpty(truncatedRefusal), "a truncated replay was refused with no reason given");
        }

        /// <summary>
        /// A replay is only ever valid under the content it was recorded with.
        /// Writing one whole content version ahead of what is running is the
        /// simplest way to be certain the two disagree, without depending on
        /// FactionData ever actually changing during a test run.
        /// </summary>
        private static void ContentVersionMismatchIsRefused(string path)
        {
            var header = new ReplayHeader(FactionData.ContentVersion + 1, Seed,
                ScriptedLog.Peer0Faction, ScriptedLog.Peer1Faction);
            ReplayLog.Write(path, header, ScriptedLog.Build());

            bool ok = ReplayLog.TryRead(path, out _, out _, out string refusal);
            Check(!ok, "a replay recorded under a different content version was accepted");
            Check(refusal.Contains("content version"), "wrong refusal reason: " + refusal);
        }

        private static ulong[] Run(ReplayHeader header, List<Command>[] log)
        {
            World world = MatchScenario.Build(header.Seed, header.Peer0Faction, header.Peer1Faction);
            var hashes = new ulong[log.Length];
            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                hashes[t] = world.Hash();
            }
            return hashes;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
