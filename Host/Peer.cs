using System.Collections.Generic;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.Host
{
    /// <summary>
    /// One local player: a World, its session, and a scripted command source so a
    /// match can run headless. The script draws from its own DetRandom, never from
    /// World.Random, because the simulation's draw count is state.
    /// </summary>
    internal sealed class Peer
    {
        public readonly World World;
        public readonly LockstepSession Session;

        /// <summary>Tick to hash. Add() throws on a repeat, which is the "no tick runs twice" check.</summary>
        public readonly Dictionary<int, ulong> Hashes = new Dictionary<int, ulong>();

        /// <summary>
        /// Every peer records, always, because the check that matters is whether
        /// two peers of one match wrote the same file.
        /// </summary>
        public readonly MatchRecorder Recorder = new MatchRecorder();

        /// <summary>
        /// Where this peer publishes confirmed frames for whoever is watching,
        /// or null when nobody is. Assigning it changes nothing about how the
        /// match runs: publishing is a copy into a ring buffer, so a spectator
        /// that never reads it, or never existed, costs this peer one array per
        /// tick and no waiting at all.
        /// </summary>
        public SpectatorFeed Feed;

        // What the script commands, read out of the world it was handed rather
        // than written down as constants: the harness map and the client's map
        // number their entities differently, and a script naming raw ids would
        // command another peer's units on one of the two.
        private readonly List<int> units = new List<int>();
        private readonly int worker;
        private readonly int node;

        private readonly DetRandom script;
        private int lastScripted = -1;

        public Peer(int peerId, ITransport transport, MatchConfig cfg, int corruptEntity, int corruptHp)
            : this(peerId, transport, cfg, BuildWorld(cfg.Seed, corruptEntity, corruptHp))
        {
        }

        /// <summary>
        /// A peer on a world somebody else built. A networked match plays the
        /// client's map so that what it saves is a match the replay harness can
        /// rebuild; the self-check plays the harness map below.
        /// </summary>
        public Peer(int peerId, ITransport transport, MatchConfig cfg, World world)
        {
            World = world;
            Session = new LockstepSession(World, transport, cfg, peerId);
            script = new DetRandom(0xBADC0DE ^ (ulong)peerId);

            worker = -1;
            node = -1;
            int nodesSeen = 0;
            for (int id = 0; id < World.EntityCount; id++)
            {
                Entity e = World.GetEntity(id);
                // The peer's own node is the one its start was given, which on
                // both maps is the peer'th resource node in spawn order.
                if (e.Kind == EntityKind.ResourceNode && nodesSeen++ == peerId) node = e.Id;
                if (e.Owner != peerId) continue;
                if (e.Kind == EntityKind.Unit) units.Add(e.Id);
                else if (e.Kind == EntityKind.Worker && worker < 0) worker = e.Id;
            }
        }

        /// <summary>
        /// The harness map. Static and shared so that replaying a recorded match
        /// starts from the world the match was played on: a second copy of these
        /// spawns would drift from this one the day either is edited, and the
        /// replay would then disagree for a reason that has nothing to do with
        /// the log it is checking.
        /// </summary>
        public static World BuildWorld(ulong seed, int corruptEntity, int corruptHp)
        {
            var world = new World(seed);
            for (int owner = 0; owner < 2; owner++)
            {
                for (int i = 0; i < 3; i++)
                {
                    int id = owner * 3 + i;
                    world.SpawnUnit(
                        owner,
                        Role.Melee,
                        new FixVec2(Fix.FromInt(owner * 40), Fix.FromInt(i * 5)),
                        id == corruptEntity ? corruptHp : -1);
                }
            }

            // An economy the netcode has to carry. A Move command would not notice
            // if Command.Arg were dropped on the wire; Gather names its node there.
            for (int owner = 0; owner < 2; owner++)
            {
                Fix x = Fix.FromInt(owner * 40);
                world.SpawnWorker(owner, new FixVec2(x, Fix.FromInt(20)));
                world.SpawnBuilding(owner, Role.Production, new FixVec2(x + Fix.FromInt(2), Fix.FromInt(20)), complete: true);
                world.SpawnResourceNode(new FixVec2(x + Fix.FromInt(6), Fix.FromInt(20)), 500);
            }
            return world;
        }

        public void Update(long nowMs) => Session.Update(nowMs);

        /// <summary>Issues this tick's scripted command, then attempts one tick.</summary>
        public bool Step(long nowMs)
        {
            if (Session.State != SessionState.Running) return false;

            if (lastScripted != Session.Tick)
            {
                lastScripted = Session.Tick;
                if (Session.Tick == 5 && worker >= 0 && node >= 0)
                {
                    Session.Issue(CommandType.Gather, worker, FixVec2.Zero, node);
                }
                if (Session.Tick % 17 == 0 && units.Count > 0)
                {
                    // Each peer only ever commands its own units, so the script
                    // stays deterministic without any cross-peer coordination.
                    int entity = units[script.NextInt(units.Count)];
                    Session.Issue(CommandType.Move, entity, new FixVec2(
                        Fix.FromInt(script.NextInt(64)),
                        Fix.FromInt(script.NextInt(64))));
                }
            }

            if (!Session.TryStep(nowMs)) return false;
            // Both consumers read the one confirmed batch, at the one moment it
            // is valid, before the next TryStep overwrites it. The recorder keeps
            // all of it for a file; the feed keeps a window of it for a watcher.
            Recorder.Capture(Session);
            Feed?.Publish(Session);
            Hashes.Add(World.Tick, World.Hash());
            return true;
        }

        public void Pump(long nowMs)
        {
            Update(nowMs);
            Step(nowMs);
        }
    }
}
