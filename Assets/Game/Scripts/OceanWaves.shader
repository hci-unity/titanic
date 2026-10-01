// Night ocean for URP (VR + desktop): 4 Gerstner swells displaced in object space, lit by the main
// light (the Moon) as diffuse + a sharp glint, with a distance haze that blends into the sky dome.
// Waves are computed in OBJECT space, so the whole sea tilts/moves with its parent (ShipShake.keepLevel).
// _SkyDarkness (global, set by SinkingSequence) fades the night sky light out at the end.
Shader "Custom/OceanWaves"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.01, 0.035, 0.06, 1)
        _CrestColor ("Crest Color", Color) = (0.06, 0.13, 0.18, 1)
        _AmbientColor ("Night Sky Light", Color) = (0.05, 0.07, 0.1, 1)
        _HorizonColor ("Haze Color (match sky horizon)", Color) = (0.045, 0.06, 0.09, 1)
        _FogStart ("Haze Start (m)", Float) = 50
        _FogEnd ("Haze End (m)", Float) = 160
        _SpecPower ("Moon Glint Sharpness", Float) = 150
        _SpecStrength ("Moon Glint Strength", Float) = 2.5
        _WaveSpeed ("Wave Speed", Float) = 1
        _WaveA ("Wave A (dir xy, steepness, wavelength)", Vector) = (1, 0.3, 0.22, 60)
        _WaveB ("Wave B", Vector) = (0.6, 1, 0.18, 31)
        _WaveC ("Wave C", Vector) = (-0.4, 0.9, 0.15, 18)
        _WaveD ("Wave D", Vector) = (0.9, -0.6, 0.12, 9)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _CrestColor, _AmbientColor, _HorizonColor;
                float _FogStart, _FogEnd, _SpecPower, _SpecStrength, _WaveSpeed;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
            CBUFFER_END
            float _SkyDarkness; // global: 0 = normal night, 1 = black

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float height : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Gerstner wave (Catlike Coding formulation); accumulates the surface tangent frame.
            float3 Gerstner(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z;
                float k = TWO_PI / wave.w;
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y * _WaveSpeed);
                float a = steepness / k;
                float s = sin(f), co = cos(f);
                tangent += float3(-d.x * d.x * steepness * s, d.x * steepness * co, -d.x * d.y * steepness * s);
                binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * co, -d.y * d.y * steepness * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 p = IN.positionOS.xyz;
                float3 tangent = float3(1, 0, 0), binormal = float3(0, 0, 1);
                float3 offset = Gerstner(_WaveA, p, tangent, binormal) + Gerstner(_WaveB, p, tangent, binormal)
                              + Gerstner(_WaveC, p, tangent, binormal) + Gerstner(_WaveD, p, tangent, binormal);
                float ampSum = _WaveA.z * _WaveA.w + _WaveB.z * _WaveB.w + _WaveC.z * _WaveC.w + _WaveD.z * _WaveD.w;
                p += offset;

                OUT.positionWS = TransformObjectToWorld(p);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(normalize(cross(binormal, tangent)));
                OUT.height = offset.y / max(ampSum / TWO_PI, 0.001); // ~ -1 trough .. +1 crest
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                Light moon = GetMainLight();
                float lit = 1 - _SkyDarkness;

                float3 h = normalize(moon.direction + v);
                float glint = pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;
                float diffuse = saturate(dot(n, moon.direction));
                float fresnel = pow(1 - saturate(dot(n, v)), 4);

                float3 col = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(IN.height * 0.5 + 0.5));
                col = col * (_AmbientColor.rgb * lit + moon.color * diffuse * 0.5)
                    + moon.color * glint
                    + _HorizonColor.rgb * fresnel * lit;

                float fog = saturate((distance(IN.positionWS, _WorldSpaceCameraPos) - _FogStart) / (_FogEnd - _FogStart));
                col = lerp(col, _HorizonColor.rgb * lit, fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
