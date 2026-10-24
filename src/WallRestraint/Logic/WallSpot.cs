using System;

namespace WallRestraint.Logic
{
    /// <summary>
    /// Is a wall patch usable? Input: how far the drawn surface lies behind a reference plane (the collider), sampled on a grid
    /// over the patch (row-major, row 0 at the bottom; NaN = nothing drawn). A plane is fitted through the samples; the patch is
    /// usable when everything is drawn, nothing sticks out of the plane by more than <c>maxBump</c> and the plane is not tilted
    /// by more than <c>maxSlope</c> (rise over run) against the reference.
    /// </summary>
    public static class WallSpot
    {
        public struct Result
        {
            public bool Ok;
            public string Reason;     // why not (for the player and the log)
            public float Offset;      // the fitted plane's depth at the patch centre (m, + = behind the reference plane)
            public float SlopeX;      // d(depth)/d(sideways): the normal is reference + SlopeX * side + SlopeY * up
            public float SlopeY;      // d(depth)/d(up)
            public float Bump;        // largest distance of a sample from the fitted plane (m)
            public int Empty;
        }

        public static Result Judge(float[] depth, int width, int height, float cellX, float cellY, float maxBump, float maxSlope)
        {
            if (depth == null || depth.Length != width * height || width < 2 || height < 2) throw new ArgumentException("grid size");
            var r = new Result();
            // Least squares for d = a + b x + c y with x, y centred on the patch (so a is the depth at the centre).
            double sx = 0, sy = 0, sd = 0, sxx = 0, syy = 0, sxy = 0, sxd = 0, syd = 0;
            int n = 0;
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    float d = depth[j * width + i];
                    if (float.IsNaN(d)) { r.Empty++; continue; }
                    double x = (i - (width - 1) / 2.0) * cellX, y = (j - (height - 1) / 2.0) * cellY;
                    n++; sx += x; sy += y; sd += d; sxx += x * x; syy += y * y; sxy += x * y; sxd += x * d; syd += y * d;
                }
            if (n < 3) { r.Reason = "nothing is drawn there (an invisible wall?)"; return r; }
            if (!Solve3(n, sx, sy, sxx, sxy, syy, sd, sxd, syd, out double a, out double b, out double c))
            {
                r.Reason = "too few samples";
                return r;
            }
            r.Offset = (float)a;
            r.SlopeX = (float)b;
            r.SlopeY = (float)c;
            double bump = 0;
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    float d = depth[j * width + i];
                    if (float.IsNaN(d)) continue;
                    double x = (i - (width - 1) / 2.0) * cellX, y = (j - (height - 1) / 2.0) * cellY;
                    bump = Math.Max(bump, Math.Abs(d - (a + b * x + c * y)));
                }
            r.Bump = (float)bump;
            if (r.Empty > 0) r.Reason = "the wall has gaps there (" + r.Empty + " of " + (width * height) + " samples empty)";
            else if (bump > maxBump) r.Reason = "the wall is not flat there (" + (bump * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " cm bump)";
            else if (Math.Abs(b) > maxSlope || Math.Abs(c) > maxSlope) r.Reason = "the wall is not straight in front of her";
            else r.Ok = true;
            return r;
        }

        public struct HeightFit
        {
            public bool Ok;
            public float Knee;         // the chosen knee height (m above the floor)
            public float Low, High;    // the contiguous range of knee heights around it that fit (for + / -)
            public Result Surface;     // the wall under the chosen footprint
            public string Reason;      // why nothing fits
        }

        /// <summary>
        /// Which knee heights the wall allows. <paramref name="depth"/> is one tall probe (as for <see cref="Judge"/>; row 0's
        /// lower edge at <paramref name="rowBase"/> m above the floor, rows <paramref name="cellY"/> high). The footprint for a
        /// knee height k covers k + <paramref name="below"/> .. k + <paramref name="above"/> (everything in the pose moves with
        /// the knees). Candidates run from <paramref name="kMin"/> to <paramref name="kMax"/> in <paramref name="step"/>; the one
        /// nearest <paramref name="preferred"/> that fits wins (ties: the lower).
        /// </summary>
        public static HeightFit Heights(float[] depth, int width, int height, float cellX, float cellY, float rowBase,
                                        float below, float above, float kMin, float kMax, float step, float preferred,
                                        float maxBump, float maxSlope)
        {
            var fit = new HeightFit();
            int n = Math.Max(1, (int)Math.Floor((kMax - kMin) / step + 1e-4) + 1);
            var ok = new bool[n];
            var results = new Result[n];
            for (int i = 0; i < n; i++)
            {
                float k = kMin + i * step;
                int r0 = (int)Math.Floor((k + below - rowBase) / cellY), r1 = (int)Math.Ceiling((k + above - rowBase) / cellY);
                if (r0 < 0 || r1 > height || r1 - r0 < 2)
                {
                    results[i] = new Result { Reason = "the wall is not tall enough there" };
                    continue;
                }
                var sub = new float[width * (r1 - r0)];
                Array.Copy(depth, r0 * width, sub, 0, sub.Length);
                results[i] = Judge(sub, width, r1 - r0, cellX, cellY, maxBump, maxSlope);
                ok[i] = results[i].Ok;
            }
            int want = (int)Math.Round((Math.Min(Math.Max(preferred, kMin), kMax) - kMin) / step);
            want = Math.Min(Math.Max(want, 0), n - 1);
            int best = -1;
            for (int d = 0; d < n && best < 0; d++)
            {
                if (want - d >= 0 && ok[want - d]) best = want - d;
                else if (want + d < n && ok[want + d]) best = want + d;
            }
            if (best < 0)
            {
                fit.Reason = results[want].Reason ?? "the wall does not fit";
                fit.Surface = results[want];
                return fit;
            }
            int lo = best, hi = best;
            while (lo > 0 && ok[lo - 1]) lo--;
            while (hi < n - 1 && ok[hi + 1]) hi++;
            fit.Ok = true;
            fit.Knee = kMin + best * step;
            fit.Low = kMin + lo * step;
            fit.High = kMin + hi * step;
            fit.Surface = results[best];
            return fit;
        }

        /// <summary>
        /// The patch as text for the log, top row first: '.' on the fitted plane (within <paramref name="tolerance"/>), a digit
        /// = recessed by that many cm (9 = 9 or more), a letter = sticks out by A = 1 .. I = 9 cm (# more), ' ' nothing drawn.
        /// </summary>
        public static string Map(float[] depth, int width, int height, float cellX, float cellY, in Result r, float tolerance)
        {
            var sb = new System.Text.StringBuilder();
            for (int j = height - 1; j >= 0; j--)
            {
                for (int i = 0; i < width; i++)
                {
                    float d = depth[j * width + i];
                    if (float.IsNaN(d)) { sb.Append(' '); continue; }
                    double x = (i - (width - 1) / 2.0) * cellX, y = (j - (height - 1) / 2.0) * cellY;
                    double res = d - (r.Offset + r.SlopeX * x + r.SlopeY * y);
                    int cm = (int)Math.Min(9, Math.Max(1, Math.Round(Math.Abs(res) * 100)));
                    sb.Append(Math.Abs(res) <= tolerance ? '.' : res > 0 ? (char)('0' + cm) : Math.Abs(res) > 0.095 ? '#' : (char)('A' + cm - 1));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        // Normal equations of the plane fit (symmetric 3x3), Cramer's rule.
        private static bool Solve3(int n, double sx, double sy, double sxx, double sxy, double syy, double sd, double sxd, double syd,
                                   out double a, out double b, out double c)
        {
            double m00 = n, m01 = sx, m02 = sy, m11 = sxx, m12 = sxy, m22 = syy;
            double det = m00 * (m11 * m22 - m12 * m12) - m01 * (m01 * m22 - m12 * m02) + m02 * (m01 * m12 - m11 * m02);
            a = b = c = 0;
            if (Math.Abs(det) < 1e-12) return false;
            a = (sd * (m11 * m22 - m12 * m12) - m01 * (sxd * m22 - m12 * syd) + m02 * (sxd * m12 - m11 * syd)) / det;
            b = (m00 * (sxd * m22 - m12 * syd) - sd * (m01 * m22 - m12 * m02) + m02 * (m01 * syd - sxd * m02)) / det;
            c = (m00 * (m11 * syd - sxd * m12) - m01 * (m01 * syd - sxd * m02) + sd * (m01 * m12 - m11 * m02)) / det;
            return true;
        }
    }
}
