using System;
using System.Collections.Generic;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track B's write scope is Client/Assets/Scripts/ (.plan/general/2026-08-11-parallel-milestones.md).
    /// Replay/Program.cs belongs to track A's checks, so this file holds the
    /// one issue #96 adds and Program.cs only carries the single line that
    /// calls Check().
    ///
    /// Checks HitDetection and AlertWindow, the two pieces the alert feature's
    /// decisions were pulled into so they could run without a Camera, a
    /// Minimap, or Time.unscaledTime. Hits.cs and Alert.cs themselves stay
    /// untested here on purpose — they are now nothing but Unity plumbing over
    /// these two, and neither compiles outside Unity.
    /// </summary>
    internal static class AlertChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            AnHpDropIsCaught();
            NoDropIsNotCaught();
            AnotherPeersLossIsIgnored();
            AnOnScreenHitIsFilteredOut();
            TheCooldownSuppressesASecondAlert();
        }

        /// <summary>
        /// A worker never fights back (see Program.cs's RunAttackOrder, which
        /// uses the same trick for the same reason), so ordering a melee unit
        /// to attack one is the simplest way to put a one-directional hp drop
        /// on the board without the two sides muddying each other's numbers.
        /// </summary>
        private static World FightUntilHpDrops(int attackerOwner, int targetOwner, out int targetId)
        {
            World world = new World(Seed);
            int attacker = world.SpawnUnit(attackerOwner, Role.Melee, At(20, 20));
            int target = world.SpawnWorker(targetOwner, At(21, 20));
            targetId = target;

            // Baseline before anything can have dropped. HitDetection seeds a
            // freshly seen entity's "was" hp from whatever it currently reads,
            // so a first read taken after combat already started would hide
            // the very drop this scenario exists to produce.
            HitDetection.OffScreen(world, targetOwner, _ => false);

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

        private static void AnHpDropIsCaught()
        {
            World world = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out int target);

            var hits = HitDetection.OffScreen(world, localPeer: 1, isOnScreen: _ => false);
            Check(hits.Count > 0, "an hp drop that happened was not caught");

            FixVec2 hurtAt = world.GetEntity(target).Position;
            bool found = false;
            for (int i = 0; i < hits.Count; i++) found |= hits[i].Equals(hurtAt);
            Check(found, "the caught point was not the hurt entity's position");
        }

        private static void NoDropIsNotCaught()
        {
            World world = new World(Seed);
            int lonely = world.SpawnUnit(0, Role.Melee, At(5, 5));
            // Outside World.AcquireRange (10) of anything, so nothing ever
            // engages and the fixture proves the negative it claims to.
            world.SpawnUnit(1, Role.Melee, At(60, 60));

            HitDetection.OffScreen(world, 0, _ => false); // baseline
            var idle = new List<Command>();
            for (int t = 0; t < 50; t++) world.Step(idle);

            Check(world.GetEntity(lonely).Hp == world.GetEntity(lonely).MaxHp,
                "the fixture dealt damage it was not supposed to; it proves nothing");

            var hits = HitDetection.OffScreen(world, 0, _ => false);
            Check(hits.Count == 0, "a drop was reported where none happened");
        }

        private static void AnotherPeersLossIsIgnored()
        {
            World world = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out _);

            // The attacker's own peer, not the one whose worker lost hp. A
            // worker never fights back, so peer 0 has no drop of its own here;
            // any hit reported for it can only be peer 1's leaking through.
            var hits = HitDetection.OffScreen(world, localPeer: 0, isOnScreen: _ => false);
            Check(hits.Count == 0, "another peer's loss was reported as the local peer's own");
        }

        private static void AnOnScreenHitIsFilteredOut()
        {
            World world = FightUntilHpDrops(attackerOwner: 0, targetOwner: 1, out _);

            // Pretend the whole world is already visible.
            var hits = HitDetection.OffScreen(world, localPeer: 1, isOnScreen: _ => true);
            Check(hits.Count == 0, "an on-screen hit was not filtered out");
        }

        /// <summary>
        /// Also covers expiry (Showing goes false at ShowSeconds, independent
        /// of the longer cooldown), since both are the clock AlertWindow was
        /// split out to take as a parameter instead of reading.
        /// </summary>
        private static void TheCooldownSuppressesASecondAlert()
        {
            const float showSeconds = 4f;
            const float cooldownSeconds = 6f;

            AlertWindow window = AlertWindow.None;
            window = window.Advance(true, 0f, showSeconds, cooldownSeconds, out bool armedFirst);
            Check(armedFirst, "the first hit into a fresh window did not arm it");
            Check(window.Showing(0f), "the alert did not show right after arming");

            AlertWindow afterSecondHit = window.Advance(true, 1f, showSeconds, cooldownSeconds, out bool armedSecond);
            Check(!armedSecond, "a second hit inside the cooldown re-armed the alert");
            Check(afterSecondHit.Until == window.Until, "a suppressed hit still moved the expiry");
            Check(afterSecondHit.CooldownUntil == window.CooldownUntil, "a suppressed hit still moved the cooldown");

            Check(afterSecondHit.Showing(3.999f), "the alert stopped showing before its own expiry");
            Check(!afterSecondHit.Showing(4f), "the alert kept showing past its own expiry");

            // Six seconds on: the cooldown (not the four-second display) has
            // now elapsed, so a hit is allowed to arm the window again.
            AlertWindow rearmed = afterSecondHit.Advance(true, 6f, showSeconds, cooldownSeconds, out bool armedThird);
            Check(armedThird, "a hit after the cooldown elapsed failed to re-arm the alert");
            Check(rearmed.Until == 10f, "the re-armed window did not expire relative to the new now");
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
