// Stereo-correct planar mirror shader for URP (VR + desktop).
//
// Samples a SEPARATE reflection texture per eye (_ReflLeft / _ReflRight), chosen by
// unity_StereoEyeIndex, using SCREEN-SPACE UVs. Each per-eye texture is rendered by
// MirrorReflection.cs from that eye's reflected view + oblique projection, so screen-space
// sampling lines up exactly and the reflection stays world-anchored like a real mirror.
//
// No U-flip here: the C# side uses a true world-space reflection matrix, which already
// produces a correctly-handed mirror image. (If the image ever comes out horizontally
// mirrored the wrong way, that's the one knob to revisit.)
Shader "Custom/MirrorReflection"
{
    Properties
    {
        _ReflLeft ("Reflection Left", 2D) = "black" {}
        _ReflRight ("Reflection Right", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "MirrorPass"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ReflLeft);  SAMPLER(sampler_ReflLeft);
            TEXTURE2D(_ReflRight); SAMPLER(sampler_ReflRight);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vi.positionCS;
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                half4 col = (unity_StereoEyeIndex == 0)
                    ? SAMPLE_TEXTURE2D(_ReflLeft, sampler_ReflLeft, uv)
                    : SAMPLE_TEXTURE2D(_ReflRight, sampler_ReflRight, uv);
                return col * _Tint;
            }
            ENDHLSL
        }
    }
}
