using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using WordCraft.Server;

// Real HTTP + WebSocket boundary checks, no NuGet test framework or external service.
var clock = new ManualClock();
var lobby = new Lobby(clock);
var waiting = lobby.Create(new Versions(1, 1), false);
clock.Advance(TimeSpan.FromMinutes(6));
ExpectLobbyError(() => lobby.Status(waiting.PlayerToken), 401);
var slowHost = lobby.Create(new Versions(1, 1), true);
var slowGuest = lobby.Create(new Versions(1, 1), true);
clock.Advance(TimeSpan.FromSeconds(61));
ExpectLobbyError(() => lobby.Status(slowGuest.PlayerToken), 401);
var activeHost = lobby.Create(new Versions(1, 1), true);
var activeGuest = lobby.Create(new Versions(1, 1), true);
lobby.Attach(activeHost.PlayerToken);
lobby.Attach(activeGuest.PlayerToken);
clock.Advance(TimeSpan.FromMinutes(121));
ExpectLobbyError(() => lobby.Status(activeHost.PlayerToken), 401);
for (var i = 0; i < 128; i++) lobby.Create(new Versions(1, 1), false);
ExpectLobbyError(() => lobby.Create(new Versions(1, 1), false), 503);
clock.Advance(TimeSpan.FromMinutes(6));
Assert(lobby.Create(new Versions(1, 1), false).State == "waiting", "capacity reclaimed");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var url = $"http://127.0.0.1:{port}";
var server = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net8.0/WordCraft.Server.dll"));
using var process = Process.Start(new ProcessStartInfo("dotnet")
{
    ArgumentList = { server, "--urls", url }, UseShellExecute = false,
    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
}) ?? throw new Exception("Server failed to start.");
var stdout = process.StandardOutput.ReadToEndAsync();
var stderr = process.StandardError.ReadToEndAsync();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var ct = timeout.Token;
using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(url) };
try
{
    while (true)
    {
        try { using var health = await http.GetAsync("/health", ct); health.EnsureSuccessStatusCode(); break; }
        catch (HttpRequestException) { if (process.HasExited) throw new Exception(await stderr + await stdout); await Task.Delay(50, ct); }
    }
    using (var invalid = await http.PostAsJsonAsync("/rooms", new Versions(0, 1), ct)) Assert(invalid.StatusCode == HttpStatusCode.BadRequest, "zero version");
    using (var denied = await http.GetAsync("/session", ct)) Assert(denied.StatusCode == HttpStatusCode.Unauthorized, "missing auth");
    var host = await Post("/rooms", new Versions(1, 2));
    using (var mismatch = await http.PostAsJsonAsync($"/rooms/{host.RoomCode}/join", new Versions(1, 3), ct)) Assert(mismatch.StatusCode == HttpStatusCode.Conflict, "room version mismatch");
    var guest = await Post($"/rooms/{host.RoomCode}/join", new Versions(1, 2));
    var matched = await Status(host.PlayerToken);
    Assert(matched.MatchId == guest.MatchId && matched.Seed == guest.Seed && guest.PeerId == 1 && matched.PeerId == 0, "shared match");
    using (var third = await http.PostAsJsonAsync($"/rooms/{host.RoomCode}/join", new Versions(1, 2), ct)) Assert(third.StatusCode == HttpStatusCode.NotFound, "third peer rejected");
    using var a = await Connect(host.PlayerToken);
    using var b = await Connect(guest.PlayerToken);
    using (var duplicate = new ClientWebSocket())
    {
        duplicate.Options.SetRequestHeader("Authorization", "Bearer " + host.PlayerToken);
        try { await duplicate.ConnectAsync(new Uri(url.Replace("http:", "ws:") + "/relay"), ct); throw new Exception("duplicate accepted"); }
        catch (WebSocketException) { }
    }
    var payload = Enumerable.Range(0, Lobby.MaxPayload).Select(i => (byte)i).ToArray();
    await a.SendAsync(payload.AsMemory(0, 100), WebSocketMessageType.Binary, false, ct);
    await a.SendAsync(payload.AsMemory(100), WebSocketMessageType.Binary, true, ct);
    var received = new byte[payload.Length];
    var total = 0;
    WebSocketReceiveResult frame;
    do { frame = await b.ReceiveAsync(new ArraySegment<byte>(received, total, received.Length - total), ct); total += frame.Count; } while (!frame.EndOfMessage);
    Assert(payload.SequenceEqual(received), "fragmented max-size binary relay");
    await b.SendAsync(new byte[] { 3, 2, 1 }, WebSocketMessageType.Binary, true, ct);
    var reverse = await a.ReceiveAsync(new ArraySegment<byte>(received), ct);
    Assert(reverse.Count == 3 && received[0] == 3, "bidirectional relay");
    await Delete(host.PlayerToken);
    using (var expired = await Request(HttpMethod.Get, "/session", guest.PlayerToken)) Assert(expired.StatusCode == HttpStatusCode.Unauthorized, "cancel invalidates both peers");
    var q1 = await Post("/queue", new Versions(1, 2));
    var different = await Post("/queue", new Versions(1, 3));
    Assert((await Status(q1.PlayerToken)).State == "waiting", "versions isolated");
    var q2 = await Post("/queue", new Versions(1, 2));
    Assert((await Status(q1.PlayerToken)).MatchId == q2.MatchId, "quick match");
    await Delete(q1.PlayerToken);
    await Delete(different.PlayerToken);
    // A disconnected socket destroys its room and its partner's authorization.
    var d1 = await Post("/queue", new Versions(1, 2));
    var d2 = await Post("/queue", new Versions(1, 2));
    using var disconnected = await Connect(d1.PlayerToken);
    using var partner = await Connect(d2.PlayerToken);
    await disconnected.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    try { await partner.ReceiveAsync(new ArraySegment<byte>(received), ct); } catch (WebSocketException) { }
    using (var gone = await Request(HttpMethod.Get, "/session", d2.PlayerToken)) Assert(gone.StatusCode == HttpStatusCode.Unauthorized, "disconnect cleanup");
    var oversizeHost = await Post("/queue", new Versions(1, 2));
    var oversizeGuest = await Post("/queue", new Versions(1, 2));
    using var badSender = await Connect(oversizeHost.PlayerToken);
    using var badPartner = await Connect(oversizeGuest.PlayerToken);
    await badSender.SendAsync(new byte[Lobby.MaxPayload + 1], WebSocketMessageType.Binary, true, ct);
    try { await badPartner.ReceiveAsync(new ArraySegment<byte>(received), ct); } catch (WebSocketException) { }
    using (var gone = await Request(HttpMethod.Get, "/session", oversizeGuest.PlayerToken)) Assert(gone.StatusCode == HttpStatusCode.Unauthorized, "oversize closes match");
    Console.WriteLine("PASS: TTL/connection grace/match lifetime/capacity reclamation, health, validation, auth, room/full/version checks, seed/roles, max/fragmented binary, reverse relay, duplicate socket, cancel, queue/version isolation, disconnect cleanup, oversized message rejection.");
}
finally
{
    if (!process.HasExited) process.Kill(true);
    await process.WaitForExitAsync();
    if (timeout.IsCancellationRequested) Console.Error.WriteLine(await stdout + await stderr);
}
async Task<Ticket> Post(string path, Versions versions)
{
    using var response = await http.PostAsJsonAsync(path, versions, ct);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Ticket>(ct))!;
}
Task<HttpResponseMessage> Request(HttpMethod method, string path, string token)
{
    var request = new HttpRequestMessage(method, path);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return http.SendAsync(request, ct);
}
async Task<Ticket> Status(string token)
{
    using var response = await Request(HttpMethod.Get, "/session", token);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Ticket>(ct))!;
}
async Task Delete(string token) { using var response = await Request(HttpMethod.Delete, "/session", token); response.EnsureSuccessStatusCode(); }
async Task<ClientWebSocket> Connect(string token)
{
    var socket = new ClientWebSocket();
    socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
    await socket.ConnectAsync(new Uri(url.Replace("http:", "ws:") + "/relay"), ct);
    return socket;
}
static void Assert(bool success, string name) { if (!success) throw new Exception("FAILED: " + name); }
static void ExpectLobbyError(Action action, int status)
{
    try { action(); throw new Exception("Expected lobby error " + status); }
    catch (LobbyError error) when (error.Status == status) { }
}
sealed class ManualClock : TimeProvider
{
    DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
