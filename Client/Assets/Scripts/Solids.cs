using System.Collections.Generic;
using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// Appends flat-shaded convex solids into one mesh, so a body on the field is
    /// one or two renderers rather than twenty GameObjects.
    ///
    /// Flat shading is the point of the whole class. .art/STYLE.md separates forms
    /// by value and not by stroke, and Paper.cginc turns n·l into three bands; a
    /// smooth normal averaged across an edge would smear that edge into a
    /// gradient, so every face here owns its vertices and its one normal. All
    /// colour is vertex colour, which is what lets every body on the field share
    /// one material and still be six palettes.
    ///
    /// Every solid is convex, and that is used rather than tracked: a face's
    /// winding is decided by testing its normal against a point inside the solid
    /// and flipping it if it points inward. Hand-wound index lists are the usual
    /// way to get a face that renders from behind, and a model nobody can open in
    /// an editor is the worst place to find one.
    ///
    /// Coordinates arrive in "authored" space, the space the model functions in
    /// Models.cs are written in, and are stored as (point - Origin) * Scale. Origin
    /// is the pivot of the part this mesh becomes, so an arm can be written in the
    /// same coordinates as the torso it hangs from and still swing about its
    /// shoulder.
    /// </summary>
    public sealed class Solids
    {
        /// <summary>
        /// The twelve corners of an icosahedron, and its twenty faces. Winding is
        /// irrelevant here because <see cref="Face"/> orients every face itself.
        /// </summary>
        private static readonly Vector3[] icosahedronCorners = BuildIcosahedronCorners();

        private static readonly int[] icosahedronFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        private readonly List<Vector3> positions = new List<Vector3>(512);
        private readonly List<Vector3> normals = new List<Vector3>(512);
        private readonly List<Color> colors = new List<Color>(512);
        private readonly List<int> triangles = new List<int>(768);
        private readonly System.Random grainSource;
        private readonly float grain;
        private float highest = float.NegativeInfinity;

        /// <param name="seed">Seeds the per-face value grain, so a model is the same shape and shade on every peer.</param>
        /// <param name="grain">
        /// How far one face's value may wander from its part's colour, as a
        /// fraction. Small on purpose: STYLE.md allows "a very subtle gradient
        /// inside a large shape", and a few percent per facet is what keeps a
        /// low-poly sphere reading as cut paper instead of as plastic.
        /// </param>
        public Solids(int seed, float grain)
        {
            grainSource = new System.Random(seed);
            this.grain = grain;
        }

        /// <summary>The authored point that becomes this mesh's local zero: the pivot of the part it is.</summary>
        public Vector3 Origin { get; set; }

        /// <summary>World units per authored unit. Units are authored one unit tall and scaled to their footprint.</summary>
        public float Scale { get; set; } = 1f;

        public bool Empty => positions.Count == 0;

        /// <summary>The highest authored y anything in this mesh reached, or negative infinity when it is empty.</summary>
        public float Highest => highest;

        // ------------------------------------------------------------------
        // Solids
        // ------------------------------------------------------------------

        public void Box(Vector3 center, Vector3 size, Color color) =>
            Box(center, size, color, Quaternion.identity);

        /// <summary>A box turned about its own centre.</summary>
        public void Box(Vector3 center, Vector3 size, Color color, Quaternion rotation)
        {
            Vector3 bottom = center + rotation * new Vector3(0f, -size.y * 0.5f, 0f);
            Vector2 footprint = new Vector2(size.x, size.z);
            Taper(bottom, footprint, footprint, size.y, color, rotation, Vector2.zero);
        }

        public void Taper(Vector3 bottom, Vector2 bottomSize, Vector2 topSize, float height, Color color) =>
            Taper(bottom, bottomSize, topSize, height, color, Quaternion.identity, Vector2.zero);

        public void Taper(Vector3 bottom, Vector2 bottomSize, Vector2 topSize, float height, Color color, Quaternion rotation) =>
            Taper(bottom, bottomSize, topSize, height, color, rotation, Vector2.zero);

        /// <summary>
        /// A box whose top and bottom rectangles differ: a wedge, a plinth, a
        /// horn, a plate. Sizes are (x, z) before rotation; height runs up local
        /// y from <paramref name="bottom"/>, which is also the pivot of the
        /// rotation. <paramref name="topShift"/> slides the top rectangle in
        /// local (x, z), which is how a slab leans without a second rotation.
        /// </summary>
        public void Taper(Vector3 bottom, Vector2 bottomSize, Vector2 topSize, float height, Color color,
                          Quaternion rotation, Vector2 topShift)
        {
            if (height <= 0f) return;

            float bottomHalfX = bottomSize.x * 0.5f, bottomHalfZ = bottomSize.y * 0.5f;
            float topHalfX = topSize.x * 0.5f, topHalfZ = topSize.y * 0.5f;

            var corners = new Vector3[8];
            corners[0] = new Vector3(-bottomHalfX, 0f, -bottomHalfZ);
            corners[1] = new Vector3(bottomHalfX, 0f, -bottomHalfZ);
            corners[2] = new Vector3(bottomHalfX, 0f, bottomHalfZ);
            corners[3] = new Vector3(-bottomHalfX, 0f, bottomHalfZ);
            corners[4] = new Vector3(-topHalfX + topShift.x, height, -topHalfZ + topShift.y);
            corners[5] = new Vector3(topHalfX + topShift.x, height, -topHalfZ + topShift.y);
            corners[6] = new Vector3(topHalfX + topShift.x, height, topHalfZ + topShift.y);
            corners[7] = new Vector3(-topHalfX + topShift.x, height, topHalfZ + topShift.y);
            for (int i = 0; i < 8; i++) corners[i] = bottom + rotation * corners[i];

            Vector3 inside = Average(corners);
            Color shade = Grained(color);
            Face(new[] { corners[0], corners[1], corners[2], corners[3] }, shade, inside);
            Face(new[] { corners[4], corners[5], corners[6], corners[7] }, shade, inside);
            Face(new[] { corners[0], corners[1], corners[5], corners[4] }, Grained(color), inside);
            Face(new[] { corners[1], corners[2], corners[6], corners[5] }, Grained(color), inside);
            Face(new[] { corners[2], corners[3], corners[7], corners[6] }, Grained(color), inside);
            Face(new[] { corners[3], corners[0], corners[4], corners[7] }, Grained(color), inside);
        }

        public void Prism(Vector3 bottom, int sides, float bottomRadius, float topRadius, float height, Color color) =>
            Prism(bottom, sides, bottomRadius, topRadius, height, color, Quaternion.identity, 1f);

        public void Prism(Vector3 bottom, int sides, float bottomRadius, float topRadius, float height, Color color,
                          Quaternion rotation) =>
            Prism(bottom, sides, bottomRadius, topRadius, height, color, rotation, 1f);

        /// <summary>
        /// A ring of <paramref name="sides"/> faces from one radius to another:
        /// a cylinder, a frustum, a cone when one radius is zero, a pyramid when
        /// the sides are four. The ring is turned half a side so a flat face, not
        /// an edge, looks down +z. <paramref name="depth"/> squashes the ring along
        /// local z, for an oval band round a body that is not round.
        /// </summary>
        public void Prism(Vector3 bottom, int sides, float bottomRadius, float topRadius, float height, Color color,
                          Quaternion rotation, float depth)
        {
            if (sides < 3 || height <= 0f) return;

            var lower = new Vector3[sides];
            var upper = new Vector3[sides];
            float offset = Mathf.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float angle = offset + i * 2f * Mathf.PI / sides;
                float x = Mathf.Sin(angle), z = Mathf.Cos(angle) * depth;
                lower[i] = bottom + rotation * new Vector3(x * bottomRadius, 0f, z * bottomRadius);
                upper[i] = bottom + rotation * new Vector3(x * topRadius, height, z * topRadius);
            }

            Vector3 inside = bottom + rotation * new Vector3(0f, height * 0.4f, 0f);
            Color cap = Grained(color);
            if (bottomRadius > 0f) Face(lower, cap, inside);
            if (topRadius > 0f) Face(upper, cap, inside);

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Color side = Grained(color);
                if (topRadius <= 0f) Face(new[] { lower[i], lower[next], upper[i] }, side, inside);
                else if (bottomRadius <= 0f) Face(new[] { lower[i], upper[next], upper[i] }, side, inside);
                else Face(new[] { lower[i], lower[next], upper[next], upper[i] }, side, inside);
            }
        }

        public void Ball(Vector3 center, Vector3 radii, Color color) =>
            Ball(center, radii, color, Quaternion.identity, 1);

        public void Ball(Vector3 center, float radius, Color color) =>
            Ball(center, Vector3.one * radius, color, Quaternion.identity, 1);

        /// <summary>
        /// An ellipsoid from an icosahedron. Detail 1 subdivides once into eighty
        /// faces, which is round at a glance and still visibly cut; detail 0 keeps
        /// the bare twenty, which is a boulder.
        /// </summary>
        public void Ball(Vector3 center, Vector3 radii, Color color, Quaternion rotation, int detail)
        {
            var faces = new List<Vector3>(detail > 0 ? 240 : 60);
            for (int f = 0; f < icosahedronFaces.Length; f += 3)
            {
                Vector3 a = icosahedronCorners[icosahedronFaces[f]];
                Vector3 b = icosahedronCorners[icosahedronFaces[f + 1]];
                Vector3 c = icosahedronCorners[icosahedronFaces[f + 2]];
                if (detail <= 0)
                {
                    faces.Add(a); faces.Add(b); faces.Add(c);
                    continue;
                }

                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                faces.Add(a); faces.Add(ab); faces.Add(ca);
                faces.Add(ab); faces.Add(b); faces.Add(bc);
                faces.Add(ca); faces.Add(bc); faces.Add(c);
                faces.Add(ab); faces.Add(bc); faces.Add(ca);
            }

            for (int i = 0; i < faces.Count; i += 3)
            {
                Face(new[]
                {
                    center + rotation * Vector3.Scale(faces[i], radii),
                    center + rotation * Vector3.Scale(faces[i + 1], radii),
                    center + rotation * Vector3.Scale(faces[i + 2], radii),
                }, Grained(color), center);
            }
        }

        /// <summary>A boulder: the bare twenty-face ellipsoid, turned so no two boulders sit alike.</summary>
        public void Lump(Vector3 center, Vector3 radii, Color color, Quaternion rotation) =>
            Ball(center, radii, color, rotation, 0);

        /// <summary>
        /// The upper half of an ellipsoid standing on a flat floor at
        /// <paramref name="bottom"/>: a slime, a mound, a moss cap. Latitude rings
        /// rather than an icosahedron because a dome must end on a level circle,
        /// and a sliced icosahedron does not.
        /// </summary>
        public void Dome(Vector3 bottom, Vector3 radii, int sides, int rings, Color color)
        {
            if (sides < 3 || rings < 1) return;

            var ringPoints = new Vector3[rings][];
            for (int r = 0; r < rings; r++)
            {
                float latitude = r * 0.5f * Mathf.PI / rings;
                float across = Mathf.Cos(latitude), up = Mathf.Sin(latitude);
                ringPoints[r] = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = Mathf.PI / sides + i * 2f * Mathf.PI / sides;
                    ringPoints[r][i] = bottom + new Vector3(
                        Mathf.Sin(angle) * across * radii.x, up * radii.y, Mathf.Cos(angle) * across * radii.z);
                }
            }

            Vector3 pole = bottom + new Vector3(0f, radii.y, 0f);
            Vector3 inside = bottom + new Vector3(0f, radii.y * 0.35f, 0f);
            Face(ringPoints[0], Grained(color), inside);

            for (int r = 0; r < rings; r++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    if (r == rings - 1)
                        Face(new[] { ringPoints[r][i], ringPoints[r][next], pole }, Grained(color), inside);
                    else
                        Face(new[] { ringPoints[r][i], ringPoints[r][next], ringPoints[r + 1][next], ringPoints[r + 1][i] },
                             Grained(color), inside);
                }
            }
        }

        // ------------------------------------------------------------------
        // Output
        // ------------------------------------------------------------------

        public Mesh Bake(string name)
        {
            var mesh = new Mesh { name = name };
            if (positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        // Faces
        // ------------------------------------------------------------------

        /// <summary>
        /// One flat convex polygon, wound so its front faces away from
        /// <paramref name="inside"/>. Unity treats a triangle as front facing when
        /// its corners run clockwise seen from the front, which is the direction
        /// Vector3.Cross(b - a, c - a) points; Newell's normal points the same way
        /// for a convex polygon, so a polygon whose normal points inward is simply
        /// reversed. Degenerate slivers — a cone's tip, a pyramid's top — are
        /// dropped rather than given a normal of zero.
        /// </summary>
        private void Face(Vector3[] corners, Color color, Vector3 inside)
        {
            int count = corners.Length;
            Vector3 normal = Vector3.zero;
            Vector3 middle = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % count];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
                middle += a;
            }
            middle /= count;

            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();

            // Reversed into a copy: Prism and Dome pass a ring they go on to
            // index for the side faces, and reversing it in place would pair
            // each side with the wrong corner of the next ring.
            if (Vector3.Dot(normal, middle - inside) < 0f)
            {
                var reversed = new Vector3[count];
                for (int i = 0; i < count; i++) reversed[i] = corners[count - 1 - i];
                corners = reversed;
                normal = -normal;
            }

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 a = corners[0], b = corners[i], c = corners[i + 1];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;

                int start = positions.Count;
                Emit(a, normal, color);
                Emit(b, normal, color);
                Emit(c, normal, color);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
        }

        private void Emit(Vector3 authored, Vector3 normal, Color color)
        {
            if (authored.y > highest) highest = authored.y;
            positions.Add((authored - Origin) * Scale);
            normals.Add(normal);
            colors.Add(color);
        }

        private Color Grained(Color color)
        {
            if (grain <= 0f) return color;
            float factor = 1f + ((float)grainSource.NextDouble() * 2f - 1f) * grain;
            return new Color(Mathf.Clamp01(color.r * factor), Mathf.Clamp01(color.g * factor), Mathf.Clamp01(color.b * factor), color.a);
        }

        private static Vector3 Average(Vector3[] points)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Length; i++) sum += points[i];
            return sum / points.Length;
        }

        private static Vector3[] BuildIcosahedronCorners()
        {
            float golden = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var corners = new[]
            {
                new Vector3(-1f, golden, 0f), new Vector3(1f, golden, 0f), new Vector3(-1f, -golden, 0f), new Vector3(1f, -golden, 0f),
                new Vector3(0f, -1f, golden), new Vector3(0f, 1f, golden), new Vector3(0f, -1f, -golden), new Vector3(0f, 1f, -golden),
                new Vector3(golden, 0f, -1f), new Vector3(golden, 0f, 1f), new Vector3(-golden, 0f, -1f), new Vector3(-golden, 0f, 1f),
            };
            for (int i = 0; i < corners.Length; i++) corners[i] = corners[i].normalized;
            return corners;
        }
    }
}
