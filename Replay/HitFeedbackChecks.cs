using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track B's write scope is Client/Assets/Scripts/ (.plan/general/2026-08-11-parallel-milestones.md).
    /// Replay/Program.cs belongs to track A's checks, so this file holds the
    /// one issue #104 adds and Program.cs only carries the single line that
    /// calls Check() — the same split AlertChecks.cs already set up for #96.
    ///
    /// Checks HitDetection.HitIds, the one piece of #104's on-screen hit
    /// feedback that has a decision worth pinning down without a Camera or a
    /// scene: that it is the same scan OffScreen already trusted (issue #96),
    /// just unfiltered by where the hit lands on screen, and that the two do
    /// not steal each other's tick when both are asked in it. The flash itself
    /// — a fading SpriteRenderer ring — is Unity plumbing over that answer and
    /// stays untested here, same as Hits.cs and Alert.cs stay untested next to
    /// HitDetection and AlertWindow.
    /// </summary>
    internal static class HitFeedbackChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            AnOnScreenHitStillReachesHitIds();
            HitIdsAndOffScreenAgreeWhicheverIsAskedFirst();
            AnotherPeersHitIsNotInHitIds();
        }

        /// <summary>
        /// Same fixture shape as AlertChecks.FightUntilHpDrops: a melee unit
        /// ordered at a worker, which never fights back, so the hp drop is
        /// one-directional and unambiguous.
        /// </summary>
        private static World FightUntilHpDrops(int attackerOwner, int targetOwner, out int targetId)
        {
            World world = new World(Seed);
            int attacker = world.SpawnUnit(attackerOwner, Role.Melee, At(20, 20));
            int target = world.SpawnWorker(targetOwner, At(21, 20));
            targetId = target;

            // Baseline before anything can have dropped, through the same Scan
            // OffScreen seeds itself with in AlertChecks — the two entry points
            // share one cache, and a check that only ever calls one of them
            // would not prove that sharing is safe.
            HitDetection.HitIds(world, targetOwner);

            var order = new List<Command> { new Command(0, attackerOwner, 0, CommandType.Attack, attacker, FixVec2.Zero, target) };
            var idle = new List<Command>();
            world.Step(order);

            int guard = 0;
            while (world.GetEntity(target).Hp == world.GetEntity(target).MaxHp)
            {
                world.Step(idle);
                if (++guard > 400) throw new Exception("fixture never dealt damage inside the guard window");
            }
            return world;
        }

        /// <summary>
        /// OffScreen(isOnScreen: _ => true) reports nothing — the whole point of
        /// that filter — but the on-screen flash needs exactly the hit OffScreen
        /// just threw away. HitIds is where it has to still be, or the flash has
        /// nothing to read and #104's on-screen half never fires.
        /// </summary>
        private static void AnOnScreenHitStillReachesHitIds()
        {
            World world = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out int target);

            var onScreen = HitDetection.OffScreen(world, localPeer: 1, isOnScreen: _ => true);
            Check(onScreen.Count == 0, "an on-screen hit leaked through OffScreen's own filter");

            var ids = HitDetection.HitIds(world, localPeer: 1);
            bool found = false;
            for (int i = 0; i < ids.Count; i++) found |= ids[i] == target;
            Check(found, "HitIds dropped a hit that OffScreen's filter alone was supposed to exclude");
        }

        /// <summary>
        /// HitIds and OffScreen share one hp scan, cached once a tick
        /// (HitDetection.Scan). Whichever of the two a caller reaches for
        /// first in a given tick must not leave the other reading a stale or
        /// empty answer for the rest of it — the bug this once-per-tick split
        /// would reintroduce if the scan and the screen filter were cached
        /// together instead of separately.
        /// </summary>
        private static void HitIdsAndOffScreenAgreeWhicheverIsAskedFirst()
        {
            World first = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out int firstTarget);
            var idsFirst = HitDetection.HitIds(first, localPeer: 1);
            var offAfterIds = HitDetection.OffScreen(first, localPeer: 1, isOnScreen: _ => false);
            Check(idsFirst.Count > 0, "HitIds asked first found nothing to report");
            Check(offAfterIds.Count == idsFirst.Count,
                "OffScreen asked after HitIds in the same tick disagreed on how many hits happened");
            FixVec2 targetPos = first.GetEntity(firstTarget).Position;
            Check(offAfterIds[0].Equals(targetPos), "OffScreen asked after HitIds pointed at the wrong body");

            World second = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out int secondTarget);
            var offFirst = HitDetection.OffScreen(second, localPeer: 1, isOnScreen: _ => false);
            var idsAfterOff = HitDetection.HitIds(second, localPeer: 1);
            Check(offFirst.Count > 0, "OffScreen asked first found nothing to report");
            bool foundSecond = false;
            for (int i = 0; i < idsAfterOff.Count; i++) foundSecond |= idsAfterOff[i] == secondTarget;
            Check(foundSecond, "HitIds asked after OffScreen in the same tick lost the hit");
        }

        /// <summary>Mirrors AlertChecks.AnotherPeersLossIsIgnored for the new entry point.</summary>
        private static void AnotherPeersHitIsNotInHitIds()
        {
            World world = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out _);

            var ids = HitDetection.HitIds(world, localPeer: 0);
            Check(ids.Count == 0, "another peer's loss was reported as the local peer's own");
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
