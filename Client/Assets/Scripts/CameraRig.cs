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

        private Camera cam;
        private Vector3 dragAnchor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => new GameObject("WordCraft Camera Rig").AddComponent<CameraRig>();

        private void Awake() => DontDestroyOnLoad(gameObject);

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
