using System;
using System.IO;
using System.Numerics;
using WallRestraint.Logic;

namespace WallRestraint.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;
        private static bool Near(Vector3 a, Vector3 b, float eps = 1e-4f) => Vector3.Distance(a, b) <= eps;

        private static int Main()
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "wallrestraint.wrm");
            if (File.Exists(modelPath)) { ModelTests(modelPath); PoseTests(modelPath); }
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing: export it with mods/WallRestraint/tools/export_model.py)");
            FormatErrorTests();
            FrameTests();
            SpotTests();

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void ModelTests(string path)
        {
            WallModel m;
            using (var s = File.OpenRead(path)) m = WallModel.Read(s);
            Check(m.Parts.Count == 10, "model: 10 parts (collar 2, arch 2, arm cuffs 2, leg tubes 4)");
            Check(m.Parts.TrueForAll(p => p.Positions.Length == p.Normals.Length && p.Indices.Length > 0), "model: every part has geometry");
            Check(m.Parts.Exists(p => p.Kind == PartKind.LegTube && p.Leather) && m.Parts.Exists(p => p.Kind == PartKind.LegTube && !p.Leather),
                  "model: leg tubes have a metal shell and a leather lining");
            // Everything hangs on the wall: nothing deeper than the leg tubes reach into it, nothing far out in the room.
            bool onWall = m.Parts.TrueForAll(p => Array.TrueForAll(p.Positions, v => v.X > -0.2f && v.X < 0.35f));
            Check(onWall, "model: all geometry within -0.2 .. 0.35 m of the wall surface");
            Check(Near(m.Arms[0].Center.Y, -m.Arms[1].Center.Y, 1e-3f) && Near(m.Legs[0].Mouth.Y, -m.Legs[1].Mouth.Y, 1e-3f), "model: sides mirror");
            Check(m.Arms[0].Center.Y < 0 && m.Legs[0].Mouth.Y < 0, "model: side 0 is y < 0");
            Check(m.CollarCenter.Z > m.ArchTop && m.ArchBottom > m.Legs[0].Mouth.Z, "model: collar above the back rest above the leg tubes");
            Check(m.CollarInnerRadius > 0.03f && m.CollarInnerRadius < 0.08f, "model: collar hole is neck sized (" + m.CollarInnerRadius + ")");
            Check(m.Arms[0].InnerRadius > 0.02f && m.Arms[0].InnerRadius < 0.06f, "model: arm cuff hole is arm sized (" + m.Arms[0].InnerRadius + ")");
            Check(m.Legs[0].InnerRadius > 0.04f && m.Legs[0].InnerRadius < m.Legs[0].OuterRadius, "model: leg tube inner < outer radius");
            Check(m.Legs[0].Mouth.X > 0f && m.Legs[0].Inside > 0f, "model: leg tubes stick out of the wall and reach into it");
            var fp = m.Footprint();
            Check(fp.HalfWidth > 0.25f && fp.HalfWidth < 0.4f && fp.Bottom > 0.8f && fp.Top < 2.3f, "model: footprint " + fp);
        }

        // Proportions measured in game [observed 2026-10-24, WallController.Bones]: the first save's character (about 1.5 m)
        // and the much smaller character of the "Amateur" save (neck 0.77 m above the feet).
        internal static BodyDims Adult => new BodyDims
        {
            Spine = 0.407f, HipHalf = 0.10f, HipDrop = 0.115f, Thigh = 0.318f, Shin = 0.432f, ShoulderHalf = 0.104f, ShoulderDrop = 0.034f,
            UpperArm = 0.263f, Forearm = 0.252f, BackDepth = 0.09f, NeckRadius = 0.05f, ArmRadius = 0.04f, KneeRadius = 0.05f,
        };

        internal static BodyDims Small => new BodyDims
        {
            Spine = 0.206f, HipHalf = 0.037f, HipDrop = 0.08f, Thigh = 0.218f, Shin = 0.232f, ShoulderHalf = 0.05f, ShoulderDrop = 0.02f,
            UpperArm = 0.179f, Forearm = 0.172f, BackDepth = 0.06f, NeckRadius = 0.035f, ArmRadius = 0.03f, KneeRadius = 0.04f,
        };

        private static void PoseTests(string path)
        {
            WallModel m;
            using (var s = File.OpenRead(path)) m = WallModel.Read(s);
            foreach (var (name, b) in new[] { ("adult", Adult), ("small", Small) })
            {
                var p = PoseParams.Default;
                var o = WallPose.Layout(m, b, p);
                Check(o.Problem == null, name + ": layout fits (" + o.Problem + ")");
                Check(Near(Vector3.Distance(o.KneeR, o.ThighR), b.Thigh, 1e-3f) && Near(Vector3.Distance(o.KneeL, o.ThighL), b.Thigh, 1e-3f),
                      name + ": thighs keep their length");
                Check(Near(Vector3.Distance(o.Hips, o.Neck), b.Spine, 1e-3f), name + ": spine keeps its length");
                Check(Near(Vector3.Distance(o.ShoulderR, o.ElbowR), b.UpperArm, 1e-3f) && Near(Vector3.Distance(o.ElbowR, o.HandR), b.Forearm, 1e-3f),
                      name + ": arms keep their length");
                Check(Near(o.LegAtR.Z, o.KneeR.Z + p.TubeLift * b.KneeRadius) && o.LegAtR.Z > o.KneeR.Z, name + ": leg tubes lifted over the calf");
                Check(Near(o.KneeR.Z, p.KneeHeight) && o.FootR.X < 0 && Near(o.FootR.Z, o.KneeR.Z), name + ": shins go level into the wall");
                Check(o.KneeR.Y > 0 && o.KneeL.Y < 0 && o.ShoulderR.Y > 0, name + ": her right is +y");
                Check(o.Neck.Z > o.Hips.Z && o.Hips.Z > o.KneeR.Z && o.KneeR.Z > o.FootR.Z - 1e-4f, name + ": neck above hips above knees");
                Check(Near(o.CollarAt.X, m.CollarCenter.X * o.CollarScale) && Near(o.CollarScale * m.CollarInnerRadius, b.NeckRadius + p.Clearance),
                      name + ": collar ring around the neck, its bracket reaching the wall");
                Check(Near(o.ArmAtR.X, m.Arms[0].Center.X * o.ArmScale) && o.ArmAtR.Z < o.ShoulderR.Z, name + ": arm cuffs below the shoulders, on their brackets");
                Check(o.ArchScale > 0.2f && Near(o.ArchScale * m.ArchApexOut, o.BackContact.X), name + ": back rest reaches her back (scale " + o.ArchScale + ")");
                Check(o.BackContact.Z > o.Hips.Z && o.BackContact.Z < o.Neck.Z, name + ": back rest between hips and neck");
                Check(o.LegAtR.X < o.KneeR.X && Near(o.LegScale * m.Legs[0].InnerRadius, b.KneeRadius + p.TubeClearance), name + ": leg tubes just below the knees");
                Check(Near(o.LegAtR.X, m.Legs[0].Mouth.X * o.LegScale) && o.LegAtR.X > 0.03f, name + ": leg tubes stick out of the wall as modelled");
            }
            foreach (var (name, b) in new[] { ("adult", Adult), ("small", Small) })
            {
                var p = PoseParams.Default;
                var span = WallPose.Span(m, b, p);
                p.KneeHeight = 0.7f;
                var fpA = WallPose.Footprint(m, WallPose.Layout(m, b, p));
                p.KneeHeight = 1.1f;
                var fpB = WallPose.Footprint(m, WallPose.Layout(m, b, p));
                Check(Near(fpB.Bottom - fpA.Bottom, 0.4f, 1e-4f) && Near(fpB.Top - fpA.Top, 0.4f, 1e-4f) && Near(fpA.HalfWidth, fpB.HalfWidth)
                      && Near(fpA.Bottom, 0.7f + span.Below, 1e-4f) && Near(fpA.Top, 0.7f + span.Above, 1e-4f),
                      name + ": the footprint moves 1:1 with the knee height (Span)");
                p.KneeHeight = WallPose.MinKneeHeight(m, b, p);
                var kneel = WallPose.Layout(m, b, p);
                float tubeBottom = kneel.LegAtR.Z - m.Legs[0].OuterRadius * kneel.LegScale;
                Check(kneel.Problem == null && tubeBottom >= 0.01f - 1e-4f && kneel.KneeR.Z >= b.KneeRadius + 0.01f - 1e-4f
                      && (Near(tubeBottom, 0.01f, 1e-4f) || Near(kneel.KneeR.Z, b.KneeRadius + 0.01f, 1e-4f)) && kneel.Hips.Z > kneel.KneeR.Z,
                      name + ": kneeling - tubes and knees clear the floor, one of them by just 1 cm (knees at " + p.KneeHeight.ToString("0.000") + " m)");
            }
            var shinBody = Adult;
            shinBody.ShinRadius = 0.04f;
            var shinLayout = WallPose.Layout(m, shinBody, PoseParams.Default);
            Check(Near(shinLayout.LegScale * m.Legs[0].InnerRadius, 0.04f + PoseParams.Default.TubeClearance) && shinLayout.KneeR.X > shinLayout.LegAtR.X + shinBody.KneeRadius,
                  "pose: a measured shin sizes the tube; the knee stays a knee radius and more outside its mouth");
            var wide = PoseParams.Default;
            wide.KneeSpread = 20f;
            Check(WallPose.Layout(m, Adult, wide).Problem != null, "pose: impossible knee spread reported");
        }

        private static void FormatErrorTests()
        {
            bool Rejects(byte[] data)
            {
                try { WallModel.Read(new MemoryStream(data)); return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            }
            Check(Rejects(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0 }), "format: wrong magic rejected");
            var bad = new MemoryStream();
            var w = new BinaryWriter(bad);
            w.Write(WallModel.Magic);
            w.Write(2u);
            Check(Rejects(bad.ToArray()), "format: unknown version rejected");
            var cut = new MemoryStream();
            w = new BinaryWriter(cut);
            w.Write(WallModel.Magic);
            w.Write(1u);
            w.Write(0f);
            Check(Rejects(cut.ToArray()), "format: truncated file rejected");
        }

        private static void FrameTests()
        {
            // A wall facing +z (Unity numbers), surface at (1, 0, 5), model scale 0.9.
            var f = new WallFrame(new Vector3(1, 0, 5), new Vector3(0, 0, 1), Vector3.UnitY, 0.9f);
            Check(Near(f.Point(Vector3.Zero), new Vector3(1, 0, 5)), "frame: model origin = wall point");
            Check(Near(f.Point(new Vector3(1, 0, 0)), new Vector3(1, 0, 5.9f)), "frame: model x = out of the wall, scaled");
            Check(Near(f.Point(new Vector3(0, 0, 2)), new Vector3(1, 1.8f, 5)), "frame: model z = up, scaled");
            Check(Near(f.Side, Vector3.Cross(Vector3.UnitY, new Vector3(0, 0, 1))), "frame: model y = up x normal");
            var basis = new Matrix4x4(f.Normal.X, f.Normal.Y, f.Normal.Z, 0, f.Side.X, f.Side.Y, f.Side.Z, 0, f.Up.X, f.Up.Y, f.Up.Z, 0, 0, 0, 0, 1);
            Check(Near(basis.GetDeterminant(), 1f), "frame: determinant +1 (winding kept)");
            var q = f.Rotation();
            Check(Near(Vector3.Transform(Vector3.UnitX, q), f.Normal) && Near(Vector3.Transform(Vector3.UnitY, q), f.Side)
                  && Near(Vector3.Transform(Vector3.UnitZ, q), f.Up), "frame: rotation maps the model axes");
            var p = new Vector3(0.3f, -0.2f, 1.1f);
            Check(Near(f.ToModel(f.Point(p)), p), "frame: ToModel inverts Point");
            var tilted = new WallFrame(Vector3.Zero, Vector3.Normalize(new Vector3(1, 0.2f, 0)), Vector3.UnitY, 1f);
            Check(Near(tilted.Normal, Vector3.UnitX) && Near(Vector3.Dot(tilted.Normal, tilted.Up), 0f), "frame: normal kept level");
        }

        private static void SpotTests()
        {
            const int w = 8, h = 18;
            const float cx = 0.08f, cy = 0.09f;
            float[] Grid(Func<float, float, float> depth)
            {
                var g = new float[w * h];
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                        g[j * w + i] = depth((i - (w - 1) / 2f) * cx, (j - (h - 1) / 2f) * cy);
                return g;
            }
            var flat = WallSpot.Judge(Grid((x, y) => 0.17f), w, h, cx, cy, 0.015f, 0.09f);
            Check(flat.Ok && Near(flat.Offset, 0.17f) && Near(flat.Bump, 0f), "spot: flat wall 17 cm behind the collider accepted, offset 17 cm");
            var slope = WallSpot.Judge(Grid((x, y) => 0.05f + 0.05f * x), w, h, cx, cy, 0.015f, 0.09f);
            Check(slope.Ok && Near(slope.SlopeX, 0.05f) && Near(slope.Offset, 0.05f), "spot: slightly turned wall accepted, slope found");
            var turned = WallSpot.Judge(Grid((x, y) => 0.3f * x), w, h, cx, cy, 0.015f, 0.09f);
            Check(!turned.Ok && turned.Reason.Contains("straight"), "spot: strongly turned wall rejected");
            var bump = Grid((x, y) => 0f);
            bump[5 * w + 3] = -0.06f; // a picture frame / pipe 6 cm out
            var bumpy = WallSpot.Judge(bump, w, h, cx, cy, 0.015f, 0.09f);
            Check(!bumpy.Ok && bumpy.Reason.Contains("flat"), "spot: 6 cm bump rejected");
            var hole = Grid((x, y) => 0f);
            hole[10 * w + 2] = float.NaN;
            var holey = WallSpot.Judge(hole, w, h, cx, cy, 0.015f, 0.09f);
            Check(!holey.Ok && holey.Empty == 1 && holey.Reason.Contains("gaps"), "spot: a gap rejected");
            var empty = WallSpot.Judge(Grid((x, y) => float.NaN), w, h, cx, cy, 0.015f, 0.09f);
            Check(!empty.Ok && empty.Reason.Contains("invisible"), "spot: nothing drawn (invisible blocker) rejected");

            // Heights: one tall probe, 16 x 64 rows of 4 cm from the floor (0 .. 2.56 m); footprint = knee - 0.10 .. knee + 0.85.
            const int tw = 16, th = 64;
            const float tcy = 0.04f, below = -0.10f, above = 0.85f;
            float[] Wall(Func<float, float> depthAtHeight)
            {
                var g = new float[tw * th];
                for (int j = 0; j < th; j++)
                    for (int i = 0; i < tw; i++)
                        g[j * tw + i] = depthAtHeight((j + 0.5f) * tcy);
                return g;
            }
            WallSpot.HeightFit H(float[] g, float preferred) =>
                WallSpot.Heights(g, tw, th, 0.05f, tcy, 0f, below, above, 0.11f, 1.3f, 0.01f, preferred, 0.015f, 0.09f);
            var fence = H(Wall(y => y < 1.6f ? 0f : float.NaN), 1.04f); // a 1.6 m fence: nothing drawn above it
            Check(fence.Ok && fence.Knee < 1.04f && fence.Knee + above <= 1.6f + 1e-3f && fence.Knee + above > 1.6f - 0.06f && Near(fence.High, fence.Knee) && Near(fence.Low, 0.11f),
                  "heights: low fence - as high as fits under its top (" + fence.Knee.ToString("0.00") + "), down to kneeling");
            var tall = H(Wall(y => 0f), 1.04f);
            Check(tall.Ok && Near(tall.Knee, 1.04f, 1e-3f) && tall.High > 1.2f, "heights: tall wall - the preferred height, + can go higher");
            var plinth = H(Wall(y => y < 0.3f ? -0.05f : 0f), 0.11f); // a plinth 5 cm out up to 30 cm
            Check(plinth.Ok && plinth.Knee + below >= 0.3f - 0.04f && Near(plinth.Low, plinth.Knee), "heights: plinth at the bottom - kneeling refused, the lowest height above it (" + plinth.Knee.ToString("0.00") + ")");
            var blocker = H(Wall(y => float.NaN), 1.04f);
            Check(!blocker.Ok && blocker.Reason != null, "heights: invisible wall refused (" + blocker.Reason + ")");
        }
    }
}
