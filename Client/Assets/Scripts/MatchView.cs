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
                Drop(barBacks);
                Drop(barFills);
                Drop(queueFills);
                skins.Clear();      // parallel to bodies, and Create refills it
                cooldowns.Clear();  // same
                hitUntil.Clear();   // same: a restart's ids owe nothing to the match before it
                alive.Clear();      // same
                queues.Clear();     // same
                flashOnLanding.Clear(); // same
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

                // A dead body that is not playing its death has nothing left to
                // move; switched off, so a long match's dead cost no Update.
                if (!e.Alive && !falls && !body.Dying && body.enabled) body.enabled = false;
                if (!e.Alive && (falls || body.Dying))
                {
                    rings[i].Visible = false;
                    rangeRings[i].Visible = false;
                    hitFlashes[i].Visible = false;
                    shadows[i].Visible = false;
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
                    RangeRing(rangeRings[i], world.RangeFor(e), p);
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
            Consequences(world);
            Remnants(world);
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
                Shoot(world, e, body);
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
        /// </summary>
        private void Shoot(World world, Entity e, BodyRig body)
        {
            if (e.Owner < 0 || e.TargetId < 0 || e.TargetId >= bodies.Count) return;

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
                if (seen)
                {
                    effects.Shot(body.Muzzle, hit, colour, leave, e.TargetId);
                }
                else
                {
                    // Into the fog: the tracer leaves, and is gone before it
                    // could say where the hidden body stands. No burst, no
                    // ring, no landing.
                    Vector3 muzzle = body.Muzzle;
                    effects.Shot(muzzle, Vector3.Lerp(muzzle, hit, HiddenShotReach), colour, leave, -1, bursts: false);
                }
            }
            else
            {
                victim.Hit();
                if (seen) effects.Impact(hit, colour, BodyRig.LungeLandDelay);
            }
        }

        /// <summary>How far toward a target hidden in the fog its tracer is drawn before it vanishes.</summary>
        private const float HiddenShotReach = 0.6f;

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
        private static void RangeRing(Decal d, Fix range, Vector2 centre)
        {
            float radius = range.Raw / (float)Fix.One;
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
