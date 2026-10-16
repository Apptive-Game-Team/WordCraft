using UnityEngine;
using UnityEngine.Rendering;

namespace WordCraft.View
{
    /// <summary>How a body travels, which decides what its hull does while it moves.</summary>
    internal enum Gait
    {
        /// <summary>Legs swing or step; the hull bobs once a step.</summary>
        Walk,
        /// <summary>No legs. The whole hull hops and squashes on landing: slimes, the toad.</summary>
        Hop,
        /// <summary>No legs. The hull drifts with a slow bob and leans into travel: spirits, devices.</summary>
        Float,
        /// <summary>Airborne. Held <see cref="BodyRig.HoverHeight"/> off the ground, bobbing, wings beating.</summary>
        Fly,
        /// <summary>Never moves. Buildings, nodes, debris.</summary>
        Still,
    }

    /// <summary>What the two limb parts do with the gait.</summary>
    internal enum LimbMotion
    {
        /// <summary>Rotate about the hip: a biped's legs.</summary>
        Swing,
        /// <summary>Lift and slide: a diagonal pair or a tripod of legs on a crawler.</summary>
        Step,
        /// <summary>Rotate about the body's long axis: wings, fins, vanes.</summary>
        Flap,
    }

    /// <summary>The shape of one attack.</summary>
    internal enum Strike
    {
        /// <summary>Wind up, lunge forward, swing the arm down. Melee.</summary>
        Lunge,
        /// <summary>Kick back and tip the arm up. Anything that fires.</summary>
        Recoil,
        /// <summary>Swell and settle. Bodies with no weapon worth swinging.</summary>
        Pulse,
    }

    /// <summary>
    /// How a building's parts come together while it is built (issue #186):
    /// one way per faction, each the faction's own idea of making something.
    /// </summary>
    internal enum Assembly
    {
        /// <summary>세계수 정령: each part grows up out of a thin shoot.</summary>
        Grow,
        /// <summary>지옥불 군단: each part tears up out of the ground, shaking.</summary>
        Tear,
        /// <summary>물 슬라임: each part swells up and wobbles to rest.</summary>
        Inflate,
        /// <summary>돌 골렘 부족: each part drops from above and lands on the pile.</summary>
        Stack,
        /// <summary>차원 유랑종: each plate swings shut from open.</summary>
        Close,
        /// <summary>인간 마법 문명: each part is laid up floor by floor, square and without overshoot.</summary>
        Floors,
    }

    /// <summary>
    /// Everything that is the same about every body of one roster entry and one
    /// owner: the baked meshes, where the moving parts hinge, and how it moves.
    /// Built once by Models and shared by every body it describes, so a hundred
    /// 꼬마돌 are a hundred transforms over one set of meshes.
    /// </summary>
    internal sealed class Design
    {
        public Mesh Hull, Arm, LimbLeft, LimbRight, Crown;
        public Vector3 ArmPivot, LimbLeftPivot, LimbRightPivot, CrownPivot;
        public Gait Gait;
        public LimbMotion Limbs;
        public Strike Strike;
        /// <summary>Wings hang from the hull and bob with it; legs hang from the root and stay on the ground.</summary>
        public bool LimbsRideHull;
        /// <summary>World units of travel per full gait cycle, so feet do not skate.</summary>
        public float Stride;
        /// <summary>World height of the body proper, for scaling every motion to it.</summary>
        public float Size;
        public float Footprint;
        public float Top;
        public bool Flying;
        public bool Building;
        /// <summary>Degrees a second the crown turns by itself: an orb, a ring of shards.</summary>
        public float CrownSpin;
        /// <summary>Whether <see cref="BodyRig.Face"/> turns the crown of a building toward its target.</summary>
        public bool CrownAims;
        /// <summary>Degrees the crown pitches when it fires: negative kicks a barrel up, positive lurches forward.</summary>
        public float CrownKick;
        public Color Tint = Color.white;

        /// <summary>
        /// A building's hull after the first part, split so a site can be put
        /// together part by part (issue #186). Each hangs from the hull at its
        /// pivot; <see cref="TierHeights"/> is how tall each one stands over it.
        /// Null for a body with one hull mesh.
        /// </summary>
        public Mesh[] Tiers;
        public Vector3[] TierPivots;
        public float[] TierHeights;
        /// <summary>World height of the first hull part over its foot.</summary>
        public float HullHeight;

        /// <summary>
        /// A building a player builds: put together part by part, at work,
        /// razed. Everything else that stands still — a resource node, a
        /// rubble pile — is false, keeps one hull and sinks the old way.
        /// </summary>
        public bool Assembled;
        public Assembly Assembly;
        /// <summary>A razed building's chunks, and the dust a finished or razed building raises.</summary>
        public Color Debris;
        /// <summary>What a working building gives off: the small motes that rise from its roof.</summary>
        public Color Mote;
        /// <summary>The light inside that flashes once and goes out when it is razed; alpha zero for none.</summary>
        public Color Glow;
    }

    /// <summary>
    /// One body on the field: where it stands, which way it faces, and the few
    /// moving parts that make it read as alive — legs, an arm, a head bob, wings,
    /// a turret. No animation clips and no bones; issue #159 rules both out, and
    /// a dozen sines on five transforms is all a body twenty-five cells from the
    /// camera can show anyway.
    ///
    /// The view hands it a ground point every frame and nothing else about
    /// motion. Facing and walking are inferred from how far that point moved,
    /// which keeps the simulation the only thing that decides where anything
    /// goes: this never predicts, it only follows.
    ///
    /// Time is <see cref="ViewClock"/>: unscaled, like every other effect in the
    /// view, so a paused or slowed match still breathes rather than freezing
    /// mid-stride, and holdable, so a screenshot can step it frame by frame.
    /// </summary>
    public sealed class BodyRig : MonoBehaviour
    {
        /// <summary>How far above the ground a flyer's feet are. The lead's blob shadow stays on the ground under it.</summary>
        public const float HoverHeight = 0.9f;

        /// <summary>
        /// How far a swinging leg turns either side of straight down. Models sizes
        /// a biped's stride from this and its hip height, so the planted foot moves
        /// back exactly as far as the body moves forward and does not skate.
        /// </summary>
        public const float SwingDegrees = 28f;

        /// <summary>How long a growth's old body swells before the new one comes up. MatchView waits this long to show it.</summary>
        public const float SwellSeconds = 0.15f;

        private const float TurnDegreesPerSecond = 540f;
        private const float AttackSeconds = 0.35f;

        /// <summary>The share of a strike spent winding up before it goes: a pull back and a crouch.</summary>
        private const float WindupShare = 0.25f;

        /// <summary>
        /// Seconds from <see cref="Attack"/> to the moment a strike goes: the end
        /// of the wind-up. A shot leaves the muzzle here, not when Attack is called.
        /// </summary>
        public const float StrikeDelay = AttackSeconds * WindupShare;

        /// <summary>Seconds from <see cref="Attack"/> to a lunge's furthest reach, where a melee blow lands.</summary>
        public const float LungeLandDelay = AttackSeconds * 0.55f;
        private const float HitSeconds = 0.3f;
        private const float DeathSeconds = 0.6f;
        private const float SpawnSeconds = 0.3f;

        /// <summary>The ghost alpha of a site under construction, as UiStyle.SiteAlpha was for the sprite view.</summary>
        private const float SiteAlpha = 0.75f;

        // Buildings (issue #186).

        /// <summary>How long a razed building takes to come down: longer than a body's death, it has further to fall.</summary>
        private const float DestroySeconds = 0.8f;

        /// <summary>The share of construction the crown takes to seat itself, at the very end.</summary>
        private const float CrownShare = 0.1f;

        /// <summary>
        /// How long one part takes to go up, in the steps that separate two
        /// parts' starts: longer than one, so the next part starts while the
        /// last is still settling and the site never stands still.
        /// </summary>
        private const float PartSteps = 1.4f;

        /// <summary>How far along its own rise the first part is at a site's first tick, so a new site is never nothing.</summary>
        private const float SiteFloor = 0.2f;

        /// <summary>The squash and spring back of a building the moment it is finished.</summary>
        private const float SettleSeconds = 0.35f;

        /// <summary>A finish seen this long after the last progress reading is not news: it finished out of sight.</summary>
        private const float RecentProgress = 0.5f;

        /// <summary>How long a working building takes to come up to speed and to wind down.</summary>
        private const float WorkRamp = 0.4f;

        /// <summary>How many times its idle rate a self-turning crown turns while the building works.</summary>
        private const float WorkSpin = 3.5f;

        /// <summary>The issue's 1.5 percent: how far a working building without a turning crown swells and shrinks.</summary>
        private const float WorkPulse = 0.015f;
        private const float WorkPulseRate = 7f;

        /// <summary>Seconds between two motes rising off a working building. A mote lives a second, so three are up at once.</summary>
        private const float MoteInterval = 0.33f;

        /// <summary>
        /// Seconds between two embers off a burning body (issue #206). An ember
        /// lives 0.8 s, so two or three are up at once, as the issue asks.
        /// </summary>
        private const float EmberInterval = 0.3f;

        /// <summary>An ember's size, in cells: big enough to survive the downscale to a 64 px body.</summary>
        private const float EmberSize = 0.26f;

        /// <summary>The issue's 1.5 percent: how much smaller a slowed body's hull stands.</summary>
        private const float SlowShrink = 0.015f;

        /// <summary>The swell and lean of a building as a unit it made comes out.</summary>
        private const float DeliverSeconds = 0.45f;
        private const float DeliverSwell = 0.1f;
        private const float DeliverLean = 10f;

        /// <summary>A jump this long between two frames is a body being placed somewhere, not walking there.</summary>
        private const float TeleportDistance = 4f;

        /// <summary>
        /// Smoothing on the measured speed. The simulation moves bodies in tick
        /// steps and frames outnumber ticks, so the raw per-frame speed alternates
        /// between a stride and nothing; a tenth of a second evens that out without
        /// letting a stop slide on for long. It is also the body's acceleration:
        /// the gait takes this long to come up to speed and to wind down.
        /// </summary>
        private const float SpeedSmoothingSeconds = 0.12f;

        /// <summary>How quickly a lean into a turn or a start follows the motion that causes it.</summary>
        private const float LeanSmoothingSeconds = 0.08f;

        /// <summary>World units a second at which the gait is fully in step. Below it the walk fades into idle.</summary>
        private const float FullGaitSpeed = 0.5f;

        private const float MovingEpsilon = 1e-4f;

        /// <summary>A wash or a fade closer than this to the last one applied is not worth a property block.</summary>
        private const float LookEpsilon = 0.004f;

        /// <summary>What the fog's memory multiplies a body by, before the shader takes most of its colour away.</summary>
        private static readonly Color RememberedMultiply = new Color(0.72f, 0.72f, 0.76f, 1f);
        private const float RememberedDesaturate = 0.65f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        private static readonly int WashId = Shader.PropertyToID("_Wash");
        private static MaterialPropertyBlock block;

        private Design design;
        private Transform hull, arm, limbLeft, limbRight, crown;

        /// <summary>Where impacts, dust, motes and chunks are drawn from. Null for a body that makes none.</summary>
        private Effects effects;

        // A building's parts: its first hull mesh and then each tier, all
        // hanging from the hull in the order a site puts them up. The hull of a
        // building is a bare transform over them, so a flinch or a swell moves
        // every part together while construction moves them one by one.
        private Transform[] parts;
        private Vector3[] partRest;
        private float[] partHeight;
        // Where each part was when the building fell, for the fall to start from.
        private Vector3[] partFrom;
        private Quaternion[] partFromTurn;
        private Vector3[] partFromScale;
        private float crownHeight;
        // The crown's own construction pose, on top of whatever aim, spin or kick it has.
        private Vector3 crownLift;
        private Quaternion crownTwist = Quaternion.identity;
        private Vector3 crownSize = Vector3.one;
        private Vector3 crownFrom;
        private Quaternion crownFromTurn = Quaternion.identity;
        private Vector3 crownFromScale = Vector3.one;

        private float lastProgressAt = float.NegativeInfinity;
        private float settledAt = float.NegativeInfinity;
        private bool working;
        /// <summary>0 idle to 1 at work, eased over <see cref="WorkRamp"/>.</summary>
        private float work;
        /// <summary>A self-turning crown's angle, summed rather than read off the clock so a change of speed does not jump it.</summary>
        private float spin;
        private float nextMote;
        private int motes;
        private bool burning, slowed;
        private float nextEmber;
        private int embers;
        private float deliverAt = float.NegativeInfinity;
        private Vector3 deliverToward = Vector3.forward;
        private bool crumbled, dusted;
        private MeshRenderer[] renderers;
        private Vector3 hullRest, armRest, limbLeftRest, limbRightRest, crownRest;

        private bool placed;
        private Vector3 lastGround;
        private float speed;
        private float lastSpeed;
        private float travelled;
        private float targetYaw;
        private float lastYaw;
        private float aimYaw;
        private bool aiming;
        private float lean;
        private float surge;
        private float attackStarted = float.NegativeInfinity;
        private float hitStarted = float.NegativeInfinity;
        private float spawnStarted = float.NegativeInfinity;
        private float seed;
        /// <summary>Per body, 0.85 to 1.15: idle rates differ as well as phases, so a crowd never falls into step.</summary>
        private float tempo = 1f;
        /// <summary>-1 or 1: which way this body topples when it dies.</summary>
        private float side = 1f;

        // One-shots that end the body. A dying body plays out where it fell and
        // then switches itself off; a swelling one is a growth's old body.
        private bool dying;
        private bool rubble;
        private float diedAt;
        private Vector3 fellAt;
        private Quaternion fellFacing;
        /// <summary>The fall has begun: past <see cref="diedAt"/>, with the pose it fell from captured.</summary>
        private bool falling;
        private bool swelling;
        private float swellStarted;
        private bool spent;

        private float progress = 1f;
        private float alpha = 1f;
        private float fade = 1f;
        private Color wash;
        private float washAmount;
        private Color tint = Color.white;
        private bool remembered;
        private bool shown = true;
        private bool ghosted;

        public float Footprint => design != null ? design.Footprint : 1f;
        public float Top => design != null ? design.Top : 1f;
        public bool Flying => design != null && design.Flying;

        /// <summary>Whether the renderers are on: what the last frame showed.</summary>
        public bool Shown => shown;

        /// <summary>Whether the body is the fog's frozen memory rather than a live sight.</summary>
        public bool Remembered => remembered;

        /// <summary>Playing its death: the view keeps drawing it although the entity is gone.</summary>
        public bool Dying => dying && !spent;

        /// <summary>A death or a swell has played out and the body has switched itself off.</summary>
        public bool Spent => spent;

        /// <summary>
        /// Height of the middle of the body over its root, in world units: where a
        /// shot aims and an impact bursts. A flyer's root is already in the air.
        /// </summary>
        public float ChestLift =>
            design == null ? 0.5f : design.Building ? (design.Top - 0.25f) * 0.5f : design.Size * 0.5f;

        /// <summary>The middle of the body, in world space.</summary>
        public Vector3 Chest => transform.position + Vector3.up * ChestLift;

        /// <summary>
        /// Where a shot leaves the body: the crown of a tower, the hand of a body
        /// with an arm, otherwise the front of its chest.
        /// </summary>
        public Vector3 Muzzle
        {
            get
            {
                if (design == null) return transform.position;
                if (crown != null) return crown.position + Vector3.up * 0.15f;
                Vector3 forward = transform.forward * (design.Size * 0.3f);
                if (arm != null) return arm.position + forward * 0.5f;
                return Chest + forward;
            }
        }

        // ------------------------------------------------------------------
        // Assembly
        // ------------------------------------------------------------------

        /// <summary>
        /// Hangs the design's parts under this transform. Legs hang from the root
        /// so they stay planted while the hull bobs; the arm, the crown and any
        /// wings hang from the hull so they ride its bob, lunge and squash. At most
        /// six GameObjects: root, hull, two limbs, arm — or root, hull, crown.
        /// </summary>
        internal void Assemble(Design design, int variation)
        {
            this.design = design;
            seed = (variation & 1023) * 0.618f;
            tempo = 0.85f + 0.3f * Mathf.Repeat(seed * 1.37f, 1f);
            // Which way a death tips: away from the arm when there is one, so
            // the arm goes up with the body rather than folding under it and
            // showing below the silhouette as a part come loose; otherwise by
            // id, so a crowd does not all fall the same way.
            side = design.Arm != null && Mathf.Abs(design.ArmPivot.x) > 1e-3f
                ? Mathf.Sign(design.ArmPivot.x)
                : (variation & 1) == 0 ? 1f : -1f;
            targetYaw = transform.eulerAngles.y;
            lastYaw = targetYaw;
            aimYaw = targetYaw;

            var made = new System.Collections.Generic.List<MeshRenderer>(8);
            if (design.Assembled)
            {
                // A bare hull over the parts, so construction can move each
                // part while a flinch still moves the whole.
                hull = new GameObject("Hull").transform;
                hull.SetParent(transform, false);
                int count = 1 + (design.Tiers != null ? design.Tiers.Length : 0);
                parts = new Transform[count];
                partRest = new Vector3[count];
                partHeight = new float[count];
                partFrom = new Vector3[count];
                partFromTurn = new Quaternion[count];
                partFromScale = new Vector3[count];
                parts[0] = Part("Base", design.Hull, hull, Vector3.zero, made);
                partHeight[0] = design.HullHeight;
                for (int k = 1; k < count; k++)
                {
                    parts[k] = Part("Tier", design.Tiers[k - 1], hull, design.TierPivots[k - 1], made);
                    partRest[k] = design.TierPivots[k - 1];
                    partHeight[k] = design.TierHeights[k - 1];
                }
            }
            else
            {
                hull = Part("Hull", design.Hull, transform, Vector3.zero, made);
            }
            arm = Part("Arm", design.Arm, hull, design.ArmPivot, made);
            Transform limbParent = design.LimbsRideHull ? hull : transform;
            limbLeft = Part("LimbLeft", design.LimbLeft, limbParent, design.LimbLeftPivot, made);
            limbRight = Part("LimbRight", design.LimbRight, limbParent, design.LimbRightPivot, made);
            crown = Part("Crown", design.Crown, hull, design.CrownPivot, made);
            renderers = made.ToArray();

            hullRest = Vector3.zero;
            armRest = design.ArmPivot;
            limbLeftRest = design.LimbLeftPivot;
            limbRightRest = design.LimbRightPivot;
            crownRest = design.CrownPivot;
            crownHeight = Mathf.Max(design.Top - 0.25f - crownRest.y, 0.4f);

            tint = design.Tint;
            ApplyLook();
        }

        private static Transform Part(string name, Mesh mesh, Transform parent, Vector3 pivot,
                                      System.Collections.Generic.List<MeshRenderer> made)
        {
            if (mesh == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = Models.Opaque;
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            // Paper.cginc lights from its own hemisphere globals; probes would
            // add a second ambient on top of it.
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            made.Add(meshRenderer);
            return go.transform;
        }

        // ------------------------------------------------------------------
        // The contract
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts the body on this ground point, and reads facing and stride from
        /// how far it moved since the last call. Call every frame. The first call
        /// only places, so a body spawned far from the origin does not stride
        /// across the map to get there. Ignored once the body is dying: it falls
        /// where it stood.
        /// </summary>
        public void Place(Vector3 ground)
        {
            if (dying) return;

            Vector3 lift = Flying ? Vector3.up * HoverHeight : Vector3.zero;
            transform.position = ground + lift;

            if (!placed)
            {
                placed = true;
                lastGround = ground;
                return;
            }

            Vector3 delta = ground - lastGround;
            delta.y = 0f;
            lastGround = ground;

            float distance = delta.magnitude;
            if (distance > TeleportDistance) distance = 0f;

            float deltaTime = ViewClock.Delta;
            float measured = deltaTime > 1e-5f ? distance / deltaTime : 0f;
            float blend = deltaTime > 0f ? 1f - Mathf.Exp(-deltaTime / SpeedSmoothingSeconds) : 0f;
            speed = Mathf.Lerp(speed, measured, blend);
            travelled += distance;

            if (design != null && !design.Building && distance > MovingEpsilon)
                targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Turns toward a yaw in degrees, for a body standing still and fighting.
        /// Movement overrides it the moment the body moves again. A building does
        /// not turn; if it has an aiming crown, the crown turns instead.
        /// </summary>
        public void Face(float yawDegrees)
        {
            if (design != null && design.Building)
            {
                aimYaw = yawDegrees;
                aiming = true;
                return;
            }
            targetYaw = yawDegrees;
        }

        public void Attack() => attackStarted = ViewClock.Now;

        /// <summary>
        /// The hit reaction, starting now. A ranged hit is not delayed here:
        /// MatchView calls this when the tracer lands (Effects reports the
        /// landing), so a start is never pushed into the future and a stream of
        /// shots cannot keep a flinch from ever happening.
        /// </summary>
        public void Hit() => hitStarted = ViewClock.Now;

        /// <summary>The body's height in world units: what a death's chunks are sized from.</summary>
        public float Size => design != null ? design.Size : 1f;

        /// <summary>
        /// The body comes up out of the ground: scaled from nothing at its feet to
        /// a little past full size and back, over <see cref="SpawnSeconds"/>. A
        /// delay holds it at nothing first, which is how a growth's new body waits
        /// for the old one to finish swelling.
        /// </summary>
        public void Spawn(float delay = 0f)
        {
            spawnStarted = ViewClock.Now + delay;
            if (delay > 0f) transform.localScale = Vector3.one * 0.001f;
        }

        /// <summary>
        /// The entity is gone and the body plays its death where it stands, then
        /// switches itself off. <paramref name="toRubble"/> is 돌 골렘 부족's combat
        /// unit, which leaves debris on its cell: the body sinks in the rubble's
        /// colour into the pile rather than fading out on top of it.
        ///
        /// <paramref name="delay"/> holds the fall back until the shot that killed
        /// it lands: the simulation deals ranged damage at once and the tracer
        /// arrives a third of a second later, and a body already lying down when
        /// its killing shot reaches it reads backwards. Until then it goes on
        /// standing and breathing where it stood. A later call can only bring the
        /// fall nearer, never push it further away.
        /// </summary>
        public void Die(bool toRubble, float delay = 0f)
        {
            if (design == null) return;
            float at = ViewClock.Now + Mathf.Max(delay, 0f);
            if (dying)
            {
                if (!falling && at < diedAt) diedAt = at;
                return;
            }
            dying = true;
            rubble = toRubble;
            diedAt = at;
            // A dead body neither burns nor stays slowed, even while its fall
            // waits for the tracer that killed it.
            burning = false;
            slowed = false;
        }

        /// <summary>
        /// A growth's old body: it swells once and switches itself off, and the
        /// caller destroys it when <see cref="Spent"/> says so.
        /// </summary>
        public void Swell()
        {
            if (swelling || design == null) return;
            swelling = true;
            swellStarted = ViewClock.Now;
        }

        /// <summary>
        /// Construction from 0 to 1 (issue #186). A building is put together
        /// part by part: the first nine tenths are shared out among its hull's
        /// parts in order, and the last tenth seats the crown. How each part
        /// comes up is the faction's <see cref="Assembly"/>. It stays a ghost
        /// until it is finished, as a site did in the sprite view.
        ///
        /// The moment it finishes, it squashes once and springs back and a ring
        /// of dust runs out from its foot, but only when the last reading was a
        /// moment ago: a site that finished in the fog and is seen again
        /// finished, it does not finish again in front of the player.
        /// </summary>
        public void Progress(float t)
        {
            t = Mathf.Clamp01(t);
            float now = ViewClock.Now;
            bool recent = now - lastProgressAt <= RecentProgress;
            lastProgressAt = now;
            if (Mathf.Approximately(t, progress)) return;

            bool finished = progress < 1f && t >= 1f;
            progress = t;
            if (parts != null) PoseAssembly(now);
            if (finished && recent && shown && !remembered && parts != null)
            {
                settledAt = now;
                if (effects != null) effects.Dust(transform.position, design.Footprint * 0.5f, Dust);
            }
            ApplyLook();
        }

        /// <summary>Where this body's dust, chunks and motes come from. MatchView hands it its pool.</summary>
        public void Use(Effects pool) => effects = pool;

        /// <summary>
        /// Whether the building is producing or researching. A crown that turns
        /// by itself turns faster; anything else swells and shrinks a little.
        /// Either way it gives off motes of its faction's colour. Eased in and
        /// out, so a queue that empties and fills again on the same tick does
        /// not stutter.
        /// </summary>
        public void Work(bool on) => working = on;

        /// <summary>
        /// Whether the body is burning (Entity.BurnTicksLeft above zero): embers
        /// rise off it while it is seen (issue #206).
        /// </summary>
        public void Burning(bool on) => burning = on;

        /// <summary>
        /// Whether the body is slowed (Entity.SlowTicksLeft above zero): its hull
        /// stands <see cref="SlowShrink"/> smaller, as if held down (issue #206).
        /// </summary>
        public void Slowed(bool on) => slowed = on;

        /// <summary>
        /// A unit just came out of this building toward <paramref name="yawDegrees"/>:
        /// the hull swells once and leans that way, as if it had pushed it out.
        /// </summary>
        public void Deliver(float yawDegrees)
        {
            deliverAt = ViewClock.Now;
            float radians = yawDegrees * Mathf.Deg2Rad;
            deliverToward = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary>The dust a building raises when it is finished or comes down: its rubble's colour, lightened.</summary>
        private Color Dust => BodyPalette.Lighter(design.Debris, 0.55f);

        public void Tint(Color multiply)
        {
            if (multiply == tint) return;
            tint = multiply;
            ApplyLook();
        }

        public void Alpha(float a)
        {
            a = Mathf.Clamp01(a);
            if (Mathf.Approximately(a, alpha)) return;
            alpha = a;
            ApplyLook();
        }

        public void Show(bool on)
        {
            if (on == shown || renderers == null) return;
            shown = on;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = on;
        }

        /// <summary>
        /// The fog's "last known state": colour drained, value lowered, and the
        /// body frozen at rest. A remembered building is a memory of where it
        /// was, and a memory that went on breathing would claim to be news.
        /// </summary>
        public void Dim(bool remembered)
        {
            if (remembered == this.remembered) return;
            this.remembered = remembered;
            if (remembered) Rest();
            ApplyLook();
        }

        // ------------------------------------------------------------------
        // Look
        // ------------------------------------------------------------------

        private void ApplyLook()
        {
            if (renderers == null) return;

            float shownAlpha = Mathf.Min(alpha * fade, progress < 1f ? SiteAlpha : 1f);
            bool ghost = shownAlpha < 0.999f;
            if (ghost != ghosted || (renderers.Length > 0 && renderers[0].sharedMaterial == null))
            {
                ghosted = ghost;
                Material material = ghost ? Models.Ghost : Models.Opaque;
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterial = material;
                    renderers[i].shadowCastingMode = ghost ? ShadowCastingMode.Off : ShadowCastingMode.On;
                }
            }

            Color shownTint = remembered ? tint * RememberedMultiply : tint;
            shownTint.a = 1f;
            Color shownWash = wash;
            shownWash.a = washAmount;

            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            block.SetColor(TintId, shownTint);
            block.SetFloat(AlphaId, shownAlpha);
            block.SetFloat(DesaturateId, remembered ? RememberedDesaturate : 0f);
            block.SetColor(WashId, shownWash);
            for (int i = 0; i < renderers.Length; i++) renderers[i].SetPropertyBlock(block);
        }

        /// <summary>
        /// The two looks motion changes frame to frame — a wash toward one colour
        /// and a fade — pushed only when they moved, so a body at rest costs no
        /// property block at all.
        /// </summary>
        private void Look(Color toward, float amount, float opacity)
        {
            if (Mathf.Abs(amount - washAmount) < LookEpsilon && Mathf.Abs(opacity - fade) < LookEpsilon &&
                (amount <= 0f || toward == wash))
            {
                // Settle exactly on the end values, or a flash that stopped a
                // hair short of zero would leave the body faintly lit for good.
                if ((amount <= 0f && washAmount > 0f) || (opacity >= 1f && fade < 1f))
                {
                    washAmount = amount;
                    fade = opacity;
                    ApplyLook();
                }
                return;
            }
            wash = toward;
            washAmount = amount;
            fade = opacity;
            ApplyLook();
        }

        // ------------------------------------------------------------------
        // Motion
        // ------------------------------------------------------------------

        private void Update() => Animate();

        /// <summary>
        /// One frame of motion at <see cref="ViewClock.Now"/>. Unity calls it from
        /// Update; the screenshot harness, which runs in an editor that never
        /// calls Update, calls it itself after stepping the clock.
        /// </summary>
        public void Animate()
        {
            if (design == null || hull == null || spent) return;

            float now = ViewClock.Now;
            float deltaTime = ViewClock.Delta;

            if (dying && now >= diedAt)
            {
                if (!falling)
                {
                    falling = true;
                    fellAt = transform.position;
                    fellFacing = transform.rotation;
                    if (parts != null) Capture();
                }
                Fall(now);
                return;
            }
            if (swelling)
            {
                Swelling(now);
                return;
            }
            if (remembered) return;

            transform.localScale = Pop(now);
            if (burning) Embers(now);

            if (design.Building) Stand(now, deltaTime);
            else Move(now, deltaTime);
        }

        /// <summary>
        /// The spawn's scale: nothing before it starts, then up out of the ground
        /// past full size and back. Taller than wide on the way up, so it reads as
        /// rising rather than as inflating.
        /// </summary>
        private Vector3 Pop(float now)
        {
            float t = (now - spawnStarted) / SpawnSeconds;
            if (t >= 1f) return Vector3.one;
            if (t < 0f) return Vector3.one * 0.001f;

            // Up on a smoothstep to a tenth past full size at seven tenths of
            // the way, then settled back. A smoothstep rather than an ease out,
            // so the rise is still visibly rising a third of the way in.
            const float peak = 0.7f, over = 1.1f;
            float grow = t < peak
                ? over * Smooth(t / peak)
                : over - (over - 1f) * Smooth((t - peak) / (1f - peak));
            grow = Mathf.Max(grow, 0.001f);
            float stretch = Mathf.Sin(t * Mathf.PI) * 0.18f;
            return new Vector3(grow * (1f - stretch * 0.5f), grow * (1f + stretch), grow * (1f - stretch * 0.5f));
        }

        private void Move(float now, float deltaTime)
        {
            Quaternion before = transform.rotation;
            transform.rotation = Quaternion.RotateTowards(
                before, Quaternion.Euler(0f, targetYaw, 0f), TurnDegreesPerSecond * deltaTime);

            float size = design.Size;
            float gait = Mathf.Clamp01(speed / FullGaitSpeed);
            float phase = design.Stride > 0f ? travelled / design.Stride * 2f * Mathf.PI : 0f;
            float idle = 1f - gait;
            float breath = Mathf.Sin(now * 2.2f * tempo + seed);

            // Lean into a turn by how fast the body is turning, and into a start
            // or a stop by how fast its speed is changing. Both smoothed, so a
            // lean eases in and out instead of snapping with the frame.
            float yaw = transform.eulerAngles.y;
            float turnRate = deltaTime > 1e-5f ? Mathf.DeltaAngle(lastYaw, yaw) / deltaTime : 0f;
            float accel = deltaTime > 1e-5f ? (speed - lastSpeed) / deltaTime : 0f;
            lastYaw = yaw;
            lastSpeed = speed;
            bool airy = design.Gait == Gait.Float || design.Gait == Gait.Fly;
            float maxLean = airy ? 22f : 13f;
            float leanTarget = Mathf.Clamp(-turnRate * (airy ? 0.06f : 0.035f), -maxLean, maxLean) * Mathf.Max(gait, 0.3f);
            float surgeTarget = Mathf.Clamp(accel * 0.9f, -9f, 9f);
            float follow = deltaTime > 0f ? 1f - Mathf.Exp(-deltaTime / LeanSmoothingSeconds) : 0f;
            lean = Mathf.Lerp(lean, leanTarget, follow);
            surge = Mathf.Lerp(surge, surgeTarget, follow);

            Vector3 offset = Vector3.zero;
            Vector3 scale = Vector3.one;
            float pitch = surge;
            float roll = lean;
            float twist = 0f;

            switch (design.Gait)
            {
                case Gait.Walk:
                    // Highest as the legs pass under the hip, lowest at full stride.
                    offset.y = Mathf.Abs(Mathf.Cos(phase)) * 0.04f * size * gait;
                    scale = new Vector3(1f - breath * 0.015f * idle, 1f + breath * 0.035f * idle, 1f - breath * 0.015f * idle);
                    pitch += 5f * gait;
                    break;
                case Gait.Hop:
                {
                    // A ballistic hop a stride long: up on a parabola, a squash
                    // the moment it lands that springs out over a fifth of the hop,
                    // and a stretch while it is in the air.
                    float u = Mathf.Repeat(phase / (2f * Mathf.PI), 1f);
                    float air = 4f * u * (1f - u);
                    offset.y = air * 0.16f * size * gait;
                    float land = Mathf.Exp(-u * 11f) * 0.2f * gait;
                    float flight = air * 0.08f * gait;

                    // Standing, a slime settles under its own weight and gathers
                    // itself again, once every second and a half: a slow press
                    // with a quick release, large enough to differ between two
                    // frames an eighth of a second apart.
                    float press = 0.5f + 0.5f * Mathf.Sin(now * 4.2f * tempo + seed);
                    press = press * press * 0.11f * idle;

                    float squash = land + press - flight;
                    scale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
                    pitch += 6f * gait * (0.5f - u);
                    break;
                }
                case Gait.Float:
                    // Plus or minus 0.06 world units whatever the body's size, so a
                    // small spirit drifts as visibly as a large one.
                    offset.y = 0.05f * size + Mathf.Sin(now * 1.8f * tempo + seed) * 0.06f;
                    pitch += 12f * gait;
                    roll += Mathf.Sin(now * 1.3f * tempo + seed * 2f) * 4f * (0.4f + idle * 0.6f);
                    twist = Mathf.Sin(now * 0.9f * tempo + seed) * 5f * idle;
                    break;
                case Gait.Fly:
                    offset.y = Mathf.Sin(now * 1.6f * tempo + seed) * 0.08f * size;
                    pitch += 10f * gait;
                    roll += Mathf.Sin(now * 1.1f * tempo + seed * 2f) * 6f * (0.4f + idle * 0.6f);
                    break;
            }

            float armPitch = 0f;
            float attack = Phase(attackStarted, AttackSeconds, now);
            if (attack >= 0f)
            {
                // The wind-up: in over the first quarter, held at its peak, out
                // over the next tenth while the strike goes.
                float wind = attack < WindupShare
                    ? Smooth(attack / WindupShare)
                    : 1f - Smooth(Mathf.Clamp01((attack - WindupShare) / 0.12f));
                switch (design.Strike)
                {
                    case Strike.Lunge:
                    {
                        // Crouch and pull back for the wind-up, then throw the
                        // whole body forward past where it stood and bring the
                        // arm down through a swing of about 160 degrees.
                        float thrust = Bump(attack, WindupShare, 0.9f);
                        offset.z += (-0.12f * wind + 0.32f * thrust) * size;
                        offset.y += (-0.06f * wind + 0.04f * thrust) * size;
                        pitch += -14f * wind + 22f * thrust;
                        armPitch = -70f * wind + 90f * thrust;
                        scale = Vector3.Scale(scale, new Vector3(1f + 0.05f * wind, 1f - 0.1f * wind, 1f + 0.05f * wind));
                        break;
                    }
                    case Strike.Recoil:
                    {
                        // Brace for the wind-up — crouched, leaning in, the arm
                        // levelled — then a kick back at the shot and an easing
                        // return. The arm travels about 70 degrees from the brace
                        // to the top of the kick. The opposite direction and the
                        // opposite timing of a lunge, so the two never read alike.
                        float kick = attack < WindupShare ? 0f : 1f - (attack - WindupShare) / (1f - WindupShare);
                        kick = kick * kick * kick;
                        offset.z += (0.04f * wind - 0.15f * kick) * size;
                        offset.y -= 0.05f * wind * size;
                        pitch += 7f * wind - 12f * kick;
                        armPitch = 30f * wind - 40f * kick;
                        scale = Vector3.Scale(scale, new Vector3(1f + 0.05f * wind + 0.06f * kick, 1f - 0.1f * wind - 0.08f * kick, 1f + 0.05f * wind - 0.04f * kick));
                        break;
                    }
                    case Strike.Pulse:
                    {
                        // Gather in for the wind-up, swell past rest, contract
                        // below it, settle: the whole body is the weapon.
                        float swell = 0.28f * Bump(attack, WindupShare, 0.65f) - 0.12f * Bump(attack, 0.6f, 0.95f);
                        scale = Vector3.Scale(scale, new Vector3(1f + 0.1f * wind, 1f - 0.14f * wind, 1f + 0.1f * wind));
                        scale *= 1f + swell;
                        offset.y += 0.06f * Bump(attack, WindupShare, 0.65f) * size;
                        break;
                    }
                }
            }

            float washToward = 0f;
            float hit = Phase(hitStarted, HitSeconds, now);
            if (hit >= 0f)
            {
                // Knocked back and squashed, and lit for the first instant: the
                // flash is what says "hit" at a distance, the squash up close.
                float knock = Bump(hit, 0f, 0.7f);
                offset.z -= 0.12f * knock * size;
                scale = Vector3.Scale(scale, new Vector3(1f + 0.14f * knock, 1f - 0.2f * knock, 1f + 0.14f * knock));
                pitch -= 14f * knock;
                float flash = Mathf.Clamp01(1f - hit / 0.45f);
                washToward = 0.45f * flash * flash;
            }
            Look(BodyPalette.HitFlash, washToward, 1f);

            if (slowed && !dying) scale *= 1f - SlowShrink;

            hull.localPosition = hullRest + offset;
            hull.localRotation = Quaternion.Euler(pitch, twist, roll);
            hull.localScale = scale;

            if (arm != null) arm.localRotation = Quaternion.Euler(armPitch + breath * 3f * idle, 0f, 0f);

            Limbs(now, phase, gait, size);
        }

        private void Limbs(float now, float phase, float gait, float size)
        {
            if (limbLeft == null && limbRight == null) return;

            switch (design.Limbs)
            {
                case LimbMotion.Swing:
                {
                    // Positive pitch swings a foot back. A leg is planted while
                    // its foot sweeps back, which with this sign is the half of
                    // the cycle the stride is measured over.
                    float swing = Mathf.Sin(phase) * SwingDegrees * gait;
                    if (limbLeft != null) limbLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
                    if (limbRight != null) limbRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                    break;
                }
                case LimbMotion.Step:
                {
                    // Lifted while it swings forward, planted while it slides
                    // back. The back slide is a quarter stride either way of the
                    // hip, so it covers exactly the ground the body does.
                    float reach = design.Stride * 0.25f * gait, lift = 0.07f * size * gait;
                    if (limbLeft != null)
                        limbLeft.localPosition = limbLeftRest + new Vector3(
                            0f, Mathf.Max(0f, Mathf.Sin(phase)) * lift, -Mathf.Cos(phase) * reach);
                    if (limbRight != null)
                        limbRight.localPosition = limbRightRest + new Vector3(
                            0f, Mathf.Max(0f, -Mathf.Sin(phase)) * lift, Mathf.Cos(phase) * reach);
                    break;
                }
                case LimbMotion.Flap:
                {
                    // A flyer beats whether or not it travels — it is holding
                    // itself up — and beats harder and faster when it does.
                    float rate = design.Flying ? 7f + 3f * gait : 3.2f;
                    float sweep = design.Flying ? 26f + 12f * gait : 16f;
                    float beat = Mathf.Sin(now * rate * tempo + seed) * sweep;
                    if (limbLeft != null) limbLeft.localRotation = Quaternion.Euler(0f, 0f, -beat);
                    if (limbRight != null) limbRight.localRotation = Quaternion.Euler(0f, 0f, beat);
                    break;
                }
            }
        }

        /// <summary>
        /// A building never walks. It flinches when hit, swells or kicks its
        /// crown when it fires, turns its crown if the crown is a turret, and
        /// while it is a site its parts go up one by one. Finished, it settles;
        /// at work, it turns or pulses; delivering a unit, it swells and leans.
        /// </summary>
        private void Stand(float now, float deltaTime)
        {
            if (parts != null && progress < 1f) PoseAssembly(now);

            Vector3 scale = Vector3.one;

            float washToward = 0f;
            float hit = Phase(hitStarted, HitSeconds, now);
            if (hit >= 0f)
            {
                float flinch = Bump(hit, 0f, 0.7f);
                scale = new Vector3(1f + 0.03f * flinch, 1f - 0.05f * flinch, 1f + 0.03f * flinch);
                float flash = Mathf.Clamp01(1f - hit / 0.45f);
                washToward = 0.35f * flash * flash;
            }
            Look(BodyPalette.HitFlash, washToward, 1f);

            // A shot is a kick at once and an easing return, like a unit's recoil.
            float attack = Phase(attackStarted, AttackSeconds, now);
            float fire = attack >= 0f ? (1f - attack) * (1f - attack) : 0f;
            if (crown == null && attack >= 0f) scale *= 1f + 0.05f * Bump(attack, 0f, 1f);

            // Finished: down onto its foundations and back up past rest, once.
            float settle = Phase(settledAt, SettleSeconds, now);
            if (settle >= 0f)
            {
                float press = Bump(settle, 0f, 0.55f) - 0.4f * Bump(settle, 0.55f, 1f);
                scale = Vector3.Scale(scale, new Vector3(1f + 0.06f * press, 1f - 0.12f * press, 1f + 0.06f * press));
            }

            bool spins = crown != null && design.CrownSpin != 0f && !design.CrownAims;
            float rate = deltaTime > 0f ? deltaTime / WorkRamp : 0f;
            work = Mathf.MoveTowards(work, working && progress >= 1f ? 1f : 0f, rate);
            if (!spins && work > 0f) scale *= 1f + WorkPulse * work * Mathf.Sin(now * WorkPulseRate * tempo + seed);
            // Motes whether or not the crown turns: a crown turning three times
            // faster is plain in motion and invisible in a still, and a still at
            // 64 px is what the strips judge by.
            if (working && progress >= 1f && parts != null) Motes(now);

            Quaternion lean = Quaternion.identity;
            float deliver = Phase(deliverAt, DeliverSeconds, now);
            if (deliver >= 0f)
            {
                // Swollen fast and let go slowly, leaning out toward the door
                // the unit used and back.
                float swell = Bump(deliver, 0f, 0.6f);
                scale *= 1f + DeliverSwell * swell;
                Vector3 toward = Quaternion.Inverse(transform.rotation) * deliverToward;
                float tip = Mathf.Tan(DeliverLean * Bump(deliver, 0f, 1f) * Mathf.Deg2Rad);
                lean = Quaternion.FromToRotation(Vector3.up, (Vector3.up + toward * tip).normalized);
            }
            hull.localScale = scale;
            hull.localRotation = lean;

            if (crown == null) return;

            Quaternion turn = crown.localRotation;
            if (design.CrownAims && aiming)
            {
                // The crown is a child of the hull, so its local yaw is the aim
                // less whatever the root is turned by.
                float local = aimYaw - transform.eulerAngles.y;
                Quaternion yaw = Quaternion.Euler(0f, local, 0f);
                Quaternion current = Quaternion.Euler(0f, CurrentYaw(Quaternion.Inverse(crownTwist) * crown.localRotation), 0f);
                turn = Quaternion.RotateTowards(current, yaw, TurnDegreesPerSecond * 0.5f * deltaTime);
            }
            else if (design.CrownSpin != 0f)
            {
                spin += design.CrownSpin * (1f + (WorkSpin - 1f) * work) * deltaTime;
                turn = Quaternion.Euler(0f, spin + seed * 100f, 0f);
            }
            else
            {
                turn = Quaternion.Euler(0f, CurrentYaw(Quaternion.Inverse(crownTwist) * crown.localRotation), 0f);
            }

            crown.localRotation = crownTwist * turn * Quaternion.Euler(design.CrownKick * fire, 0f, 0f);
            crown.localPosition = crownRest + crownLift + turn * new Vector3(0f, 0f, -0.18f * fire);
            crown.localScale = crownSize;
        }

        /// <summary>
        /// A mote every <see cref="MoteInterval"/> off a working building's roof,
        /// each from a different side of it. Only while it is seen: a building
        /// at work in the fog gives nothing away.
        /// </summary>
        private void Motes(float now)
        {
            if (effects == null || !shown || remembered) return;
            if (now < nextMote) return;
            // A building that has just started, or just come into sight, starts now
            // rather than paying out every mote it missed.
            if (now - nextMote > MoteInterval) nextMote = now;
            nextMote += MoteInterval;
            motes++;
            float around = seed * 2.3f + motes * 2.4f;
            float roof = (design.Top - 0.25f) * 0.85f;
            Vector3 at = transform.position + Vector3.up * roof +
                         new Vector3(Mathf.Sin(around), 0f, Mathf.Cos(around)) * (design.Footprint * 0.2f);
            effects.Mote(at, design.Mote, design.Footprint);
        }

        /// <summary>
        /// An ember every <see cref="EmberInterval"/> off a burning body, from
        /// around its upper half, each from a different side. Only while it is
        /// seen, as a working building's motes are.
        /// </summary>
        private void Embers(float now)
        {
            if (effects == null || !shown || dying || now < nextEmber) return;
            if (now - nextEmber > EmberInterval) nextEmber = now;
            nextEmber += EmberInterval;
            embers++;
            float around = seed * 1.7f + embers * 2.4f;
            // Top counts a flier's hover, and its root is already up there.
            float top = design.Building ? (design.Top - 0.25f) * 0.85f
                      : (design.Top - (design.Flying ? HoverHeight : 0f)) * 0.7f;
            Vector3 at = transform.position + Vector3.up * top +
                         new Vector3(Mathf.Sin(around), 0f, Mathf.Cos(around)) * (design.Footprint * 0.22f);
            effects.Ember(at, BodyPalette.Hellfire.Ember, EmberSize);
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        /// <summary>
        /// Every part at its pose for the current progress. Part k of n starts
        /// at k steps of the hull's share and takes <see cref="PartSteps"/>
        /// steps; the crown takes the last tenth. Finished, everything is at rest.
        /// </summary>
        private void PoseAssembly(float now)
        {
            bool done = progress >= 1f;
            int count = parts.Length;
            float span = crown != null ? 1f - CrownShare : 1f;
            float step = span / (count - 1 + PartSteps);
            for (int k = 0; k < count; k++)
            {
                float u = done ? 1f : Mathf.Clamp01((progress - k * step) / (step * PartSteps));
                if (k == 0) u = Mathf.Max(u, SiteFloor);
                Put(k, u, now, partRest[k], partHeight[k], out Vector3 position, out Quaternion turn, out Vector3 scale);
                Transform part = parts[k];
                part.localPosition = position;
                part.localRotation = turn;
                part.localScale = scale;
            }

            if (crown == null) return;
            float c = done ? 1f : Mathf.Clamp01((progress - span) / CrownShare);
            Put(count, c, now, crownRest, crownHeight, out Vector3 at, out crownTwist, out crownSize);
            crownLift = at - crownRest;
            if (done) crownTwist = Quaternion.identity;
        }

        /// <summary>
        /// Part <paramref name="k"/>, resting at <paramref name="rest"/> and
        /// <paramref name="height"/> tall, <paramref name="u"/> of the way through
        /// going up, in the faction's way. Nothing before it starts.
        /// </summary>
        private void Put(int k, float u, float now, Vector3 rest, float height,
                         out Vector3 position, out Quaternion turn, out Vector3 scale)
        {
            position = rest;
            turn = Quaternion.identity;
            scale = Vector3.one;
            if (u >= 1f) return;
            if (u <= 0f)
            {
                scale = Vector3.one * 0.001f;
                return;
            }

            float side = k % 2 == 0 ? 1f : -1f;
            switch (design.Assembly)
            {
                case Assembly.Grow:
                {
                    // Up out of a thin shoot, filling out as it rises, a little
                    // past full at the end.
                    float over = 1f + 0.08f * Bump(u, 0.55f, 1f);
                    float wide = Mathf.Lerp(0.3f, 1f, EaseOut(u)) * over;
                    scale = new Vector3(wide, Mathf.Max(Smooth(u), 0.001f) * over, wide);
                    break;
                }
                case Assembly.Tear:
                {
                    // Up out of the ground from its own height below, fast at
                    // first, shaking all the way and slowing as it clears.
                    float up = EaseOut(u);
                    float shake = (1f - u) * 0.06f * Mathf.Max(height, 0.5f);
                    position = rest + new Vector3(Mathf.Sin(now * 61f + k * 1.7f) * shake,
                                                  -height * 1.05f * (1f - up),
                                                  Mathf.Sin(now * 47f + k) * shake * 0.6f);
                    turn = Quaternion.Euler(0f, 0f, side * 9f * (1f - up) * Mathf.Sin(now * 23f + k));
                    break;
                }
                case Assembly.Inflate:
                {
                    // Swelled past full, sagged under it, settled: a bubble
                    // finding its size.
                    float s = u < 0.55f ? 1.16f * Smooth(u / 0.55f)
                        : u < 0.8f ? Mathf.Lerp(1.16f, 0.94f, Smooth((u - 0.55f) / 0.25f))
                        : Mathf.Lerp(0.94f, 1f, Smooth((u - 0.8f) / 0.2f));
                    s = Mathf.Max(s, 0.001f);
                    float wobble = Mathf.Sin(u * Mathf.PI * 3f) * 0.07f * (1f - u);
                    scale = new Vector3(s * (1f + wobble), s * (1f - wobble), s * (1f + wobble));
                    break;
                }
                case Assembly.Stack:
                case Assembly.Close when k == 0:
                {
                    if (k == 0)
                    {
                        // The footing is heaped up where it lies, not dropped
                        // from above or swung in: a site marks its ground first.
                        scale = new Vector3(1f, Mathf.Max(Smooth(u), 0.001f), 1f);
                        break;
                    }
                    // Dropped from above, tumbling a little, onto the pile; a
                    // squash where it lands.
                    const float land = 0.7f;
                    if (u < land)
                    {
                        float f = u / land;
                        float left = 1f - f;
                        position.y += (1.2f + height * 0.4f) * (1f - f * f);
                        turn = Quaternion.Euler(side * 16f * left, 35f * left, side * 12f * left);
                        scale = Vector3.one * Mathf.Min(1f, u / 0.1f);
                    }
                    else
                    {
                        float press = Bump(u, land, 1f);
                        scale = new Vector3(1f + 0.1f * press, 1f - 0.18f * press, 1f + 0.1f * press);
                    }
                    break;
                }
                case Assembly.Close:
                {
                    // Open, then shut a little too far, then true. A plate
                    // standing off to one side swings in on its foot; a ring of
                    // plates round the middle closes in and twists shut.
                    float shut = u < 0.8f ? 1.06f * Smooth(u / 0.8f) : Mathf.Lerp(1.06f, 1f, (u - 0.8f) / 0.2f);
                    float open = 1f - shut;
                    float appear = Smooth(Mathf.Clamp01(u / 0.15f));
                    if (Mathf.Abs(rest.x) > 0.05f)
                    {
                        turn = Quaternion.Euler(0f, 0f, -Mathf.Sign(rest.x) * 80f * open);
                        scale = Vector3.one * Mathf.Max(appear, 0.001f);
                    }
                    else
                    {
                        turn = Quaternion.Euler(0f, side * 70f * open, 0f);
                        float spread = (1f + 0.7f * open) * appear;
                        scale = new Vector3(Mathf.Max(spread, 0.001f), Mathf.Max(Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(shut)) * appear, 0.001f),
                                            Mathf.Max(spread, 0.001f));
                    }
                    break;
                }
                default:
                    // Laid up from its foot, square, with no overshoot: a course
                    // of stone goes on and stays where it is put.
                    scale = new Vector3(1f, Mathf.Max(EaseOut(u), 0.001f), 1f);
                    break;
            }
        }

        /// <summary>
        /// The death, from <see cref="Die"/> to switched off. Four ways down,
        /// each the body's own: a walker tips over and collapses with its legs
        /// splayed, a flyer drops out of the air and bounces once, a building
        /// comes apart (<see cref="Raze"/>), and a golem breaks into the
        /// rubble it leaves (the chunks are Effects.Crumble; the body itself only
        /// shrinks away under them).
        ///
        /// Opaque for the first sixty percent: a see-through body is a ghost, not
        /// a fall, and the see-through material also drops the shadow. Only the
        /// last forty percent fades.
        ///
        /// The root carries the tip and the sink, so legs hanging from it go down
        /// with the body. The hull carries the collapse, so the non-uniform scale
        /// is applied under the rotation and nothing riding the hull is sheared
        /// off it.
        /// </summary>
        private void Fall(float now)
        {
            if (parts != null)
            {
                Raze(now);
                return;
            }

            float t = (now - diedAt) / DeathSeconds;
            if (t >= 1f)
            {
                Spend();
                return;
            }
            t = Mathf.Clamp01(t);

            float size = design.Size;
            Vector3 offset = Vector3.zero;
            Vector3 squash = Vector3.one;
            float sag = 0f;
            float pitch = 0f, roll = 0f;
            float splay = 0f;
            float opacity = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
            float washed = 0f;
            Color toward = BodyPalette.HitFlash;

            if (rubble)
            {
                // Rubble-coloured at once, then shrunk away over the first
                // three quarters while the chunks Effects.Crumble throws fall
                // around it, so one frame holds both the body and its pieces.
                float crumble = Smooth(Mathf.Clamp01(t / 0.45f));
                washed = 0.8f * Mathf.Clamp01(t / 0.1f);
                toward = UiStyle.Remnant;
                float left = Mathf.Max(1f - crumble, 0.02f);
                squash = new Vector3(left * (1f + 0.3f * crumble), left * left, left * (1f + 0.3f * crumble));
                opacity = 1f;
            }
            else if (design.Building)
            {
                // A resource node run dry: it shudders and settles into the
                // ground, with nothing to break off and nothing inside.
                float shake = (1f - t) * 0.06f * design.Footprint;
                offset.x = Mathf.Sin(now * 57f + seed) * shake;
                offset.z = Mathf.Sin(now * 43f + seed * 3f) * shake * 0.6f;
                offset.y = -t * t * Mathf.Max(design.Top - 0.25f, 0.5f) * 0.6f;
                squash.y = 1f - 0.3f * t;
                roll = Mathf.Sin(now * 31f + seed) * 2.5f * (1f - t);
            }
            else if (design.Flying)
            {
                // Out of the air, nose down and rolling; one bounce off the
                // ground with a squash on each landing; then down into it.
                const float land = 0.3f, rest = 0.5f;
                float drop = Mathf.Clamp01(t / land);
                float bounce = t > land && t < rest ? (t - land) / (rest - land) : -1f;
                float sink = Mathf.Clamp01((t - rest) / (1f - rest));
                offset.y = -HoverHeight * drop * drop - sink * sink * size * 0.55f;
                if (bounce >= 0f) offset.y += 4f * bounce * (1f - bounce) * 0.2f * size;
                float impact = Mathf.Max(Spike(t, land, 0.07f), Spike(t, rest, 0.07f));
                pitch = 25f * drop;
                roll = side * 50f * drop;
                float collapse = Smooth(sink);
                squash = new Vector3(1f + 0.2f * impact + 0.15f * collapse, 1f - 0.3f * impact - 0.4f * collapse,
                                     1f + 0.2f * impact + 0.15f * collapse);
                splay = drop;
            }
            else
            {
                // Over onto one side, the hull buckling down into the hip and
                // the legs splaying out, then down into the ground. The tip is
                // what reads as dying at a distance; the sink says it will not
                // get up again.
                float tip = EaseOut(Mathf.Clamp01(t / 0.35f));
                float collapse = EaseOut(Mathf.Clamp01((t - 0.08f) / 0.4f));
                float sink = Mathf.Clamp01((t - 0.35f) / 0.65f);
                roll = side * 55f * tip;
                pitch = -10f * tip;
                squash = new Vector3(1f + 0.12f * collapse, 1f - 0.35f * collapse, 1f + 0.12f * collapse);
                sag = -0.08f * size * collapse;
                offset.y = -sink * sink * size * 0.55f;
                splay = collapse;
            }

            transform.position = fellAt + offset;
            transform.rotation = fellFacing * Quaternion.Euler(pitch, 0f, roll);
            transform.localScale = Vector3.one;
            hull.localPosition = hullRest + new Vector3(0f, sag, 0f);
            hull.localRotation = Quaternion.identity;
            hull.localScale = squash;
            if (arm != null) arm.localRotation = Quaternion.identity;
            Splay(splay);
            Look(toward, washed, opacity);
        }

        /// <summary>Every part's pose the moment the building starts to fall: a site falls from where it had got to.</summary>
        private void Capture()
        {
            for (int k = 0; k < parts.Length; k++)
            {
                partFrom[k] = parts[k].localPosition;
                partFromTurn[k] = parts[k].localRotation;
                partFromScale[k] = parts[k].localScale;
            }
            if (crown == null) return;
            crownFrom = crown.localPosition;
            crownFromTurn = crown.localRotation;
            crownFromScale = crown.localScale;
        }

        /// <summary>
        /// A building coming down, over <see cref="DestroySeconds"/> (issue
        /// #186): it shudders; its upper parts and its crown break off one after
        /// another and topple out and down, each its own way, while chunks of
        /// it are thrown (Effects.Crumble); a ring of dust runs out as they land;
        /// what is left sinks into the ground and fades in the last thirty
        /// percent. A building with a light inside (지옥불's ember, 유랑종's other
        /// world) flashes it once as it breaks and goes dark.
        /// </summary>
        private void Raze(float now)
        {
            float t = (now - diedAt) / DestroySeconds;
            if (t >= 1f)
            {
                Spend();
                return;
            }
            t = Mathf.Clamp01(t);

            // What is left after the parts break off is the foundations, so the
            // sink goes as deep as they are tall: a low slab is not driven a
            // whole tower's height into the ground in the first frame of it.
            float footprint = design.Footprint;
            float shudder = 1f - Smooth(Mathf.Clamp01(t / 0.3f));
            float shake = shudder * 0.07f * footprint;
            float sink = Smooth(Mathf.Clamp01((t - 0.45f) / 0.55f));
            var offset = new Vector3(Mathf.Sin(now * 57f + seed) * shake, -sink * Mathf.Min(partHeight[0], 1.5f) * 0.9f,
                                     Mathf.Sin(now * 43f + seed * 3f) * shake * 0.6f);
            transform.position = fellAt + offset;
            transform.rotation = fellFacing * Quaternion.Euler(0f, 0f, Mathf.Sin(now * 31f + seed) * 3f * shudder);
            transform.localScale = Vector3.one;
            hull.localPosition = hullRest;
            hull.localRotation = Quaternion.identity;
            hull.localScale = new Vector3(1f + 0.1f * sink, 1f - 0.4f * sink, 1f + 0.1f * sink);

            // The foundations stay; everything above them breaks away, the top
            // first, so the building comes apart from the top down.
            for (int k = 1; k < parts.Length; k++)
            {
                Break(parts[k], parts.Length - k, t, partFrom[k], partFromTurn[k], partFromScale[k], partHeight[k], k);
            }
            if (crown != null) Break(crown, 0, t, crownFrom, crownFromTurn, crownFromScale, crownHeight, parts.Length);

            if (!crumbled && t >= 0.15f)
            {
                crumbled = true;
                if (effects != null)
                {
                    int pieces = Mathf.Clamp(Mathf.RoundToInt(footprint), 2, 4);
                    effects.Crumble(fellAt, footprint * 0.85f, design.Debris, fellFacing.eulerAngles.y + seed * 40f, 0f, pieces);
                }
            }
            if (!dusted && t >= 0.32f)
            {
                dusted = true;
                if (effects != null) effects.Dust(fellAt, footprint * 0.45f, Dust);
            }

            float opacity = 1f - Mathf.Clamp01((t - 0.7f) / 0.3f);
            Color toward;
            float washed;
            if (design.Glow.a > 0f && t < 0.27f)
            {
                toward = design.Glow;
                washed = 0.8f * Spike(t, 0.15f, 0.12f);
            }
            else
            {
                // Dark and dusty: the light is out, or there never was one.
                toward = BodyPalette.Darker(design.Debris, 0.4f);
                float dark = design.Glow.a > 0f ? 0.55f : 0.3f;
                washed = dark * Smooth(Mathf.Clamp01((t - 0.27f) / 0.25f));
            }
            Look(toward, washed, opacity);
        }

        /// <summary>
        /// One part breaking off: after a wait set by its <paramref name="order"/>
        /// (0 goes first), it slides out from the building, tips over the way it
        /// slides, drops, and shrinks a little as it goes into the ground.
        /// </summary>
        private void Break(Transform part, int order, float t, Vector3 from, Quaternion fromTurn, Vector3 fromScale,
                           float height, int index)
        {
            float b = Mathf.Clamp01((t - 0.1f - 0.05f * order) / 0.45f);
            float yaw = seed * 57f + index * 137.5f;
            var away = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            part.localPosition = from + away * ((0.45f * height + 0.3f) * EaseOut(b)) +
                                 Vector3.down * ((height * 1.1f + 0.4f) * b * b);
            part.localRotation = Quaternion.AngleAxis(75f * b * b, Vector3.Cross(Vector3.up, away)) * fromTurn;
            part.localScale = fromScale * (1f - 0.35f * b);
        }

        /// <summary>
        /// Limbs going limp as a body dies: legs out to the sides, wings and fins
        /// drooping, by <paramref name="amount"/> from 0 to 1.
        /// </summary>
        private void Splay(float amount)
        {
            if (limbLeft == null && limbRight == null) return;
            float degrees = design.Limbs == LimbMotion.Flap ? 30f : 35f;
            float angle = amount * degrees;
            if (design.Limbs == LimbMotion.Flap) angle = -angle; // a wing droops the other way a leg splays
            if (limbLeft != null)
            {
                limbLeft.localPosition = limbLeftRest;
                limbLeft.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
            if (limbRight != null)
            {
                limbRight.localPosition = limbRightRest;
                limbRight.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        /// <summary>A growth's old body: up a quarter and lightening, then gone.</summary>
        private void Swelling(float now)
        {
            float t = (now - swellStarted) / SwellSeconds;
            if (t >= 1f)
            {
                Spend();
                return;
            }
            t = Mathf.Clamp01(t);
            float grow = 1f + 0.25f * Mathf.Sin(t * Mathf.PI * 0.5f);
            transform.localScale = new Vector3(grow, grow, grow);
            Look(BodyPalette.HitFlash, 0.5f * t, 1f);
        }

        /// <summary>The end of a death or a swell: nothing drawn, nothing animated.</summary>
        private void Spend()
        {
            spent = true;
            Show(false);
            enabled = false;
        }

        /// <summary>The yaw part of a crown's rotation, so the fire pitch of one frame never leaks into the next.</summary>
        private static float CurrentYaw(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-8f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
        }

        /// <summary>Every moving part at its rest pose. What a remembered body is frozen at.</summary>
        private void Rest()
        {
            if (hull == null) return;
            hull.localPosition = hullRest;
            hull.localRotation = Quaternion.identity;
            hull.localScale = Vector3.one;
            if (arm != null) arm.localRotation = Quaternion.identity;
            if (limbLeft != null) { limbLeft.localPosition = limbLeftRest; limbLeft.localRotation = Quaternion.identity; }
            if (limbRight != null) { limbRight.localPosition = limbRightRest; limbRight.localRotation = Quaternion.identity; }
            if (crown != null) crown.localPosition = crownRest + crownLift;
            transform.localScale = Vector3.one;
            washAmount = 0f;
            fade = 1f;
        }

        /// <summary>How far through a one-shot motion begun at <paramref name="started"/> we are, 0..1, or -1 when it is over.</summary>
        private static float Phase(float started, float seconds, float now)
        {
            float t = (now - started) / seconds;
            return t >= 0f && t <= 1f ? t : -1f;
        }

        /// <summary>A half sine across [from, to] of a 0..1 phase, zero outside it: one smooth push and return.</summary>
        private static float Bump(float t, float from, float to)
        {
            if (t <= from || t >= to) return 0f;
            float local = (t - from) / (to - from);
            return Mathf.Sin(local * Mathf.PI);
        }

        /// <summary>A short triangular spike, 1 at <paramref name="at"/> and 0 more than <paramref name="width"/> either side.</summary>
        private static float Spike(float t, float at, float width) => Mathf.Max(0f, 1f - Mathf.Abs(t - at) / width);

        /// <summary>Slow, fast, slow: the smoothstep on 0..1.</summary>
        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Fast at first, slow at the end: a quadratic ease out on 0..1.</summary>
        internal static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }
}
