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
    /// Direct IP UDP, no external service. The listening side learns the remote
    /// endpoint from the first datagram it receives.
    /// </summary>
    // ponytail: one remote endpoint, first sender wins. 1v1 is the target; for
    // N peers keep a list of endpoints and fan out the same bytes to each.
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
