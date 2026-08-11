using System;
using System.Collections.Generic;
using System.IO;
using WordCraft.Sim;

namespace WordCraft.Replay
{
    /// <summary>
    /// What a saved match needs to replay into the same result: the content it
    /// was played under, the seed its world was built from, which faction each
    /// peer played, and which peers the simulation itself was playing. The last
    /// one matters because an AI peer's decisions are computed fresh from world
    /// state every tick (World.AiSystem, called from inside Step) rather than
    /// carried as commands in the log — so replaying an AI peer's match without
    /// telling the world it was ever AI-controlled runs a different match with
    /// that peer doing nothing at all. The starting entities themselves are not
    /// stored here; they come back from the seed and the factions through the
    /// same scenario builder every match already uses, the same way a networked
    /// match's two peers arrive at an identical world without ever exchanging it.
    /// </summary>
    public readonly struct ReplayHeader
    {
        public readonly uint ContentVersion;
        public readonly ulong Seed;
        public readonly Faction Peer0Faction;
        public readonly Faction Peer1Faction;

        /// <summary>Bit i set means peer i was played by the simulation, not a human.</summary>
        public readonly byte AiPeers;

        public ReplayHeader(uint contentVersion, ulong seed, Faction peer0Faction, Faction peer1Faction,
            byte aiPeers = 0)
        {
            ContentVersion = contentVersion;
            Seed = seed;
            Peer0Faction = peer0Faction;
            Peer1Faction = peer1Faction;
            AiPeers = aiPeers;
        }

        public bool IsAi(int peer) => (AiPeers & (1 << peer)) != 0;
    }

    /// <summary>
    /// A match's input log on disk, and nothing else: no floating point, no wall
    /// clock, no compression. Reading a file this did not write, or one that has
    /// been truncated or had a bit flipped, ends with TryRead returning false and
    /// a reason — never an exception, and never a log that looks complete but
    /// isn't.
    ///
    /// Net/Wire.cs already hand-rolls a big-endian reader and writer for the same
    /// reason this format needs its own: a reflection based serializer can pad or
    /// order fields differently per runtime, and a replay that reads back wrong
    /// is a desync with no peer on the other end to blame it on. It is not reused
    /// directly. Wire's Reader and Writer are declared `internal` to
    /// WordCraft.Net with no InternalsVisibleTo, so neither this project nor Host
    /// can see them even with a project reference; and Wire.Writer's buffer is
    /// fixed at Wire.MaxDatagram (1400 bytes), sized for one UDP packet, where a
    /// whole match's log is expected to run well past that. This format follows
    /// the same rules — big-endian, fixed width, explicitly signed — written
    /// where a growable buffer and a plain file both fit.
    ///
    /// Layout, every multi-byte field big-endian:
    ///   u32 magic ("WCRP")
    ///   u32 format version
    ///   u32 content version
    ///   u64 seed
    ///   u8  peer 0 faction, u8 peer 1 faction
    ///   u8  AI peers bitmask (bit i: peer i was played by the simulation)
    ///   i32 tick count (the length of the log array)
    ///   i32 block count (how many of those ticks carry a command)
    ///   block count times:
    ///     i32 tick, u16 command count, then that many commands:
    ///       u8 peer id, i32 seq, u8 command type, i32 entity id,
    ///       i64 target x (raw Fix), i64 target y (raw Fix), i32 arg
    ///   u64 checksum, over every byte before it
    /// </summary>
    public static class ReplayLog
    {
        // "WCRP": WordCraft rePlay. Checked before anything else in the header is
        // trusted; a file that fails here was never one of these to begin with.
        private const uint Magic = 0x57435250;

        // Bumped on any layout change above. A reader on another version refuses
        // rather than guessing at a shape that no longer matches.
        private const uint FormatVersion = 1;

        public static void Write(string path, ReplayHeader header, IReadOnlyList<Command>[] log)
        {
            var body = new List<byte>();
            WriteU32(body, Magic);
            WriteU32(body, FormatVersion);
            WriteU32(body, header.ContentVersion);
            WriteU64(body, header.Seed);
            WriteU8(body, (byte)header.Peer0Faction);
            WriteU8(body, (byte)header.Peer1Faction);
            WriteU8(body, header.AiPeers);
            WriteI32(body, log.Length);

            int blockCount = 0;
            for (int t = 0; t < log.Length; t++)
            {
                if (log[t] != null && log[t].Count > 0) blockCount++;
            }
            WriteI32(body, blockCount);

            for (int t = 0; t < log.Length; t++)
            {
                IReadOnlyList<Command> commands = log[t];
                if (commands == null || commands.Count == 0) continue;

                WriteI32(body, t);
                WriteU16(body, (ushort)commands.Count);
                for (int i = 0; i < commands.Count; i++)
                {
                    Command c = commands[i];
                    WriteU8(body, (byte)c.PeerId);
                    WriteI32(body, c.Seq);
                    WriteU8(body, (byte)c.Type);
                    WriteI32(body, c.EntityId);
                    WriteI64(body, c.Target.X.Raw);
                    WriteI64(body, c.Target.Y.Raw);
                    WriteI32(body, c.Arg);
                }
            }

            WriteU64(body, Fnv1a64(body, body.Count));

            // Built up in memory and written in one call: a reader that opened
            // this path mid-write would see a partial file, and a partial file is
            // exactly what TryRead below is built to refuse rather than trust.
            File.WriteAllBytes(path, body.ToArray());
        }

        public static bool TryRead(string path, out ReplayHeader header, out List<Command>[] log, out string refusal)
        {
            header = default;
            log = null;

            byte[] buf;
            try
            {
                buf = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                refusal = "could not read " + path + ": " + ex.Message;
                return false;
            }

            // The checksum covers everything before it, so it has to be verified
            // before any field it is guarding is trusted. This is what turns a
            // single flipped bit into a refusal instead of a log that parses
            // cleanly and replays into a different match.
            if (buf.Length < 8)
            {
                refusal = "file too short to hold a checksum";
                return false;
            }
            int bodyLength = buf.Length - 8;
            ulong storedChecksum = ReadFixedU64(buf, bodyLength);
            ulong actualChecksum = Fnv1a64(buf, bodyLength);
            if (storedChecksum != actualChecksum)
            {
                refusal = "checksum mismatch: the file is corrupted or truncated";
                return false;
            }

            int pos = 0;
            bool ok = true;

            uint magic = ReadU32(buf, ref pos, bodyLength, ref ok);
            uint formatVersion = ReadU32(buf, ref pos, bodyLength, ref ok);
            uint contentVersion = ReadU32(buf, ref pos, bodyLength, ref ok);
            ulong seed = ReadU64(buf, ref pos, bodyLength, ref ok);
            byte peer0 = ReadU8(buf, ref pos, bodyLength, ref ok);
            byte peer1 = ReadU8(buf, ref pos, bodyLength, ref ok);
            byte aiPeers = ReadU8(buf, ref pos, bodyLength, ref ok);
            int tickCount = ReadI32(buf, ref pos, bodyLength, ref ok);
            int blockCount = ReadI32(buf, ref pos, bodyLength, ref ok);
            if (!ok) { refusal = "truncated header"; return false; }

            if (magic != Magic) { refusal = "not a replay file"; return false; }
            if (formatVersion != FormatVersion)
            {
                refusal = "replay format " + formatVersion + ", this build reads " + FormatVersion;
                return false;
            }
            if (tickCount < 0 || blockCount < 0)
            {
                refusal = "negative tick or block count in header";
                return false;
            }
            if (peer0 >= FactionData.FactionCount || peer1 >= FactionData.FactionCount)
            {
                refusal = "unknown faction id in header";
                return false;
            }
            // The whole reason this field is stored: a replay recorded under
            // other content would step through the same commands and land
            // somewhere else. Refuse instead of silently playing it anyway.
            if (contentVersion != FactionData.ContentVersion)
            {
                refusal = "content version " + contentVersion + ", running " + FactionData.ContentVersion;
                return false;
            }

            var built = new List<Command>[tickCount];
            for (int t = 0; t < tickCount; t++) built[t] = new List<Command>();

            for (int b = 0; b < blockCount; b++)
            {
                int tick = ReadI32(buf, ref pos, bodyLength, ref ok);
                int count = ReadU16(buf, ref pos, bodyLength, ref ok);
                if (!ok) { refusal = "truncated block header"; return false; }
                if (tick < 0 || tick >= tickCount)
                {
                    refusal = "block names tick " + tick + ", outside 0.." + (tickCount - 1);
                    return false;
                }

                var commands = new List<Command>(count);
                for (int i = 0; i < count; i++)
                {
                    byte peerId = ReadU8(buf, ref pos, bodyLength, ref ok);
                    int seq = ReadI32(buf, ref pos, bodyLength, ref ok);
                    byte type = ReadU8(buf, ref pos, bodyLength, ref ok);
                    int entityId = ReadI32(buf, ref pos, bodyLength, ref ok);
                    long x = ReadI64(buf, ref pos, bodyLength, ref ok);
                    long y = ReadI64(buf, ref pos, bodyLength, ref ok);
                    int arg = ReadI32(buf, ref pos, bodyLength, ref ok);
                    if (!ok) { refusal = "truncated command in the block for tick " + tick; return false; }

                    commands.Add(new Command(tick, peerId, seq, (CommandType)type, entityId,
                        new FixVec2(new Fix(x), new Fix(y)), arg));
                }
                built[tick] = commands;
            }

            header = new ReplayHeader(contentVersion, seed, (Faction)peer0, (Faction)peer1, aiPeers);
            log = built;
            refusal = null;
            return true;
        }

        // ---- big-endian, fixed width, explicitly signed: see the type comment ----

        private static void WriteU8(List<byte> buf, byte v) => buf.Add(v);
        private static void WriteU16(List<byte> buf, ushort v) { buf.Add((byte)(v >> 8)); buf.Add((byte)v); }
        private static void WriteU32(List<byte> buf, uint v) { for (int i = 24; i >= 0; i -= 8) buf.Add((byte)(v >> i)); }
        private static void WriteU64(List<byte> buf, ulong v) { for (int i = 56; i >= 0; i -= 8) buf.Add((byte)(v >> i)); }
        private static void WriteI32(List<byte> buf, int v) => WriteU32(buf, unchecked((uint)v));
        private static void WriteI64(List<byte> buf, long v) => WriteU64(buf, unchecked((ulong)v));

        private static bool Need(int pos, int n, int len) => pos + n <= len;

        private static byte ReadU8(byte[] buf, ref int pos, int len, ref bool ok)
        {
            if (!ok || !Need(pos, 1, len)) { ok = false; return 0; }
            return buf[pos++];
        }

        private static ushort ReadU16(byte[] buf, ref int pos, int len, ref bool ok)
        {
            if (!ok || !Need(pos, 2, len)) { ok = false; return 0; }
            ushort v = (ushort)((buf[pos] << 8) | buf[pos + 1]);
            pos += 2;
            return v;
        }

        private static uint ReadU32(byte[] buf, ref int pos, int len, ref bool ok)
        {
            if (!ok || !Need(pos, 4, len)) { ok = false; return 0; }
            uint v = 0;
            for (int i = 0; i < 4; i++) v = (v << 8) | buf[pos + i];
            pos += 4;
            return v;
        }

        private static ulong ReadU64(byte[] buf, ref int pos, int len, ref bool ok)
        {
            if (!ok || !Need(pos, 8, len)) { ok = false; return 0; }
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | buf[pos + i];
            pos += 8;
            return v;
        }

        private static int ReadI32(byte[] buf, ref int pos, int len, ref bool ok) =>
            unchecked((int)ReadU32(buf, ref pos, len, ref ok));

        private static long ReadI64(byte[] buf, ref int pos, int len, ref bool ok) =>
            unchecked((long)ReadU64(buf, ref pos, len, ref ok));

        /// <summary>Reads the checksum itself, at a known offset outside the bounds-checked region above.</summary>
        private static ulong ReadFixedU64(byte[] buf, int at)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | buf[at + i];
            return v;
        }

        /// <summary>
        /// FNV-1a, integer arithmetic only. Bounds checking on the fields above
        /// catches a file cut short; this catches a bit flipped in the middle of
        /// one that is otherwise still the right length and still parses clean.
        /// </summary>
        private static ulong Fnv1a64(List<byte> bytes, int count)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < count; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong Fnv1a64(byte[] bytes, int count)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < count; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
