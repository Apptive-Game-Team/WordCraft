using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WordCraft.Server;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://localhost:5080");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddSingleton<Lobby>();
builder.Services.AddHostedService<Reaper>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, cancellation) =>
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many requests." }, cancellation);
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (LobbyError error)
    {
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(new { message = error.Message });
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { message = "Invalid request." });
    }
});
app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/rooms", (Versions versions, Lobby lobby) => lobby.Create(versions, false));
app.MapPost("/rooms/{code}/join", (string code, Versions versions, Lobby lobby) => lobby.Join(code, versions));
app.MapPost("/queue", (Versions versions, Lobby lobby) => lobby.Create(versions, true));
app.MapGet("/session", (HttpContext context, Lobby lobby) => lobby.Status(Token(context)));
app.MapDelete("/session", (HttpContext context, Lobby lobby) => { lobby.Cancel(Token(context)); return Results.NoContent(); });
app.MapGet("/relay", async (HttpContext context, Lobby lobby) =>
{
    if (!context.WebSockets.IsWebSocketRequest) throw new LobbyError(400, "WebSocket required.");
    var peer = lobby.Attach(Token(context));
    try
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await lobby.Relay(peer, socket, context.RequestAborted);
    }
    finally { lobby.CancelIfPresent(peer.Token); }
});
app.Run();

static string Token(HttpContext context)
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length != 71)
        throw new LobbyError(401, "A valid bearer token is required.");
    return authorization[7..];
}

namespace WordCraft.Server
{
    sealed class Reaper(Lobby lobby) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(stoppingToken)) lobby.Reap();
        }
    }
}
