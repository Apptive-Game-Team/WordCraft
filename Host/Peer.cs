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

        /// <summary>Worker and node ids per owner, assigned in a fixed spawn order.</summary>
        private static int WorkerOf(int owner) => 6 + owner * 3;
        private static int NodeOf(int owner) => 8 + owner * 3;

        private readonly DetRandom script;
        private int lastScripted = -1;

        public Peer(int peerId, ITransport transport, MatchConfig cfg, int corruptEntity, int corruptHp)
        {
            World = new World(cfg.Seed);
            for (int owner = 0; owner < 2; owner++)
            {
                for (int i = 0; i < 3; i++)
                {
                    int id = owner * 3 + i;
                    World.SpawnUnit(
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
                World.SpawnWorker(owner, new FixVec2(x, Fix.FromInt(20)));
                World.SpawnBuilding(owner, Role.Production, new FixVec2(x + Fix.FromInt(2), Fix.FromInt(20)), complete: true);
                World.SpawnResourceNode(new FixVec2(x + Fix.FromInt(6), Fix.FromInt(20)), 500);
            }

            Session = new LockstepSession(World, transport, cfg, peerId);
            script = new DetRandom(0xBADC0DE ^ (ulong)peerId);
        }

        public void Update(long nowMs) => Session.Update(nowMs);

        /// <summary>Issues this tick's scripted command, then attempts one tick.</summary>
        public bool Step(long nowMs)
        {
            if (Session.State != SessionState.Running) return false;

            if (lastScripted != Session.Tick)
            {
                lastScripted = Session.Tick;
                if (Session.Tick == 5)
                {
                    Session.Issue(CommandType.Gather, WorkerOf(Session.PeerId), FixVec2.Zero,
                        NodeOf(Session.PeerId));
                }
                if (Session.Tick % 17 == 0)
                {
                    // Each peer only ever commands its own units, so the script
                    // stays deterministic without any cross-peer coordination.
                    int entity = Session.PeerId * 3 + script.NextInt(3);
                    Session.Issue(CommandType.Move, entity, new FixVec2(
                        Fix.FromInt(script.NextInt(64)),
                        Fix.FromInt(script.NextInt(64))));
                }
            }

            if (!Session.TryStep(nowMs)) return false;
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
