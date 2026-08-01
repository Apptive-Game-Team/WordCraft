using System;
using System.Net;
using System.Net.Sockets;

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
}
