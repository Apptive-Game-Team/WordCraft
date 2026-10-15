// Body.shader — every solid body on the field: units, buildings, resource nodes,
// debris. One material for all of them (Models.Opaque), so the field batches and
// a palette is vertex colour rather than a material per faction.
//
// _Tint and _Desaturate are declared as properties so a MaterialPropertyBlock
// can override them per renderer. A value the shader reads but Properties does
// not name is a global, and a property block cannot reach it.
Shader "WordCraft/Body"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Desaturate ("Desaturate", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf WcPaper fullforwardshadows nofog finalcolor:wcfinal addshadow
        #pragma target 3.0
        #include "Paper.cginc"

        fixed4 _Tint;
        fixed _Desaturate;

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed3 albedo = IN.color.rgb * _Tint.rgb;

            // The fog's "last known state" reads as a memory by losing colour
            // before it loses value. A multiply alone can only darken, and a
            // darkened palette still says which faction it is just as loudly.
            fixed grey = dot(albedo, fixed3(0.299, 0.587, 0.114));
            o.Albedo = lerp(albedo, fixed3(grey, grey, grey), _Desaturate);
            o.Alpha = 1;
        }

        void wcfinal (Input IN, SurfaceOutput o, inout fixed4 color)
        {
            WcAtmosphere(IN.worldPos, color);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
