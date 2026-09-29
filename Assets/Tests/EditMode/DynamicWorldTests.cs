using System;
using System.IO;
using System.IO.Compression;
using GaussianSplatting.Runtime;
using MuseXR.DynamicWorlds;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace MusePico.Tests
{
    public class MarbleWireTests
    {
        [Test]
        public void DepthToRgbSendsThePngInlineWithItsRange()
        {
            string body = MarbleWire.DepthToRgbBody("QUJD", 1.6f, 21.5f, "rainbows");
            StringAssert.Contains("\"depth_pano_image\":{\"source\":\"data_base64\",\"data_base64\":\"QUJD\",\"extension\":\"png\"}", body);
            StringAssert.Contains("\"z_min\":1.6", body);
            StringAssert.Contains("\"z_max\":21.5", body);
            StringAssert.Contains("\"text_prompt\":\"rainbows\"", body);
        }

        [Test]
        public void PanoWorldIsPrivateAPanoramaAndNamesItsModel()
        {
            string body = MarbleWire.PanoWorldBody("UE5H", "marble-1.1", new string('x', 80));
            StringAssert.Contains("\"model\":\"marble-1.1\"", body);
            StringAssert.Contains("\"type\":\"image\"", body);
            StringAssert.Contains("\"is_pano\":true", body);
            StringAssert.Contains("\"public\":false", body);
            StringAssert.Contains("\"display_name\":\"" + new string('x', 64) + "\"", body);
        }

        // The real shape of a finished depth_to_rgb operation, 29 Sep 2026 (trimmed).
        const string PanoDone = "{\"operation_id\":\"ea405bb1\",\"done\":true,\"error\":null," +
            "\"metadata\":{\"progress\":{\"status\":\"SUCCEEDED\"}},\"response\":{\"world_id\":\"\"," +
            "\"assets\":{\"imagery\":{\"pano_url\":\"https://cdn.marble.worldlabs.ai/p.png\"},\"splats\":{\"spz_urls\":null}}}," +
            "\"cost\":{\"total_credits\":80}}";

        [Test]
        public void ThePanoramaUrlIsFoundWhereTheApiReallyPutsIt()
        {
            Assert.AreEqual("https://cdn.marble.worldlabs.ai/p.png", MarbleWire.PanoUrl(PanoDone));
            var op = MarbleWire.ParseOperation(PanoDone);
            Assert.IsTrue(op.done);
            Assert.AreEqual(80f, op.cost.total_credits);
        }

        [Test]
        public void ThePanoramaUrlIsAlsoFoundWhereTheSpecPutsIt() =>
            Assert.AreEqual("https://x/p.png", MarbleWire.PanoUrl("{\"done\":true,\"response\":{\"pano_url\":\"https://x/p.png\"}}"));

        [Test]
        public void AFinishedWorldGivesItsIdFromEitherField()
        {
            Assert.AreEqual("w1", MarbleWire.WorldId(MarbleWire.ParseOperation("{\"done\":true,\"response\":{\"world_id\":\"w1\"}}")));
            Assert.AreEqual("w2", MarbleWire.WorldId(MarbleWire.ParseOperation("{\"done\":true,\"response\":{\"id\":\"w2\"}}")));
            Assert.AreEqual("w3", MarbleWire.WorldId(MarbleWire.ParseOperation("{\"done\":true,\"metadata\":{\"world_id\":\"w3\"}}")));
        }

        const string WorldRecord = "{\"world\":{\"world_id\":\"2fa3\",\"assets\":{\"splats\":{\"spz_urls\":" +
            "{\"100k\":\"https://c/100k.spz\",\"500k\":\"https://c/500k.spz\",\"full_res\":\"https://c/full.spz\"}," +
            "\"semantics_metadata\":{\"metric_scale_factor\":0.7723,\"ground_plane_offset\":0.4529}}}}}";

        [Test]
        public void TheTierAndTheMetricDataComeOutOfTheWorldRecord()
        {
            Assert.AreEqual("https://c/500k.spz", MarbleWire.SpzUrl(WorldRecord, "500k"));
            Assert.AreEqual("https://c/100k.spz", MarbleWire.SpzUrl(WorldRecord, "100k"));
            var s = MarbleWire.ParseSemantics(WorldRecord);
            Assert.AreEqual(0.7723f, s.metric_scale_factor, 1e-6f);
            Assert.AreEqual(0.4529f, s.ground_plane_offset, 1e-6f);
        }

        [Test]
        public void ADraftWorldHasNoMetricData() =>
            Assert.IsNull(MarbleWire.ParseSemantics("{\"world\":{\"assets\":{\"splats\":{\"spz_urls\":{\"500k\":\"u\"}}}}}"));
    }

    public class DynamicWorldPlacementTests
    {
        [Test]
        public void TheFloorIsTheDenseBandAtTheHighEndOfRawY()
        {
            // 60% scattered through the room (raw Y -1.4 .. 0.6), 40% on a floor at raw Y 0.586.
            var rng = new System.Random(1);
            var y = new float[10000];
            for (int i = 0; i < y.Length; i++)
                y[i] = i % 5 < 2 ? 0.586f + (float)(rng.NextDouble() - 0.5) * 0.004f : -1.4f + (float)rng.NextDouble() * 2.0f;
            Assert.AreEqual(0.586f, DynamicWorldPlacement.FloorRawY(y), 0.01f);
        }

        [Test]
        public void MarblesGroundDataAndTheFloorBandAgreeOnTheMeasuredWorld()
        {
            // marble-1.1 on the DepthRoom pano: metric 0.7723, ground 0.4529 -> raw floor 0.5865.
            float fromSemantics = DynamicWorldPlacement.LiftFromSemantics(0.4529f, 0.7723f);
            float fromFloor = DynamicWorldPlacement.LiftForFloor(0.5865f);
            Assert.AreEqual(fromSemantics, fromFloor, 0.01f);
            Assert.AreEqual(1.70f, fromSemantics, 0.02f);   // the lift the Editor fit found by hand
        }

        [Test]
        public void TheRotationTurnsYDownIntoYUpAndKeepsForward()
        {
            Vector3 p = DynamicWorldPlacement.Rotation * new Vector3(1f, 2f, 3f);
            Assert.AreEqual(-1f, p.x, 1e-5f);
            Assert.AreEqual(-2f, p.y, 1e-5f);
            Assert.AreEqual(3f, p.z, 1e-5f);
        }
    }

    public class StepClockTests
    {
        [Test]
        public void EachStepIsTimedFromItsOwnStartAndTheSummaryListsThemAll()
        {
            var c = new StepClock(0);
            StringAssert.Contains("t=15.00 BEGIN paint", c.Begin("paint", 15));
            StringAssert.Contains("END   paint  19.50 s  80 credits", c.End(34.5, "80 credits"));
            c.Begin("world", 40);
            c.End(340);
            string s = c.Summary(345);
            StringAssert.Contains("total 345.00 s", s);
            StringAssert.Contains("paint", s);
            StringAssert.Contains("300.00 s", s);
            Assert.AreEqual(2, c.Steps.Count);
        }
    }

    public class RuntimeSplatAssetBuilderTests
    {
        static byte[] Spz(Vector3[] positions, int fractBits = 12)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
            using (var w = new BinaryWriter(gz))
            {
                int n = positions.Length;
                w.Write(0x5053474eu); w.Write(2u); w.Write((uint)n);
                w.Write((byte)0); w.Write((byte)fractBits); w.Write((byte)0); w.Write((byte)0);
                foreach (var p in positions)
                    foreach (float f in new[] { p.x, p.y, p.z })
                    {
                        int v = Mathf.RoundToInt(f * (1 << fractBits));
                        w.Write((byte)(v & 0xFF)); w.Write((byte)((v >> 8) & 0xFF)); w.Write((byte)((v >> 16) & 0xFF));
                    }
                for (int i = 0; i < n; i++) w.Write((byte)200);                 // alpha
                for (int i = 0; i < n * 3; i++) w.Write((byte)128);             // colour
                for (int i = 0; i < n * 3; i++) w.Write((byte)100);             // log scale
                for (int i = 0; i < n * 3; i++) w.Write((byte)128);             // rotation
            }
            return ms.ToArray();
        }

        [Test]
        public void DecodingRecoversPositionsIncludingNegativeOnes()
        {
            var pts = new[] { new Vector3(1.5f, -0.586f, 3f), new Vector3(-2.25f, 0.5f, -4f) };
            using var s = RuntimeSplatAssetBuilder.DecodeSpz(Spz(pts));
            Assert.AreEqual(2, s.Length);
            Assert.Less((s[0].pos - pts[0]).magnitude, 1e-3f);
            Assert.Less((s[1].pos - pts[1]).magnitude, 1e-3f);
            Assert.AreEqual(200 / 255f, s[0].opacity, 1e-6f);
        }

        [Test]
        public void ABuiltAssetHasItsBoundsAndEveryDataBlock()
        {
            var pts = new Vector3[1000];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(i % 10, i / 100, (i / 10) % 10) * 0.1f;
            using var s = RuntimeSplatAssetBuilder.DecodeSpz(Spz(pts));
            var asset = RuntimeSplatAssetBuilder.Build(s, "test");
            Assert.AreEqual(1000, asset.splatCount);
            Assert.AreEqual(Vector3.zero, asset.boundsMin);
            Assert.Less((asset.boundsMax - new Vector3(0.9f, 0.9f, 0.9f)).magnitude, 1e-3f);
            Assert.IsNotNull(asset.chunkData);
            Assert.IsNotNull(asset.posData);
            Assert.IsNotNull(asset.otherData);
            Assert.IsNotNull(asset.colorData);
            Assert.IsNotNull(asset.shData);
        }

        /// <summary>
        /// The check that the headset path is the Editor path: the generated DepthRoom world converted
        /// both ways must give identical bytes. Both files are git-ignored, so on a fresh clone this
        /// is skipped rather than failed.
        /// </summary>
        [Test]
        public void TheHeadsetConverterWritesTheSameBytesAsTheEditorConverter()
        {
            const string spzPath = "Tools/marble/worlds-out/2fa37677-9a02-4173-a6d9-960da154b6eb/depthroom-uncertainty-500k.spz";
            const string assetPath = "Assets/Worlds/Generated/depthroom-uncertainty-500k.asset";
            var editor = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
            if (!File.Exists(spzPath) || editor == null) Assert.Ignore("generated world not on this machine");

            using var s = RuntimeSplatAssetBuilder.DecodeSpz(File.ReadAllBytes(spzPath));
            var runtime = RuntimeSplatAssetBuilder.Build(s, "runtime");

            Assert.AreEqual(editor.splatCount, runtime.splatCount);
            Assert.AreEqual(editor.boundsMin, runtime.boundsMin);
            Assert.AreEqual(editor.boundsMax, runtime.boundsMax);
            // Splat order, chunk ranges and SH must be identical. The float encodes are allowed one
            // rounding step on a few values: two separately Burst-compiled copies of the same maths
            // round a handful of .5 boundaries differently (measured: 0.14% of positions and 0.12%
            // of colours, each by exactly 1 step; 1 of 1,000,000 rotation/scale words). A wrong
            // format, order or scale would move every value, and fails this.
            Close("chunks", editor.chunkData, runtime.chunkData, maxStep: 0, maxFraction: 0);
            Close("sh", editor.shData, runtime.shData, maxStep: 0, maxFraction: 0);
            Close("positions", editor.posData, runtime.posData, maxStep: 1, maxFraction: 0.005, norm11: true);
            Close("colour", editor.colorData, runtime.colorData, maxStep: 1, maxFraction: 0.005);
            Close("other", editor.otherData, runtime.otherData, maxStep: int.MaxValue, maxFraction: 0.0001);
        }

        /// <summary>Compares 32-bit words: how many differ, and by how many steps per packed field.</summary>
        static void Close(string what, TextAsset a, TextAsset b, int maxStep, double maxFraction, bool norm11 = false)
        {
            var x = a.GetData<uint>();
            var y = b.GetData<uint>();
            Assert.AreEqual(x.Length, y.Length, what + " length");
            int diff = 0, worst = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] == y[i]) continue;
                diff++;
                int step = norm11
                    ? Mathf.Max(Mathf.Abs((int)(x[i] & 2047) - (int)(y[i] & 2047)),
                                Mathf.Abs((int)((x[i] >> 11) & 1023) - (int)((y[i] >> 11) & 1023)),
                                Mathf.Abs((int)(x[i] >> 21) - (int)(y[i] >> 21)))
                    : Mathf.Max(Mathf.Abs((int)(x[i] & 255) - (int)(y[i] & 255)),
                                Mathf.Abs((int)((x[i] >> 8) & 255) - (int)((y[i] >> 8) & 255)),
                                Mathf.Abs((int)((x[i] >> 16) & 255) - (int)((y[i] >> 16) & 255)),
                                Mathf.Abs((int)(x[i] >> 24) - (int)(y[i] >> 24)));
                worst = Mathf.Max(worst, step);
            }
            Assert.LessOrEqual(diff, maxFraction * x.Length, $"{what}: {diff} of {x.Length} values differ");
            Assert.LessOrEqual(worst, maxStep, $"{what}: a value is off by {worst} steps");
        }
    }
}
