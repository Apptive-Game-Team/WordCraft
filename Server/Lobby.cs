using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace WordCraft.Server;

public sealed record Versions(uint ProtocolVersion, uint ContentVersion);
public sealed record Ticket(string PlayerToken, string? RoomCode, string? MatchId, int PeerId,
    string State, uint ProtocolVersion, uint ContentVersion, string Seed);
public sealed class LobbyError(int status, string message) : Exception(message) { public int Status => status; }

public sealed class Lobby(TimeProvider? timeProvider = null)
{
    public const int MaxPayload = 65536;
    const int MaxPlayers = 128;
    readonly object gate = new();
    readonly Dictionary<string, Peer> peers = new(StringComparer.Ordinal);
    readonly Dictionary<string, Peer> rooms = new(StringComparer.Ordinal);
    readonly List<Peer> queue = [];
    DateTimeOffset Now => (timeProvider ?? TimeProvider.System).GetUtcNow();

    public sealed class Peer(string token, Versions versions, DateTimeOffset created)
    {
        public string Token { get; } = token;
        public Versions Versions { get; } = versions;
        public DateTimeOffset Created { get; } = created;
        public string? Code;
        public Match? Match;
        public int Id;
        public bool Attached;
        public Channel<byte[]> Outgoing { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    }
    public sealed class Match(Peer host, Peer guest, DateTimeOffset created)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Seed { get; } = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)).ToString(CultureInfo.InvariantCulture);
        public DateTimeOffset Created { get; } = created;
        public Peer[] Peers { get; } = [host, guest];
        public CancellationTokenSource End { get; } = new();
    }
    public Ticket Create(Versions versions, bool quick)
    {
        Validate(versions);
        lock (gate)
        {
            ReapLocked();
            if (peers.Count >= MaxPlayers) throw new LobbyError(503, "Server capacity reached.");
            var peer = new Peer(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), versions, Now);
            peers.Add(peer.Token, peer);
            if (quick)
            {
                var host = queue.FirstOrDefault(p => p.Versions == versions);
                if (host == null) queue.Add(peer);
                else { queue.Remove(host); Pair(host, peer); }
            }
            else
            {
                do { peer.Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)); } while (rooms.ContainsKey(peer.Code));
                rooms.Add(peer.Code, peer);
            }
            return View(peer);
        }
    }
    public Ticket Join(string code, Versions versions)
    {
        Validate(versions);
        if (code.Length != 10 || !code.All(Uri.IsHexDigit)) throw new LobbyError(400, "Invalid room code.");
        lock (gate)
        {
            ReapLocked();
            if (!rooms.TryGetValue(code.ToUpperInvariant(), out var host)) throw new LobbyError(404, "Room unavailable.");
            if (host.Versions != versions) throw new LobbyError(409, "Game versions do not match.");
            if (peers.Count >= MaxPlayers) throw new LobbyError(503, "Server capacity reached.");
            var guest = new Peer(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), versions, Now) { Code = host.Code };
            peers.Add(guest.Token, guest);
            rooms.Remove(host.Code!);
            Pair(host, guest);
            return View(guest);
        }
    }
    static void Validate(Versions versions)
    {
        if (versions.ProtocolVersion == 0 || versions.ContentVersion == 0) throw new LobbyError(400, "Positive game versions required.");
    }
    void Pair(Peer host, Peer guest)
    {
        var match = new Match(host, guest, Now);
        host.Match = guest.Match = match;
        guest.Id = 1;
    }
    static Ticket View(Peer peer) => new(peer.Token, peer.Code, peer.Match?.Id, peer.Id,
        peer.Match == null ? "waiting" : "matched", peer.Versions.ProtocolVersion, peer.Versions.ContentVersion, peer.Match?.Seed ?? "0");
    Peer Find(string token) => peers.TryGetValue(token, out var peer) ? peer : throw new LobbyError(401, "Session expired or invalid.");
    public Ticket Status(string token) { lock (gate) { ReapLocked(); return View(Find(token)); } }
    public Peer Attach(string token)
    {
        lock (gate)
        {
            ReapLocked();
            var peer = Find(token);
            if (peer.Match == null) throw new LobbyError(409, "Match is not ready.");
            if (peer.Attached) throw new LobbyError(409, "Player already connected.");
            peer.Attached = true;
            return peer;
        }
    }
    public void Cancel(string token) { lock (gate) Remove(Find(token)); }
    public void CancelIfPresent(string token) { lock (gate) { if (peers.TryGetValue(token, out var peer)) Remove(peer); } }
    void Remove(Peer peer)
    {
        foreach (var member in peer.Match?.Peers ?? [peer])
        {
            peers.Remove(member.Token);
            queue.Remove(member);
            if (member.Code != null) rooms.Remove(member.Code);
            member.Outgoing.Writer.TryComplete();
        }
        peer.Match?.End.Cancel();
    }
    public void Reap() { lock (gate) ReapLocked(); }
    void ReapLocked()
    {
        var now = Now;
        foreach (var peer in peers.Values.ToArray())
        {
            var match = peer.Match;
            if (match == null ? now - peer.Created > TimeSpan.FromMinutes(5)
                : now - match.Created > TimeSpan.FromHours(2) ||
                  (now - match.Created > TimeSpan.FromSeconds(60) && match.Peers.Any(p => !p.Attached))) Remove(peer);
        }
    }
    public async Task Relay(Peer peer, WebSocket socket, CancellationToken aborted)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(aborted, peer.Match!.End.Token);
        var receiver = Receive();
        var sender = Send();
        try { await Task.WhenAny(receiver, sender); }
        finally
        {
            lifetime.Cancel();
            socket.Abort();
            try { await Task.WhenAll(receiver, sender); }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException or ChannelClosedException) { }
        }
        async Task Receive()
        {
            var buffer = new byte[MaxPayload];
            var window = Environment.TickCount64;
            var messages = 0;
            while (!lifetime.IsCancellationRequested)
            {
                var count = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), lifetime.Token);
                    if (result.MessageType != WebSocketMessageType.Binary) return;
                    count += result.Count;
                    if (count == MaxPayload && !result.EndOfMessage) return;
                } while (!result.EndOfMessage);
                var now = Environment.TickCount64;
                if (now - window >= 1000) { window = now; messages = 0; }
                if (++messages > 256) return;
                if (count == 0 || !peer.Match.Peers[1 - peer.Id].Outgoing.Writer.TryWrite(buffer[..count])) return;
            }
        }
        async Task Send()
        {
            await foreach (var payload in peer.Outgoing.Reader.ReadAllAsync(lifetime.Token))
                await socket.SendAsync(payload, WebSocketMessageType.Binary, true, lifetime.Token);
        }
    }
}
