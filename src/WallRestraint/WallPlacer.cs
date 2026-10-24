using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using WallRestraint.Logic;
using NVector3 = System.Numerics.Vector3;

namespace WallRestraint
{
    /// <summary>
    /// Finds a wall in front of the player that can carry the restraints: a collider ahead (physics), then the drawn surface
    /// and the knee heights it allows (<see cref="DepthProbe"/> + <see cref="WallSpot.Heights"/>), then room for her in front.
    /// </summary>
    internal static class WallPlacer
    {
        public struct Spot
        {
            public bool Ok;
            public string Reason;
            public WallFrame Frame;
            public WallSpot.Result Surface;
            public float Knee, KneeLow, KneeHigh; // chosen knee height and the range the wall allows (m above the floor)
            public string Collider;
            public IntPtr ColliderPtr;
            public string Map;        // the probed patch as text (WallSpot.Map)
        }

        public const int GridW = 16;
        private const float RowHeight = 0.03f, Step = 0.01f;

        /// <summary>
        /// The wall in front of her and the knee heights it allows: one tall probe from kneeling (<paramref name="kneeMin"/>) to the
        /// highest footprint (<paramref name="kneeMax"/>), then <see cref="WallSpot.Heights"/> picks the height nearest
        /// <paramref name="kneePreferred"/>. <paramref name="span"/>: the footprint around the knee height (<see cref="WallPose.Span"/>).
        /// </summary>
        public static Spot Find(PlayerController player, (float Below, float Above, float HalfWidth) span, float kneeMin, float kneeMax,
                                float kneePreferred, float reach, bool force = false)
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
            spot.ColliderPtr = hit.collider.Pointer;
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

            // One tall probe of the drawn surface, relative to the collider plane, covering every candidate footprint.
            float width = 2f * span.HalfWidth + 0.1f;
            float bottom = Math.Max(0.01f, kneeMin + span.Below - 0.02f), top = kneeMax + span.Above + 0.02f;
            int rows = Math.Min(128, Math.Max(8, (int)Math.Ceiling((top - bottom) / RowHeight)));
            float cellY = (top - bottom) / rows;
            Vector3 side = Vector3.Cross(up, n);
            Vector3 onPlane = hit.point + up * (floorY - hit.point.y); // the collider plane at floor height, below the hit
            Vector3 center = onPlane + up * ((bottom + top) / 2f);
            float[] depth = DepthProbe.SampleWithout(pt, center, n, up, width, top - bottom, GridW, rows);
            if (depth == null)
            {
                spot.Reason = "depth shader missing";
                return spot;
            }
            var fit = WallSpot.Heights(depth, GridW, rows, width / GridW, cellY, bottom, span.Below, span.Above, kneeMin, kneeMax, Step,
                                       kneePreferred, 0.015f, 0.09f);
            var s = fit.Surface;
            spot.Surface = s;
            spot.Map = WallSpot.Map(depth, GridW, rows, width / GridW, cellY, new WallSpot.Result { Offset = s.Offset }, 0.015f);
            spot.Knee = fit.Ok ? fit.Knee : Math.Min(Math.Max(kneePreferred, kneeMin), kneeMax);
            spot.KneeLow = fit.Ok ? fit.Low : spot.Knee;
            spot.KneeHigh = fit.Ok ? fit.High : spot.Knee;
            if (!fit.Ok)
            {
                spot.Reason = fit.Reason;
                if (!force) return spot;
            }
            // The fitted plane: depth grows along side by SlopeX, so its normal leans that way (upright walls: SlopeY ignored).
            Vector3 visibleN = (n + side * s.SlopeX).normalized;
            Vector3 origin = onPlane - n * s.Offset;
            var frame = new WallFrame(N(origin), N(visibleN), N(up), 1f);
            string blocked = InTheWay(frame, (span.HalfWidth, spot.Knee + span.Below, spot.Knee + span.Above), spot.ColliderPtr, pt);
            if (blocked != null)
            {
                spot.Reason = "something is in the way (" + blocked + ")";
                if (!force) return spot;
            }
            spot.Frame = frame;
            spot.Ok = spot.Reason == null || force; // forced (diagnostics): placed anyway, Reason says what would refuse it
            return spot;
        }

        /// <summary>Room for her: a box in front of the wall over the footprint's height, as wide as the restraints. Returns the
        /// name of the first collider in the way, or null.</summary>
        public static string InTheWay(WallFrame frame, (float HalfWidth, float Bottom, float Top) fp, IntPtr wall, Transform player)
        {
            Vector3 boxCenter = U(frame.Point(new NVector3(0.25f, 0f, (fp.Bottom + fp.Top) / 2f)));
            Vector3 half = new Vector3(fp.HalfWidth, (fp.Top - fp.Bottom) / 2f, 0.2f);
            Quaternion rot = Quaternion.LookRotation(U(frame.Normal), Vector3.up);
            var cols = Physics.OverlapBox(boxCenter, half, rot, ~0, QueryTriggerInteraction.Ignore);
            int npc = LayerMask.NameToLayer("Npc");
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                // The wall's own collider may stand up to ~18 cm in front of the drawn surface: it does not count.
                if (c == null || c.Pointer == wall || c.transform.IsChildOf(player) || c.gameObject.layer == npc) continue;
                return c.name;
            }
            return null;
        }

        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
