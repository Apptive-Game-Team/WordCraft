using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// "you are under attack": the one sentence this HUD ever puts on screen
    /// unasked. Minimap already marks an off-screen hit with a dot at the edge
    /// of a 200px square; a player mid-fight watching the front line does not
    /// see it. This turns the same hit into words, plus a key to go there.
    ///
    ///   Space   camera to wherever the alert is pointing at
    ///
    /// Reads Hits rather than watching hp itself — see Hits for why a hit is
    /// read off the entity rather than told by the simulation. View only:
    /// nothing here is asked of World but GetEntity and Hp, and nothing here
    /// is ever written back to it.
    /// </summary>
    public static class Alert
    {
        private const KeyCode JumpKey = KeyCode.Space;

        /// <summary>How long the sentence stays readable before it fades from having happened.</summary>
        private const float ShowSeconds = 4f;

        /// <summary>
        /// How long after firing before the next hit can fire again. Longer
        /// than ShowSeconds on purpose: a base under sustained fire loses hp
        /// every tick, and without a cooldown separate from the display time
        /// the sentence would re-arm itself every tick it is still true
        /// instead of reading as one event.
        /// </summary>
        private const float CooldownSeconds = 6f;

        private static Vector2 point;

        /// <summary>Unscaled time the sentence stops showing. Time.unscaledTime never reaches -1.</summary>
        private static float until = -1f;

        private static float cooldownUntil = -1f;

        /// <summary>The world the state above belongs to. A new one makes it a lie.</summary>
        private static World shown;

        /// <summary>
        /// Call once a frame from the match screen. Reads the same off-screen
        /// hit list Minimap paints from, so the two never disagree about what
        /// counts as one.
        /// </summary>
        public static void Draw(World world, int localPeer)
        {
            if (!ReferenceEquals(world, shown))
            {
                shown = world;
                until = -1f;
                cooldownUntil = -1f;
            }

            var hits = Hits.OffScreen(world, localPeer, Camera.main);
            if (hits.Count > 0 && Time.unscaledTime >= cooldownUntil)
            {
                // The latest hit rather than the first of the tick: a player
                // who already missed several only cares where the fight is now.
                point = hits[hits.Count - 1];
                until = Time.unscaledTime + ShowSeconds;
                cooldownUntil = Time.unscaledTime + CooldownSeconds;
            }

            bool showing = Time.unscaledTime < until;

            // Gated on showing rather than always live: a key that jumps
            // somewhere with nothing on screen naming the spot would be a
            // control nobody could discover or trust.
            if (showing && Event.current.type == EventType.KeyDown && Event.current.keyCode == JumpKey)
            {
                if (CameraRig.Instance != null) CameraRig.Instance.CenterOn(point);
                Event.current.Use();
            }

            if (!showing || Event.current.type != EventType.Repaint) return;

            Rect box = UiStyle.AlertBox();
            UiStyle.PanelBox(box, UiStyle.Panel);

            float x = box.x + UiStyle.S3;
            float width = box.width - UiStyle.S3 * 2f;
            UiStyle.Label(new Rect(x, box.y + UiStyle.S2, width, UiStyle.Line), "you are under attack", UiStyle.Danger);
            UiStyle.Note(new Rect(x, box.y + UiStyle.S2 + UiStyle.Line + UiStyle.S1, width, UiStyle.Micro + UiStyle.S1),
                "space  ·  jump there");
        }
    }
}
