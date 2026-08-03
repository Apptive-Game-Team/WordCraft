using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The square at the left end of the bottom bar: the whole map at once, and
    /// the camera's window on it. Without one there is no way to answer "where",
    /// which is the only question a player of this game asks that the screen in
    /// front of them cannot.
    ///
    /// A Texture2D repainted from entity state plus a few GUI rectangles. A
    /// second camera onto a RenderTexture would cost a whole extra render pass to
    /// draw the real art at four pixels across, which is a smear.
    ///
    /// Drawn by Hud rather than from an OnGUI of its own, because the panel
    /// background has to be underneath it and the order two components' OnGUI run
    /// in is undefined. Static because there is one map and one texture; there is
    /// nothing per instance to hold.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class Minimap
    {
        /// <summary>Texture pixels across. Two per grid cell, so a unit is a dot and a building a block.</summary>
        private const int Res = 128;

        private const int UnitRadius = 1;     // 3x3 px
        private const int BuildingRadius = 2; // 5x5 px, so buildings read as the bigger thing

        /// <summary>
        /// Lighter than the world's ground, so the square reads as an object
        /// against the near-black panel, and dark enough that both peer colours
        /// stay legible on it. Enemy orange on dark grey is the pairing that has
        /// to survive a glance; that is what fixes this value.
        /// </summary>
        private static readonly Color32 GroundColor = new Color32(38, 40, 46, 255);

        private static readonly Color32 NodeColor = new Color32(255, 209, 64, 255);
        private static readonly Color ViewColor = new Color(1f, 1f, 1f, 0.75f);

        private static Texture2D texture;
        private static Color32[] pixels;

        /// <summary>
        /// Paints the map into <paramref name="area"/>. Call from OnGUI; the
        /// texture is rebuilt on the repaint pass only, because OnGUI runs several
        /// times a frame and the world has not moved between two of them.
        /// </summary>
        public static void Draw(Rect area, MatchRunner runner)
        {
            if (Event.current.type != EventType.Repaint) return;

            Paint(runner.World);
            GUI.DrawTexture(area, texture);
            ViewRect(area);
        }

        /// <summary>
        /// Every alive entity, every frame, over a cleared buffer.
        /// </summary>
        // ponytail: 16k pixel clear, one blob per entity, and a 64 KB texture
        // upload per frame, for a few dozen entities on a 64x64 map. Repaint only
        // the cells that changed, or keep static things in a second texture drawn
        // underneath, the day a profile says this costs anything.
        private static void Paint(World world)
        {
            if (texture == null)
            {
                texture = new Texture2D(Res, Res, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Point, // dots, not smudges
                    wrapMode = TextureWrapMode.Clamp,
                };
                pixels = new Color32[Res * Res];
            }

            for (int i = 0; i < pixels.Length; i++) pixels[i] = GroundColor;

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                if (!e.Alive) continue;
                Blob(e.Position, Tint(e), e.Kind == EntityKind.Building ? BuildingRadius : UnitRadius);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
        }

        /// <summary>
        /// The owner's colour, the same one the world draws with, so a blue blob
        /// here is the blue army there. Size is what tells a building from a unit;
        /// hue is only ever whose it is.
        /// </summary>
        private static Color32 Tint(Entity e)
        {
            if (e.Kind == EntityKind.ResourceNode || e.Owner < 0) return NodeColor;
            return e.Owner < MatchView.PeerColor.Length ? MatchView.PeerColor[e.Owner] : Color.gray;
        }

        /// <summary>
        /// A filled square of pixels at a world point, clipped: a building's block
        /// hangs off the edge of the texture at the map corners. Tick positions
        /// rather than the interpolated draw ones, because the difference is under
        /// a pixel at this scale and it saves knowing about the runner.
        /// </summary>
        private static void Blob(FixVec2 position, Color32 color, int radius)
        {
            Vector2 p = MatchRunner.ToView(position);
            int cx = (int)(p.x * Res / MatchScenario.MapSize);
            int cy = (int)(p.y * Res / MatchScenario.MapSize);

            for (int y = cy - radius; y <= cy + radius; y++)
            {
                if (y < 0 || y >= Res) continue;
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || x >= Res) continue;
                    // Texture row 0 is the bottom one and GUI.DrawTexture puts the
                    // last row at the top of the rect, so world +y is up on screen
                    // with no flip anywhere.
                    pixels[y * Res + x] = color;
                }
            }
        }

        /// <summary>
        /// What the camera can see, as an outline. Clamped to the square, because
        /// the rig lets the view sit a few cells past the map edge and a rectangle
        /// drawn outside here would land on the selection list.
        /// </summary>
        private static void ViewRect(Rect area)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            Vector3 c = cam.transform.position;

            float left = X(area, c.x - halfWidth);
            float right = X(area, c.x + halfWidth);
            float top = Y(area, c.y + halfHeight);
            float bottom = Y(area, c.y - halfHeight);

            GUI.color = ViewColor;
            Line(left, top, right - left, 1f);
            Line(left, bottom, right - left, 1f);
            Line(left, top, 1f, bottom - top);
            Line(right, top, 1f, bottom - top);
            GUI.color = Color.white;
        }

        private static void Line(float x, float y, float width, float height) =>
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

        /// <summary>World x to a pixel column of the square. Clamped, so nothing draws outside it.</summary>
        private static float X(Rect area, float worldX) =>
            area.x + Mathf.Clamp01(worldX / MatchScenario.MapSize) * area.width;

        /// <summary>World y to a pixel row. GUI y grows downward and world y grows up, hence the flip.</summary>
        private static float Y(Rect area, float worldY) =>
            area.y + (1f - Mathf.Clamp01(worldY / MatchScenario.MapSize)) * area.height;
    }
}
