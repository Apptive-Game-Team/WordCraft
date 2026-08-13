using System;
using System.Collections.Generic;
using System.IO;
using WordCraft.Net;
using WordCraft.Replay; // ReplayLog and ReplayHeader, compiled in from Replay; see Host.csproj
using WordCraft.Sim;

namespace WordCraft.Host
{
    /// <summary>
    /// Two peers in one process over the faulty in-memory link. Assert based, no
    /// test framework, and driven by a virtual clock so a failure reproduces.
    /// </summary>
    internal static class SelfCheck
    {
        private const int Ticks = 400;
        private const long StepMs = 5;

        public static int Run()
        {
            try
            {
                LossyMatchStaysInSync();
                BothPeersSaveTheSameMatch();
                DesyncHaltsAndNamesTheField();
                SeedMismatchIsRejectedBeforeTick0();
                ContentMismatchIsRejectedBeforeTick0();
                FactionMismatchIsRejectedBeforeTick0();
                PeerTimeoutEndsTheMatch();
                SoloRunsWithoutAPeer();
                SpectatorWatchesWithoutHoldingTheBarrier();
                SpectatorThatFallsTooFarBehindIsDropped();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                return 1;
            }

            Console.WriteLine("OK: lockstep self-check passed (" + Ticks +
                              " ticks over a lossy link, both peers saved the same match," +
                              " desync located, handshake rejected," +
                              " timeout handled, solo barrier opens," +
                              " spectator hashes match and never hold the barrier)");
            return 0;
        }

        /// <summary>Drop, duplicate, reorder, and delay, yet identical hashes every tick.</summary>
        private static void LossyMatchStaysInSync()
        {
            // Two different factions, each known only to the peer that picked it.
            // Factions are hashed, so every tick below also asserts that the
            // handshake delivered them.
            var link = new FaultyLink(0xFEEDFACE, dropPercent: 20, duplicatePercent: 10, baseDelayMs: 30, jitterMs: 40);
            var a = new Peer(0, link.A, new MatchConfig { LocalFaction = Faction.WaterSlimes }, -1, 0);
            var b = new Peer(1, link.B, new MatchConfig { LocalFaction = Faction.RockGolems }, -1, 0);

            Drive(link, a, b, 0,
                () => Stopped(a) || Stopped(b) || (a.Session.Tick >= Ticks && b.Session.Tick >= Ticks),
                600000);

            Check(!Stopped(a), "peer 0 stopped: " + a.Session.StopReason);
            Check(!Stopped(b), "peer 1 stopped: " + b.Session.StopReason);
            Check(a.Session.Tick >= Ticks && b.Session.Tick >= Ticks, "peers did not reach " + Ticks + " ticks");

            for (int t = 1; t <= Ticks; t++)
            {
                Check(a.Hashes.ContainsKey(t) && b.Hashes.ContainsKey(t), "missing hash for tick " + t);
                Check(a.Hashes[t] == b.Hashes[t], "hash mismatch at tick " + t);
            }

            // Both peers banked resources, so the Gather command's node id survived
            // the wire. Dropping Command.Arg would leave this at zero while the
            // hashes still matched, because both peers would be equally wrong.
            for (int peer = 0; peer < 2; peer++)
            {
                Check(a.World.GetResources(peer) > 0, "peer " + peer + " never gathered anything");
                Check(a.World.GetResources(peer) == b.World.GetResources(peer),
                    "peers disagree on peer " + peer + " resources");
                Check(a.World.FactionOf(peer) == b.World.FactionOf(peer),
                    "peers disagree on peer " + peer + " faction");
            }

            Check(a.World.FactionOf(0) == Faction.WaterSlimes && a.World.FactionOf(1) == Faction.RockGolems,
                "the handshake did not carry each peer's own choice");
        }

        /// <summary>
        /// The recording half of a networked match, over the same faulty link.
        /// Two peers save with no coordination between them and the two files
        /// have to be the same bytes, because each holds the confirmed batch and
        /// the confirmed batch is by definition what both of them executed. A
        /// peer that recorded its own commands instead would still write a file
        /// that loads and replays — into a different match — and this is what
        /// catches that.
        ///
        /// Then the file is read back and stepped through a fresh world. Two
        /// peers agreeing proves only that they wrote the same thing, not that
        /// what they wrote is the match they played; the per-tick hashes answer
        /// that half.
        /// </summary>
        private static void BothPeersSaveTheSameMatch()
        {
            const int RecordedTicks = 200;

            var cfgA = new MatchConfig { LocalFaction = Faction.WaterSlimes };
            var cfgB = new MatchConfig { LocalFaction = Faction.RockGolems };
            var link = new FaultyLink(0x5AF3, dropPercent: 20, duplicatePercent: 10, baseDelayMs: 30, jitterMs: 40);
            var a = new Peer(0, link.A, cfgA, -1, 0);
            var b = new Peer(1, link.B, cfgB, -1, 0);

            Drive(link, a, b, 0,
                () => Stopped(a) || Stopped(b) ||
                      (a.Session.Tick >= RecordedTicks && b.Session.Tick >= RecordedTicks),
                600000);

            Check(!Stopped(a), "peer 0 stopped: " + a.Session.StopReason);
            Check(!Stopped(b), "peer 1 stopped: " + b.Session.StopReason);
            Check(a.Recorder.TickCount >= RecordedTicks && b.Recorder.TickCount >= RecordedTicks,
                "a peer recorded fewer than " + RecordedTicks + " ticks: " +
                a.Recorder.TickCount + " and " + b.Recorder.TickCount);

            string pathA = Path.Combine(Path.GetTempPath(), "wordcraft-selfcheck-peer0.wcrp");
            string pathB = Path.Combine(Path.GetTempPath(), "wordcraft-selfcheck-peer1.wcrp");
            try
            {
                a.Recorder.Save(pathA, HeaderFor(a, cfgA), RecordedTicks);
                b.Recorder.Save(pathB, HeaderFor(b, cfgB), RecordedTicks);

                Check(SameBytes(File.ReadAllBytes(pathA), File.ReadAllBytes(pathB)),
                    "the peers saved different files: " +
                    (ReplayComparison.FirstDifference(pathA, pathB) ?? "identical commands, different bytes"));

                ReplaysToTheSameHashes(pathA, a.Hashes, RecordedTicks);
            }
            finally
            {
                if (File.Exists(pathA)) File.Delete(pathA);
                if (File.Exists(pathB)) File.Delete(pathB);
            }
        }

        /// <summary>
        /// What a peer knows about the match it just played. The factions come
        /// from the world rather than the config because only one of the two was
        /// this peer's own choice; the other arrived in the handshake, and both
        /// peers have to end up writing the same header from opposite sides.
        /// </summary>
        private static ReplayHeader HeaderFor(Peer peer, MatchConfig cfg) =>
            new ReplayHeader(FactionData.ContentVersion, cfg.Seed,
                peer.World.FactionOf(0), peer.World.FactionOf(1));

        /// <summary>
        /// Steps a saved match through a world built the way the match's own was,
        /// and compares every tick against the hash the live peer recorded. The
        /// factions are restored from the header for the same reason the session
        /// sets them before tick 0: they are hashed state, so a replay that
        /// skipped them would diverge on tick 1 with the log entirely correct.
        /// </summary>
        private static void ReplaysToTheSameHashes(string path, Dictionary<int, ulong> live, int ticks)
        {
            bool ok = ReplayLog.TryRead(path, out ReplayHeader header, out List<Command>[] log, out string refusal);
            Check(ok, "a freshly saved networked match was refused: " + refusal);
            Check(log.Length == ticks, "the saved match holds " + log.Length + " ticks, wanted " + ticks);

            World world = Peer.BuildWorld(header.Seed, -1, 0);
            world.SetPeerFaction(0, header.Peer0Faction);
            world.SetPeerFaction(1, header.Peer1Faction);

            for (int t = 0; t < log.Length; t++)
            {
                world.Step(log[t]);
                Check(world.Hash() == live[world.Tick],
                    "the replayed match left the recorded one at tick " + world.Tick);
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>One peer starts with a tampered unit; the desync must be located, not just noticed.</summary>
        private static void DesyncHaltsAndNamesTheField()
        {
            var cfg = new MatchConfig();
            var link = new FaultyLink(0x5EED, dropPercent: 0, duplicatePercent: 0, baseDelayMs: 20, jitterMs: 10);
            var a = new Peer(0, link.A, cfg, -1, 0);
            var b = new Peer(1, link.B, cfg, corruptEntity: 3, corruptHp: 99);

            Drive(link, a, b, 0, () => Stopped(a) && a.Session.ReportComplete, 120000);

            string reason = a.Session.StopReason ?? "";
            Check(reason.Contains("desync at tick " + cfg.HashInterval), "wrong desync tick: " + reason);
            Check(reason.Contains("entity 3 field Hp local=100 remote=99"), "divergence not located: " + reason);
            Check(a.World.Tick <= cfg.HashInterval + 8, "halted too late, at tick " + a.World.Tick);
        }

        /// <summary>A disagreement about the seed must never reach World.Step.</summary>
        private static void SeedMismatchIsRejectedBeforeTick0()
        {
            var link = new FaultyLink(0xBEE5, dropPercent: 0, duplicatePercent: 0, baseDelayMs: 10, jitterMs: 0);
            var a = new Peer(0, link.A, new MatchConfig { Seed = 0xC0FFEE }, -1, 0);
            var b = new Peer(1, link.B, new MatchConfig { Seed = 0xDEADBEEF }, -1, 0);

            Drive(link, a, b, 0, () => Stopped(a) && Stopped(b), 60000);

            Check(a.World.Tick == 0 && b.World.Tick == 0, "a tick executed despite a rejected handshake");
            Check((a.Session.StopReason ?? "").Contains("seed"), "peer 0 reason: " + a.Session.StopReason);
            Check((b.Session.StopReason ?? "").Contains("seed"), "peer 1 reason: " + b.Session.StopReason);
        }

        /// <summary>A peer on a different roster must never reach World.Step either.</summary>
        private static void ContentMismatchIsRejectedBeforeTick0()
        {
            var link = new FaultyLink(0xC0117E, dropPercent: 0, duplicatePercent: 0, baseDelayMs: 10, jitterMs: 0);
            var a = new Peer(0, link.A, new MatchConfig(), -1, 0);
            var b = new Peer(1, link.B,
                new MatchConfig { ContentVersion = FactionData.ContentVersion + 1 }, -1, 0);

            Drive(link, a, b, 0, () => Stopped(a) && Stopped(b), 60000);

            Check(a.World.Tick == 0 && b.World.Tick == 0, "a tick executed despite a rejected handshake");
            Check((a.Session.StopReason ?? "").Contains("content version"), "peer 0 reason: " + a.Session.StopReason);
            Check((b.Session.StopReason ?? "").Contains("content version"), "peer 1 reason: " + b.Session.StopReason);
        }

        /// <summary>
        /// Two peers that disagree about who plays what must never reach
        /// World.Step: a faction is hashed, so the disagreement would surface as
        /// a desync on the first checkpoint instead of as a rejection.
        /// </summary>
        private static void FactionMismatchIsRejectedBeforeTick0()
        {
            var link = new FaultyLink(0xFAC7, dropPercent: 0, duplicatePercent: 0, baseDelayMs: 10, jitterMs: 0);
            var a = new Peer(0, link.A, new MatchConfig { LocalFaction = Faction.WaterSlimes }, -1, 0);
            // Peer 1 arrived believing peer 0 plays Hellfire. It does not.
            var b = new Peer(1, link.B, new MatchConfig
            {
                LocalFaction = Faction.RockGolems,
                RemoteFaction = Faction.Hellfire,
            }, -1, 0);

            Drive(link, a, b, 0, () => Stopped(a) && Stopped(b), 60000);

            Check(a.World.Tick == 0 && b.World.Tick == 0, "a tick executed despite a rejected handshake");
            Check((a.Session.StopReason ?? "").Contains("faction"), "peer 0 reason: " + a.Session.StopReason);
            Check((b.Session.StopReason ?? "").Contains("faction"), "peer 1 reason: " + b.Session.StopReason);
        }

        /// <summary>A vanished peer ends the match instead of being simulated with invented input.</summary>
        private static void PeerTimeoutEndsTheMatch()
        {
            var cfg = new MatchConfig { TimeoutMs = 1000 };
            var link = new FaultyLink(0xC0DE, dropPercent: 0, duplicatePercent: 0, baseDelayMs: 20, jitterMs: 0);
            var a = new Peer(0, link.A, cfg, -1, 0);
            var b = new Peer(1, link.B, cfg, -1, 0);

            long now = Drive(link, a, b, 0, () => a.Session.Tick >= 60 && b.Session.Tick >= 60, 60000);
            int cut = a.World.Tick;
            link.Partitioned = true;

            Drive(link, a, b, now, () => Stopped(a) && Stopped(b), 60000);

            Check((a.Session.StopReason ?? "").Contains("timeout"), "peer 0 reason: " + a.Session.StopReason);
            Check((b.Session.StopReason ?? "").Contains("timeout"), "peer 1 reason: " + b.Session.StopReason);
            Check(a.World.Tick <= cut + cfg.InputDelay + 4,
                "peer 0 ran past the last delivered input: " + cut + " -> " + a.World.Tick);
        }

        /// <summary>
        /// One peer, no link, and the barrier opens every tick anyway. Before this
        /// a lone peer sat at tick 0 forever waiting on remote input that was never
        /// coming, which is the reason single player could not exist.
        ///
        /// The command at the end is the other half: a solo peer's own input still
        /// goes through the seal, the input delay, and the canonical sort, so a
        /// solo match replays from its log exactly like a networked one.
        /// </summary>
        private static void SoloRunsWithoutAPeer()
        {
            var cfg = new MatchConfig { Solo = true };
            var world = new World(cfg.Seed);
            world.SpawnUnit(0, Role.Melee, new FixVec2(Fix.FromInt(10), Fix.FromInt(10)));
            var session = new LockstepSession(world, NullTransport.It, cfg, 0);

            session.Update(0);
            Check(session.State == SessionState.Running, "a solo session never started: " + session.StopReason);

            const int SoloTicks = 100;
            for (int t = 0; t < SoloTicks; t++)
            {
                session.Update(t);
                Check(session.TryStep(t), "the solo barrier closed at tick " + world.Tick);
            }
            Check(world.Tick == SoloTicks, "a solo match ran " + world.Tick + " ticks, wanted " + SoloTicks);

            var where = new FixVec2(Fix.FromInt(20), Fix.FromInt(10));
            session.Issue(CommandType.Move, 0, where);
            for (int t = 0; t <= cfg.InputDelay; t++)
            {
                session.Update(SoloTicks + t);
                Check(session.TryStep(SoloTicks + t), "the solo barrier closed while a command was in flight");
            }
            Check(world.GetEntity(0).Target.Equals(where),
                "a solo peer's own command never executed after " + cfg.InputDelay + " ticks of delay");
        }

        /// <summary>
        /// A third party watching the same lossy match, and the two things that
        /// have to be true at once: it sees exactly what the players saw, and the
        /// players never wait for it.
        ///
        /// It attaches late, on purpose, and it freezes for a stretch in the
        /// middle. The freeze is the measurement: the ticks the players advanced
        /// while the spectator advanced none are the number that says it is not
        /// in the barrier. Nothing weaker would — a spectator kept in step by a
        /// driver that pumps it every iteration passes an equality check whether
        /// or not the players are waiting on it.
        ///
        /// Note also what the loop below waits for. Drive's condition names the
        /// two peers and never the spectator, which is what a match on a real
        /// link does; wire the spectator into that condition and this check stops
        /// finishing rather than starts failing an assert.
        /// </summary>
        private static void SpectatorWatchesWithoutHoldingTheBarrier()
        {
            const int AttachAt = 60;    // player tick the spectator shows up at
            const int StallFrom = 140;  // spectator tick it freezes at
            const int StallPumps = 400; // how long it stays frozen, in driver iterations
            const int PlayerTicksDuring = 60; // fewest player ticks that must fit in that freeze

            var cfgA = new MatchConfig { LocalFaction = Faction.WaterSlimes };
            var cfgB = new MatchConfig { LocalFaction = Faction.RockGolems };
            var link = new FaultyLink(0x5EC7A, dropPercent: 20, duplicatePercent: 10, baseDelayMs: 30, jitterMs: 40);
            var a = new Peer(0, link.A, cfgA, -1, 0);
            var b = new Peer(1, link.B, cfgB, -1, 0);

            // Published by one peer because both publish the same thing: the
            // confirmed batch is the merged, canonically ordered input, which is
            // exactly what BothPeersSaveTheSameMatch proves is peer independent.
            var feed = new SpectatorFeed();
            a.Feed = feed;

            long now = Drive(link, a, b, 0, () => Stopped(a) || a.Session.Tick >= AttachAt, 120000);
            Check(!Stopped(a) && !Stopped(b), "the match ended before the spectator attached");

            World world = Peer.BuildWorld(cfgA.Seed, -1, 0);
            world.SetPeerFaction(0, cfgA.LocalFaction);
            world.SetPeerFaction(1, cfgB.LocalFaction);
            var spectator = new Spectator(world, feed);
            var hashes = new Dictionary<int, ulong>();

            Check(feed.OldestTick == 0,
                "the spectator attached to a feed that no longer starts at tick 0, so this is not a late join");

            int playersAtStallStart = -1, playersAtStallEnd = -1;
            int watchedAtStallStart = -1, watchedAtStallEnd = -1;
            int frozenPumps = 0;

            void Watch(long _)
            {
                // The freeze is counted in driver iterations, not in player
                // ticks, so that it ends even if the players stopped advancing.
                // Measured against player progress it would be self-fulfilling:
                // a spectator that really did hold the barrier would freeze the
                // players, the window would never close, and the failure would
                // arrive as a stall with no explanation instead of as the assert
                // below naming what happened.
                if (spectator.Tick >= StallFrom && frozenPumps < StallPumps)
                {
                    if (frozenPumps++ == 0)
                    {
                        playersAtStallStart = a.Session.Tick;
                        watchedAtStallStart = spectator.Tick;
                    }
                    return; // frozen: the whole point is that the match cannot tell
                }
                if (playersAtStallStart >= 0 && playersAtStallEnd < 0)
                {
                    playersAtStallEnd = a.Session.Tick;
                    watchedAtStallEnd = spectator.Tick;
                }
                // Hashed one tick at a time. Add throws on a repeat, so this is
                // also the check that no confirmed frame is replayed twice.
                while (spectator.Follow(1) > 0) hashes.Add(spectator.Tick, world.Hash());
            }

            Drive(link, a, b, now,
                () => Stopped(a) || Stopped(b) || (a.Session.Tick >= Ticks && b.Session.Tick >= Ticks),
                600000, Watch);

            Check(!Stopped(a), "peer 0 stopped: " + a.Session.StopReason);
            Check(!Stopped(b), "peer 1 stopped: " + b.Session.StopReason);
            Check(a.Session.Tick >= Ticks && b.Session.Tick >= Ticks,
                "the players did not reach " + Ticks + " ticks while a spectator watched");

            Check(playersAtStallEnd >= 0, "the spectator never stalled, so nothing was proved about the barrier");
            Check(watchedAtStallEnd == watchedAtStallStart,
                "the spectator advanced during its own stall, from tick " +
                watchedAtStallStart + " to " + watchedAtStallEnd);
            Check(playersAtStallEnd - playersAtStallStart >= PlayerTicksDuring,
                "the players advanced only " + (playersAtStallEnd - playersAtStallStart) +
                " ticks over " + StallPumps + " pumps while the spectator was frozen at tick " +
                watchedAtStallStart + "; the spectator is holding the barrier");

            while (spectator.Follow(1) > 0) hashes.Add(spectator.Tick, world.Hash());
            Check(!spectator.Dropped, "the spectator was dropped: " + spectator.DropReason);
            Check(spectator.Tick >= Ticks,
                "the spectator caught up only to tick " + spectator.Tick + ", wanted " + Ticks);

            for (int t = 1; t <= Ticks; t++)
            {
                Check(hashes.ContainsKey(t), "the spectator has no hash for tick " + t);
                Check(hashes[t] == a.Hashes[t] && hashes[t] == b.Hashes[t],
                    "the spectator diverged from the players at tick " + t);
            }
        }

        /// <summary>
        /// The cost of the bounded window, stated as a check. A spectator that
        /// stops reading for longer than the feed is deep loses frames that
        /// nothing can hand back, and drops itself saying so.
        ///
        /// What matters is the other half: the match runs to completion anyway,
        /// on the same virtual budget, and the players never learn that anything
        /// went wrong. An unbounded feed would avoid the drop by making the
        /// players carry a vanished watcher's backlog instead.
        /// </summary>
        private static void SpectatorThatFallsTooFarBehindIsDropped()
        {
            const int WatchUntil = 40; // player tick the spectator stops reading at
            const int FeedFrames = 16;

            var cfgA = new MatchConfig { LocalFaction = Faction.WaterSlimes };
            var cfgB = new MatchConfig { LocalFaction = Faction.RockGolems };
            var link = new FaultyLink(0xC0FFEE5, dropPercent: 20, duplicatePercent: 10, baseDelayMs: 30, jitterMs: 40);
            var a = new Peer(0, link.A, cfgA, -1, 0);
            var b = new Peer(1, link.B, cfgB, -1, 0);

            var feed = new SpectatorFeed(FeedFrames);
            a.Feed = feed;

            World world = Peer.BuildWorld(cfgA.Seed, -1, 0);
            world.SetPeerFaction(0, cfgA.LocalFaction);
            world.SetPeerFaction(1, cfgB.LocalFaction);
            var spectator = new Spectator(world, feed);

            void WatchThenLeave(long _)
            {
                if (a.Session.Tick > WatchUntil) return; // walked away without a goodbye
                spectator.Follow(1);
            }

            Drive(link, a, b, 0,
                () => Stopped(a) || Stopped(b) || (a.Session.Tick >= Ticks && b.Session.Tick >= Ticks),
                600000, WatchThenLeave);

            Check(!Stopped(a) && !Stopped(b), "a spectator that walked away ended the match");
            Check(a.Session.Tick >= Ticks && b.Session.Tick >= Ticks,
                "the players did not reach " + Ticks + " ticks after the spectator stopped reading");

            int watched = spectator.Tick;
            Check(spectator.Follow(1) == 0, "the spectator stepped a frame the feed had already overwritten");
            Check(spectator.Dropped, "the spectator did not notice it had lost " +
                                     (a.Session.Tick - watched) + " ticks of frames");
            Check((spectator.DropReason ?? "").Contains("left the feed"),
                "the spectator dropped for the wrong reason: " + spectator.DropReason);
            Check(spectator.Tick == watched, "a dropped spectator kept stepping, to tick " + spectator.Tick);
        }

        private static bool Stopped(Peer p) => p.Session.State == SessionState.Stopped;

        /// <summary>
        /// Advances the virtual clock until the condition holds. The optional
        /// watcher runs after both peers have pumped and is never part of the
        /// condition: it is for a party the match does not wait for, and putting
        /// it in `until` would be exactly the mistake the spectator checks are
        /// looking for.
        /// </summary>
        private static long Drive(FaultyLink link, Peer a, Peer b, long now, Func<bool> until, long budgetMs,
            Action<long> watcher = null)
        {
            long deadline = now + budgetMs;
            while (now <= deadline)
            {
                link.Now = now;
                a.Pump(now);
                b.Pump(now);
                watcher?.Invoke(now);
                if (until()) return now;
                now += StepMs;
            }
            throw new Exception("stalled: condition never held within " + budgetMs + " virtual ms" +
                                " (peer 0 tick " + a.World.Tick + " " + a.Session.State + " " + a.Session.StopReason +
                                ", peer 1 tick " + b.World.Tick + " " + b.Session.State + " " + b.Session.StopReason + ")");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
