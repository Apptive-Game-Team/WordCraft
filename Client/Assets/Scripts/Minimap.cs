using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The square at the left end of the bottom bar: the whole map at once, and
    /// the camera's window on it, which is a trapezoid because the camera looks
    /// down at a slant. Without one there is no way to answer "where",
    /// which is the only question a player of this game asks that the screen in
    /// front of them cannot.
    ///
    ///   left click   put the camera there
    ///   left drag    keep putting it there
    ///   right click  the order a right click at that world point would give
    ///
    /// Something of yours taking damage off screen leaves a mark here for a
    /// couple of seconds — Alert reads the same hits and says so in words, but
    /// the dot was here first and stays for a glance that does not need one.
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

        /// <summary>How long a hit somewhere else stays lit, in seconds.</summary>
        private const float MarkSeconds = 2.5f;

        /// <summary>A hit this near a live mark refreshes it instead of adding another.</summary>
        private const float MarkMergeCells = 4f;

        /// <summary>Value added to open ground per height level: +8% a level, so three levels of plateau are a quarter lighter.</summary>
        private const float LevelLift = 0.08f;

        /// <summary>What the pixels along a cliff are multiplied by.</summary>
        private const float CliffShade = 0.6f;

        /// <summary>A mark is a square this many pixels across. Big enough to catch the eye off-centre.</summary>
        private const float MarkPixels = 7f;

        private static Texture2D texture;
        private static Color32[] pixels;

        /// <summary>
        /// The terrain, painted once per world. Terrain cannot change during a
        /// match, so the ground under the dots is a copy rather than a repaint —
        /// which is cheaper than the flat clear this replaced, not dearer.
        /// </summary>
        private static Color32[] backdrop;

        /// <summary>
        /// The same terrain with the fog already burnt into it. Rebuilt when vision
        /// changes, which is once every few ticks, so a frame still costs one copy.
        /// A minimap that showed the whole map would make the field's fog cosmetic:
        /// this square is where a player looks to find out what is happening
        /// somewhere else, which is exactly what scouting is supposed to buy.
        /// </summary>
        private static Color32[] fogged;

        private static int foggedVersion = -1;

        /// <summary>True while a left drag that began on the square is still down.</summary>
        private static bool scrubbing;

        private static readonly List<Mark> marks = new List<Mark>();

        /// <summary>The four view corners on the ground, and whether each landed. Held so a repaint allocates nothing.</summary>
        private static readonly Vector2[] viewGround = new Vector2[4];
        private static readonly bool[] viewLanded = new bool[4];

        private struct Mark
        {
            public Vector2 Point;
            public float Until;
        }

        /// <summary>The world the state above belongs to. A new one makes it a lie.</summary>
        private static World shown;

        /// <summary>
        /// Paints the map into <paramref name="area"/>. Call from OnGUI; the
        /// texture is rebuilt on the repaint pass only, because OnGUI runs several
        /// times a frame and the world has not moved between two of them.
        /// </summary>
        public static void Draw(Rect area, MatchRunner runner)
        {
            World world = runner.World;
            if (!ReferenceEquals(world, shown))
            {
                // A restart hands out ids from zero again, so nothing held across
                // one is about the match being played now.
                scrubbing = false;
                marks.Clear();
                Backdrop(world);
                foggedVersion = -1;
                shown = world;
            }

            Mouse(area);
            if (Event.current.type != EventType.Repaint) return;

            Paint(world);
            GUI.DrawTexture(area, texture);
            ViewRect(area);
            Marks(area, world, runner.LocalPeer);

            // The frame goes on last so nothing painted inside covers it. It is the
            // only edge the square has: it sits flush in the panel corner, and
            // without a line the map and the panel are one dark shape.
            UiStyle.Frame(area, UiStyle.Edge);
        }

        /// <summary>
        /// Something of yours is being hit somewhere you are not looking. Off
        /// screen and yours is Hits's question to answer, not this file's — the
        /// alert asks the same one, and a second walk over every entity here
        /// would just be this method computing Hits's own answer again.
        ///
        /// Only what is off screen is marked. A fight the player is watching needs
        /// no second telling, and a mark under the battle they already have selected
        /// is noise on the one channel that has to mean "look away from this".
        /// </summary>
        private static void Marks(Rect area, World world, int localPeer)
        {
            IReadOnlyList<Vector2> hits = Hits.OffScreen(world, localPeer, Camera.main);
            for (int i = 0; i < hits.Count; i++) Add(hits[i]);

            for (int i = marks.Count - 1; i >= 0; i--)
            {
                float left = marks[i].Until - Time.unscaledTime;
                if (left <= 0f)
                {
                    marks.RemoveAt(i);
                    continue;
                }

                Color fade = UiStyle.MinimapMark;
                fade.a = left / MarkSeconds;
                UiStyle.Fill(new Rect(
                    X(area, marks[i].Point.x) - MarkPixels * 0.5f,
                    Y(area, marks[i].Point.y) - MarkPixels * 0.5f, MarkPixels, MarkPixels), fade);
            }
        }

        /// <summary>
        /// One fight is one mark. A battle damages a dozen bodies a second, and a
        /// mark each would be a red smear that never fades while it lasts.
        /// </summary>
        private static void Add(Vector2 point)
        {
            float until = Time.unscaledTime + MarkSeconds;
            for (int i = 0; i < marks.Count; i++)
            {
                if (Vector2.Distance(marks[i].Point, point) > MarkMergeCells) continue;
                marks[i] = new Mark { Point = marks[i].Point, Until = until };
                return;
            }
            marks.Add(new Mark { Point = point, Until = until });
        }

        /// <summary>
        /// A left press puts the camera where it landed and a drag keeps doing it,
        /// so scrubbing across the map sweeps the view across the map. The camera
        /// is not simulation state and no peer hears about it.
        ///
        /// A right press is an order, handed to Orders as a world point so the
        /// minimap never decides what a right click means. Press only, so holding
        /// the button down is one order rather than one per event.
        ///
        /// IMGUI events rather than Input, because OnGUI runs several times a
        /// frame and Input.GetMouseButtonDown is true on every one of them. The
        /// drag is gated on having started here: a selection box dragged across
        /// the bar must not teleport the camera on its way past.
        /// </summary>
        private static void Mouse(Rect area)
        {
            Event ev = Event.current;
            if (ev.type == EventType.MouseUp) scrubbing = false;

            // Deliberately not fenced to the square: a scrub that runs off the
            // edge keeps following the pointer, clamped, rather than sticking.
            if (scrubbing && ev.type == EventType.MouseDrag)
            {
                Center(ToWorld(area, ev.mousePosition));
                ev.Use();
                return;
            }

            if (ev.type != EventType.MouseDown || !area.Contains(ev.mousePosition)) return;
            Vector2 point = ToWorld(area, ev.mousePosition);

            if (ev.button == 0)
            {
                scrubbing = true;
                Center(point);
            }
            else if (ev.button == 1 && Orders.Instance != null)
            {
                Orders.Instance.RightClick(point);
            }
            else
            {
                return; // the middle button is the camera rig's, and it reads Input
            }
            ev.Use();
        }

        private static void Center(Vector2 point)
        {
            if (CameraRig.Instance != null) CameraRig.Instance.CenterOn(point);
        }

        /// <summary>A point on the square as a point on the map. Clamped, so a drag off the edge still lands.</summary>
        private static Vector2 ToWorld(Rect area, Vector2 mouse) =>
            new Vector2(
                Mathf.Clamp01((mouse.x - area.x) / area.width) * MatchScenario.MapSize,
                (1f - Mathf.Clamp01((mouse.y - area.y) / area.height)) * MatchScenario.MapSize);

        /// <summary>
        /// Every alive entity, every frame, over a copy of the baked terrain.
        /// </summary>
        // ponytail: 16k pixel copy, a 4096-cell debris scan, one blob per entity,
        // and a 64 KB texture upload per frame, for a few dozen entities on a
        // 64x64 map. Repaint only the cells that changed, or keep static things in
        // a second texture drawn underneath, the day a profile says this costs
        // anything.
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

            if (foggedVersion != Fog.Version) Fogged();
            fogged.CopyTo(pixels, 0);
            WaterFields(world);
            Debris(world);

            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                byte show = Fog.Show(i, e);
                if (show == Fog.Hidden) continue;

                // Remembered things are dimmed the same amount the ground under
                // them is, so one square reads as one picture of one moment.
                Color32 tint = show == Fog.Memory
                    ? Color32.Lerp(Tint(world, e), UiStyle.FogUnseen, UiStyle.FogSeen.a)
                    : Tint(world, e);
                Blob(e.Position, tint, e.Kind == EntityKind.Building ? BuildingRadius : UnitRadius);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
        }

        /// <summary>
        /// 돌 골렘 부족 잔해, over the terrain copy and under the dots. A wall that
        /// appears in the middle of a match changes where an army can go, and where
        /// an army can go is the only question this square answers — debris left
        /// out of it would send a player scrubbing across a map that no longer
        /// connects the way it is drawn.
        ///
        /// Not in the backdrop, which is painted once because terrain cannot
        /// change. This one can, so it is painted onto the copy every frame.
        ///
        /// Its own tone rather than MinimapRock: over the ground so it reads as
        /// blocked, under the rock so it does not read as permanent. No countdown
        /// here — a cell is two pixels, and how long is left is the field's to say.
        /// </summary>
        private static void Debris(World world)
        {
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (world.HasRemnant(cell)) Cell(cell, UiStyle.MinimapRemnant);
            }
        }

        /// <summary>
        /// 물 슬라임 물 지대 (issue #212), under the debris and the dots: a field
        /// is why a 급류 슬라임 is suddenly fast and a 간헐천 can be aimed, and
        /// both are questions a player asks of the whole map. No fade here; how
        /// long a field has left is the field's to say, as with debris.
        /// </summary>
        private static void WaterFields(World world)
        {
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (world.HasWaterField(cell)) Cell(cell, UiStyle.MinimapWaterField);
            }
        }

        /// <summary>
        /// Fills a currently visible cell. Remembered terrain does not reveal
        /// new water fields or debris, nor their expiry outside current vision.
        /// </summary>
        private static void Cell(int cell, Color32 colour)
        {
            // Res is a whole multiple of the map, so a cell is a square block of
            // pixels rather than something to resample.
            const int block = Res / MatchScenario.MapSize;
            int cx = cell % MatchScenario.MapSize;
            int cy = cell / MatchScenario.MapSize;
            if (Fog.AtCell(cx, cy) != Fog.Seen) return;

            int x0 = cx * block;
            int y0 = cy * block;
            for (int y = y0; y < y0 + block; y++)
            {
                for (int x = x0; x < x0 + block; x++) pixels[y * Res + x] = colour;
            }
        }

        /// <summary>
        /// The map itself, under everything. Water and rock are the two things a
        /// player has to know about a place they are not looking at: where the
        /// army cannot go is what makes the empty half of the square mean anything.
        ///
        /// Flat fills, no shoreline: a cell is two pixels here and an edge band at
        /// that size is a smear rather than a shape. What carries across from the
        /// field is the order of value, so the map is the same shape twice.
        ///
        /// Open ground is lifted in value by <see cref="LevelLift"/> a height level,
        /// so a plateau is a lighter patch, and the pixel row or column a cliff
        /// runs along is darkened, so the plateau has an edge that reads at this
        /// size. Water and rock keep their tints: those two already say "blocked",
        /// and a lighter lake would say "high ground".
        /// </summary>
        private static void Backdrop(World world)
        {
            // Heights come from Field, which is shaped once per world; asking
            // again for the same world returns at once.
            Field.Build(world);

            const int map = MatchScenario.MapSize;
            const int block = Res / map;

            if (backdrop == null) backdrop = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                int cy = y * map / Res;
                for (int x = 0; x < Res; x++)
                {
                    int cx = x * map / Res;
                    int cell = cy * map + cx;
                    TileKind kind = world.TerrainAt(cell);
                    Color32 tint = Tiles.MinimapTint(kind);

                    if (kind == TileKind.Open)
                    {
                        tint = Scale(tint, 1f + Field.Level(cell) * LevelLift);

                        // The pixel on the side of the cell that faces a cliff. Both
                        // cells of the pair get theirs, so the edge is two pixels.
                        int px = x - cx * block;
                        int py = y - cy * block;
                        bool edge =
                            (px == 0 && cx > 0 && Field.Cliff(cell, cell - 1)) ||
                            (px == block - 1 && cx < map - 1 && Field.Cliff(cell, cell + 1)) ||
                            (py == 0 && cy > 0 && Field.Cliff(cell, cell - map)) ||
                            (py == block - 1 && cy < map - 1 && Field.Cliff(cell, cell + map));
                        if (edge) tint = Scale(tint, CliffShade);
                    }

                    backdrop[y * Res + x] = tint;
                }
            }
        }

        /// <summary>A colour with its value multiplied, clamped at white.</summary>
        private static Color32 Scale(Color32 c, float by) =>
            new Color32(
                (byte)Mathf.Min(255, Mathf.RoundToInt(c.r * by)),
                (byte)Mathf.Min(255, Mathf.RoundToInt(c.g * by)),
                (byte)Mathf.Min(255, Mathf.RoundToInt(c.b * by)),
                c.a);

        /// <summary>
        /// The terrain with this peer's vision applied: never-seen cells are the
        /// same nothing the field paints them, remembered cells are the terrain
        /// pushed one step down in value, and lit cells are the terrain itself.
        /// </summary>
        private static void Fogged()
        {
            if (fogged == null) fogged = new Color32[Res * Res];
            foggedVersion = Fog.Version;

            for (int y = 0; y < Res; y++)
            {
                int cy = y * MatchScenario.MapSize / Res;
                for (int x = 0; x < Res; x++)
                {
                    int i = y * Res + x;
                    byte state = Fog.AtCell(x * MatchScenario.MapSize / Res, cy);
                    fogged[i] = state == Fog.Hidden ? (Color32)UiStyle.FogUnseen
                        : state == Fog.Memory
                            ? Color32.Lerp(backdrop[i], UiStyle.FogUnseen, UiStyle.FogSeen.a)
                            : backdrop[i];
                }
            }
        }

        /// <summary>
        /// The owner's colour, the same one the world draws with, so a blue blob
        /// here is the blue army there. Size is what tells a building from a unit;
        /// hue is only ever whose it is.
        /// </summary>
        private static Color32 Tint(World world, Entity e)
        {
            // Before the Owner test below, which would otherwise hand a 꼬마돌 the
            // resource node's yellow: both are ownerless and only one of them is
            // something to mine. Telling those two apart is the whole reason this
            // dot has a colour of its own (issue #130).
            if (world.IsNeutralRock(e)) return UiStyle.MinimapNeutral;
            return e.Kind == EntityKind.ResourceNode || e.Owner < 0 ? UiStyle.Node : UiStyle.Owner(e.Owner);
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
        /// What the camera can see, as an outline. The camera is perspective and
        /// tilted, so the ground it sees is a trapezoid, wider along the far edge of
        /// the screen than along the near one, and it follows the hills. The four
        /// screen corners are put on the ground with Field.Pointer, the same march
        /// a click uses, so the outline and the cursor agree about what is where.
        ///
        /// A corner whose ray goes over the horizon never lands. It is walked out
        /// along the ray's direction on the ground to the map edge instead, which is
        /// where that side of the view runs off to. A ray pointing straight up has
        /// no such direction, and the two edges that start at it are left out.
        ///
        /// Every point is clamped into the square, because the rig lets the view
        /// sit a few cells past the map edge and a line drawn outside here would
        /// land on the selection list.
        /// </summary>
        private static void ViewRect(Rect area)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Rect screen = cam.pixelRect;
            // Around the screen, from its bottom left corner.
            Vector2[] corners =
            {
                new Vector2(screen.xMin, screen.yMin),
                new Vector2(screen.xMax, screen.yMin),
                new Vector2(screen.xMax, screen.yMax),
                new Vector2(screen.xMin, screen.yMax),
            };

            Vector2[] ground = viewGround;
            bool[] landed = viewLanded;
            for (int i = 0; i < 4; i++)
            {
                landed[i] = Field.Pointer(cam, corners[i], out ground[i]) ||
                            HorizonPoint(cam, corners[i], out ground[i]);
            }

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                if (!landed[i] || !landed[next]) continue;
                Line(new Vector2(X(area, ground[i].x), Y(area, ground[i].y)),
                     new Vector2(X(area, ground[next].x), Y(area, ground[next].y)),
                     UiStyle.MinimapView);
            }
        }

        /// <summary>
        /// Where a ray that never reaches the ground leaves the map: its horizontal
        /// direction, followed from where it starts until it runs out of square.
        /// False for a ray with no horizontal direction at all.
        /// </summary>
        private static bool HorizonPoint(Camera cam, Vector2 screenPoint, out Vector2 plane)
        {
            plane = Vector2.zero;
            Ray ray = cam.ScreenPointToRay(screenPoint);

            Vector2 direction = Field.Plane(ray.direction);
            if (direction.sqrMagnitude < 1e-8f) return false;
            direction.Normalize();

            const float edge = MatchScenario.MapSize;
            Vector2 start = Field.Plane(ray.origin);
            start.x = Mathf.Clamp(start.x, 0f, edge);
            start.y = Mathf.Clamp(start.y, 0f, edge);

            // Distance along the direction to the wall it is heading for, per axis;
            // the nearer wall is the one it leaves through.
            float reach = float.MaxValue;
            if (direction.x > 1e-6f) reach = Mathf.Min(reach, (edge - start.x) / direction.x);
            else if (direction.x < -1e-6f) reach = Mathf.Min(reach, -start.x / direction.x);
            if (direction.y > 1e-6f) reach = Mathf.Min(reach, (edge - start.y) / direction.y);
            else if (direction.y < -1e-6f) reach = Mathf.Min(reach, -start.y / direction.y);

            plane = start + direction * reach;
            return true;
        }

        /// <summary>
        /// A hairline between two points on the GUI. IMGUI has no line primitive,
        /// so it is a one pixel rectangle rotated about its start, with the matrix
        /// put back after so nothing drawn next is turned.
        /// </summary>
        private static void Line(Vector2 from, Vector2 to, Color color)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f) return;

            Matrix4x4 was = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            UiStyle.Fill(new Rect(from.x, from.y - UiStyle.Hairline * 0.5f, length, UiStyle.Hairline), color);
            GUI.matrix = was;
        }

        /// <summary>World x to a pixel column of the square. Clamped, so nothing draws outside it.</summary>
        private static float X(Rect area, float worldX) =>
            area.x + Mathf.Clamp01(worldX / MatchScenario.MapSize) * area.width;

        /// <summary>World y to a pixel row. GUI y grows downward and world y grows up, hence the flip.</summary>
        private static float Y(Rect area, float worldY) =>
            area.y + (1f - Mathf.Clamp01(worldY / MatchScenario.MapSize)) * area.height;
    }
}
