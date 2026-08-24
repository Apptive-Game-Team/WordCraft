using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Whether a 징발 order would be taken, asked before the client sends one and
    /// before it draws the cell that sends it.
    ///
    /// Pulled out on its own, free of UnityEngine, for the reason ProductionMenu
    /// and SelectionMatch were: the one part of this feature a graphics device
    /// cannot see is whether the view's test still says what the simulation says,
    /// and that runs headless in Replay/CaptureOrderChecks.cs. Orders.cs and
    /// CommandCard.cs are the Unity-facing halves that turn the answer into a
    /// click and a button.
    ///
    /// The view filters only what it would be silly to send — the simulation is
    /// still what decides, and it refuses the rest in silence. The reason this
    /// one is asked twice rather than left to the simulation alone is the card:
    /// 징발 belongs to one faction of six, and a cell that five factions may press
    /// and none of them may use is a cell they learn by being ignored.
    /// </summary>
    public static class CaptureOrder
    {
        /// <summary>
        /// Whether this faction may capture at all. Read off World.CaptureFaction
        /// rather than named again here, so the card cannot come to disagree with
        /// the rule that refuses the command.
        /// </summary>
        public static bool Available(Faction faction) => faction == World.CaptureFaction;

        /// <summary>
        /// Whether this peer ordering this body to take this target is an order
        /// worth putting on the wire: the peer's faction captures, the body is a
        /// live worker of theirs, and the target is a 꼬마돌 nobody has taken.
        ///
        /// Every clause is the simulation's own, in the simulation's order, and
        /// the neutrality test is World.IsNeutralRock itself rather than a second
        /// reading of Owner and Role. A capture the client thought it could make
        /// and the simulation dropped is a worker standing where the player left
        /// it with no message of any kind, which is the failure this exists to
        /// keep from happening.
        /// </summary>
        public static bool Allows(World world, int peer, int workerId, int targetId)
        {
            if (!Available(world.FactionOf(peer))) return false;
            if (workerId < 0 || workerId >= world.EntityCount) return false;
            if (targetId < 0 || targetId >= world.EntityCount) return false;

            Entity worker = world.GetEntity(workerId);
            if (!worker.Alive || worker.Owner != peer || worker.Kind != EntityKind.Worker) return false;

            return world.IsNeutralRock(world.GetEntity(targetId));
        }

        /// <summary>
        /// How far through a capture this body is, from 0 to 1, or -1 when it is
        /// not capturing. One reading for the field meter and the panel's line, so
        /// the bar and the sentence can never say different numbers.
        ///
        /// CaptureTicksLeft is zero both before the clock starts and after it ends
        /// (the worker is still walking, or the rock has just changed hands), and
        /// CaptureTargetId is what tells those apart from not capturing at all.
        /// A worker on its way is 0, not nothing: the order has been taken.
        /// </summary>
        public static float Progress(Entity worker)
        {
            if (worker.CaptureTargetId < 0) return -1f;
            if (worker.CaptureTicksLeft <= 0) return 0f;
            return (World.CaptureTicks - worker.CaptureTicksLeft) / (float)World.CaptureTicks;
        }
    }
}
