using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// Pan with WASD or the arrow keys, with the mouse at a screen edge, or with a
    /// middle-button drag; zoom the wheel toward the cursor. Every input moves a
    /// target and the camera eases onto it, which is safe only because the camera
    /// is not simulation state and no peer ever hears about it.
    ///
    ///   WASD / arrows   pan
    ///   screen edge     pan
    ///   middle drag     pan
    ///   wheel           zoom toward the cursor
    ///   Z               save a camera bookmark
    ///   X               jump back to it
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        private const float PanUnitsPerSecond = 26f;

        /// <summary>How near an edge the pointer has to be before the map scrolls.</summary>
        private const float EdgePixels = 14f;

        /// <summary>
        /// Wheel notch to zoom factor, as e^(-rate * notch). Multiplicative rather
        /// than a fixed step, so one notch changes the view by the same proportion
        /// however far out it already is. Wheels and trackpads report wildly
        /// different magnitudes; this is the knob to turn if it feels wrong.
        /// </summary>
        private const float ZoomRate = 2.2f;

        /// <summary>
        /// Closest in. 12 cells tall, so a unit's 1.8 cell footprint is 15% of the
        /// screen and a base's 4 cells is a third of it: past this the screen holds
        /// less than one fight and zooming in stops telling the player anything.
        /// </summary>
        private const float MinSize = 6f;

        /// <summary>
        /// Where a match opens: 24 cells tall, about a quarter of the map, which is
        /// a base and the ground in front of it. The view builds the camera at its
        /// own size and this overrides it on the first frame, because framing is
        /// this file's job and a second copy of the number drifts from this one.
        /// </summary>
        private const float DefaultSize = 12f;

        /// <summary>
        /// Furthest out: half the map tall, and never wider than the map (see
        /// MaxZoom). Two reasons, and the second is the one that bites. A view
        /// larger than the map makes the map an object floating in a frame rather
        /// than the frame itself, and every unit on it too small to click. And the
        /// clamp below has no position to offer once the view outgrows the map, so
        /// it centres instead and panning simply stops working — which is exactly
        /// what the old 34 did at 68 cells against a 64 cell map.
        /// </summary>
        private const float MaxSize = 16f;

        /// <summary>Zoom level the pan speed was tuned at, so panning feels equal at every zoom.
        /// No longer a reachable zoom: speed is proportional to size, so this only sets the ratio.</summary>
        private const float ReferenceSize = 18f;

        /// <summary>How much void the view is allowed past the map edge.</summary>
        private const float MarginCells = 4f;

        /// <summary>
        /// Ease rate per second. High on purpose: this is an RTS, not a flight sim,
        /// and a camera that floats or overshoots feels worse than one that snaps.
        /// </summary>
        private const float Smoothing = 18f;

        public static CameraRig Instance { get; private set; }

        private Camera cam;

        // Where the camera is heading. Input moves these; the camera chases them.
        private Vector3 target;
        private float targetSize;

        private Vector2 dragAnchorScreen;
        private Vector3 dragAnchorTarget;

        // ponytail: one bookmark. An array and F5-F8 the day one is not enough.
        private Vector3 bookmark;
        private bool hasBookmark;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Camera Rig").AddComponent<CameraRig>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Puts a world point in the middle of the screen. Control groups, the idle
        /// worker button and the base jump go this way; it is a camera move and nothing else
        /// hears about it. Eased like every other move, so a jump reads as a jump and not a cut.</summary>
        public void CenterOn(Vector2 point) => target = new Vector3(point.x, point.y, -10f);

        private void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
                // Adopt where the view put it rather than jumping on the first frame,
                // but take over the zoom: snapped rather than eased, so the player
                // does not watch a zoom on boot that they did not ask for.
                target = cam.transform.position;
                targetSize = DefaultSize;
                cam.orthographicSize = DefaultSize;
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
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, k);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, k);
        }

        /// <summary>Pan, zoom and bookmark: everything the camera does that a key asked for.</summary>
        private void Controls()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(scroll, 0f)) Zoom(scroll);

            if (Input.GetMouseButtonDown(2))
            {
                dragAnchorScreen = Input.mousePosition;
                dragAnchorTarget = target;
            }

            if (Input.GetMouseButton(2))
            {
                // Drag the ground, not the camera: the point grabbed stays under the
                // cursor. Measured from where the drag started rather than from the
                // eased camera, or the drag chases its own smoothing and never settles.
                Vector2 moved = (Vector2)Input.mousePosition - dragAnchorScreen;
                target = dragAnchorTarget - (Vector3)(moved * WorldPerPixel());
            }
            else
            {
                var axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) + EdgePush();
                // Speed follows the zoom, so the screen moves at one apparent rate at
                // every zoom: zoomed out covers more world in the same wrist movement.
                float speed = PanUnitsPerSecond * (targetSize / ReferenceSize);
                // Clamped rather than normalised, so keys and edge together are not
                // faster than either alone but a light key press is still a light pan.
                target += (Vector3)(Vector2.ClampMagnitude(axis, 1f) * (speed * Time.unscaledDeltaTime));
            }

            Bookmarks();
        }

        /// <summary>
        /// Zooms about the cursor: the world point under the pointer stays under the
        /// pointer. Zooming toward the screen centre instead is the single most
        /// common thing that makes an RTS camera feel wrong, because the thing being
        /// zoomed in on slides away while you zoom in on it.
        /// </summary>
        private void Zoom(float scroll)
        {
            Vector2 fromCentre = (Vector2)Input.mousePosition - new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 grabbed = (Vector2)target + fromCentre * WorldPerPixel();

            targetSize = Mathf.Clamp(targetSize * Mathf.Exp(-scroll * ZoomRate), MinSize, MaxZoom());

            // Same pixel offset, new scale: put the camera where that point lands back under the cursor.
            Vector2 kept = grabbed - fromCentre * WorldPerPixel();
            target = new Vector3(kept.x, kept.y, target.z);
        }

        /// <summary>
        /// One saved viewpoint. Z and X because the command card owns RTY/FGH/VBN,
        /// the camera owns WASD, the control groups own the digits, and the
        /// bottom-left pair is what a left hand resting on WASD can still reach.
        /// </summary>
        private void Bookmarks()
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                bookmark = target;
                hasBookmark = true;
            }
            else if (Input.GetKeyDown(KeyCode.X) && hasBookmark)
            {
                target = bookmark;
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

        /// <summary>The one place the target is fenced, so every mover is fenced the same way.</summary>
        private void Clamp()
        {
            // Re-clamped every frame rather than only on a wheel notch: the far
            // limit depends on the aspect and a player can resize the window.
            targetSize = Mathf.Clamp(targetSize, MinSize, MaxZoom());

            target.x = OnMap(target.x, targetSize * cam.aspect);
            target.y = OnMap(target.y, targetSize);
            target.z = -10f;
        }

        /// <summary>
        /// The far limit, pulled in on a wide window so the view is never wider
        /// than the map. On 16:9 the constant binds; a 21:9 monitor at that same
        /// constant would see 76 cells of a 64 cell map across, which is a screen
        /// of void either side and, worse, a horizontal clamp with nothing left to
        /// clamp to, so it locks to the map's centre line and panning dies.
        /// </summary>
        private float MaxZoom() =>
            Mathf.Clamp(MatchScenario.MapSize / (2f * cam.aspect), MinSize, MaxSize);

        /// <summary>
        /// Keeps the visible edge on the map give or take a margin, rather than
        /// keeping the camera centre on it, which lets half a screen of void in.
        /// A view wider than the map satisfies no position at all, so it centres
        /// instead; MaxZoom now keeps the view inside the map on any sane monitor,
        /// and this branch is what is left for the insane ones.
        /// </summary>
        private static float OnMap(float v, float half)
        {
            float lo = half - MarginCells;
            float hi = MatchScenario.MapSize + MarginCells - half;
            return lo > hi ? MatchScenario.MapSize * 0.5f : Mathf.Clamp(v, lo, hi);
        }

        /// <summary>World units per screen pixel at the target zoom. Orthographic, so both axes agree.</summary>
        private float WorldPerPixel() => 2f * targetSize / Screen.height;
    }
}
