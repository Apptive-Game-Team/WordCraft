using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// A perspective camera on a boom over the map. It looks at one point on the
    /// ground, the frame, from a distance and a compass heading; the pitch follows
    /// the distance, so pulling out tips toward overhead and pushing in drops
    /// toward the horizon the way every modern RTS camera does. Every input moves
    /// a target and the camera eases onto it, which is safe only because the camera
    /// is not simulation state and no peer ever hears about it.
    ///
    ///   WASD / arrows   pan, relative to the heading
    ///   screen edge     pan
    ///   middle drag     grab the ground and pull it
    ///   wheel           zoom toward the cursor
    ///   Q / E           turn the heading
    ///   Home            heading back to north
    ///   Z               save a camera bookmark
    ///   X               jump back to it
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>
        /// Vertical field of view. Narrow for a perspective camera, on purpose: a
        /// wide lens makes the far end of a tilted map rush away and the near end
        /// loom, and bodies at the two ends of the screen stop being the same size.
        /// Narrow keeps the field close to the orthographic read the sprites had
        /// while still giving the hills their depth.
        /// </summary>
        public const float FieldOfView = 36f;

        /// <summary>
        /// Closest in. About 11 cells of ground across the screen's height, so a
        /// unit's 1.8 cell footprint is a sixth of the screen and a base a third:
        /// past this the screen holds less than one fight.
        /// </summary>
        private const float MinDistance = 13f;

        /// <summary>Where a match opens: about 24 cells tall, a base and the ground in front of it.</summary>
        private const float DefaultDistance = 30f;

        /// <summary>Furthest out: a little under half the map tall, so panning still has somewhere to go.</summary>
        private const float MaxDistance = 58f;

        /// <summary>Pitch at the two ends of the zoom. Steeper far out, so the whole map reads as a map.</summary>
        private const float NearPitch = 47f;
        private const float FarPitch = 64f;

        private const float PanUnitsPerSecond = 26f;

        /// <summary>Zoom level the pan speed was tuned at, so panning feels equal at every zoom.</summary>
        private const float ReferenceDistance = 30f;

        /// <summary>How near an edge the pointer has to be before the map scrolls.</summary>
        private const float EdgePixels = 14f;

        /// <summary>
        /// Wheel notch to zoom factor, as e^(-rate * notch). Multiplicative rather
        /// than a fixed step, so one notch changes the view by the same proportion
        /// however far out it already is.
        /// </summary>
        private const float ZoomRate = 2.2f;

        private const float TurnDegreesPerSecond = 110f;

        /// <summary>How far past the map's edge the frame may go. The camera itself goes further; this is what it looks at.</summary>
        private const float MarginCells = 1.5f;

        /// <summary>
        /// Ease rate per second. High on purpose: this is an RTS, not a flight sim,
        /// and a camera that floats or overshoots feels worse than one that snaps.
        /// </summary>
        private const float Smoothing = 18f;

        public static CameraRig Instance { get; private set; }

        private Camera cam;

        // Where the camera is heading. Input moves these; the camera chases them.
        private Vector2 frame;
        private float distance = DefaultDistance;
        private float yaw;

        // Where it is now, eased toward the targets above.
        private Vector2 shownFrame;
        private float shownDistance = DefaultDistance;
        private float shownYaw;

        private Vector2 dragGrabbed;
        private float dragHeight;
        private bool dragging;

        // ponytail: one bookmark. An array and F5-F8 the day one is not enough.
        private Vector2 bookmark;
        private bool hasBookmark;

        private bool adopted;

        /// <summary>The point on the ground the player is looking at, on the simulation's plane.</summary>
        public Vector2 Frame => shownFrame;

        /// <summary>Compass heading of the view, in degrees. Zero looks along +z, which is sim +y: north.</summary>
        public float Yaw => shownYaw;

        /// <summary>How far the eye is from what it looks at, in world units.</summary>
        public float Distance => shownDistance;

        /// <summary>
        /// Half the height of the view at the frame's distance, in world units.
        /// The nearest thing a perspective camera has to an orthographic size, and
        /// what anything sizing itself to the zoom reads: a ring band, a sound's
        /// falloff. Measured at the frame rather than at the ground under the
        /// cursor, so it does not jump when the cursor crosses a hill.
        /// </summary>
        public float HalfHeight => shownDistance * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

        /// <summary>World units per screen pixel at the frame's distance.</summary>
        public float WorldPerPixel => 2f * HalfHeight / Screen.height;

        /// <summary>The heading as a direction on the plane: which way "up the screen" points on the map.</summary>
        public Vector2 Forward => Heading(shownYaw);

        /// <summary>Which way "right on the screen" points on the map.</summary>
        public Vector2 Right
        {
            get
            {
                Vector2 f = Forward;
                return new Vector2(f.y, -f.x);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Camera Rig").AddComponent<CameraRig>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            frame = shownFrame = new Vector2(MatchScenario.MapSize * 0.5f, MatchScenario.MapSize * 0.5f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Puts a point on the ground in the middle of the screen the player can see, which is
        /// not the middle of the window (see <see cref="FramingOffset"/>). Control groups, the idle worker
        /// button and the base jump go this way; it is a camera move and nothing else hears
        /// about it. Eased like every other move, so a jump reads as a jump and not a cut.</summary>
        public void CenterOn(Vector2 point) => frame = point;

        private void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            if (!adopted)
            {
                // Snapped rather than eased on the first frame, so the player does
                // not watch a zoom on boot that they did not ask for.
                adopted = true;
                shownFrame = frame;
                shownDistance = distance;
                shownYaw = yaw;
                Apply();
            }

            // Only the match screen drives the camera. WASD is also how an address
            // gets typed into the start screen, and a map sliding under the menu is
            // nobody's idea of a text field. The ease below keeps running, so a
            // camera sent back to the middle for the menu actually gets there.
            if (MatchRunner.Instance == null || MatchRunner.Instance.Phase == Phase.Match) Controls();

            Clamp();

            // Exponential ease, so the rate is the same on a 30fps machine and a
            // 144fps one. A plain Lerp against a raw delta is not, and the
            // difference is exactly the sort of thing a player feels but cannot name.
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            shownFrame = Vector2.Lerp(shownFrame, frame, k);
            shownDistance = Mathf.Lerp(shownDistance, distance, k);
            shownYaw = Mathf.LerpAngle(shownYaw, yaw, k);
            Apply();
        }

        /// <summary>Pan, zoom, turn and bookmark: everything the camera does that a key asked for.</summary>
        private void Controls()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(scroll, 0f)) Zoom(scroll);

            if (Input.GetMouseButtonDown(2) && Field.Pointer(cam, Input.mousePosition, out dragGrabbed))
            {
                dragging = true;
                dragHeight = Field.Height(dragGrabbed);
            }
            if (!Input.GetMouseButton(2)) dragging = false;

            if (dragging)
            {
                // Drag the ground, not the camera: the point grabbed stays under the
                // cursor. The ray is cast from where the camera is heading rather
                // than from where its ease has got to, or the drag chases its own
                // smoothing and never settles. Solved against the flat plane at the
                // grabbed point's height instead of against the hills, because a
                // drag across a cliff must not jump when the ray crosses the face.
                Ray ray = TargetRay(Input.mousePosition);
                if (ray.direction.y < -0.01f)
                {
                    float t = (ray.origin.y - dragHeight) / -ray.direction.y;
                    Vector2 under = Field.Plane(ray.GetPoint(t));
                    frame += dragGrabbed - under;
                }
            }
            else
            {
                var axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + EdgePush();
                // Speed follows the zoom, so the screen moves at one apparent rate at
                // every zoom: zoomed out covers more world in the same wrist movement.
                float speed = PanUnitsPerSecond * (distance / ReferenceDistance);
                // Clamped rather than normalised, so keys and edge together are not
                // faster than either alone but a light key press is still a light pan.
                Vector2 push = Vector2.ClampMagnitude(axis, 1f) * (speed * Time.unscaledDeltaTime);
                Vector2 f = Heading(yaw);
                frame += f * push.y + new Vector2(f.y, -f.x) * push.x;
            }

            float turn = 0f;
            if (Input.GetKey(KeyCode.Q)) turn -= 1f;
            if (Input.GetKey(KeyCode.E)) turn += 1f;
            yaw += turn * TurnDegreesPerSecond * Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.Home)) yaw = 0f;

            Bookmarks();
        }

        /// <summary>
        /// Zooms about the cursor: the ground under the pointer stays under the
        /// pointer. Zooming toward the screen centre instead is the single most
        /// common thing that makes an RTS camera feel wrong, because the thing being
        /// zoomed in on slides away while you zoom in on it. With the pitch tied to
        /// the distance this is exact only for a flat map; on a hill it is a cell
        /// out at worst, and the ease hides that.
        /// </summary>
        private void Zoom(float scroll)
        {
            float before = distance;
            distance = Mathf.Clamp(distance * Mathf.Exp(-scroll * ZoomRate), MinDistance, MaxDistance);

            if (Field.Pointer(cam, Input.mousePosition, out Vector2 under))
            {
                frame += (under - frame) * (1f - distance / before);
            }
        }

        /// <summary>
        /// One saved viewpoint. Z and X because the command card owns RTY/FGH/VBN,
        /// the camera owns WASD and QE, the control groups own the digits, and the
        /// bottom-left pair is what a left hand resting on WASD can still reach.
        /// </summary>
        private void Bookmarks()
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                bookmark = frame;
                hasBookmark = true;
            }
            else if (Input.GetKeyDown(KeyCode.X) && hasBookmark)
            {
                frame = bookmark;
            }
        }

        /// <summary>
        /// Which way the pointer sitting at a screen edge pushes the map. A pointer
        /// outside the window, or a window that is not focused at all, pushes
        /// nowhere: alt-tabbing away must not leave the map sliding. The HUD is
        /// deliberately not excluded: the bottom edge scrolls through the panel,
        /// which is what every RTS does.
        /// </summary>
        private static Vector2 EdgePush()
        {
            if (!Application.isFocused) return Vector2.zero;

            Vector3 m = Input.mousePosition;
            if (m.x < 0f || m.y < 0f || m.x > Screen.width || m.y > Screen.height) return Vector2.zero;

            var push = Vector2.zero;
            if (m.x <= EdgePixels) push.x = -1f;
            else if (m.x >= Screen.width - 1f - EdgePixels) push.x = 1f;
            if (m.y <= EdgePixels) push.y = -1f;
            else if (m.y >= Screen.height - 1f - EdgePixels) push.y = 1f;
            return push;
        }

        /// <summary>The one place the targets are fenced, so every mover is fenced the same way.</summary>
        private void Clamp()
        {
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            // The fence is on what the player is looking at, not on where the eye
            // is. The eye swings out past the map edge at a low pitch and that is
            // fine: what must not happen is the frame leaving the map, because then
            // the player is looking at the void and panning has nothing to pan to.
            frame.x = Mathf.Clamp(frame.x, -MarginCells, MatchScenario.MapSize + MarginCells);
            frame.y = Mathf.Clamp(frame.y, -MarginCells, MatchScenario.MapSize + MarginCells);
        }

        /// <summary>Moves the camera to the eased pose. The only place the transform is written.</summary>
        private void Apply() => Aim(cam, shownFrame, shownDistance, shownYaw, hud: true);

        /// <summary>
        /// Puts any camera where the rig would put it for a frame, distance and
        /// heading. The screenshot harness uses it to look at the field from the
        /// same poses the player would, without a rig, a HUD or a frame of Update:
        /// <paramref name="hud"/> false leaves out the command card offset, which
        /// means nothing in a render with no command card in it.
        /// </summary>
        public static void Aim(Camera camera, Vector2 at, float dist, float heading, bool hud)
        {
            Pose(at, dist, heading, hud, out Vector3 position, out Quaternion rotation);
            camera.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Where the eye sits and which way it looks for a given frame, distance
        /// and heading. The frame is pushed down the screen by the HUD offset first,
        /// so the point the player asked to look at lands in the open rather than
        /// behind the command card. The boom is anchored at the frame's own ground
        /// height, so a frame on a plateau is looked at from the same distance as
        /// one in a valley rather than from a camera that has sunk into the hill.
        /// </summary>
        private static void Pose(Vector2 at, float dist, float heading, bool hud,
                                 out Vector3 position, out Quaternion rotation)
        {
            float pitch = Pitch(dist);
            Vector2 forward = Heading(heading);

            // The HUD offset, measured along the ground. A pixel offset on screen
            // is a world offset at the frame's distance, stretched by the pitch
            // because the ground is seen at a slant.
            float offset = hud ? FramingOffset(dist) / Mathf.Max(Mathf.Sin(pitch * Mathf.Deg2Rad), 0.2f) : 0f;
            Vector2 focus = at + forward * offset;

            Vector3 target = Field.At(focus);
            rotation = Quaternion.Euler(pitch, heading, 0f);
            position = target - rotation * Vector3.forward * dist;
        }

        private static float Pitch(float dist) =>
            Mathf.Lerp(NearPitch, FarPitch, Mathf.InverseLerp(MinDistance, MaxDistance, dist));

        private static Vector2 Heading(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>
        /// How far below what it is framing the camera aims, in world units at the
        /// frame's distance. The HUD's bottom panel is far taller than its top bar,
        /// so the middle of the window is not the middle of what the player can
        /// see; a camera centred on the window sends every jump — a control group,
        /// the base key, a minimap click — to a point behind the command card. Half
        /// the difference puts it back in the open. Read off the HUD's own two
        /// constants, so a taller command card moves the camera with it.
        /// </summary>
        private static float FramingOffset(float dist)
        {
            float worldPerPixel = 2f * dist * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            return (UiStyle.BottomPanel - UiStyle.TopBar) * 0.5f * worldPerPixel;
        }

        /// <summary>
        /// A ray through a screen point from the pose the camera is heading to,
        /// not the one it is at. The screen point's direction in the camera's own
        /// space does not depend on where the camera is, so it is read off the
        /// real camera and re-aimed from the target pose.
        /// </summary>
        private Ray TargetRay(Vector2 screen)
        {
            Ray now = cam.ScreenPointToRay(screen);
            Vector3 local = cam.transform.InverseTransformDirection(now.direction);
            Pose(frame, distance, yaw, hud: true, out Vector3 position, out Quaternion rotation);
            return new Ray(position, rotation * local);
        }
    }
}
