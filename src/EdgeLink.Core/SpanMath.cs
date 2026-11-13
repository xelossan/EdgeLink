namespace EdgeLink.Core;

/// <summary>Helpers for laying out spans on a monitor edge so that they never overlap.</summary>
public static class SpanMath {
    private const double Epsilon = 1e-9;

    /// <summary>Spans already taken on a monitor edge, optionally ignoring the one being edited.</summary>
    public static List<(double Start, double End)> Occupied(AppConfig config, Guid monitorId, Edge edge, LinkEnd? except = null)
    {
        var result = new List<(double, double)>();
        foreach (var link in config.Links) {
            foreach (var end in link.Ends) {
                if (ReferenceEquals(end, except) || end.MonitorId != monitorId || end.Edge != edge) continue;
                result.Add((end.Start, end.End));
            }
        }

        return result;
    }

    /// <summary>The unoccupied stretch of the edge containing <paramref name="at"/>, or null if that point is taken.</summary>
    public static (double Start, double End)? FreeIntervalAt(double at, IReadOnlyList<(double Start, double End)> occupied)
    {
        double lo = 0, hi = 1;
        foreach (var (start, end) in occupied) {
            if (at > start + Epsilon && at < end - Epsilon) return null;

            if (end <= at + Epsilon) lo = Math.Max(lo, end);
            else if (start >= at - Epsilon) hi = Math.Min(hi, start);
        }

        return hi - lo > Epsilon ? (lo, hi) : null;
    }

    /// <summary>
    /// A span of the wanted length centred on <paramref name="center"/>, shifted and shortened as
    /// needed to stay within the free stretch around that point.
    /// </summary>
    public static (double Start, double End)? Place(double center, double length, IReadOnlyList<(double Start, double End)> occupied)
    {
        center = Math.Clamp(center, 0, 1);
        if (FreeIntervalAt(center, occupied) is not (var lo, var hi)) return null;

        length = Math.Min(length, hi - lo);
        // not Math.Clamp: rounding can leave hi - length a hair below lo, which Clamp rejects
        var start = Math.Max(lo, Math.Min(center - length / 2, hi - length));
        return (start, Math.Min(start + length, hi));
    }

    /// <summary>Rounds a fraction of an edge to a whole pixel.</summary>
    public static double SnapToPixel(double fraction, int edgeLength) =>
        Math.Clamp(Math.Round(fraction * edgeLength) / edgeLength, 0, 1);
}
