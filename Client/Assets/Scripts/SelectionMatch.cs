using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Whether two entities are the same kind for selection purposes. Double
    /// click, ctrl-click on a selection chip (Selection.KeepKind), and the
    /// on-screen same-kind select all reduce to this one question. Pulled out
    /// on its own so the answer — unlike Selection's Camera picking, screen
    /// bounds, and drag box, all of which need a scene — can run headless in
    /// Replay/SelectionMatchChecks.cs, the same way #96 pulled HitDetection
    /// and AlertWindow out of Hits.cs and Alert.cs.
    ///
    /// Role alone stopped meaning "the same unit" the day #114 let one role
    /// hold several roster entries: 지옥불's Ranged holds both 균열 파수병 and
    /// the warlord's free 자손 (one walks, one flies, and their stats
    /// differ), and 차원 유랑종's Melee holds 폭풍편 beside two extinct-slime
    /// summons. Entity.Slot (#114, hashed) is the half a role-only match was
    /// missing — without it, a double click on 균열 파수병 grabs 자손 too, and
    /// an attack-move issued to the mixed group moves only part of it.
    /// </summary>
    public static class SelectionMatch
    {
        /// <summary>Same roster entry: same Role, and within it, the same Slot.</summary>
        public static bool SameKind(Entity a, Entity b) => a.Role == b.Role && a.Slot == b.Slot;
    }
}
