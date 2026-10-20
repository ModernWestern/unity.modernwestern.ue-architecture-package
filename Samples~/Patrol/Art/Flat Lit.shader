// Solid color with a fixed light, for the sample's cubes. The pass has no LightMode tag, so the Built-in pipeline and
// URP both draw it the same way and nothing shows up pink, whatever the project uses.
Shader "UE Architecture Samples/Patrol/Flat Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Light from the upper left, in world space: no scene lights needed.
                float light = saturate(dot(normalize(i.normal), normalize(float3(-0.5, 1.0, -0.7))));
                return fixed4(_Color.rgb * (0.45 + 0.55 * light), 1);
            }
            ENDCG
        }
    }
}
