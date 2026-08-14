using System.Text;

namespace WordCraft.Net
{
    internal enum MsgType : byte
    {
        Hello = 1,
        Reject = 2,
        Input = 3,
        Hash = 4,
        Dump = 5,

        // The watch wire. It rides a different socket from the four above,
        // never the match link, so these numbers only ever meet a spectator or
        // the peer serving one. Reject is shared: a refusal reads the same
        // whether it ended a handshake or a watcher's arrival.
        Watch = 6,
        Welcome = 7,
        Frame = 8,
    }

    internal static class Wire
    {
        // One datagram, never fragmented. LAN MTU is 1500; leave room for headers.
        public const int MaxDatagram = 1400;
    }

    /// <summary>
    /// Big-endian, fixed width, explicitly signed. Hand written on purpose: a
    /// reflection based serializer may reorder or pad fields differently per
    /// runtime, and two peers that disagree about a single byte of input desync
    /// with no way to tell which one was wrong.
    /// </summary>
    internal sealed class Writer
    {
        public readonly byte[] Buf = new byte[Wire.MaxDatagram];
        public int Length;

        public void Reset(MsgType type) { Length = 0; U8((byte)type); }

        public void U8(byte v) { Buf[Length++] = v; }
        public void U16(ushort v) { Buf[Length++] = (byte)(v >> 8); Buf[Length++] = (byte)v; }
        public void U32(uint v) { for (int i = 24; i >= 0; i -= 8) Buf[Length++] = (byte)(v >> i); }
        public void U64(ulong v) { for (int i = 56; i >= 0; i -= 8) Buf[Length++] = (byte)(v >> i); }
        public void I32(int v) { U32(unchecked((uint)v)); }
        public void I64(long v) { U64(unchecked((ulong)v)); }

        /// <summary>ASCII, length prefixed. Diagnostics only, never simulation input.</summary>
        public void Text(string s)
        {
            if (s == null) s = "";
            if (s.Length > 200) s = s.Substring(0, 200);
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            U16((ushort)bytes.Length);
            for (int i = 0; i < bytes.Length; i++) Buf[Length++] = bytes[i];
        }
    }

    /// <summary>
    /// Every read is bounds checked and every caller must test <see cref="Ok"/>
    /// before acting. Datagrams arrive from an untrusted socket; a truncated or
    /// hostile packet must be dropped, not throw and kill the match.
    /// </summary>
    internal sealed class Reader
    {
        private readonly byte[] buf;
        private readonly int len;
        private int pos;
        public bool Ok = true;

        public Reader(byte[] b, int length) { buf = b; len = length; }

        private bool Need(int n)
        {
            if (pos + n > len) { Ok = false; return false; }
            return true;
        }

        public byte U8() => Need(1) ? buf[pos++] : (byte)0;

        public ushort U16()
        {
            if (!Need(2)) return 0;
            ushort v = (ushort)((buf[pos] << 8) | buf[pos + 1]);
            pos += 2;
            return v;
        }

        public uint U32()
        {
            if (!Need(4)) return 0;
            uint v = 0;
            for (int i = 0; i < 4; i++) v = (v << 8) | buf[pos + i];
            pos += 4;
            return v;
        }

        public ulong U64()
        {
            if (!Need(8)) return 0;
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | buf[pos + i];
            pos += 8;
            return v;
        }

        public int I32() => unchecked((int)U32());
        public long I64() => unchecked((long)U64());

        public string Text()
        {
            int n = U16();
            if (!Ok || !Need(n)) return "";
            string s = Encoding.ASCII.GetString(buf, pos, n);
            pos += n;
            return s;
        }
    }
}
