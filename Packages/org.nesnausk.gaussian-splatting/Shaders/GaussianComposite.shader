// SPDX-License-Identifier: MIT
Shader "Hidden/Gaussian Splatting/Composite"
{
    SubShader
    {
        // Pass 0: composite the splat layer over the camera colour.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#include "UnityCG.cginc"

struct v2f
{
    float4 vertex : SV_POSITION;
};

v2f vert (uint vtxID : SV_VertexID)
{
    v2f o;
    float2 quadPos = float2(vtxID&1, (vtxID>>1)&1) * 4.0 - 1.0;
	o.vertex = float4(quadPos, 1, 1);
    return o;
}

Texture2D _GaussianSplatRT;
// MuseXR reduced-resolution splat layer (GaussianSplatSettings.ResolutionScale). When it is off
// the original Load path below runs unchanged.
SamplerState sampler_linear_clamp;
float4 _GaussianSplatOutputSize; // xy = camera target size in pixels
float _GaussianSplatScaled;

half4 frag (v2f i) : SV_Target
{
    half4 col;
    if (_GaussianSplatScaled > 0)
    {
        // The RT holds colour premultiplied by coverage, and the blend below multiplies by
        // coverage again. Bilinear filtering across an edge (world next to an empty,
        // depth-rejected texel) lowers both, so the edge came out too dark. Restore the colour to
        // the strongest of the four taps' coverage while keeping the filtered coverage for the
        // blend. Where all four taps agree this equals the plain sample, i.e. the original look.
        float2 uv = i.vertex.xy / _GaussianSplatOutputSize.xy;
        col = _GaussianSplatRT.SampleLevel(sampler_linear_clamp, uv, 0);
        float4 a4 = _GaussianSplatRT.GatherAlpha(sampler_linear_clamp, uv);
        float aMax = max(max(a4.x, a4.y), max(a4.z, a4.w));
        col.rgb *= aMax / max(col.a, 1e-4);
    }
    else
        col = _GaussianSplatRT.Load(int3(i.vertex.xy, 0));
    col.rgb = GammaToLinearSpace(col.rgb);
    col.a = saturate(col.a * 1.5);
    return col;
}
ENDCG
        }

        // Pass 1 (MuseXR): reduce the camera depth to the splat layer's resolution, so the splats
        // stay depth-tested against geometry. Keeps the FARTHEST depth of the 2x2 full-res texels
        // under each low-res pixel: splats are rejected only where geometry covers the whole
        // block, so at silhouettes the world may overlap an object by about one pixel rather than
        // leave a dark gap around it.
        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off
            ColorMask 0

CGPROGRAM
#pragma vertex vert
#pragma fragment fragDepth
#pragma require compute
#pragma use_dxc
#include "UnityCG.cginc"

struct v2f
{
    float4 vertex : SV_POSITION;
};

v2f vert (uint vtxID : SV_VertexID)
{
    v2f o;
    float2 quadPos = float2(vtxID&1, (vtxID>>1)&1) * 4.0 - 1.0;
	o.vertex = float4(quadPos, 1, 1);
    return o;
}

Texture2D<float> _GaussianSplatSrcDepth;
float4 _GaussianSplatSrcDepthSize; // xy = full-res size, z = full-res pixels per low-res pixel

float fragDepth (v2f i) : SV_Depth
{
    float2 p = i.vertex.xy * _GaussianSplatSrcDepthSize.z;
    int2 maxXY = int2(_GaussianSplatSrcDepthSize.xy) - 1;
    int2 a = clamp(int2(p - 0.5), int2(0, 0), maxXY);
    int2 b = min(a + 1, maxXY);
    float d0 = _GaussianSplatSrcDepth.Load(int3(a.x, a.y, 0));
    float d1 = _GaussianSplatSrcDepth.Load(int3(b.x, a.y, 0));
    float d2 = _GaussianSplatSrcDepth.Load(int3(a.x, b.y, 0));
    float d3 = _GaussianSplatSrcDepth.Load(int3(b.x, b.y, 0));
#if UNITY_REVERSED_Z
    return min(min(d0, d1), min(d2, d3));   // reversed Z: far is 0
#else
    return max(max(d0, d1), max(d2, d3));
#endif
}
ENDCG
        }
    }
}
