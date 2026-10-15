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

        private const float TurnDegreesPerSecond = 540f;
        private const float AttackSeconds = 0.25f;
        private const float HitSeconds = 0.2f;

        /// <summary>The ghost alpha of a site under construction, as UiStyle.SiteAlpha was for the sprite view.</summary>
        private const float SiteAlpha = 0.75f;

        /// <summary>A jump this long between two frames is a body being placed somewhere, not walking there.</summary>
        private const float TeleportDistance = 4f;

        /// <summary>
        /// Smoothing on the measured speed. The simulation moves bodies in tick
        /// steps and frames outnumber ticks, so the raw per-frame speed alternates
        /// between a stride and nothing; a tenth of a second evens that out without
        /// letting a stop slide on for long.
        /// </summary>
        private const float SpeedSmoothingSeconds = 0.12f;

        /// <summary>World units a second at which the gait is fully in step. Below it the walk fades into idle.</summary>
        private const float FullGaitSpeed = 0.5f;

        private const float MovingEpsilon = 1e-4f;

        /// <summary>What the fog's memory multiplies a body by, before the shader takes most of its colour away.</summary>
        private static readonly Color RememberedMultiply = new Color(0.72f, 0.72f, 0.76f, 1f);
        private const float RememberedDesaturate = 0.65f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        private static MaterialPropertyBlock block;

        private Design design;
        private Transform hull, arm, limbLeft, limbRight, crown;
        private MeshRenderer[] renderers;
        private Vector3 hullRest, armRest, limbLeftRest, limbRightRest, crownRest;

        private bool placed;
        private Vector3 lastGround;
        private float speed;
        private float travelled;
        private float targetYaw;
        private float aimYaw;
        private bool aiming;
        private float attackStarted = float.NegativeInfinity;
        private float hitStarted = float.NegativeInfinity;
        private float seed;

        private float progress = 1f;
        private float alpha = 1f;
        private Color tint = Color.white;
        private bool remembered;
        private bool shown = true;
        private bool ghosted;

        public float Footprint => design != null ? design.Footprint : 1f;
        public float Top => design != null ? design.Top : 1f;
        public bool Flying => design != null && design.Flying;

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
            targetYaw = transform.eulerAngles.y;
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
        /// across the map to get there.
        /// </summary>
        public void Place(Vector3 ground)
        {
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
        /// Construction from 0 to 1. The body grows out of the ground — scaled up
        /// from its feet, which is the same thing as rising — and stays a ghost
        /// until it is finished, as a site did in the sprite view.
        /// </summary>
        public void Progress(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Approximately(t, progress)) return;
            progress = t;
            transform.localScale = t >= 1f ? Vector3.one : new Vector3(1f, Mathf.Lerp(0.15f, 1f, t), 1f);
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

            float shownAlpha = Mathf.Min(alpha, progress < 1f ? SiteAlpha : 1f);
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

            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            block.SetColor(TintId, shownTint);
            block.SetFloat(AlphaId, shownAlpha);
            block.SetFloat(DesaturateId, remembered ? RememberedDesaturate : 0f);
            for (int i = 0; i < renderers.Length; i++) renderers[i].SetPropertyBlock(block);
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
            if (design == null || remembered || hull == null) return;

            float now = ViewClock.Now;
            float deltaTime = ViewClock.Delta;

            if (design.Building) Stand(now, deltaTime);
            else Move(now, deltaTime);
        }

        private void Move(float now, float deltaTime)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), TurnDegreesPerSecond * deltaTime);

            float size = design.Size;
            float gait = Mathf.Clamp01(speed / FullGaitSpeed);
            float phase = design.Stride > 0f ? travelled / design.Stride * 2f * Mathf.PI : 0f;
            float idle = 1f - gait;
            float breath = Mathf.Sin(now * 2.2f + seed);

            Vector3 offset = Vector3.zero;
            Vector3 scale = Vector3.one;
            float pitch = 0f;

            switch (design.Gait)
            {
                case Gait.Walk:
                    offset.y = Mathf.Abs(Mathf.Sin(phase)) * 0.035f * size * gait;
                    scale.y = 1f + breath * 0.015f * idle;
                    pitch = 4f * gait;
                    break;
                case Gait.Hop:
                {
                    float hop = Mathf.Abs(Mathf.Sin(phase * 0.5f));
                    offset.y = hop * 0.14f * size * gait;
                    float squash = (1f - hop) * 0.12f * gait + breath * 0.03f * idle;
                    scale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);
                    break;
                }
                case Gait.Float:
                    offset.y = (0.04f + Mathf.Sin(now * 1.8f + seed) * 0.035f) * size;
                    pitch = 12f * gait;
                    break;
                case Gait.Fly:
                    offset.y = Mathf.Sin(now * 1.6f + seed) * 0.08f * size;
                    pitch = 10f * gait;
                    break;
            }

            float attack = Phase(attackStarted, AttackSeconds, now);
            float armPitch = 0f;
            if (attack >= 0f)
            {
                switch (design.Strike)
                {
                    case Strike.Lunge:
                        offset.z += Bump(attack, 0.25f, 1f) * 0.16f * size;
                        armPitch = -35f * Bump(attack, 0f, 0.4f) + 80f * Bump(attack, 0.3f, 1f);
                        break;
                    case Strike.Recoil:
                        offset.z -= Bump(attack, 0f, 0.7f) * 0.06f * size;
                        armPitch = -22f * Bump(attack, 0f, 0.6f);
                        break;
                    case Strike.Pulse:
                        scale *= 1f + 0.1f * Bump(attack, 0f, 1f);
                        break;
                }
            }

            float hit = Phase(hitStarted, HitSeconds, now);
            if (hit >= 0f)
            {
                float flinch = Mathf.Sin(hit * Mathf.PI);
                scale = Vector3.Scale(scale, new Vector3(1f + 0.1f * flinch, 1f - 0.13f * flinch, 1f + 0.1f * flinch));
                pitch -= 9f * flinch;
            }

            hull.localPosition = hullRest + offset;
            hull.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            hull.localScale = scale;

            if (arm != null) arm.localRotation = Quaternion.Euler(armPitch + breath * 2f * idle, 0f, 0f);

            Limbs(now, phase, gait, size);
        }

        private void Limbs(float now, float phase, float gait, float size)
        {
            if (limbLeft == null && limbRight == null) return;

            switch (design.Limbs)
            {
                case LimbMotion.Swing:
                {
                    float swing = Mathf.Sin(phase) * 30f * gait;
                    if (limbLeft != null) limbLeft.localRotation = Quaternion.Euler(swing, 0f, 0f);
                    if (limbRight != null) limbRight.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                    break;
                }
                case LimbMotion.Step:
                {
                    float reach = 0.07f * size * gait, lift = 0.05f * size * gait;
                    if (limbLeft != null)
                        limbLeft.localPosition = limbLeftRest + new Vector3(
                            0f, Mathf.Max(0f, Mathf.Sin(phase)) * lift, Mathf.Cos(phase) * reach);
                    if (limbRight != null)
                        limbRight.localPosition = limbRightRest + new Vector3(
                            0f, Mathf.Max(0f, -Mathf.Sin(phase)) * lift, -Mathf.Cos(phase) * reach);
                    break;
                }
                case LimbMotion.Flap:
                {
                    float rate = design.Flying ? 7f : 3.2f;
                    float beat = Mathf.Sin(now * rate + seed) * (design.Flying ? 32f : 16f);
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

            float hit = Phase(hitStarted, HitSeconds, now);
            if (hit >= 0f)
            {
                float flinch = Mathf.Sin(hit * Mathf.PI);
                scale = new Vector3(1f + 0.03f * flinch, 1f - 0.05f * flinch, 1f + 0.03f * flinch);
            }

            float attack = Phase(attackStarted, AttackSeconds, now);
            float fire = attack >= 0f ? Bump(attack, 0f, 1f) : 0f;
            if (crown == null) scale *= 1f + 0.04f * fire;
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
            crown.localPosition = crownRest + turn * new Vector3(0f, 0f, -0.12f * fire);
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
            float local = Mathf.Clamp01((t - from) / (to - from));
            return Mathf.Sin(local * Mathf.PI);
        }
    }
}
