// The field's marks: selection rings, reach rings, arrival marks, hover, blob
// shadows, meters. Unlit on purpose — a mark is information the HUD put on the
// ground, not a thing standing in the light, so it is the same colour at noon
// and in a shadow and under the fog, exactly as docs/UI-STYLE.md says the local
// peer's own marks are never fogged.
//
// One shader, two uses, told apart by the material's ZTest:
//   LEqual  — a decal lying on the terrain. Depth-tested so a hill in front of
//             it hides it like it hides the ground it is drawn on, and pulled a
//             hair toward the camera by the Offset so it never fights the ground
//             for the same depth.
//   Always  — a meter floating over a head. Never hidden, because a health bar
//             behind a ridge is a health bar the player cannot read.
Shader "WordCraft/Decal"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Offset -2, -2
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
