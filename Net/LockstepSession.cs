using System;
using System.Collections.Generic;
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

        public int TimeoutMs = 5000;
        public int ResendIntervalMs = 50;
        public int HelloIntervalMs = 100;

        /// <summary>How long to keep pumping after a desync so the peer's dump can land.</summary>
        public int DumpGraceMs = 2000;
    }

    /// <summary>One entity as it crosses the wire. Fix values travel as raw longs.</summary>
    internal struct StateRow
    {
        public int Id;
        public int Owner;
        public byte Alive;
        public long PosX, PosY, TgtX, TgtY, Speed;
        public int Hp;
    }

    internal sealed class Snapshot
    {
        public ulong Hash;
        public ulong RngState;
        public ulong DrawCount;
        public StateRow[] Rows;
    }

    /// <summary>
    /// Peer to peer lockstep. Holds a World but only ever advances it through
    /// Step, and only when every peer's input for that tick has arrived.
    /// </summary>
    // ponytail: hardwired to two peers (localPeer and one remote). Going to N
    // means an array of remotes, a per-remote ack, and an all-peers barrier test;
    // nothing here assumes more than that, but nothing here is built for it either.
    public sealed class LockstepSession
    {
        /// <summary>Bump on any wire format change. Old peers must be rejected, not tolerated.</summary>
        public const uint ProtocolVersion = 1;

        private const int PeerCount = 2;
        private const int MaxBlocksPerDatagram = 24;
        private const int MaxCommandsPerTick = 256;
        private const int MaxTickLookahead = 1024;
        private const int RowsPerDumpChunk = 20;

        // Encoded sizes, kept next to the encoder so the datagram budget below
        // stays honest if a field is ever added to the input message.
        private const int HeaderBytes = 8;       // msgType, peerId, ack, blockCount
        private const int BlockHeaderBytes = 6;  // tick, commandCount
        private const int CommandBytes = 29;     // type, entityId, seq, targetX, targetY, arg

        private readonly World world;
        private readonly ITransport transport;
        private readonly MatchConfig cfg;
        private readonly int localPeer;
        private readonly int remotePeer;
        private readonly Writer w = new Writer();

        // Keyed by tick and only ever indexed, never enumerated, so dictionary
        // ordering can never leak into the order commands reach Step.
        private readonly Dictionary<int, Command[]>[] inputs = new Dictionary<int, Command[]>[PeerCount];

        private readonly List<Command> pending = new List<Command>();
        private readonly List<Command> batch = new List<Command>();
        private int localSeq;

        private int sealedThrough = -1;   // highest local tick whose input is final
        private int ackedThrough = -1;    // highest local tick the remote confirmed
        private int prunedThrough = -1;
        private int receivedThrough = -1; // highest remote tick we hold contiguously

        private bool started;
        private bool remoteActive;
        private long lastRecvMs, dumpDeadlineMs;
        private long lastSendMs = long.MinValue / 2;
        private long lastHelloMs = long.MinValue / 2;

        private readonly Dictionary<int, Snapshot> checkpoints = new Dictionary<int, Snapshot>();
        private readonly Dictionary<int, ulong> remoteHashes = new Dictionary<int, ulong>();
        private readonly Dictionary<int, StateRow> remoteRows = new Dictionary<int, StateRow>();
        private int dumpTick = -1, dumpTotal = -1;
        private ulong dumpRngState, dumpDrawCount;

        public SessionState State { get; private set; } = SessionState.Handshaking;
        public string StopReason { get; private set; }

        /// <summary>False while a desync report is still waiting on the peer's state dump.</summary>
        public bool ReportComplete { get; private set; } = true;

        public int PeerId => localPeer;
        public int Tick => world.Tick;

        public LockstepSession(World world, ITransport transport, MatchConfig cfg, int localPeerId)
        {
            this.world = world;
            this.transport = transport;
            this.cfg = cfg;
            localPeer = localPeerId;
            remotePeer = 1 - localPeerId;
            for (int i = 0; i < PeerCount; i++) inputs[i] = new Dictionary<int, Command[]>();
        }

        /// <summary>
        /// Queues a command for tick Tick + InputDelay. The delay is what lets a
        /// peer run tick T while the network is still carrying input for T+delay.
        /// </summary>
        public void Issue(CommandType type, int entityId, FixVec2 target, int arg = 0)
        {
            if (State != SessionState.Running) return;
            pending.Add(new Command(world.Tick + cfg.InputDelay, localPeer, localSeq++, type, entityId, target, arg));
        }

        /// <summary>Pumps the socket, resends unacked input, and enforces the peer timeout.</summary>
        public void Update(long nowMs)
        {
            if (!started) { started = true; lastRecvMs = nowMs; }

            Receive(nowMs);

            if (State == SessionState.Handshaking)
            {
                if (nowMs - lastHelloMs >= cfg.HelloIntervalMs)
                {
                    SendHello();
                    lastHelloMs = nowMs;
                }
                if (nowMs - lastRecvMs > cfg.TimeoutMs) Stop("handshake timeout after " + cfg.TimeoutMs + " ms");
                return;
            }

            if (State == SessionState.Stopped)
            {
                if (!ReportComplete && nowMs >= dumpDeadlineMs) FinishReport("peer state dump never arrived");
                return;
            }

            // Our Hello may have been the one that was lost; keep repeating it
            // until the peer proves it started by sending input.
            if (!remoteActive && nowMs - lastHelloMs >= cfg.HelloIntervalMs)
            {
                SendHello();
                lastHelloMs = nowMs;
            }

            if (nowMs - lastSendMs >= cfg.ResendIntervalMs)
            {
                SendInputs();
                lastSendMs = nowMs;
            }

            if (nowMs - lastRecvMs > cfg.TimeoutMs)
            {
                // Ending here is the whole point: inventing input for the missing
                // peer would make the two simulations disagree about what happened.
                Stop("peer timeout at tick " + world.Tick + " after " + cfg.TimeoutMs + " ms of silence");
            }
        }

        /// <summary>
        /// The tick barrier. Runs exactly one tick, and only when every peer's
        /// input for it is in hand. Returns false when the barrier is closed.
        /// </summary>
        public bool TryStep(long nowMs)
        {
            if (State != SessionState.Running) return false;

            int t = world.Tick;
            if (!inputs[remotePeer].TryGetValue(t, out Command[] remoteCmds)) return false;

            // Seal before stepping: once tick t runs, the commands issued during
            // it belong to t+delay and that bucket must never change afterwards.
            SealLocal(t + cfg.InputDelay, pending);
            pending.Clear();

            if (!inputs[localPeer].TryGetValue(t, out Command[] localCmds)) return false;

            batch.Clear();
            batch.AddRange(localCmds);
            batch.AddRange(remoteCmds);
            world.Step(batch); // the only line in this assembly that mutates the simulation

            inputs[remotePeer].Remove(t);
            PruneLocal();

            // Push the freshly sealed tick out now; the resend timer is the
            // backstop for loss, not the primary delivery path.
            SendInputs();
            lastSendMs = nowMs;

            if (cfg.HashInterval > 0 && world.Tick % cfg.HashInterval == 0) Checkpoint(nowMs);
            return true;
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
            // Nothing could have been issued before the match existed, so the
            // first InputDelay ticks are empty on every peer by definition.
            for (int t = 0; t < cfg.InputDelay; t++) SealLocal(t, null);
            SendInputs();
        }

        // ---- input exchange ----

        private void SealLocal(int tick, List<Command> cmds)
        {
            // Sealing twice would let the same tick execute with two different
            // inputs on two peers. First seal wins, always.
            if (inputs[localPeer].ContainsKey(tick)) return;
            inputs[localPeer][tick] = cmds == null || cmds.Count == 0
                ? Array.Empty<Command>()
                : cmds.ToArray();
            if (tick > sealedThrough) sealedThrough = tick;
        }

        /// <summary>
        /// Drops local input that is both acknowledged and already executed here.
        /// The peer can be ahead of us, so acknowledgement alone is not enough:
        /// discarding a tick we have not run yet closes the barrier forever.
        /// </summary>
        private void PruneLocal()
        {
            int limit = Math.Min(ackedThrough, world.Tick - 1);
            while (prunedThrough < limit) inputs[localPeer].Remove(++prunedThrough);
        }

        private void SendInputs()
        {
            // ponytail: resends every unacked tick wholesale instead of keeping a
            // sliding window. Commands are 29 bytes and the ack lag is a few
            // ticks, so this fits one datagram; add a window only if it stops fitting.
            w.Reset(MsgType.Input);
            w.U8((byte)localPeer);
            w.I32(receivedThrough); // piggybacked ack

            // ponytail: a single tick carrying more than ~47 commands would never
            // fit one datagram and would stall the barrier until the peer timeout.
            // Split a tick across datagrams if the game ever issues batches that big.
            int first = ackedThrough + 1;
            int count = 0;
            int bytes = HeaderBytes;
            for (int t = first; t <= sealedThrough && count < MaxBlocksPerDatagram; t++)
            {
                if (!inputs[localPeer].TryGetValue(t, out Command[] block)) continue;
                int need = BlockHeaderBytes + block.Length * CommandBytes;
                if (bytes + need > Wire.MaxDatagram) break;
                bytes += need;
                count++;
            }

            w.U16((ushort)count);
            int written = 0;
            for (int t = first; t <= sealedThrough && written < count; t++)
            {
                if (!inputs[localPeer].TryGetValue(t, out Command[] cmds)) continue;
                w.I32(t);
                w.U16((ushort)cmds.Length);
                for (int i = 0; i < cmds.Length; i++)
                {
                    Command c = cmds[i];
                    w.U8((byte)c.Type);
                    w.I32(c.EntityId);
                    w.I32(c.Seq);
                    w.I64(c.Target.X.Raw); // Fix crosses the wire as its raw long, never as text
                    w.I64(c.Target.Y.Raw);
                    w.I32(c.Arg);
                }
                written++;
            }
            transport.Send(w.Buf, w.Length);
        }

        private void OnInput(Reader r)
        {
            int peer = r.U8();
            int ack = r.I32();
            int blocks = r.U16();
            if (!r.Ok || peer != remotePeer) return;

            remoteActive = true;
            if (ack > ackedThrough)
            {
                ackedThrough = ack;
                PruneLocal();
            }

            for (int b = 0; b < blocks; b++)
            {
                int tick = r.I32();
                int n = r.U16();
                if (!r.Ok || n > MaxCommandsPerTick) return;

                var cmds = new Command[n];
                for (int i = 0; i < n; i++)
                {
                    var type = (CommandType)r.U8();
                    int entityId = r.I32();
                    int seq = r.I32();
                    long x = r.I64();
                    long y = r.I64();
                    int arg = r.I32();
                    // Tick and PeerId come from the envelope, not the body, so a
                    // command can never claim a tick its own block disagrees with.
                    cmds[i] = new Command(tick, peer, seq, type, entityId, new FixVec2(new Fix(x), new Fix(y)), arg);
                }
                if (!r.Ok) return;

                if (tick < world.Tick) continue;                    // already executed, this is a resend
                if (tick > world.Tick + MaxTickLookahead) continue;  // nonsense tick, do not buffer it
                if (inputs[peer].ContainsKey(tick)) continue;        // duplicate

                inputs[peer][tick] = cmds;
                while (inputs[peer].ContainsKey(receivedThrough + 1)) receivedThrough++;
            }
        }

        // ---- hash exchange and desync report ----

        private void Checkpoint(long nowMs)
        {
            int t = world.Tick;
            var snap = new Snapshot
            {
                Hash = world.Hash(),
                RngState = world.Random.State,
                DrawCount = world.Random.DrawCount,
                Rows = ReadRows(),
            };
            checkpoints[t] = snap;

            w.Reset(MsgType.Hash);
            w.I32(t);
            w.U64(snap.Hash);
            transport.Send(w.Buf, w.Length);

            Compare(t, nowMs);
        }

        private StateRow[] ReadRows()
        {
            var rows = new StateRow[world.EntityCount];
            for (int i = 0; i < rows.Length; i++)
            {
                Entity e = world.GetEntity(i);
                rows[i] = new StateRow
                {
                    Id = e.Id,
                    Owner = e.Owner,
                    Alive = e.Alive ? (byte)1 : (byte)0,
                    PosX = e.Position.X.Raw,
                    PosY = e.Position.Y.Raw,
                    TgtX = e.Target.X.Raw,
                    TgtY = e.Target.Y.Raw,
                    Speed = e.Speed.Raw,
                    Hp = e.Hp,
                };
            }
            return rows;
        }

        private void OnHash(Reader r, long nowMs)
        {
            int tick = r.I32();
            ulong hash = r.U64();
            if (!r.Ok) return;
            remoteHashes[tick] = hash;
            Compare(tick, nowMs);
        }

        private void Compare(int tick, long nowMs)
        {
            if (State == SessionState.Stopped) return;
            if (!checkpoints.TryGetValue(tick, out Snapshot mine)) return;
            if (!remoteHashes.TryGetValue(tick, out ulong theirs)) return;

            checkpoints.Remove(tick);
            remoteHashes.Remove(tick);

            if (mine.Hash == theirs) return;

            // Halt on the spot. Every further tick built on diverged state makes
            // the report less useful and the recorded match worthless anyway.
            Stop("desync at tick " + tick +
                 ": local hash 0x" + mine.Hash.ToString("X16") +
                 ", remote hash 0x" + theirs.ToString("X16"));
            checkpoints[tick] = mine;
            ReportComplete = false;
            dumpDeadlineMs = nowMs + cfg.DumpGraceMs;
            SendDump(tick, mine);
            TryDiff();
        }

        private void SendDump(int tick, Snapshot snap)
        {
            for (int start = 0; start < snap.Rows.Length || start == 0; start += RowsPerDumpChunk)
            {
                int n = Math.Min(RowsPerDumpChunk, snap.Rows.Length - start);
                if (n < 0) n = 0;
                w.Reset(MsgType.Dump);
                w.I32(tick);
                w.U64(snap.RngState);
                w.U64(snap.DrawCount);
                w.I32(snap.Rows.Length);
                w.I32(start);
                w.U16((ushort)n);
                for (int i = 0; i < n; i++)
                {
                    StateRow row = snap.Rows[start + i];
                    w.I32(row.Id);
                    w.I32(row.Owner);
                    w.U8(row.Alive);
                    w.I64(row.PosX);
                    w.I64(row.PosY);
                    w.I64(row.TgtX);
                    w.I64(row.TgtY);
                    w.I64(row.Speed);
                    w.I32(row.Hp);
                }
                transport.Send(w.Buf, w.Length);
                if (snap.Rows.Length == 0) break;
            }
        }

        private void OnDump(Reader r, long nowMs)
        {
            int tick = r.I32();
            ulong rngState = r.U64();
            ulong drawCount = r.U64();
            int total = r.I32();
            int start = r.I32();
            int n = r.U16();
            if (!r.Ok || total < 0 || start < 0 || n > RowsPerDumpChunk) return;

            if (tick != dumpTick)
            {
                dumpTick = tick;
                dumpTotal = total;
                remoteRows.Clear();
            }
            dumpRngState = rngState;
            dumpDrawCount = drawCount;

            for (int i = 0; i < n; i++)
            {
                var row = new StateRow
                {
                    Id = r.I32(),
                    Owner = r.I32(),
                    Alive = r.U8(),
                    PosX = r.I64(),
                    PosY = r.I64(),
                    TgtX = r.I64(),
                    TgtY = r.I64(),
                    Speed = r.I64(),
                    Hp = r.I32(),
                };
                if (!r.Ok) return;
                remoteRows[start + i] = row;
            }

            // The peer only dumps when it has already halted, so we halt too even
            // if our own hash comparison has not fired yet.
            if (State != SessionState.Stopped)
            {
                Stop("desync reported by peer at tick " + tick);
                ReportComplete = false;
                dumpDeadlineMs = nowMs + cfg.DumpGraceMs;
                if (checkpoints.TryGetValue(tick, out Snapshot mine)) SendDump(tick, mine);
            }
            TryDiff();
        }

        private void TryDiff()
        {
            if (ReportComplete || dumpTick < 0 || remoteRows.Count != dumpTotal) return;
            if (!checkpoints.TryGetValue(dumpTick, out Snapshot mine)) return;

            // Fields are compared in the same order World.Hash mixes them, so
            // "first divergence" means the same thing as "what broke the hash".
            if (mine.RngState != dumpRngState)
            {
                FinishReport("first divergence: rng State local=0x" + mine.RngState.ToString("X16") +
                             " remote=0x" + dumpRngState.ToString("X16"));
                return;
            }
            if (mine.DrawCount != dumpDrawCount)
            {
                FinishReport("first divergence: rng DrawCount local=" + mine.DrawCount +
                             " remote=" + dumpDrawCount);
                return;
            }
            if (mine.Rows.Length != dumpTotal)
            {
                FinishReport("first divergence: entity count local=" + mine.Rows.Length + " remote=" + dumpTotal);
                return;
            }

            for (int i = 0; i < mine.Rows.Length; i++)
            {
                StateRow a = mine.Rows[i], b = remoteRows[i];
                string field = null;
                string va = null, vb = null;
                if (a.Id != b.Id) { field = "Id"; va = a.Id.ToString(); vb = b.Id.ToString(); }
                else if (a.Owner != b.Owner) { field = "Owner"; va = a.Owner.ToString(); vb = b.Owner.ToString(); }
                else if (a.Alive != b.Alive) { field = "Alive"; va = a.Alive.ToString(); vb = b.Alive.ToString(); }
                else if (a.PosX != b.PosX) { field = "Position.X.Raw"; va = a.PosX.ToString(); vb = b.PosX.ToString(); }
                else if (a.PosY != b.PosY) { field = "Position.Y.Raw"; va = a.PosY.ToString(); vb = b.PosY.ToString(); }
                else if (a.TgtX != b.TgtX) { field = "Target.X.Raw"; va = a.TgtX.ToString(); vb = b.TgtX.ToString(); }
                else if (a.TgtY != b.TgtY) { field = "Target.Y.Raw"; va = a.TgtY.ToString(); vb = b.TgtY.ToString(); }
                else if (a.Speed != b.Speed) { field = "Speed.Raw"; va = a.Speed.ToString(); vb = b.Speed.ToString(); }
                else if (a.Hp != b.Hp) { field = "Hp"; va = a.Hp.ToString(); vb = b.Hp.ToString(); }
                if (field == null) continue;

                FinishReport("first divergence: entity " + a.Id + " field " + field +
                             " local=" + va + " remote=" + vb);
                return;
            }

            FinishReport("dumps agree, divergence is in state that is hashed but not dumped");
        }

        private void FinishReport(string detail)
        {
            StopReason = StopReason + "; " + detail;
            ReportComplete = true;
        }

        // ---- plumbing ----

        private void Receive(long nowMs)
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
                    case MsgType.Input: OnInput(r); break;
                    case MsgType.Hash: OnHash(r, nowMs); break;
                    case MsgType.Dump: OnDump(r, nowMs); break;
                    default: continue; // unknown type, drop it
                }

                if (r.Ok) lastRecvMs = nowMs;
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
