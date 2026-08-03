using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Which of the three screens the client is showing. A match is one of them,
    /// not the only one, and the client can go round this loop as many times as
    /// the player likes without the process restarting.
    /// </summary>
    public enum Phase
    {
        Start,
        Match,
        Result,
    }

    /// <summary>
    /// Owns the world and the lockstep session, and is the only thing in this
    /// assembly allowed to advance either. Everything else reads World and calls
    /// Session.Issue. The simulation is never written to from the view.
    ///
    /// Launched with no arguments the client opens the start screen. Arguments
    /// mean "skip it and connect as told", because the headless self-checks and
    /// the two-process LAN test drive the player and cannot click anything.
    ///
    ///   WordCraft.app                 start screen
    ///   WordCraft.app -port 45677     host on that port, no start screen
    ///   WordCraft.app -join 10.0.0.4  join that address
    ///   WordCraft.app -ticks 400      host, stop there and print the state hash
    ///   WordCraft.app -faction Humans play that faction
    /// </summary>
    public sealed class MatchRunner : MonoBehaviour
    {
        public const int DefaultPort = 45677;
        private const int TickMs = 1000 / World.TicksPerSecond;

        /// <summary>
        /// How long a start-screen handshake waits: as long as it takes. The host
        /// is waiting for a person to launch a second client, not for a packet that
        /// is already late, so cancel is the way out of it rather than a timeout.
        /// </summary>
        private const int LobbyTimeoutMs = 24 * 60 * 60 * 1000;

        public static MatchRunner Instance { get; private set; }

        public World World { get; private set; }
        public LockstepSession Session { get; private set; }
        public int LocalPeer { get; private set; }
        public Phase Phase { get; private set; }

        /// <summary>Human readable description of the link, for the HUD.</summary>
        public string Link { get; private set; }

        /// <summary>What ended the match, in the words the result screen shows.</summary>
        public string Outcome { get; private set; }

        /// <summary>True when the session halted rather than the game finishing.</summary>
        public bool Halted { get; private set; }

        /// <summary>Tick the match ended on, held because the session keeps running afterwards.</summary>
        public int EndTick { get; private set; }

        /// <summary>Match length in simulated seconds. Ticks are the clock; the wall is not.</summary>
        public float EndSeconds => EndTick / (float)World.TicksPerSecond;

        /// <summary>True once anything at all has arrived from the other side.</summary>
        public bool PeerHeard => transport != null && transport.Received > 0;

        /// <summary>True between pressing host or join and the handshake settling.</summary>
        public bool Connecting => Phase == Phase.Start && transport != null;

        // Entity positions at the previous and the current tick boundary, in view
        // space. Rendering interpolates between these two; neither is ever read
        // back into the simulation, which only ever holds the tick-boundary value.
        private readonly List<Vector2> previous = new List<Vector2>();
        private readonly List<Vector2> current = new List<Vector2>();

        private UdpTransport transport;

        // The session reads this every Update, so the lobby's patience can be
        // handed back once the match is running.
        private MatchConfig config;
        private int peerTimeoutMs;

        private long startMs = -1;
        private long lastStepMs;
        private bool stopReported;
        private int stopAtTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // The scene is empty on purpose. Building the match here means no
            // gameplay change ever has to touch a scene or prefab asset.
            new GameObject("WordCraft").AddComponent<MatchRunner>();
        }

        /// <summary>Fraction of a tick elapsed since the last one executed, 0 to 1.</summary>
        public float Alpha => Mathf.Clamp01((NowMs() - lastStepMs) / (float)TickMs);

        /// <summary>Interpolated draw position. View only; not a simulation value.</summary>
        public Vector2 DrawPosition(int id) => Vector2.Lerp(previous[id], current[id], Alpha);

        /// <summary>
        /// Nearest alive entity to a world point within radius, or -1. Picks
        /// against the drawn position, because that is what the player aimed at.
        /// </summary>
        public int EntityAt(Vector2 point, float radius, bool mineOnly)
        {
            int best = -1;
            float bestDistance = radius;

            for (int i = 0; i < World.EntityCount; i++)
            {
                Entity e = World.GetEntity(i);
                if (!e.Alive) continue;
                if (mineOnly && e.Owner != LocalPeer) continue;

                float d = Vector2.Distance(point, DrawPosition(i));
                if (d > bestDistance) continue;
                best = i;
                bestDistance = d;
            }
            return best;
        }

        public static Vector2 ToView(FixVec2 p) =>
            new Vector2(p.X.Raw / (float)Fix.One, p.Y.Raw / (float)Fix.One);

        /// <summary>
        /// Rounds a pointer position into fixed point. Safe despite the float:
        /// the raw long produced here is what travels on the wire and what both
        /// peers execute, so the simulation never repeats this conversion.
        /// </summary>
        public static FixVec2 ToSim(Vector2 p) =>
            new FixVec2(new Fix((long)Math.Round(p.x * Fix.One)), new Fix((long)Math.Round(p.y * Fix.One)));

        // ---- screens ----

        /// <summary>
        /// Back to the start screen with nothing carried over: a fresh world, a
        /// fresh session, and a socket closed rather than reused. Restarting has to
        /// be indistinguishable from launching, or the second match of a session is
        /// not the game the first one was.
        /// </summary>
        public void ShowStart()
        {
            transport?.Dispose();
            transport = null;

            var cfg = new MatchConfig();
            LocalPeer = 0;
            World = MatchScenario.Build(cfg.Seed, MatchConfig.DefaultFaction(0), MatchConfig.DefaultFaction(1));
            Session = new LockstepSession(World, DeadTransport.It, cfg, 0);
            Link = "no match";
            Phase = Phase.Start;
            ResetMatchState();

            // The map is behind the menu, so put the camera back where a fresh
            // launch would have left it rather than wherever the last match ended.
            if (CameraRig.Instance != null)
            {
                CameraRig.Instance.CenterOn(new Vector2(MatchScenario.MapSize / 2f, MatchScenario.MapSize / 2f));
            }
        }

        /// <summary>
        /// Builds the match and starts the handshake. A null remote hosts. Returns
        /// null, or why it could not start: the address and the port come straight
        /// from a player, so they are checked here rather than thrown out of an
        /// OnGUI callback where nothing can report them.
        ///
        /// The match itself does not begin here. The session has to reach Running
        /// first, and until it does the start screen stays up showing why not.
        /// </summary>
        public string StartMatch(string remote, int port, Faction faction)
        {
            if (port < 0 || port > 65535) return "port out of range: " + port;

            IPEndPoint target = null;
            if (remote != null)
            {
                if (!IPAddress.TryParse(remote.Trim(), out IPAddress ip)) return "not an address: " + remote;
                target = new IPEndPoint(ip, port);
            }

            UdpTransport socket;
            try
            {
                // The joining side binds anything free; only the host's port has to
                // be the one the other player was told about.
                socket = new UdpTransport(remote == null ? port : 0, target);
            }
            catch (SocketException e)
            {
                return "socket: " + e.Message; // port already taken, usually
            }

            transport?.Dispose();
            transport = socket;

            var cfg = new MatchConfig { LocalFaction = faction };
            config = cfg;
            peerTimeoutMs = cfg.TimeoutMs;

            // -ticks is a script measuring a hash. It has to die rather than hang
            // when its partner never launches; everyone else has a cancel button.
            if (stopAtTick <= 0) cfg.TimeoutMs = LobbyTimeoutMs;

            LocalPeer = remote == null ? 0 : 1;

            // The other peer's faction is a placeholder until its Hello arrives.
            // The session writes the real one before tick 0, and no tick and no
            // hash happen in between, so the guess never reaches the simulation.
            Faction mine = cfg.LocalFaction;
            Faction theirs = cfg.RemoteFaction ?? MatchConfig.DefaultFaction(1 - LocalPeer);
            World = MatchScenario.Build(cfg.Seed,
                LocalPeer == 0 ? mine : theirs,
                LocalPeer == 0 ? theirs : mine);
            Session = new LockstepSession(World, transport, cfg, LocalPeer);
            Link = remote == null
                ? "listening on udp " + transport.LocalPort
                : "connecting to " + remote + ":" + port;

            Phase = Phase.Start; // the handshake decides when this becomes Match
            ResetMatchState();
            return null;
        }

        /// <summary>Every per-match field, in one place, so a restart cannot miss one.</summary>
        private void ResetMatchState()
        {
            startMs = -1;
            lastStepMs = 0;
            stopReported = false;
            Outcome = null;
            Halted = false;
            EndTick = 0;

            previous.Clear();
            Snapshot(current);
            previous.AddRange(current);
        }

        /// <summary>
        /// Freezes the verdict. The session keeps being pumped after this so a
        /// desync dump can still land, and the peer timeout that follows a finished
        /// match must not rewrite the result the player already earned.
        /// </summary>
        private void End()
        {
            EndTick = World.Tick;
            Halted = Session.State == SessionState.Stopped;
            Outcome = Halted
                ? "MATCH HALTED"
                : World.Winner < 0
                    ? "DRAW: both bases fell on the same tick"
                    : World.Winner == LocalPeer ? "VICTORY: enemy base destroyed" : "DEFEAT: base destroyed";
            Phase = Phase.Result;
        }

        // ---- lifecycle ----

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            stopAtTick = Arg("-ticks", 0);
            ShowStart();

            string join = Arg("-join");
            if (!CommandLineMatch()) return;

            string bad = StartMatch(join, Arg("-port", DefaultPort), FactionArg(join == null ? 0 : 1));
            if (bad != null) Debug.LogError("cannot start: " + bad);
        }

        private void OnDestroy()
        {
            transport?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            long now = NowMs();

            if (Phase == Phase.Start)
            {
                if (!Connecting) return;
                Session.Update(now);
                // Tick 0 waits for the handshake. A rejection leaves the session
                // stopped and the start screen up, holding the reason.
                if (Session.State != SessionState.Running) return;

                // Silence means a broken link from here on, not an empty lobby.
                config.TimeoutMs = peerTimeoutMs;
                Phase = Phase.Match;
                return;
            }

            if (Phase == Phase.Result)
            {
                // Still pumped, so the peer's state dump can finish a desync report
                // while the player is reading the one line it produced.
                Session.Update(now);
                return;
            }

            Session.Update(now);

            if (Session.State == SessionState.Stopped && !stopReported)
            {
                // The result screen is useless to a headless peer, and a desync that
                // leaves no trace in the player log cannot be diagnosed at all.
                stopReported = true;
                Debug.LogError("STOPPED at tick " + World.Tick + ": " + Session.StopReason);
            }

            if (Session.State != SessionState.Running)
            {
                if (Session.State == SessionState.Stopped && stopAtTick <= 0) End();
                return;
            }

            if (startMs < 0)
            {
                startMs = now;
                lastStepMs = now;
            }

            // The wall clock decides how many ticks are due; the barrier decides
            // how many actually run. A frame rate spike must never let this peer
            // execute a tick the other one has not sent input for.
            long due = (now - startMs) / TickMs;
            while (World.Tick <= due && StepOnce(now)) { }

            // A headless peer is measuring a hash, not playing. It runs to -ticks
            // and prints, and a result screen nobody can click would hang it.
            if (stopAtTick > 0)
            {
                if (World.Tick < stopAtTick) return;

                // Two clients that ran the same match must end on the same hash.
                // This is the line a LAN check reads out of each player log.
                Debug.Log("OK: " + World.Tick + " ticks, final hash 0x" + World.Hash().ToString("X16"));
                Application.Quit();
                return;
            }

            if (World.MatchOver) End();
        }

        private bool StepOnce(long now)
        {
            previous.Clear();
            previous.AddRange(current);

            if (!Session.TryStep(now)) return false;

            Snapshot(current);
            // Entities spawned during this tick have no previous position, so they
            // appear at rest rather than sliding in from wherever slot n used to be.
            while (previous.Count < current.Count) previous.Add(current[previous.Count]);

            lastStepMs = now;
            return true;
        }

        private void Snapshot(List<Vector2> into)
        {
            into.Clear();
            for (int i = 0; i < World.EntityCount; i++) into.Add(ToView(World.GetEntity(i).Position));
        }

        private static long NowMs() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        // ---- command line ----

        /// <summary>
        /// True when the launch named a match. Anything else opens the start screen,
        /// which is the only way a player who did not open a terminal gets to play.
        /// </summary>
        private static bool CommandLineMatch() =>
            Arg("-join") != null || Arg("-port") != null || Arg("-faction") != null || Arg("-ticks") != null;

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        private static int Arg(string name, int fallback) =>
            int.TryParse(Arg(name), out int v) ? v : fallback;

        /// <summary>
        /// -faction &lt;name&gt;, case insensitive. A name that means nothing is a
        /// mistake worth shouting about, not worth quietly playing something else.
        /// </summary>
        private static Faction FactionArg(int peer)
        {
            string picked = Arg("-faction");
            if (picked == null) return MatchConfig.DefaultFaction(peer);
            if (Enum.TryParse(picked, true, out Faction faction) &&
                Enum.IsDefined(typeof(Faction), faction))
            {
                return faction;
            }

            Debug.LogError("unknown faction '" + picked + "'; one of: " +
                           string.Join(", ", Enum.GetNames(typeof(Faction))));
            return MatchConfig.DefaultFaction(peer);
        }

        /// <summary>
        /// A socket that is not there. The start and result screens still hold a
        /// world and a session, because Orders, Selection and MatchView read both
        /// every frame and a null there is a crash rather than a blank screen.
        /// Nothing pumps this one, so it sits in Handshaking and issues nothing.
        /// </summary>
        // ponytail: an inert session instead of a phase check in every view
        // component. If a screen ever needs the session genuinely absent, add the
        // checks then; today they would be four copies of the same null guard.
        private sealed class DeadTransport : ITransport
        {
            public static readonly DeadTransport It = new DeadTransport();

            public void Send(byte[] data, int length) { }

            public bool TryReceive(out byte[] packet)
            {
                packet = null;
                return false;
            }
        }
    }
}
