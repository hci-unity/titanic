// The iceberg, for URP (VR + desktop): faceted ice lit by the main light (the Moon) plus a faint icy glow
// so it reads at night, hazed into the horizon with distance exactly like OceanWaves (so it emerges from the
// dark instead of popping in), and the scene fog (underwater murk) on top. _SkyDarkness fades it to black.
Shader "Custom/Iceberg"
{
    Properties
    {
        _Color ("Ice Color", Color) = (0.72, 0.84, 0.95, 1)
        _Glow ("Night Glow", Float) = 0.06
        _AmbientColor ("Night Sky Light", Color) = (0.05, 0.07, 0.1, 1)
        _HorizonColor ("Haze Color (match sky horizon)", Color) = (0.045, 0.06, 0.09, 1)
        _FogStart ("Haze Start (m)", Float) = 50
        _FogEnd ("Haze End (m)", Float) = 160
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "IcebergForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _AmbientColor, _HorizonColor;
                float _Glow, _FogStart, _FogEnd;
            CBUFFER_END
            float _SkyDarkness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                Light moon = GetMainLight();
                float lit = 1 - _SkyDarkness;

                float diffuse = saturate(dot(n, moon.direction));
                float rim = pow(1 - saturate(dot(n, v)), 3);
                float3 col = _Color.rgb * (_AmbientColor.rgb * 2 * lit + moon.color * diffuse + _Glow * lit)
                           + _Color.rgb * rim * 0.08 * lit;

                float haze = saturate((distance(IN.positionWS, _WorldSpaceCameraPos) - _FogStart) / (_FogEnd - _FogStart));
                col = lerp(col, _HorizonColor.rgb * lit, haze);
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
