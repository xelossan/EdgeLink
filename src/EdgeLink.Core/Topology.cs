namespace EdgeLink.Core;

/// <summary>
/// Immutable snapshot of the connected monitors and the links that are usable between them.
/// Built on the UI thread and read by the mouse hook thread without locking.
/// </summary>
public sealed class Topology {
    public static readonly Topology Empty = new([], false);

    public IReadOnlyList<ActiveMonitor> Monitors { get; }
    public bool BlockUnlinkedEdges { get; }

    /// <summary>Bounding box of all monitors.</summary>
    public PixelRect VirtualBounds { get; }

    private Topology(List<ActiveMonitor> monitors, bool blockUnlinkedEdges)
    {
        Monitors = monitors;
        BlockUnlinkedEdges = blockUnlinkedEdges;

        if (monitors.Count > 0) {
            var left = monitors.Min(m => m.Bounds.X);
            var top = monitors.Min(m => m.Bounds.Y);
            VirtualBounds = new PixelRect(left, top, monitors.Max(m => m.Bounds.Right) - left, monitors.Max(m => m.Bounds.Bottom) - top);
        }
    }

    public ActiveMonitor? FindMonitor(PixelPoint p)
    {
        // plain loop: this runs for every mouse move
        for (var i = 0; i < Monitors.Count; i++) {
            if (Monitors[i].Bounds.Contains(p)) return Monitors[i];
        }

        return null;
    }

    public static Topology Build(AppConfig config, IEnumerable<OnlineMonitor> online)
    {
        var monitors = online.Select(o => new ActiveMonitor(o.Profile.Id, o.Bounds)).ToList();
        var byId = monitors.ToDictionary(m => m.Id);

        foreach (var link in config.Links) {
            if (!link.Enabled) continue;
            if (!byId.TryGetValue(link.A.MonitorId, out var a) || !byId.TryGetValue(link.B.MonitorId, out var b)) continue;
            if (!IsUsable(link.A) || !IsUsable(link.B)) continue;

            a.Add(link.A.Edge, new ActiveSpan(link.Id, link.A.Start, link.A.End, b, link.B.Edge, link.B.Start, link.B.End, link.Reversed));
            b.Add(link.B.Edge, new ActiveSpan(link.Id, link.B.Start, link.B.End, a, link.A.Edge, link.A.Start, link.A.End, link.Reversed));
        }

        return new Topology(monitors, config.Settings.BlockUnlinkedEdges);
    }

    private static bool IsUsable(LinkEnd end) => end.Start >= 0 && end.End <= 1 && end.End > end.Start;
}

public sealed class ActiveMonitor(Guid id, PixelRect bounds) {
    private readonly List<ActiveSpan>[] _spans = [[], [], [], []];

    public Guid Id { get; } = id;
    public PixelRect Bounds { get; } = bounds;
    public bool HasLinks { get; private set; }

    public IReadOnlyList<ActiveSpan> Spans(Edge edge) => _spans[(int) edge];

    internal void Add(Edge edge, ActiveSpan span)
    {
        _spans[(int) edge].Add(span);
        HasLinks = true;
    }
}

/// <summary>One end of a link as seen from the monitor the cursor is leaving.</summary>
public sealed record ActiveSpan(
    Guid LinkId,
    double Start,
    double End,
    ActiveMonitor Target,
    Edge TargetEdge,
    double TargetStart,
    double TargetEnd,
    bool Reversed);
