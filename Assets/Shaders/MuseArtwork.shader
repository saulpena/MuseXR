// A hung artwork that sits in a splat world's light without any real lighting.
//
// Splat worlds have their light baked in, so a real light on a painting cannot match the room and
// costs per-pixel lighting besides. This is URP Unlit plus three cheap fakes, all driven by the
// time ring (TimeRingDriver) through globals so every artwork follows at once:
//   - the room's grade, as a tint (_BaseColor, set per renderer);
//   - a picture lamp: a soft pool of light toward the top of the canvas (_MuseFrameLight);
//   - the same distance haze the splats get (_MuseHaze), so a painting fades exactly like the
//     wall it hangs on instead of looking pasted on in front of the mist.
// One texture sample and a handful of ALU: the same cost class as URP/Unlit.
Shader "MuseXR/Artwork"
{
    Properties
    {
        [MainTexture] _BaseMap ("Artwork", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint (room grade)", Color) = (1,1,1,1)
        _LampCentre ("Lamp centre (UV)", Vector) = (0.5, 1.05, 0, 0)
        _LampRadius ("Lamp pool radius (UV)", Float) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _LampCentre;
                float _LampRadius;
            CBUFFER_END

            // rgb = lamp colour premultiplied by intensity above "off" (0 = no lamp), a unused.
            float4 _MuseFrameLight;
            // rgb = haze colour, a = density per metre (0 = none). Same values as the splats get.
            float4 _MuseHaze;

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;

                // Picture lamp: brightest near the top edge, falling off smoothly across the canvas.
                float d = length((i.uv - _LampCentre.xy) / max(_LampRadius, 1e-3));
                half pool = saturate(1.0 - d * d);
                col *= 1.0 + _MuseFrameLight.rgb * pool;

                // The splats' haze, by distance from the eye.
                if (_MuseHaze.a > 0)
                {
                    float hazeAmount = 1.0 - exp(-_MuseHaze.a * distance(i.positionWS, GetCameraPositionWS()));
                    col = lerp(col, _MuseHaze.rgb, hazeAmount);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
