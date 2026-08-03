using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// Pan with WASD or the arrow keys or a middle-button drag, zoom with the
    /// wheel. Frame time drives all of it, which is safe only because the camera
    /// is not simulation state and no peer ever hears about it.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        private const float PanUnitsPerSecond = 26f;
        private const float ZoomStep = 6f;
        private const float MinSize = 6f;
        private const float MaxSize = 34f;

        /// <summary>Zoom level the pan speed was tuned at, so panning feels equal at every zoom.</summary>
        private const float ReferenceSize = 18f;

        public static CameraRig Instance { get; private set; }

        private Camera cam;
        private Vector3 dragAnchor;

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

        /// <summary>Puts a world point in the middle of the screen. Control groups and the idle
        /// worker button jump this way; it is a camera move and nothing else hears about it.</summary>
        public void CenterOn(Vector2 point)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            Place(new Vector3(point.x, point.y, 0f));
        }

        private void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(scroll, 0f))
            {
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - scroll * ZoomStep, MinSize, MaxSize);
            }

            Vector3 position = cam.transform.position;

            if (Input.GetMouseButtonDown(2)) dragAnchor = MouseWorld();
            if (Input.GetMouseButton(2))
            {
                // Drag the ground, not the camera: the point under the cursor has
                // to stay under the cursor or the map feels like it is sliding.
                position += dragAnchor - MouseWorld();
            }
            else
            {
                var axis = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                float speed = PanUnitsPerSecond * (cam.orthographicSize / ReferenceSize);
                position += (Vector3)(axis.normalized * (speed * Time.unscaledDeltaTime));
            }

            Place(position);
        }

        /// <summary>The one place the camera is written, so every mover clamps the same way.</summary>
        private void Place(Vector3 position)
        {
            position.x = Mathf.Clamp(position.x, 0f, MatchScenario.MapSize);
            position.y = Mathf.Clamp(position.y, 0f, MatchScenario.MapSize);
            position.z = -10f;
            cam.transform.position = position;
        }

        private Vector3 MouseWorld()
        {
            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;
            return world;
        }
    }
}
