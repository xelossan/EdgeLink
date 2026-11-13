using EdgeLink.Core;

namespace EdgeLink.Tests;

public class SpanMathAndStoreTests {
    [Fact]
    public void Free_interval_lies_between_neighbouring_spans()
    {
        var occupied = new List<(double, double)> { (0.0, 0.2), (0.7, 1.0) };

        Assert.Equal((0.2, 0.7), SpanMath.FreeIntervalAt(0.5, occupied));
        Assert.Null(SpanMath.FreeIntervalAt(0.1, occupied));
        Assert.Equal((0.0, 1.0), SpanMath.FreeIntervalAt(0.5, []));
    }

    [Fact]
    public void Placed_span_is_shifted_and_shortened_to_fit()
    {
        var occupied = new List<(double, double)> { (0.0, 0.2) };

        var shifted = SpanMath.Place(0.25, 0.3, occupied);
        Assert.NotNull(shifted);
        Assert.Equal(0.2, shifted.Value.Start, 9);
        Assert.Equal(0.5, shifted.Value.End, 9);

        var shortened = SpanMath.Place(0.5, 5, occupied);
        Assert.Equal((0.2, 1.0), shortened);

        Assert.Null(SpanMath.Place(0.1, 0.3, occupied));
    }

    [Fact]
    public void Config_survives_a_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"edgelink-test-{Guid.NewGuid():N}.json");
        try {
            var config = new AppConfig();
            var a = new MonitorProfile { Label = "A", Fingerprint = new MonitorFingerprint { DevicePath = "x", Width = 10, Height = 20 } };
            var b = new MonitorProfile { Label = "B" };
            config.Monitors.AddRange([a, b]);
            config.Links.Add(new Link {
                A = new LinkEnd { MonitorId = a.Id, Edge = Edge.Bottom, Start = 0.25, End = 0.75 },
                B = new LinkEnd { MonitorId = b.Id, Edge = Edge.Top },
                Reversed = true
            });
            config.Settings.BlockUnlinkedEdges = false;

            var store = new ConfigStore(path);
            store.Save(config);
            var loaded = store.Load();

            Assert.False(loaded.Settings.BlockUnlinkedEdges);
            Assert.Equal(a.Fingerprint, loaded.Monitors[0].Fingerprint);
            var link = Assert.Single(loaded.Links);
            Assert.Equal(Edge.Bottom, link.A.Edge);
            Assert.Equal(0.25, link.A.Start);
            Assert.True(link.Reversed);
            Assert.Contains("\"Bottom\"", File.ReadAllText(path));
        }
        finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_config_is_set_aside_not_lost()
    {
        var path = Path.Combine(Path.GetTempPath(), $"edgelink-test-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ not json");

            var loaded = new ConfigStore(path).Load();

            Assert.Empty(loaded.Monitors);
            Assert.Equal("{ not json", File.ReadAllText(path + ".broken"));
        }
        finally {
            File.Delete(path);
            File.Delete(path + ".broken");
        }
    }
}
