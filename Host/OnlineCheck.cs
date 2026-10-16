using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WordCraft.Net;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Host
{
    internal static class OnlineCheck
    {
        private sealed class Ticket
        {
            public string PlayerToken { get; set; }
            public string MatchId { get; set; }
            public string Seed { get; set; }
            public string State { get; set; }
            public int PeerId { get; set; }
        }

        public static async Task<int> Run(string server)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var http = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
            string aToken = null, bToken = null;
            try
            {
                async Task<Ticket> Join()
                {
                    using var response = await http.PostAsJsonAsync("queue", new
                    { protocolVersion = LockstepSession.ProtocolVersion, contentVersion = FactionData.ContentVersion }, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadFromJsonAsync<Ticket>(cancellationToken: timeout.Token);
                }
                Ticket a = await Join(); aToken = a.PlayerToken;
                Ticket b = await Join(); bToken = b.PlayerToken;
                using var status = new HttpRequestMessage(HttpMethod.Get, "session");
                status.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aToken);
                using var result = await http.SendAsync(status, timeout.Token);
                result.EnsureSuccessStatusCode();
                a = await result.Content.ReadFromJsonAsync<Ticket>(cancellationToken: timeout.Token);
                if (a.State != "matched" || a.MatchId != b.MatchId || a.Seed != b.Seed || a.PeerId == b.PeerId)
                    throw new Exception("Matchmaking did not pair two distinct peers with the same seed.");
                var endpoint = new UriBuilder(new Uri(http.BaseAddress, "relay"));
                endpoint.Scheme = endpoint.Scheme == "https" ? "wss" : "ws";
                using var linkA = await RelayTransport.ConnectAsync(endpoint.Uri, aToken, timeout.Token);
                using var linkB = await RelayTransport.ConnectAsync(endpoint.Uri, bToken, timeout.Token);
                ulong seed = ulong.Parse(a.Seed);
                World wa = MatchScenario.Build(seed, Faction.TreeSpirits, Faction.Hellfire);
                World wb = MatchScenario.Build(seed, Faction.TreeSpirits, Faction.Humans);
                var sa = new LockstepSession(wa, linkA, new MatchConfig
                { Seed = seed, LocalFaction = Faction.TreeSpirits, TimeoutMs = 10000 }, 0, MatchScenario.Build);
                var sb = new LockstepSession(wb, linkB, new MatchConfig
                { Seed = seed, LocalFaction = Faction.Humans, TimeoutMs = 10000 }, 1, MatchScenario.Build);
                var clock = Stopwatch.StartNew();
                const int ticks = 400;
                bool issued = false;
                while (wa.Tick < ticks || wb.Tick < ticks)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    long now = clock.ElapsedMilliseconds;
                    sa.Update(now); sb.Update(now);
                    wa = sa.World; wb = sb.World;
                    if (linkA.Failure != null || linkB.Failure != null) throw new Exception(linkA.Failure ?? linkB.Failure);
                    if (sa.State == SessionState.Stopped || sb.State == SessionState.Stopped)
                        throw new Exception(sa.StopReason ?? sb.StopReason);
                    if (!issued && sa.State == SessionState.Running && sb.State == SessionState.Running)
                    {
                        if (wa.Hash() != wb.Hash()) throw new Exception("Negotiated starting worlds differ.");
                        for (int i = 0; i < wa.EntityCount; i++)
                        {
                            Entity e = wa.GetEntity(i);
                            if (e.Kind == EntityKind.Worker)
                            {
                                var position = new FixVec2(Fix.FromInt(25), Fix.FromInt(25));
                                (e.Owner == 0 ? sa : sb).Issue(CommandType.Move, e.Id, position);
                            }
                        }
                        issued = true;
                    }
                    if (wa.Tick < ticks) sa.TryStep(now);
                    if (wb.Tick < ticks) sb.TryStep(now);
                    await Task.Delay(2, timeout.Token);
                }
                if (!issued || wa.Hash() != wb.Hash()) throw new Exception("Relayed game states diverged.");
                Console.WriteLine("OK: online queue + authenticated relay + " + ticks +
                    " lockstep ticks and player commands, hash 0x" + wa.Hash().ToString("X16"));
                linkA.Dispose();
                while (linkB.Failure == null) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(10, timeout.Token); }
                Console.WriteLine("OK: opponent disconnect closes the relay");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL: online check: " + ex.Message); return 1; }
            finally
            {
                foreach (string token in new[] { aToken, bToken })
                {
                    if (token == null) continue;
                    using var request = new HttpRequestMessage(HttpMethod.Delete, "session");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { using var response = await http.SendAsync(request, cleanup.Token); }
                    catch (HttpRequestException) { }
                    catch (OperationCanceledException) { }
                }
            }
        }
    }
}
