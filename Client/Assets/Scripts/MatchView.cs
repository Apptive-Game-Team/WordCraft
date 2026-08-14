using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Draws the world. Reads simulation state and nothing else writes back, so
    /// this whole file could be deleted without changing a single tick.
    ///
    /// Roster art where the faction has it, primitives where it does not: a disc
    /// for mobile things, a square for static ones, hue for owner. Several slots
    /// in docs/FACTIONS.md are still concepts, so the fallback is permanent
    /// furniture, not a stopgap. Art is drawn untinted because STYLE.md makes the
    /// palette the thing that identifies a faction.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        /// <summary>How far a ring stands out past the art, as a fraction of the visible height.</summary>
        private const float RingBandRatio = 0.012f;

        /// <summary>Floor on that band, in cells, so zooming right in does not swallow the unit.</summary>
        private const float RingBandFloor = 0.10f;

        /// <summary>How near the cursor an entity has to be to light up. Matches the pickers.</summary>
        private const float HoverRadius = 1.1f;

        /// <summary>
        /// The range ring's line thickness, as a fraction of its own radius,
        /// baked into the texture in MakeRingSprite. A boundary, not a filled
        /// area: docs/UI-STYLE.md bans a gradient outright, and a disc filled
        /// out to a weapon's whole reach would wash the ground under it in the
        /// exact moment — a fight in progress — this issue exists to make
        /// legible. A line says how far without covering what is inside it.
        /// </summary>
        private const float RangeBandRatio = 0.08f;

        /// <summary>
        /// How long a hit's ring stays lit, in seconds. Short on purpose: a
        /// body under sustained fire is hit every tick or two, and a flash
        /// that outlived the next hit would never turn back off, reading as a
        /// held tint rather than the pulse-per-hit the acceptance criteria ask
        /// for ("피격 표시가 쌓이지 않는다. 사라진다"). A fresh hit only ever
        /// pushes this entity's own expiry forward — see the LateUpdate stamp
        /// below — so nothing here can accumulate a second ring on one body.
        /// </summary>
        private const float HitFlashSeconds = 0.35f;

        // Overlay geometry, in grid cells. A bar is the same size whatever the art
        // under it, so a row of damaged units reads as one row.
        private const float BarHeight = 0.16f;
        private const int OverlayOrder = 50;
        private const int GroundOrder = -100;
        private const int HoverOrder = 3;

        /// <summary>Debris is terrain: over the baked map, under everything that is alive.</summary>
        private const int RemnantOrder = -50;

        /// <summary>
        /// The fog sits over everything the world draws, so a remembered building
        /// is dimmed by the same sprite that dims the ground under it rather than
        /// by a second tint kept in step with the first by hand.
        /// </summary>
        private const int FogOrder = 60;

        /// <summary>
        /// The local peer's own marks: a rally point and a placement ghost may both
        /// be put down in ground this peer cannot see, and fog never hides what the
        /// player is doing right now.
        /// </summary>
        private const int AboveFogOrder = FogOrder + 1;

        /// <summary>
        /// How far a worker primitive is lifted toward white. Same owner hue, paler
        /// body, so a worker is told from a fighter without a second colour.
        /// </summary>
        private const float WorkerLift = 0.45f;

        /// <summary>Where the imported WordOnline sprites live, relative to a Resources folder.</summary>
        public const string SpriteFolder = "Art/Sprites/";

        /// <summary>
        /// Debris art, named here rather than looked up through FactionData: the
        /// roster's signature slot is deliberately empty because a death state is
        /// not a unit, so there is no slot for this to be the sprite of.
        /// </summary>
        private const string RemnantArt = "RockRemnant";

        public Camera Cam { get; private set; }

        private MatchRunner runner;

        /// <summary>The world these renderers were built for. A new one means new renderers.</summary>
        private World shown;

        private Sprite disc;
        private Sprite square;
        private readonly List<SpriteRenderer> views = new List<SpriteRenderer>();

        // Rings are not children of the art they ring. The art is scaled to fit its
        // footprint, and a child would inherit that scale, which is exactly what a
        // ring whose thickness has to answer to the zoom instead must not do.
        private readonly List<SpriteRenderer> rings = new List<SpriteRenderer>();
        private readonly List<float> footprints = new List<float>();

        /// <summary>A hairline circle at the selected entity's own weapon reach (issue #104). One per entity id, most of them always off.</summary>
        private readonly List<SpriteRenderer> rangeRings = new List<SpriteRenderer>();
        private Sprite rangeRingArt;

        /// <summary>The fading ring a hit itself puts on a body (issue #104), sized like the selection ring and coloured like danger instead of chosen.</summary>
        private readonly List<SpriteRenderer> hitFlashes = new List<SpriteRenderer>();

        /// <summary>Unscaled time each entity's flash goes dark. -1 means never armed; parallel to views.</summary>
        private readonly List<float> hitUntil = new List<float>();

        /// <summary>One highlight, moved to whatever the cursor is over.</summary>
        private SpriteRenderer hover;

        /// <summary>
        /// The whole map, one sprite. Flat until a world exists, because the start
        /// screen has no terrain to draw and the menu sits over this.
        /// </summary>
        private SpriteRenderer ground;

        /// <summary>The mask over it. One sprite, repainted on the fog's cadence rather than the frame's.</summary>
        private SpriteRenderer fog;

        // Overlays, parallel to views. Kept off the entity's own transform because
        // authored art is scaled to fit its footprint and a bar must not inherit that.
        private readonly List<SpriteRenderer> barBacks = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> barFills = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> queueFills = new List<SpriteRenderer>();

        // Rally markers are a pool, not one per entity: only the selected buildings
        // that have set one are ever drawn.
        private readonly List<SpriteRenderer> rallyMarkers = new List<SpriteRenderer>();

        // One renderer per blocked cell, pooled and re-aimed every frame. Not
        // indexed by anything the simulation owns, so a restart needs no reset:
        // the tail is switched off every frame anyway.
        private readonly List<SpriteRenderer> remnants = new List<SpriteRenderer>();
        private Sprite remnantArt;

        /// <summary>The building under the cursor during placement. One, made once.</summary>
        private SpriteRenderer ghost;
        private Role ghostRole;

        private readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft View").AddComponent<MatchView>();

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            disc = MakeSprite(round: true);
            square = MakeSprite(round: false);
            rangeRingArt = MakeRingSprite();
            remnantArt = Resources.Load<Sprite>(SpriteFolder + RemnantArt);

            float mid = MatchScenario.MapSize / 2f;
            Cam = new GameObject("Camera").AddComponent<Camera>();
            Cam.tag = "MainCamera";
            Cam.orthographic = true;
            Cam.orthographicSize = 18f;
            Cam.transform.position = new Vector3(mid, mid, -10f);
            Cam.backgroundColor = UiStyle.Sky;
            DontDestroyOnLoad(Cam.gameObject);

            ground = NewRenderer("Ground", square, UiStyle.Ground, GroundOrder);
            ground.transform.position = new Vector3(mid, mid, 0f);
            ground.transform.localScale = new Vector3(MatchScenario.MapSize, MatchScenario.MapSize, 1f);

            // Under everything an entity draws, above the ground.
            hover = NewRenderer("Hover", disc, UiStyle.HoverRing, HoverOrder);
            hover.enabled = false;

            fog = NewRenderer("Fog", Fog.Field(), Color.white, FogOrder); // a mask, not a tint
            fog.transform.position = new Vector3(mid, mid, 0f);
            fog.enabled = false;
        }

        private void Start() => runner = MatchRunner.Instance;

        /// <summary>
        /// LateUpdate, so the interpolated draw position is read after the frame's
        /// ticks have run rather than one frame behind them.
        /// </summary>
        private void LateUpdate()
        {
            if (runner == null) return;
            World world = runner.World;

            // Every pool is indexed by entity id. A restart hands out the same ids
            // to different entities, so keeping the old renderers would draw the new
            // match with the previous one's sprites and leave its dead standing.
            if (!ReferenceEquals(world, shown))
            {
                Drop(views);
                Drop(rings);
                Drop(rangeRings);
                Drop(hitFlashes);
                Drop(barBacks);
                Drop(barFills);
                Drop(queueFills);
                Drop(rallyMarkers);
                footprints.Clear(); // parallel to views, and Create refills it
                hitUntil.Clear();   // same: a restart's ids owe nothing to the match before it
                shown = world;

                // Terrain is fixed for the whole match, so the map is baked here and
                // never touched again. The sprite is one cell to one unit, so the
                // quad that was scaled up to cover the map now sits at scale one.
                ground.sprite = Tiles.Field(world);
                ground.color = Color.white; // a picture, not a tint
                ground.transform.localScale = Vector3.one;
            }

            for (int i = views.Count; i < world.EntityCount; i++) views.Add(Create(world, world.GetEntity(i)));

            // Stamped once here rather than read again per entity below: this is
            // the same list HitDetection hands the minimap and the alert, so a
            // hit already lit up off-screen feedback does not walk World a second
            // time to also light up on-screen feedback (issue #104).
            IReadOnlyList<int> hits = HitDetection.HitIds(world, runner.LocalPeer);
            for (int h = 0; h < hits.Count; h++) hitUntil[hits[h]] = Time.unscaledTime + HitFlashSeconds;

            Fog.Update(runner);
            fog.enabled = Fog.Active;

            int markers = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);
                SpriteRenderer sr = views[i];
                byte show = Fog.Show(i, e);
                if (show == Fog.Hidden)
                {
                    sr.enabled = false;
                    rings[i].enabled = false;
                    rangeRings[i].enabled = false;
                    hitFlashes[i].enabled = false;
                    Overlays(i);
                    continue;
                }

                // Remembered: the renderer is left exactly as the last frame that
                // could see it left it, which is what "last known state" means for a
                // thing that cannot move. Nothing is read off the entity here, so a
                // building razed out of sight goes on standing and one raised out of
                // sight does not appear. Its meters go, because hp is not remembered.
                if (show == Fog.Memory)
                {
                    rings[i].enabled = false;
                    rangeRings[i].enabled = false;
                    hitFlashes[i].enabled = false;
                    Overlays(i);
                    continue;
                }

                bool picked = Selection.Instance != null && Selection.Instance.Contains(i);
                sr.enabled = true;
                rings[i].enabled = picked;
                Vector2 p = runner.DrawPosition(i);
                sr.transform.position = new Vector3(p.x, p.y, 0f);
                if (picked) Ring(rings[i], footprints[i], p);

                // "이게 저걸 때릴 수 있나" is a question the roster already answers;
                // this only ever reads it, never a re-typed number of its own, so
                // the ring can never say something FactionData does not (issue #104).
                if (picked && world.Armed(e))
                {
                    RangeRing(rangeRings[i], FactionData.Stats(world.FactionOf(e.Owner), e.Role, e.Slot).Range, p);
                }
                else
                {
                    rangeRings[i].enabled = false;
                }

                bool flashing = hitUntil[i] > Time.unscaledTime;
                hitFlashes[i].enabled = flashing;
                if (flashing)
                {
                    Ring(hitFlashes[i], footprints[i], p);
                    Color flash = UiStyle.Danger;
                    flash.a = Mathf.Clamp01((hitUntil[i] - Time.unscaledTime) / HitFlashSeconds);
                    hitFlashes[i].color = flash;
                }

                Overlays(world, i, e, p, picked);

                // A site under construction reads as a ghost until it finishes.
                if (e.Kind == EntityKind.Building)
                {
                    Color c = sr.color;
                    c.a = e.BuildTicksLeft > 0 ? UiStyle.SiteAlpha : 1f;
                    sr.color = c;
                }

                if (picked && e.Kind == EntityKind.Building && e.HasRallyPoint)
                {
                    Rally(markers++, MatchRunner.ToView(e.RallyPoint));
                }
            }

            for (int i = markers; i < rallyMarkers.Count; i++) rallyMarkers[i].enabled = false;
            Remnants(world);
            Hover(world);
            Ghost(world);
        }

        /// <summary>
        /// 돌 골렘 부족 잔해: a rock on every cell the simulation is blocking.
        ///
        /// Not baked into the terrain texture. That is one 512x512 upload per
        /// world because terrain never changes; debris appears and lapses mid
        /// match, and rebaking the map every time a golem falls pays the whole
        /// map's cost for one cell. A pooled renderer a cell is the same shape
        /// the rally markers already use.
        ///
        /// Drawn as terrain rather than as a body. Roster art goes down untinted
        /// because a palette is what identifies a faction; this is tinted into the
        /// blocked value band instead, so what a player reads is a wall.
        ///
        /// Alpha carries how much life is left, which is what SiteAlpha already
        /// means on a building that is not finished — one idiom, one thing to
        /// learn. Alpha rather than scale because debris blocks its whole cell
        /// until the last tick: a pile that shrank would open gaps between
        /// neighbours that a player would walk into. It never reaches zero, or
        /// this would end where it started, with an invisible wall.
        /// </summary>
        // ponytail: every cell tested every frame, 4096 int compares for a map
        // that holds a handful of these. It is less than the entity loop above it
        // and it needs nothing from Sim; ask the simulation for a list of live
        // cells the day a profile disagrees.
        private void Remnants(World world)
        {
            int used = 0;
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (!world.HasRemnant(cell)) continue;

                SpriteRenderer sr = Debris(used++);
                Vector2 p = MatchRunner.ToView(World.CellCenter(cell));
                sr.transform.position = new Vector3(p.x, p.y, 0f);

                float left = (world.RemnantExpiry(cell) - world.Tick) / (float)World.RemnantTicks;
                Color c = UiStyle.Remnant;
                c.a = Mathf.Lerp(UiStyle.RemnantFade, 1f, Mathf.Clamp01(left));
                sr.color = c;
                sr.enabled = true;
            }

            for (int i = used; i < remnants.Count; i++) remnants[i].enabled = false;
        }

        /// <summary>
        /// One pooled debris renderer, covering exactly the cell the simulation is
        /// blocking and no more.
        ///
        /// Stretched to the cell rather than fitted to its longest edge the way
        /// roster art is. The source is 128x89, so a fit would leave a third of a
        /// cell of clear ground between two stacked remnants — a hole in a wall
        /// that has none, which is the one thing this layer may not draw. Fitting
        /// the short edge instead would spill the art a fifth of a cell over each
        /// horizontal neighbour, which lies the other way. A pile of rubble
        /// squashed is still a pile of rubble; a wall with a gap is a route.
        /// </summary>
        private SpriteRenderer Debris(int index)
        {
            while (remnants.Count <= index)
            {
                SpriteRenderer made = NewRenderer("Remnant",
                    remnantArt != null ? remnantArt : square, UiStyle.Remnant, RemnantOrder);
                Vector2 size = remnantArt != null ? (Vector2)remnantArt.bounds.size : Vector2.one;
                made.transform.localScale = new Vector3(
                    size.x > 0f ? 1f / size.x : 1f, size.y > 0f ? 1f / size.y : 1f, 1f);
                remnants.Add(made);
            }
            return remnants[index];
        }

        /// <summary>
        /// A ring the width of the art's footprint plus a band, and the band follows
        /// the zoom. A band fixed in world units disappears with the whole map on
        /// screen; a band fixed in pixels swallows the unit zoomed right in. Neither
        /// is legible at both ends, so the band is a fraction of what is visible.
        /// </summary>
        private void Ring(SpriteRenderer ring, float footprint, Vector2 p)
        {
            float band = Mathf.Max(RingBandFloor, Cam.orthographicSize * RingBandRatio);
            float d = footprint + band * 2f;
            ring.transform.position = new Vector3(p.x, p.y, 0f);
            ring.transform.localScale = new Vector3(d, d, 1f);
        }

        /// <summary>
        /// Sizes and places the reach ring at a weapon's actual range, read off
        /// the roster in cells and converted the same way Fog already converts
        /// the same field (Fog.Reach): Fix.Raw over Fix.One, since a range is a
        /// plain distance and never needs the position math MatchRunner.ToView
        /// does for a point. Unlike the selection Ring, this one's thickness is
        /// not held to a screen-space floor — the shape it draws is the
        /// boundary the player asked about, not a sliver around a footprint
        /// that would otherwise vanish, so it is free to scale with the map
        /// like everything else drawn in it.
        /// </summary>
        private static void RangeRing(SpriteRenderer sr, Fix range, Vector2 center)
        {
            float radius = range.Raw / (float)Fix.One;
            sr.transform.position = new Vector3(center.x, center.y, 0f);
            sr.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            sr.enabled = true;
        }

        /// <summary>
        /// Lights up whatever the cursor is over, friend or enemy, so a click has a
        /// visible target before it is a committed order. Nothing lights up under
        /// the HUD, because a click there never reaches the world either.
        ///
        /// Nothing the fog is hiding lights up either, or sweeping the cursor over
        /// black ground would map the enemy army through it. The click still lands:
        /// the pickers ask the simulation and the simulation sees everything. This
        /// is the view hiding a ring, not a rule about what may be targeted, which
        /// is a rule view-only fog cannot have.
        /// </summary>
        // ponytail: a full entity scan every frame. It is the same scan the pickers
        // already do per click over a few dozen entities; index the grid the day
        // that stops being true.
        private void Hover(World world)
        {
            int id = Hud.OverUi(Input.mousePosition)
                ? -1
                : runner.EntityAt(Cam.ScreenToWorldPoint(Input.mousePosition), HoverRadius, mineOnly: false);

            hover.enabled = id >= 0 && Fog.Show(id, world.GetEntity(id)) == Fog.Seen;
            if (!hover.enabled) return;

            hover.sprite = Shape(world.GetEntity(id).Kind);
            Ring(hover, footprints[id], runner.DrawPosition(id));
        }

        /// <summary>
        /// The placement preview: the building's own art under the cursor, snapped
        /// to the cell it would take, tinted by whether the simulation would accept
        /// it. The tint is feedback and nothing else. It is read from a world up to
        /// a tick behind the one the command will meet, so the answer that counts is
        /// the one Apply takes; nothing here gates the click.
        /// </summary>
        private void Ghost(World world)
        {
            Orders orders = Orders.Instance;
            bool placing = orders != null && orders.Pending == CommandType.Build &&
                           orders.Placing != Role.None && Cam != null &&
                           !Hud.OverUi(Input.mousePosition);

            if (!placing)
            {
                if (ghost != null) ghost.enabled = false;
                return;
            }

            Role role = orders.Placing;
            FixVec2 aimed = MatchRunner.ToSim(Cam.ScreenToWorldPoint(Input.mousePosition));
            bool legal = world.CanBuild(runner.LocalPeer, role, aimed);

            if (ghost == null) ghost = NewRenderer("BuildGhost", square, UiStyle.GhostOk, AboveFogOrder);
            if (ghostRole != role)
            {
                ghostRole = role;
                // Entry 0 only: Build still names a plain role (Sim/World.cs
                // Apply), so nothing it places can be anything else yet (#111).
                Sprite drawn = ArtFor(world.FactionOf(runner.LocalPeer), role, 0);
                ghost.sprite = drawn != null ? drawn : square;
                ghost.transform.localScale = drawn != null ? FitScale(drawn, role) : Vector3.one * 2f;
            }

            // Drawn on the cell the simulation would use, not under the pointer, so
            // what the player sees is where the building lands.
            Vector2 cell = MatchRunner.ToView(World.CellCenter(World.CellOf(aimed)));
            ghost.transform.position = new Vector3(cell.x, cell.y, 0f);
            ghost.color = legal ? UiStyle.GhostOk : UiStyle.GhostBad;
            ghost.enabled = true;
        }

        /// <summary>
        /// Health bar over anything hurt or selected, production progress under a
        /// selected building. Both are read straight off the entity every frame and
        /// held nowhere, so the overlay cannot drift from what the simulation says.
        /// </summary>
        /// <summary>Every meter off: the thing is dead, unseen, or only remembered.</summary>
        private void Overlays(int i)
        {
            barBacks[i].enabled = false;
            barFills[i].enabled = false;
            queueFills[i].enabled = false;
        }

        private void Overlays(World world, int i, Entity e, Vector2 p, bool picked)
        {
            bool node = e.Kind == EntityKind.ResourceNode;
            bool hurt = e.Hp < e.MaxHp;
            bool bar = e.Alive && !node && (hurt || picked);

            barBacks[i].enabled = bar;
            barFills[i].enabled = bar;
            if (bar)
            {
                float width = e.Kind == EntityKind.Building ? 2.2f : 1.1f;
                float y = p.y + (e.Kind == EntityKind.Building ? 1.7f : 0.75f);
                float ratio = e.MaxHp > 0 ? Mathf.Clamp01(e.Hp / (float)e.MaxHp) : 0f;

                Place(barBacks[i], p.x, y, width, 1f);
                Place(barFills[i], p.x, y, width, ratio);
                barFills[i].color = UiStyle.Health(ratio);
            }

            // ProduceTicksLeft counts down and is zero in the tick before a queued
            // unit starts, so an empty bar means queued-not-started, not finished.
            bool queue = e.Alive && picked && e.Kind == EntityKind.Building && e.QueueCount > 0;
            queueFills[i].enabled = queue;
            if (queue)
            {
                // The queue holds one roster entry at a time (Economy.TryQueueUnit),
                // and that entry's clock since #93 priced it per faction and role,
                // and since #110 per entry too — the shared default
                // World.ProduceTicks reads a warlord's bar as full for its first
                // 100 of 140 ticks.
                int ticks = FactionData.Production(world.FactionOf(e.Owner), e.ProduceRole, e.ProduceSlot).Ticks;
                float done = e.ProduceTicksLeft == 0
                    ? 0f
                    : (ticks - e.ProduceTicksLeft) / (float)ticks;
                Place(queueFills[i], p.x, p.y + 1.45f, 2.2f, done);
            }
        }

        /// <summary>Grows a bar from its left edge rather than its middle.</summary>
        private static void Place(SpriteRenderer sr, float x, float y, float width, float fill)
        {
            float w = width * fill;
            sr.transform.position = new Vector3(x - (width - w) * 0.5f, y, 0f);
            sr.transform.localScale = new Vector3(w, BarHeight, 1f);
        }

        private void Rally(int index, Vector2 point)
        {
            while (rallyMarkers.Count <= index)
            {
                var made = NewRenderer("Rally", square, UiStyle.Rally, AboveFogOrder);
                made.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
                made.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                rallyMarkers.Add(made);
            }
            rallyMarkers[index].transform.position = new Vector3(point.x, point.y, 0f);
            rallyMarkers[index].enabled = true;
        }

        private SpriteRenderer Create(World world, Entity e)
        {
            Color owner = UiStyle.Owner(e.Owner);
            Sprite shape = Shape(e.Kind);
            Color color;
            float scale;
            float spin = 0f;

            switch (e.Kind)
            {
                case EntityKind.Worker:
                    color = Color.Lerp(owner, Color.white, WorkerLift);
                    scale = 0.6f;
                    break;
                case EntityKind.Unit:
                    color = owner;
                    scale = 0.9f;
                    break;
                case EntityKind.Building:
                    color = owner;
                    scale = 1.6f;
                    break;
                default:
                    color = UiStyle.Node;
                    scale = 0.9f;
                    // Rotated square: a diamond reads as "not a building" at a glance.
                    spin = 45f;
                    break;
            }

            Sprite drawn = e.Owner < 0 ? null : ArtFor(world.FactionOf(e.Owner), e.Role, e.Slot);
            var sr = NewRenderer(Label(world, e), drawn != null ? drawn : shape,
                drawn != null ? Color.white : color, e.Kind == EntityKind.Building ? 5 : 10);
            sr.transform.localScale = drawn != null ? FitScale(drawn, e.Role) : new Vector3(scale, scale, 1f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, drawn != null ? 0f : spin);

            var ring = NewRenderer("Ring", shape, UiStyle.Ring, sr.sortingOrder - 1);
            ring.enabled = false;
            rings.Add(ring);
            footprints.Add(drawn != null ? Cells(e.Role) : scale);

            // Above the fog like the rally marker and the build ghost: both are
            // information about what the local peer itself is doing right now,
            // and fog never hides that (docs/UI-STYLE.md 안개).
            var rangeRing = NewRenderer("Range", rangeRingArt, UiStyle.RangeRing, AboveFogOrder);
            rangeRing.enabled = false;
            rangeRings.Add(rangeRing);

            var flash = NewRenderer("HitFlash", shape, UiStyle.Danger, sr.sortingOrder - 1);
            flash.enabled = false;
            hitFlashes.Add(flash);
            hitUntil.Add(-1f);

            barBacks.Add(Overlay("HpBack", UiStyle.MeterBack, OverlayOrder));
            barFills.Add(Overlay("HpFill", Color.white, OverlayOrder + 1));
            queueFills.Add(Overlay("Queue", UiStyle.Queue, OverlayOrder + 1));

            return sr;
        }

        /// <summary>Roster art for this entry, or null when it has none yet.</summary>
        private Sprite ArtFor(Faction faction, Role role, int slot)
        {
            if (role == Role.None) return null;
            string file = FactionData.Sprite(faction, role, slot);
            if (file.Length == 0) return null;

            if (!art.TryGetValue(file, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>(SpriteFolder + file);
                art[file] = sprite; // cache the miss too, so a bad name is one failed load
            }
            return sprite;
        }

        private static string Label(World world, Entity e) =>
            e.Owner < 0 ? e.Kind.ToString() : FactionData.Name(world.FactionOf(e.Owner), e.Role, e.Slot);

        /// <summary>
        /// Fits authored art to its role's footprint in cells. The source sprites
        /// were drawn for another game at another pixels-per-unit, so their own
        /// size means nothing here.
        /// </summary>
        private static Vector3 FitScale(Sprite sprite, Role role)
        {
            Vector2 size = sprite.bounds.size;
            float longest = Mathf.Max(size.x, size.y);
            float k = longest > 0f ? Cells(role) / longest : 1f;
            return new Vector3(k, k, 1f);
        }

        /// <summary>
        /// A role's footprint on the grid. What the art is fitted to, what a ring
        /// rings, and the floor on what a building of that role can see: a thing
        /// is never fogged under its own feet.
        /// </summary>
        public static float Cells(Role role)
        {
            switch (role)
            {
                case Role.Base: return 4.0f;
                case Role.Production: return 3.0f;
                case Role.Defense: return 2.5f;
                case Role.Worker: return 1.2f;
                default: return 1.8f;
            }
        }

        private Sprite Shape(EntityKind kind) =>
            kind == EntityKind.Unit || kind == EntityKind.Worker ? disc : square;

        /// <summary>Empties a pool. Rings are children of their view, so ordering does not matter.</summary>
        private static void Drop(List<SpriteRenderer> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null) Destroy(pool[i].gameObject);
            }
            pool.Clear();
        }

        private SpriteRenderer Overlay(string name, Color color, int order)
        {
            SpriteRenderer sr = NewRenderer(name, square, color, order);
            sr.enabled = false;
            return sr;
        }

        private SpriteRenderer NewRenderer(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>One texture per shape, generated once. 1 sprite unit is 1 grid cell.</summary>
        private static Sprite MakeSprite(bool round)
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            float radius = size / 2f - 1f;
            var centre = new Vector2((size - 1) / 2f, (size - 1) / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = !round || Vector2.Distance(new Vector2(x, y), centre) <= radius;
                    // A mask, not a colour: the tint comes from the SpriteRenderer.
                    pixels[y * size + x] = inside ? (Color32)Color.white : (Color32)Color.clear;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        /// <summary>
        /// A hairline circle: clear everywhere except a thin band at the very
        /// edge, at RangeBandRatio of its own radius. Baked once at a higher
        /// texel count than MakeSprite's discs and squares, because this one is
        /// stretched out to a weapon's whole reach rather than to a body's own
        /// footprint, and a thin band needs the extra resolution to survive
        /// that stretch as a line instead of a stair-step.
        /// </summary>
        private static Sprite MakeRingSprite()
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            float outer = size / 2f - 1f;
            float inner = outer * (1f - RangeBandRatio);
            var centre = new Vector2((size - 1) / 2f, (size - 1) / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), centre);
                    bool inside = d <= outer && d >= inner;
                    pixels[y * size + x] = inside ? (Color32)Color.white : (Color32)Color.clear;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
