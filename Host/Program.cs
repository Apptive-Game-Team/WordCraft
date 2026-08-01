using System;
using System.Net;
using System.Threading;
using WordCraft.Net;

namespace WordCraft.Host
{
    /// <summary>
    /// Headless runner for a two peer lockstep match.
    ///
    ///   dotnet run --project Host -- selfcheck
    ///   dotnet run --project Host -- host [port] [ticks]
    ///   dotnet run --project Host -- join &lt;ip&gt; [port] [ticks]
    /// </summary>
    internal static class Program
    {
        private const int DefaultPort = 45677;
        private const int DefaultTicks = 600;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "selfcheck";
            switch (mode)
            {
                case "selfcheck": return SelfCheck.Run();
                case "host": return RunUdp(0, null, Arg(args, 1, DefaultPort), Arg(args, 2, DefaultTicks));
                case "join":
                    if (args.Length < 2) { Console.WriteLine("usage: join <ip> [port] [ticks]"); return 2; }
                    return RunUdp(1, args[1], Arg(args, 2, DefaultPort), Arg(args, 3, DefaultTicks));
                default:
                    Console.WriteLine("usage: selfcheck | host [port] [ticks] | join <ip> [port] [ticks]");
                    return 2;
            }
        }

        private static int Arg(string[] args, int index, int fallback) =>
            args.Length > index && int.TryParse(args[index], out int v) ? v : fallback;

        private static int RunUdp(int peerId, string remoteIp, int port, int ticks)
        {
            var cfg = new MatchConfig();
            using var transport = peerId == 0
                ? new UdpTransport(port, null) // listener; learns the peer from its first datagram
                : new UdpTransport(0, new IPEndPoint(IPAddress.Parse(remoteIp), port));

            var peer = new Peer(peerId, transport, cfg, -1, 0);
            Console.WriteLine("peer " + peerId + " on udp " + transport.LocalPort +
                              ", seed 0x" + cfg.Seed.ToString("X") +
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
