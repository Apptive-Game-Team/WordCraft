using System;
using System.Net;
using System.Threading;
using WordCraft.Net;
using WordCraft.Sim;

namespace WordCraft.Host
{
    /// <summary>
    /// Headless runner for a two peer lockstep match.
    ///
    ///   dotnet run --project Host -- selfcheck
    ///   dotnet run --project Host -- host [port] [ticks] [-faction &lt;name&gt;]
    ///   dotnet run --project Host -- join &lt;ip&gt; [port] [ticks] [-faction &lt;name&gt;]
    /// </summary>
    internal static class Program
    {
        private const int DefaultPort = 45677;
        private const int DefaultTicks = 600;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "selfcheck";
            if (mode == "selfcheck") return SelfCheck.Run();

            int peerId = mode == "host" ? 0 : 1;
            if (mode != "host" && (mode != "join" || args.Length < 2))
            {
                Console.WriteLine("usage: selfcheck | host [port] [ticks] [-faction <name>]" +
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

            return peerId == 0
                ? RunUdp(0, null, Arg(args, 1, DefaultPort), Arg(args, 2, DefaultTicks), faction)
                : RunUdp(1, args[1], Arg(args, 2, DefaultPort), Arg(args, 3, DefaultTicks), faction);
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
