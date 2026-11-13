namespace EdgeLink.Core;

/// <summary>
/// Decides, for every mouse move, whether the cursor should be carried through a link, stopped at
/// a wall, or left to Windows. Stateless apart from a small guard against bouncing straight back.
/// </summary>
public sealed class CrossingEngine {
    /// <summary>How far the cursor must travel inwards from the edge it arrived at before that link re-arms.</summary>
    public const int RearmDistance = 16;

    private volatile Topology _topology = Topology.Empty;

    // set after arriving through a link whose two edges do not face each other: there, the motion
    // that carried the cursor across would immediately carry it back
    private bool _guardActive;
    private Guid _guardLink;
    private Guid _guardMonitor;
    private Edge _guardEdge;

    public Topology Topology {
        get => _topology;
        set => _topology = value;
    }

    /// <param name="current">Where the cursor is now.</param>
    /// <param name="proposed">Where the mouse move wants to put it; may lie outside every monitor.</param>
    /// <param name="target">Where to put the cursor instead, when the result is true.</param>
    /// <returns>True if the move must be replaced by a jump to <paramref name="target"/>.</returns>
    public bool TryRedirect(PixelPoint current, PixelPoint proposed, out PixelPoint target)
    {
        target = default;

        var topology = _topology;
        var monitor = topology.FindMonitor(current);
        if (monitor == null) return false;

        if (_guardActive && (monitor.Id != _guardMonitor || monitor.Bounds.DistanceFromEdge(_guardEdge, current) >= RearmDistance)) {
            _guardActive = false;
        }

        var bounds = monitor.Bounds;
        if (bounds.Contains(proposed)) return false;

        // a monitor without links is left entirely to Windows, otherwise the cursor could get stuck on it
        if (!monitor.HasLinks) return false;

        double dx = proposed.X - current.X;
        double dy = proposed.Y - current.Y;

        // the move leaves through at most one vertical and one horizontal edge; try them in the
        // order the motion crosses them
        var hasFirst = false;
        var hasSecond = false;
        (Edge Edge, double T) first = default, second = default;

        if (proposed.X < bounds.X) Insert(Edge.Left, (bounds.X - current.X) / dx);
        else if (proposed.X >= bounds.Right) Insert(Edge.Right, (bounds.Right - 1 - current.X) / dx);

        if (proposed.Y < bounds.Y) Insert(Edge.Top, (bounds.Y - current.Y) / dy);
        else if (proposed.Y >= bounds.Bottom) Insert(Edge.Bottom, (bounds.Bottom - 1 - current.Y) / dy);

        if (hasFirst && TryCross(monitor, first.Edge, first.T, current, dx, dy, out target)) return true;
        if (hasSecond && TryCross(monitor, second.Edge, second.T, current, dx, dy, out target)) return true;

        if (!topology.BlockUnlinkedEdges) return false;

        target = bounds.Clamp(proposed);
        return true;

        void Insert(Edge edge, double t)
        {
            if (!hasFirst) {
                first = (edge, t);
                hasFirst = true;
            }
            else if (t < first.T) {
                second = first;
                first = (edge, t);
                hasSecond = true;
            }
            else {
                second = (edge, t);
                hasSecond = true;
            }
        }
    }

    private bool TryCross(ActiveMonitor monitor, Edge edge, double t, PixelPoint current, double dx, double dy, out PixelPoint target)
    {
        target = default;

        var spans = monitor.Spans(edge);
        if (spans.Count == 0) return false;

        var bounds = monitor.Bounds;
        var length = bounds.EdgeLength(edge);

        // pixel along the edge where the motion crosses it
        var along = edge.IsVertical()
            ? Math.Clamp(current.Y + t * dy, bounds.Y, bounds.Bottom - 1) - bounds.Y
            : Math.Clamp(current.X + t * dx, bounds.X, bounds.Right - 1) - bounds.X;
        var pixel = Math.Clamp((int) Math.Floor(along), 0, length - 1);

        for (var i = 0; i < spans.Count; i++) {
            var span = spans[i];
            var (from, to) = PixelRange(span.Start, span.End, length);
            if (pixel < from || pixel > to) continue;

            if (_guardActive && _guardLink == span.LinkId && _guardMonitor == monitor.Id && _guardEdge == edge) return false;

            target = MapToTarget(span, pixel, from, to);

            _guardActive = span.TargetEdge != edge.Opposite();
            _guardLink = span.LinkId;
            _guardMonitor = span.Target.Id;
            _guardEdge = span.TargetEdge;
            return true;
        }

        return false;
    }

    private static PixelPoint MapToTarget(ActiveSpan span, int pixel, int from, int to)
    {
        var u = to > from ? (pixel - from) / (double) (to - from) : 0.5;
        if (span.Reversed) u = 1 - u;

        var bounds = span.Target.Bounds;
        var (targetFrom, targetTo) = PixelRange(span.TargetStart, span.TargetEnd, bounds.EdgeLength(span.TargetEdge));
        var position = targetFrom + (int) Math.Round(u * (targetTo - targetFrom));

        return span.TargetEdge switch {
            Edge.Left => new PixelPoint(bounds.X, bounds.Y + position),
            Edge.Right => new PixelPoint(bounds.Right - 1, bounds.Y + position),
            Edge.Top => new PixelPoint(bounds.X + position, bounds.Y),
            _ => new PixelPoint(bounds.X + position, bounds.Bottom - 1)
        };
    }

    /// <summary>First and last pixel (inclusive) covered by a span on an edge of the given length.</summary>
    public static (int From, int To) PixelRange(double start, double end, int length)
    {
        var from = Math.Clamp((int) Math.Round(start * length), 0, length - 1);
        var to = Math.Clamp((int) Math.Round(end * length) - 1, from, length - 1);
        return (from, to);
    }
}
