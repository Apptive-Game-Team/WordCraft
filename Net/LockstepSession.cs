using WordCraft.Sim;

namespace WordCraft.Net
{
    public enum SessionState
    {
        Handshaking,
        Running,
        Stopped,
    }

    /// <summary>
    /// Everything both peers must already agree on before tick 0. Any difference
    /// is a rejection at handshake, not a desync twenty seconds into the match.
    /// </summary>
    public sealed class MatchConfig
    {
        public uint SimVersion = 1;
        public uint ContentVersion = 1;
        public ulong Seed = 0xC0FFEE;

        /// <summary>Ticks between issuing a command and executing it. 3 at 20 Hz absorbs 150 ms.</summary>
        public int InputDelay = 3;

        /// <summary>Ticks between state hash exchanges.</summary>
        public int HashInterval = 20;

        public int HelloIntervalMs = 100;
    }

    /// <summary>
    /// Peer to peer lockstep. Holds a World but only ever advances it through
    /// Step, and only when every peer's input for that tick has arrived.
    /// </summary>
    public sealed class LockstepSession
    {
        /// <summary>Bump on any wire format change. Old peers must be rejected, not tolerated.</summary>
        public const uint ProtocolVersion = 1;

        private readonly World world;
        private readonly ITransport transport;
        private readonly MatchConfig cfg;
        private readonly int localPeer;
        private readonly Writer w = new Writer();

        private long lastHelloMs = long.MinValue / 2;

        public SessionState State { get; private set; } = SessionState.Handshaking;
        public string StopReason { get; private set; }

        public int Tick => world.Tick;

        public LockstepSession(World world, ITransport transport, MatchConfig cfg, int localPeerId)
        {
            this.world = world;
            this.transport = transport;
            this.cfg = cfg;
            localPeer = localPeerId;
        }

        /// <summary>Pumps the socket and repeats the handshake until the peer answers.</summary>
        public void Update(long nowMs)
        {
            Receive();

            if (State == SessionState.Handshaking)
            {
                if (nowMs - lastHelloMs >= cfg.HelloIntervalMs)
                {
                    SendHello();
                    lastHelloMs = nowMs;
                }
                return;
            }

            // Our Hello may have been the one that was lost; keep repeating it
            // until the peer answers.
            if (State == SessionState.Running && nowMs - lastHelloMs >= cfg.HelloIntervalMs)
            {
                SendHello();
                lastHelloMs = nowMs;
            }
        }

        // ---- handshake ----

        private void SendHello()
        {
            w.Reset(MsgType.Hello);
            w.U32(ProtocolVersion);
            w.U32(cfg.SimVersion);
            w.U32(cfg.ContentVersion);
            w.U64(cfg.Seed);
            w.U16((ushort)cfg.InputDelay);
            w.U16((ushort)cfg.HashInterval);
            w.U8((byte)localPeer);
            transport.Send(w.Buf, w.Length);
        }

        private void OnHello(Reader r)
        {
            uint proto = r.U32();
            uint sim = r.U32();
            uint content = r.U32();
            ulong seed = r.U64();
            int delay = r.U16();
            int hashInterval = r.U16();
            int peer = r.U8();
            if (!r.Ok) return;

            string bad = null;
            if (proto != ProtocolVersion) bad = "protocol version " + proto + ", expected " + ProtocolVersion;
            else if (sim != cfg.SimVersion) bad = "simulation version " + sim + ", expected " + cfg.SimVersion;
            else if (content != cfg.ContentVersion) bad = "content version " + content + ", expected " + cfg.ContentVersion;
            else if (seed != cfg.Seed) bad = "seed 0x" + seed.ToString("X") + ", expected 0x" + cfg.Seed.ToString("X");
            else if (delay != cfg.InputDelay) bad = "input delay " + delay + ", expected " + cfg.InputDelay;
            else if (hashInterval != cfg.HashInterval) bad = "hash interval " + hashInterval + ", expected " + cfg.HashInterval;
            else if (peer == localPeer) bad = "peer id collision on " + peer;

            if (bad != null)
            {
                // Rejected before tick 0. A mismatched peer that is allowed to
                // start looks like a desync later, which is far harder to read.
                w.Reset(MsgType.Reject);
                w.Text(bad);
                transport.Send(w.Buf, w.Length);
                Stop("handshake rejected: peer reports " + bad);
                return;
            }

            if (State == SessionState.Handshaking) BeginMatch();
        }

        private void BeginMatch()
        {
            State = SessionState.Running;
        }

        // ---- plumbing ----

        private void Receive()
        {
            while (transport.TryReceive(out byte[] packet))
            {
                var r = new Reader(packet, packet.Length);
                var type = (MsgType)r.U8();
                if (!r.Ok) continue;

                switch (type)
                {
                    case MsgType.Hello: OnHello(r); break;
                    case MsgType.Reject: Stop("rejected by peer: " + r.Text()); break;
                    default: break; // unknown type, drop it
                }
            }
        }

        private void Stop(string reason)
        {
            if (State == SessionState.Stopped) return;
            State = SessionState.Stopped;
            StopReason = reason;
        }
    }
}
