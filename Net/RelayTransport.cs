using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WordCraft.Net
{
    /// <summary>Preserves datagram boundaries over an outbound WebSocket relay.</summary>
    public sealed class RelayTransport : ITransport, IDisposable
    {
        public const int MaxPacket = 65536;
        private const int QueueLimit = 128;
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim ready = new SemaphoreSlim(0);
        private readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        private readonly Queue<byte[]> incoming = new Queue<byte[]>();
        private int disposed, received;
        private string failure;

        public int Received => Volatile.Read(ref received);
        public string Failure => Volatile.Read(ref failure);

        public static async Task<RelayTransport> ConnectAsync(Uri uri, string token, CancellationToken cancellation)
        {
            if (uri == null || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("Relay requires a WebSocket URL.", nameof(uri));
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Missing player token.", nameof(token));
            var link = new RelayTransport();
            try
            {
                link.socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                link.socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                await link.socket.ConnectAsync(uri, cancellation).ConfigureAwait(false);
                _ = Task.Run(link.ReceiveLoop);
                _ = Task.Run(link.SendLoop);
                return link;
            }
            catch { link.Dispose(); throw; }
        }

        public void Send(byte[] data, int length)
        {
            if (data == null || length <= 0 || length > data.Length || length > MaxPacket)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (Volatile.Read(ref disposed) != 0) return;
            lock (outgoing)
            {
                if (outgoing.Count >= QueueLimit) { Fail("Relay send queue is full."); return; }
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                outgoing.Enqueue(copy);
            }
            ready.Release();
        }

        public bool TryReceive(out byte[] packet)
        {
            lock (incoming)
            {
                packet = incoming.Count > 0 ? incoming.Dequeue() : null;
                return packet != null;
            }
        }

        private async Task SendLoop()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    await ready.WaitAsync(lifetime.Token).ConfigureAwait(false);
                    byte[] packet;
                    lock (outgoing) packet = outgoing.Dequeue();
                    await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary,
                        true, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (Volatile.Read(ref disposed) == 0) Fail("Relay connection lost while sending."); }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[MaxPacket];
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    int count = 0;
                    WebSocketReceiveResult part;
                    do
                    {
                        if (count == buffer.Length) { Fail("Relay packet exceeds the size limit."); return; }
                        part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count),
                            lifetime.Token).ConfigureAwait(false);
                        if (part.MessageType == WebSocketMessageType.Close) { Fail("The other player disconnected."); return; }
                        if (part.MessageType != WebSocketMessageType.Binary) { Fail("Unexpected relay message."); return; }
                        count += part.Count;
                    } while (!part.EndOfMessage);
                    if (count == 0) { Fail("Empty relay packet."); return; }
                    var packet = new byte[count];
                    Buffer.BlockCopy(buffer, 0, packet, 0, count);
                    lock (incoming)
                    {
                        if (incoming.Count >= QueueLimit) { Fail("Relay receive queue is full."); return; }
                        incoming.Enqueue(packet);
                    }
                    Interlocked.Increment(ref received);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (Volatile.Read(ref disposed) == 0) Fail("Relay connection lost while receiving."); }
        }

        private void Fail(string reason)
        {
            Interlocked.CompareExchange(ref failure, reason, null);
            Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            socket.Abort();
            socket.Dispose();
        }
    }
}
