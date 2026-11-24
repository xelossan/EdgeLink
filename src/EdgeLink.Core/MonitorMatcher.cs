namespace EdgeLink.Core;

public sealed record OnlineMonitor(MonitorProfile Profile, DetectedMonitor Detected) {
    public PixelRect Bounds => Detected.Bounds;
}

/// <summary>
/// Decides which saved profile each connected monitor is. Windows offers no single reliable id:
/// real monitors are identified well by EDID and device path, but virtual displays have neither,
/// so every available hint is scored and the best overall pairing wins.
/// </summary>
public static class MonitorMatcher {
    /// <summary>Pairs scoring below this are treated as different monitors.</summary>
    public const int Threshold = 30;

    public static int Score(MonitorFingerprint saved, MonitorFingerprint current)
    {
        if (saved.HasEdid != current.HasEdid) return 0;

        var strong = saved.HasEdid;
        var score = 0;

        if (strong) {
            if (saved.EdidManufacturer != current.EdidManufacturer || saved.EdidProduct != current.EdidProduct) return 0;
            score += 30;

            // a differing serial is not held against the pair: virtual displays that fake an EDID
            // (GlideX) may not keep theirs, and real twins are still separated by the bonus
            if (saved.Serial.Length > 0 && saved.Serial == current.Serial) score += 100;
        }

        var sameSize = (saved.Width == current.Width && saved.Height == current.Height)
                       || (saved.Width == current.Height && saved.Height == current.Width); // rotated
        var samePath = saved.DevicePath.Length > 0 && string.Equals(saved.DevicePath, current.DevicePath, StringComparison.OrdinalIgnoreCase);

        // without EDID the device path merely reflects connection order, so the resolution is the
        // better hint; with EDID it is the other way round
        if (samePath) score += strong ? 50 : 15;
        if (sameSize) score += strong ? 10 : 30;

        if (saved.FriendlyName.Length > 0 && saved.FriendlyName == current.FriendlyName) score += 10;
        if (saved.AdapterPath.Length > 0 && string.Equals(saved.AdapterPath, current.AdapterPath, StringComparison.OrdinalIgnoreCase)) score += 10;
        if (saved.X == current.X && saved.Y == current.Y) score += 10;

        return score;
    }

    /// <summary>
    /// Matches the connected monitors to the profiles in <paramref name="config"/>, creating profiles
    /// for monitors never seen before and refreshing the fingerprint of every matched profile.
    /// </summary>
    public static List<OnlineMonitor> Assign(AppConfig config, IReadOnlyList<DetectedMonitor> detected)
    {
        var pairs = new List<(int Score, int Profile, int Detected)>();
        for (var p = 0; p < config.Monitors.Count; p++) {
            for (var d = 0; d < detected.Count; d++) {
                var score = config.Monitors[p].AllFingerprints.Max(f => Score(f, detected[d].Fingerprint));
                if (score >= Threshold) pairs.Add((score, p, d));
            }
        }

        var assigned = new MonitorProfile?[detected.Count];
        var usedProfiles = new HashSet<int>();
        foreach (var (_, p, d) in pairs.OrderByDescending(x => x.Score).ThenBy(x => x.Profile).ThenBy(x => x.Detected)) {
            if (assigned[d] != null || !usedProfiles.Add(p)) continue;
            assigned[d] = config.Monitors[p];
        }

        var result = new List<OnlineMonitor>(detected.Count);
        for (var d = 0; d < detected.Count; d++) {
            var profile = assigned[d];
            if (profile == null) {
                profile = new MonitorProfile { Label = NextLabel(config, detected[d].Fingerprint) };
                config.Monitors.Add(profile);
            }

            UpdateFingerprint(profile, detected[d].Fingerprint);
            result.Add(new OnlineMonitor(profile, detected[d]));
        }

        return result;
    }

    /// <summary>
    /// Makes the current fingerprint the profile's main one. When the screen came in through one of
    /// its other connections, that entry is refreshed and the previous main one is kept as another.
    /// </summary>
    private static void UpdateFingerprint(MonitorProfile profile, MonitorFingerprint current)
    {
        var best = -1;
        var bestScore = Score(profile.Fingerprint, current);
        for (var i = 0; i < profile.OtherFingerprints.Count; i++) {
            var score = Score(profile.OtherFingerprints[i], current);
            if (score > bestScore) (best, bestScore) = (i, score);
        }

        if (best >= 0) profile.OtherFingerprints[best] = profile.Fingerprint;
        profile.Fingerprint = current;
    }

    /// <summary>
    /// Exchanges which physical monitors two profiles stand for; used when the automatic matching
    /// mixed up two indistinguishable displays. Labels and links stay with their profile.
    /// </summary>
    public static void SwapIdentity(MonitorProfile a, MonitorProfile b)
    {
        (a.Fingerprint, b.Fingerprint) = (b.Fingerprint, a.Fingerprint);
        (a.OtherFingerprints, b.OtherFingerprints) = (b.OtherFingerprints, a.OtherFingerprints);
    }

    /// <summary>
    /// Records that <paramref name="source"/> is another connection of the same physical screen as
    /// <paramref name="target"/>: the target takes over its fingerprints and its links, and the
    /// source profile is removed. The target keeps its name and settings.
    /// </summary>
    public static void Merge(AppConfig config, MonitorProfile source, MonitorProfile target)
    {
        if (ReferenceEquals(source, target)) return;

        // the screen is connected the way the source saw it, so that becomes the target's main fingerprint
        target.OtherFingerprints.Add(target.Fingerprint);
        target.OtherFingerprints.AddRange(source.OtherFingerprints);
        target.Fingerprint = source.Fingerprint;

        // a link between the two would now lead from the screen to itself
        config.Links.RemoveAll(l => l.A.MonitorId == source.Id && l.B.MonitorId == target.Id
                                    || l.A.MonitorId == target.Id && l.B.MonitorId == source.Id);

        foreach (var link in config.Links) {
            foreach (var end in link.Ends) {
                if (end.MonitorId == source.Id) end.MonitorId = target.Id;
            }
        }

        config.Monitors.Remove(source);
    }

    private static string NextLabel(AppConfig config, MonitorFingerprint fingerprint)
    {
        var baseName = fingerprint.FriendlyName.Length > 0 ? fingerprint.FriendlyName : "Monitor";
        if (fingerprint.FriendlyName.Length > 0 && config.Monitors.All(m => m.Label != baseName)) return baseName;

        for (var i = fingerprint.FriendlyName.Length > 0 ? 2 : 1;; i++) {
            var label = $"{baseName} {i}";
            if (config.Monitors.All(m => m.Label != label)) return label;
        }
    }
}
