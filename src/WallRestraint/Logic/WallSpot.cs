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
