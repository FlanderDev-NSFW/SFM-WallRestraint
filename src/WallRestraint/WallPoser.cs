using System;
using UnityEngine;
using WallRestraint.Logic;
using NVector3 = System.Numerics.Vector3;

namespace WallRestraint
{
    /// <summary>
    /// Measures her body from the bones and holds her in the <see cref="PoseLayout"/>: every <c>LateUpdate</c> (after the
    /// animator, before skinning) the hips are put in place and each bone is turned so its child points at its target joint,
    /// from the hips outwards. Only directions are set, so the avatar keeps its own bone lengths.
    /// </summary>
    internal static class WallPoser
    {
        /// <summary>Lengths straight from the bones (pose independent); limb radii and back depth estimated from them.</summary>
        public static bool Measure(Animator anim, out BodyDims b, out string problem)
        {
            b = default;
            problem = null;
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), neck = B(HumanBodyBones.Neck);
            Transform ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform llL = B(HumanBodyBones.LeftLowerLeg), llR = B(HumanBodyBones.RightLowerLeg);
            Transform ftL = B(HumanBodyBones.LeftFoot), ftR = B(HumanBodyBones.RightFoot);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);
            if (hips == null || neck == null || ulL == null || ulR == null || llL == null || llR == null || ftL == null || ftR == null
                || uaL == null || uaR == null || laL == null || laR == null || hdL == null || hdR == null)
            {
                problem = "her skeleton is missing a bone";
                return false;
            }
            float D(Transform a, Transform c) => Vector3.Distance(a.position, c.position);
            b.Spine = D(hips, neck);
            b.HipHalf = D(ulL, ulR) / 2f;
            b.HipDrop = Vector3.Distance(hips.position, (ulL.position + ulR.position) / 2f);
            b.Thigh = (D(ulL, llL) + D(ulR, llR)) / 2f;
            b.Shin = (D(llL, ftL) + D(llR, ftR)) / 2f;
            b.ShoulderHalf = D(uaL, uaR) / 2f;
            b.ShoulderDrop = Vector3.Distance(neck.position, (uaL.position + uaR.position) / 2f) * 0.5f;
            b.UpperArm = (D(uaL, laL) + D(uaR, laR)) / 2f;
            b.Forearm = (D(laL, hdL) + D(laR, hdR)) / 2f;
            // Estimates until the limbs are measured from the mesh (ratios of the 1.5 m character of the first save).
            b.BackDepth = 0.22f * b.Spine;
            b.NeckRadius = 0.12f * b.Spine;
            b.ArmRadius = 0.12f * b.UpperArm;
            b.KneeRadius = 0.15f * b.Thigh;
            if (b.Spine < 0.05f || b.Thigh < 0.05f || b.Shin < 0.05f || b.UpperArm < 0.05f)
            {
                problem = "her proportions could not be measured";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Aiming bones leaves the joints a few cm from their targets (the avatar's own offsets), so the restraints follow the
        /// posed bones: height and sideways from the neck, the upper arms and the knees; the distance from the wall stays the
        /// layout's (the brackets must reach it).
        /// </summary>
        public static void Follow(Animator anim, WallFrame f, PoseLayout o, float cuffOnArm)
        {
            NVector3 M(Vector3 world) => f.ToModel(WallPlacer.N(world));
            NVector3 Keep(NVector3 at, Vector3 world) { var m = M(world); return new NVector3(at.X, m.Y, m.Z); }
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            o.CollarAt = Keep(o.CollarAt, B(HumanBodyBones.Neck).position);
            Vector3 ArmPoint(HumanBodyBones upper, HumanBodyBones lower) => Vector3.Lerp(B(upper).position, B(lower).position, cuffOnArm);
            o.ArmAtL = Keep(o.ArmAtL, ArmPoint(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm));
            o.ArmAtR = Keep(o.ArmAtR, ArmPoint(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm));
            o.LegAtL = Keep(o.LegAtL, B(HumanBodyBones.LeftLowerLeg).position);
            o.LegAtR = Keep(o.LegAtR, B(HumanBodyBones.RightLowerLeg).position);
        }

        /// <summary>Turn <paramref name="bone"/> so that <paramref name="child"/> lies in the direction of <paramref name="target"/>.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>The rotation taking direction a1 to b1 exactly and a2 as near to b2 as possible.</summary>
        private static Quaternion Align(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
        {
            return Quaternion.LookRotation(b1, Vector3.Cross(b1, b2)) * Quaternion.Inverse(Quaternion.LookRotation(a1, Vector3.Cross(a1, a2)));
        }

        public static void Apply(Animator anim, WallFrame f, PoseLayout o, float archBulge)
        {
            Vector3 W(NVector3 p) => WallPlacer.U(f.Point(p));
            Transform B(HumanBodyBones bone) => anim.GetBoneTransform(bone);
            Transform hips = B(HumanBodyBones.Hips), spine = B(HumanBodyBones.Spine), chest = B(HumanBodyBones.Chest);
            Transform neck = B(HumanBodyBones.Neck), head = B(HumanBodyBones.Head);
            Transform ulL = B(HumanBodyBones.LeftUpperLeg), ulR = B(HumanBodyBones.RightUpperLeg);
            Transform llL = B(HumanBodyBones.LeftLowerLeg), llR = B(HumanBodyBones.RightLowerLeg);
            Transform ftL = B(HumanBodyBones.LeftFoot), ftR = B(HumanBodyBones.RightFoot);
            Transform uaL = B(HumanBodyBones.LeftUpperArm), uaR = B(HumanBodyBones.RightUpperArm);
            Transform laL = B(HumanBodyBones.LeftLowerArm), laR = B(HumanBodyBones.RightLowerArm);
            Transform hdL = B(HumanBodyBones.LeftHand), hdR = B(HumanBodyBones.RightHand);

            // Hips: spine direction exactly, the thigh joints' line as near as possible; then moved into place.
            Vector3 spineNow = neck.position - hips.position, legsNow = ulR.position - ulL.position;
            Vector3 spineWant = W(o.Neck) - W(o.Hips), legsWant = W(o.ThighR) - W(o.ThighL);
            hips.rotation = Align(spineNow, legsNow, spineWant, legsWant) * hips.rotation;
            hips.position = W(o.Hips);

            // Spine arched over the rest: the chest pushed out of the wall a little.
            NVector3 chestAt = o.Hips + (o.Neck - o.Hips) * 0.55f + new NVector3(archBulge, 0, 0);
            if (chest != null)
            {
                Aim(spine, chest, W(chestAt));
                Aim(chest, neck, W(o.Neck));
            }
            else Aim(spine, neck, W(o.Neck));
            // Head: up along the spine, tipped a little forward (out of the wall).
            if (head != null)
            {
                Vector3 up = (W(o.Neck) - W(chestAt)).normalized;
                float len = Vector3.Distance(neck.position, head.position);
                Aim(neck, head, neck.position + (up + WallPlacer.U(f.Normal) * 0.35f).normalized * len);
            }

            Aim(ulL, llL, W(o.KneeL));
            Aim(ulR, llR, W(o.KneeR));
            Aim(llL, ftL, W(o.FootL));
            Aim(llR, ftR, W(o.FootR));
            Aim(uaL, laL, W(o.ElbowL));
            Aim(uaR, laR, W(o.ElbowR));
            Aim(laL, hdL, W(o.HandL));
            Aim(laR, hdR, W(o.HandR));
        }
    }
}
