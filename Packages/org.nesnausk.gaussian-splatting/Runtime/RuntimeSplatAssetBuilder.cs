// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// MuseXR: builds a <see cref="GaussianSplatAsset"/> from .spz bytes in a PLAYER, where the
    /// Editor-only GaussianSplatAssetCreator cannot run. Same pipeline, same Medium quality
    /// (Norm11 / Norm11 / Norm8x4 / Norm6), so a world generated on the headset is byte-for-byte the
    /// asset the Editor would have written — RuntimeSplatAssetBuilderTests compares the two.
    ///
    /// The only real difference is where the data ends up: the Editor writes .bytes files and
    /// imports them as TextAssets; here the same bytes go straight into <c>new TextAsset(bytes)</c>.
    /// The encode jobs are copies of the creator's (they are private to an Editor assembly), kept
    /// in the same order and with the same constants.
    /// </summary>
    [BurstCompile]
    public static class RuntimeSplatAssetBuilder
    {
        /// <summary>Same layout as the Editor's InputSplatData.</summary>
        public struct Splat
        {
            public Vector3 pos;
            public Vector3 nor;
            public Vector3 dc0;
            public Vector3 sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, shA, shB, shC, shD, shE, shF;
            public float opacity;
            public Vector3 scale;
            public Quaternion rot;
        }

        const GaussianSplatAsset.VectorFormat kFormatPos = GaussianSplatAsset.VectorFormat.Norm11;
        const GaussianSplatAsset.VectorFormat kFormatScale = GaussianSplatAsset.VectorFormat.Norm11;
        const GaussianSplatAsset.ColorFormat kFormatColor = GaussianSplatAsset.ColorFormat.Norm8x4;
        const GaussianSplatAsset.SHFormat kFormatSH = GaussianSplatAsset.SHFormat.Norm6;

        // ------------------------------------------------------------------ SPZ

        /// <summary>
        /// Decodes a gzipped SPZ v2 file. The caller owns (and must Dispose) the returned array.
        /// SH level 0 — every Marble export — carries no harmonics; they stay zero.
        /// </summary>
        public static NativeArray<Splat> DecodeSpz(byte[] spz)
        {
            using var ms = new MemoryStream(spz, false);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);

            var header = new byte[16];
            if (ReadFully(gz, header) != 16) throw new IOException("SPZ: file shorter than its header");
            uint magic = BitConverter.ToUInt32(header, 0);
            uint version = BitConverter.ToUInt32(header, 4);
            int count = (int)BitConverter.ToUInt32(header, 8);
            int shLevel = header[12], fractBits = header[13];
            if (magic != 0x5053474e) throw new IOException($"SPZ: bad magic {magic:X8}");
            if (version != 2) throw new IOException($"SPZ: version {version}, only 2 is supported");
            if (count < 1 || count > 10_000_000) throw new IOException($"SPZ: splat count {count} out of range");
            if (shLevel > 3 || fractBits > 24) throw new IOException($"SPZ: sh level {shLevel} / fract bits {fractBits} out of range");

            int shCoeffs = shLevel switch { 1 => 3, 2 => 8, 3 => 15, _ => 0 };
            var packedPos = new byte[count * 9];
            var packedAlpha = new byte[count];
            var packedCol = new byte[count * 3];
            var packedScale = new byte[count * 3];
            var packedRot = new byte[count * 3];
            var packedSh = new byte[count * 3 * shCoeffs];
            bool ok = ReadFully(gz, packedPos) == packedPos.Length
                    && ReadFully(gz, packedAlpha) == packedAlpha.Length
                    && ReadFully(gz, packedCol) == packedCol.Length
                    && ReadFully(gz, packedScale) == packedScale.Length
                    && ReadFully(gz, packedRot) == packedRot.Length
                    && ReadFully(gz, packedSh) == packedSh.Length;
            if (!ok) throw new IOException("SPZ: file smaller than its header says");

            var splats = new NativeArray<Splat>(count, Allocator.Persistent);
            var job = new UnpackJob
            {
                packedPos = new NativeArray<byte>(packedPos, Allocator.TempJob),
                packedAlpha = new NativeArray<byte>(packedAlpha, Allocator.TempJob),
                packedCol = new NativeArray<byte>(packedCol, Allocator.TempJob),
                packedScale = new NativeArray<byte>(packedScale, Allocator.TempJob),
                packedRot = new NativeArray<byte>(packedRot, Allocator.TempJob),
                packedSh = new NativeArray<byte>(packedSh, Allocator.TempJob),
                shCoeffs = shCoeffs,
                fractScale = 1.0f / (1 << fractBits),
                splats = splats,
            };
            job.Schedule(count, 4096).Complete();
            job.packedPos.Dispose(); job.packedAlpha.Dispose(); job.packedCol.Dispose();
            job.packedScale.Dispose(); job.packedRot.Dispose(); job.packedSh.Dispose();
            return splats;
        }

        static int ReadFully(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        [BurstCompile]
        struct UnpackJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction, ReadOnly] public NativeArray<byte> packedPos, packedScale, packedRot, packedAlpha, packedCol, packedSh;
            public float fractScale;
            public int shCoeffs;
            public NativeArray<Splat> splats;

            public void Execute(int index)
            {
                var splat = splats[index];
                splat.pos = new Vector3(UnpackFloat(index * 3 + 0) * fractScale, UnpackFloat(index * 3 + 1) * fractScale, UnpackFloat(index * 3 + 2) * fractScale);

                splat.scale = new Vector3(packedScale[index * 3 + 0], packedScale[index * 3 + 1], packedScale[index * 3 + 2]) / 16.0f - new Vector3(10.0f, 10.0f, 10.0f);
                splat.scale = GaussianUtils.LinearScale(splat.scale);

                Vector3 xyz = new Vector3(packedRot[index * 3 + 0], packedRot[index * 3 + 1], packedRot[index * 3 + 2]) * (1.0f / 127.5f) - new Vector3(1, 1, 1);
                float w = math.sqrt(math.max(0.0f, 1.0f - xyz.sqrMagnitude));
                var qq = math.normalize(new float4(xyz.x, xyz.y, xyz.z, w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                splat.rot = new Quaternion(qq.x, qq.y, qq.z, qq.w);

                splat.opacity = packedAlpha[index] / 255.0f;

                Vector3 col = new Vector3(packedCol[index * 3 + 0], packedCol[index * 3 + 1], packedCol[index * 3 + 2]);
                col = col / 255.0f - new Vector3(0.5f, 0.5f, 0.5f);
                col /= 0.15f;
                splat.dc0 = GaussianUtils.SH0ToColor(col);

                if (shCoeffs > 0)
                {
                    int shIdx = index * shCoeffs * 3;
                    splat.sh1 = UnpackSH(shIdx); shIdx += 3;
                    splat.sh2 = UnpackSH(shIdx); shIdx += 3;
                    splat.sh3 = UnpackSH(shIdx); shIdx += 3;
                    if (shCoeffs > 3)
                    {
                        splat.sh4 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh5 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh6 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh7 = UnpackSH(shIdx); shIdx += 3;
                        splat.sh8 = UnpackSH(shIdx); shIdx += 3;
                    }
                    if (shCoeffs > 8)
                    {
                        splat.sh9 = UnpackSH(shIdx); shIdx += 3;
                        splat.shA = UnpackSH(shIdx); shIdx += 3;
                        splat.shB = UnpackSH(shIdx); shIdx += 3;
                        splat.shC = UnpackSH(shIdx); shIdx += 3;
                        splat.shD = UnpackSH(shIdx); shIdx += 3;
                        splat.shE = UnpackSH(shIdx); shIdx += 3;
                        splat.shF = UnpackSH(shIdx);
                    }
                }
                splats[index] = splat;
            }

            float UnpackFloat(int idx)
            {
                int fx = packedPos[idx * 3 + 0] | (packedPos[idx * 3 + 1] << 8) | (packedPos[idx * 3 + 2] << 16);
                fx |= (fx & 0x800000) != 0 ? -16777216 : 0;
                return fx;
            }

            Vector3 UnpackSH(int idx) =>
                (new Vector3(packedSh[idx], packedSh[idx + 1], packedSh[idx + 2]) - new Vector3(128.0f, 128.0f, 128.0f)) / 128.0f;
        }

        // ------------------------------------------------------------------ asset

        /// <summary>
        /// Encodes decoded splats into a Medium-quality asset. <paramref name="splats"/> is reordered
        /// and rewritten in place (as the Editor creator does); the caller still owns and disposes it.
        /// </summary>
        public static unsafe GaussianSplatAsset Build(NativeArray<Splat> splats, string name)
        {
            float3 boundsMin, boundsMax;
            new CalcBoundsJob { m_BoundsMin = &boundsMin, m_BoundsMax = &boundsMax, m_SplatData = splats }.Schedule().Complete();

            ReorderMorton(splats, boundsMin, boundsMax);

            var asset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            asset.Initialize(splats.Length, kFormatPos, kFormatScale, kFormatColor, kFormatSH, boundsMin, boundsMax, null);
            asset.name = name;
            asset.SetPriorityCount(0);

            var dataHash = new Hash128((uint)asset.splatCount, (uint)asset.formatVersion, 0, 0);
            byte[] chunk = CreateChunkData(splats, ref dataHash);
            byte[] pos = CreatePositionsData(splats, ref dataHash);
            byte[] other = CreateOtherData(splats, ref dataHash);
            byte[] color = CreateColorData(splats, ref dataHash);
            byte[] sh = CreateSHData(splats, ref dataHash);
            asset.SetDataHash(dataHash);

            asset.SetAssetFiles(Text(chunk, name + "_chk"), Text(pos, name + "_pos"), Text(other, name + "_oth"),
                                Text(color, name + "_col"), Text(sh, name + "_shs"));
            return asset;
        }

        static TextAsset Text(byte[] bytes, string name) => new TextAsset(new ReadOnlySpan<byte>(bytes)) { name = name };

        [BurstCompile]
        struct CalcBoundsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMin;
            [NativeDisableUnsafePtrRestriction] public unsafe float3* m_BoundsMax;
            [ReadOnly] public NativeArray<Splat> m_SplatData;

            public unsafe void Execute()
            {
                float3 boundsMin = float.PositiveInfinity;
                float3 boundsMax = float.NegativeInfinity;
                for (int i = 0; i < m_SplatData.Length; ++i)
                {
                    float3 p = m_SplatData[i].pos;
                    boundsMin = math.min(boundsMin, p);
                    boundsMax = math.max(boundsMax, p);
                }
                *m_BoundsMin = boundsMin;
                *m_BoundsMax = boundsMax;
            }
        }

        [BurstCompile]
        struct ReorderMortonJob : IJobParallelFor
        {
            const float kScaler = (float)((1 << 21) - 1);
            public float3 m_BoundsMin;
            public float3 m_InvBoundsSize;
            [ReadOnly] public NativeArray<Splat> m_SplatData;
            public NativeArray<(ulong, int)> m_Order;

            public void Execute(int index)
            {
                float3 p = ((float3)m_SplatData[index].pos - m_BoundsMin) * m_InvBoundsSize * kScaler;
                m_Order[index] = (GaussianUtils.MortonEncode3((uint3)p), index);
            }
        }

        struct OrderComparer : IComparer<(ulong, int)>
        {
            public int Compare((ulong, int) a, (ulong, int) b)
            {
                if (a.Item1 < b.Item1) return -1;
                if (a.Item1 > b.Item1) return +1;
                return a.Item2 - b.Item2;
            }
        }

        static void ReorderMorton(NativeArray<Splat> splats, float3 boundsMin, float3 boundsMax)
        {
            var order = new ReorderMortonJob
            {
                m_SplatData = splats,
                m_BoundsMin = boundsMin,
                m_InvBoundsSize = 1.0f / (boundsMax - boundsMin),
                m_Order = new NativeArray<(ulong, int)>(splats.Length, Allocator.TempJob),
            };
            order.Schedule(splats.Length, 4096).Complete();
            order.m_Order.Sort(new OrderComparer());

            var copy = new NativeArray<Splat>(splats, Allocator.TempJob);
            for (int i = 0; i < copy.Length; ++i)
                splats[i] = copy[order.m_Order[i].Item2];
            copy.Dispose();
            order.m_Order.Dispose();
        }

        [BurstCompile]
        struct CalcChunkDataJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Splat> splatData;
            public NativeArray<GaussianSplatAsset.ChunkInfo> chunks;

            public void Execute(int chunkIdx)
            {
                float3 minpos = float.PositiveInfinity, minscl = float.PositiveInfinity, minshs = float.PositiveInfinity;
                float4 mincol = float.PositiveInfinity;
                float3 maxpos = float.NegativeInfinity, maxscl = float.NegativeInfinity, maxshs = float.NegativeInfinity;
                float4 maxcol = float.NegativeInfinity;

                int begin = math.min(chunkIdx * GaussianSplatAsset.kChunkSize, splatData.Length);
                int end = math.min((chunkIdx + 1) * GaussianSplatAsset.kChunkSize, splatData.Length);

                for (int i = begin; i < end; ++i)
                {
                    Splat s = splatData[i];
                    s.scale = math.pow(s.scale, 1.0f / 8.0f);
                    s.opacity = GaussianUtils.SquareCentered01(s.opacity);
                    splatData[i] = s;

                    minpos = math.min(minpos, s.pos); maxpos = math.max(maxpos, s.pos);
                    minscl = math.min(minscl, s.scale); maxscl = math.max(maxscl, s.scale);
                    mincol = math.min(mincol, new float4(s.dc0, s.opacity)); maxcol = math.max(maxcol, new float4(s.dc0, s.opacity));
                    minshs = math.min(minshs, s.sh1); maxshs = math.max(maxshs, s.sh1);
                    minshs = math.min(minshs, s.sh2); maxshs = math.max(maxshs, s.sh2);
                    minshs = math.min(minshs, s.sh3); maxshs = math.max(maxshs, s.sh3);
                    minshs = math.min(minshs, s.sh4); maxshs = math.max(maxshs, s.sh4);
                    minshs = math.min(minshs, s.sh5); maxshs = math.max(maxshs, s.sh5);
                    minshs = math.min(minshs, s.sh6); maxshs = math.max(maxshs, s.sh6);
                    minshs = math.min(minshs, s.sh7); maxshs = math.max(maxshs, s.sh7);
                    minshs = math.min(minshs, s.sh8); maxshs = math.max(maxshs, s.sh8);
                    minshs = math.min(minshs, s.sh9); maxshs = math.max(maxshs, s.sh9);
                    minshs = math.min(minshs, s.shA); maxshs = math.max(maxshs, s.shA);
                    minshs = math.min(minshs, s.shB); maxshs = math.max(maxshs, s.shB);
                    minshs = math.min(minshs, s.shC); maxshs = math.max(maxshs, s.shC);
                    minshs = math.min(minshs, s.shD); maxshs = math.max(maxshs, s.shD);
                    minshs = math.min(minshs, s.shE); maxshs = math.max(maxshs, s.shE);
                    minshs = math.min(minshs, s.shF); maxshs = math.max(maxshs, s.shF);
                }

                maxpos = math.max(maxpos, minpos + 1.0e-5f);
                maxscl = math.max(maxscl, minscl + 1.0e-5f);
                maxcol = math.max(maxcol, mincol + 1.0e-5f);
                maxshs = math.max(maxshs, minshs + 1.0e-5f);

                GaussianSplatAsset.ChunkInfo info = default;
                info.posX = new float2(minpos.x, maxpos.x);
                info.posY = new float2(minpos.y, maxpos.y);
                info.posZ = new float2(minpos.z, maxpos.z);
                info.sclX = math.f32tof16(minscl.x) | (math.f32tof16(maxscl.x) << 16);
                info.sclY = math.f32tof16(minscl.y) | (math.f32tof16(maxscl.y) << 16);
                info.sclZ = math.f32tof16(minscl.z) | (math.f32tof16(maxscl.z) << 16);
                info.colR = math.f32tof16(mincol.x) | (math.f32tof16(maxcol.x) << 16);
                info.colG = math.f32tof16(mincol.y) | (math.f32tof16(maxcol.y) << 16);
                info.colB = math.f32tof16(mincol.z) | (math.f32tof16(maxcol.z) << 16);
                info.colA = math.f32tof16(mincol.w) | (math.f32tof16(maxcol.w) << 16);
                info.shR = math.f32tof16(minshs.x) | (math.f32tof16(maxshs.x) << 16);
                info.shG = math.f32tof16(minshs.y) | (math.f32tof16(maxshs.y) << 16);
                info.shB = math.f32tof16(minshs.z) | (math.f32tof16(maxshs.z) << 16);
                chunks[chunkIdx] = info;

                for (int i = begin; i < end; ++i)
                {
                    Splat s = splatData[i];
                    s.pos = ((float3)s.pos - minpos) / (maxpos - minpos);
                    s.scale = ((float3)s.scale - minscl) / (maxscl - minscl);
                    s.dc0 = ((float3)s.dc0 - mincol.xyz) / (maxcol.xyz - mincol.xyz);
                    s.opacity = (s.opacity - mincol.w) / (maxcol.w - mincol.w);
                    s.sh1 = ((float3)s.sh1 - minshs) / (maxshs - minshs);
                    s.sh2 = ((float3)s.sh2 - minshs) / (maxshs - minshs);
                    s.sh3 = ((float3)s.sh3 - minshs) / (maxshs - minshs);
                    s.sh4 = ((float3)s.sh4 - minshs) / (maxshs - minshs);
                    s.sh5 = ((float3)s.sh5 - minshs) / (maxshs - minshs);
                    s.sh6 = ((float3)s.sh6 - minshs) / (maxshs - minshs);
                    s.sh7 = ((float3)s.sh7 - minshs) / (maxshs - minshs);
                    s.sh8 = ((float3)s.sh8 - minshs) / (maxshs - minshs);
                    s.sh9 = ((float3)s.sh9 - minshs) / (maxshs - minshs);
                    s.shA = ((float3)s.shA - minshs) / (maxshs - minshs);
                    s.shB = ((float3)s.shB - minshs) / (maxshs - minshs);
                    s.shC = ((float3)s.shC - minshs) / (maxshs - minshs);
                    s.shD = ((float3)s.shD - minshs) / (maxshs - minshs);
                    s.shE = ((float3)s.shE - minshs) / (maxshs - minshs);
                    s.shF = ((float3)s.shF - minshs) / (maxshs - minshs);
                    splatData[i] = s;
                }
            }
        }

        static byte[] CreateChunkData(NativeArray<Splat> splats, ref Hash128 dataHash)
        {
            int chunkCount = (splats.Length + GaussianSplatAsset.kChunkSize - 1) / GaussianSplatAsset.kChunkSize;
            var job = new CalcChunkDataJob { splatData = splats, chunks = new NativeArray<GaussianSplatAsset.ChunkInfo>(chunkCount, Allocator.TempJob) };
            job.Schedule(chunkCount, 8).Complete();
            dataHash.Append(ref job.chunks);
            byte[] bytes = job.chunks.Reinterpret<byte>(UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()).ToArray();
            job.chunks.Dispose();
            return bytes;
        }

        static uint EncodeFloat3ToNorm11(float3 v) =>
            (uint)(v.x * 2047.5f) | ((uint)(v.y * 1023.5f) << 11) | ((uint)(v.z * 2047.5f) << 21);

        static ushort EncodeFloat3ToNorm565(float3 v) =>
            (ushort)((uint)(v.x * 31.5f) | ((uint)(v.y * 63.5f) << 5) | ((uint)(v.z * 31.5f) << 11));

        static uint EncodeQuatToNorm10(float4 v) =>
            (uint)(v.x * 1023.5f) | ((uint)(v.y * 1023.5f) << 10) | ((uint)(v.z * 1023.5f) << 20) | ((uint)(v.w * 3.5f) << 30);

        [BurstCompile]
        struct PositionsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Splat> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<uint> m_Output;
            public void Execute(int index) => m_Output[index] = EncodeFloat3ToNorm11(math.saturate((float3)m_Input[index].pos));
        }

        [BurstCompile]
        struct OtherJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Splat> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<uint> m_Output; // rotation, then Norm11 scale: 8 bytes a splat
            public void Execute(int index)
            {
                Quaternion q = m_Input[index].rot;
                m_Output[index * 2 + 0] = EncodeQuatToNorm10(new float4(q.x, q.y, q.z, q.w));
                m_Output[index * 2 + 1] = EncodeFloat3ToNorm11(math.saturate((float3)m_Input[index].scale));
            }
        }

        static int NextMultipleOf(int size, int multipleOf) => (size + multipleOf - 1) / multipleOf * multipleOf;

        static byte[] CreatePositionsData(NativeArray<Splat> splats, ref Hash128 dataHash)
        {
            int dataLen = NextMultipleOf(splats.Length * GaussianSplatAsset.GetVectorSize(kFormatPos), 8);
            var data = new NativeArray<uint>(dataLen / 4, Allocator.TempJob);
            new PositionsJob { m_Input = splats, m_Output = data }.Schedule(splats.Length, 8192).Complete();
            var bytes = data.Reinterpret<byte>(4);
            dataHash.Append(bytes);
            var result = bytes.ToArray();
            data.Dispose();
            return result;
        }

        static byte[] CreateOtherData(NativeArray<Splat> splats, ref Hash128 dataHash)
        {
            int formatSize = GaussianSplatAsset.GetOtherSizeNoSHIndex(kFormatScale); // 8: no SH index at Norm6
            int dataLen = NextMultipleOf(splats.Length * formatSize, 8);
            var data = new NativeArray<uint>(dataLen / 4, Allocator.TempJob);
            new OtherJob { m_Input = splats, m_Output = data }.Schedule(splats.Length, 8192).Complete();
            var bytes = data.Reinterpret<byte>(4);
            dataHash.Append(bytes);
            var result = bytes.ToArray();
            data.Dispose();
            return result;
        }

        static int SplatIndexToTextureIndex(uint idx)
        {
            uint2 xy = GaussianUtils.DecodeMorton2D_16x16(idx);
            uint width = GaussianSplatAsset.kTextureWidth / 16;
            idx >>= 8;
            uint x = (idx % width) * 16 + xy.x;
            uint y = (idx / width) * 16 + xy.y;
            return (int)(y * GaussianSplatAsset.kTextureWidth + x);
        }

        [BurstCompile]
        struct ColorJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Splat> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<float4> m_Output;
            public void Execute(int index)
            {
                var s = m_Input[index];
                m_Output[SplatIndexToTextureIndex((uint)index)] = new float4(s.dc0.x, s.dc0.y, s.dc0.z, s.opacity);
            }
        }

        [BurstCompile]
        struct ColorToNorm8Job : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<uint> m_Output;
            public void Execute(int index)
            {
                float4 pix = math.saturate(m_Input[index]);
                m_Output[index] = (uint)(pix.x * 255.5f) | ((uint)(pix.y * 255.5f) << 8) | ((uint)(pix.z * 255.5f) << 16) | ((uint)(pix.w * 255.5f) << 24);
            }
        }

        static byte[] CreateColorData(NativeArray<Splat> splats, ref Hash128 dataHash)
        {
            var (width, height) = GaussianSplatAsset.CalcTextureSize(splats.Length);
            var data = new NativeArray<float4>(width * height, Allocator.TempJob);
            new ColorJob { m_Input = splats, m_Output = data }.Schedule(splats.Length, 8192).Complete();
            dataHash.Append(data);
            dataHash.Append((int)kFormatColor);

            var enc = new NativeArray<uint>(width * height, Allocator.TempJob);
            new ColorToNorm8Job { m_Input = data, m_Output = enc }.Schedule(width * height, 8192).Complete();
            var result = enc.Reinterpret<byte>(4).ToArray();
            enc.Dispose();
            data.Dispose();
            return result;
        }

        [BurstCompile]
        struct SHJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Splat> m_Input;
            [NativeDisableParallelForRestriction] public NativeArray<GaussianSplatAsset.SHTableItemNorm6> m_Output;
            public void Execute(int index)
            {
                var s = m_Input[index];
                GaussianSplatAsset.SHTableItemNorm6 res;
                res.sh1 = EncodeFloat3ToNorm565(s.sh1); res.sh2 = EncodeFloat3ToNorm565(s.sh2); res.sh3 = EncodeFloat3ToNorm565(s.sh3);
                res.sh4 = EncodeFloat3ToNorm565(s.sh4); res.sh5 = EncodeFloat3ToNorm565(s.sh5); res.sh6 = EncodeFloat3ToNorm565(s.sh6);
                res.sh7 = EncodeFloat3ToNorm565(s.sh7); res.sh8 = EncodeFloat3ToNorm565(s.sh8); res.sh9 = EncodeFloat3ToNorm565(s.sh9);
                res.shA = EncodeFloat3ToNorm565(s.shA); res.shB = EncodeFloat3ToNorm565(s.shB); res.shC = EncodeFloat3ToNorm565(s.shC);
                res.shD = EncodeFloat3ToNorm565(s.shD); res.shE = EncodeFloat3ToNorm565(s.shE); res.shF = EncodeFloat3ToNorm565(s.shF);
                res.shPadding = default;
                m_Output[index] = res;
            }
        }

        static byte[] CreateSHData(NativeArray<Splat> splats, ref Hash128 dataHash)
        {
            int dataLen = (int)GaussianSplatAsset.CalcSHDataSize(splats.Length, kFormatSH);
            int itemSize = UnsafeUtility.SizeOf<GaussianSplatAsset.SHTableItemNorm6>();
            var data = new NativeArray<GaussianSplatAsset.SHTableItemNorm6>(dataLen / itemSize, Allocator.TempJob);
            new SHJob { m_Input = splats, m_Output = data }.Schedule(splats.Length, 8192).Complete();
            var bytes = data.Reinterpret<byte>(itemSize);
            dataHash.Append(bytes);
            var result = bytes.ToArray();
            data.Dispose();
            return result;
        }
    }
}
