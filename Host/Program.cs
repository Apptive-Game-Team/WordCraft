using System;
using System.Net;
using System.Threading;
using WordCraft.Net;
using WordCraft.Sim;
using WordCraft.View; // MatchScenario, compiled in from the client; see Host.csproj

namespace WordCraft.Host
{
    /// <summary>
    /// Headless runner for a two peer lockstep match.
    ///
    ///   dotnet run --project Host -- selfcheck
    ///   dotnet run --project Host -- solo [ticks] [-faction &lt;name&gt;]
    ///   dotnet run --project Host -- host [port] [ticks] [-faction &lt;name&gt;]
    ///   dotnet run --project Host -- join &lt;ip&gt; [port] [ticks] [-faction &lt;name&gt;]
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

            int peerId = mode == "join" ? 1 : 0;
            if (mode != "host" && mode != "solo" && (mode != "join" || args.Length < 2))
            {
                Console.WriteLine("usage: selfcheck | solo [ticks] [-faction <name>]" +
                                  " | host [port] [ticks] [-faction <name>]" +
                                  " | join <ip> [port] [ticks] [-faction <name>]");
                return 2;
            }

            Faction faction = MatchConfig.DefaultFaction(peerId);
            string picked = Flag(args, "-faction");
            if (picked != null && (!Enum.TryParse(picked, true, out faction) ||
                                   !Enum.IsDefined(typeof(Faction), faction)))
            {
                Console.WriteLine("unknown faction '" + picked + "'; one of: " +
                                  string.Join(", ", Enum.GetNames(typeof(Faction))));
                return 2;
            }

            if (mode == "solo") return RunSolo(Arg(args, 1, DefaultTicks), faction);

            return peerId == 0
                ? RunUdp(0, null, Arg(args, 1, DefaultPort), Arg(args, 2, DefaultTicks), faction)
                : RunUdp(1, args[1], Arg(args, 2, DefaultPort), Arg(args, 3, DefaultTicks), faction);
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
        private static int RunSolo(int ticks, Faction faction)
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

            long now = 0;
            while (world.Tick < ticks && !world.MatchOver)
            {
                session.Update(now);
                if (session.State == SessionState.Stopped) break;
                // A solo barrier that closes is a fault, not a wait: there is
                // nothing left that could open it later.
                if (!session.TryStep(now)) break;
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

        private static int RunUdp(int peerId, string remoteIp, int port, int ticks, Faction faction)
        {
            var cfg = new MatchConfig { LocalFaction = faction };
            using var transport = peerId == 0
                ? new UdpTransport(port, null) // listener; learns the peer from its first datagram
                : new UdpTransport(0, new IPEndPoint(IPAddress.Parse(remoteIp), port));

            var peer = new Peer(peerId, transport, cfg, -1, 0);
            Console.WriteLine("peer " + peerId + " on udp " + transport.LocalPort +
                              ", seed 0x" + cfg.Seed.ToString("X") +
                              ", faction " + cfg.LocalFaction +
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

            if (peer.Session.State == SessionState.Stopped)
            {
                Console.WriteLine("STOPPED at tick " + peer.World.Tick + ": " + peer.Session.StopReason);
                return 1;
            }

            Console.WriteLine("OK: " + peer.World.Tick + " ticks, final hash 0x" + peer.World.Hash().ToString("X16"));
            return 0;
        }
    }
}
