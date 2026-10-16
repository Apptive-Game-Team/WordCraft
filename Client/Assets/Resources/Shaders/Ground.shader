// Ground.shader — every opaque surface of the terrain: grass, cliff faces, the
// rock ridge, lake beds, the cliff round the map's edge and the small props
// baked into the same chunks (FieldMesh.cs, FieldProps.cs).
//
// All colour comes from the vertex colour, so one material serves the whole
// field and a repaint is a change to FieldPalette.cs, never to a material. The
// light is Paper.cginc's: three value bands, hemisphere ambient, a paper rim,
// the fog of war and distance haze, and last the fade that sinks everything
// outside the map into the backdrop.
Shader "WordCraft/Ground"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf WcPaper fullforwardshadows nofog finalcolor:wcfinal
        #pragma target 3.0

        #include "Paper.cginc"

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = IN.color.rgb;
            o.Alpha = 1.0;
        }

        void wcfinal(Input IN, SurfaceOutput o, inout fixed4 color)
        {
            WcAtmosphere(IN.worldPos, color);
            WcEdgeFade(IN.worldPos, color);
        }
        ENDCG
    }

    // For the shadow caster pass. The terrain moves no vertex, so the stock
    // caster casts exactly the shape this draws.
    FallBack "Diffuse"
}
