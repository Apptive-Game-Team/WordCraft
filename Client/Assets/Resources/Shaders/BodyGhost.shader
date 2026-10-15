// BodyGhost.shader — the same body, see-through: a construction site, the
// placement ghost, debris fading out. Shared as Models.Ghost; a body swaps to it
// while its alpha is below one and back when it is not.
//
// A body is several overlapping solids, and a plain alpha:fade draws every one
// of them, so a ghost tower would show its own buried plinth and the far wall of
// its roof through the near one — an x-ray, not a ghost. The first pass writes
// depth only, so the colour pass keeps just the surface nearest the camera and
// the ghost reads as one faded object. Same UnityObjectToClipPos as the surface
// pass, so the two depths agree exactly and the colour pass passes LEqual.
Shader "WordCraft/BodyGhost"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.5
        _Desaturate ("Desaturate", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        // For the surface passes below. The depth pass overrides it with its own.
        ZWrite Off

        Pass
        {
            Name "GHOSTDEPTH"
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag () : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        CGPROGRAM
        #pragma surface surf WcPaper nofog finalcolor:wcfinal alpha:fade
        #pragma target 3.0
        #include "Paper.cginc"

        fixed4 _Tint;
        fixed _Alpha;
        fixed _Desaturate;

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed3 albedo = IN.color.rgb * _Tint.rgb;
            fixed grey = dot(albedo, fixed3(0.299, 0.587, 0.114));
            o.Albedo = lerp(albedo, fixed3(grey, grey, grey), _Desaturate);
            o.Alpha = _Alpha;
        }

        void wcfinal (Input IN, SurfaceOutput o, inout fixed4 color)
        {
            WcAtmosphere(IN.worldPos, color);
        }
        ENDCG
    }
}
