using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Every colour the lit field is made of, in one place, each with the reason
    /// it is that colour. FieldMesh, FieldProps and Sky ask here and write no
    /// colour literal of their own, for the same reason UiStyle is the only file
    /// the HUD may write one in: a repaint is one diff, not a search.
    ///
    /// Why these are not the Ground, Water and Rock tokens UiStyle used to carry: those were
    /// chosen for an unlit 2D bake, where the colour in the texture was the colour
    /// on screen, and they sit at the dark end on purpose so the HUD rule "walkable
    /// ground is the darkest thing on the map" held without any light at all. A
    /// lit mesh is multiplied by the light and lifted by the hemisphere ambient in
    /// Paper.cginc, and those tones pushed through that come out muddy, which is
    /// the one thing .art/STYLE.md names as a reject alongside neon. So the field
    /// gets its own base colours here, picked to land mid-to-high in value with
    /// medium saturation once lit, and the HUD chrome (0x17171C) stays well below
    /// all of them, which keeps the field the brightest thing on screen.
    ///
    /// The arithmetic every value below was picked against: a flat top in the
    /// base band comes out at roughly albedo × (sun + sky light) ≈ albedo × 1.3,
    /// a face in the shade band at roughly albedo × 0.85, and one in the
    /// highlight band at roughly albedo × 1.5. So no albedo here goes much past
    /// 0.65 in any channel, or a sunlit ramp would blow out to paper white.
    ///
    /// The project is in gamma colour space (ProjectSettings m_ActiveColorSpace 0),
    /// so a vertex colour reaches the shader as written. A switch to linear would
    /// need every colour here passed through Color.linear before it goes in a mesh.
    /// </summary>
    public static class FieldPalette
    {
        private static Color Rgb(int hex) => new Color(
            ((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        private static Color Rgba(int hex, float alpha)
        {
            Color c = Rgb(hex);
            c.a = alpha;
            return c;
        }

        // ---------------------------------------------------------------- light

        /// <summary>
        /// The sky behind the diorama, and what distance fades toward. A muted
        /// blue grey a step below lit grass in value: light enough that the board
        /// reads as standing in air rather than in a void, dark enough that the
        /// field is still the brightest thing on screen and the HUD panels still
        /// sit far below both.
        /// </summary>
        public static readonly Color Backdrop = Rgb(0x7C8B97);

        /// <summary>
        /// Unity's own flat ambient. Kept near black because Paper.cginc adds the
        /// real ambient as a sky and earth hemisphere, and a second full ambient on
        /// top would flatten the three value bands into one.
        /// </summary>
        public static readonly Color Ambient = Rgb(0x0E0F12);

        /// <summary>The sun. Slightly warm so the shade band, which the cool sky fills, reads as shade and not as dirt.</summary>
        public static readonly Color Sun = Rgb(0xFFF1DA);

        /// <summary>Hemisphere ambient from above. Cool, the colour of open sky, so faces turned up and away from the sun still read as outdoors.</summary>
        public static readonly Color SkyLight = Rgb(0x4C525C);

        /// <summary>Hemisphere ambient from below. Warm and dim, the grass and earth bouncing back into the undersides.</summary>
        public static readonly Color EarthLight = Rgb(0x2C2722);

        /// <summary>The lit paper edge. Pale and warm like the sun it belongs to; alpha is its strength.</summary>
        public static readonly Color Rim = Rgba(0xFFEACB, 0.35f);

        // ---------------------------------------------------------------- open ground

        /// <summary>Grass at level 0. Medium saturation sage, so units in their owner hues stay the loudest thing on it.</summary>
        public static readonly Color GrassLow = Rgb(0x6A7C52);

        /// <summary>
        /// Grass at the top level. A touch lighter and warmer than the valley, so
        /// height reads from colour as well as from shade even where the light
        /// happens to hit two levels the same way.
        /// </summary>
        public static readonly Color GrassHigh = Rgb(0x899264);

        /// <summary>
        /// What a ramp leans toward. Dry, warmer earth, so a slope cell is a
        /// different band from the flat around it and a player sees the ramp as a
        /// ramp before the light has said anything.
        /// </summary>
        public static readonly Color SlopeTint = Rgb(0x8E7D58);

        /// <summary>How far a slope cell moves from its level's grass toward <see cref="SlopeTint"/>.</summary>
        public const float SlopeShift = 0.3f;

        /// <summary>
        /// Per cell value grain, as a fraction either way. Far below the 2D bake's
        /// grain: the light now separates facets on its own, and grain on top of
        /// that is the fine detail noise the style rules out.
        /// </summary>
        public const float Grain = 0.010f;

        // ---------------------------------------------------------------- cliffs

        /// <summary>A cliff's lip. Warm exposed earth, so a wall separates from the grass above it by hue as well as by band.</summary>
        public static readonly Color CliffTop = Rgb(0x8C7A5E);

        /// <summary>A cliff's foot. Darker, so the eye reads depth down the face without any line drawn at the bottom.</summary>
        public static readonly Color CliffFoot = Rgb(0x5A4D40);

        /// <summary>The foot of a shore wall, at the lake bed. Wet earth: darker and cooler than a dry cliff foot.</summary>
        public static readonly Color ShoreFoot = Rgb(0x433D34);

        // ---------------------------------------------------------------- rock

        /// <summary>
        /// The ridge's top. Cool violet grey, the lifted cousin of the old UiStyle.Rock (#454250), so
        /// the mountain keeps the hue that already says "blocked" in the HUD and on
        /// the minimap.
        /// </summary>
        public static readonly Color RockTop = Rgb(0x8E8996);

        /// <summary>The ridge's walls, a step down from its top so the top catches the light first.</summary>
        public static readonly Color RockFace = Rgb(0x777281);

        /// <summary>The ridge's foot, where it meets the ground.</summary>
        public static readonly Color RockFoot = Rgb(0x55515E);

        // ---------------------------------------------------------------- water

        /// <summary>The lake bed. Dark wet earth, seen through the water, which is what makes a lake a hollow.</summary>
        public static readonly Color Bed = Rgb(0x48433A);

        /// <summary>Open water between the shore band and the middle.</summary>
        public static readonly Color Water = Rgb(0x4A7894);

        /// <summary>The middle of a lake: every cell around it is water. Gives a lake depth, as WaterDeep did in the 2D bake.</summary>
        public static readonly Color WaterDeep = Rgb(0x3A6482);

        /// <summary>The band where water meets anything else. Pale, and going all the way round because foam has no light direction.</summary>
        public static readonly Color Foam = Rgb(0xA6C4D0);

        // ---------------------------------------------------------------- map rim

        /// <summary>The plinth the board stands on. Dark earth, so the cut edge of the diorama reads as a side and not as more ground.</summary>
        public static readonly Color Plinth = Rgb(0x51463A);

        /// <summary>The plinth's foot, darker again, so the board seems to stand on something rather than hang.</summary>
        public static readonly Color PlinthFoot = Rgb(0x342D26);

        /// <summary>The thin base slab's lip. A shade off the plinth so the step between them is a step and not a line.</summary>
        public static readonly Color SlabTop = Rgb(0x463D33);

        /// <summary>The slab's sides.</summary>
        public static readonly Color SlabSide = Rgb(0x2C2620);

        // ---------------------------------------------------------------- props

        /// <summary>A grass tuft. Deeper and a little more saturated than the ground under it, so it reads as a clump and not as a stain.</summary>
        public static readonly Color Tuft = Rgb(0x5A7743);

        /// <summary>A tuft's tips, lighter, so a blade catches the light at its end.</summary>
        public static readonly Color TuftTip = Rgb(0x7C9258);

        /// <summary>A tree's lowest tier. Muted pine green, darker than the grass so a tree separates from the ground it stands on.</summary>
        public static readonly Color Foliage = Rgb(0x4C6845);

        /// <summary>A tree's top tier. Lighter, the upper-left highlight the style puts on every form.</summary>
        public static readonly Color FoliageTop = Rgb(0x627E55);

        /// <summary>A trunk. Brown bark, barely seen under the lowest tier.</summary>
        public static readonly Color Trunk = Rgb(0x6A5442);

        /// <summary>A loose stone's sides. The ridge's palette, so a pebble is visibly a piece of the mountain.</summary>
        public static readonly Color Pebble = Rgb(0x7D7887);

        /// <summary>A loose stone's top.</summary>
        public static readonly Color PebbleTop = Rgb(0x96919E);

        // ---------------------------------------------------------------- rules

        /// <summary>
        /// Grass for a height level: valley to summit along one ramp. Divided by
        /// the simulation's own ceiling so a map that only uses the lower levels
        /// is still mostly valley green.
        /// </summary>
        public static Color Grass(int level)
        {
            float t = Mathf.Clamp01(level / (float)Mathf.Max(1, World.MaxHeight));
            return Color.Lerp(GrassLow, GrassHigh, t);
        }

        /// <summary>The same level's grass on a ramp cell.</summary>
        public static Color Slope(int level) => Color.Lerp(Grass(level), SlopeTint, SlopeShift);

        /// <summary>
        /// A colour moved by a hair of grain. unit is a 0..1 hash draw for the
        /// cell; the result is the colour scaled up or down by at most
        /// <see cref="Grain"/>, alpha untouched.
        /// </summary>
        public static Color Grained(Color colour, float unit)
        {
            float scale = 1f + (unit * 2f - 1f) * Grain;
            return new Color(colour.r * scale, colour.g * scale, colour.b * scale, colour.a);
        }
    }
}
