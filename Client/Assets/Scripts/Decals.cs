using UnityEngine;
using UnityEngine.Rendering;

namespace WordCraft.View
{
    /// <summary>
    /// The shared mesh and material furniture behind every mark the view puts on
    /// the field. Two materials: one that lies on the ground and is hidden by
    /// what stands in front of it, one that floats over a head and never is. Both
    /// come from the one Decal shader and differ only in their depth test.
    /// </summary>
    public static class Marks
    {
        private static Material ground;
        private static Material overlay;
        private static Mesh quad;
        private static Mesh diamond;

        /// <summary>A mark lying on the terrain: depth-tested, pulled a hair off the surface by the shader.</summary>
        public static Material Ground => ground != null ? ground : ground = Make(CompareFunction.LessEqual);

        /// <summary>A mark over a body that must never be hidden: a meter, a flag.</summary>
        public static Material Overlay => overlay != null ? overlay : overlay = Make(CompareFunction.Always);

        /// <summary>A unit square in the xy plane, centred, white. Scaled and faced per use.</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Quad" };
                quad.SetVertices(new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                });
                quad.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                return quad;
            }
        }

        /// <summary>
        /// An octahedron a unit tall and wide: the rally marker, standing up out
        /// of the ground where a flat diamond used to lie on it. Lit faces are not
        /// wanted on a mark, so it uses the decal material and reads as one flat
        /// shape from every angle, which is the point of a marker.
        /// </summary>
        public static Mesh Diamond
        {
            get
            {
                if (diamond != null) return diamond;
                diamond = new Mesh { name = "Diamond" };
                var v = new[]
                {
                    new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f),
                    new Vector3(0.35f, 0f, 0f), new Vector3(-0.35f, 0f, 0f),
                    new Vector3(0f, 0f, 0.35f), new Vector3(0f, 0f, -0.35f),
                };
                diamond.SetVertices(v);
                var c = new Color[v.Length];
                for (int i = 0; i < c.Length; i++) c[i] = Color.white;
                diamond.SetColors(c);
                diamond.SetTriangles(new[]
                {
                    0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4,
                    1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3,
                }, 0);
                diamond.RecalculateBounds();
                return diamond;
            }
        }

        private static Material Make(CompareFunction test)
        {
            Shader shader = Resources.Load<Shader>("Shaders/Decal");
            if (shader == null)
            {
                Debug.LogWarning("Shaders/Decal missing from Resources; marks fall back to Sprites/Default");
                shader = Shader.Find("Sprites/Default");
            }
            var m = new Material(shader) { name = "Decal " + test };
            m.SetFloat("_ZTest", (float)test);
            return m;
        }
    }

    /// <summary>
    /// A ring or a disc that lies on the ground and follows it: every vertex is
    /// dropped onto the terrain, so a ring around a body halfway up a ramp bends
    /// with the ramp instead of cutting into it on one side and floating on the
    /// other. Rebuilt whenever it moves, which is cheap — a few dozen vertices —
    /// and only ever for the handful of marks that are on at once.
    /// </summary>
    public sealed class Decal : MonoBehaviour
    {
        /// <summary>Segments round the circle. Enough that a ring 4 cells across is a circle and not a polygon.</summary>
        private const int Segments = 48;

        /// <summary>How far above the ground the vertices sit, so a slope between two samples does not poke through.</summary>
        private const float Lift = 0.04f;

        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private Vector3[] vertices;
        private Color[] colours;
        private bool ring;

        private Vector2 at;
        private float radius = -1f;
        private float band = -1f;

        /// <summary>Makes one, switched off, parented under <paramref name="parent"/>.</summary>
        public static Decal Make(Transform parent, string name, bool ring, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var d = go.AddComponent<Decal>();
            d.ring = ring;
            d.filter = go.AddComponent<MeshFilter>();
            d.meshRenderer = go.AddComponent<MeshRenderer>();
            d.meshRenderer.sharedMaterial = Marks.Ground;
            d.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            d.meshRenderer.receiveShadows = false;
            d.mesh = new Mesh { name = name };
            d.mesh.MarkDynamic();
            d.filter.sharedMesh = d.mesh;
            d.block = new MaterialPropertyBlock();

            int count = ring ? Segments * 2 : Segments + 1;
            d.vertices = new Vector3[count];
            d.colours = new Color[count];
            d.Triangles();
            d.Colour = colour;
            d.Visible = false;
            return d;
        }

        public bool Visible
        {
            get => meshRenderer.enabled;
            set => meshRenderer.enabled = value;
        }

        public Color Colour
        {
            set
            {
                block.SetColor("_Color", value);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Lays the mark on the ground around a point. A ring is <paramref name="band"/>
        /// wide inside <paramref name="radius"/>; a disc ignores the band. Skipped
        /// when nothing moved, so a standing selection costs nothing per frame.
        /// </summary>
        public void Set(Vector2 centre, float radius, float band)
        {
            if (centre == at && Mathf.Approximately(radius, this.radius) && Mathf.Approximately(band, this.band)) return;
            at = centre;
            this.radius = radius;
            this.band = band;

            if (ring)
            {
                float inner = Mathf.Max(radius - band, 0f);
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * (Mathf.PI * 2f / Segments);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vertices[i * 2] = Field.At(centre + dir * radius, Lift);
                    vertices[i * 2 + 1] = Field.At(centre + dir * inner, Lift);
                }
            }
            else
            {
                vertices[0] = Field.At(centre, Lift);
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * (Mathf.PI * 2f / Segments);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vertices[i + 1] = Field.At(centre + dir * radius, Lift);
                }
            }

            mesh.SetVertices(vertices);
            mesh.RecalculateBounds();
        }

        private void Triangles()
        {
            for (int i = 0; i < colours.Length; i++) colours[i] = Color.white;

            int[] tris;
            if (ring)
            {
                tris = new int[Segments * 6];
                for (int i = 0; i < Segments; i++)
                {
                    int o = i * 2;
                    int n = ((i + 1) % Segments) * 2;
                    tris[i * 6 + 0] = o;
                    tris[i * 6 + 1] = n;
                    tris[i * 6 + 2] = o + 1;
                    tris[i * 6 + 3] = o + 1;
                    tris[i * 6 + 4] = n;
                    tris[i * 6 + 5] = n + 1;
                }
            }
            else
            {
                tris = new int[Segments * 3];
                for (int i = 0; i < Segments; i++)
                {
                    tris[i * 3 + 0] = 0;
                    tris[i * 3 + 1] = (i + 1) % Segments + 1;
                    tris[i * 3 + 2] = i + 1;
                }
            }

            // Vertices first, so the index buffer is never larger than the vertex
            // buffer it points into, which Unity checks on SetTriangles.
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }

    /// <summary>
    /// A flat quad that always faces the camera: a health bar, a queue meter. A
    /// meter is screen furniture that happens to hang over a body, so it is drawn
    /// with the overlay material and no hill ever hides it, and it is turned to
    /// the camera every frame rather than lying on the ground, where a tilted
    /// camera would foreshorten it into a sliver.
    /// </summary>
    public sealed class Billboard : MonoBehaviour
    {
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;

        /// <summary>
        /// Makes one, switched off. <paramref name="front"/> draws it after the
        /// other billboards: a fill and its back stand on the same spot, and
        /// with no depth test the camera-distance sort alone can put the back
        /// over the fill. The cost: where two bars overlap on screen, a far
        /// body's fill also paints over a near body's back.
        /// </summary>
        public static Billboard Make(Transform parent, string name, Color colour, bool front = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var b = go.AddComponent<Billboard>();
            go.AddComponent<MeshFilter>().sharedMesh = Marks.Quad;
            b.meshRenderer = go.AddComponent<MeshRenderer>();
            b.meshRenderer.sharedMaterial = Marks.Overlay;
            b.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            b.meshRenderer.receiveShadows = false;
            if (front) b.meshRenderer.sortingOrder = 1;
            b.block = new MaterialPropertyBlock();
            b.Colour = colour;
            b.Visible = false;
            return b;
        }

        public bool Visible
        {
            get => meshRenderer.enabled;
            set => meshRenderer.enabled = value;
        }

        public Color Colour
        {
            set
            {
                block.SetColor("_Color", value);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Places a bar of <paramref name="width"/> by <paramref name="height"/>
        /// world units at a point, filled <paramref name="fill"/> of the way from
        /// its left edge, facing <paramref name="cam"/>. Growing from the left edge
        /// rather than the middle is what makes a bar read as a quantity.
        /// </summary>
        public void Set(Camera cam, Vector3 at, float width, float height, float fill)
        {
            float w = width * fill;
            Quaternion facing = cam.transform.rotation;
            transform.SetPositionAndRotation(at - cam.transform.right * ((width - w) * 0.5f), facing);
            transform.localScale = new Vector3(Mathf.Max(w, 0f), height, 1f);
        }
    }
}
