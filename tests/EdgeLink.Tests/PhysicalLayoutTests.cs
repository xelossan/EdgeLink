using EdgeLink.Core;

namespace EdgeLink.Tests;

public class PhysicalLayoutTests {
    private static DetectedMonitor Detected(int x, int y, int width, int height, int edidWidth = 0, int edidHeight = 0, int dpi = 96) =>
        new(new MonitorFingerprint { X = x, Y = y, Width = width, Height = height }) { EdidWidthMm = edidWidth, EdidHeightMm = edidHeight, Dpi = dpi };

    [Fact]
    public void Manual_diagonal_wins_over_everything()
    {
        var size = PhysicalLayout.Estimate(new MonitorProfile { DiagonalInches = 11 }, Detected(0, 0, 2360, 1640, 300, 160, 192));

        Assert.Equal(PhysicalSizeSource.Manual, size.Source);
        Assert.Equal(11, size.DiagonalInches, 3);
        Assert.Equal(2360.0 / 1640, size.WidthMm / size.HeightMm, 3);
    }

    [Fact]
    public void Consistent_edid_is_used_as_is()
    {
        var size = PhysicalLayout.Estimate(new MonitorProfile(), Detected(0, 0, 3440, 1440, 800, 335));

        Assert.Equal(new PhysicalSize(800, 335, PhysicalSizeSource.Edid), size);
    }

    [Fact]
    public void Edid_of_a_rotated_monitor_is_turned_with_it()
    {
        var size = PhysicalLayout.Estimate(new MonitorProfile(), Detected(0, 0, 1440, 3440, 800, 335));

        Assert.Equal(new PhysicalSize(335, 800, PhysicalSizeSource.Edid), size);
    }

    [Fact]
    public void Edid_with_wrong_proportions_keeps_only_the_longer_side()
    {
        var size = PhysicalLayout.Estimate(new MonitorProfile(), Detected(0, 0, 2560, 720, 340, 200));

        Assert.Equal(PhysicalSizeSource.EdidCorrected, size.Source);
        Assert.Equal(340, size.WidthMm, 3);
        Assert.Equal(95.625, size.HeightMm, 3);
    }

    [Fact]
    public void Without_edid_higher_scaling_means_a_smaller_screen()
    {
        var plain = PhysicalLayout.Estimate(new MonitorProfile(), Detected(0, 0, 2200, 1440));
        var scaled = PhysicalLayout.Estimate(new MonitorProfile(), Detected(0, 0, 2200, 1440, dpi: 192));

        Assert.Equal(PhysicalSizeSource.Scaling, scaled.Source);
        Assert.Equal(plain.WidthMm / 2, scaled.WidthMm, 3);
    }

    [Fact]
    public void Arrangement_keeps_neighbours_together_without_overlap()
    {
        // an ultrawide with a small bar display under it and a tablet on either side of the bar
        var monitors = new List<(PixelRect, PhysicalSize)> {
            (new PixelRect(0, 0, 3440, 1440), new PhysicalSize(800, 335, PhysicalSizeSource.Edid)),
            (new PixelRect(425, 1449, 2560, 720), new PhysicalSize(340, 96, PhysicalSizeSource.Edid)),
            (new PixelRect(2985, 1455, 2200, 1440), new PhysicalSize(240, 157, PhysicalSizeSource.Manual)),
            (new PixelRect(-1935, 1440, 2360, 1640), new PhysicalSize(236, 164, PhysicalSizeSource.Manual))
        };

        var rects = PhysicalLayout.Arrange(monitors);

        for (var i = 0; i < rects.Length; i++) {
            Assert.Equal(monitors[i].Item2.WidthMm, rects[i].Width, 6);
            for (var j = i + 1; j < rects.Length; j++) {
                var overlap = rects[i].X < rects[j].Right - 0.01 && rects[j].X < rects[i].Right - 0.01
                              && rects[i].Y < rects[j].Bottom - 0.01 && rects[j].Y < rects[i].Bottom - 0.01;
                Assert.False(overlap, $"{i} overlaps {j}");
            }
        }

        var (main, bar, right, left) = (rects[0], rects[1], rects[2], rects[3]);
        Assert.Equal(main.Bottom, bar.Y, 6); // bar directly under the ultrawide
        Assert.Equal(bar.Right, right.X, 6); // tablets flank the bar
        Assert.Equal(bar.X, left.Right, 6);
        Assert.True(Math.Abs(right.Y - bar.Y) < 3 && Math.Abs(left.Y - bar.Y) < 3); // tops stay lined up
    }

    [Fact]
    public void A_monitor_that_would_land_on_another_is_pushed_clear()
    {
        // two small-pixel but physically large monitors side by side under one narrow monitor
        var monitors = new List<(PixelRect, PhysicalSize)> {
            (new PixelRect(0, 0, 1000, 1000), new PhysicalSize(100, 100, PhysicalSizeSource.Manual)),
            (new PixelRect(0, 1000, 500, 500), new PhysicalSize(300, 300, PhysicalSizeSource.Manual)),
            (new PixelRect(500, 1000, 500, 500), new PhysicalSize(300, 300, PhysicalSizeSource.Manual))
        };

        var rects = PhysicalLayout.Arrange(monitors);

        var overlap = rects[1].X < rects[2].Right - 0.01 && rects[2].X < rects[1].Right - 0.01
                      && rects[1].Y < rects[2].Bottom - 0.01 && rects[2].Y < rects[1].Bottom - 0.01;
        Assert.False(overlap);
    }
}
