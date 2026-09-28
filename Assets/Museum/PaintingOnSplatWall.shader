// A flat artwork hung on a Gaussian-splat wall.
//
// The splat pass depth-tests every splat against scene depth, and a Marble capture's wall is not a
// surface but a few centimetres to a quarter metre of soft splats in front of it. A quad placed flat
// on the wall is therefore painted over; pulled far enough forward to win, it floats visibly off the
// wall at any oblique angle (measured in SunlitMuseum, 28 Sep 2026: hidden at 3 cm, needed 24 cm at
// the far end of the hall).
//
// So the colour is drawn where the quad is — flat on the wall, no parallax — while the depth written
// is the same point pushed _DepthPull metres out from the wall along the view ray. Wall splats behind
// that lose; anything standing further out (a bust on a plinth) still draws over the painting.
Shader "MuseXR/Painting On Splat Wall"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _DepthPull("Depth pull out from the wall (m)", Float) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _DepthPull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float3 outwardWS : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            // A Unity Quad faces its own -Z, and a hung artwork's +Z points into the wall.
            o.outwardWS = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
            return o;
        }

        // Depth of the point _DepthPull metres out from the wall, measured along the view ray so the
        // pixel stays where it is. Grazing views are clamped rather than pulled towards infinity.
        float PulledDepth(Varyings i)
        {
            float3 toCamera = normalize(GetCameraPositionWS() - i.positionWS);
            float along = _DepthPull / max(dot(toCamera, i.outwardWS), 0.25);
            float4 cs = TransformWorldToHClip(i.positionWS + toCamera * along);
            float depth = cs.z / cs.w;
            #if !UNITY_REVERSED_Z
            depth = depth * 0.5 + 0.5;
            #endif
            return depth;
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings i, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                depth = PulledDepth(i);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings i, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                depth = PulledDepth(i);
                return 0;
            }
            ENDHLSL
        }
    }
}
