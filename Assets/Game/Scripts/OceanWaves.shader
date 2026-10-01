// Night ocean for URP (VR + desktop): 4 Gerstner swells displaced in object space, lit by the main
// light (the Moon) as diffuse + a sharp glint, with a distance haze that blends into the sky dome.
// Waves are computed in OBJECT space, so the whole sea tilts/moves with its parent (ShipShake.keepLevel).
// _SkyDarkness (global, set by SinkingSequence) fades the night sky light out at the end.
// _WaveCalm (global, Flooding) shrinks the swells; back faces (seen from underwater) draw as the fog color.
// Water is see-through by DEPTH (scene depth texture): shallow = clear teal over the floor, deep = opaque,
// so it reads as water where it floods the room. Small ripples add sparkle on top of the swells.
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
        _ShallowColor ("Shallow Tint", Color) = (0.05, 0.16, 0.18, 1)
        _Clarity ("Clarity (lower = clearer)", Float) = 0.7
        _RippleStrength ("Ripple Strength", Float) = 0.12
        _RippleScale ("Ripple Wavelength (m)", Float) = 1.6
    }
    SubShader
    {
        // Just before other transparents, so window glass still draws over the sea seen through it.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off // the surface must stay visible from below once the player is underwater
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _CrestColor, _AmbientColor, _HorizonColor, _ShallowColor;
                float _FogStart, _FogEnd, _SpecPower, _SpecStrength, _WaveSpeed, _Clarity, _RippleStrength, _RippleScale;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
            CBUFFER_END
            float _SkyDarkness; // global: 0 = normal night, 1 = black
            float _WaveCalm;    // global: 0 = full swells, 1 = flat

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
                float steepness = wave.z * (1 - _WaveCalm);
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
                float ampSum = (_WaveA.z * _WaveA.w + _WaveB.z * _WaveB.w + _WaveC.z * _WaveC.w + _WaveD.z * _WaveD.w) * (1 - _WaveCalm);
                p += offset;

                OUT.positionWS = TransformObjectToWorld(p);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(normalize(cross(binormal, tangent)));
                OUT.height = offset.y / max(ampSum / TWO_PI, 0.001); // ~ -1 trough .. +1 crest
                return OUT;
            }

            // Small fast ripples (normal only): three crossing sine trains, analytic slope.
            float3 Ripples(float2 xz)
            {
                float2 slope = 0;
                float2 dirs[3] = { float2(0.8, 0.6), float2(-0.5, 0.87), float2(0.2, -0.98) };
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float k = TWO_PI / (_RippleScale * (1 + i * 0.37));
                    float f = k * dot(dirs[i], xz) - _Time.y * (2.1 + i * 0.6);
                    slope += dirs[i] * cos(f);
                }
                return float3(-slope.x, 0, -slope.y) * _RippleStrength;
            }

            half4 frag(Varyings IN, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                if (!front) return half4(unity_FogColor.rgb * (1 - _SkyDarkness), 1); // underside, seen from underwater

                float3 n = normalize(IN.normalWS + Ripples(IN.positionWS.xz));
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

                // Water thickness along the view ray: scene depth behind the surface minus the surface's own depth.
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float thickness = max(0, sceneDepth - LinearEyeDepth(IN.positionWS, GetWorldToViewMatrix()));
                float opacity = 1 - exp(-thickness * _Clarity);
                col = lerp(_ShallowColor.rgb * (_AmbientColor.rgb * 3 * lit + moon.color * 0.4), col, opacity);

                float fog = saturate((distance(IN.positionWS, _WorldSpaceCameraPos) - _FogStart) / (_FogEnd - _FogStart));
                col = lerp(col, _HorizonColor.rgb * lit, fog);
                return half4(col, saturate(max(opacity, glint) * 0.85 + 0.15)); // never fully invisible: a faint film even at the edge
            }
            ENDHLSL
        }
    }
}
