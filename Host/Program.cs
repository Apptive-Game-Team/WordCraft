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
    ///   dotnet run --project Host -- host [port] [ticks] [-faction &lt;name&gt;] [-save &lt;path&gt;]
    ///   dotnet run --project Host -- join &lt;ip&gt; [port] [ticks] [-faction &lt;name&gt;] [-save &lt;path&gt;]
    ///   dotnet run --project Host -- replay &lt;path&gt;
    ///   dotnet run --project Host -- compare &lt;path&gt; &lt;path&gt;
    /// </summary>
    internal static class Program
    {
        private const int DefaultPort = 45677;
        private const int DefaultTicks = 600;

        /// <summary>The peer the simulation plays in a solo match. The player is peer 0.</summary>
        private const int SoloOpponent = 1;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "selfcheck";
            if (mode == "selfcheck") return SelfCheck.Run();
            if (mode == "replay") return RunReplay(args);
            if (mode == "compare") return RunCompare(args);

            int peerId = mode == "join" ? 1 : 0;
            if (mode != "host" && mode != "solo" && (mode != "join" || args.Length < 2))
            {
                Console.WriteLine("usage: selfcheck | solo [ticks] [-faction <name>] [-save <path>]" +
                                  " | host [port] [ticks] [-faction <name>] [-opponent <name>] [-save <path>]" +
                                  " | join <ip> [port] [ticks] [-faction <name>] [-opponent <name>] [-save <path>]" +
                                  " | replay <path> | compare <path> <path>");
                return 2;
            }

            Faction? faction = PickFaction(args, "-faction", MatchConfig.DefaultFaction(peerId));
            if (faction == null) return 2;

            if (mode == "solo") return RunSolo(Arg(args, 1, DefaultTicks), faction.Value, Flag(args, "-save"));

            Faction? opponent = PickFaction(args, "-opponent", MatchConfig.DefaultFaction(1 - peerId));
            if (opponent == null) return 2;

            string savePath = Flag(args, "-save");
            return peerId == 0
                ? RunUdp(0, null, Arg(args, 1, DefaultPort), Arg(args, 2, DefaultTicks),
                    faction.Value, opponent.Value, savePath)
                : RunUdp(1, args[1], Arg(args, 2, DefaultPort), Arg(args, 3, DefaultTicks),
                    faction.Value, opponent.Value, savePath);
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
            Faction opponent, string savePath)
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
                Thread.Sleep(1);
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
