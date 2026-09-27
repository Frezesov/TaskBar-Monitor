using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarMonitor.Core;

public enum MetricKind { Cpu, Ram, Gpu, GpuTemp, Vram, NetUp, NetDown, DiskActivity, DiskSpace }

public enum OverlayAnchor { LeftOfTray, LeftEdge }

public enum LabelColorMode { Accent, SameAsValue, Custom }

public enum ValueColorMode { Auto, Accent, Custom }

public enum TaskbarThemeOverride { Auto, Light, Dark }

public enum SparklineMode { Off, Inline, Background }

public enum LabelStyle { Full, Compact }

public enum TextWeight { Regular, SemiBold, Bold }

public enum DoubleClickAction { TaskManager, Settings, None }

public sealed class MetricEntry
{
    public MetricKind Kind { get; set; }
    public bool Enabled { get; set; }
}

public sealed class AppSettings
{
    public bool ShowOverlay { get; set; } = true;
    public bool SnapToTaskbar { get; set; } = true;
    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.LeftOfTray;
    public int Offset { get; set; } = 8;
    public int FreeX { get; set; } = int.MinValue;
    public int FreeY { get; set; } = int.MinValue;
    public bool LockPosition { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public int UpdateIntervalMs { get; set; } = 1000;
    public DoubleClickAction DoubleClick { get; set; } = DoubleClickAction.TaskManager;
    public bool ShowTooltip { get; set; } = true;

    public List<MetricEntry> Metrics { get; set; } = DefaultMetrics();
    public string GpuId { get; set; } = "";
    public string NetworkAdapter { get; set; } = "";
    public string SpaceDrive { get; set; } = "C:\\";
    public bool TwoRows { get; set; } = true;
    public LabelStyle LabelStyle { get; set; } = LabelStyle.Full;

    public TaskbarThemeOverride ThemeOverride { get; set; } = TaskbarThemeOverride.Auto;
    public LabelColorMode LabelColor { get; set; } = LabelColorMode.Accent;
    public ValueColorMode ValueColor { get; set; } = ValueColorMode.Auto;
    public string CustomLabelLight { get; set; } = "#005FB8";
    public string CustomLabelDark { get; set; } = "#60CDFF";
    public string CustomValueLight { get; set; } = "#1B1B1B";
    public string CustomValueDark { get; set; } = "#FFFFFF";
    public bool LoadColors { get; set; } = true;
    public int WarnPercent { get; set; } = 75;
    public int CritPercent { get; set; } = 90;
    public int WarnTemp { get; set; } = 75;
    public int CritTemp { get; set; } = 85;
    public SparklineMode Sparklines { get; set; } = SparklineMode.Inline;
    public string FontFamily { get; set; } = OverlayFonts.Default;
    public TextWeight FontWeight { get; set; } = TextWeight.SemiBold;
    public int ScalePercent { get; set; } = 100;
    public int ColumnSpacing { get; set; } = 6;
    public bool ShowPods { get; set; } = true;
    public bool ShowBackground { get; set; }

    public bool CheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string LatestVersion { get; set; } = "";

    public bool WelcomeShown { get; set; }

    public static List<MetricEntry> DefaultMetrics() =>
    [
        new() { Kind = MetricKind.Cpu, Enabled = true },
        new() { Kind = MetricKind.Ram, Enabled = true },
        new() { Kind = MetricKind.Gpu, Enabled = true },
        new() { Kind = MetricKind.GpuTemp, Enabled = true },
        new() { Kind = MetricKind.NetUp, Enabled = false },
        new() { Kind = MetricKind.NetDown, Enabled = false },
        new() { Kind = MetricKind.Vram, Enabled = false },
        new() { Kind = MetricKind.DiskActivity, Enabled = false },
        new() { Kind = MetricKind.DiskSpace, Enabled = false },
    ];

    // Repairs values from a hand-edited or older settings file.
    public void Normalize()
    {
        var seen = new HashSet<MetricKind>();
        Metrics = Metrics.Where(m => Enum.IsDefined(m.Kind) && seen.Add(m.Kind)).ToList();
        foreach (var missing in DefaultMetrics().Where(m => !seen.Contains(m.Kind)))
            Metrics.Add(new MetricEntry { Kind = missing.Kind, Enabled = false });

        Offset = Math.Clamp(Offset, 0, 2000);
        UpdateIntervalMs = Math.Clamp(UpdateIntervalMs, 500, 5000);
        WarnPercent = Math.Clamp(WarnPercent, 10, 99);
        CritPercent = Math.Clamp(CritPercent, WarnPercent + 1, 100);
        WarnTemp = Math.Clamp(WarnTemp, 30, 109);
        CritTemp = Math.Clamp(CritTemp, WarnTemp + 1, 110);
        ScalePercent = Math.Clamp(ScalePercent, 80, 150);
        ColumnSpacing = Math.Clamp(ColumnSpacing, 0, 24);
        if (string.IsNullOrWhiteSpace(FontFamily))
            FontFamily = OverlayFonts.Default;
    }
}

internal sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore() => FilePath = ResolvePath();

    public string FilePath { get; }

    // Portable: next to the exe when that folder is writable, otherwise in %APPDATA%.
    private static string ResolvePath()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var portable = Path.Combine(exeDir, FileName);
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarMonitor", FileName);

        if (File.Exists(portable)) return portable;
        if (File.Exists(roaming)) return roaming;
        return CanWrite(exeDir) ? portable : roaming;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }
}
