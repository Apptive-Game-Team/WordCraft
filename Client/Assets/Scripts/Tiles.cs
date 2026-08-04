using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The map's terrain, baked to one texture and drawn as one sprite.
    ///
    /// Terrain is decided when the map is built and never changes, so this runs
    /// once a match. There is no per-frame cost at all: the field ends up with
    /// the same single ground quad it already had, wearing a picture instead of
    /// a flat colour. That is the whole reason it is a texture and not a mesh, a
    /// sprite per cell, or a tilemap package — 4096 cells redrawn every frame is
    /// a bill this layer never needs to pay.
    ///
    /// What it draws is flat colour and one rule about edges. A grid of flat
    /// squares reads as a spreadsheet, so where a cell meets a different kind
    /// the boundary gets a band: foam inside the water, a lit rim on the rock's
    /// two lit faces, and the rock's shadow on the ground it falls across.
    /// Fills say what a cell is; edges are what make the shapes read as terrain.
    ///
    /// Reads the world and writes nothing to it, like the rest of the view.
    /// </summary>
    public static class Tiles
    {
        // Sides, in the order the texel loop applies them. Up last, so a rock
        // shadow wins the corner texels it shares with a shoreline.
        private const int Left = 0;
        private const int Right = 1;
        private const int Down = 2;
        private const int Up = 3;

        private static readonly int[] StepX = { -1, 1, 0, 0 };
        private static readonly int[] StepY = { 0, 0, -1, 1 };

        /// <summary>The world this texture was baked from. A new one means a new map.</summary>
        private static World baked;

        private static Texture2D texture;
        private static Sprite sprite;

        /// <summary>
        /// The whole map as one sprite, one world unit to one cell, pivoted in
        /// the middle. Cached: the second call for the same world is a field read.
        /// </summary>
        public static Sprite Field(World world)
        {
            if (ReferenceEquals(world, baked) && sprite != null) return sprite;
            baked = world;
            sprite = Bake(world);
            return sprite;
        }

        /// <summary>
        /// The same three kinds at minimap scale, where a cell is two pixels and
        /// an edge band would be a smear. Flat fills only, in the same order of
        /// value as the field, so the shape of the map is the same shape twice.
        /// </summary>
        public static Color32 MinimapTint(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Water: return UiStyle.MinimapWater;
                case TileKind.Rock: return UiStyle.MinimapRock;
                default: return UiStyle.MinimapGround;
            }
        }

        private static Sprite Bake(World world)
        {
            const int cells = MatchScenario.MapSize;
            int texels = UiStyle.TileTexels;
            int res = cells * texels;

            var pixels = new Color32[res * res];
            var band = new int[4];
            var tone = new Color[4];

            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    TileKind kind = At(world, cx, cy);
                    Color body = Body(world, kind, cx, cy);

                    for (int s = 0; s < 4; s++)
                    {
                        band[s] = 0;
                        TileKind other = At(world, cx + StepX[s], cy + StepY[s]);
                        if (other == kind) continue;

                        // Light comes from the top left, so a slab is lit on its
                        // top and left faces and throws its shadow down and to the
                        // right, onto the neighbour rather than onto itself. Ringing
                        // rock on all four sides would be an outline, and .art/STYLE.md
                        // separates shapes by value instead of drawing outlines.
                        if (other == TileKind.Rock)
                        {
                            if (s != Up && s != Left) continue;
                            tone[s] = UiStyle.RockShade;
                        }
                        else if (kind == TileKind.Rock)
                        {
                            if (s != Up && s != Left) continue;
                            tone[s] = UiStyle.RockLit;
                        }
                        else if (kind == TileKind.Water)
                        {
                            // Foam has no light direction, so it goes all the way round.
                            tone[s] = UiStyle.WaterShore;
                        }
                        else
                        {
                            continue; // open ground beside water: the rim is the water's
                        }

                        // A band a texel deeper on some cells than others. A shoreline
                        // of one constant thickness is a rectangle with a stripe on it.
                        band[s] = UiStyle.TileBand + (Hash(cx + s * 8191, cy - s * 4093) & 1);
                    }

                    int originX = cx * texels;
                    int originY = cy * texels;
                    for (int ty = 0; ty < texels; ty++)
                    {
                        for (int tx = 0; tx < texels; tx++)
                        {
                            Color c = body;
                            if (tx < band[Left]) c = tone[Left];
                            if (texels - 1 - tx < band[Right]) c = tone[Right];
                            if (ty < band[Down]) c = tone[Down];
                            if (texels - 1 - ty < band[Up]) c = tone[Up];
                            // Texture row 0 is the bottom one, and the sprite is drawn
                            // unflipped, so world +y is up on screen with no flip anywhere.
                            pixels[(originY + ty) * res + originX + tx] = c;
                        }
                    }
                }
            }

            // A restart bakes a new map, and the old texture is not garbage the GC
            // may collect on its own.
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false)
            {
                // Point, because the style is cut paper: a shoreline magnified ten
                // times is a hard chunky edge, and bilinear would turn every band
                // into the one thing the HUD standard bans, a gradient.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);

            // Pixels per unit is the cell size, so one sprite unit is one grid cell
            // and the sprite lands on the map at scale one.
            return Sprite.Create(texture, new Rect(0f, 0f, res, res), new Vector2(0.5f, 0.5f), texels);
        }

        /// <summary>
        /// A cell's fill. Deep water where a lake has a middle, and every other
        /// cell lifted a hair, so a lake is water rather than a rectangle of paint.
        /// </summary>
        private static Color Body(World world, TileKind kind, int cx, int cy)
        {
            Color c;
            switch (kind)
            {
                case TileKind.Water:
                    c = Enclosed(world, cx, cy) ? UiStyle.WaterDeep : UiStyle.Water;
                    break;
                case TileKind.Rock:
                    c = UiStyle.Rock;
                    break;
                default:
                    c = UiStyle.Ground;
                    break;
            }
            return (Hash(cx, cy) & 1) == 0 ? c : Color.Lerp(c, UiStyle.Ink, UiStyle.TileGrain);
        }

        private static bool Enclosed(World world, int cx, int cy)
        {
            for (int s = 0; s < 4; s++)
            {
                if (At(world, cx + StepX[s], cy + StepY[s]) != TileKind.Water) return false;
            }
            return true;
        }

        /// <summary>
        /// The kind at a cell, clamped at the map edge. Clamping makes a border
        /// cell its own neighbour, so the map's outer rim draws no band: there is
        /// nothing on the far side of it for an edge to be an edge against.
        /// </summary>
        private static TileKind At(World world, int x, int y)
        {
            const int last = MatchScenario.MapSize - 1;
            if (x < 0) x = 0; else if (x > last) x = last;
            if (y < 0) y = 0; else if (y > last) y = last;
            return world.TerrainAt(y * MatchScenario.MapSize + x);
        }

        /// <summary>
        /// A cell hash, for grain and for edge jitter. Not World.Random: this is
        /// view noise, the draw count it would spend is simulation state, and
        /// spending one here would desync every peer that drew the map.
        /// </summary>
        private static int Hash(int x, int y)
        {
            int h = (x * 73856093) ^ (y * 19349663);
            h ^= h >> 13;
            return h & 0x7FFFFFFF;
        }
    }
}
