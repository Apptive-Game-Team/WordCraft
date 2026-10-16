using System.Collections.Generic;
using UnityEngine;
using WordCraft.Sim;

namespace WordCraft.View
{
    /// <summary>
    /// Draws the world. Reads simulation state and nothing else writes back, so
    /// this whole file could be deleted without changing a single tick.
    ///
    /// The field is three dimensional: the terrain is a mesh (FieldMesh), every
    /// body is a model built from the roster (Models), and the marks the HUD puts
    /// on the ground — rings, meters, the placement ghost — lie on the terrain or
    /// float over a head (Decals). The simulation is still a flat grid; Field is
    /// the one place that turns its (x, y) into a point on the hills, and every
    /// position below goes through it.
    ///
    /// Roster art is no longer drawn on the field. The sprites stay in Resources
    /// for the HUD — the command card and the production menu still show them as
    /// icons — and <see cref="SpriteFolder"/> stays here because that is where
    /// those readers look it up.
    /// </summary>
    public sealed class MatchView : MonoBehaviour
    {
        /// <summary>How far a ring stands out past the body, as a fraction of the view's half-height.</summary>
        private const float RingBandRatio = 0.012f;

        /// <summary>Floor on that band, in cells, so zooming right in does not swallow the unit.</summary>
        private const float RingBandFloor = 0.10f;

        /// <summary>How near the cursor an entity has to be to light up. Matches the pickers.</summary>
        private const float HoverRadius = 1.1f;

        /// <summary>
        /// The range ring's line width, as a fraction of its own radius. A
        /// boundary, not a filled area: docs/UI-STYLE.md bans a gradient
        /// outright, and a disc filled out to a weapon's whole reach would wash
        /// the ground under it in the exact moment — a fight in progress — this
        /// exists to make legible. A line says how far without covering what is
        /// inside it.
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

        // Meter geometry, in world units. A bar is the same size whatever the body
        // under it, so a row of damaged units reads as one row.
        private const float BarHeight = 0.16f;
        private const float BarLift = 0.45f;

        /// <summary>
        /// How wide 차원 유랑종's 도착 지점 is drawn, in cells: a body's own
        /// footprint, because that is what the mark means — a body stands up here.
        /// A disc rather than the rally marker's diamond for the same reason: the
        /// two sit on the same map and name the two halves of one journey, and
        /// UiStyle.Rally and GhostOk are near enough in hue that shape has to be
        /// what tells them apart.
        /// </summary>
        private const float ArrivalMarkCells = 0.9f;

        /// <summary>How translucent the placement ghost is. The same idea SiteAlpha carries for a site.</summary>
        private const float GhostAlpha = 0.55f;

        /// <summary>
        /// A flier's shadow on the ground, as a fraction of its footprint. The one
        /// thing that tells the eye how high a body is hovering, and so the thing
        /// that makes it read as flying rather than as standing on a taller base.
        /// </summary>
        private const float ShadowRatio = 0.55f;

        // 상태 이상과 오라 (issue #206).

        /// <summary>A slowed body's vine ring: its radius as a share of the footprint, and its band, in cells.</summary>
        private const float SlowRingRatio = 0.42f;
        private const float SlowRingBand = 0.22f;

        /// <summary>The vine ring's alpha with the whole slow ahead of it and with none left: it fades as the slow runs out.</summary>
        private const float SlowAlphaFull = 0.95f;
        private const float SlowAlphaLast = 0.3f;

        /// <summary>An aura's radius ring, as a share of its radius: thinner than a weapon's, it is a reach, not a threat.</summary>
        private const float AuraBandRatio = 0.05f;

        /// <summary>The burn aura's standing ring: a fixed band in cells, and faint, since it is on whether or not anyone asked.</summary>
        private const float BurnAuraBand = 0.12f;

        /// <summary>A burn's tick, as a share of a weapon's burst.</summary>
        private const float BurnTickScale = 0.55f;

        /// <summary>
        /// Slack on a chain's hop radius, in cells, when the view guesses which
        /// bodies a chain reached: the guess measures between whole-tick
        /// positions as the simulation does, but a frame can span two ticks.
        /// </summary>
        private const float ChainSlack = 0.5f;

        /// <summary>Seconds between one hop of a chain and the next, so the jump is seen to travel.</summary>
        private const float ChainHopDelay = 0.07f;

        /// <summary>
        /// The vine ring: 세계수's lightest leaf, lifted a fifth toward white so
        /// it still reads on the dark ground under a 지옥불 hull.
        /// </summary>
        private static readonly Color SlowColour = BodyPalette.Lighter(BodyPalette.TreeSpirits.LeafLight, 0.2f);
        private static readonly Color HealRing = WithAlpha(UiStyle.Good, 0.8f);

        /// <summary>A heal's motes: the Good green lifted toward white, so they read on grass, which is green too.</summary>
        private static readonly Color HealMote = BodyPalette.Lighter(UiStyle.Good, 0.45f);

        /// <summary>A heal's ring, and its alpha: stronger than a building's dust, so a heal reads at 64 px.</summary>
        private static readonly Color HealDust = BodyPalette.Lighter(UiStyle.Good, 0.3f);
        private const float HealDustAlpha = 0.95f;
        private static readonly Color AuraRing = WithAlpha(UiStyle.Accent, 0.7f);
        private static readonly Color BurnAuraRing = WithAlpha(BodyPalette.Hellfire.Ember, 0.35f);

        // 물 지대·한시 소환·폭발·광역·전개 (issue #212).

        /// <summary>A water field cell's alpha, and what it has faded to on the tick before it lapses.</summary>
        private const float FieldAlpha = 0.38f;
        private const float FieldAlphaLast = 0.12f;

        /// <summary>
        /// The last ticks of a field, over which it fades. Under the 41 ticks a
        /// finished 솟는 샘's field always has left (80 a write, a write every
        /// 40), so a spring's standing field never flickers.
        /// </summary>
        private const int FieldFadeTicks = 40;

        /// <summary>The issue's last 60 ticks of a summon's life, in which it blinks.</summary>
        private const int ExpiryBlinkTicks = 60;

        /// <summary>A summon's lifetime meter beside the hp bar: its width, and its gap from the bar, in world units.</summary>
        private const float LifeWidth = 0.42f;
        private const float LifeGap = 0.06f;

        /// <summary>A death or arrival burst, and a strike landing: the issue's 1.5 of a weapon's impact.</summary>
        private const float BlastImpact = 1.5f;

        /// <summary>How high over its point a falling strike starts, in cells, and how long 간헐천's spout rises.</summary>
        private const float DropHeight = 7f;
        private const float DropFlight = 0.28f;

        /// <summary>How high 간헐천's spout rises, in cells.</summary>
        private const float SpoutHeight = 2.6f;

        /// <summary>
        /// A delayed strike's warning ring on its point while the clock runs:
        /// faint at the cast and strong at the landing. Its band, like an aura's,
        /// a share of the radius.
        /// </summary>
        private const float WarnAlphaFirst = 0.3f;
        private const float WarnAlphaLast = 0.8f;
        private const float WarnBandRatio = 0.06f;

        /// <summary>A zone's edge, faint: the reach of a thing the chips already show standing.</summary>
        private const float ZoneAlpha = 0.45f;

        /// <summary>A 칼바람 회오리 hit's ring, under a strike's.</summary>
        private const float GaleHitAlpha = 0.6f;

        // A zone's chips: how many and how fast they turn, degrees a second.
        private const int GaleChips = 12;
        private const float GaleTurn = 320f;
        private const int SandChips = 18;
        private const float SandTurn = 110f;

        /// <summary>
        /// Slack on World.RemnantUseReach when the view guesses which debris a
        /// 바위 술사 took: it measures from where the body is drawn, a frame can
        /// span two ticks, and the simulation measures to the cell centre.
        /// </summary>
        private const float RemnantSlack = 0.6f;

        /// <summary>Slack on a 잔해 수리's radius when the view guesses which ally it mended.</summary>
        private const float MendSlack = 1f;

        /// <summary>A delayed strike, a zone and a tide, coloured as their faction shoots, but 낙뢰 in lightning yellow, as a chain's bolt is.</summary>
        private static Color StrikeColour(Faction faction) =>
            faction == Faction.Humans ? BodyPalette.TreeSpirits.Bolt : BodyPalette.Shot(faction);

        /// <summary>Where the imported WordOnline sprites live, relative to a Resources folder. The HUD's icons.</summary>
        public const string SpriteFolder = "Art/Sprites/";

        public Camera Cam { get; private set; }

        private MatchRunner runner;

        /// <summary>The world these bodies were built for. A new one means new bodies.</summary>
        private World shown;

        private readonly List<BodyRig> bodies = new List<BodyRig>();

        /// <summary>
        /// What each body was dressed for (<see cref="SkinOf"/>), parallel to
        /// bodies. 징발 is the one rule that changes a live body's owner and roster
        /// entry, so this is the only reason a body is ever built twice.
        /// </summary>
        private readonly List<int> skins = new List<int>();

        /// <summary>
        /// Each entity's weapon cooldown as of the last frame, parallel to bodies.
        /// A cooldown that went up is a shot that was fired — Combat resets it on
        /// the tick an attack lands — and that is the one moment the body swings.
        /// </summary>
        private readonly List<int> cooldowns = new List<int>();

        // Rings lie on the ground under a body, and are not children of it: the
        // body turns to face where it walks, and a ring that turned with it would
        // be a ring that visibly spins.
        private readonly List<Decal> rings = new List<Decal>();

        /// <summary>A hairline circle at the selected entity's own weapon reach (issue #104). One per entity id, most of them always off.</summary>
        private readonly List<Decal> rangeRings = new List<Decal>();

        /// <summary>The fading ring a hit itself puts on a body (issue #104), sized like the selection ring and coloured like danger instead of chosen.</summary>
        private readonly List<Decal> hitFlashes = new List<Decal>();

        /// <summary>
        /// Whether each entity was alive as of the last frame, parallel to bodies.
        /// Alive going from true to false is the frame a death is drawn from: the
        /// simulation has no death event, only the flag, and this is the view
        /// noticing the flag change.
        /// </summary>
        private readonly List<bool> alive = new List<bool>();

        /// <summary>
        /// Bodies that no longer belong to any entity and are playing their way
        /// out: a growth's old body swelling before the new one comes up. Destroyed
        /// when they report themselves spent.
        /// </summary>
        private readonly List<BodyRig> retiring = new List<BodyRig>();

        /// <summary>Tracers and impact shards, pooled (issue #165).</summary>
        private Effects effects;

        /// <summary>
        /// This frame's hit ids, copied out of HitDetection's shared list at the
        /// top of the frame and applied after every shot of the frame has been
        /// fired, so a hit whose tracer is still in the air waits for it.
        /// </summary>
        private readonly List<int> struck = new List<int>();

        /// <summary>Entities seen dying this frame, given their death after every shot of the frame.</summary>
        private readonly List<int> fallen = new List<int>();

        /// <summary>
        /// Each entity's production queue as of the last frame, parallel to
        /// bodies. A queue that went down on the frame a unit of the same owner
        /// appeared is a unit delivered; one that went down with nothing new
        /// beside it was cancelled, and the building does nothing (issue #186).
        /// </summary>
        private readonly List<int> queues = new List<int>();

        /// <summary>The units and workers that appeared this frame: what a building that just delivered looks for.</summary>
        private readonly List<int> born = new List<int>();

        /// <summary>
        /// Per entity: a hit flash is owed when the tracer now flying at it lands.
        /// Parallel to bodies. HitDetection decides who gets a flash (the local
        /// peer's own bodies); the landing only decides when.
        /// </summary>
        private readonly List<bool> flashOnLanding = new List<bool>();

        /// <summary>
        /// False until the first frame of a world has built its bodies. Everything
        /// built on that frame was already standing when the match began; only a
        /// body built after it was produced, and only that one rises out of the ground.
        /// </summary>
        private bool primed;

        /// <summary>
        /// The reach above which a weapon fires something visible. FactionData
        /// gives melee a reach of exactly two cells; the issue draws a tracer for
        /// anything that reaches further.
        /// </summary>
        public static readonly Fix TracerReach = Fix.FromInt(2);

        /// <summary>Whether a weapon with these stats throws a tracer rather than landing a blow. The one test, shared with ViewShots.</summary>
        public static bool Throws(UnitStats stats) => Throws(stats.Range);

        /// <summary>
        /// The same test on a reach alone, for a caller holding a live body: World.RangeFor
        /// is the researched reach, and 골렘 사거리 lifts a golem's 2 cells to 3, so a
        /// swing that has become a throw gets a tracer.
        /// </summary>
        public static bool Throws(Fix range) => range > TracerReach;

        /// <summary>
        /// 돌 골렘 부족's combat unit leaves rubble where it falls (Sim/World.cs
        /// DropRemnant), so it dies into rubble. The one test, shared with ViewShots.
        /// </summary>
        public static bool Rubble(Faction faction, EntityKind kind) =>
            kind == EntityKind.Unit && faction == Faction.RockGolems;

        /// <summary>
        /// What the view saw of each entity's hp and status clocks last frame,
        /// parallel to bodies (issue #206). The simulation keeps no event for a
        /// heal or a burn's tick, only the numbers; this is the view comparing
        /// them, as it compares cooldowns for a shot.
        /// </summary>
        private struct Vital
        {
            public int Hp, MaxHp, Site, Burn, Slow;
            /// <summary>AbilityTicksLeft and AbilityCooldown, and Deployed (issue #212).</summary>
            public int Clock, Cooldown;
            public bool Deployed;
            /// <summary>The slow clock when it was last set: what the vine ring's fade is measured against.</summary>
            public int SlowPeak;
        }

        private readonly List<Vital> vitals = new List<Vital>();

        /// <summary>Whether each entity's hp fell this frame, parallel to bodies: what a chain's guess looks for.</summary>
        private readonly List<bool> dropped = new List<bool>();

        /// <summary>The vine ring under a slowed body, parallel to bodies.</summary>
        private readonly List<Decal> slowRings = new List<Decal>();

        /// <summary>An aura source's radius ring, parallel to bodies: the burn aura's always, the others' while selected.</summary>
        private readonly List<Decal> auraRings = new List<Decal>();

        /// <summary>The aura row of the ability table each entity's roster entry holds, or -1; parallel to bodies.</summary>
        private readonly List<int> auraRows = new List<int>();

        // The chain shooters that fired this frame and when their tracer lands,
        // in pairs. Refilled every frame, so nothing allocates once they are big
        // enough.
        private readonly List<int> chained = new List<int>();
        private readonly List<float> chainLands = new List<float>();

        /// <summary>The ids one chain's guess has already used, first target first.</summary>
        private readonly int[] hops = new int[FactionData.MaxChainHops + 1];

        /// <summary>Unscaled time each entity's flash goes dark. -1 means never armed; parallel to bodies.</summary>
        private readonly List<float> hitUntil = new List<float>();

        /// <summary>A flier's blob shadow on the ground, parallel to bodies; off for everything that walks.</summary>
        private readonly List<Decal> shadows = new List<Decal>();

        /// <summary>One highlight, moved to whatever the cursor is over.</summary>
        private Decal hover;

        // Meters, parallel to bodies. Billboards over the head, never hidden by a hill.
        private readonly List<Billboard> barBacks = new List<Billboard>();
        private readonly List<Billboard> barFills = new List<Billboard>();
        private readonly List<Billboard> queueFills = new List<Billboard>();

        // Rally markers are a pool, not one per entity: only the selected buildings
        // that have set one are ever drawn.
        private readonly List<MeshRenderer> rallyMarkers = new List<MeshRenderer>();

        // One rubble pile per blocked cell, pooled and re-aimed every frame. Not
        // indexed by anything the simulation owns, so a restart needs no reset:
        // the tail is switched off every frame anyway.
        private readonly List<BodyRig> remnants = new List<BodyRig>();

        /// <summary>The building under the cursor during placement. Rebuilt when the role changes.</summary>
        private BodyRig ghost;
        private Role ghostRole = Role.None;
        private Faction ghostFaction;

        // 차원 유랑종 통로 조준 (#139). Three marks, all of them the local peer's own:
        // the point the player has named, the point under the cursor while they are
        // naming one, and a hairline circle on every body lending them a radius.
        private Decal arrivalMark;
        private Decal arrivalCursor;
        private readonly List<Decal> arrivalRings = new List<Decal>();

        /// <summary>Refilled every frame the aim is armed, so nothing here allocates per frame.</summary>
        private readonly List<int> anchors = new List<int>();

        /// <summary>One square a water field cell, pooled like the debris piles (issue #212).</summary>
        private readonly List<Decal> fieldCells = new List<Decal>();

        /// <summary>Delayed strikes' warning rings and zones' edges, handed out afresh every frame (issue #212).</summary>
        private readonly List<Decal> zoneRings = new List<Decal>();
        private int zoneRingsUsed;

        // A summon's lifetime meter, parallel to bodies; made the first time a
        // body with a lifetime is drawn (issue #212).
        private readonly List<Billboard> lifeBacks = new List<Billboard>();
        private readonly List<Billboard> lifeFills = new List<Billboard>();

        /// <summary>The one row with state each entity's roster entry holds, or -1; parallel to bodies (issue #212).</summary>
        private readonly List<int> castRows = new List<int>();

        /// <summary>Whether each entity's hp rose as a heal this frame, parallel to bodies: what a 잔해 수리's guess looks for.</summary>
        private readonly List<bool> mended = new List<bool>();

        /// <summary>The 잔해 수리 casters seen to go off this frame, handled once every body's heal is known.</summary>
        private readonly List<int> menders = new List<int>();

        /// <summary>Whether each cell held debris as of the last frame (issue #212).</summary>
        private readonly bool[] hadRemnant = new bool[World.GridCells];

        /// <summary>
        /// Debris cells taken this frame: standing last frame, and written to 0
        /// now, which is what ConsumeRemnant writes and a lapse never does. What
        /// a throw or a repair is thrown from; each is used once.
        /// </summary>
        private readonly List<int> taken = new List<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft View").AddComponent<MatchView>();

        private void Awake()
        {
            // Only in play mode: ViewShots builds a view in the editor, where
            // DontDestroyOnLoad throws.
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);

            // A perspective camera; the rig puts it where it goes. Built here
            // rather than in a scene so that no gameplay change ever has to touch
            // a scene asset, which is the rule the whole view runs on.
            Cam = new GameObject("Camera").AddComponent<Camera>();
            Cam.tag = "MainCamera";
            Cam.orthographic = false;
            Cam.fieldOfView = CameraRig.FieldOfView;
            Cam.nearClipPlane = 0.5f;
            Cam.farClipPlane = 400f;
            if (Application.isPlaying) DontDestroyOnLoad(Cam.gameObject);
            Sky.Build(Cam);

            hover = Decal.Make(transform, "Hover", ring: true, UiStyle.HoverRing);
            effects = new Effects(transform);
        }

        private void Start() => runner = MatchRunner.Instance;

        /// <summary>
        /// A view over <paramref name="over"/> built outside play mode, for the
        /// screenshot harness: the editor never calls Awake, Start or LateUpdate
        /// on a component added from a batch method, so this does the first two
        /// and <see cref="Draw"/> stands in for the third. The game never calls
        /// either; it boots its view from <see cref="Boot"/>.
        /// </summary>
        public static MatchView Detached(MatchRunner over)
        {
            var view = new GameObject("WordCraft View (detached)").AddComponent<MatchView>();
            view.Awake();
            view.runner = over;
            return view;
        }

        /// <summary>One frame of the view, exactly as LateUpdate draws it. For <see cref="Detached"/> views.</summary>
        public void Draw() => LateUpdate();

        /// <summary>
        /// LateUpdate, so the interpolated draw position is read after the frame's
        /// ticks have run rather than one frame behind them, and after the camera
        /// rig has moved, so the meters face where the camera is this frame.
        /// </summary>
        private void LateUpdate()
        {
            if (runner == null) return;
            World world = runner.World;

            Sky.Update(Cam);

            // Every pool is indexed by entity id. A restart hands out the same ids
            // to different entities, so keeping the old bodies would draw the new
            // match with the previous one's models and leave its dead standing.
            if (!ReferenceEquals(world, shown))
            {
                DropBodies(bodies);
                Drop(rings);
                Drop(rangeRings);
                Drop(hitFlashes);
                Drop(shadows);
                Drop(slowRings);
                Drop(auraRings);
                Drop(barBacks);
                Drop(barFills);
                Drop(queueFills);
                Drop(lifeBacks);
                Drop(lifeFills);
                castRows.Clear();   // parallel to bodies, and Create refills it
                mended.Clear();     // same
                System.Array.Clear(hadRemnant, 0, hadRemnant.Length);
                skins.Clear();      // parallel to bodies, and Create refills it
                cooldowns.Clear();  // same
                hitUntil.Clear();   // same: a restart's ids owe nothing to the match before it
                alive.Clear();      // same
                queues.Clear();     // same
                flashOnLanding.Clear(); // same
                vitals.Clear();     // same
                dropped.Clear();    // same
                auraRows.Clear();   // same
                DropBodies(retiring);
                effects.Clear();
                primed = false;
                shown = world;

                // Terrain is fixed for the whole match, so the field is shaped and
                // meshed here and never touched again.
                Field.Build(world);
                FieldMesh.Build(world, transform);
            }

            born.Clear();
            for (int i = bodies.Count; i < world.EntityCount; i++)
            {
                Entity made = world.GetEntity(i);
                BodyRig body = Create(world, made);
                bodies.Add(body);
                if (primed && made.Kind != EntityKind.Building && made.Kind != EntityKind.ResourceNode)
                {
                    body.Spawn();
                    born.Add(i);
                }
            }
            primed = true;
            Retire();

            // Stamped once here rather than read again per entity below: this is
            // the same list HitDetection hands the minimap and the alert, so a
            // hit already lit up off-screen feedback does not walk World a second
            // time to also light up on-screen feedback (issue #104).
            IReadOnlyList<int> hits = HitDetection.HitIds(world, runner.LocalPeer);
            struck.Clear();
            for (int h = 0; h < hits.Count; h++) struck.Add(hits[h]);
            fallen.Clear();
            chained.Clear();
            chainLands.Clear();
            menders.Clear();
            TakenRemnants(world);
            effects.BeginOrbits();
            zoneRingsUsed = 0;

            Fog.Update(runner);

            float band = Band();
            int markers = 0;
            for (int i = 0; i < world.EntityCount; i++)
            {
                Entity e = world.GetEntity(i);

                // Before the fog tests below, so a 꼬마돌 taken out of sight is
                // already a Towerback the first frame it is seen again.
                if (skins[i] != SkinOf(e))
                {
                    // A body that changes what it is in sight — a growth landing,
                    // a 꼬마돌 taken — swells once and the new body comes up in
                    // its place. Out of sight it is simply swapped.
                    BodyRig old = bodies[i];
                    bodies[i] = Create(world, e);
                    skins[i] = SkinOf(e);
                    auraRows[i] = AuraRow(world, e);
                    castRows[i] = CastRow(world, e);
                    // A new body is a new reading: what the old one had is no
                    // heal and no burn of this one.
                    vitals[i] = VitalOf(e, default);
                    if (e.Alive && old.Shown && !old.Remembered)
                    {
                        old.Swell();
                        retiring.Add(old);
                        bodies[i].Spawn(BodyRig.SwellSeconds);
                    }
                    else
                    {
                        Destroy(old.gameObject);
                    }
                }

                BodyRig body = bodies[i];

                // Before the fog, which hides anything dead: a death seen live
                // plays out where it fell, then the body switches itself off and
                // the fog's Hidden below takes over. One that happened in the
                // fog, or to a remembered building, is not news and plays nothing.
                //
                // The death itself is given after the loop, once every shot of
                // the frame has been fired, so a body killed by a tracer that
                // leaves this frame waits for it whatever order the ids run in.
                bool was = alive[i];
                alive[i] = e.Alive;
                bool falls = was && !e.Alive && body.Shown && !body.Remembered;
                if (falls) fallen.Add(i);

                // Every entity, seen or not, so a cooldown that reset in the fog
                // is not taken for a fresh shot the frame the shooter comes into
                // sight. Pose only compares.
                bool fired = e.AttackCooldown > cooldowns[i];
                cooldowns[i] = e.AttackCooldown;

                // The same for a queue: read for every entity, so one that ran
                // down out of sight is not taken for a delivery later.
                bool delivered = e.QueueCount < queues[i];
                queues[i] = e.QueueCount;

                // And hp and the two status clocks (issue #206). Hp that rose
                // with MaxHp where it was is a heal: a growth raises both, and
                // a site's hp climbs with its construction, so neither counts.
                // A burn clock that ran past a multiple of World.BurnPeriod is
                // a burn's tick, StatusSystem's own test.
                Vital last = vitals[i];
                vitals[i] = VitalOf(e, last);
                dropped[i] = e.Hp < last.Hp;
                bool healed = e.Alive && e.Hp > last.Hp && e.MaxHp == last.MaxHp && last.Site == 0 && e.BuildTicksLeft == 0;
                bool scorched = e.Alive && e.BurnTicksLeft < last.Burn && BurnPeriods(last.Burn) > BurnPeriods(e.BurnTicksLeft);
                mended[i] = healed;

                // Every entity, seen or not: a caster in the fog can still drop
                // a meteor where the player can see (issue #212).
                Casting(world, i, e, last);

                // A dead body that is not playing its death has nothing left to
                // move; switched off, so a long match's dead cost no Update.
                if (!e.Alive && !falls && !body.Dying && body.enabled) body.enabled = false;
                if (!e.Alive && (falls || body.Dying))
                {
                    rings[i].Visible = false;
                    rangeRings[i].Visible = false;
                    hitFlashes[i].Visible = false;
                    shadows[i].Visible = false;
                    StatusesOff(i, body);
                    Overlays(i);
                    continue;
                }
                byte show = Fog.Show(i, e);
                if (show == Fog.Hidden)
                {
                    body.Show(false);
                    rings[i].Visible = false;
                    rangeRings[i].Visible = false;
                    hitFlashes[i].Visible = false;
                    shadows[i].Visible = false;
                    StatusesOff(i, body);
                    Overlays(i);
                    continue;
                }

                // Remembered: the body is left exactly as the last frame that could
                // see it left it, which is what "last known state" means for a
                // thing that cannot move. Nothing is read off the entity here, so a
                // building razed out of sight goes on standing and one raised out of
                // sight does not appear. Its meters go, because hp is not remembered.
                if (show == Fog.Memory)
                {
                    body.Dim(true);
                    rings[i].Visible = false;
                    rangeRings[i].Visible = false;
                    hitFlashes[i].Visible = false;
                    StatusesOff(i, body);
                    Overlays(i);
                    continue;
                }

                bool picked = Selection.Instance != null && Selection.Instance.Contains(i);
                body.Show(true);
                body.Dim(false);

                Vector2 p = runner.DrawPosition(i);
                body.Place(Field.At(p));
                Pose(world, i, e, body, fired);
                if (delivered) Deliver(world, e, p, body);
                Statuses(world, i, e, p, body, picked, healed, scorched);
                Summoned(world, e, body);
                Deployment(world, i, e, p, body, last);

                shadows[i].Visible = body.Flying;
                if (body.Flying) shadows[i].Set(p, body.Footprint * ShadowRatio, 0f);

                rings[i].Visible = picked;
                if (picked) rings[i].Set(p, body.Footprint * 0.5f + band, band);

                // "이게 저걸 때릴 수 있나" is a question the simulation already
                // answers; this only ever reads it, never a re-typed number of its
                // own, so the ring can never say something the shot does not. It is
                // World.RangeFor and not the roster's Range: a researched range
                // upgrade moves the reach the shot is tested against, and a ring
                // left at the base number would draw a boundary the unit hits past
                // (issues #104, #182).
                if (picked && world.Armed(e))
                {
                    RangeRing(rangeRings[i], Reach(world, i, e), p);
                }
                else
                {
                    rangeRings[i].Visible = false;
                }

                bool flashing = hitUntil[i] > ViewClock.Now;
                hitFlashes[i].Visible = flashing;
                if (flashing)
                {
                    hitFlashes[i].Set(p, body.Footprint * 0.5f + band, band);
                    Color flash = UiStyle.Danger;
                    flash.a = Mathf.Clamp01((hitUntil[i] - ViewClock.Now) / HitFlashSeconds);
                    hitFlashes[i].Colour = flash;
                }

                Overlays(world, i, e, p, body, picked);

                if (picked && e.Kind == EntityKind.Building && e.HasRallyPoint)
                {
                    Rally(markers++, MatchRunner.ToView(e.RallyPoint));
                }
            }

            for (int i = markers; i < rallyMarkers.Count; i++) rallyMarkers[i].enabled = false;
            Chains(world);
            Arrivals(world);
            Mends(world);
            effects.EndOrbits();
            for (int z = zoneRingsUsed; z < zoneRings.Count; z++) zoneRings[z].Visible = false;
            Consequences(world);
            Remnants(world);
            WaterFields(world);
            Hover(world);
            Ghost(world);
            Arrival(world);
        }

        /// <summary>
        /// What the body is doing, read off the entity and told to the model. A
        /// swing when the cooldown resets, a turn toward the target while it
        /// stands and shoots, and for a site the fraction built. All of it is a
        /// reading of state the simulation already holds; nothing here is a
        /// second clock.
        /// </summary>
        private void Pose(World world, int i, Entity e, BodyRig body, bool fired)
        {
            if (fired)
            {
                body.Attack();
                float lands = Shoot(world, e, body);
                if (lands >= 0f && ChainRow(world, e) >= 0)
                {
                    chained.Add(i);
                    chainLands.Add(lands);
                }
            }

            if (e.TargetId >= 0 && e.TargetId < world.EntityCount && e.Target.Equals(e.Position))
            {
                Entity target = world.GetEntity(e.TargetId);
                if (target.Alive)
                {
                    Vector2 to = runner.DrawPosition(e.TargetId) - runner.DrawPosition(i);
                    if (to.sqrMagnitude > 0.0001f) body.Face(Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
                }
            }

            if (e.Kind != EntityKind.Building) return;

            // A site under construction is put together part by part as it is
            // built, and reads as a ghost until it finishes — the same SiteAlpha
            // idiom the sprite had, now carried by the model.
            if (e.BuildTicksLeft > 0 && e.Owner >= 0)
            {
                int ticks = FactionData.BuildTicks(world.FactionOf(e.Owner), e.Role, e.Slot);
                body.Progress(ticks > 0 ? 1f - e.BuildTicksLeft / (float)ticks : 1f);
                body.Work(false);
            }
            else
            {
                body.Progress(1f);
                // At work: a queue holds something or a level is being researched.
                body.Work(e.QueueCount > 0 || e.ResearchIndex >= 0);
            }
        }

        /// <summary>
        /// Cells past a building's edge a unit it delivered can stand: a produced
        /// unit stands up at the building's position plus World.RallyOffset, two
        /// cells out, so anything further away came out of somewhere else.
        /// </summary>
        private const float DoorReach = 2f;

        /// <summary>
        /// A building's queue went down this frame. If a unit of the same owner
        /// appeared at its door, it came out of here: the building leans toward
        /// the nearest such unit (issue #186). Only at its door, so a queue that
        /// went down because it was cancelled does not lean toward a unit another
        /// building delivered on the same frame. A 차원 유랑종 통로 that sends its
        /// unit to a far arrival point does not lean at all.
        /// </summary>
        private void Deliver(World world, Entity e, Vector2 p, BodyRig body)
        {
            if (e.Kind != EntityKind.Building) return;
            float reach = Cells(e.Role) * 0.5f + DoorReach;
            float nearest = reach * reach;
            Vector2 toward = Vector2.zero;
            for (int b = 0; b < born.Count; b++)
            {
                int id = born[b];
                if (world.GetEntity(id).Owner != e.Owner) continue;
                Vector2 to = runner.DrawPosition(id) - p;
                if (to.sqrMagnitude >= nearest) continue;
                nearest = to.sqrMagnitude;
                toward = to;
            }
            if (toward.sqrMagnitude < 1e-6f) return;
            body.Deliver(Mathf.Atan2(toward.x, toward.y) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// What the shot that just reset this cooldown looks like in the air. A
        /// weapon that reaches past <see cref="TracerReach"/> throws a tracer from
        /// the shooter to the target once the shooter's wind-up is over, bursts
        /// where it lands, and the target flinches on the arrival. Anything
        /// shorter is a blow: the target flinches now and the burst comes when
        /// the lunge lands. The target is read off the entity on the tick it
        /// fired, when TargetId still names the body it hit even if that body
        /// died of it.
        ///
        /// Drawn after the damage, not before: the simulation's hit is instant,
        /// so a tracer lands up to a third of a second after the bar has already
        /// moved. That is the view's delay only; nothing here touches the world.
        /// The delayed flinch replaces the one HitDetection gave the target
        /// earlier in this frame, because Hit keeps only the latest start.
        ///
        /// Returns the seconds until the shot lands where the player can see it,
        /// or -1 when nothing lands in sight: what a chain's jump waits for.
        /// </summary>
        private float Shoot(World world, Entity e, BodyRig body)
        {
            if (e.Owner < 0 || e.TargetId < 0 || e.TargetId >= bodies.Count) return -1f;

            Faction faction = world.FactionOf(e.Owner);
            Color colour = BodyPalette.Shot(faction);
            BodyRig victim = bodies[e.TargetId];
            Vector2 from = runner.DrawPosition(e.Id), to = runner.DrawPosition(e.TargetId);

            // On the near face of the target rather than its middle, so the
            // shards burst out of the side the shot came from and are not
            // swallowed by the body they hit.
            Vector2 toward = from - to;
            if (toward.sqrMagnitude > 1e-6f) to += toward.normalized * (victim.Footprint * 0.35f);
            Vector3 hit = Field.At(to, victim.ChestLift + (victim.Flying ? BodyRig.HoverHeight : 0f));

            // Asked of a copy marked alive, because the shot that killed it is
            // still a shot at something the player could see. The copy is the
            // view's own; the world is not written.
            Entity target = world.GetEntity(e.TargetId);
            target.Alive = true;
            bool seen = Fog.Show(e.TargetId, target) == Fog.Seen;

            if (Throws(world.RangeFor(e)))
            {
                // A tower kicks the instant it fires and has no wind-up to wait for.
                float leave = e.Kind == EntityKind.Building ? 0f : BodyRig.StrikeDelay;
                if (seen) return Throw(world, e, body, hit, colour, leave);

                // Into the fog: the tracer leaves, and is gone before it
                // could say where the hidden body stands. No burst, no
                // ring, no landing.
                Vector3 muzzle = body.Muzzle;
                effects.Shot(muzzle, Vector3.Lerp(muzzle, hit, HiddenShotReach), colour, leave, -1, bursts: false);
                return -1f;
            }

            victim.Hit();
            if (!seen) return -1f;
            effects.Impact(hit, colour, BodyRig.LungeLandDelay);
            return BodyRig.LungeLandDelay;
        }

        /// <summary>How far toward a target hidden in the fog its tracer is drawn before it vanishes.</summary>
        private const float HiddenShotReach = 0.6f;

        /// <summary>
        /// A seen shot's tracer (issue #212). 잔해 투척: a lump of the debris the
        /// 바위 술사 took this tick flies from its cell instead, and lands with
        /// the splash's ring. 용암 폭발, 폭격 and a deployed 견인 대포 lob theirs
        /// up and down onto the target, landing with their splash's ring. Every
        /// other weapon fires straight.
        /// </summary>
        private float Throw(World world, Entity e, BodyRig body, Vector3 hit, Color colour, float leave)
        {
            int row = UnlockedRow(world, e, AbilityKind.RemnantThrow);
            if (row >= 0)
            {
                int debris = TakeRemnant(MatchRunner.ToView(e.Position));
                if (debris >= 0)
                {
                    Vector3 pile = Field.At(MatchRunner.ToView(World.CellCenter(debris)), 0.35f);
                    float splash = world.AbilityRadius(e.Owner, FactionData.AbilityRowAt(row));
                    return effects.Lob(pile, hit, UiStyle.Remnant, leave, e.TargetId, splash, chunk: true);
                }
            }

            row = UnlockedRow(world, e, AbilityKind.SplashOnHit);
            if (row < 0 && e.Deployed) row = UnlockedRow(world, e, AbilityKind.Deploy);
            if (row >= 0)
            {
                float splash = world.AbilityRadius(e.Owner, FactionData.AbilityRowAt(row));
                return effects.Lob(body.Muzzle, hit, colour, leave, e.TargetId, splash);
            }
            return effects.Shot(body.Muzzle, hit, colour, leave, e.TargetId);
        }

        /// <summary>The row of this kind this body's entry holds when its owner has unlocked it, or -1.</summary>
        private static int UnlockedRow(World world, Entity e, AbilityKind kind)
        {
            if (e.Owner < 0) return -1;
            int row = FactionData.AbilityOf(world.FactionOf(e.Owner), e.Role, e.Slot, kind);
            return row >= 0 && world.AbilityUnlocked(e.Owner, FactionData.AbilityRowAt(row)) ? row : -1;
        }

        /// <summary>
        /// The debris cells taken since the last frame, into <see cref="taken"/>,
        /// and this frame's debris remembered for the next.
        /// </summary>
        private void TakenRemnants(World world)
        {
            taken.Clear();
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                bool has = world.HasRemnant(cell);
                if (hadRemnant[cell] && !has && world.RemnantExpiry(cell) == 0) taken.Add(cell);
                hadRemnant[cell] = has;
            }
        }

        /// <summary>
        /// The taken debris cell nearest <paramref name="from"/> within the reach
        /// 잔해 투척 and 잔해 수리 take it from, removed so it is thrown once; -1
        /// when there is none.
        /// </summary>
        private int TakeRemnant(Vector2 from)
        {
            float reach = World.RemnantUseReach.Raw / (float)Fix.One + RemnantSlack;
            float best = reach * reach;
            int found = -1;
            for (int k = 0; k < taken.Count; k++)
            {
                float d = (MatchRunner.ToView(World.CellCenter(taken[k])) - from).sqrMagnitude;
                if (d > best) continue;
                best = d;
                found = k;
            }
            if (found < 0) return -1;
            int cell = taken[found];
            taken[found] = taken[taken.Count - 1];
            taken.RemoveAt(taken.Count - 1);
            return cell;
        }

        // ------------------------------------------------------------------
        // 한시 소환·폭발·광역·전개 (issue #212)
        // ------------------------------------------------------------------

        /// <summary>
        /// What a caster's clock, point and cooldown say this frame, read for
        /// every entity whether seen or not, since what it says is drawn on the
        /// point and not on the caster: a delayed strike's warning and landing,
        /// a zone's chips, a tide's landing, and a 잔해 수리 going off.
        /// </summary>
        private void Casting(World world, int i, Entity e, Vital last)
        {
            int row = castRows[i];
            if (row < 0 || e.Owner < 0) return;
            AbilityRow r = FactionData.AbilityRowAt(row);
            switch (r.Kind)
            {
                case AbilityKind.DelayedStrike: DelayedStrike(world, e, r, last); break;
                case AbilityKind.StrikeZone:
                case AbilityKind.SandZone: Zone(world, e, r, last); break;
                case AbilityKind.TideStrike: Tide(world, e, r, last); break;
                case AbilityKind.RemnantRepair:
                    if (e.Alive && e.AbilityCooldown > last.Cooldown) menders.Add(i);
                    break;
            }
        }

        /// <summary>
        /// 유성우, 낙뢰, 간헐천: a ring on the point while the clock runs, filling
        /// in as the landing nears, and the strike itself on the frame the clock
        /// reaches 0. A clock that reached 0 because its caster died never lands:
        /// Kill clears it, and only a caster still standing, or one whose last
        /// tick it was, is read as a landing.
        /// </summary>
        private void DelayedStrike(World world, Entity e, AbilityRow r, Vital last)
        {
            if (!Shows(e.Owner, e.AbilityPoint)) return;
            Faction faction = world.FactionOf(e.Owner);
            Vector2 point = MatchRunner.ToView(e.AbilityPoint);
            float radius = world.AbilityRadius(e.Owner, r);
            Color colour = StrikeColour(faction);

            if (e.Alive && e.AbilityTicksLeft > 0)
            {
                float left = e.AbilityTicksLeft / (float)Mathf.Max(world.AbilityTicks(e.Owner, r), 1);
                Edge(point, radius, WithAlpha(colour, Mathf.Lerp(WarnAlphaLast, WarnAlphaFirst, left)));
            }

            // Dropped DropLead ticks early, timed to land on the tick the burst
            // does, so the stones and the hp bars they empty arrive together. A
            // frame that jumped past the lead straight onto the landing drops
            // it at once. The cost: a caster killed in those last ticks takes its
            // strike with it, and the stones already falling land on nothing.
            int clock = e.AbilityTicksLeft;
            if (e.Alive && clock > 0 && clock <= DropLead && last.Clock > DropLead)
            {
                Strike(faction, point, radius, colour, clock / (float)World.TicksPerSecond);
            }
            else if (last.Clock > DropLead && clock == 0 && e.Alive)
            {
                Strike(faction, point, radius, colour, ShortDrop);
            }
        }

        /// <summary>How many ticks before a delayed strike lands its stones start falling: the fall's 0.3 s.</summary>
        private const int DropLead = 6;

        /// <summary>The fall of a strike whose lead the frame skipped: short, since the burst has already landed.</summary>
        private const float ShortDrop = 0.12f;

        /// <summary>
        /// A delayed strike landing on <paramref name="point"/>. 유성우: three
        /// stones falling from the sky a moment apart. 낙뢰: a bolt down from it.
        /// 간헐천: a spout up out of the ground. Each bursts at 1.5 a weapon's
        /// size with a ring the strike's radius.
        /// </summary>
        private void Strike(Faction faction, Vector2 point, float radius, Color colour, float arrive)
        {
            Vector3 ground = Field.At(point);
            switch (faction)
            {
                case Faction.Humans:
                    // The bolt that fell, standing the moment it lands.
                    effects.Fall(Field.At(point, 0.3f), DropHeight, colour, 0f, arrive, BlastImpact, radius);
                    effects.Jump(ground + Vector3.up * DropHeight, ground, colour, arrive);
                    return;
                case Faction.WaterSlimes:
                    effects.Fall(ground, -SpoutHeight, BodyPalette.WaterSlimes.Pale, arrive, DropFlight, 1f, 0f, bursts: false);
                    effects.Impact(ground + Vector3.up * 0.3f, colour, arrive, BlastImpact);
                    effects.Blast(ground, radius, colour, arrive);
                    return;
                default:
                    // Two smaller stones either side a moment ahead, then the main one on the point.
                    for (int k = 0; k < 2; k++)
                    {
                        float around = (k == 0 ? 40f : 220f) * Mathf.Deg2Rad;
                        Vector2 off = new Vector2(Mathf.Cos(around), Mathf.Sin(around)) * (radius * 0.5f);
                        effects.Fall(Field.At(point + off, 0.2f), DropHeight, colour, 0f, arrive * (0.7f + 0.15f * k), 1f, 0f);
                    }
                    effects.Fall(Field.At(point, 0.3f), DropHeight, colour, 0f, arrive, BlastImpact, radius);
                    return;
            }
        }

        /// <summary>
        /// 칼바람 회오리 and 모래폭풍: chips circling the point and a faint edge
        /// for as long as the clock runs, and for 칼바람 회오리 a ring on each
        /// tick it hits (a multiple of its period, the clock counted down onto it).
        /// </summary>
        private void Zone(World world, Entity e, AbilityRow r, Vital last)
        {
            if (!e.Alive || !Shows(e.Owner, e.AbilityPoint)) return;
            bool sand = r.Kind == AbilityKind.SandZone;
            // Darker than the shades the two factions are drawn in, so a chip
            // reads against lit grass: 모래폭풍 an ochre, 칼바람 회오리 a deep teal.
            Color colour = sand ? BodyPalette.RockGolems.Waking : BodyPalette.TreeSpirits.WindDeep;
            Vector2 point = MatchRunner.ToView(e.AbilityPoint);
            float radius = world.AbilityRadius(e.Owner, r);

            if (e.AbilityTicksLeft > 0)
            {
                effects.Orbit(point, radius, colour, sand ? SandChips : GaleChips, sand ? SandTurn : GaleTurn, e.Id * 37f);
                Edge(point, radius, WithAlpha(colour, ZoneAlpha));
            }

            if (!sand && r.Period > 0 && ZoneHit(last.Clock, e.AbilityTicksLeft, r.Period))
            {
                effects.Blast(Field.At(point), radius, BodyPalette.TreeSpirits.WindLight, 0f, GaleHitAlpha);
            }
        }

        /// <summary>Whether a zone clock that ran from <paramref name="was"/> down to <paramref name="now"/> passed a tick it hits on: a multiple of the period, 0 included.</summary>
        private static bool ZoneHit(int was, int now, int period) =>
            was > now && (was - 1) / period * period >= now;

        /// <summary>
        /// 해일 부름 lands in Apply and leaves neither clock nor point, so the
        /// view finds it from what it does leave: the caster's cooldown jumps,
        /// and the 3×3 field it laid lapses on the tick of the cast plus the
        /// row's ticks. The middle of the cells with exactly that expiry, within
        /// the cast's range of the caster, is the point. A 물 지대 깔기 cast on
        /// the same tick close by would pull it off centre.
        /// </summary>
        private void Tide(World world, Entity e, AbilityRow r, Vital last)
        {
            if (!e.Alive || e.AbilityCooldown <= last.Cooldown) return;
            // The cooldown is set to the row's on the cast tick and taken down
            // once on that same tick, so the cast tick is this far back.
            int cast = world.Tick - r.Cooldown + e.AbilityCooldown;
            int expiry = cast + world.AbilityTicks(e.Owner, r);
            int centre = World.CellOf(e.Position);
            int cx = centre % World.GridSize, cy = centre / World.GridSize;
            int reach = r.CastRange + 2;
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int y = Mathf.Max(cy - reach, 0); y <= Mathf.Min(cy + reach, World.GridSize - 1); y++)
            {
                for (int x = Mathf.Max(cx - reach, 0); x <= Mathf.Min(cx + reach, World.GridSize - 1); x++)
                {
                    int cell = y * World.GridSize + x;
                    if (world.WaterFieldExpiry(cell) != expiry) continue;
                    sum += MatchRunner.ToView(World.CellCenter(cell));
                    count++;
                }
            }
            if (count == 0) return;
            Vector2 point = sum / count;
            if (!Shows(e.Owner, MatchRunner.ToSim(point))) return;
            Color colour = StrikeColour(Faction.WaterSlimes);
            Vector3 ground = Field.At(point);
            effects.Impact(ground + Vector3.up * 0.3f, colour, 0f, BlastImpact);
            effects.Blast(ground, world.AbilityRadius(e.Owner, r), colour);
        }

        /// <summary>
        /// 잔해 수리, once every body's heal this frame is known: a lump of the
        /// debris it took flies from its cell to the ally that was mended, the
        /// one nearest the caster among those whose hp rose. The heal's own mark
        /// is issue #206's and is already drawn. Only between seen ends.
        /// </summary>
        private void Mends(World world)
        {
            for (int m = 0; m < menders.Count; m++)
            {
                int id = menders[m];
                Entity caster = world.GetEntity(id);
                Vector2 at = MatchRunner.ToView(caster.Position);
                int debris = TakeRemnant(at);
                if (debris < 0) continue;

                int row = castRows[id];
                float reach = world.AbilityRadius(caster.Owner, FactionData.AbilityRowAt(row)) + MendSlack;
                float best = reach * reach;
                int ally = -1;
                for (int j = 0; j < world.EntityCount; j++)
                {
                    if (!mended[j]) continue;
                    Entity v = world.GetEntity(j);
                    if (v.Owner != caster.Owner) continue;
                    float d = (MatchRunner.ToView(v.Position) - at).sqrMagnitude;
                    if (d > best) continue;
                    best = d;
                    ally = j;
                }
                if (ally < 0 || !Seen(world, ally)) continue;
                Vector3 pile = Field.At(MatchRunner.ToView(World.CellCenter(debris)), 0.35f);
                effects.Lob(pile, Chest(ally), UiStyle.Remnant, 0f, -1, 0f, chunk: true, bursts: false);
            }
        }

        /// <summary>
        /// 도착 방전: a body that came off a queue this frame and holds an
        /// ArrivalBurst row goes off where it stood up, if it is seen.
        /// </summary>
        private void Arrivals(World world)
        {
            for (int b = 0; b < born.Count; b++)
            {
                int id = born[b];
                if (Seen(world, id)) Burst(world, world.GetEntity(id), runner.DrawPosition(id), AbilityKind.ArrivalBurst, 0f);
            }
        }

        /// <summary>
        /// 전격 과부하, 화염 분출, 도착 방전: when the body's entry holds a row
        /// of <paramref name="kind"/>, one ring out to the row's radius and a
        /// burst 1.5 a weapon's size, in the faction's shot colour, after
        /// <paramref name="delay"/> seconds.
        /// </summary>
        private void Burst(World world, Entity e, Vector2 at, AbilityKind kind, float delay)
        {
            int row = UnlockedRow(world, e, kind);
            if (row < 0) return;
            Color colour = BodyPalette.Shot(world.FactionOf(e.Owner));
            Vector3 ground = Field.At(at);
            effects.Impact(ground + Vector3.up * 0.4f, colour, delay, BlastImpact);
            effects.Blast(ground, world.AbilityRadius(e.Owner, FactionData.AbilityRowAt(row)), colour, delay);
        }

        /// <summary>A summon's last ticks: it blinks.</summary>
        private static void Summoned(World world, Entity e, BodyRig body) =>
            body.Expiring(e.ExpireTick > 0 && e.ExpireTick - world.Tick <= ExpiryBlinkTicks);

        /// <summary>
        /// 전개 on a seen 견인 대포: how far into the transition it is, told to
        /// the body, and a ring of dust on the frame it finishes deploying.
        /// </summary>
        private void Deployment(World world, int i, Entity e, Vector2 p, BodyRig body, Vital last)
        {
            if (castRows[i] < 0 || FactionData.AbilityRowAt(castRows[i]).Kind != AbilityKind.Deploy) return;
            body.Deploy(Settle(world, e, FactionData.AbilityRowAt(castRows[i])));
            if (e.Deployed && !last.Deployed)
            {
                effects.Dust(Field.At(p), body.Footprint * 0.5f, BodyPalette.Lighter(BodyPalette.Main(world.FactionOf(e.Owner)), 0.5f));
            }
        }

        /// <summary>
        /// How far into 전개 a body is, 0 ready to walk to 1 deployed. Mid
        /// transition Deployed still holds where it started, and the clock counts
        /// the ticks left of the row's.
        /// </summary>
        private static float Settle(World world, Entity e, AbilityRow r)
        {
            float to = e.Deployed ? 1f : 0f;
            if (e.AbilityTicksLeft <= 0) return to;
            float left = e.AbilityTicksLeft / (float)Mathf.Max(world.AbilityTicks(e.Owner, r), 1);
            return Mathf.Lerp(1f - to, to, left);
        }

        /// <summary>
        /// The radius the selected body's reach ring is drawn at: World.RangeFor,
        /// except for a 견인 대포 in transition, whose ring grows from the reach it
        /// has standing to the one it has deployed (or shrinks back) as the
        /// transition runs. Both ends are asked of copies of the entity; the
        /// world is not written.
        /// </summary>
        private float Reach(World world, int i, Entity e)
        {
            float range = world.RangeFor(e).Raw / (float)Fix.One;
            int row = castRows[i];
            if (row < 0 || e.AbilityTicksLeft <= 0) return range;
            AbilityRow r = FactionData.AbilityRowAt(row);
            if (r.Kind != AbilityKind.Deploy) return range;
            Entity standing = e, deployed = e;
            standing.Deployed = false;
            deployed.Deployed = true;
            return Mathf.Lerp(world.RangeFor(standing).Raw / (float)Fix.One, world.RangeFor(deployed).Raw / (float)Fix.One,
                              Settle(world, e, r));
        }

        /// <summary>The one row with state this entry holds, or -1: what <see cref="Casting"/> reads the clock for.</summary>
        private static int CastRow(World world, Entity e) =>
            e.Owner < 0 ? -1 : FactionData.StatefulAbilityOf(world.FactionOf(e.Owner), e.Role, e.Slot);

        /// <summary>Whether a mark on <paramref name="point"/> may be drawn: the local peer's own, or a point it can see.</summary>
        private bool Shows(int owner, FixVec2 point) => owner == runner.LocalPeer || Fog.At(point) == Fog.Seen;

        /// <summary>A pooled ring round a point for this frame: a strike's warning, a zone's edge.</summary>
        private void Edge(Vector2 point, float radius, Color colour)
        {
            if (zoneRingsUsed >= zoneRings.Count) zoneRings.Add(Decal.Make(transform, "ZoneRing", ring: true, colour));
            Decal d = zoneRings[zoneRingsUsed++];
            d.Set(point, radius, Mathf.Max(radius * WarnBandRatio, RingBandFloor));
            d.Colour = colour;
            d.Visible = true;
        }

        /// <summary>
        /// 물 슬라임 물 지대: one translucent square on every field cell the local
        /// peer can see, 물 슬라임's Pale, fading over its last
        /// <see cref="FieldFadeTicks"/>. Pooled and not indexed by anything the
        /// simulation owns, as the debris piles are, so a restart needs no reset.
        /// Only cells that are seen: a mark is unlit and the fog does not cover
        /// it, so a field drawn in the fog would show where the enemy stands.
        /// </summary>
        // ponytail: the same 4096-cell scan Remnants does. Ask the simulation for
        // a list of live cells the day a profile disagrees.
        private void WaterFields(World world)
        {
            int used = 0;
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (!world.HasWaterField(cell)) continue;
                if (Fog.AtCell(cell % World.GridSize, cell / World.GridSize) != Fog.Seen) continue;

                while (fieldCells.Count <= used) fieldCells.Add(Decal.MakeSquare(transform, "WaterField", BodyPalette.WaterSlimes.Pale));
                Decal square = fieldCells[used++];
                square.Set(MatchRunner.ToView(World.CellCenter(cell)), 0.5f, 0f);
                float left = (world.WaterFieldExpiry(cell) - world.Tick) / (float)FieldFadeTicks;
                square.Colour = WithAlpha(BodyPalette.WaterSlimes.Pale, Mathf.Lerp(FieldAlphaLast, FieldAlpha, Mathf.Clamp01(left)));
                square.Visible = true;
            }
            for (int k = used; k < fieldCells.Count; k++) fieldCells[k].Visible = false;
        }

        /// <summary>
        /// A summon's lifetime, beside its hp bar whether or not the bar is up:
        /// a short bar of <see cref="UiStyle.InkDim"/> on a meter back, full at
        /// the lifetime its owner gives the row and empty on the tick it lapses.
        /// 층위 고정 can hold it past full; it reads full.
        /// </summary>
        private void Life(World world, int i, Entity e, Vector3 head, float barWidth)
        {
            if (!e.Alive || e.ExpireTick <= 0 || e.Owner < 0)
            {
                LifeOff(i);
                return;
            }
            if (lifeBacks[i] == null)
            {
                lifeBacks[i] = Billboard.Make(transform, "LifeBack", UiStyle.MeterBack);
                lifeFills[i] = Billboard.Make(transform, "LifeFill", UiStyle.InkDim, front: true);
            }
            int lifetime = world.LifetimeFor(e.Owner, FactionData.Stats(world.FactionOf(e.Owner), e.Role, e.Slot));
            float left = Mathf.Clamp01((e.ExpireTick - world.Tick) / (float)Mathf.Max(lifetime, 1));
            Vector3 at = head + Cam.transform.right * (barWidth * 0.5f + LifeGap + LifeWidth * 0.5f);
            lifeBacks[i].Set(Cam, at, LifeWidth, BarHeight, 1f);
            lifeFills[i].Set(Cam, at, LifeWidth, BarHeight, left);
            lifeBacks[i].Visible = true;
            lifeFills[i].Visible = true;
        }

        private void LifeOff(int i)
        {
            if (lifeBacks[i] == null) return;
            lifeBacks[i].Visible = false;
            lifeFills[i].Visible = false;
        }

        /// <summary>
        /// 상태 이상과 오라 on one seen body (issue #206): the vine ring and the
        /// smaller hull of a slow, the embers of a burn and a small burst on each
        /// burn tick, the motes and ring of a heal, and the radius ring of an
        /// aura source. All read off the entity and the ability table this
        /// frame; nothing is kept but the last frame's numbers.
        /// </summary>
        private void Statuses(World world, int i, Entity e, Vector2 p, BodyRig body, bool picked, bool healed, bool scorched)
        {
            body.Burning(e.BurnTicksLeft > 0);
            body.Slowed(e.SlowTicksLeft > 0);

            if (e.SlowTicksLeft <= 0)
            {
                Off(slowRings, i);
            }
            else
            {
                Decal vine = Lazy(slowRings, i, "Slow", SlowColour);
                vine.Visible = true;
                vine.Set(p, body.Footprint * SlowRingRatio, SlowRingBand);
                float left = Mathf.Clamp01(e.SlowTicksLeft / (float)Mathf.Max(vitals[i].SlowPeak, 1));
                vine.Colour = WithAlpha(SlowColour, Mathf.Lerp(SlowAlphaLast, SlowAlphaFull, left));
            }

            if (scorched)
            {
                // On the side facing the camera, so the burst is not swallowed by the body.
                Vector3 at = body.Chest - Cam.transform.forward * (body.Footprint * 0.35f);
                effects.Impact(at, BodyPalette.Hellfire.Ember, 0f, BurnTickScale);
            }

            if (healed)
            {
                Vector3 side = Cam.transform.right * (body.Footprint * 0.25f);
                Vector3 chest = body.Chest;
                effects.Mend(chest + side, HealMote);
                effects.Mend(chest - side + Vector3.up * 0.2f, HealMote);
                effects.Dust(Field.At(p), body.Footprint * 0.5f, HealDust, 0f, HealDustAlpha);
            }

            int aura = auraRows[i];
            bool ringed = false;
            if (aura >= 0)
            {
                AbilityRow row = FactionData.AbilityRowAt(aura);
                bool burns = row.Kind == AbilityKind.BurnAura;
                if ((picked || burns) && AuraWorks(world, e, row))
                {
                    float radius = world.AbilityRadius(e.Owner, row);
                    Decal reach = Lazy(auraRings, i, "Aura", AuraRing);
                    reach.Visible = true;
                    reach.Set(p, radius, burns ? BurnAuraBand : radius * AuraBandRatio);
                    reach.Colour = burns ? BurnAuraRing : row.Kind == AbilityKind.HealAura ? HealRing : AuraRing;
                    ringed = true;
                }
            }
            if (!ringed) Off(auraRings, i);
        }

        /// <summary>A body that is dead, unseen or only remembered: no status marks and no embers.</summary>
        private void StatusesOff(int i, BodyRig body)
        {
            Off(slowRings, i);
            Off(auraRings, i);
            body.Burning(false);
            body.Slowed(false);
            body.Expiring(false);
        }

        /// <summary>
        /// The status ring of entity <paramref name="i"/> in this list, made the
        /// first time it is wanted: most entities are never slowed and hold no
        /// aura, so a node or a wall of buildings carries none.
        /// </summary>
        private Decal Lazy(List<Decal> list, int i, string name, Color colour)
        {
            if (list[i] == null) list[i] = Decal.Make(transform, name, ring: true, colour);
            return list[i];
        }

        private static void Off(List<Decal> list, int i)
        {
            if (list[i] != null) list[i].Visible = false;
        }

        /// <summary>
        /// 연쇄 번개 and 연쇄 방전, guessed. The simulation keeps no record of where
        /// a chain jumped (Sim/Combat.cs ChainFrom keeps its hit list on the stack
        /// for one shot), so the view approximates it from what it can read: for
        /// each body that fired this frame holding an unlocked ChainOnHit row, it
        /// starts from the target the shot was at and, up to the row's hop count,
        /// jumps to the nearest hostile body whose hp fell this frame within the
        /// row's radius (plus <see cref="ChainSlack"/>) of the last one, as
        /// ChainFrom picks nearest. A body hurt by something else on the same
        /// frame can be taken for a hop, and a hop to a body that was already at
        /// zero is missed; both are rare and both only move a bolt.
        ///
        /// The bolts wait for the shot's tracer to land, and each hop a moment
        /// more. Only between two seen bodies. The body a bolt jumps to is the
        /// bolt's target in Effects, so it flinches, flashes and falls when the
        /// bolt appears, not at the tick, as the first target waits for its
        /// tracer: Chains runs before Consequences reads Effects.Incoming.
        ///
        /// Lightning yellow (세계수's Bolt) whichever faction fires, 인간's
        /// 전기 타워 included: issue #206 names the chain's colour, and a jump
        /// in the shooter's own tracer colour would read as a second shot.
        /// </summary>
        private void Chains(World world)
        {
            for (int c = 0; c < chained.Count; c++)
            {
                Entity a = world.GetEntity(chained[c]);
                int ability = ChainRow(world, a);
                if (ability < 0 || a.TargetId < 0) continue;
                AbilityRow row = FactionData.AbilityRowAt(ability);
                float reach = world.AbilityRadius(a.Owner, row) + ChainSlack;
                int limit = Mathf.Min(row.Limit, FactionData.MaxChainHops);

                int count = 0;
                hops[count++] = a.TargetId;
                int from = a.TargetId;
                for (int hop = 0; hop < limit; hop++)
                {
                    Vector2 at = MatchRunner.ToView(world.GetEntity(from).Position);
                    int best = -1;
                    float bestDistance = reach * reach;
                    for (int j = 0; j < world.EntityCount; j++)
                    {
                        if (!dropped[j] || Used(j, count)) continue;
                        Entity v = world.GetEntity(j);
                        if (v.Owner < 0 || v.Owner == a.Owner || v.Kind == EntityKind.ResourceNode) continue;
                        float d = (MatchRunner.ToView(v.Position) - at).sqrMagnitude;
                        if (d > bestDistance) continue;
                        best = j;
                        bestDistance = d;
                    }
                    if (best < 0) break;

                    if (Seen(world, from) && Seen(world, best))
                    {
                        effects.Jump(Chest(from), Chest(best), BodyPalette.TreeSpirits.Bolt,
                                     chainLands[c] + (hop + 1) * ChainHopDelay, best);
                    }
                    hops[count++] = best;
                    from = best;
                }
            }
        }

        private bool Used(int id, int count)
        {
            for (int h = 0; h < count; h++)
            {
                if (hops[h] == id) return true;
            }
            return false;
        }

        /// <summary>Whether the fog shows this body, asked of a copy marked alive, as Shoot asks: a chain that killed it still jumped where the player could see.</summary>
        private static bool Seen(World world, int id)
        {
            Entity e = world.GetEntity(id);
            e.Alive = true;
            return Fog.Show(id, e) == Fog.Seen;
        }

        /// <summary>The middle of a body where it is drawn: where a shot aims, and where a chain jumps from and to.</summary>
        private Vector3 Chest(int id)
        {
            BodyRig body = bodies[id];
            return Field.At(runner.DrawPosition(id), body.ChestLift + (body.Flying ? BodyRig.HoverHeight : 0f));
        }

        /// <summary>The ChainOnHit row this body can use right now, or -1: its roster entry holds one and its owner has unlocked it.</summary>
        private static int ChainRow(World world, Entity e)
        {
            if (e.Owner < 0) return -1;
            int row = FactionData.AbilityOf(world.FactionOf(e.Owner), e.Role, e.Slot, AbilityKind.ChainOnHit);
            return row >= 0 && world.AbilityUnlocked(e.Owner, FactionData.AbilityRowAt(row)) ? row : -1;
        }

        /// <summary>
        /// The aura row of the ability table this body's roster entry holds, or
        /// -1. Every entry holds at most one: the type load refuses an entry with
        /// two rows of one kind, and no entry today holds two aura kinds. The
        /// first in table order wins if one ever does.
        /// </summary>
        private static int AuraRow(World world, Entity e)
        {
            if (e.Owner < 0) return -1;
            Faction faction = world.FactionOf(e.Owner);
            for (int r = 0; r < FactionData.AbilityRowCount; r++)
            {
                AbilityRow row = FactionData.AbilityRowAt(r);
                if (row.Faction == faction && row.Role == e.Role && row.Slot == e.Slot && FactionData.IsAura(row.Kind)) return r;
            }
            return -1;
        }

        /// <summary>
        /// Whether this source's aura is on right now, the rule Sim/Auras.cs
        /// CollectAuraSources applies: unlocked for its owner, not growing, and
        /// a building finished.
        /// </summary>
        private static bool AuraWorks(World world, Entity e, AbilityRow row) =>
            e.Alive && e.Owner >= 0 && world.AbilityUnlocked(e.Owner, row) && !World.IsGrowing(e) &&
            (e.Kind != EntityKind.Building || e.BuildTicksLeft == 0);

        /// <summary>This frame's reading of an entity, carrying the slow's peak over from <paramref name="was"/> unless the slow was set again.</summary>
        private static Vital VitalOf(Entity e, Vital was) => new Vital
        {
            Hp = e.Hp,
            MaxHp = e.MaxHp,
            Site = e.BuildTicksLeft,
            Burn = e.BurnTicksLeft,
            Slow = e.SlowTicksLeft,
            Clock = e.AbilityTicksLeft,
            Cooldown = e.AbilityCooldown,
            Deployed = e.Deployed,
            SlowPeak = e.SlowTicksLeft > was.Slow ? e.SlowTicksLeft : e.SlowTicksLeft == 0 ? 0 : was.SlowPeak,
        };

        /// <summary>
        /// How many multiples of World.BurnPeriod lie below this burn clock,
        /// zero included: a clock that ran from a to b passed a burn tick when
        /// this differs between them. -1 for a clock at zero.
        /// </summary>
        private static int BurnPeriods(int ticks) => ticks <= 0 ? -1 : (ticks - 1) / World.BurnPeriod;

        private static Color WithAlpha(Color colour, float alpha)
        {
            colour.a = alpha;
            return colour;
        }

        /// <summary>
        /// What follows from this frame's shots, after all of them are fired:
        /// hits flinch and flash now, or when the tracer at them lands; the dead
        /// fall now, or when the last tracer at them lands; and the tracers that
        /// landed this frame start their targets' flinch. Order matters only
        /// here, and here it is fixed, whatever order the entity ids ran in.
        /// </summary>
        private void Consequences(World world)
        {
            float now = ViewClock.Now;
            for (int h = 0; h < struck.Count; h++)
            {
                int id = struck[h];
                if (effects.Incoming(id) > 0f)
                {
                    flashOnLanding[id] = true;
                    continue;
                }
                hitUntil[id] = now + HitFlashSeconds;
                bodies[id].Hit();
            }

            for (int d = 0; d < fallen.Count; d++)
            {
                int id = fallen[d];
                Entity e = world.GetEntity(id);
                BodyRig body = bodies[id];
                float wait = effects.Incoming(id);
                bool rubble = e.Owner >= 0 && Rubble(world.FactionOf(e.Owner), e.Kind);
                body.Die(rubble, wait);
                Burst(world, e, runner.DrawPosition(id), AbilityKind.DeathBurst, wait);
                if (rubble)
                {
                    effects.Crumble(Field.At(runner.DrawPosition(id)), body.Size, UiStyle.Remnant,
                                    body.transform.eulerAngles.y, wait);
                }
            }

            effects.Animate();
            IReadOnlyList<int> landed = effects.Landed;
            for (int l = 0; l < landed.Count; l++)
            {
                int id = landed[l];
                if (id < 0 || id >= bodies.Count) continue;
                bodies[id].Hit();
                if (flashOnLanding[id])
                {
                    flashOnLanding[id] = false;
                    hitUntil[id] = now + HitFlashSeconds;
                }
            }
        }

        /// <summary>Destroys the retiring bodies that have played out. Swap-removes, so nothing here allocates.</summary>
        private void Retire()
        {
            for (int i = retiring.Count - 1; i >= 0; i--)
            {
                if (retiring[i] != null && !retiring[i].Spent) continue;
                if (retiring[i] != null) Destroy(retiring[i].gameObject);
                retiring[i] = retiring[retiring.Count - 1];
                retiring.RemoveAt(retiring.Count - 1);
            }
        }

        /// <summary>
        /// 차원 유랑종 통로 조준: where the next body will stand up, whether it still
        /// may, and the circles that decide it.
        ///
        /// Three things are drawn and all three are guesses, in the sense the build
        /// ghost's tint is a guess. The verdict is asked of the world this frame is
        /// drawing, which is a tick behind the one a Produce would meet and however
        /// many ticks behind the one the body arrives in. Nothing here gates a
        /// click; Orders sends the point whatever the colour says, and the
        /// simulation decides. That is the caveat Sim/Driftworlds.cs hands the view
        /// along with the public ArrivalValid, and it is handled the way CanBuild's
        /// is: by drawing the answer and never acting on it.
        ///
        /// The standing mark is re-asked every frame rather than kept from the
        /// click, and that is this feature's answer to the mechanic judging twice.
        /// The player should see the second verdict, because the second verdict can
        /// change for a reason nothing else on screen reports: a 굴절 기둥 falls, or
        /// a 경계 운반자 simply walks away and takes its radius with it, and from
        /// then on the queue comes out at the 통로. Without this the player learns
        /// it by watching bodies appear in the wrong place.
        ///
        /// The mark stands whether or not the 통로 is selected, unlike a rally
        /// marker. There is one of these for the whole client rather than one per
        /// building, and it is the thing that turns red while the player is looking
        /// somewhere else entirely — which is exactly when they need to be told.
        /// </summary>
        private void Arrival(World world)
        {
            Orders orders = Orders.Instance;
            int peer = runner.LocalPeer;
            bool aims = orders != null && ArrivalOrder.Available(world.FactionOf(peer));

            if (aims && orders.HasArrivalPoint)
            {
                Mark(ref arrivalMark, "ArrivalPoint");
                Verdict(arrivalMark, MatchRunner.ToView(orders.ArrivalPoint),
                    ArrivalOrder.Valid(world, peer, orders.ArrivalPoint));
            }
            else if (arrivalMark != null)
            {
                arrivalMark.Visible = false;
            }

            Vector2 under = default;
            bool aiming = aims && orders.Aiming && Cam != null && !Hud.OverUi(Input.mousePosition) &&
                          Field.Pointer(Cam, Input.mousePosition, out under);
            if (!aiming)
            {
                if (arrivalCursor != null) arrivalCursor.Visible = false;
                for (int i = 0; i < arrivalRings.Count; i++) arrivalRings[i].Visible = false;
                return;
            }

            // Drawn where the pointer is and not on a cell centre, unlike the build
            // ghost: a building takes a whole cell and a body stands on a point, so
            // the simulation keeps the point as it was named (Sim/Driftworlds.cs
            // AimArrival) and never runs it through CellOf.
            FixVec2 aimed = MatchRunner.ToSim(under);
            Mark(ref arrivalCursor, "ArrivalCursor");
            Verdict(arrivalCursor, MatchRunner.ToView(aimed), ArrivalOrder.Valid(world, peer, aimed));

            // The radius on the bodies that lend it, so the region is something the
            // player can see instead of something they sweep the cursor for. Only
            // while the aim is armed: this is the one moment the boundary is the
            // question, and a map wearing every anchor's circle for a whole match
            // is a map with a pattern on it.
            ArrivalOrder.Anchors(world, peer, anchors);
            Fix radius = ArrivalOrder.RadiusFor(world, peer);
            for (int i = 0; i < anchors.Count; i++)
            {
                RangeRing(AnchorRing(i), radius, runner.DrawPosition(anchors[i]));
            }
            for (int i = anchors.Count; i < arrivalRings.Count; i++) arrivalRings[i].Visible = false;
        }

        /// <summary>
        /// One of the two arrival marks, made the first time it is wanted. Ghost
        /// colours rather than the semantic pair, for the reason docs/UI-STYLE.md
        /// gives the build ghost: this is not a status, it is a preview of whether
        /// the simulation will take this point.
        /// </summary>
        private void Mark(ref Decal d, string name)
        {
            if (d == null) d = Decal.Make(transform, name, ring: false, UiStyle.GhostOk);
        }

        /// <summary>Puts a mark on a point and dresses it with the answer.</summary>
        private static void Verdict(Decal d, Vector2 p, bool valid)
        {
            d.Set(p, ArrivalMarkCells * 0.5f, 0f);
            d.Colour = valid ? UiStyle.GhostOk : UiStyle.GhostBad;
            d.Visible = true;
        }

        /// <summary>
        /// A pooled radius circle, the same hairline the weapon rings use. Not
        /// indexed by anything the simulation owns — the tail is switched off every
        /// frame — so a restart needs no reset, exactly like the debris pool.
        /// </summary>
        private Decal AnchorRing(int index)
        {
            while (arrivalRings.Count <= index)
            {
                arrivalRings.Add(Decal.Make(transform, "ArrivalRing", ring: true, UiStyle.RangeRing));
            }
            return arrivalRings[index];
        }

        /// <summary>
        /// 돌 골렘 부족 잔해: a rubble pile on every cell the simulation is blocking.
        ///
        /// Not part of the terrain mesh. That is built once per world because
        /// terrain never changes; debris appears and lapses mid match, and
        /// rebuilding the field every time a golem falls pays the whole map's cost
        /// for one cell. A pooled model a cell is the same shape the rally markers
        /// already use.
        ///
        /// Alpha carries how much life is left, which is what a site's alpha
        /// already means on a building that is not finished — one idiom, one thing
        /// to learn. Alpha rather than scale because debris blocks its whole cell
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
            int longest = RemnantFade.LongestTicks(world);
            int used = 0;
            for (int cell = 0; cell < World.GridCells; cell++)
            {
                if (!world.HasRemnant(cell)) continue;

                BodyRig pile = Debris(used++);
                pile.Show(true);
                pile.Place(Field.At(MatchRunner.ToView(World.CellCenter(cell))));

                float left = (world.RemnantExpiry(cell) - world.Tick) / (float)longest;
                pile.Alpha(Mathf.Lerp(UiStyle.RemnantFade, 1f, Mathf.Clamp01(left)));
            }

            for (int i = used; i < remnants.Count; i++) remnants[i].Show(false);
        }

        private BodyRig Debris(int index)
        {
            while (remnants.Count <= index) remnants.Add(Models.Remnant(transform));
            return remnants[index];
        }

        /// <summary>
        /// The ring band, in world units. A band fixed in world units disappears
        /// with the whole map on screen; a band fixed in pixels swallows the unit
        /// zoomed right in. Neither is legible at both ends, so the band is a
        /// fraction of what is visible: the camera's half-height at its frame.
        /// </summary>
        private static float Band()
        {
            float half = CameraRig.Instance != null ? CameraRig.Instance.HalfHeight : 10f;
            return Mathf.Max(RingBandFloor, half * RingBandRatio);
        }

        /// <summary>
        /// Sizes and places the reach ring at a weapon's actual range, read off
        /// the roster in cells and converted the same way Fog already converts
        /// the same field (Fog.Reach): Fix.Raw over Fix.One, since a range is a
        /// plain distance and never needs the position math MatchRunner.ToView
        /// does for a point. Unlike the selection ring, this one's width is not
        /// held to a screen-space floor — the shape it draws is the boundary the
        /// player asked about, not a sliver around a footprint that would
        /// otherwise vanish, so it is free to scale with the map.
        /// </summary>
        private static void RangeRing(Decal d, Fix range, Vector2 centre) => RangeRing(d, range.Raw / (float)Fix.One, centre);

        /// <summary>The same ring at a radius in cells: a 견인 대포's growing as it deploys (issue #212).</summary>
        private static void RangeRing(Decal d, float radius, Vector2 centre)
        {
            d.Set(centre, radius, radius * RangeBandRatio);
            d.Visible = true;
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
            int id = -1;
            if (!Hud.OverUi(Input.mousePosition) && Field.Pointer(Cam, Input.mousePosition, out Vector2 under))
            {
                id = runner.EntityAt(under, HoverRadius, mineOnly: false);
            }

            hover.Visible = id >= 0 && Fog.Show(id, world.GetEntity(id)) == Fog.Seen;
            if (!hover.Visible) return;

            float band = Band();
            hover.Set(runner.DrawPosition(id), bodies[id].Footprint * 0.5f + band, band);
        }

        /// <summary>
        /// The placement preview: the building's own model under the cursor,
        /// snapped to the cell it would take, tinted by whether the simulation
        /// would accept it. The tint is feedback and nothing else. It is read from
        /// a world up to a tick behind the one the command will meet, so the answer
        /// that counts is the one Apply takes; nothing here gates the click.
        /// </summary>
        private void Ghost(World world)
        {
            Orders orders = Orders.Instance;
            Vector2 under = default;
            bool placing = orders != null && orders.Pending == CommandType.Build &&
                           orders.Placing != Role.None && Cam != null &&
                           !Hud.OverUi(Input.mousePosition) &&
                           Field.Pointer(Cam, Input.mousePosition, out under);

            if (!placing)
            {
                if (ghost != null) ghost.Show(false);
                return;
            }

            Role role = orders.Placing;
            Faction faction = world.FactionOf(runner.LocalPeer);
            FixVec2 aimed = MatchRunner.ToSim(under);
            bool legal = world.CanBuild(runner.LocalPeer, role, aimed);

            if (ghost == null || ghostRole != role || ghostFaction != faction)
            {
                if (ghost != null) Destroy(ghost.gameObject);
                ghostRole = role;
                ghostFaction = faction;
                // Entry 0 only: Build still names a plain role (Sim/World.cs
                // Apply), so nothing it places can be anything else yet (#111).
                ghost = Models.Build(transform, faction, role, 0, EntityKind.Building, runner.LocalPeer, neutral: false);
                ghost.name = "BuildGhost";
                ghost.Alpha(GhostAlpha);
            }

            // Drawn on the cell the simulation would use, not under the pointer, so
            // what the player sees is where the building lands.
            Vector2 cell = MatchRunner.ToView(World.CellCenter(World.CellOf(aimed)));
            ghost.Show(true);
            ghost.Place(Field.At(cell));
            ghost.Tint(legal ? UiStyle.GhostOk : UiStyle.GhostBad);
        }

        /// <summary>Every meter off: the thing is dead, unseen, or only remembered.</summary>
        private void Overlays(int i)
        {
            barBacks[i].Visible = false;
            barFills[i].Visible = false;
            queueFills[i].Visible = false;
            LifeOff(i);
        }

        /// <summary>
        /// Health bar over anything hurt or selected, production progress under a
        /// selected building. Both are read straight off the entity every frame and
        /// held nowhere, so the overlay cannot drift from what the simulation says.
        /// </summary>
        private void Overlays(World world, int i, Entity e, Vector2 p, BodyRig body, bool picked)
        {
            bool node = e.Kind == EntityKind.ResourceNode;
            bool hurt = e.Hp < e.MaxHp;
            bool bar = e.Alive && !node && (hurt || picked);

            Vector3 head = Field.At(p, body.Top + BarLift);
            float width = e.Kind == EntityKind.Building ? 2.2f : 1.1f;

            barBacks[i].Visible = bar;
            barFills[i].Visible = bar;
            if (bar)
            {
                float ratio = e.MaxHp > 0 ? Mathf.Clamp01(e.Hp / (float)e.MaxHp) : 0f;
                barBacks[i].Set(Cam, head, width, BarHeight, 1f);
                barFills[i].Set(Cam, head, width, BarHeight, ratio);
                barFills[i].Colour = UiStyle.Health(ratio);
            }
            Life(world, i, e, head, width);

            // ProduceTicksLeft counts down and is zero in the tick before a queued
            // unit starts, so an empty bar means queued-not-started, not finished.
            // 징발 진행도, on the worker, because that is the only body the
            // progress is on: World.SpawnNeutralRock deliberately hangs no state
            // on the rock. The same bar as the production queue's and in the same
            // colour, because it is the same question — what is this body making —
            // and a capture is a worker making a Towerback.
            //
            // Drawn whether or not it is selected, unlike the queue. Sixty ticks
            // is three seconds a player chose to spend standing still, and the
            // opponent being able to see them spent is the competition this
            // mechanic exists to create.
            float capture = e.Alive && e.Kind == EntityKind.Worker
                ? CaptureOrder.Progress(world, e)
                : -1f;

            bool queue = e.Alive && picked && e.Kind == EntityKind.Building && e.QueueCount > 0;
            queueFills[i].Visible = queue || capture >= 0f;
            Vector3 below = head - Cam.transform.up * (BarHeight * 1.4f);
            if (capture >= 0f)
            {
                queueFills[i].Set(Cam, below, width, BarHeight, capture);
            }
            else if (queue)
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
                queueFills[i].Set(Cam, below, width, BarHeight, done);
            }
        }

        /// <summary>
        /// A rally marker: a diamond standing on the point, turning slowly so the
        /// eye finds it. Standing rather than lying flat because a flat diamond on
        /// a tilted camera is a thin lozenge, and this is the one mark that has to
        /// be found at a glance across the map.
        /// </summary>
        private void Rally(int index, Vector2 point)
        {
            while (rallyMarkers.Count <= index)
            {
                var go = new GameObject("Rally");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.AddComponent<MeshFilter>().sharedMesh = Marks.Diamond;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Marks.Ground;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", UiStyle.Rally);
                mr.SetPropertyBlock(block);
                go.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
                rallyMarkers.Add(mr);
            }

            MeshRenderer marker = rallyMarkers[index];
            marker.transform.position = Field.At(point, 0.5f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f));
            marker.transform.rotation = Quaternion.Euler(0f, Time.unscaledTime * 60f, 0f);
            marker.enabled = true;
        }

        /// <summary>
        /// A body and everything that rides along with its id: ring, reach ring,
        /// hit flash, shadow, meters. All of it indexed by entity id, which is why
        /// every list here is appended in lockstep.
        /// </summary>
        private BodyRig Create(World world, Entity e)
        {
            BodyRig body = Dress(world, e);
            body.Use(effects);

            if (skins.Count <= e.Id)
            {
                skins.Add(SkinOf(e));
                cooldowns.Add(e.AttackCooldown);
                queues.Add(e.QueueCount);
                alive.Add(e.Alive);
                flashOnLanding.Add(false);
                hitUntil.Add(-1f);
                vitals.Add(VitalOf(e, default));
                dropped.Add(false);
                auraRows.Add(AuraRow(world, e));
                castRows.Add(CastRow(world, e));
                mended.Add(false);
                lifeBacks.Add(null); // made on first use: Life
                lifeFills.Add(null);
                slowRings.Add(null); // made on first use: Lazy
                auraRings.Add(null);
                rings.Add(Decal.Make(transform, "Ring", ring: true, UiStyle.Ring));
                rangeRings.Add(Decal.Make(transform, "Range", ring: true, UiStyle.RangeRing));
                hitFlashes.Add(Decal.Make(transform, "HitFlash", ring: true, UiStyle.Danger));
                shadows.Add(Decal.Make(transform, "Shadow", ring: false, UiStyle.Shadow));
                barBacks.Add(Billboard.Make(transform, "HpBack", UiStyle.MeterBack));
                barFills.Add(Billboard.Make(transform, "HpFill", Color.white, front: true));
                queueFills.Add(Billboard.Make(transform, "Queue", UiStyle.Queue));
            }

            return body;
        }

        /// <summary>
        /// Builds a body's model from what it is: the roster entry for anything
        /// owned, the map's own 꼬마돌 for the neutral rock, a crystal cluster for
        /// a node. One method because it runs twice. A body is built once per
        /// entity id, which held for every rule the simulation had until 징발:
        /// everything else makes a body or ends one, and this is the first thing
        /// that leaves a body standing and changes what it is. Without the second
        /// call a captured 꼬마돌 goes on being drawn as a 꼬마돌 for the rest of
        /// the match, with the enemy's Towerback shooting out of it.
        ///
        /// The neutral 꼬마돌 is the one body on the field that is tinted. It
        /// belongs to no faction, it is built with a faction's worker shape, and
        /// untinted it would claim to be that faction's worker. docs/UI-STYLE.md
        /// 징발 has the measurements.
        /// </summary>
        private BodyRig Dress(World world, Entity e)
        {
            bool neutral = world.IsNeutralRock(e);
            Faction faction = e.Owner >= 0
                ? world.FactionOf(e.Owner)
                : neutral ? Faction.RockGolems : Faction.TreeSpirits;

            BodyRig body = Models.Build(transform, faction, e.Role, e.Slot, e.Kind, e.Owner, neutral);
            body.name = Label(world, e);
            body.Tint(neutral ? UiStyle.Neutral : Color.white);
            body.Show(false); // the fog decides, below, before the first frame it is seen
            return body;
        }

        /// <summary>
        /// Everything about a body that decides how it is built, in one number, so
        /// the frame can ask whether it changed without asking the roster again.
        /// Owner is offset by one because a neutral's is -1.
        /// </summary>
        private static int SkinOf(Entity e) => ((e.Owner + 1) * 64 + (int)e.Role) * 64 + e.Slot;

        private static string Label(World world, Entity e)
        {
            if (world.IsNeutralRock(e)) return "꼬마돌";
            return e.Owner < 0 ? e.Kind.ToString() : FactionData.Name(world.FactionOf(e.Owner), e.Role, e.Slot);
        }

        /// <summary>
        /// A role's footprint on the grid: what a ring rings, and the floor on what
        /// a building of that role can see — a thing is never fogged under its own
        /// feet. Models size themselves to the same table, so the ring and the
        /// body agree.
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

        private static void DropBodies(List<BodyRig> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null) Destroy(pool[i].gameObject);
            }
            pool.Clear();
        }

        private static void Drop<T>(List<T> pool) where T : MonoBehaviour
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null) Destroy(pool[i].gameObject);
            }
            pool.Clear();
        }
    }
}
