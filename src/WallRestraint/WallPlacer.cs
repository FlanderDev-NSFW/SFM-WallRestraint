using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using WallRestraint.Logic;
using NVector3 = System.Numerics.Vector3;

namespace WallRestraint
{
    /// <summary>
    /// Finds a wall in front of the player that can carry the restraints: a collider ahead (physics), then the drawn surface
    /// over the model's footprint (<see cref="DepthProbe"/> + <see cref="WallSpot"/>), then room for her body in front of it.
    /// </summary>
    internal static class WallPlacer
    {
        public struct Spot
        {
            public bool Ok;
            public string Reason;
            public WallFrame Frame;
            public WallSpot.Result Surface;
            public string Collider;
            public string Map;        // the probed patch as text (WallSpot.Map)
        }

        public const int GridW = 16, GridH = 32;

        /// <param name="footprint">what will hang on the wall and stand in front of it: half width, bottom and top above the floor (m)</param>
        public static Spot Find(PlayerController player, (float HalfWidth, float Bottom, float Top) footprint, float reach, bool force = false)
        {
            var spot = new Spot();
            Transform pt = player.transform;
            float floorY = pt.position.y;
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(pt.forward, up).normalized;
            Vector3 eye = pt.position + up * 1.0f;
            if (!WallSurvey.Cast(eye, forward, reach, pt, out RaycastHit hit))
            {
                spot.Reason = "no wall in front of her";
                return spot;
            }
            spot.Collider = hit.collider.name;
            Vector3 n = Vector3.ProjectOnPlane(hit.normal, up);
            if (n.sqrMagnitude < 0.25f || Vector3.Angle(hit.normal, n) > 10f)
            {
                spot.Reason = "that is not an upright wall";
                return spot;
            }
            n.Normalize();
            if (Vector3.Dot(n, -forward) < 0.5f)
            {
                spot.Reason = "she has to face the wall";
                return spot;
            }

            // The drawn surface over the footprint (plus a margin), relative to the collider plane.
            var fp = footprint;
            float width = 2f * fp.HalfWidth + 0.1f, bottom = Math.Max(0.02f, fp.Bottom - 0.05f), top = fp.Top + 0.05f;
            Vector3 side = Vector3.Cross(up, n);
            Vector3 onPlane = hit.point + up * (floorY - hit.point.y); // the collider plane at floor height, below the hit
            Vector3 center = onPlane + up * ((bottom + top) / 2f);
            float[] depth = DepthProbe.SampleWithout(pt, center, n, up, width, top - bottom, GridW, GridH);
            if (depth == null)
            {
                spot.Reason = "depth shader missing";
                return spot;
            }
            var s = WallSpot.Judge(depth, GridW, GridH, width / GridW, (top - bottom) / GridH, 0.015f, 0.09f);
            spot.Surface = s;
            spot.Map = WallSpot.Map(depth, GridW, GridH, width / GridW, (top - bottom) / GridH, s, 0.015f);
            if (!s.Ok)
            {
                spot.Reason = s.Reason;
                if (!force) return spot;
            }
            // The fitted plane: depth grows along side by SlopeX, so its normal leans that way (upright walls: SlopeY ignored).
            Vector3 visibleN = (n + side * s.SlopeX).normalized;
            Vector3 origin = onPlane - n * s.Offset;
            var frame = new WallFrame(N(origin), N(visibleN), N(up), 1f);

            // Room for her: a box in front of the wall from the knees to above the head, as wide as the restraints.
            Vector3 boxCenter = U(frame.Point(new NVector3(0.25f, 0f, (fp.Bottom + fp.Top) / 2f)));
            Vector3 half = new Vector3(fp.HalfWidth, (fp.Top - fp.Bottom) / 2f, 0.2f);
            Quaternion rot = Quaternion.LookRotation(U(frame.Normal), up);
            var cols = Physics.OverlapBox(boxCenter, half, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                // The wall's own collider may stand up to ~18 cm in front of the drawn surface: it does not count.
                if (c == null || c.Pointer == hit.collider.Pointer || c.transform.IsChildOf(pt) || c.gameObject.layer == LayerMask.NameToLayer("Npc")) continue;
                spot.Reason = "something is in the way (" + c.name + ")";
                if (!force) return spot;
                break;
            }
            spot.Frame = frame;
            spot.Ok = spot.Reason == null || force; // forced (diagnostics): placed anyway, Reason says what would refuse it
            return spot;
        }

        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
