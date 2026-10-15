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
        private const float AttackSeconds = 0.36f;
        private const float HitSeconds = 0.3f;
        private const float DeathSeconds = 0.6f;
        private const float SpawnSeconds = 0.3f;

        /// <summary>The ghost alpha of a site under construction, as UiStyle.SiteAlpha was for the sprite view.</summary>
        private const float SiteAlpha = 0.75f;

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
        private bool swelling;
        private float swellStarted;
        private bool spent;

        private float progress = 1f;
        private float siteHeight = 1f;
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
            side = (variation & 1) == 0 ? 1f : -1f;
            targetYaw = transform.eulerAngles.y;
            lastYaw = targetYaw;
            aimYaw = targetYaw;

            var made = new System.Collections.Generic.List<MeshRenderer>(5);
            hull = Part("Hull", design.Hull, transform, Vector3.zero, made);
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

        public void Hit() => hitStarted = ViewClock.Now;

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
        /// </summary>
        public void Die(bool toRubble)
        {
            if (dying || design == null) return;
            dying = true;
            rubble = toRubble;
            diedAt = ViewClock.Now;
            fellAt = transform.position;
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
        /// Construction from 0 to 1. The body grows out of the ground — scaled up
        /// from its feet, which is the same thing as rising — and stays a ghost
        /// until it is finished, as a site did in the sprite view.
        /// </summary>
        public void Progress(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Approximately(t, progress)) return;
            progress = t;
            siteHeight = t >= 1f ? 1f : Mathf.Lerp(0.15f, 1f, t);
            transform.localScale = new Vector3(1f, siteHeight, 1f);
            ApplyLook();
        }

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

            if (dying)
            {
                Fall(now);
                return;
            }
            if (swelling)
            {
                Swelling(now);
                return;
            }
            if (remembered) return;

            Vector3 pop = Pop(now);
            transform.localScale = new Vector3(pop.x, pop.y * siteHeight, pop.z);

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
                    scale = new Vector3(1f - breath * 0.008f * idle, 1f + breath * 0.018f * idle, 1f - breath * 0.008f * idle);
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

                    // Standing, a slime settles under its own weight now and
                    // then: a short press and release every second and a half.
                    float press = Mathf.Max(0f, Mathf.Sin(now * 4.2f * tempo + seed));
                    press = press * press * press * 0.07f * idle;

                    float squash = land + press - flight;
                    scale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
                    pitch += 6f * gait * (0.5f - u);
                    break;
                }
                case Gait.Float:
                    offset.y = (0.05f + Mathf.Sin(now * 1.8f * tempo + seed) * 0.04f) * size;
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
                switch (design.Strike)
                {
                    case Strike.Lunge:
                    {
                        // Draw back, then throw the whole body forward past where
                        // it stood and bring the arm down through the swing.
                        float draw = Bump(attack, 0f, 0.38f);
                        float thrust = Bump(attack, 0.28f, 0.9f);
                        offset.z += (-0.1f * draw + 0.3f * thrust) * size;
                        offset.y += 0.05f * thrust * size;
                        pitch += -12f * draw + 20f * thrust;
                        armPitch = -60f * draw + 95f * thrust;
                        break;
                    }
                    case Strike.Recoil:
                    {
                        // A kick back at the instant of the shot and an easing
                        // return: the opposite of a lunge's direction and of its
                        // timing, so the two never read alike.
                        float kick = 1f - attack;
                        kick = kick * kick * kick;
                        offset.z -= 0.14f * kick * size;
                        pitch -= 16f * kick;
                        armPitch = -40f * kick;
                        scale = Vector3.Scale(scale, new Vector3(1f + 0.06f * kick, 1f - 0.08f * kick, 1f - 0.04f * kick));
                        break;
                    }
                    case Strike.Pulse:
                    {
                        // Swell, then contract below rest, then settle: the
                        // whole body is the weapon.
                        float swell = 0.26f * Bump(attack, 0f, 0.5f) - 0.12f * Bump(attack, 0.45f, 0.95f);
                        scale *= 1f + swell;
                        offset.y += 0.06f * Bump(attack, 0f, 0.5f) * size;
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
                washToward = 0.55f * flash * flash;
            }
            Look(BodyPalette.HitFlash, washToward, 1f);

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
        /// crown when it fires, and turns its crown if the crown is a turret.
        /// </summary>
        private void Stand(float now, float deltaTime)
        {
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
            if (crown == null) scale *= 1f + 0.05f * Bump(Mathf.Max(attack, 0f), 0f, 1f) * (attack >= 0f ? 1f : 0f);
            hull.localScale = scale;

            if (crown == null) return;

            Quaternion turn = crown.localRotation;
            if (design.CrownAims && aiming)
            {
                // The crown is a child of the hull, so its local yaw is the aim
                // less whatever the root is turned by.
                float local = aimYaw - transform.eulerAngles.y;
                Quaternion yaw = Quaternion.Euler(0f, local, 0f);
                Quaternion current = Quaternion.Euler(0f, CurrentYaw(crown.localRotation), 0f);
                turn = Quaternion.RotateTowards(current, yaw, TurnDegreesPerSecond * 0.5f * deltaTime);
            }
            else if (design.CrownSpin != 0f)
            {
                turn = Quaternion.Euler(0f, now * design.CrownSpin + seed * 100f, 0f);
            }
            else
            {
                turn = Quaternion.Euler(0f, CurrentYaw(crown.localRotation), 0f);
            }

            crown.localRotation = turn * Quaternion.Euler(design.CrownKick * fire, 0f, 0f);
            crown.localPosition = crownRest + turn * new Vector3(0f, 0f, -0.18f * fire);
        }

        /// <summary>
        /// The death, from <see cref="Die"/> to switched off. Four ways down,
        /// each the body's own: a walker crumples sideways and sinks, a flyer
        /// drops out of the air first, a building shudders and settles into the
        /// ground, and a golem becomes the rubble it leaves.
        /// </summary>
        private void Fall(float now)
        {
            float t = (now - diedAt) / DeathSeconds;
            if (t >= 1f)
            {
                Spend();
                return;
            }
            t = Mathf.Clamp01(t);

            float size = design.Building ? Mathf.Max(design.Top - 0.25f, 0.5f) : design.Size;
            Vector3 offset = Vector3.zero;
            Vector3 scale = Vector3.one;
            float pitch = 0f, roll = 0f;
            float opacity = 1f;
            float washed = 0f;
            Color toward = BodyPalette.HitFlash;

            if (rubble)
            {
                // Grey first, then down into a heap: wide and low, the shape of
                // the pile that is already lying on the cell.
                float crumble = EaseOut(Mathf.Clamp01(t / 0.55f));
                washed = 0.7f * Mathf.Clamp01(t / 0.3f);
                toward = UiStyle.Remnant;
                scale = new Vector3(1f + 0.35f * crumble, 1f - 0.72f * crumble, 1f + 0.35f * crumble);
                offset.y = -0.12f * size * t * t;
                roll = side * 8f * crumble;
                opacity = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);
            }
            else if (design.Building)
            {
                float shake = (1f - t) * 0.06f * design.Footprint;
                offset.x = Mathf.Sin(now * 57f + seed) * shake;
                offset.z = Mathf.Sin(now * 43f + seed * 3f) * shake * 0.6f;
                offset.y = -t * t * size * 0.6f;
                scale.y = 1f - 0.3f * t;
                roll = Mathf.Sin(now * 31f + seed) * 2.5f * (1f - t);
                opacity = 1f - Mathf.Clamp01((t - 0.35f) / 0.65f);
            }
            else if (design.Flying)
            {
                // Out of the air in the first half, accelerating, nose down and
                // rolling; then into the ground like anything else.
                float drop = Mathf.Clamp01(t / 0.45f);
                float sink = Mathf.Clamp01((t - 0.45f) / 0.55f);
                offset.y = -HoverHeight * drop * drop - sink * sink * size * 0.6f;
                pitch = 25f * drop;
                roll = side * 55f * drop;
                scale = new Vector3(1f + 0.2f * sink, 1f - 0.45f * sink, 1f + 0.2f * sink);
                opacity = 1f - sink;
            }
            else
            {
                // Topple to one side and buckle in the first part, then sink and
                // fade. The topple is what reads as dying at a distance; the sink
                // is what says the body will not get up again.
                float buckle = EaseOut(Mathf.Clamp01(t / 0.4f));
                float sink = Mathf.Clamp01((t - 0.2f) / 0.8f);
                roll = side * 38f * buckle;
                pitch = -12f * buckle;
                scale = new Vector3(1f + 0.15f * buckle, 1f - 0.4f * buckle, 1f + 0.15f * buckle);
                offset.y = -sink * sink * size * 0.7f;
                opacity = 1f - Mathf.Clamp01((t - 0.3f) / 0.7f);
            }

            transform.position = fellAt + offset;
            transform.localScale = new Vector3(scale.x, scale.y * siteHeight, scale.z);
            hull.localPosition = hullRest;
            hull.localRotation = Quaternion.Euler(pitch, 0f, roll);
            hull.localScale = Vector3.one;
            Look(toward, washed, opacity);
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
            transform.localScale = new Vector3(grow, grow * siteHeight, grow);
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
            if (crown != null) crown.localPosition = crownRest;
            transform.localScale = new Vector3(1f, siteHeight, 1f);
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

        /// <summary>Slow, fast, slow: the smoothstep on 0..1.</summary>
        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Fast at first, slow at the end: a quadratic ease out on 0..1.</summary>
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
    }
}
