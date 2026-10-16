// Water.shader — the lake surfaces FieldMesh.cs lays over each lake bed.
//
// A flat matte sheet, lit by Paper.cginc like the ground beside it, faded so the
// bed shows through and a lake reads as a hollow with water in it. Colour comes
// from the vertex colour, open water toward the deep tone in the middle.
//
// The foam is drawn here and not in the mesh. Every vertex carries its distance
// to the nearest ground in alpha (a fraction of _ShoreSpan, FieldMesh.ShoreSpan),
// and the foam is wherever that distance is under a line that three slow sines
// of position and time bend back and forth. So the foam follows the shore but
// not the cell edges, and it moves. A second, fainter line further out swells
// in and out, the way water laps.
//
// Two slow wave trains cross the surface in different directions, each a broad
// faint swell of lighter water. Soft bands, not glints: .art/STYLE.md rules out
// specular sparkle, and Paper's lighting has none to give.
//
// The ripple is a slow, very small rise and fall of the surface, under three
// hundredths of a unit, with its slope fed into the normal so the light moves on
// it a little.
//
// Casts no shadow (no shadow caster pass; FieldMesh also turns casting off on the
// renderer), so a lake never darkens its own bed.
Shader "WordCraft/Water"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.78
        _Ripple ("Ripple height", Range(0, 0.03)) = 0.022
        _Foam ("Foam colour, set from FieldPalette.Foam", Color) = (1, 1, 1, 1)
        _ShoreSpan ("Shore distance alpha 1 stands for, set from FieldMesh.ShoreSpan", Float) = 2.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf WcPaper alpha:fade vertex:vert nofog finalcolor:wcfinal
        #pragma target 3.0

        #include "Paper.cginc"

        half _Opacity;
        float _Ripple;
        fixed4 _Foam;
        float _ShoreSpan;

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void vert(inout appdata_full v)
        {
            // Read in world space so neighbouring chunks ripple as one sheet. The
            // terrain is built in world units under an unscaled root, so an
            // object-space offset is a world-space one.
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float across = world.x * 1.3 + _Time.y * 0.9;
            float along = world.z * 1.1 - _Time.y * 0.7;
            float sinAcross = sin(across);
            float sinAlong = sin(along);

            v.vertex.y += _Ripple * sinAcross * sinAlong;

            // The slope of that rise, so the surface's light moves with it.
            float slopeX = _Ripple * 1.3 * cos(across) * sinAlong;
            float slopeZ = _Ripple * 1.1 * sinAcross * cos(along);
            v.normal = normalize(float3(-slopeX, 1.0, -slopeZ));
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 p = IN.worldPos.xz;
            float t = _Time.y;
            float shore = IN.color.a * _ShoreSpan;

            // How far the foam line is pushed out or pulled in here: a long slow
            // swell that differs from one stretch of shore to the next, and three
            // shorter sines on top, so the line never traces the lake's outline
            // and never stands still.
            float wobble = sin(p.x * 0.70 + p.y * 0.45 + 1.7) * 0.14
                         + sin(p.x * 1.9 + p.y * 0.7 + t * 0.50) * 0.10
                         + sin(p.y * 2.6 - p.x * 1.1 - t * 0.37) * 0.07
                         + sin((p.x + p.y) * 4.3 + t * 0.90) * 0.03;

            // The foam band, from the bank out to about a third of a cell. The
            // bank's waterline stands 0.1 to 0.3 in from the cell edge, so the
            // visible foam is a ribbon that thins to nothing in places and swells
            // to half a cell in others.
            float edge = 0.42 + wobble;
            float foam = 1.0 - smoothstep(edge - 0.03, edge + 0.03, shore);

            // A thin line further out that swells toward the shore and back, in
            // short pieces and faint, so it reads as a ripple coming in and not as
            // a second frame round the lake.
            float swell = 0.85 + 0.10 * sin(t * 0.6 + p.x * 0.4 + p.y * 0.3) + wobble * 1.4;
            float pieces = smoothstep(0.3, 0.8, sin(p.x * 1.3 - p.y * 1.7 + t * 0.3));
            float lap = (1.0 - smoothstep(0.02, 0.05, abs(shore - swell))) * 0.18 * pieces;

            // Two wave trains in different directions, drawn as broad, faint
            // swells of lighter water rather than lines: a thin bright crest reads
            // as glare, which .art/STYLE.md rules out.
            float a = sin(dot(p, float2(0.80, 0.60)) * 2.3 + t * 0.55);
            float b = sin(dot(p, float2(-0.45, 0.89)) * 1.8 - t * 0.42);
            float crest = (smoothstep(0.8, 1.0, a) + smoothstep(0.8, 1.0, b)) * 0.035;

            float white = max(foam * 0.85, max(lap, crest));
            o.Albedo = lerp(IN.color.rgb, _Foam.rgb, white);
            o.Alpha = lerp(_Opacity, 0.92, foam);
        }

        void wcfinal(Input IN, SurfaceOutput o, inout fixed4 color)
        {
            WcAtmosphere(IN.worldPos, color);
        }
        ENDCG
    }

    FallBack Off
}
