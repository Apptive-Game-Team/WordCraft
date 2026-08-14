using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using WordCraft.Sim;

namespace WordCraft.Net
{
    /// <summary>
    /// The one abstraction in this layer. Everything above it is written against
    /// unreliable, unordered datagrams, so the in-memory link and a real socket
    /// are interchangeable and the lockstep logic is testable without sockets.
    /// </summary>
    public interface ITransport
    {
        void Send(byte[] data, int length);
        bool TryReceive(out byte[] packet);
    }

    /// <summary>
    /// A datagram link with more than one party on the other side, where every
    /// party is addressed on its own.
    ///
    /// This is not ITransport with a bigger number. ITransport is the match
    /// link: two players, every byte meant for the other one, and a barrier that
    /// waits for what comes back. Here nothing is waited for, so an endpoint
    /// that stops answering costs a table row and nothing else. Spectators are
    /// the only thing on the other side of this, and that is the point — the
    /// fan-out exists because a read-only consumer can be fanned out to safely.
    /// </summary>
    public interface IFanoutTransport
    {
        /// <summary>
        /// One datagram and the endpoint it came from. Ids are handed out on
        /// first contact and never reused, so an id that has gone quiet is
        /// somebody who left rather than a slot the next arrival inherits.
        ///
        /// That is a contract and not a convenience. FeedPublisher decides
        /// whether an address may be sent frames by deriving a token from its
        /// id, so an implementation that let one id change hands would hand the
        /// second address the first one's proof, and the forged Watch this
        /// interface's users are guarded against would work again.
        /// </summary>
        bool TryReceive(out byte[] packet, out int from);

        /// <summary>Sends to one endpoint. An unknown id is a no-op, never a fault.</summary>
        void SendTo(int endpoint, byte[] data, int length);
    }

    /// <summary>
    /// A link with nothing on the other end, for a solo session. It never sends
    /// and never receives, but a session still holds an ITransport and a null
    /// there would be a crash rather than a quiet nothing.
    /// </summary>
    public sealed class NullTransport : ITransport
    {
        public static readonly NullTransport It = new NullTransport();

        public void Send(byte[] data, int length) { }

        public bool TryReceive(out byte[] packet)
        {
            packet = null;
            return false;
        }
    }

    /// <summary>
    /// Direct IP UDP, no external service. The listening side learns the remote
    /// endpoint from the first datagram it receives.
    /// </summary>
    // ponytail: one remote endpoint, first sender wins. 1v1 is still the target
    // and this class is still where that is nailed down. UdpFanout below now
    // keeps a list of endpoints and sends to each, but those endpoints are
    // spectators — nobody acks, nobody is waited for — so it is not the N-peer
    // version of this class and does not make it one. N *players* needs what it
    // always needed: several remotes here, a per-remote ack in LockstepSession,
    // and a barrier that tests all of them.
    // ponytail: and keep spectators off this socket. First sender wins means a
    // watcher that speaks before the peer does is taken for the peer, and the
    // match then waits forever on somebody who will never send input. That is
    // why UdpFanout binds a port of its own instead of sharing this one.
    public sealed class UdpTransport : ITransport, IDisposable
    {
        private readonly Socket socket;
        private readonly byte[] rx = new byte[Wire.MaxDatagram];
        private EndPoint remote;

        public UdpTransport(int localPort, IPEndPoint remoteEndpoint)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            socket.Blocking = false;
            remote = remoteEndpoint;
        }

        public int LocalPort => ((IPEndPoint)socket.LocalEndPoint).Port;

        /// <summary>
        /// Datagrams accepted from the peer. The session cannot answer "has anyone
        /// answered yet", because it goes from Handshaking straight to Running on
        /// the first valid Hello; only the socket knows the difference between a
        /// start screen still waiting and one already talking to somebody.
        /// </summary>
        public int Received { get; private set; }

        public void Send(byte[] data, int length)
        {
            if (remote == null) return; // listener has not heard from anyone yet
            try { socket.SendTo(data, 0, length, SocketFlags.None, remote); }
            catch (SocketException) { /* a dropped send is just packet loss; resend covers it */ }
        }

        public bool TryReceive(out byte[] packet)
        {
            packet = null;
            try
            {
                if (socket.Available <= 0) return false;
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n = socket.ReceiveFrom(rx, 0, rx.Length, SocketFlags.None, ref from);
                if (remote == null) remote = from;
                else if (!from.Equals(remote)) return false; // stray sender, not our peer
                packet = new byte[n];
                Buffer.BlockCopy(rx, 0, packet, 0, n);
                Received++;
                return true;
            }
            catch (SocketException)
            {
                // WouldBlock, or a Windows ICMP port-unreachable reset. Both mean
                // "no packet right now", never "the match is over".
                return false;
            }
        }

        public void Dispose() => socket.Dispose();
    }

    /// <summary>
    /// Direct IP UDP again, on its own port, with as many endpoints as have
    /// spoken to it. A datagram teaches this socket a new endpoint the way the
    /// first one teaches UdpTransport its peer, but here learning one is cheap:
    /// an endpoint here receives frames and is never waited for, so the worst a
    /// stranger achieves is a table row.
    ///
    /// What that stranger is then sent is FeedPublisher's business, and it is
    /// its business because the address on a datagram is a claim rather than a
    /// fact: the datagrams a stranger "did not ask for" may be landing on
    /// somebody else entirely.
    /// </summary>
    // ponytail: endpoints are learned and never retired, only capped. A watcher
    // that reconnects from a new source port takes a second row, and a port left
    // running for hours fills the table with the dead. FeedPublisher forgets a
    // watcher that stops speaking, which is what keeps sending bounded; reclaiming
    // the row itself needs ids that can be retired, and ids here are never reused.
    // Two things have been added to that price since. A forged Watch is refused
    // before it is fed, but it is refused a layer above this one, after IdOf has
    // already given it a row — so thirty-two forged addresses still fill this table
    // and shut real watchers out. That is a denial of spectating rather than an
    // amplifier aimed at a stranger, and #121 was the second one, so it stands.
    // And retiring an id is no longer merely awkward, it is load bearing:
    // FeedPublisher derives a watcher's token from the id, so an id that outlived
    // one address and was handed to the next would hand over the proof with it.
    // Retire ids only together with a token that mixes in the address itself.
    public sealed class UdpFanout : IFanoutTransport, IDisposable
    {
        /// <summary>Enough for the watchers a LAN match will ever have, and small enough to scan.</summary>
        private const int MaxEndpoints = 32;

        private readonly Socket socket;
        private readonly byte[] rx = new byte[Wire.MaxDatagram];
        private readonly List<EndPoint> endpoints = new List<EndPoint>();

        public UdpFanout(int localPort)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            socket.Blocking = false;
        }

        public int LocalPort => ((IPEndPoint)socket.LocalEndPoint).Port;

        /// <summary>Endpoints this socket has ever heard from. Ids are indexes into that.</summary>
        public int EndpointCount => endpoints.Count;

        public bool TryReceive(out byte[] packet, out int from)
        {
            packet = null;
            from = -1;
            try
            {
                if (socket.Available <= 0) return false;
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                int n = socket.ReceiveFrom(rx, 0, rx.Length, SocketFlags.None, ref sender);

                from = IdOf(sender);
                if (from < 0) return false; // table full: a stranger is refused, never swapped in

                packet = new byte[n];
                Buffer.BlockCopy(rx, 0, packet, 0, n);
                return true;
            }
            catch (SocketException)
            {
                // WouldBlock, or a Windows ICMP port-unreachable reset from a
                // watcher that has gone. Neither is this socket's problem.
                return false;
            }
        }

        public void SendTo(int endpoint, byte[] data, int length)
        {
            if (endpoint < 0 || endpoint >= endpoints.Count) return;
            try { socket.SendTo(data, 0, length, SocketFlags.None, endpoints[endpoint]); }
            catch (SocketException) { /* a watcher's loss to bear; there is no resend request here */ }
        }

        private int IdOf(EndPoint sender)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i].Equals(sender)) return i;
            }
            if (endpoints.Count >= MaxEndpoints) return -1;
            endpoints.Add(sender);
            return endpoints.Count - 1;
        }

        public void Dispose() => socket.Dispose();
    }

    /// <summary>
    /// Fan-out over a fixed set of one to one links, so the harness can put a
    /// spectator on the wire without a socket. Endpoint ids are indexes into the
    /// links handed in, which is all the addressing a test needs, and each link
    /// can be as lossy as the match link is.
    /// </summary>
    public sealed class FanoutLink : IFanoutTransport
    {
        private readonly ITransport[] links;
        private int cursor;

        public FanoutLink(params ITransport[] links)
        {
            this.links = links ?? Array.Empty<ITransport>();
        }

        /// <summary>Round-robin, so one chatty endpoint cannot starve the rest.</summary>
        public bool TryReceive(out byte[] packet, out int from)
        {
            for (int i = 0; i < links.Length; i++)
            {
                cursor = (cursor + 1) % links.Length;
                if (!links[cursor].TryReceive(out packet)) continue;
                from = cursor;
                return true;
            }
            packet = null;
            from = -1;
            return false;
        }

        public void SendTo(int endpoint, byte[] data, int length)
        {
            if (endpoint < 0 || endpoint >= links.Length) return;
            links[endpoint].Send(data, length);
        }
    }

    /// <summary>
    /// Two in-process endpoints joined by a lossy link. Faults are drawn from a
    /// seeded DetRandom and delivery is gated on a virtual clock the driver sets,
    /// so a failing run reproduces exactly instead of depending on wall time.
    /// </summary>
    public sealed class FaultyLink
    {
        private struct Pending
        {
            public long DueMs;
            public byte[] Data;
        }

        private readonly DetRandom rng;
        private readonly int dropPercent;
        private readonly int duplicatePercent;
        private readonly int baseDelayMs;
        private readonly int jitterMs;
        private readonly List<Pending> toA = new List<Pending>();
        private readonly List<Pending> toB = new List<Pending>();

        /// <summary>Virtual milliseconds. The driver advances this, not a clock.</summary>
        public long Now;

        /// <summary>Set true to simulate a peer vanishing without a goodbye.</summary>
        public bool Partitioned;

        public ITransport A { get; }
        public ITransport B { get; }

        public FaultyLink(ulong seed, int dropPercent, int duplicatePercent, int baseDelayMs, int jitterMs)
        {
            rng = new DetRandom(seed);
            this.dropPercent = dropPercent;
            this.duplicatePercent = duplicatePercent;
            this.baseDelayMs = baseDelayMs;
            this.jitterMs = jitterMs;
            A = new Endpoint(this, toB, toA);
            B = new Endpoint(this, toA, toB);
        }

        private void Enqueue(List<Pending> queue, byte[] data, int length)
        {
            if (Partitioned) return;
            if (rng.NextInt(100) < dropPercent) return;

            int copies = rng.NextInt(100) < duplicatePercent ? 2 : 1;
            for (int i = 0; i < copies; i++)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                // Independent per-copy delay: this is what produces reordering.
                queue.Add(new Pending { DueMs = Now + baseDelayMs + rng.NextInt(jitterMs + 1), Data = copy });
            }
        }

        private bool Dequeue(List<Pending> queue, out byte[] packet)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].DueMs > Now) continue;
                packet = queue[i].Data;
                queue.RemoveAt(i);
                return true;
            }
            packet = null;
            return false;
        }

        private sealed class Endpoint : ITransport
        {
            private readonly FaultyLink link;
            private readonly List<Pending> outbox;
            private readonly List<Pending> inbox;

            public Endpoint(FaultyLink link, List<Pending> outbox, List<Pending> inbox)
            {
                this.link = link;
                this.outbox = outbox;
                this.inbox = inbox;
            }

            public void Send(byte[] data, int length) => link.Enqueue(outbox, data, length);
            public bool TryReceive(out byte[] packet) => link.Dequeue(inbox, out packet);
        }
    }
}
