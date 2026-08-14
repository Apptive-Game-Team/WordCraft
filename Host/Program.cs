using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using WordCraft.Net;
using WordCraft.Replay; // ReplayLog and ReplayHeader, compiled in from Replay; see Host.csproj
using WordCraft.Sim;
using WordCraft.View; // MatchScenario, compiled in from the client; see Host.csproj

namespace WordCraft.Host
{
    /// <summary>
    /// Headless runner for a two peer lockstep match.
    ///
    ///   dotnet run --project Host -- selfcheck
    ///   dotnet run --project Host -- solo [ticks] [-faction &lt;name&gt;] [-save &lt;path&gt;]
    ///   dotnet run --project Host -- host [port] [ticks] [-faction &lt;name&gt;] [-save &lt;path&gt;] [-watch &lt;port&gt;]
    ///   dotnet run --project Host -- join &lt;ip&gt; [port] [ticks] [-faction &lt;name&gt;] [-save &lt;path&gt;] [-watch &lt;port&gt;]
    ///   dotnet run --project Host -- watch &lt;ip&gt; [port] [ticks]
    ///   dotnet run --project Host -- replay &lt;path&gt;
    ///   dotnet run --project Host -- compare &lt;path&gt; &lt;path&gt;
    /// </summary>
    internal static class Program
    {
        private const int DefaultPort = 45677;
        private const int DefaultWatchPort = 45678;
        private const int DefaultTicks = 600;

        /// <summary>How long a watcher waits in silence before deciding the match it was watching is over.</summary>
        private const int WatchSilenceMs = 5000;

        /// <summary>How long a peer keeps serving watchers after its own match has finished.</summary>
        private const int WatchTailMs = 500;

        /// <summary>The peer the simulation plays in a solo match. The player is peer 0.</summary>
        private const int SoloOpponent = 1;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "selfcheck";
            if (mode == "selfcheck") return SelfCheck.Run();
            if (mode == "replay") return RunReplay(args);
            if (mode == "compare") return RunCompare(args);
            if (mode == "watch") return RunWatch(args);

            int peerId = mode == "join" ? 1 : 0;
            if (mode != "host" && mode != "solo" && (mode != "join" || args.Length < 2))
            {
                Console.WriteLine("usage: selfcheck | solo [ticks] [-faction <name>] [-save <path>]" +
                                  " | host [port] [ticks] [-faction <name>] [-opponent <name>] [-save <path>] [-watch <port>]" +
                                  " | join <ip> [port] [ticks] [-faction <name>] [-opponent <name>] [-save <path>] [-watch <port>]" +
                                  " | watch <ip> [port] [ticks]" +
                                  " | replay <path> | compare <path> <path>");
                return 2;
            }

            Faction? faction = PickFaction(args, "-faction", MatchConfig.DefaultFaction(peerId));
            if (faction == null) return 2;

            if (mode == "solo") return RunSolo(Arg(args, 1, DefaultTicks), faction.Value, Flag(args, "-save"));

            Faction? opponent = PickFaction(args, "-opponent", MatchConfig.DefaultFaction(1 - peerId));
            if (opponent == null) return 2;

            string savePath = Flag(args, "-save");
            // Absent, not zero: a spectator port has to be asked for. A peer that
            // opened one by default would be answering strangers in every match.
            int watchPort = -1;
            string watchArg = Flag(args, "-watch");
            if (watchArg != null && !int.TryParse(watchArg, out watchPort))
            {
                Console.WriteLine("-watch wants a port number, got '" + watchArg + "'");
                return 2;
            }
            return peerId == 0
                ? RunUdp(0, null, Arg(args, 1, DefaultPort), Arg(args, 2, DefaultTicks),
                    faction.Value, opponent.Value, savePath, watchPort)
                : RunUdp(1, args[1], Arg(args, 2, DefaultPort), Arg(args, 3, DefaultTicks),
                    faction.Value, opponent.Value, savePath, watchPort);
        }

        /// <summary>
        /// One human peer and one the simulation plays, on the client's own map and
        /// with no socket. Four lines of setup, and they are the four a start-screen
        /// button would need: Solo on the config, the scenario, SetPeerAi on the
        /// opponent, and a session over a transport that goes nowhere.
        ///
        /// Run flat out on a counted clock rather than paced at 20 Hz. Nothing here
        /// waits on a network, and the clock only exists because Update and TryStep
        /// take one.
        /// </summary>
        private static int RunSolo(int ticks, Faction faction, string savePath)
        {
            Faction opponent = MatchConfig.DefaultFaction(SoloOpponent);
            var cfg = new MatchConfig { Solo = true, LocalFaction = faction, RemoteFaction = opponent };

            World world = MatchScenario.Build(cfg.Seed, faction, opponent);
            world.SetPeerAi(SoloOpponent, true);
            var session = new LockstepSession(world, NullTransport.It, cfg, 0);

            Console.WriteLine("solo, seed 0x" + cfg.Seed.ToString("X") +
                              ", faction " + faction + " against " + opponent +
                              " (peer " + SoloOpponent + ", played by the simulation)" +
                              ", input delay " + cfg.InputDelay + ", target " + ticks + " ticks");

            // The same recorder a networked match uses, reading the same confirmed
            // batch. Solo has nothing that takes interactive input yet and the
            // opponent's decisions live inside World.Step rather than as commands,
            // so the entries are empty today — but they come from what the session
            // executed, so the day solo does take input the log follows without
            // this code changing.
            var recorder = new MatchRecorder();

            long now = 0;
            while (world.Tick < ticks && !world.MatchOver)
            {
                session.Update(now);
                if (session.State == SessionState.Stopped) break;
                // A solo barrier that closes is a fault, not a wait: there is
                // nothing left that could open it later.
                if (!session.TryStep(now)) break;
                recorder.Capture(session);
                now += 1000 / World.TicksPerSecond;
            }

            if (session.State == SessionState.Stopped)
            {
                Console.WriteLine("STOPPED at tick " + world.Tick + ": " + session.StopReason);
                return 1;
            }
            if (world.Tick < ticks && !world.MatchOver)
            {
                Console.WriteLine("STALLED at tick " + world.Tick + ": the solo barrier closed");
                return 1;
            }

            Report(world, SoloOpponent);
            Console.WriteLine("OK: " + world.Tick + " ticks, final hash 0x" + world.Hash().ToString("X16"));

            if (savePath != null)
            {
                var header = new ReplayHeader(FactionData.ContentVersion, cfg.Seed, faction, opponent,
                    aiPeers: (byte)(1 << SoloOpponent));
                recorder.Save(savePath, header, recorder.TickCount);
                Console.WriteLine("saved replay to " + savePath);
            }

            return 0;
        }

        /// <summary>
        /// Reads a saved match back and steps a fresh world through exactly what
        /// was recorded. Whatever ReplayLog.TryRead refuses, this refuses the
        /// same way: a message and a nonzero exit, never an exception reaching
        /// the console as a stack trace.
        /// </summary>
        private static int RunReplay(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: replay <path>");
                return 2;
            }

            if (!ReplayLog.TryRead(args[1], out ReplayHeader header, out List<Command>[] log, out string refusal))
            {
                Console.WriteLine("REFUSED: " + refusal);
                return 1;
            }

            World world = MatchScenario.Build(header.Seed, header.Peer0Faction, header.Peer1Faction);
            // An AI peer's decisions are computed from world state inside Step,
            // not carried as commands, so this has to be restored before the
            // first tick runs or that peer plays no part in the replay at all.
            for (int peer = 0; peer < MatchScenario.Peers; peer++)
            {
                if (header.IsAi(peer)) world.SetPeerAi(peer, true);
            }

            Console.WriteLine("replaying " + args[1] + ": seed 0x" + header.Seed.ToString("X") +
                              ", " + header.Peer0Faction + " vs " + header.Peer1Faction +
                              ", " + log.Length + " ticks");

            for (int t = 0; t < log.Length; t++) world.Step(log[t]);

            Console.WriteLine("OK: " + world.Tick + " ticks, final hash 0x" + world.Hash().ToString("X16"));
            return 0;
        }

        /// <summary>
        /// A third machine watching a match it is not in. It sends nothing the
        /// players act on, so the two of them run at the same speed whether this
        /// is here or not, and the only thing that can go wrong belongs to this
        /// process: falling further behind than the peer's window is deep.
        ///
        /// Everything it needs to build a world arrives in the Welcome. The seed
        /// and both factions are hashed state, so a watcher told to assume them
        /// would report hashes that disagree for a reason that has nothing to do
        /// with the match. Being told them is also why this can only join a match
        /// whose window still reaches tick 0: there is no state transfer, so the
        /// only world this can build is one that has not started.
        /// </summary>
        private static int RunWatch(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: watch <ip> [port] [ticks]");
                return 2;
            }

            int port = Arg(args, 2, DefaultWatchPort);
            int ticks = Arg(args, 3, DefaultTicks);
            var cfg = new MatchConfig();

            using var transport = new UdpTransport(0, new IPEndPoint(IPAddress.Parse(args[1]), port));
            var subscriber = new FeedSubscriber(transport, cfg);
            Console.WriteLine("watching " + args[1] + ":" + port + " from udp " + transport.LocalPort +
                              ", target " + ticks + " ticks");

            Spectator spectator = null;
            while (true)
            {
                long now = Environment.TickCount64;
                subscriber.Update(now);

                if (subscriber.Refusal != null)
                {
                    Console.WriteLine("REFUSED: " + subscriber.Refusal);
                    return 1;
                }

                if (spectator == null && subscriber.Welcomed)
                {
                    // The players' map, which is the client's scenario. A watcher
                    // on a different map steps the right commands into the wrong
                    // world and disagrees from tick 1.
                    World world = MatchScenario.Build(subscriber.Seed,
                        subscriber.Peer0Faction, subscriber.Peer1Faction);
                    spectator = new Spectator(world, subscriber.Feed);
                    Console.WriteLine("watching a match on seed 0x" + subscriber.Seed.ToString("X") +
                                      ", " + subscriber.Peer0Faction + " against " + subscriber.Peer1Faction);
                }

                if (spectator != null)
                {
                    spectator.Follow(WordCraft.Sim.World.TicksPerSecond);
                    if (spectator.Dropped)
                    {
                        Console.WriteLine("DROPPED at tick " + spectator.Tick + ": " + spectator.DropReason);
                        return 1;
                    }
                    if (spectator.Tick >= ticks) break;
                }

                if (now - subscriber.LastHeardMs > WatchSilenceMs)
                {
                    // Not an error the match would recognise. A peer that finished
                    // or quit simply stops sending, and there is no goodbye on
                    // this wire because a watcher is nobody's responsibility.
                    Console.WriteLine("SILENT for " + WatchSilenceMs + " ms at tick " +
                                      (spectator == null ? 0 : spectator.Tick) +
                                      (spectator == null ? "; nobody is serving this port" : "; the match has ended"));
                    return spectator == null ? 1 : 0;
                }

                Thread.Sleep(1);
            }

            Console.WriteLine("OK: " + spectator.Tick + " ticks, final hash 0x" +
                              spectator.World.Hash().ToString("X16") +
                              " (" + subscriber.FramesReceived + " frames off the wire)");
            return 0;
        }

        /// <summary>What the simulated peer did with its match, so a run is readable rather than merely green.</summary>
        private static void Report(World world, int peer)
        {
            int workers = 0, fighters = 0, buildings = 0, marching = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive || e.Owner != peer) continue;
                if (e.Kind == EntityKind.Worker) workers++;
                else if (e.Kind == EntityKind.Unit) fighters++;
                else if (e.Kind == EntityKind.Building) buildings++;
                if (e.Mode == OrderMode.AttackMove) marching++;
            }

            Console.WriteLine("peer " + peer + ": " + workers + " workers, " + fighters + " fighters (" +
                              marching + " marching), " + buildings + " buildings, " +
                              world.GetResources(peer) + " banked, population " +
                              world.GetPopulation(peer) + "/" + world.PopulationCap(peer));
            if (world.MatchOver) Console.WriteLine("match over at tick " + world.Tick + ", winner " + world.Winner);
        }

        /// <summary>The faction a flag names, the fallback when it is absent, or null when it is not a faction.</summary>
        private static Faction? PickFaction(string[] args, string flag, Faction fallback)
        {
            string picked = Flag(args, flag);
            if (picked == null) return fallback;
            if (!Enum.TryParse(picked, true, out Faction faction) || !Enum.IsDefined(typeof(Faction), faction))
            {
                Console.WriteLine("unknown faction '" + picked + "'; one of: " +
                                  string.Join(", ", Enum.GetNames(typeof(Faction))));
                return null;
            }
            return faction;
        }

        private static int Arg(string[] args, int index, int fallback) =>
            args.Length > index && int.TryParse(args[index], out int v) ? v : fallback;

        /// <summary>Value after a named flag, anywhere in the arguments, or null.</summary>
        private static string Flag(string[] args, string name)
        {
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        /// <summary>
        /// Reports the first way two saved matches differ. Both peers of a match
        /// record the batch they executed, so their files are the same file;
        /// when they are not, this names the tick and the command where the two
        /// simulations stopped being the same match.
        /// </summary>
        private static int RunCompare(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: compare <path> <path>");
                return 2;
            }

            string difference = ReplayComparison.FirstDifference(args[1], args[2]);
            if (difference != null)
            {
                Console.WriteLine("DIFFERENT: " + difference);
                return 1;
            }

            Console.WriteLine("OK: " + args[1] + " and " + args[2] + " are the same match");
            return 0;
        }

        /// <summary>
        /// A match against a peer on a socket, on the client's own map — the same
        /// map solo plays and the same one the replay harness rebuilds from a
        /// header, which is what makes -save here worth saving.
        ///
        /// That map is why both factions have to be named before tick 0 rather
        /// than waited for: units spawn with their faction's stats, so a peer
        /// that guessed its opponent wrong would build a different world and
        /// disagree on tick 1. Putting the guess in RemoteFaction turns being
        /// wrong into a handshake rejection, which says what happened.
        /// </summary>
        private static int RunUdp(int peerId, string remoteIp, int port, int ticks, Faction faction,
            Faction opponent, string savePath, int watchPort)
        {
            var cfg = new MatchConfig { LocalFaction = faction, RemoteFaction = opponent };
            using var transport = peerId == 0
                ? new UdpTransport(port, null) // listener; learns the peer from its first datagram
                : new UdpTransport(0, new IPEndPoint(IPAddress.Parse(remoteIp), port));

            World world = MatchScenario.Build(cfg.Seed,
                peerId == 0 ? faction : opponent,
                peerId == 0 ? opponent : faction);
            var peer = new Peer(peerId, transport, cfg, world);
            Console.WriteLine("peer " + peerId + " on udp " + transport.LocalPort +
                              ", seed 0x" + cfg.Seed.ToString("X") +
                              ", faction " + cfg.LocalFaction + " against " + opponent +
                              ", input delay " + cfg.InputDelay + ", target " + ticks + " ticks");

            // A second socket, never the match one: the match link takes its peer
            // from whoever speaks first, so a watcher arriving there would be
            // taken for the opponent and the match would wait on it forever.
            using UdpFanout fanout = watchPort >= 0 ? new UdpFanout(watchPort) : null;
            FeedPublisher publisher = null;
            if (fanout != null)
            {
                peer.Feed = new SpectatorFeed();
                publisher = new FeedPublisher(world, cfg, peer.Feed, fanout);
                Console.WriteLine("watchers welcome on udp " + fanout.LocalPort +
                                  ", window " + peer.Feed.Capacity + " ticks");
            }

            long start = Environment.TickCount64;
            while (peer.Session.Tick < ticks)
            {
                long now = Environment.TickCount64;
                peer.Update(now);
                if (peer.Session.State == SessionState.Stopped) break;

                // Wall clock decides how many ticks are due; the barrier decides
                // how many actually run. Never run ahead of 20 Hz.
                long due = (now - start) / (1000 / WordCraft.Sim.World.TicksPerSecond);
                while (peer.Session.Tick <= due && peer.Step(now)) { }

                // After the ticks, and outside the condition that ran them. A
                // watcher is served with whatever is left of this iteration and
                // is never a reason for the next one to wait.
                publisher?.Update(now);
                Thread.Sleep(1);
            }

            // Serve the last frames before quitting. A watcher is a tick or two
            // behind by construction, and the moment this loop ended there was
            // nobody left who would ever send them.
            if (publisher != null)
            {
                long tail = Environment.TickCount64 + WatchTailMs;
                while (Environment.TickCount64 < tail)
                {
                    publisher.Update(Environment.TickCount64);
                    Thread.Sleep(1);
                }
            }

            // Keep pumping briefly so a desync report can collect the peer's dump.
            long grace = Environment.TickCount64 + 2500;
            while (!peer.Session.ReportComplete && Environment.TickCount64 < grace)
            {
                peer.Update(Environment.TickCount64);
                Thread.Sleep(1);
            }

            // Saved before the stop is reported, because a match that ended in a
            // desync is the one most worth sending to somebody else.
            if (savePath != null) SaveMatch(peer, cfg, savePath, ticks);

            if (peer.Session.State == SessionState.Stopped)
            {
                Console.WriteLine("STOPPED at tick " + peer.World.Tick + ": " + peer.Session.StopReason);
                return 1;
            }

            Console.WriteLine("OK: " + peer.World.Tick + " ticks, final hash 0x" + peer.World.Hash().ToString("X16"));
            return 0;
        }

        /// <summary>
        /// Writes what this peer executed, which is what both peers executed.
        /// Each side saves on its own with no coordination, and `compare` on the
        /// two files then answers whether they played the same match.
        ///
        /// The factions are read from the world rather than from the local
        /// config because only one of the two was ever this peer's own choice;
        /// the other arrived in the handshake. Neither peer is played by the
        /// simulation, so the AI mask stays empty.
        /// </summary>
        private static void SaveMatch(Peer peer, MatchConfig cfg, string path, int ticks)
        {
            var header = new ReplayHeader(FactionData.ContentVersion, cfg.Seed,
                peer.World.FactionOf(0), peer.World.FactionOf(1));

            // The tick target, not the tick reached: a peer can notice the end of
            // the match a tick or two later than the other one and that is not a
            // disagreement about what happened.
            peer.Recorder.Save(path, header, ticks);
            Console.WriteLine("saved replay to " + path + " (" + Math.Min(ticks, peer.Recorder.TickCount) + " ticks)");
        }
    }
}
