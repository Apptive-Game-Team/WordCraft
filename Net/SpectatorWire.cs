using System;
using System.Collections.Generic;
using WordCraft.Sim;

namespace WordCraft.Net
{
    /// <summary>
    /// The watch wire, both ends of it, described once so the two ends cannot
    /// drift apart. Big-endian, fixed width, explicitly signed, the same rules
    /// the match wire follows and for the same reason.
    ///
    ///   Watch    watcher -> peer   u32 version, i32 have
    ///   Welcome  peer -> watcher   u32 version, u32 sim, u32 content, u64 seed,
    ///                              u8 peer0Faction, u8 peer1Faction,
    ///                              i32 oldestTick, i32 latestTick
    ///   Frame    peer -> watcher   u16 blockCount, then per block
    ///                              i32 tick, u16 commandCount, then per command
    ///                              u8 type, u8 peerId, i32 entityId, i32 seq,
    ///                              i64 targetX, i64 targetY, i32 arg
    ///
    /// A frame command carries its peer id and the match wire's does not: an
    /// Input block is one peer's own commands and the envelope names it, while
    /// a confirmed frame is both peers' merged, and the peer id is what orders
    /// them. Dropping it would leave a watcher applying one peer's orders as the
    /// other's, which is a desync with every byte delivered.
    /// </summary>
    internal static class FeedWire
    {
        /// <summary>
        /// Bump on any change to the three messages above. Deliberately not
        /// LockstepSession.ProtocolVersion: the match wire has not changed here
        /// and bumping it would reject peers that are in fact compatible, and
        /// the two mismatches want opposite outcomes anyway — a peer that speaks
        /// the wrong match wire ends the match, a watcher that speaks the wrong
        /// watch wire is turned away and the match carries on.
        /// </summary>
        public const uint Version = 1;

        public const int FrameHeaderBytes = 3;  // msgType, blockCount
        public const int BlockHeaderBytes = 6;  // tick, commandCount
        public const int CommandBytes = 30;     // type, peerId, entityId, seq, targetX, targetY, arg

        /// <summary>The same ceiling OnInput enforces, for the same reason: a hostile count must not allocate.</summary>
        public const int MaxCommandsPerTick = 256;
    }

    /// <summary>
    /// The watching side of a match, running on a peer that is playing. It
    /// answers whoever shows up on its socket and sends them the confirmed
    /// frames its own peer already ran, out of the bounded ring in
    /// <see cref="SpectatorFeed"/>.
    ///
    /// Nothing here can reach the barrier, and that is a property of the wire
    /// rather than of care taken while writing this. A watcher says two things:
    /// "I am here", which bounds how long this keeps sending to it, and "I hold
    /// everything through tick N", which bounds which bytes are sent. There is
    /// no third thing it can say, so there is nothing for a peer to wait on.
    /// <see cref="Update"/> returns void for the same reason — a caller cannot
    /// be told to try again later, because there is no later.
    ///
    /// Work is bounded on every axis a vanished or hostile watcher could push:
    /// at most <see cref="MaxWatchers"/> of them, one datagram each per
    /// <see cref="SendIntervalMs"/>, each datagram capped by the MTU, and a
    /// watcher that stops speaking is forgotten after <see cref="TimeoutMs"/>.
    /// A watcher that falls further behind than the ring is deep is not chased:
    /// the frames leave the window, this keeps sending what it still has, and
    /// the drop happens on the watcher, which is the whole argument for the
    /// bounded ring wearing its socket clothes.
    /// </summary>
    public sealed class FeedPublisher
    {
        /// <summary>A LAN match's worth of onlookers. The cost of one is a table row and a datagram.</summary>
        public const int MaxWatchers = 8;

        /// <summary>Silence this long and a watcher is forgotten. It cannot say goodbye reliably.</summary>
        public int TimeoutMs = 3000;

        /// <summary>Fastest one watcher is sent to. Twice a tick at 20 Hz, so loss is covered by the next one.</summary>
        public int SendIntervalMs = 20;

        /// <summary>
        /// Frames per datagram, held under the MTU by the budget below. Sized so
        /// a watcher that stalled catches up faster than the match advances;
        /// smaller and a watcher that fell behind never closes the gap.
        /// </summary>
        private const int MaxBlocksPerDatagram = 64;

        private struct Watcher
        {
            public bool Live;
            public int Endpoint;
            public int Have;        // what it last said it holds, -1 for nothing
            public long LastHeardMs;
            public long LastSentMs;
        }

        private readonly World world;
        private readonly MatchConfig cfg;
        private readonly SpectatorFeed feed;
        private readonly IFanoutTransport transport;
        private readonly Watcher[] watchers = new Watcher[MaxWatchers];
        private readonly Writer w = new Writer();

        /// <summary>
        /// The world is read for the two factions and never written. They are
        /// hashed state, so a watcher that had to guess them would diverge on
        /// tick 1 with every frame it received correct, which is why they travel
        /// in the Welcome rather than being assumed from a config file.
        /// </summary>
        public FeedPublisher(World world, MatchConfig cfg, SpectatorFeed feed, IFanoutTransport transport)
        {
            this.world = world;
            this.cfg = cfg;
            this.feed = feed;
            this.transport = transport;
        }

        /// <summary>How many watchers are being sent to right now.</summary>
        public int WatcherCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < watchers.Length; i++)
                {
                    if (watchers[i].Live) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Takes whatever arrived, forgets whoever stopped speaking, and sends.
        /// Call it as often as convenient: the send pacing is in here, so the
        /// rate is the match's rather than the caller's loop's.
        /// </summary>
        public void Update(long nowMs)
        {
            Receive(nowMs);

            for (int i = 0; i < watchers.Length; i++)
            {
                if (!watchers[i].Live) continue;
                if (nowMs - watchers[i].LastHeardMs > TimeoutMs)
                {
                    // Gone without a goodbye. Nothing to unwind: it was never
                    // part of anything, so forgetting it is the whole cleanup.
                    watchers[i].Live = false;
                    continue;
                }
                SendFrames(i, nowMs);
            }
        }

        private void Receive(long nowMs)
        {
            while (transport.TryReceive(out byte[] packet, out int from))
            {
                var r = new Reader(packet, packet.Length);
                var type = (MsgType)r.U8();
                if (!r.Ok || type != MsgType.Watch) continue; // nothing else belongs on this socket

                uint version = r.U32();
                int have = r.I32();
                if (!r.Ok) continue;

                if (version != FeedWire.Version)
                {
                    // Turned away rather than tolerated, and the match does not
                    // notice. A watcher on the wrong wire would misread frames
                    // and report hashes that disagree for no reason worth chasing.
                    w.Reset(MsgType.Reject);
                    w.Text("watch wire version " + version + ", expected " + FeedWire.Version);
                    transport.SendTo(from, w.Buf, w.Length);
                    continue;
                }

                int slot = SlotFor(from);
                if (slot < 0) continue; // full: the ninth watcher is refused, never swapped in

                watchers[slot].Have = have;
                watchers[slot].LastHeardMs = nowMs;

                // Answered every time it asks, because a lost Welcome has no
                // other repair: the watcher cannot tell us it never arrived, and
                // it cannot build its world without one. Before the first tick
                // there is nothing truthful to say — the factions are settled at
                // BeginMatch — so it waits for the next keepalive.
                if (feed.LatestTick >= 0) SendWelcome(from);
            }
        }

        private int SlotFor(int endpoint)
        {
            int free = -1;
            for (int i = 0; i < watchers.Length; i++)
            {
                if (watchers[i].Live && watchers[i].Endpoint == endpoint) return i;
                if (!watchers[i].Live && free < 0) free = i;
            }
            if (free < 0) return -1;

            watchers[free] = new Watcher
            {
                Live = true,
                Endpoint = endpoint,
                Have = -1,
                LastSentMs = long.MinValue / 2,
            };
            return free;
        }

        private void SendWelcome(int endpoint)
        {
            w.Reset(MsgType.Welcome);
            w.U32(FeedWire.Version);
            w.U32(cfg.SimVersion);
            w.U32(cfg.ContentVersion);
            w.U64(cfg.Seed);
            w.U8((byte)world.FactionOf(0));
            w.U8((byte)world.FactionOf(1));
            // The window, so a watcher that arrived too late is told so instead
            // of stepping nothing and dropping with a reason about a ring it has
            // never seen. There is no state transfer: a match whose oldest
            // readable frame is not tick 0 cannot be joined at all.
            w.I32(feed.OldestTick);
            w.I32(feed.LatestTick);
            transport.SendTo(endpoint, w.Buf, w.Length);
        }

        private void SendFrames(int slot, long nowMs)
        {
            int latest = feed.LatestTick;
            if (latest < 0) return;

            Watcher watcher = watchers[slot];
            if (watcher.Have >= latest) return;                     // it has everything there is
            if (nowMs - watcher.LastSentMs < SendIntervalMs) return; // paced, so a fast caller is not a fast sender

            // From what it last reported, or from the oldest frame still held
            // when it is further behind than that. Re-sending what it may
            // already have is the entire loss recovery on this wire: there is no
            // resend request, because a request is a thing to wait for.
            int first = Math.Max(watcher.Have + 1, feed.OldestTick);
            if (first > latest) return;

            int count = 0;
            int bytes = FeedWire.FrameHeaderBytes;
            for (int t = first; t <= latest && count < MaxBlocksPerDatagram; t++)
            {
                if (!feed.TryFrame(t, out IReadOnlyList<Command> block)) continue;
                int need = FeedWire.BlockHeaderBytes + block.Count * FeedWire.CommandBytes;
                if (bytes + need > Wire.MaxDatagram) break;
                bytes += need;
                count++;
            }
            if (count == 0) return;

            w.Reset(MsgType.Frame);
            w.U16((ushort)count);
            int written = 0;
            for (int t = first; t <= latest && written < count; t++)
            {
                if (!feed.TryFrame(t, out IReadOnlyList<Command> block)) continue;
                w.I32(t);
                w.U16((ushort)block.Count);
                for (int i = 0; i < block.Count; i++)
                {
                    Command c = block[i];
                    w.U8((byte)c.Type);
                    w.U8((byte)c.PeerId);
                    w.I32(c.EntityId);
                    w.I32(c.Seq);
                    w.I64(c.Target.X.Raw); // Fix crosses the wire as its raw long, never as text
                    w.I64(c.Target.Y.Raw);
                    w.I32(c.Arg);
                }
                written++;
            }

            transport.SendTo(watcher.Endpoint, w.Buf, w.Length);
            watchers[slot].LastSentMs = nowMs;
        }
    }

    /// <summary>
    /// The other end: a socket, and a <see cref="SpectatorFeed"/> filled from
    /// it. What comes out is the same bounded ring a peer in this process would
    /// have published into, so <see cref="Spectator"/> steps it without knowing
    /// which side of a wire its frames came from.
    ///
    /// It sends a keepalive and nothing else. The tick in it is a hint about
    /// what not to re-send; it is never an acknowledgement, because nothing on
    /// the far side waits for it, and a peer that started waiting would have
    /// made this a third party to the barrier — the one thing a spectator must
    /// never be.
    ///
    /// A watcher cannot join a match that has already run past the publisher's
    /// window. There is no state transfer, so the only world it can build is one
    /// at tick 0, and the Welcome says whether tick 0 is still readable. Refused
    /// up front and named, rather than stepped into a drop later.
    /// </summary>
    public sealed class FeedSubscriber
    {
        /// <summary>How often we say we are still here. Also how stale the publisher's idea of our progress gets.</summary>
        public int WatchIntervalMs = 100;

        private readonly ITransport transport;
        private readonly SpectatorFeed feed;
        private readonly MatchConfig expect;
        private readonly Writer w = new Writer();
        private readonly List<Command> block = new List<Command>();

        private long lastWatchMs = long.MinValue / 2;
        private bool started;

        /// <summary>
        /// The ring this fills. Hand it to a Spectator; the two are the same
        /// pair a local peer and its watcher already are.
        /// </summary>
        public SpectatorFeed Feed => feed;

        /// <summary>True once the publisher has answered with the match's parameters.</summary>
        public bool Welcomed { get; private set; }

        /// <summary>Why this watcher cannot watch, or null while it can.</summary>
        public string Refusal { get; private set; }

        public ulong Seed { get; private set; }
        public Faction Peer0Faction { get; private set; }
        public Faction Peer1Faction { get; private set; }

        /// <summary>Frames taken off the wire, including the re-sent ones. Diagnostics, never a decision.</summary>
        public int FramesReceived { get; private set; }

        /// <summary>When the publisher was last heard from, so a caller can notice a match that ended.</summary>
        public long LastHeardMs { get; private set; }

        /// <summary>
        /// What this watcher expects the match to be. Only the versions are
        /// checked: the seed and the factions are the match's to state, and are
        /// taken from the Welcome rather than assumed.
        /// </summary>
        public FeedSubscriber(ITransport transport, MatchConfig expect = null, SpectatorFeed feed = null)
        {
            this.transport = transport;
            this.expect = expect ?? new MatchConfig();
            this.feed = feed ?? new SpectatorFeed();
        }

        public void Update(long nowMs)
        {
            if (!started) { started = true; LastHeardMs = nowMs; }
            if (Refusal != null) return;

            if (nowMs - lastWatchMs >= WatchIntervalMs)
            {
                w.Reset(MsgType.Watch);
                w.U32(FeedWire.Version);
                w.I32(feed.ContiguousThrough);
                transport.Send(w.Buf, w.Length);
                lastWatchMs = nowMs;
            }

            while (transport.TryReceive(out byte[] packet))
            {
                var r = new Reader(packet, packet.Length);
                var type = (MsgType)r.U8();
                if (!r.Ok) continue;

                switch (type)
                {
                    case MsgType.Welcome: OnWelcome(r); break;
                    case MsgType.Frame: OnFrame(r); break;
                    case MsgType.Reject: Refuse("turned away by the match: " + r.Text()); break;
                    default: continue; // unknown type, drop it
                }

                if (r.Ok) LastHeardMs = nowMs;
                if (Refusal != null) return;
            }
        }

        private void OnWelcome(Reader r)
        {
            uint version = r.U32();
            uint sim = r.U32();
            uint content = r.U32();
            ulong seed = r.U64();
            byte faction0 = r.U8();
            byte faction1 = r.U8();
            int oldest = r.I32();
            int latest = r.I32();
            if (!r.Ok) return;

            if (Welcomed) return; // already building on the first answer; the rest are repeats

            if (version != FeedWire.Version)
            {
                Refuse("watch wire version " + version + ", expected " + FeedWire.Version);
                return;
            }
            if (sim != expect.SimVersion)
            {
                Refuse("simulation version " + sim + ", expected " + expect.SimVersion);
                return;
            }
            if (content != expect.ContentVersion)
            {
                // A watcher on a different roster steps the same commands into
                // different units. It would report hashes that disagree with the
                // players and be believed, which is worse than not watching.
                Refuse("content version " + content + ", expected " + expect.ContentVersion);
                return;
            }
            if (faction0 >= FactionData.FactionCount || faction1 >= FactionData.FactionCount)
            {
                Refuse("unknown faction id " + faction0 + " or " + faction1);
                return;
            }
            if (oldest > 0)
            {
                Refuse("the match is at tick " + latest + " and its feed starts at tick " + oldest +
                       "; there is no state transfer, so there is no world to start from");
                return;
            }

            Seed = seed;
            Peer0Faction = (Faction)faction0;
            Peer1Faction = (Faction)faction1;
            Welcomed = true;
        }

        private void OnFrame(Reader r)
        {
            int blocks = r.U16();
            if (!r.Ok) return;

            for (int b = 0; b < blocks; b++)
            {
                int tick = r.I32();
                int n = r.U16();
                if (!r.Ok || n > FeedWire.MaxCommandsPerTick) return;

                block.Clear();
                for (int i = 0; i < n; i++)
                {
                    var type = (CommandType)r.U8();
                    int peer = r.U8();
                    int entityId = r.I32();
                    int seq = r.I32();
                    long x = r.I64();
                    long y = r.I64();
                    int arg = r.I32();
                    // The tick comes from the block envelope, the same way an
                    // Input block's does, so a command cannot claim a tick the
                    // frame it arrived in disagrees with.
                    block.Add(new Command(tick, peer, seq, type, entityId, new FixVec2(new Fix(x), new Fix(y)), arg));
                }
                if (!r.Ok) return;

                if (feed.Accept(tick, block)) FramesReceived++;
            }
        }

        private void Refuse(string reason)
        {
            if (Refusal == null) Refusal = reason;
        }
    }
}
