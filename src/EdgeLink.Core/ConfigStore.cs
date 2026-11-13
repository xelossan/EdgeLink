using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeLink.Core;

public sealed class ConfigStore(string path) {
    private static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    /// <summary>Loads the config. An unreadable file is set aside as *.broken rather than overwritten.</summary>
    public AppConfig Load()
    {
        if (!File.Exists(Path)) return new AppConfig();

        try {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path), Options) ?? new AppConfig();
            Sanitize(config);
            return config;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) {
            try {
                File.Copy(Path, Path + ".broken", overwrite: true);
            }
            catch (Exception) {
                // nothing more we can do; start with a fresh config
            }

            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        // write-then-rename so a crash mid-write cannot leave a truncated config behind
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        File.Move(temp, Path, overwrite: true);
    }

    private static void Sanitize(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        config.Monitors ??= [];
        config.Links ??= [];

        config.Monitors.RemoveAll(m => m == null);
        foreach (var monitor in config.Monitors) {
            monitor.Fingerprint ??= new MonitorFingerprint();
            monitor.Label ??= "";
        }

        var known = config.Monitors.Select(m => m.Id).ToHashSet();
        config.Links.RemoveAll(l => l?.A == null || l.B == null || !known.Contains(l.A.MonitorId) || !known.Contains(l.B.MonitorId));
    }
}
