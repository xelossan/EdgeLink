using System.Text.Json.Serialization;

namespace EdgeLink.Core;

public sealed class AppConfig {
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<MonitorProfile> Monitors { get; set; } = [];
    public List<Link> Links { get; set; } = [];

    public MonitorProfile? FindMonitor(Guid id) => Monitors.FirstOrDefault(m => m.Id == id);
}

public sealed class AppSettings {
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// On a monitor that has at least one active link, the cursor can only leave through links;
    /// everything else on its edges behaves like a wall.
    /// </summary>
    public bool BlockUnlinkedEdges { get; set; } = true;
}

/// <summary>
/// A monitor the user has seen at some point. Links refer to the profile id, never to anything
/// Windows reports, so a profile and its links survive the monitor being unplugged or re-enumerated.
/// </summary>
public sealed class MonitorProfile {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public MonitorFingerprint Fingerprint { get; set; } = new();
}

/// <summary>Everything Windows tells us about a monitor that can help recognise it again.</summary>
public sealed record MonitorFingerprint {
    public string DevicePath { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string AdapterPath { get; init; } = "";
    public int EdidManufacturer { get; init; }
    public int EdidProduct { get; init; }
    public string Serial { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>
    /// Virtual displays (spacedesk, GlideX and similar) report no EDID, and the device path Windows
    /// gives them depends on connection order, so it cannot be trusted as an identity.
    /// </summary>
    [JsonIgnore]
    public bool HasEdid => EdidManufacturer != 0 || EdidProduct != 0;
}

/// <summary>A monitor that is connected right now.</summary>
public sealed record DetectedMonitor(MonitorFingerprint Fingerprint) {
    public PixelRect Bounds => new(Fingerprint.X, Fingerprint.Y, Fingerprint.Width, Fingerprint.Height);
    public bool IsPrimary => Fingerprint is { X: 0, Y: 0 };
}

/// <summary>
/// A two-way passage between a span of one monitor edge and a span of another. Positions along the
/// source span map proportionally onto the target span, so the spans may differ in length.
/// </summary>
public sealed class Link {
    public Guid Id { get; set; } = Guid.NewGuid();
    public LinkEnd A { get; set; } = new();
    public LinkEnd B { get; set; } = new();

    /// <summary>Maps the start of A onto the end of B instead of onto its start.</summary>
    public bool Reversed { get; set; }

    public bool Enabled { get; set; } = true;

    public IEnumerable<LinkEnd> Ends {
        get {
            yield return A;
            yield return B;
        }
    }

    public LinkEnd Other(LinkEnd end) => ReferenceEquals(end, A) ? B : A;
}

public sealed class LinkEnd {
    public Guid MonitorId { get; set; }
    public Edge Edge { get; set; }

    /// <summary>Start of the span as a fraction (0..1) of the edge, measured from its top/left end.</summary>
    public double Start { get; set; }

    /// <summary>End of the span as a fraction (0..1) of the edge.</summary>
    public double End { get; set; } = 1;
}
