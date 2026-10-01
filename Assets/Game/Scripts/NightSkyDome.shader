// Night sky drawn on an inside-out sphere that lives under "Outside", so it stays level with the sea
// while the ship tilts (a real skybox is camera-anchored and can't). Gradient + stars use the dome's
// OBJECT-space direction; the moon disk follows the main light (the Moon, also kept level).
// _SkyDarkness (global, set by SinkingSequence) fades it to black at the end.
Shader "Custom/NightSkyDome"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.004, 0.006, 0.016, 1)
        _HorizonColor ("Horizon Color", Color) = (0.045, 0.06, 0.09, 1)
        _HorizonFalloff ("Horizon Falloff", Float) = 3
        _MoonColor ("Moon Color", Color) = (0.95, 0.97, 1, 1)
        _MoonSize ("Moon Size (cos of radius)", Float) = 0.99975
        _MoonGlow ("Moon Glow", Float) = 0.25
        _StarDensity ("Star Density", Range(0, 0.01)) = 0.002
        _StarBrightness ("Star Brightness", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Cull Front

        Pass
        {
            Name "SkyDome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _MoonColor;
                float _HorizonFalloff, _MoonSize, _MoonGlow, _StarDensity, _StarBrightness;
            CBUFFER_END
            float _SkyDarkness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 dirWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz;
                OUT.dirWS = TransformObjectToWorldDir(IN.positionOS.xyz);
                return OUT;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dOS = normalize(IN.dirOS);
                float up = saturate(dOS.y);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, 1 - pow(1 - up, _HorizonFalloff));

                float star = step(1 - _StarDensity, Hash(floor(dOS * 300)));
                col += star * _StarBrightness * saturate(up * 6);

                float m = dot(normalize(IN.dirWS), GetMainLight().direction);
                col += _MoonColor.rgb * (smoothstep(_MoonSize - 0.00006, _MoonSize, m) + pow(saturate(m), 400) * _MoonGlow);

                return half4(col * (1 - _SkyDarkness), 1);
            }
            ENDHLSL
        }
    }
}
