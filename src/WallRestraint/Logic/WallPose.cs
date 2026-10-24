using System;
using System.Numerics;

namespace WallRestraint.Logic
{
    /// <summary>Her proportions (metres), measured from the bones once per lock; lengths do not depend on the pose.</summary>
    public struct BodyDims
    {
        public float Spine;        // hips bone -> neck bone
        public float HipHalf;      // hips centre -> thigh joint, sideways
        public float HipDrop;      // hips bone above the thigh joints
        public float Thigh, Shin;
        public float ShoulderHalf; // neck -> upper arm joint, sideways
        public float ShoulderDrop; // upper arm joint below the neck bone
        public float UpperArm, Forearm;
        public float BackDepth;    // chest bone -> skin of the back
        public float NeckRadius, ArmRadius, KneeRadius;
    }

    /// <summary>Tunable shape of the pose (config).</summary>
    public struct PoseParams
    {
        public float KneeHeightRatio;  // knee height above the floor, in shin lengths
        public float KneeSpread;       // knee centre sideways from the middle, in hip half widths
        public float ThighLean;        // thighs lean out of the wall (radians from vertical)
        public float CuffOnArm;        // arm cuff along the upper arm (0 shoulder .. 1 elbow)
        public float Clearance;        // gap between limb and restraint (m)

        public static PoseParams Default => new PoseParams
        {
            KneeHeightRatio = 2.4f, KneeSpread = 2.2f, ThighLean = 0.35f, CuffOnArm = 0.8f, Clearance = 0.004f,
        };
    }

    /// <summary>Where her joints go and where each restraint goes, in wall coordinates (x out of the wall, y = her right, z up from the floor).</summary>
    public sealed class PoseLayout
    {
        public Vector3 Hips, Neck, Head;
        public Vector3 ThighR, ThighL, KneeR, KneeL, FootR, FootL;
        public Vector3 ShoulderR, ShoulderL, ElbowR, ElbowL, HandR, HandL;
        public Vector3 BackContact;            // the point of her back that lies on the rest
        // Restraints: anchor position (model anchor -> here) and uniform scale per part.
        public Vector3 CollarAt; public float CollarScale;
        public Vector3 ArchAt; public float ArchScale;  // the rest's base on the wall, middle height
        public Vector3 ArmAtR, ArmAtL; public float ArmScale;
        public Vector3 LegAtR, LegAtL; public float LegScale;
        public string Problem;                 // null when everything fits
    }

    public static class WallPose
    {
        /// <summary>
        /// The pose built from the knees up: knees in front of the wall at a height set by the shin, shins straight into the
        /// wall; thighs up to the hip joints, leaning out by <see cref="PoseParams.ThighLean"/>; the spine from the hips back to
        /// the neck, which sits where the collar's bracket reaches; upper arms down and back to the arm cuffs. Each restraint is
        /// scaled to the limb it holds (radius + clearance over the model's hole), the rest so it touches her back.
        /// </summary>
        public static PoseLayout Layout(WallModel m, in BodyDims b, in PoseParams p)
        {
            var o = new PoseLayout();
            // Restraint scales from the limbs.
            o.CollarScale = (b.NeckRadius + p.Clearance) / m.CollarInnerRadius;
            o.ArmScale = (b.ArmRadius + p.Clearance) / m.Arms[0].InnerRadius;
            o.LegScale = (b.KneeRadius + p.Clearance) / m.Legs[0].InnerRadius;

            // Legs: the tubes stand out of the wall as modelled (scaled); the knees just outside their mouths, the shins level
            // through them into the wall.
            float kz = p.KneeHeightRatio * b.Shin;
            float ky = p.KneeSpread * b.HipHalf;
            float mouthX = m.Legs[0].Mouth.X * o.LegScale;
            float kneeX = mouthX + 0.8f * b.KneeRadius;
            o.KneeR = new Vector3(kneeX, ky, kz);
            o.KneeL = new Vector3(kneeX, -ky, kz);
            o.FootR = o.KneeR - new Vector3(b.Shin, 0, 0);
            o.FootL = o.KneeL - new Vector3(b.Shin, 0, 0);
            // Thigh joints: sideways at HipHalf, leaning out by ThighLean, length Thigh.
            float dy = b.HipHalf - ky;
            float dx = b.Thigh * (float)Math.Sin(p.ThighLean);
            float dz2 = b.Thigh * b.Thigh - dx * dx - dy * dy;
            if (dz2 <= 0) { o.Problem = "knees too far apart for the thighs"; dz2 = 1e-4f; }
            float dz = (float)Math.Sqrt(dz2);
            o.ThighR = o.KneeR + new Vector3(dx, dy, dz);
            o.ThighL = o.KneeL + new Vector3(dx, -dy, dz);
            o.Hips = (o.ThighR + o.ThighL) / 2 + new Vector3(0, 0, b.HipDrop);

            // Neck: on the collar ring, whose bracket reaches the wall at the model's distance (scaled).
            float neckX = m.CollarCenter.X * o.CollarScale;
            float rise2 = b.Spine * b.Spine - (o.Hips.X - neckX) * (o.Hips.X - neckX);
            if (rise2 <= 0) { o.Problem = "the neck cannot reach the collar"; rise2 = 1e-4f; }
            o.Neck = new Vector3(neckX, 0, o.Hips.Z + (float)Math.Sqrt(rise2));
            Vector3 spineDir = Vector3.Normalize(o.Neck - o.Hips);
            o.Head = o.Neck + spineDir * 0.05f;
            o.CollarAt = o.Neck;

            // Back rest: under the upper back (60 % up the spine), touching the skin of the back.
            Vector3 chest = o.Hips + (o.Neck - o.Hips) * 0.6f;
            Vector3 backDir = Vector3.Normalize(new Vector3(-spineDir.Z, 0, spineDir.X)); // spine turned 90 deg towards the wall
            if (backDir.X > 0) backDir = -backDir;
            o.BackContact = chest + backDir * b.BackDepth;
            o.ArchScale = o.BackContact.X / m.ArchApexOut;
            if (o.ArchScale < 0.05f) { o.Problem = "her back is against the wall, no room for the rest"; o.ArchScale = 0.05f; }
            o.ArchAt = new Vector3(0, 0, o.BackContact.Z);

            // Arms: shoulders below and beside the neck; the cuff holds the upper arm at CuffOnArm of its length, where the
            // cuff's ring sits on its bracket (model distance from the wall, scaled).
            o.ShoulderR = o.Neck + new Vector3(0, b.ShoulderHalf, -b.ShoulderDrop);
            o.ShoulderL = o.Neck + new Vector3(0, -b.ShoulderHalf, -b.ShoulderDrop);
            float cuffX = m.Arms[0].Center.X * o.ArmScale;
            float along = p.CuffOnArm * b.UpperArm;
            float ax = cuffX - o.ShoulderR.X;
            float az2 = along * along - ax * ax;
            if (az2 <= 0) { o.Problem = "the arms cannot reach the arm cuffs"; az2 = 1e-4f; }
            Vector3 armDir = Vector3.Normalize(new Vector3(ax, 0, -(float)Math.Sqrt(az2)));
            o.ArmAtR = o.ShoulderR + armDir * along;
            o.ArmAtL = o.ShoulderL + armDir * along;
            o.ElbowR = o.ShoulderR + armDir * b.UpperArm;
            o.ElbowL = o.ShoulderL + armDir * b.UpperArm;
            // Forearms hang straight down.
            o.HandR = o.ElbowR - new Vector3(0, 0, b.Forearm);
            o.HandL = o.ElbowL - new Vector3(0, 0, b.Forearm);

            o.LegAtR = new Vector3(mouthX, ky, kz);
            o.LegAtL = new Vector3(mouthX, -ky, kz);
            return o;
        }

        /// <summary>
        /// The wall area the restraints cover (half width, bottom, top; m) - the patch that must be flat and free - with the
        /// restraints' outer sizes from the model (scaled).
        /// </summary>
        public static (float HalfWidth, float Bottom, float Top) Footprint(WallModel m, PoseLayout o)
        {
            float legOuter = m.Legs[0].OuterRadius * o.LegScale;
            float armOuter = (m.Arms[0].InnerRadius + 0.02f) * o.ArmScale;
            float half = Math.Max(Math.Abs(o.LegAtR.Y) + legOuter, Math.Max(Math.Abs(o.ArmAtR.Y) + armOuter, Math.Abs(o.ShoulderR.Y)));
            float bottom = o.LegAtR.Z - legOuter;
            float top = Math.Max(o.Neck.Z, o.CollarAt.Z) + 0.02f * o.CollarScale;
            return (half, bottom, top);
        }
    }
}
