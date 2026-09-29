// SPDX-License-Identifier: MIT
// MuseXR local addition: the shape of a splat portal (see SplatPortal) and the passes that use it.
//
// The shape is drawn on a quad centred on the door, in the door's plane, 2 * _MaskHalf metres
// across. Door space is the quad's own XY in metres. It is the union of
//   - the door's aperture (|p| <= _DoorHalf), once _DoorOn is set, and
//   - a ragged "seep" patch round the door's centre whose radius grows with _Seep (0..1) up to
//     _SeepRadius metres: the next world leaking out around the doorway before you reach it.
//
// Pass 0 writes the shape into the portal mask the splat shaders read.
// Pass 1 marks, in the camera's own stencil, the shape's pixels where the solid geometry already
//        drawn lies BEYOND the door plane (the quad is nearer: ZTest LEqual passes).
// Pass 2 erases those pixels: depth back to the far plane, colour to black. So a mesh standing
//        behind the door (the Buddha the door is set into) no longer hides the world beyond it,
//        while anything in front of the door (its frame, its leaves) is untouched.
Shader "Hidden/Gaussian Splatting/Portal Mask"
{
    Properties
    {
        _DoorHalf ("Door half size (m)", Vector) = (1.2, 1.8, 0, 0)
        _MaskHalf ("Mask quad half size (m)", Vector) = (1.2, 1.8, 0, 0)
        _DoorOn ("Door aperture on", Float) = 1
        _Seep ("Seep amount 0..1", Float) = 0
        _SeepRadius ("Seep radius at 1 (m)", Float) = 2.5
        _ClipSeepToDoor ("Seep only inside the aperture (a tear in a canvas)", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    float4 _DoorHalf;
    float4 _MaskHalf;
    float _DoorOn;
    float _Seep;
    float _SeepRadius;
    float _ClipSeepToDoor;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 door : TEXCOORD0;   // door-space metres
    };

    v2f vertShape (float4 vertex : POSITION)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(vertex);
        o.door = vertex.xy * 2.0 * _MaskHalf.xy;   // Unity's Quad spans -0.5..0.5
        return o;
    }

    float hash21 (float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float valueNoise (float2 p)
    {
        float2 i = floor(p), f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    bool InShape (float2 p)
    {
        bool inDoor = all(abs(p) <= _DoorHalf.xy);
        if (_DoorOn > 0.5 && inDoor)
            return true;
        if (_Seep <= 0.001)
            return false;
        // Ragged edge: two octaves of slowly drifting noise push the boundary in and out.
        float t = _Time.y * 0.15;
        float n = valueNoise(p * 1.1 + t) * 0.65 + valueNoise(p * 2.7 - t) * 0.35;
        float r = length(p * float2(1.0, 0.8)) / max(1e-3, _SeepRadius * _Seep);
        bool inSeep = r + (n - 0.5) * 0.6 < 1.0;
        return _ClipSeepToDoor > 0.5 ? inSeep && inDoor : inSeep;
    }

    float fragMask (v2f i) : SV_Target
    {
        if (!InShape(i.door)) discard;
        return 1;
    }

    fixed4 fragMark (v2f i) : SV_Target
    {
        if (!InShape(i.door)) discard;
        return 0;
    }

    struct v2fFull { float4 pos : SV_POSITION; };

    v2fFull vertFull (uint id : SV_VertexID)
    {
        v2fFull o;
        float2 uv = float2((id << 1) & 2, id & 2);
        o.pos = float4(uv * 2.0 - 1.0, 0.5, 1.0);
        return o;
    }

    fixed4 fragErase (v2fFull i, out float depth : SV_Depth) : SV_Target
    {
        #if UNITY_REVERSED_Z
        depth = 0.0;
        #else
        depth = 1.0;
        #endif
        return fixed4(0, 0, 0, 1);
    }
    ENDCG

    SubShader
    {
        // 0: portal mask
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            CGPROGRAM
            #pragma vertex vertShape
            #pragma fragment fragMask
            ENDCG
        }

        // 1: mark solid geometry beyond the door plane inside the shape
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask 0
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vertShape
            #pragma fragment fragMark
            ENDCG
        }

        // 2: erase the marked pixels (depth to far, colour to black) and clear the mark
        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp Equal Pass Zero }
            CGPROGRAM
            #pragma vertex vertFull
            #pragma fragment fragErase
            ENDCG
        }
    }
}
