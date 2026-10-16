using System;
using System.Collections;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    public sealed partial class MatchRunner
    {
        [Serializable]
        private sealed class MatchRequest
        {
            public uint protocolVersion = LockstepSession.ProtocolVersion;
            public uint contentVersion = FactionData.ContentVersion;
        }

        [Serializable]
        private sealed class MatchTicket
        {
            public string playerToken, roomCode, matchId, state, seed;
            public int peerId;
            public uint protocolVersion, contentVersion;
        }

        public bool OnlineBusy { get; private set; }
        public string OnlineStatus { get; private set; }
        public string OnlineRoomCode { get; private set; }
        private int onlineGeneration;
        private string onlineToken, onlineBase;
        private UnityWebRequest onlineRequest;
        private bool onlineCreating;
        private int pendingCreations;
        private int pendingReleases;
        private string cleanupServer, cleanupToken;
        private readonly System.Collections.Generic.List<UnityWebRequest> abandonedCreations =
            new System.Collections.Generic.List<UnityWebRequest>();
        private CancellationTokenSource onlineConnect;
        private System.Threading.Tasks.Task<RelayTransport> pendingRelay;

        public void BeginOnline(string server, string room, bool create, Faction faction)
        {
            if (pendingCreations > 0 || pendingReleases > 0)
            {
                OnlineStatus = "Finishing the previous request. Please try again in a moment.";
                return;
            }
            if (cleanupToken != null)
            {
                OnlineStatus = "Retrying the previous cancellation. Search again once it finishes.";
                StartCoroutine(DeleteOnlineSession(cleanupServer, cleanupToken));
                return;
            }
            if (!Uri.TryCreate(server.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                FailOnline("Use an HTTPS server URL (HTTP is allowed for localhost).");
                return;
            }
            ShowStart();
            if (pendingReleases > 0)
            {
                OnlineStatus = "Finishing the previous request. Please try again in a moment.";
                return;
            }
            onlineBase = uri.AbsoluteUri.TrimEnd('/');
            OnlineBusy = true;
            OnlineStatus = "Contacting matchmaking server...";
            OnlineRoomCode = null;
            int generation = onlineGeneration;
            string path = create ? "/rooms" : string.IsNullOrWhiteSpace(room)
                ? "/queue" : "/rooms/" + Uri.EscapeDataString(room.Trim().ToUpperInvariant()) + "/join";
            StartCoroutine(FindOnlineMatch(path, faction, generation));
        }

        private IEnumerator FindOnlineMatch(string path, Faction faction, int generation)
        {
            bool first = true;
            string server = onlineBase;
            while (generation == onlineGeneration)
            {
                var request = new UnityWebRequest(server + (first ? path : "/session"), first ? "POST" : "GET");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                if (first)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new MatchRequest())));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                else request.SetRequestHeader("Authorization", "Bearer " + onlineToken);
                onlineRequest = request;
                onlineCreating = first;
                if (first) pendingCreations++;
                yield return request.SendWebRequest();
                if (generation != onlineGeneration)
                {
                    // A cancelled creation still receives its token, then removes
                    // the abandoned seat before a new search can match with it.
                    if (first)
                    {
                        MatchTicket abandoned = null;
                        try
                        {
                            if (request.result == UnityWebRequest.Result.Success)
                                abandoned = JsonUtility.FromJson<MatchTicket>(request.downloadHandler.text);
                        }
                        catch (ArgumentException) { }
                        if (!string.IsNullOrEmpty(abandoned?.playerToken))
                            yield return DeleteOnlineSession(server, abandoned.playerToken);
                        abandonedCreations.Remove(request);
                        request.Dispose();
                        pendingCreations--;
                    }
                    yield break;
                }
                onlineRequest = null;
                onlineCreating = false;
                if (first) pendingCreations--;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    long responseCode = request.responseCode;
                    request.Dispose();
                    FailOnline("Matchmaking failed (" + responseCode + "). Check the server and try again.");
                    yield break;
                }
                MatchTicket ticket = null;
                try { ticket = JsonUtility.FromJson<MatchTicket>(request.downloadHandler.text); }
                catch (ArgumentException) { }
                request.Dispose();
                if (ticket == null || ticket.protocolVersion != LockstepSession.ProtocolVersion ||
                    ticket.contentVersion != FactionData.ContentVersion ||
                    (ticket.state != "waiting" && ticket.state != "matched") ||
                    (first && string.IsNullOrEmpty(ticket.playerToken)))
                {
                    FailOnline("The server returned an incompatible match.");
                    yield break;
                }
                if (first) onlineToken = ticket.playerToken;
                first = false;
                OnlineRoomCode = ticket.roomCode;
                if (ticket.state == "matched")
                {
                    if (ticket.peerId < 0 || ticket.peerId > 1 || !ulong.TryParse(ticket.seed, out ulong seed))
                    {
                        FailOnline("The match settings are invalid.");
                        yield break;
                    }
                    OnlineStatus = "Opponent found. Connecting...";
                    onlineConnect = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var relayUri = new UriBuilder(server + "/relay");
                    relayUri.Scheme = relayUri.Scheme == "https" ? "wss" : "ws";
                    var connect = RelayTransport.ConnectAsync(relayUri.Uri, onlineToken, onlineConnect.Token);
                    pendingRelay = connect;
                    while (!connect.IsCompleted) yield return null;
                    if (generation != onlineGeneration)
                    {
                        if (connect.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) connect.Result.Dispose();
                        else if (connect.IsFaulted) { var ignored = connect.Exception; }
                        yield break;
                    }
                    onlineConnect.Dispose();
                    onlineConnect = null;
                    pendingRelay = null;
                    if (connect.IsCanceled || connect.IsFaulted)
                    {
                        if (connect.IsFaulted) { var ignored = connect.Exception; }
                        FailOnline("Could not connect to the match relay. Try again.");
                        yield break;
                    }
                    StartRelayedMatch(connect.Result, faction, ticket.peerId, seed);
                    OnlineBusy = false;
                    OnlineStatus = "Waiting for the opponent's handshake...";
                    yield break;
                }
                OnlineStatus = string.IsNullOrEmpty(OnlineRoomCode) ? "Finding an opponent..." : "Waiting for a player. Room: " + OnlineRoomCode;
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        private void FailOnline(string reason)
        {
            OnlineStatus = reason;
            OnlineBusy = false;
            ReleaseOnlineSession();
            if (stopAtTick > 0)
            {
                Debug.LogError(reason);
                Application.Quit(1);
            }
        }

        private void StartRelayedMatch(RelayTransport link, Faction faction, int peer, ulong seed)
        {
            CloseTransport();
            transport = link;
            config = new MatchConfig { LocalFaction = faction, Seed = seed, InputDelay = 5, TimeoutMs = 30000 };
            peerTimeoutMs = 15000;
            LocalPeer = peer;
            Faction other = MatchConfig.DefaultFaction(1 - peer);
            World = MatchScenario.Build(seed, peer == 0 ? faction : other, peer == 0 ? other : faction);
            Session = new LockstepSession(World, transport, config, peer, MatchScenario.Build);
            Link = "online relay";
            Phase = Phase.Start;
            ResetMatchState();
        }

        private void StopOnline(bool release = true)
        {
            onlineGeneration++;
            if (!onlineCreating || !release)
            {
                onlineRequest?.Abort();
                onlineRequest?.Dispose();
            }
            else if (onlineRequest != null) abandonedCreations.Add(onlineRequest);
            if (!release)
            {
                foreach (var request in abandonedCreations) { request.Abort(); request.Dispose(); }
                abandonedCreations.Clear();
            }
            onlineRequest = null;
            onlineCreating = false;
            onlineConnect?.Cancel();
            onlineConnect?.Dispose();
            onlineConnect = null;
            if (pendingRelay != null)
            {
                _ = pendingRelay.ContinueWith(task =>
                {
                    if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) task.Result.Dispose();
                    else if (task.IsFaulted) { var ignored = task.Exception; }
                }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
                pendingRelay = null;
            }
            if (release) ReleaseOnlineSession();
            else onlineToken = null;
            OnlineBusy = false;
            OnlineRoomCode = null;
            OnlineStatus = null;
        }

        private void ReleaseOnlineSession()
        {
            if (!string.IsNullOrEmpty(onlineToken) && !string.IsNullOrEmpty(onlineBase))
                StartCoroutine(DeleteOnlineSession(onlineBase, onlineToken));
            onlineToken = null;
        }

        private IEnumerator DeleteOnlineSession(string server, string token)
        {
            pendingReleases++;
            try
            {
                using (var request = UnityWebRequest.Delete(server + "/session"))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                    request.timeout = 5;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success || request.responseCode == 401 || request.responseCode == 404)
                    {
                        if (cleanupToken == token) { cleanupServer = null; cleanupToken = null; }
                    }
                    else
                    {
                        // Retain the seat until cancellation is acknowledged;
                        // a new search must not pair with this client's old seat.
                        cleanupServer = server;
                        cleanupToken = token;
                        OnlineStatus = "Cancellation could not reach the server. Search again to retry it.";
                    }
                }
            }
            finally { pendingReleases--; }
        }
    }
}
