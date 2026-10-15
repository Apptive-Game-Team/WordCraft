using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// The colours bodies on the field are built from, per faction, and the one
    /// rule for whose a body is.
    ///
    /// .art/STYLE.md keeps two axes apart: the rendering technique never varies,
    /// and shape language plus palette always does. Paper.cginc is the first
    /// axis; this file is half of the second, and Models.cs is the other half.
    /// Every value below that STYLE.md names is copied from it verbatim. The rest
    /// are marked "chosen" — STYLE.md has no swatch for that material — and kept
    /// restrained and mid-valued so they sit inside the faction's own range
    /// rather than beside it.
    ///
    /// Owner is not a palette. A faction palette that changed with the player
    /// would stop saying which faction a body is, so the owner gets a small
    /// accent of <see cref="UiStyle.Owner"/> on every body instead — a collar,
    /// a belt, a band, a banner — in roughly a tenth of the surface. That is
    /// enough to read across the map and too little to repaint the faction.
    /// </summary>
    public static class BodyPalette
    {
        /// <summary>STYLE.md's eye grammar: a dark oval and one tiny highlight. Chosen: warm near-black, not pure black.</summary>
        public static readonly Color Eye = Hex(0x2B2620);
        public static readonly Color EyeShine = Hex(0xF7F4EC);

        /// <summary>Whether a body carries an owner accent at all. Neutrals belong to nobody and say so by having none.</summary>
        public static bool HasAccent(int owner, bool neutral) => !neutral && owner >= 0;

        public static Color Accent(int owner) => UiStyle.Owner(owner);

        /// <summary>The faction's dominant colour, for the generic body a roster hole falls back to.</summary>
        public static Color Main(Faction faction)
        {
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpirits.Leaf;
                case Faction.Hellfire: return Hellfire.Shell;
                case Faction.WaterSlimes: return WaterSlimes.Pale;
                case Faction.RockGolems: return RockGolems.Tan;
                case Faction.Driftworlds: return Driftworlds.Shell;
                default: return Humans.Stone;
            }
        }

        /// <summary>The faction's second colour, for the same fallback.</summary>
        public static Color Second(Faction faction)
        {
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpirits.Bark;
                case Faction.Hellfire: return Hellfire.Charcoal;
                case Faction.WaterSlimes: return WaterSlimes.Blue;
                case Faction.RockGolems: return RockGolems.Moss;
                case Faction.Driftworlds: return Driftworlds.Teal;
                default: return Humans.Steel;
            }
        }

        /// <summary>
        /// What a hit washes a body toward for a moment: the eye highlight's paper
        /// white, so the flash is the lightest value already on the field rather
        /// than a new one, and reads as light rather than as a colour.
        /// </summary>
        public static Color HitFlash => EyeShine;

        /// <summary>
        /// The colour of what a faction's weapon throws: a tracer, and the shards
        /// of its impact. One per faction, taken from the faction's own swatches,
        /// and each the lightest mid value that faction has with a hue in it, so a
        /// shot reads on grass and on stone without leaving the palette.
        /// </summary>
        public static Color Shot(Faction faction)
        {
            switch (faction)
            {
                case Faction.TreeSpirits: return TreeSpirits.Bolt;
                case Faction.Hellfire: return Hellfire.Ember;
                case Faction.WaterSlimes: return WaterSlimes.Blue;
                case Faction.RockGolems: return RockGolems.Tan;
                case Faction.Driftworlds: return Driftworlds.TealLight;
                default: return Humans.Mana;
            }
        }

        /// <summary>세계수 정령. Round, leafy, weightless; lightning, nature and wind.</summary>
        public static class TreeSpirits
        {
            public static readonly Color LeafLight = Hex(0xB8CE59);
            public static readonly Color Leaf = Hex(0x9DA74D);
            public static readonly Color LeafShade = Hex(0x768738);
            public static readonly Color LeafDeep = Hex(0x384823);
            /// <summary>Chosen. FACTIONS.md asks for "젖은 목재 갈색" and STYLE.md gives no swatch.</summary>
            public static readonly Color Bark = Hex(0x7A5A3A);
            /// <summary>Chosen, the shade of <see cref="Bark"/>.</summary>
            public static readonly Color BarkShade = Hex(0x553E28);
            public static readonly Color BoltLight = Hex(0xFCFBD5);
            public static readonly Color Bolt = Hex(0xF6DB5F);
            public static readonly Color BoltDeep = Hex(0xD7A313);
            public static readonly Color WindLight = Hex(0xE5EBE7);
            public static readonly Color Wind = Hex(0xB3CCCA);
            public static readonly Color WindDeep = Hex(0x5EA191);
            /// <summary>Chosen. FACTIONS.md: the 뇌우목 arc is "창백한 청백색".</summary>
            public static readonly Color Arc = Hex(0xDCEBF5);
        }

        /// <summary>
        /// 지옥불 군단. Mass first, flame second: STYLE.md reads the anchor as a
        /// dark brown-black shell nearly everywhere, red only on upper-left facets,
        /// and real glow only in eye slits and one chest core. Models.cs spends
        /// <see cref="Ember"/> in exactly those places and nowhere else.
        /// </summary>
        public static class Hellfire
        {
            public static readonly Color Ember = Hex(0xE65E08);
            public static readonly Color Facet = Hex(0x9B3014);
            public static readonly Color Shell = Hex(0x5A291E);
            public static readonly Color Charcoal = Hex(0x2A1512);
            public static readonly Color Ash = Hex(0x6D4A39);
        }

        /// <summary>물 슬라임. Pale and gel-like; kept opaque, the translucency is implied by the light inner highlight.</summary>
        public static class WaterSlimes
        {
            public static readonly Color Foam = Hex(0xE8F7EE);
            public static readonly Color Pale = Hex(0x98DEEB);
            public static readonly Color Sky = Hex(0x6FCCF4);
            public static readonly Color Blue = Hex(0x53B2EB);
            public static readonly Color Deep = Hex(0x2D92E4);
            /// <summary>Chosen. 구름 용's cloud body; STYLE.md lists the slime blues only.</summary>
            public static readonly Color Cloud = Hex(0xF1F4F5);
        }

        /// <summary>돌 골렘 부족. Warm tan stone and moss — warm, so it never collides with human grey.</summary>
        public static class RockGolems
        {
            public static readonly Color Tan = Hex(0xCDC8B8);
            public static readonly Color Stone = Hex(0xA3A29D);
            public static readonly Color StoneShade = Hex(0x8A857A);
            public static readonly Color Moss = Hex(0x55572D);
            public static readonly Color MossDeep = Hex(0x374725);
            /// <summary>Chosen. FACTIONS.md: 각성 바위 has "균열 사이로 따뜻한 빛".</summary>
            public static readonly Color Waking = Hex(0xE2A84B);
            /// <summary>Chosen. 바위 술사's staff crystal, sampled by eye from RockMage.png.</summary>
            public static readonly Color Rune = Hex(0x6FC9C0);
        }

        /// <summary>
        /// 차원 유랑종. A dull warm-brown shell and a vivid interior seen through
        /// gaps; the interior is the only saturated colour on any of them.
        /// </summary>
        public static class Driftworlds
        {
            public static readonly Color Shell = Hex(0x6E5F4C);
            public static readonly Color ShellShade = Hex(0x4C4136);
            public static readonly Color ShellDeep = Hex(0x2B2520);
            public static readonly Color Teal = Hex(0x2F7C86);
            public static readonly Color TealLight = Hex(0x63BAC4);
            public static readonly Color Dusk = Hex(0x5F5A78);
            public static readonly Color Lilac = Hex(0xA29CC4);
            /// <summary>Chosen. 경계 운반자's belly, a light lilac so the toad's underside separates from its back.</summary>
            public static readonly Color Belly = Hex(0xD3CBE3);
            /// <summary>Chosen. The fire world seen through 화산편's shell, and the extinct fire slime.</summary>
            public static readonly Color FireWorld = Hex(0xE07A3A);
            /// <summary>Chosen. The extinct lightning slime, from ExtinctLightningSlime.png.</summary>
            public static readonly Color StormWorld = Hex(0xEFCB45);
            /// <summary>Chosen. The extinct rock slime, from ExtinctRockSlime.png.</summary>
            public static readonly Color StoneWorld = Hex(0xD6CBAE);
        }

        /// <summary>
        /// 인간 마법 문명. Cool grey stone, steel blue, bronze and wood; the only
        /// faction allowed steel blue and grey masonry.
        /// </summary>
        public static class Humans
        {
            public static readonly Color Stone = Hex(0xC4CCC8);
            public static readonly Color StoneMid = Hex(0xA09C92);
            public static readonly Color StoneDark = Hex(0x616662);
            public static readonly Color Steel = Hex(0x6191A3);
            /// <summary>Chosen, the shade of <see cref="Steel"/>.</summary>
            public static readonly Color SteelDark = Hex(0x456F80);
            public static readonly Color Wood = Hex(0x5D4032);
            /// <summary>Chosen. STYLE.md names "bronze/wood" as one swatch; the metal needs its own lighter value to read as metal.</summary>
            public static readonly Color Bronze = Hex(0xA8834A);
            /// <summary>Chosen. Mana: water in the 마나 샘, the orb, a lens. Pale so it stays restrained.</summary>
            public static readonly Color Mana = Hex(0x9FDCF2);
            /// <summary>Chosen. The inside of a hood: STYLE.md says humans have no face, so the opening is simply dark.</summary>
            public static readonly Color Hood = Hex(0x2E3236);
            /// <summary>Chosen. The paraglider's canvas.</summary>
            public static readonly Color Canvas = Hex(0xE6E1D3);
        }

        /// <summary>The resource node's crystals, in the HUD's node hue so a node on the field and on the minimap agree.</summary>
        public static Color Node => UiStyle.Node;

        /// <summary>Chosen. The dark rock a node's crystals grow out of.</summary>
        public static readonly Color NodeRock = Hex(0x55525E);

        /// <summary>Multiplies a colour's value, keeping its hue: the shade of a part without a swatch of its own.</summary>
        public static Color Darker(Color color, float factor) =>
            new Color(color.r * factor, color.g * factor, color.b * factor, color.a);

        /// <summary>Moves a colour toward white: the lit facet of a part without a swatch of its own.</summary>
        public static Color Lighter(Color color, float amount) => Color.Lerp(color, Color.white, amount);

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
