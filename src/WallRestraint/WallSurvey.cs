using System;
using System.Globalization;
using System.Text;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace WallRestraint
{
    /// <summary>
    /// Diagnostics for choosing walls: what is around the player, how flat and thick it is, and whether its mesh is usable.
    /// Bridge: <c>game call WallRestraint.WallSurvey Run</c> (all directions) or <c>... Front</c> (the camera's direction).
    /// </summary>
    public static class WallSurvey
    {
        public static string Last = "";

        public static string Run() => Survey(16, false);

        public static string Front() => Survey(1, true);

        private static string Survey(int directions, bool cameraForward)
        {
            try
            {
                var player = PlayerController.Instance;
                if (player == null) return Last = "no player";
                Transform pt = player.transform;
                var sb = new StringBuilder();
                sb.Append("player ").Append(V(pt.position)).Append(" scene ").Append(player.gameObject.scene.name).Append('\n');
                Vector3 origin = pt.position + Vector3.up * 1.0f;
                Vector3 fwd = pt.forward;
                if (cameraForward && Camera.main != null) fwd = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                for (int i = 0; i < directions; i++)
                {
                    Vector3 dir = Quaternion.AngleAxis(360f * i / directions, Vector3.up) * fwd;
                    if (!Cast(origin, dir, 4f, pt, out RaycastHit hit))
                    {
                        sb.Append(i).Append(": nothing within 4 m\n");
                        continue;
                    }
                    sb.Append(i).Append(": ").Append(Describe(hit, pt, origin.y - 1.0f)).Append('\n');
                }
                Last = sb.ToString();
                Plugin.Logger.LogInfo("[WallSurvey] " + Environment.NewLine + Last);
                return "logged " + directions + " directions";
            }
            catch (Exception e)
            {
                return Last = "failed: " + e;
            }
        }

        /// <summary>Nearest solid (non-trigger) hit that does not belong to the player.</summary>
        internal static bool Cast(Vector3 origin, Vector3 dir, float max, Transform player, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            var hits = Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(player)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            return bestDist < float.MaxValue;
        }

        private static string Describe(RaycastHit hit, Transform player, float floorY)
        {
            var sb = new StringBuilder();
            Collider c = hit.collider;
            Vector3 n = hit.normal;
            sb.Append(hit.distance.ToString("0.00", CultureInfo.InvariantCulture)).Append(" m, normal ").Append(V(n)).Append(" (tilt ")
              .Append((90f - Vector3.Angle(n, Vector3.up)).ToString("0", CultureInfo.InvariantCulture)).Append(" deg)");
            sb.Append(", collider ").Append(c.GetIl2CppType().Name).Append(" '").Append(Path(c.transform)).Append("' layer ")
              .Append(LayerMask.LayerToName(c.gameObject.layer)).Append(" static ").Append(c.gameObject.isStatic);
            var mc = c.TryCast<MeshCollider>();
            if (mc != null && mc.sharedMesh != null)
                sb.Append(" mesh '").Append(mc.sharedMesh.name).Append("' readable ").Append(mc.sharedMesh.isReadable).Append(" convex ").Append(mc.convex);
            var bc = c.TryCast<BoxCollider>();
            if (bc != null) sb.Append(" box ").Append(V(Vector3.Scale(bc.size, c.transform.lossyScale)));

            // The renderer that draws the wall there (may be another object than the collider).
            var r = c.GetComponent<MeshRenderer>();
            if (r != null)
            {
                var mf = c.GetComponent<MeshFilter>();
                sb.Append("; renderer on collider: batched ").Append(r.isPartOfStaticBatch);
                if (mf != null && mf.sharedMesh != null) sb.Append(" mesh '").Append(mf.sharedMesh.name).Append("' readable ").Append(mf.sharedMesh.isReadable);
                if (r.sharedMaterial != null) sb.Append(" shader ").Append(r.sharedMaterial.shader.name);
            }
            else sb.Append("; no renderer on collider");

            // Flatness: a 7 x 9 grid over 0.7 m wide x 1.6 m (0.2 .. 1.8 m above the floor) on the hit plane.
            Vector3 right = Vector3.Cross(Vector3.up, n).normalized;
            if (right.sqrMagnitude < 0.5f) right = Vector3.right;
            Vector3 upOnWall = Vector3.Cross(n, right).normalized;
            int miss = 0, other = 0, count = 0;
            float maxDev = 0f;
            for (int y = 0; y < 9; y++)
                for (int x = 0; x < 7; x++)
                {
                    Vector3 onPlane = hit.point + right * (-0.35f + 0.7f * x / 6f);
                    float h = 0.2f + 1.6f * y / 8f;
                    onPlane += upOnWall * ((floorY + h - hit.point.y) / Math.Max(0.2f, upOnWall.y));
                    count++;
                    if (!Cast(onPlane + n * 0.5f, -n, 1.0f, player, out RaycastHit g)) { miss++; continue; }
                    if (g.collider.Pointer != c.Pointer) other++;
                    maxDev = Math.Max(maxDev, Math.Abs(g.distance - 0.5f));
                }
            sb.Append("; flat grid ").Append(count - miss).Append('/').Append(count).Append(" hit, ").Append(other)
              .Append(" other colliders, max dev ").Append((maxDev * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" cm");

            sb.Append("; visible: ").Append(VisibleDepth(hit.point, n, right, upOnWall, floorY));

            // Thickness: from 2 m behind the surface back towards it.
            Vector3 behind = hit.point - n * 2f;
            if (Cast(behind, n, 2f - 0.001f, player, out RaycastHit back))
                sb.Append("; behind: surface at ").Append((2f - back.distance).ToString("0.00", CultureInfo.InvariantCulture)).Append(" m depth ('").Append(Path(back.collider.transform)).Append("')");
            else sb.Append("; behind: nothing within 2 m (one-sided or inside a closed collider)");
            return sb.ToString();
        }

        private const int GridW = 8, GridH = 18;

        /// <summary>What is drawn over the 0.7 x 1.6 m patch, compared with the collider plane (see <see cref="DepthProbe"/>).</summary>
        private static string VisibleDepth(Vector3 point, Vector3 n, Vector3 right, Vector3 up, float floorY)
        {
            const float width = 0.7f, height = 1.6f;
            Vector3 center = point + up * ((floorY + 0.2f + height / 2f - point.y) / Math.Max(0.2f, up.y));
            float[] depth = DepthProbe.SampleWithout(PlayerController.Instance.transform, center, n, up, width, height, GridW, GridH);
            if (depth == null) return "no depth shader";
            int empty = 0, count = 0;
            float sum = 0f, min = float.MaxValue, max = float.MinValue;
            foreach (float off in depth)
            {
                if (float.IsNaN(off)) { empty++; continue; }
                count++;
                sum += off;
                min = Math.Min(min, off);
                max = Math.Max(max, off);
            }
            if (count == 0) return "NOTHING drawn (" + empty + " empty cells)";
            return "offset mean " + (sum / count * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " cm, range "
                   + (min * 100f).ToString("0.0", CultureInfo.InvariantCulture) + ".." + (max * 100f).ToString("0.0", CultureInfo.InvariantCulture)
                   + " cm, " + empty + "/" + (GridW * GridH) + " empty";
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static string V(Vector3 v) => "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";
    }
}
