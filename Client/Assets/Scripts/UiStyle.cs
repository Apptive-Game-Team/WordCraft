using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// The client's design system: every colour, every spacing step, every type
    /// size, and the drawing of every component the HUD is made of. The written
    /// standard is docs/UI-STYLE.md; this file is that document compiled.
    ///
    /// It is the only file in the client allowed to write a colour literal or a
    /// pixel number. Everywhere else asks here, which is what makes a restyle one
    /// diff instead of a search.
    ///
    /// Four colour groups, kept strictly apart because they answer different
    /// questions and mixing them makes a HUD whose meaning changes with the
    /// player's faction pick:
    ///
    ///   chrome    the HUD's own furniture. Neutral, dark, low saturation, so the
    ///             field art stays the brightest thing on screen.
    ///   semantic  good / warning / danger. State, never identity, never accent.
    ///   owner     whose a thing is. The only place a per-player hue enters the UI.
    ///   field     what MatchView paints into the world.
    ///
    /// IMGUI on purpose. UI Toolkit needs a PanelSettings and a theme stylesheet,
    /// both serialised assets, and the client boots from an empty scene so that a
    /// gameplay change never has to touch one. A design system is tokens and
    /// consistency; it is not a widget toolkit.
    /// </summary>
    public static class UiStyle
    {
        // ---------------------------------------------------------------- colour

        private static Color Rgb(int hex) => new Color(
            ((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        private static Color Rgba(int hex, float alpha)
        {
            Color c = Rgb(hex);
            c.a = alpha;
            return c;
        }

        // ---- chrome ----

        /// <summary>Deeper than any panel. Holes: an empty card cell, a keycap well.</summary>
        public static readonly Color Void = Rgb(0x0C0C10);

        /// <summary>Panels that sit over the live map, so nearly opaque.</summary>
        public static readonly Color Panel = Rgba(0x17171C, 0.94f);

        /// <summary>The two match bars. Thinner, so the map still shows through the edge.</summary>
        public static readonly Color Bar = Rgba(0x17171C, 0.90f);

        public static readonly Color Raised = Rgb(0x22222A);
        public static readonly Color Lifted = Rgb(0x2E2E39);

        /// <summary>Hairlines. Separation is a one-pixel line, never a box outline.</summary>
        public static readonly Color Edge = Rgb(0x3A3A46);

        public static readonly Color Ink = Rgb(0xE7E3DA);
        public static readonly Color InkDim = Rgb(0x9A958B);
        public static readonly Color InkMute = Rgb(0x5F5B55);

        /// <summary>
        /// The one accent, and it is a material rather than a signal: cut paper.
        /// No faction owns bone white and it means nothing on its own, so an armed
        /// control cannot be misread as a warning.
        /// </summary>
        public static readonly Color Accent = Rgb(0xE0D3B4);

        /// <summary>Text on the accent. Armed inverts; that is the whole armed state.</summary>
        public static readonly Color OnAccent = Rgb(0x16151A);

        // ---- semantic ----

        public static readonly Color Good = Rgb(0x63B37A);
        public static readonly Color Warn = Rgb(0xD9A93C);
        public static readonly Color Danger = Rgb(0xD2544A);

        /// <summary>A halted match's panel. Danger as a surface, the only one there is.</summary>
        public static readonly Color HaltPanel = Rgba(0x3B1113, 0.96f);

        /// <summary>Health and progress: one ramp, so a bar means the same thing anywhere.</summary>
        public static Color Health(float ratio) => ratio > 0.5f ? Good : ratio > 0.25f ? Warn : Danger;

        // ---- owner ----

        /// <summary>
        /// Whose a thing is, and nothing else. Not a faction palette: the roster
        /// palettes belong to the art on the field, and a HUD that borrowed them
        /// would change meaning every time the player picked a different faction.
        /// Two hues far apart in both hue and value, so they survive a glance on a
        /// minimap four pixels wide.
        /// </summary>
        private static readonly Color[] owners =
        {
            Rgb(0x599EFF),
            Rgb(0xFF7352),
        };

        public static int OwnerCount => owners.Length;

        public static Color Owner(int peer) =>
            peer >= 0 && peer < owners.Length ? owners[peer] : InkDim;

        // ---- field ----

        public static readonly Color Sky = Rgb(0x0D0D10);
        public static readonly Color Ground = Rgb(0x1C1E24);
        public static readonly Color Node = Rgb(0xFFD140);

        // ---- field: terrain ----

        // One rule carries the whole layer: walkable ground is the darkest thing
        // on the map and anything raised out of it is blocked. Value alone answers
        // "can I walk there", so the answer survives a greyscale screenshot, a
        // colour-blind player and the map drawn at eleven pixels a cell.
        //
        //   Ground  0x1C1E24   open
        //   Water   0x1B3050   impassable
        //   Rock    0x454250   impassable
        //
        // Hue then separates the two blocked kinds from each other, and the edge
        // tones below separate them from the ground they touch.

        public static readonly Color Water = Rgb(0x1B3050);

        /// <summary>A water cell with water on all four sides. Gives a lake a middle.</summary>
        public static readonly Color WaterDeep = Rgb(0x142440);

        /// <summary>The foam rim, inside the water, on every side that meets something else.</summary>
        public static readonly Color WaterShore = Rgb(0x3E7099);

        public static readonly Color Rock = Rgb(0x454250);

        /// <summary>The lit rim of the slab, on its top and left faces. Light comes from the top left.</summary>
        public static readonly Color RockLit = Rgb(0x625E6E);

        /// <summary>The shadow the slab throws, painted on the ground below and to its right.</summary>
        public static readonly Color RockShade = Rgb(0x0E0F13);

        /// <summary>The minimap's two blocked kinds. Same order of value as the field's, lifted to sit on MinimapGround.</summary>
        public static readonly Color MinimapWater = Rgb(0x122031);
        public static readonly Color MinimapRock = Rgb(0x4A4854);

        // ---- field: 돌 골렘 부족 잔해 ----
        //
        // Debris is terrain with a clock on it, so it answers to the terrain rule
        // above — raised out of the ground means blocked — and not to the roster
        // rule, which draws authored art untinted so a palette identifies a faction.

        /// <summary>
        /// What the debris art is multiplied by. The imported sprite is warm tan
        /// and untinted it reads as a body lying on the floor; through this its
        /// midtone lands on Rock and its lit faces on RockLit, which is what the
        /// tile layer already means by blocked. Chosen by measuring the sprite, not
        /// by eye: median luminance 67.3 against Rock's 67.6, top decile 94.1
        /// against RockLit's 96.0.
        ///
        /// It stays a hair warmer than the slab it borrows its value from, which is
        /// the terrain layer's second axis doing its usual job — value says blocked,
        /// hue says which blocked thing this is.
        /// </summary>
        public static readonly Color Remnant = Rgb(0x928BAA);

        /// <summary>
        /// What debris has faded to on the tick before it lapses. Never zero: the
        /// cell is blocked right up to the last tick, and debris a player cannot see
        /// is the bug this whole layer exists to remove. At this alpha the midtone
        /// still composites to 48.7 over Ground's 30.0, so the faded pile is a wall
        /// about to go rather than a wall that has gone.
        /// </summary>
        public const float RemnantFade = 0.5f;

        /// <summary>Debris on the minimap. Over the ground so it blocks, under the rock so it is not permanent.</summary>
        public static readonly Color MinimapRemnant = Rgb(0x3A3844);

        /// <summary>Texels a cell is baked at. One cell is a flat colour plus an edge, not a picture.</summary>
        public const int TileTexels = 8;

        /// <summary>How deep an edge band cuts into its cell, in texels. A quarter of a cell.</summary>
        public const int TileBand = 2;

        /// <summary>How much deeper or shallower that band runs cell to cell. A rim of one thickness is a stripe.</summary>
        public const int TileJitter = 1;

        /// <summary>
        /// How far every other blocked cell is lifted, so a lake is not one
        /// rectangle of paint. Open ground gets none: it is most of the map, and a
        /// grain there is four thousand visible squares.
        /// </summary>
        public const float TileGrain = 0.06f;

        // ---- field: fog ----

        // Two tones and no third: the lit state is the absence of both. Unexplored
        // ground wears the same value as the sky beyond the map edge, so the part
        // of the map this peer has never walked into reads as nothing rather than
        // as a place painted dark.

        /// <summary>Never seen. Opaque, and the same value as <see cref="Sky"/>.</summary>
        public static readonly Color FogUnseen = Rgb(0x0D0D10);

        /// <summary>
        /// Seen before, not now. The same tone at part alpha, so remembered ground
        /// is the lit ground pushed one step down in value — the separation rule
        /// the whole client uses, applied to time instead of to shape.
        /// </summary>
        public static readonly Color FogSeen = Rgba(0x0D0D10, 0.55f);

        /// <summary>
        /// Ticks between vision passes. A unit covers a quarter of a cell a tick,
        /// so the fog edge is at most one cell behind the army that owns it, and
        /// four fifths of the frames pay nothing at all.
        /// </summary>
        public const int FogTicks = 4;

        public static readonly Color Ring = Rgba(0xFFFFFF, 0.75f);
        public static readonly Color HoverRing = Rgba(0xFFFFFF, 0.28f);

        /// <summary>
        /// A selected, armed entity's weapon reach (issue #104). A third circle
        /// can land on the same unit as Ring and HoverRing, so it sits at its
        /// own alpha between the two rather than reusing either — dimmer than
        /// the selection outline, brighter than a hover, so three rings on one
        /// body still read as three different things instead of a smear.
        /// </summary>
        public static readonly Color RangeRing = Rgba(0xFFFFFF, 0.45f);
        public static readonly Color MeterBack = Rgba(0x0A0A0D, 0.85f);
        public static readonly Color Queue = Rgba(0x73D9FF, 0.95f);
        public static readonly Color Rally = Rgba(0x8CFF99, 0.85f);
        public static readonly Color GhostOk = Rgba(0x73FF8C, 0.55f);
        public static readonly Color GhostBad = Rgba(0xFF594D, 0.55f);

        /// <summary>A building still going up. Faded, so a site never reads as finished.</summary>
        public const float SiteAlpha = 0.35f;

        /// <summary>The drag box. Fill and edge are one hue at two alphas, not two colours.</summary>
        public static readonly Color Marquee = Rgba(0x73F28C, 0.12f);
        public static readonly Color MarqueeEdge = Rgba(0x73F28C, 0.85f);

        /// <summary>
        /// The minimap's ground. Lighter than the panel it sits in so the square
        /// reads as an object, dark enough that both owner hues stay legible on it.
        /// </summary>
        public static readonly Color MinimapGround = Rgb(0x262830);

        public static readonly Color MinimapView = Rgba(0xFFFFFF, 0.75f);
        public static readonly Color MinimapMark = Rgb(0xFF4D40);

        // ----------------------------------------------------------------- space

        // One 4px base, each step about half again as big as the last, so two
        // adjacent steps are never mistaken for each other. Nothing in the HUD is
        // offset by a number that is not one of these.
        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 20f;
        public const float S5 = 32f;

        public const float Hairline = 1f;

        // ------------------------------------------------------------------ type

        // Four sizes and no fifth. Micro is a keycap or a unit label, Body is
        // everything a player reads mid-fight, Big is a number they read without
        // reading, Display names a screen.
        public const int Micro = 10;
        public const int Body = 13;
        public const int Big = 17;
        public const int Display = 26;

        /// <summary>One row of Body text plus its leading. The vertical rhythm of every list.</summary>
        public const float Line = 18f;

        // --------------------------------------------------------------- metrics

        /// <summary>A control the hand aims at. Tall is the one a screen is about.</summary>
        public const float Control = 26f;
        public const float ControlTall = 34f;

        /// <summary>A muted name with its number under it. The top bar is sized from this.</summary>
        public const float ReadoutHeight = Micro + S1 + Line;
        public const float ReadoutWidth = 120f;

        // The command card sizes the bottom panel, not the other way round. It is
        // nine fixed cells and it may not be cropped: the previous layout let the
        // bottom row fall off the screen, which hid three commands entirely.
        public const float CardCellSize = 54f;
        public const float KeyCap = 16f;
        public const float CardWidth = 3f * CardCellSize + 2f * S1;
        public const float CardHeight = CardWidth;

        public const float TopBar = S2 + ReadoutHeight + S1;
        public const float BottomPanel = S2 + Micro + S1 + CardHeight + S2;

        /// <summary>Square, and as tall as the bar it lives in. That is what sizes it.</summary>
        public const float MinimapSize = BottomPanel;

        public const float ChipWidth = 132f;
        public const float ChipHeight = 20f;

        public const float InfoWidth = 260f;
        public const float IdleWidth = 140f;

        /// <summary>The button that opens the mixer. Narrow: one word, and it is not the point of the bar.</summary>
        public const float MixerButton = 92f;

        /// <summary>
        /// Everything pinned to the right of the top bar. The diagnostics to its
        /// left stop here, so one number moves when a control is added rather than
        /// three subtractions being kept in step by hand.
        /// </summary>
        public const float TopRightWidth = MixerButton + S2 + IdleWidth + S4;

        /// <summary>A mixer level's track. Thick enough to hit without aiming, thin enough not to read as a button.</summary>
        public const float SliderTrack = S2;

        /// <summary>One level: its name, the gap, its track.</summary>
        public const float MixerRow = Micro + S1 + SliderTrack;

        public const float MixerWidth = 220f;
        public const float MixerHeight = S3 + Micro + S1 + 3f * (MixerRow + S3);

        /// <summary>
        /// Where the mixer hangs: under the button that opens it, in the corner that
        /// button lives in, so the panel and the control that summoned it are one
        /// shape rather than two things to find.
        /// </summary>
        public static Rect MixerPanel() =>
            new Rect(Screen.width - MixerWidth - S4, TopBar + S2, MixerWidth, MixerHeight);

        public const float MenuWidth = 470f;
        public const float MenuHeight = 372f;
        public const float ResultWidth = 690f;
        public const float ResultHeight = 240f;

        /// <summary>The alert's sentence plus its key hint, one row each.</summary>
        public const float AlertWidth = 300f;
        public const float AlertHeight = S2 + Line + S1 + Micro + S1 + S2;

        /// <summary>
        /// Top and centred: a player's eyes are already near the top bar
        /// reading mana and population, not down in a corner a fight never
        /// reaches. Under the bar rather than over it, so the two hairlines
        /// never touch.
        /// </summary>
        public static Rect AlertBox() =>
            new Rect((Screen.width - AlertWidth) * 0.5f, TopBar + S2, AlertWidth, AlertHeight);

        // ---------------------------------------------------------------- styles

        // Built once, lazily, because GUI.skin only exists inside OnGUI. Nothing
        // here is a serialised asset: the backgrounds are 1x1 textures made at run
        // time, which is the whole reason the HUD needs no asset folder.
        //
        // ponytail: those backgrounds are HideAndDontSave and never freed, and the
        // toned styles are one shared object whose colour is set per call. Both are
        // fine for one process drawing one HUD on one thread. Pool the textures and
        // clone the styles the day an editor window wants them too.

        private static bool built;
        private static GUIStyle micro, header, big, display, tinted;
        private static GUIStyle field, button, buttonArmed, buttonOff;
        private static GUIStyle cell, keycap, chip;
        private static GUIStyle sliderTrack, sliderThumb;

        // Exposed for the two GUILayout screens. Everything drawn during a match
        // is a component below and never reaches for a style directly.
        public static GUIStyle MicroStyle { get { Build(); micro.normal.textColor = InkDim; return micro; } }
        public static GUIStyle BigStyle { get { Build(); return big; } }
        public static GUIStyle DisplayStyle { get { Build(); return display; } }
        public static GUIStyle FieldStyle { get { Build(); return field; } }

        /// <summary>A label at an arbitrary tone. The style is shared, so the colour is set per call.</summary>
        public static GUIStyle Toned(Color tone)
        {
            Build();
            tinted.fontSize = Body;
            tinted.normal.textColor = tone;
            return tinted;
        }

        private static Texture2D Solid(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        private static void Build()
        {
            if (built) return;
            built = true;

            micro = Label(Micro, InkDim, TextAnchor.UpperLeft);
            header = Label(Micro, InkMute, TextAnchor.UpperLeft);
            big = Label(Big, Ink, TextAnchor.UpperLeft);
            display = Label(Display, Ink, TextAnchor.UpperLeft);
            tinted = Label(Body, Ink, TextAnchor.UpperLeft);

            keycap = Label(Micro, InkDim, TextAnchor.MiddleCenter);

            cell = Label(Body, Ink, TextAnchor.LowerCenter);
            cell.wordWrap = true;

            field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = Body,
                fixedHeight = Control,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset((int)S2, (int)S2, 0, 0),
            };
            field.normal.background = Solid(Void);
            field.focused.background = Solid(Void);
            field.normal.textColor = Ink;
            field.focused.textColor = Ink;

            button = Button(Raised, Lifted, Ink, TextAnchor.MiddleCenter);
            buttonArmed = Button(Accent, Accent, OnAccent, TextAnchor.MiddleCenter);
            buttonOff = Button(Void, Void, InkMute, TextAnchor.MiddleCenter);
            chip = Button(Raised, Lifted, Ink, TextAnchor.MiddleLeft);
            chip.fontSize = Micro;
            chip.padding = new RectOffset((int)S1, (int)S1, 0, 0);

            // The track draws nothing: Slider paints the well and the fill itself,
            // so the level is a value and not a widget with a groove in it. The
            // thumb is a hairline notch cut into the fill's leading edge.
            sliderTrack = new GUIStyle { fixedHeight = SliderTrack };
            sliderThumb = new GUIStyle { fixedWidth = S1, fixedHeight = SliderTrack };
            sliderThumb.normal.background = Solid(Void);
            sliderThumb.active.background = Solid(Void);
        }

        private static GUIStyle Label(int size, Color color, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = anchor,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
                richText = false,
            };
            s.normal.textColor = color;
            return s;
        }

        /// <summary>
        /// A button is a flat face and a text colour. No border, no bevel, no
        /// gradient: separation on this HUD is value, the same rule the art uses.
        /// </summary>
        private static GUIStyle Button(Color face, Color hot, Color color, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = Body,
                alignment = anchor,
                padding = new RectOffset((int)S2, (int)S2, 0, 0),
                margin = new RectOffset(0, 0, 0, (int)S1),
                border = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
            };
            s.normal.background = Solid(face);
            s.hover.background = Solid(hot);
            s.active.background = Solid(hot);
            s.focused.background = Solid(face);
            s.normal.textColor = color;
            s.hover.textColor = color;
            s.active.textColor = color;
            s.focused.textColor = color;
            return s;
        }

        // ------------------------------------------------------------ primitives

        public static void Fill(Rect area, Color color)
        {
            Color was = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>A hairline rectangle. Four one-pixel lines, drawn inside the rect.</summary>
        public static void Frame(Rect area, Color color)
        {
            Fill(new Rect(area.x, area.y, area.width, Hairline), color);
            Fill(new Rect(area.x, area.yMax - Hairline, area.width, Hairline), color);
            Fill(new Rect(area.x, area.y, Hairline, area.height), color);
            Fill(new Rect(area.xMax - Hairline, area.y, Hairline, area.height), color);
        }

        public static Rect Middle(float width, float height) =>
            new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        /// <summary>The content box of a panel: one large step in, all round.</summary>
        public static Rect Inside(Rect panel) =>
            new Rect(panel.x + S4, panel.y + S3, panel.width - S4 * 2f, panel.height - S3 * 2f);

        // ------------------------------------------------------------ components

        /// <summary>
        /// A panel: a surface plus a hairline on the edge that faces the map. A
        /// panel with no edge floats; a panel with four edges is a dialog box, and
        /// this HUD has one dialog and two bars.
        /// </summary>
        public static void PanelBox(Rect area, Color fill)
        {
            Fill(area, fill);
            Frame(area, Edge);
        }

        /// <summary>A screen-wide bar. The hairline goes on the side the map is on.</summary>
        public static void BarBox(Rect area, bool edgeOnTop)
        {
            Fill(area, Bar);
            Fill(edgeOnTop
                ? new Rect(area.x, area.y, area.width, Hairline)
                : new Rect(area.x, area.yMax - Hairline, area.width, Hairline), Edge);
        }

        /// <summary>
        /// A section header. Micro, muted, over the thing it names. It is the
        /// quietest text on screen on purpose: the label is read once and the value
        /// under it is read every fight.
        /// </summary>
        public static void Header(Rect area, string label) => Header(area, label, InkMute);

        public static void Header(Rect area, string label, Color tone)
        {
            Build();
            header.normal.textColor = tone;
            GUI.Label(area, label, header);
        }

        public static void Label(Rect area, string s, Color tone)
        {
            Build();
            tinted.normal.textColor = tone;
            GUI.Label(area, s, tinted);
        }

        /// <summary>
        /// Secondary information: a count, a state, a hint, a diagnostic. Micro and
        /// dim, so it is there when looked for and invisible when not.
        /// </summary>
        public static void Note(Rect area, string s) => Note(area, s, InkDim);

        public static void Note(Rect area, string s, Color tone)
        {
            Build();
            micro.normal.textColor = tone;
            GUI.Label(area, s, micro);
        }

        public static bool Button(Rect area, string label, bool armed = false, bool enabled = true)
        {
            Build();
            if (enabled) return GUI.Button(area, label, armed ? buttonArmed : button);

            // Drawn as a face rather than greyed by GUI.enabled: Unity's disabled
            // state is the whole control at half alpha, which on a dark HUD is a
            // control that has gone missing rather than one that is off.
            GUI.Label(area, label, buttonOff);
            return false;
        }

        public static bool Button(string label, float height, bool armed = false)
        {
            Build();
            return GUILayout.Button(label, armed ? buttonArmed : button, GUILayout.Height(height));
        }

        /// <summary>
        /// One cell of the 3x3 command card. The keycap is drawn in the corner
        /// because position is the shortcut and the cell is the thing worth
        /// learning; armed inverts the whole cell, keycap included, so the state is
        /// visible from the edge of vision.
        /// </summary>
        public static bool CardCell(Rect area, string key, string label, bool armed)
        {
            Build();
            bool clicked = GUI.Button(area, GUIContent.none, armed ? buttonArmed : button);

            var cap = new Rect(area.x + S1, area.y + S1, KeyCap, KeyCap);
            Fill(cap, armed ? OnAccent : Void);
            keycap.normal.textColor = armed ? Accent : InkDim;
            GUI.Label(cap, key, keycap);

            cell.normal.textColor = armed ? OnAccent : Ink;
            GUI.Label(new Rect(area.x + S1, area.y + KeyCap, area.width - S2, area.height - KeyCap - S1),
                label, cell);
            return clicked;
        }

        /// <summary>An unusable cell. A hole in the card, not a button that does nothing.</summary>
        public static void EmptyCell(Rect area)
        {
            Fill(area, Void);
            Frame(area, Raised);
        }

        /// <summary>
        /// A readout: a muted name and the number under it, which is the one thing
        /// read mid-fight. Tone carries the state, so a capped population is red
        /// without the label moving.
        /// </summary>
        public static void Readout(Rect area, string label, string value, Color tone)
        {
            Build();
            GUI.Label(new Rect(area.x, area.y, area.width, Micro + S1), label, header);
            tinted.fontSize = Big;
            tinted.normal.textColor = tone;
            GUI.Label(new Rect(area.x, area.y + Micro + S1, area.width, Line), value, tinted);
            tinted.fontSize = Body;
        }

        /// <summary>A health or progress bar in screen space. Same ramp as the field's.</summary>
        public static void Meter(Rect area, float fill, Color color)
        {
            Fill(area, Void);
            Fill(new Rect(area.x, area.y, area.width * Mathf.Clamp01(fill), area.height), color);
        }

        /// <summary>
        /// A level from 0 to 1 the player drags. The same shape as Meter — a Void
        /// well with a fill — because "how much of this is there" is one question
        /// and deserves one idiom. The fill wears the accent rather than a semantic
        /// colour: a level is not a state, it is the thing the player is in the
        /// middle of, which is the only thing the accent ever means.
        /// </summary>
        public static float Slider(Rect area, float value)
        {
            Build();
            Fill(area, Void);
            Fill(new Rect(area.x, area.y, area.width * Mathf.Clamp01(value), area.height), Accent);
            return GUI.HorizontalSlider(area, value, 0f, 1f, sliderTrack, sliderThumb);
        }

        /// <summary>
        /// One entry of the selection list. Hurt is a stripe on the leading edge
        /// rather than red text: a list where half the words are red is a list
        /// nobody scans.
        /// </summary>
        public static bool Chip(Rect area, string label, float health)
        {
            Build();
            bool clicked = GUI.Button(area, "   " + label, chip);
            Fill(new Rect(area.x, area.y, S1 * 0.5f, area.height), Health(health));
            return clicked;
        }
    }
}
