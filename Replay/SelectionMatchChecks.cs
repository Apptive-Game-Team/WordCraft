using System;
using WordCraft.Sim;
using WordCraft.View;

namespace WordCraft.Replay
{
    /// <summary>
    /// Track B's write scope is Client/Assets/Scripts/ (.plan/general/2026-08-11-parallel-milestones.md).
    /// Replay/Program.cs belongs to track A's checks, so this file holds the one
    /// issue #118 adds and Program.cs only carries the single line that calls
    /// Check() — the same split AlertChecks.cs and ProductionMenuChecks.cs set
    /// up for #96 and #114.
    ///
    /// Checks SelectionMatch.SameKind, the one decision #118 made that no
    /// batch-run Unity pass can see coming: whether two entities that share a
    /// Role but not a Slot are still read as "the same kind" for double click,
    /// ctrl-click, and KeepKind. Selection.cs itself — Camera picking, screen
    /// bounds, the drag box — is Unity plumbing over that answer and stays
    /// untested here, same as Hits.cs and Alert.cs stay untested next to
    /// HitDetection and AlertWindow.
    /// </summary>
    internal static class SelectionMatchChecks
    {
        private const ulong Seed = 0xC0FFEE;

        public static void Check()
        {
            SameRoleSameSlotMatches();
            SameRoleDifferentSlotDoesNotMatch();
            DifferentRoleDoesNotMatch();
            TheHellfireWardenAndItsOffspringDoNotMatch();
        }

        private static void SameRoleSameSlotMatches()
        {
            World world = new World(Seed);
            int a = world.SpawnUnit(0, Role.Ranged, 0, At(10, 10));
            int b = world.SpawnUnit(0, Role.Ranged, 0, At(11, 10));
            Check(SelectionMatch.SameKind(world.GetEntity(a), world.GetEntity(b)),
                "two entities in the same role and the same slot were read as different kinds");
        }

        /// <summary>The bug #118 exists to close: a role-only match would pass this.</summary>
        private static void SameRoleDifferentSlotDoesNotMatch()
        {
            World world = new World(Seed);
            int a = world.SpawnUnit(0, Role.Ranged, 0, At(10, 10));
            int b = world.SpawnUnit(0, Role.Ranged, 1, At(11, 10));
            Check(!SelectionMatch.SameKind(world.GetEntity(a), world.GetEntity(b)),
                "two entities sharing a role but not a slot were read as the same kind");
        }

        private static void DifferentRoleDoesNotMatch()
        {
            World world = new World(Seed);
            int a = world.SpawnUnit(0, Role.Melee, 0, At(10, 10));
            int b = world.SpawnUnit(0, Role.Ranged, 0, At(11, 10));
            Check(!SelectionMatch.SameKind(world.GetEntity(a), world.GetEntity(b)),
                "two entities in different roles were read as the same kind");
        }

        /// <summary>
        /// The scenario the issue names directly. FactionData.cs keys 지옥불's
        /// ranged row by entry: World.WarlordOffspringRole / WarlordOffspringSlot
        /// names the warlord's free 자손 (entry 0, airborne), and its own comment
        /// says 균열 파수병 — bought, grounded — is "entry 1 and holds the ground
        /// on the shared ranged row". Spawned through those names rather than two
        /// bare literals, so a future reshuffle of the Hellfire roster moves this
        /// check with it instead of silently checking the wrong pair.
        /// </summary>
        private static void TheHellfireWardenAndItsOffspringDoNotMatch()
        {
            const int RiftWardenSlot = World.WarlordOffspringSlot + 1;

            World world = new World(Seed);
            int warden = world.SpawnUnit(0, World.WarlordOffspringRole, RiftWardenSlot, At(10, 10));
            int offspring = world.SpawnUnit(0, World.WarlordOffspringRole, World.WarlordOffspringSlot, At(11, 10));

            Check(!SelectionMatch.SameKind(world.GetEntity(warden), world.GetEntity(offspring)),
                "지옥불's warden and its free offspring were read as the same kind despite #114 splitting them into different slots");
        }

        private static FixVec2 At(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
