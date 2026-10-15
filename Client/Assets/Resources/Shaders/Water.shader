// Water.shader — the lake surfaces FieldMesh.cs lays over each lake bed.
//
// A flat matte sheet, lit by Paper.cginc like the ground beside it, faded so the
// bed shows through and a lake reads as a hollow with water in it. Colour comes
// from the vertex colour: pale foam where the water meets anything else, deeper
// toward the middle.
//
// The ripple is a slow, very small rise and fall of the surface, under three
// hundredths of a unit, with its slope fed into the normal so the light moves on
// it a little. No specular term: .art/STYLE.md rules out glints, and Paper's
// lighting has none to give.
//
// Casts no shadow (no shadow caster pass; FieldMesh also turns casting off on the
// renderer), so a lake never darkens its own bed.
Shader "WordCraft/Water"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.78
        _Ripple ("Ripple height", Range(0, 0.03)) = 0.022
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
            o.Albedo = IN.color.rgb;
            o.Alpha = _Opacity * IN.color.a;
        }

        void wcfinal(Input IN, SurfaceOutput o, inout fixed4 color)
        {
            WcAtmosphere(IN.worldPos, color);
        }
        ENDCG
    }

    FallBack Off
}
