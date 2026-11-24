using EdgeLink.Core;

namespace EdgeLink.Tests;

public class MonitorMatcherTests {
    private const string Spacedesk = @"\\?\SWD#{1baad4ac}#0000";

    private static MonitorFingerprint Real(string path, int manufacturer, int product, string serial = "", int x = 0, int y = 0) => new() {
        DevicePath = path, FriendlyName = "PHL 346E2C", AdapterPath = "gpu", EdidManufacturer = manufacturer, EdidProduct = product,
        Serial = serial, X = x, Y = y, Width = 3440, Height = 1440
    };

    private static MonitorFingerprint Tablet(string path, int width, int height, int x, int y) => new() {
        DevicePath = path, AdapterPath = Spacedesk, X = x, Y = y, Width = width, Height = height
    };

    private static List<DetectedMonitor> Detected(params MonitorFingerprint[] fingerprints) =>
        fingerprints.Select(f => new DetectedMonitor(f)).ToList();

    private static MonitorFingerprint GlideX() => new() {
        DevicePath = "glidex", FriendlyName = "GlideX(5524)", AdapterPath = "glidex-gpu", EdidManufacturer = 38940, EdidProduct = 1,
        Serial = "892678708", X = -1935, Y = 1440, Width = 2360, Height = 1640
    };

    [Fact]
    public void A_merged_screen_is_recognised_through_either_connection()
    {
        var config = new AppConfig();
        var ipad = MonitorMatcher.Assign(config, Detected(GlideX()))[0].Profile;
        ipad.Label = "iPad";
        var other = new MonitorProfile();
        config.Monitors.Add(other);
        config.Links.Add(new Link { A = new LinkEnd { MonitorId = ipad.Id }, B = new LinkEnd { MonitorId = other.Id } });

        // the same tablet through spacedesk: no EDID, so a new profile
        var viaSpacedesk = MonitorMatcher.Assign(config, Detected(Tablet("uid261", 2360, 1640, -1931, 1440)))[0].Profile;
        Assert.NotSame(ipad, viaSpacedesk);
        config.Links.Add(new Link { A = new LinkEnd { MonitorId = viaSpacedesk.Id }, B = new LinkEnd { MonitorId = other.Id, Edge = Edge.Right } });
        config.Links.Add(new Link { A = new LinkEnd { MonitorId = viaSpacedesk.Id }, B = new LinkEnd { MonitorId = ipad.Id } });

        MonitorMatcher.Merge(config, viaSpacedesk, ipad);

        Assert.DoesNotContain(viaSpacedesk, config.Monitors);
        Assert.Equal(2, config.Links.Count); // the link between the two is gone, the other one moved
        Assert.All(config.Links, l => Assert.Equal(ipad.Id, l.A.MonitorId));

        Assert.Same(ipad, MonitorMatcher.Assign(config, Detected(Tablet("uid261", 2360, 1640, -1931, 1440)))[0].Profile);
        Assert.Same(ipad, MonitorMatcher.Assign(config, Detected(GlideX()))[0].Profile);
        Assert.Same(ipad, MonitorMatcher.Assign(config, Detected(Tablet("uid262", 2360, 1640, -1931, 1440)))[0].Profile);
        Assert.Equal(2, config.Monitors.Count);
        Assert.Single(ipad.OtherFingerprints);
    }

    [Fact]
    public void New_monitors_get_profiles_and_are_recognised_next_time()
    {
        var config = new AppConfig();
        var first = MonitorMatcher.Assign(config, Detected(Real("p1", 1, 2), Tablet("uid259", 1467, 960, 3440, 0)));
        var second = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1467, 960, 3440, 0), Real("p1", 1, 2)));

        Assert.Equal(2, config.Monitors.Count);
        Assert.Equal(first[0].Profile.Id, second[1].Profile.Id);
        Assert.Equal(first[1].Profile.Id, second[0].Profile.Id);
    }

    [Fact]
    public void Tablets_that_swapped_device_paths_are_told_apart_by_resolution()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1467, 960, 3440, 0), Tablet("uid260", 1280, 800, -1280, 0)));

        // connected in the other order: Windows hands out the device paths the other way round
        var after = MonitorMatcher.Assign(config, Detected(Tablet("uid260", 1467, 960, 3440, 0), Tablet("uid259", 1280, 800, -1280, 0)));

        Assert.Equal(2, config.Monitors.Count);
        Assert.Equal(before[0].Profile.Id, after[0].Profile.Id);
        Assert.Equal(before[1].Profile.Id, after[1].Profile.Id);
    }

    [Fact]
    public void Single_tablet_reconnecting_under_the_other_tablets_path_keeps_its_profile()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1467, 960, 3440, 0), Tablet("uid260", 1280, 800, -1280, 0)));

        var after = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1280, 800, -1280, 0)));

        Assert.Equal(before[1].Profile.Id, after[0].Profile.Id);
        Assert.Equal(2, config.Monitors.Count);
    }

    [Fact]
    public void Identical_real_monitors_are_told_apart_by_device_path()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Real("p1", 1, 2, x: 0), Real("p2", 1, 2, x: 3440)));

        // Windows rearranged them, so position no longer helps
        var after = MonitorMatcher.Assign(config, Detected(Real("p2", 1, 2, x: 0), Real("p1", 1, 2, x: 3440)));

        Assert.Equal(before[0].Profile.Id, after[1].Profile.Id);
        Assert.Equal(before[1].Profile.Id, after[0].Profile.Id);
    }

    [Fact]
    public void Serial_number_follows_a_monitor_to_another_port()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Real("p1", 1, 2, "A1", x: 0), Real("p2", 1, 2, "B2", x: 3440)));
        var after = MonitorMatcher.Assign(config, Detected(Real("p2", 1, 2, "A1", x: 3440), Real("p1", 1, 2, "B2", x: 0)));

        Assert.Equal(before[0].Profile.Id, after[0].Profile.Id);
        Assert.Equal(before[1].Profile.Id, after[1].Profile.Id);
    }

    [Fact]
    public void Different_model_is_never_matched()
    {
        Assert.Equal(0, MonitorMatcher.Score(Real("p1", 1, 2), Real("p1", 1, 3)));
        Assert.Equal(0, MonitorMatcher.Score(Real("p1", 1, 2), Tablet("p1", 3440, 1440, 0, 0)));
    }

    [Fact]
    public void Rotated_tablet_is_still_recognised()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1467, 960, 3440, 0)));
        var after = MonitorMatcher.Assign(config, Detected(Tablet("uid261", 960, 1467, 3440, 200)));

        Assert.Equal(before[0].Profile.Id, after[0].Profile.Id);
    }

    [Fact]
    public void Swapping_identity_exchanges_which_monitor_each_profile_means()
    {
        var config = new AppConfig();
        var before = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1280, 800, 3440, 0), Tablet("uid260", 1280, 800, -1280, 0)));

        MonitorMatcher.SwapIdentity(before[0].Profile, before[1].Profile);
        var after = MonitorMatcher.Assign(config, Detected(Tablet("uid259", 1280, 800, 3440, 0), Tablet("uid260", 1280, 800, -1280, 0)));

        Assert.Equal(before[0].Profile.Id, after[1].Profile.Id);
        Assert.Equal(before[1].Profile.Id, after[0].Profile.Id);
    }
}
