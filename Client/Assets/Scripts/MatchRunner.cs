using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Owns the world and the lockstep session, and is the only thing in this
    /// assembly allowed to advance either. Everything else reads World and calls
    /// Session.Issue. The simulation is never written to from the view.
    ///
    ///   WordCraft.app                 host, listens on the default port
    ///   WordCraft.app -join 10.0.0.4  join that address
    ///   WordCraft.app -ticks 400      stop there and print the state hash
    /// </summary>
    public sealed class MatchRunner : MonoBehaviour
    {
        private const int DefaultPort = 45677;
        private const int TickMs = 1000 / World.TicksPerSecond;

        public static MatchRunner Instance { get; private set; }

        public World World { get; private set; }
        public LockstepSession Session { get; private set; }
        public int LocalPeer { get; private set; }

        /// <summary>Human readable description of the link, for the HUD.</summary>
        public string Link { get; private set; }

        // Entity positions at the previous and the current tick boundary, in view
        // space. Rendering interpolates between these two; neither is ever read
        // back into the simulation, which only ever holds the tick-boundary value.
        private readonly List<Vector2> previous = new List<Vector2>();
        private readonly List<Vector2> current = new List<Vector2>();

        private UdpTransport transport;
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

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            var cfg = new MatchConfig();
            string remote = Arg("-join");
            int port = Arg("-port", DefaultPort);
            stopAtTick = Arg("-ticks", 0);
            LocalPeer = remote == null ? 0 : 1;
            cfg.LocalFaction = MatchConfig.DefaultFaction(LocalPeer);

            // The other peer's faction is a placeholder until its Hello arrives.
            // The session writes the real one before tick 0, and no tick and no
            // hash happen in between, so the guess never reaches the simulation.
            Faction mine = cfg.LocalFaction;
            Faction theirs = cfg.RemoteFaction ?? MatchConfig.DefaultFaction(1 - LocalPeer);
            World = MatchScenario.Build(cfg.Seed,
                LocalPeer == 0 ? mine : theirs,
                LocalPeer == 0 ? theirs : mine);
            transport = remote == null
                ? new UdpTransport(port, null) // listener; learns the peer from its first datagram
                : new UdpTransport(0, new IPEndPoint(IPAddress.Parse(remote), port));
            Session = new LockstepSession(World, transport, cfg, LocalPeer);
            Link = remote == null ? "listening on udp " + port : "joining " + remote + ":" + port;

            Snapshot(current);
            previous.AddRange(current);
        }

        private void OnDestroy()
        {
            transport?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            long now = NowMs();
            Session.Update(now);

            if (Session.State == SessionState.Stopped && !stopReported)
            {
                // The HUD banner is useless to a headless peer, and a desync that
                // leaves no trace in the player log cannot be diagnosed at all.
                stopReported = true;
                Debug.LogError("STOPPED at tick " + World.Tick + ": " + Session.StopReason);
            }

            if (Session.State != SessionState.Running) return;

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

            if (stopAtTick <= 0 || World.Tick < stopAtTick) return;

            // Two clients that ran the same match must end on the same hash. This
            // is the line a LAN check reads out of each player log.
            Debug.Log("OK: " + World.Tick + " ticks, final hash 0x" + World.Hash().ToString("X16"));
            Application.Quit();
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
    }
}
