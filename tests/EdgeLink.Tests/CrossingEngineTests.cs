using EdgeLink.Core;

namespace EdgeLink.Tests;

public class CrossingEngineTests {
    // a wide main monitor with a smaller one somewhere else entirely (not touching)
    private static readonly PixelRect MainBounds = new(0, 0, 3440, 1440);
    private static readonly PixelRect SmallBounds = new(5000, 3000, 1000, 500);

    private readonly AppConfig _config = new();
    private readonly MonitorProfile _main = new() { Label = "main" };
    private readonly MonitorProfile _small = new() { Label = "small" };

    public CrossingEngineTests()
    {
        _config.Monitors.AddRange([_main, _small]);
    }

    private CrossingEngine Engine(params (MonitorProfile Profile, PixelRect Bounds)[] online)
    {
        if (online.Length == 0) online = [(_main, MainBounds), (_small, SmallBounds)];

        var monitors = online.Select(o => new OnlineMonitor(o.Profile, new DetectedMonitor(new MonitorFingerprint {
            X = o.Bounds.X, Y = o.Bounds.Y, Width = o.Bounds.Width, Height = o.Bounds.Height
        })));
        return new CrossingEngine { Topology = Topology.Build(_config, monitors) };
    }

    private Link AddLink(Edge edgeA, double startA, double endA, Edge edgeB, double startB, double endB, bool reversed = false)
    {
        var link = new Link {
            A = new LinkEnd { MonitorId = _main.Id, Edge = edgeA, Start = startA, End = endA },
            B = new LinkEnd { MonitorId = _small.Id, Edge = edgeB, Start = startB, End = endB },
            Reversed = reversed
        };
        _config.Links.Add(link);
        return link;
    }

    [Fact]
    public void Move_inside_monitor_is_left_alone()
    {
        AddLink(Edge.Bottom, 0, 1, Edge.Top, 0, 1);

        Assert.False(Engine().TryRedirect(new(100, 100), new(200, 300), out _));
    }

    [Fact]
    public void Small_span_maps_onto_whole_target_edge()
    {
        // 1000..2000 px of the main monitor's bottom edge <-> the whole top edge of the small one
        AddLink(Edge.Bottom, 1000 / 3440.0, 2000 / 3440.0, Edge.Top, 0, 1);
        var engine = Engine();

        Assert.True(engine.TryRedirect(new(1000, 1439), new(1000, 1445), out var atStart));
        Assert.Equal(new PixelPoint(5000, 3000), atStart);

        Assert.True(engine.TryRedirect(new(1999, 1439), new(1999, 1445), out var atEnd));
        Assert.Equal(new PixelPoint(5999, 3000), atEnd);

        Assert.True(engine.TryRedirect(new(1500, 1439), new(1500, 1445), out var middle));
        Assert.InRange(middle.X, 5499, 5501);
    }

    [Fact]
    public void Whole_edge_maps_back_onto_small_span()
    {
        AddLink(Edge.Bottom, 1000 / 3440.0, 2000 / 3440.0, Edge.Top, 0, 1);
        var engine = Engine();

        Assert.True(engine.TryRedirect(new(5000, 3000), new(5000, 2990), out var left));
        Assert.Equal(new PixelPoint(1000, 1439), left);

        Assert.True(engine.TryRedirect(new(5999, 3000), new(5999, 2990), out var right));
        Assert.Equal(new PixelPoint(1999, 1439), right);
    }

    [Fact]
    public void Reversed_link_flips_the_mapping()
    {
        AddLink(Edge.Bottom, 0, 1, Edge.Top, 0, 1, reversed: true);

        Assert.True(Engine().TryRedirect(new(0, 1439), new(0, 1445), out var target));
        Assert.Equal(new PixelPoint(5999, 3000), target);
    }

    [Fact]
    public void Outside_the_span_the_edge_is_a_wall_when_blocking()
    {
        AddLink(Edge.Bottom, 1000 / 3440.0, 2000 / 3440.0, Edge.Top, 0, 1);

        Assert.True(Engine().TryRedirect(new(500, 1439), new(520, 1460), out var target));
        Assert.Equal(new PixelPoint(520, 1439), target); // slides along the wall
    }

    [Fact]
    public void Outside_the_span_windows_decides_when_not_blocking()
    {
        _config.Settings.BlockUnlinkedEdges = false;
        AddLink(Edge.Bottom, 1000 / 3440.0, 2000 / 3440.0, Edge.Top, 0, 1);

        Assert.False(Engine().TryRedirect(new(500, 1439), new(520, 1460), out _));
    }

    [Fact]
    public void Monitor_without_links_is_never_blocked()
    {
        Assert.False(Engine().TryRedirect(new(500, 1439), new(520, 1460), out _));
    }

    [Fact]
    public void Link_to_a_disconnected_monitor_is_inactive_but_kept()
    {
        AddLink(Edge.Bottom, 0, 1, Edge.Top, 0, 1);

        Assert.False(Engine((_main, MainBounds)).TryRedirect(new(500, 1439), new(500, 1460), out _));
        Assert.Single(_config.Links);
    }

    [Fact]
    public void Several_links_share_one_edge()
    {
        var third = new MonitorProfile();
        _config.Monitors.Add(third);
        AddLink(Edge.Bottom, 0, 0.5, Edge.Top, 0, 1);
        _config.Links.Add(new Link {
            A = new LinkEnd { MonitorId = _main.Id, Edge = Edge.Bottom, Start = 0.5, End = 1 },
            B = new LinkEnd { MonitorId = third.Id, Edge = Edge.Top, Start = 0, End = 1 }
        });
        var engine = Engine((_main, MainBounds), (_small, SmallBounds), (third, new PixelRect(-4000, 0, 800, 600)));

        Assert.True(engine.TryRedirect(new(100, 1439), new(100, 1445), out var toSmall));
        Assert.True(SmallBounds.Contains(toSmall));

        Assert.True(engine.TryRedirect(new(3000, 1439), new(3000, 1445), out var toThird));
        Assert.Equal(-4000 + (int) Math.Round((3000 - 1720) / 1719.0 * 799), toThird.X);
        Assert.Equal(0, toThird.Y);
    }

    [Fact]
    public void Diagonal_move_through_a_corner_uses_the_linked_edge()
    {
        AddLink(Edge.Bottom, 0, 1, Edge.Top, 0, 1);

        // leaves through the right edge first (no link there), then the bottom edge
        Assert.True(Engine().TryRedirect(new(3438, 1430), new(3445, 1445), out var target));
        Assert.True(SmallBounds.Contains(target));
    }

    [Fact]
    public void Link_overrides_windows_adjacency()
    {
        // the small monitor touches the main one on the right, but the right edge is linked to its bottom
        var touching = new PixelRect(3440, 0, 1000, 500);
        AddLink(Edge.Right, 0, 1, Edge.Bottom, 0, 1);

        Assert.True(Engine((_main, MainBounds), (_small, touching)).TryRedirect(new(3439, 720), new(3450, 720), out var target));
        Assert.Equal(499, target.Y);
    }

    [Fact]
    public void Same_side_link_does_not_bounce_back_until_cursor_moves_away()
    {
        // right edge <-> right edge: the motion that crosses would cross straight back
        AddLink(Edge.Right, 0, 1, Edge.Right, 0, 1);
        var engine = Engine();

        Assert.True(engine.TryRedirect(new(3439, 720), new(3450, 720), out var arrived));
        Assert.Equal(5999, arrived.X);

        // still pushing right: wall, not a bounce
        Assert.True(engine.TryRedirect(arrived, new(arrived.X + 10, arrived.Y), out var held));
        Assert.Equal(arrived, held);

        // move away from the edge, then come back: the link works again
        var away = new PixelPoint(arrived.X - 40, arrived.Y);
        Assert.False(engine.TryRedirect(arrived, away, out _));
        Assert.False(engine.TryRedirect(away, arrived, out _));
        Assert.True(engine.TryRedirect(arrived, new(arrived.X + 10, arrived.Y), out var back));
        Assert.Equal(3439, back.X);
    }

    [Fact]
    public void Facing_edges_allow_an_immediate_return()
    {
        AddLink(Edge.Bottom, 0, 1, Edge.Top, 0, 1);
        var engine = Engine();

        Assert.True(engine.TryRedirect(new(1000, 1439), new(1000, 1445), out var arrived));
        Assert.True(engine.TryRedirect(arrived, new(arrived.X, arrived.Y - 5), out var back));
        Assert.Equal(1439, back.Y);
    }

    [Fact]
    public void Round_trip_returns_close_to_the_starting_pixel()
    {
        AddLink(Edge.Bottom, 763 / 3440.0, 2695 / 3440.0, Edge.Top, 0, 1);
        var engine = Engine();

        for (var x = 763; x < 2695; x += 37) {
            Assert.True(engine.TryRedirect(new(x, 1439), new(x, 1445), out var there));
            Assert.True(engine.TryRedirect(there, new(there.X, there.Y - 5), out var back));
            Assert.InRange(back.X, x - 2, x + 2);
        }
    }
}
