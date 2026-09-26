using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarMonitor.Core;
using TaskbarMonitor.Overlay;
using TaskbarMonitor.Telemetry;
using TaskbarMonitor.Theming;

namespace TaskbarMonitor.ViewModels;

public sealed record Swatch(string Name, SolidColorBrush Brush, string Hex);

public sealed record DiagnosticRow(string Name, string Value, SolidColorBrush? Color = null);

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly string[] FontCandidates =
        ["Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", "Bahnschrift", "Inter", "Cascadia Mono", "Consolas"];

    private readonly AppSettings _s;
    private readonly SettingsStore _store;
    private readonly ThemeWatcher _theme;
    private readonly TelemetryService _telemetry;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dispatcher _dispatcher;
    private bool _autostart;
    private bool _overlayChangePending;

    public event Action? OverlayChanged;
    public event Action? SnapshotUpdated;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    internal SettingsViewModel(AppSettings settings, SettingsStore store, ThemeWatcher theme, TelemetryService telemetry)
    {
        _s = settings;
        _store = store;
        _theme = theme;
        _telemetry = telemetry;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _autostart = AutostartService.IsEnabled;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _store.Save(_s);
        };

        Metrics = new ObservableCollection<MetricItemViewModel>(
            _s.Metrics.Select(m => new MetricItemViewModel(m, OnMetricsChanged, MoveMetric)));
        UpdateMoveFlags();

        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());
        OpenTaskManagerCommand = new RelayCommand(OpenTaskManager);
        OpenSettingsFolderCommand = new RelayCommand(() => OpenPath(Path.GetDirectoryName(_store.FilePath)!));
        OpenErrorLogCommand = new RelayCommand(() => OpenPath(File.Exists(ErrorLog.FilePath) ? ErrorLog.FilePath : Path.GetDirectoryName(ErrorLog.FilePath)!));
        OpenOriginalCommand = new RelayCommand(() => OpenPath("https://github.com/kil0bit-kb/kil0bit-system-monitor"));
        OpenColorSettingsCommand = new RelayCommand(() => OpenPath("ms-settings:colors"));
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);
        RefreshSourcesCommand = new RelayCommand(RefreshSources);

        FontOptions = FontCandidates.Where(IsFontAvailable).ToList();
        RefreshSources();

        _theme.Changed += OnThemeChanged;
        _telemetry.Updated += OnTelemetryUpdated;
        Snapshot = MetricLayout.DemoSnapshot();
    }

    public AppSettings Settings => _s;

    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OpenTaskManagerCommand { get; }
    public RelayCommand OpenSettingsFolderCommand { get; }
    public RelayCommand OpenErrorLogCommand { get; }
    public RelayCommand OpenOriginalCommand { get; }
    public RelayCommand OpenColorSettingsCommand { get; }
    public RelayCommand ResetAppearanceCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }

    public string Version { get; } = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public MetricsSnapshot Snapshot { get; private set; }

    public TaskbarTheme Theme => _theme.Current;

    public OverlayPalette Palette => TaskbarPalette.Build(Theme, _s);

    public TaskbarSurface ActiveSurface => TaskbarPalette.ResolveSurface(Theme, _s.ThemeOverride);

    private void OnTelemetryUpdated(MetricsSnapshot snapshot) =>
        _dispatcher.BeginInvoke(() =>
        {
            Snapshot = snapshot;
            SnapshotUpdated?.Invoke();
        });

    private void OnThemeChanged()
    {
        NotifyAppearance();
        RaiseOverlayChanged();
    }

    internal TelemetryOptions TelemetryOptions => new(
        _s.Metrics.Where(m => m.Enabled).Select(m => m.Kind).ToHashSet(),
        _s.GpuId, _s.NetworkAdapter, _s.SpaceDrive, _s.UpdateIntervalMs);

    public bool ShowOverlay
    {
        get => _s.ShowOverlay;
        set => Update(_s.ShowOverlay, value, v => _s.ShowOverlay = v);
    }

    public bool Autostart
    {
        get => _autostart;
        set
        {
            if (_autostart == value)
                return;
            try
            {
                AutostartService.Set(value);
                _autostart = value;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                ErrorLog.Write(ex);
                _autostart = AutostartService.IsEnabled;
            }
            OnPropertyChanged();
        }
    }

    public bool SnapToTaskbar
    {
        get => _s.SnapToTaskbar;
        set => Update(_s.SnapToTaskbar, value, v => _s.SnapToTaskbar = v);
    }

    public IReadOnlyList<Option<OverlayAnchor>> AnchorOptions { get; } =
    [
        new(OverlayAnchor.LeftOfTray, "Слева от области уведомлений"),
        new(OverlayAnchor.LeftEdge, "У левого края панели"),
    ];

    public OverlayAnchor Anchor
    {
        get => _s.Anchor;
        set => Update(_s.Anchor, value, v => _s.Anchor = v);
    }

    public int Offset
    {
        get => _s.Offset;
        set => Update(_s.Offset, Math.Clamp(value, 0, 2000), v => _s.Offset = v);
    }

    public bool LockPosition
    {
        get => _s.LockPosition;
        set => Update(_s.LockPosition, value, v => _s.LockPosition = v);
    }

    public bool AlwaysOnTop
    {
        get => _s.AlwaysOnTop;
        set => Update(_s.AlwaysOnTop, value, v => _s.AlwaysOnTop = v);
    }

    public bool HideOnFullscreen
    {
        get => _s.HideOnFullscreen;
        set => Update(_s.HideOnFullscreen, value, v => _s.HideOnFullscreen = v);
    }

    public IReadOnlyList<Option<int>> IntervalOptions { get; } =
    [
        new(500, "0,5 секунды"),
        new(1000, "1 секунда"),
        new(2000, "2 секунды"),
        new(3000, "3 секунды"),
        new(5000, "5 секунд"),
    ];

    public int UpdateIntervalMs
    {
        get => _s.UpdateIntervalMs;
        set
        {
            if (Update(_s.UpdateIntervalMs, value, v => _s.UpdateIntervalMs = v))
                _telemetry.Configure(TelemetryOptions);
        }
    }

    public IReadOnlyList<Option<DoubleClickAction>> DoubleClickOptions { get; } =
    [
        new(DoubleClickAction.TaskManager, "Открыть диспетчер задач"),
        new(DoubleClickAction.Settings, "Открыть настройки"),
        new(DoubleClickAction.None, "Ничего не делать"),
    ];

    public DoubleClickAction DoubleClick
    {
        get => _s.DoubleClick;
        set => Update(_s.DoubleClick, value, v => _s.DoubleClick = v);
    }

    /// <summary>Called by the overlay after the user dragged it.</summary>
    internal void SetPosition(int? offset, int? freeX, int? freeY)
    {
        if (offset is { } o)
            Offset = o;
        if (freeX is { } x && freeY is { } y && (x != _s.FreeX || y != _s.FreeY))
        {
            _s.FreeX = x;
            _s.FreeY = y;
            ScheduleSave();
        }
    }

    public ObservableCollection<MetricItemViewModel> Metrics { get; }

    public bool HasEnabledMetrics => _s.Metrics.Any(m => m.Enabled);

    private void OnMetricsChanged()
    {
        OnPropertyChanged(nameof(HasEnabledMetrics));
        _telemetry.Configure(TelemetryOptions);
        ScheduleSave();
        RaiseOverlayChanged();
    }

    private void MoveMetric(MetricItemViewModel item, int direction)
    {
        int from = Metrics.IndexOf(item);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= Metrics.Count)
            return;
        Metrics.Move(from, to);
        var entry = _s.Metrics[from];
        _s.Metrics.RemoveAt(from);
        _s.Metrics.Insert(to, entry);
        UpdateMoveFlags();
        ScheduleSave();
        RaiseOverlayChanged();
    }

    private void UpdateMoveFlags()
    {
        for (int i = 0; i < Metrics.Count; i++)
        {
            Metrics[i].CanMoveUp = i > 0;
            Metrics[i].CanMoveDown = i < Metrics.Count - 1;
        }
    }

    public IReadOnlyList<Option<string>> GpuOptions { get; private set; } = [];
    public IReadOnlyList<Option<string>> NetworkOptions { get; private set; } = [];
    public IReadOnlyList<Option<string>> DriveOptions { get; private set; } = [];

    public string GpuId
    {
        get => _s.GpuId;
        set
        {
            if (Update(_s.GpuId, value ?? "", v => _s.GpuId = v))
                _telemetry.Configure(TelemetryOptions);
        }
    }

    public string NetworkAdapter
    {
        get => _s.NetworkAdapter;
        set
        {
            if (Update(_s.NetworkAdapter, value ?? "", v => _s.NetworkAdapter = v))
                _telemetry.Configure(TelemetryOptions);
        }
    }

    public string SpaceDrive
    {
        get => _s.SpaceDrive;
        set
        {
            if (Update(_s.SpaceDrive, value ?? "C:\\", v => _s.SpaceDrive = v))
                _telemetry.Configure(TelemetryOptions);
        }
    }

    private void RefreshSources()
    {
        var gpus = GpuAdapters.Enumerate();
        var auto = GpuAdapters.Pick(gpus, null);
        GpuOptions =
        [
            new("", auto is null ? "Автоматически" : $"Автоматически ({auto.Name})"),
            .. gpus.Select(g => new Option<string>(g.Id, $"{g.Name} · {g.DedicatedMemory / 1073741824.0:0.#} ГБ")),
        ];

        NetworkOptions =
        [
            new("", "Все физические адаптеры"),
            .. NetworkSampler.ListAdapters().Select(a => new Option<string>(a.Id, $"{a.Name} — {a.Description}")),
        ];

        var drives = new List<Option<string>>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)
                {
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" {d.VolumeLabel}";
                    drives.Add(new(d.Name, $"{d.Name.TrimEnd('\\')}{label} · {d.TotalSize / 1073741824.0:0} ГБ"));
                }
            }
            catch (IOException)
            {
            }
        }
        if (drives.Count == 0)
            drives.Add(new("C:\\", "C:"));
        DriveOptions = drives;

        OnPropertyChanged(nameof(GpuOptions));
        OnPropertyChanged(nameof(NetworkOptions));
        OnPropertyChanged(nameof(DriveOptions));
        OnPropertyChanged(nameof(GpuId));
        OnPropertyChanged(nameof(NetworkAdapter));
        OnPropertyChanged(nameof(SpaceDrive));
    }

    public IReadOnlyList<Option<bool>> RowOptions { get; } =
    [
        new(true, "В две строки (как на скриншоте)"),
        new(false, "В одну строку"),
    ];

    public bool TwoRows
    {
        get => _s.TwoRows;
        set => Update(_s.TwoRows, value, v => _s.TwoRows = v);
    }

    public IReadOnlyList<Option<LabelStyle>> LabelStyleOptions { get; } =
    [
        new(LabelStyle.Full, "Полные: CPU, RAM, GPU"),
        new(LabelStyle.Compact, "Короткие: C, R, G"),
    ];

    public LabelStyle LabelStyle
    {
        get => _s.LabelStyle;
        set => Update(_s.LabelStyle, value, v => _s.LabelStyle = v);
    }

    public IReadOnlyList<Option<TaskbarThemeOverride>> ThemeOverrideOptions { get; } =
    [
        new(TaskbarThemeOverride.Auto, "Как в Windows (автоматически)"),
        new(TaskbarThemeOverride.Light, "Всегда светлая"),
        new(TaskbarThemeOverride.Dark, "Всегда тёмная"),
    ];

    public TaskbarThemeOverride ThemeOverride
    {
        get => _s.ThemeOverride;
        set => Update(_s.ThemeOverride, value, v => _s.ThemeOverride = v);
    }

    public IReadOnlyList<Option<LabelColorMode>> LabelColorOptions { get; } =
    [
        new(LabelColorMode.Accent, "Акцентный цвет Windows"),
        new(LabelColorMode.SameAsValue, "Как у значений"),
        new(LabelColorMode.Custom, "Свой цвет"),
    ];

    public LabelColorMode LabelColor
    {
        get => _s.LabelColor;
        set => Update(_s.LabelColor, value, v => _s.LabelColor = v);
    }

    public IReadOnlyList<Option<ValueColorMode>> ValueColorOptions { get; } =
    [
        new(ValueColorMode.Auto, "Белый или чёрный (автоматически)"),
        new(ValueColorMode.Accent, "Акцентный цвет Windows"),
        new(ValueColorMode.Custom, "Свой цвет"),
    ];

    public ValueColorMode ValueColor
    {
        get => _s.ValueColor;
        set => Update(_s.ValueColor, value, v => _s.ValueColor = v);
    }

    public string CustomLabelLight
    {
        get => _s.CustomLabelLight;
        set => UpdateColor(_s.CustomLabelLight, value, v => _s.CustomLabelLight = v);
    }

    public string CustomLabelDark
    {
        get => _s.CustomLabelDark;
        set => UpdateColor(_s.CustomLabelDark, value, v => _s.CustomLabelDark = v);
    }

    public string CustomValueLight
    {
        get => _s.CustomValueLight;
        set => UpdateColor(_s.CustomValueLight, value, v => _s.CustomValueLight = v);
    }

    public string CustomValueDark
    {
        get => _s.CustomValueDark;
        set => UpdateColor(_s.CustomValueDark, value, v => _s.CustomValueDark = v);
    }

    public bool LoadColors
    {
        get => _s.LoadColors;
        set => Update(_s.LoadColors, value, v => _s.LoadColors = v);
    }

    public int WarnPercent
    {
        get => _s.WarnPercent;
        set
        {
            if (Update(_s.WarnPercent, Math.Clamp(value, 10, 99), v => _s.WarnPercent = v) && CritPercent <= _s.WarnPercent)
                CritPercent = _s.WarnPercent + 1;
        }
    }

    public int CritPercent
    {
        get => _s.CritPercent;
        set
        {
            if (Update(_s.CritPercent, Math.Clamp(value, 11, 100), v => _s.CritPercent = v) && WarnPercent >= _s.CritPercent)
                WarnPercent = _s.CritPercent - 1;
        }
    }

    public int WarnTemp
    {
        get => _s.WarnTemp;
        set
        {
            if (Update(_s.WarnTemp, Math.Clamp(value, 30, 109), v => _s.WarnTemp = v) && CritTemp <= _s.WarnTemp)
                CritTemp = _s.WarnTemp + 1;
        }
    }

    public int CritTemp
    {
        get => _s.CritTemp;
        set
        {
            if (Update(_s.CritTemp, Math.Clamp(value, 31, 110), v => _s.CritTemp = v) && WarnTemp >= _s.CritTemp)
                WarnTemp = _s.CritTemp - 1;
        }
    }

    public IReadOnlyList<Option<SparklineMode>> SparklineOptions { get; } =
    [
        new(SparklineMode.Inline, "Рядом со значением"),
        new(SparklineMode.Background, "Фоном колонки"),
        new(SparklineMode.Off, "Не показывать"),
    ];

    public SparklineMode Sparklines
    {
        get => _s.Sparklines;
        set => Update(_s.Sparklines, value, v => _s.Sparklines = v);
    }

    public IReadOnlyList<string> FontOptions { get; }

    public string FontFamily
    {
        get => _s.FontFamily;
        set => Update(_s.FontFamily, value ?? "Segoe UI Variable Text", v => _s.FontFamily = v);
    }

    public IReadOnlyList<Option<TextWeight>> FontWeightOptions { get; } =
    [
        new(TextWeight.Regular, "Обычный"),
        new(TextWeight.SemiBold, "Полужирный"),
        new(TextWeight.Bold, "Жирный"),
    ];

    public TextWeight FontWeight
    {
        get => _s.FontWeight;
        set => Update(_s.FontWeight, value, v => _s.FontWeight = v);
    }

    public int ScalePercent
    {
        get => _s.ScalePercent;
        set => Update(_s.ScalePercent, Math.Clamp(value, 80, 150), v => _s.ScalePercent = v);
    }

    public int ColumnSpacing
    {
        get => _s.ColumnSpacing;
        set => Update(_s.ColumnSpacing, Math.Clamp(value, 0, 24), v => _s.ColumnSpacing = v);
    }

    public bool ShowPods
    {
        get => _s.ShowPods;
        set => Update(_s.ShowPods, value, v => _s.ShowPods = v);
    }

    public bool ShowBackground
    {
        get => _s.ShowBackground;
        set => Update(_s.ShowBackground, value, v => _s.ShowBackground = v);
    }

    private void ResetAppearance()
    {
        var d = new AppSettings();
        ThemeOverride = d.ThemeOverride;
        LabelColor = d.LabelColor;
        ValueColor = d.ValueColor;
        CustomLabelLight = d.CustomLabelLight;
        CustomLabelDark = d.CustomLabelDark;
        CustomValueLight = d.CustomValueLight;
        CustomValueDark = d.CustomValueDark;
        LoadColors = d.LoadColors;
        WarnPercent = d.WarnPercent;
        CritPercent = d.CritPercent;
        WarnTemp = d.WarnTemp;
        CritTemp = d.CritTemp;
        Sparklines = d.Sparklines;
        FontFamily = d.FontFamily;
        FontWeight = d.FontWeight;
        ScalePercent = d.ScalePercent;
        ColumnSpacing = d.ColumnSpacing;
        ShowPods = d.ShowPods;
        ShowBackground = d.ShowBackground;
    }

    public OverlayPalette DarkPreview => TaskbarPalette.Build(TaskbarSurface.Dark, Theme, _s);
    public OverlayPalette LightPreview => TaskbarPalette.Build(TaskbarSurface.Light, Theme, _s);
    public OverlayPalette AccentPreview => TaskbarPalette.Build(TaskbarSurface.Accent, Theme, _s);

    public IReadOnlyList<Swatch> AccentSwatches =>
        Theme.Accent.All.Select((c, i) => new Swatch(AccentPalette.Names[i], Frozen(c), Contrast.ToHex(c))).ToList();

    public IReadOnlyList<DiagnosticRow> Diagnostics
    {
        get
        {
            var t = Theme;
            var p = Palette;
            string surface = p.Surface switch
            {
                TaskbarSurface.Light => "светлая",
                TaskbarSurface.Accent => "в цвет акцента",
                TaskbarSurface.HighContrast => "высокая контрастность",
                _ => "тёмная",
            };
            if (_s.ThemeOverride != TaskbarThemeOverride.Auto)
                surface += " (задана вручную)";
            return
            [
                new("Режим Windows (панель задач)", t.SystemLight ? "Светлый" : "Тёмный"),
                new("Режим приложений", t.AppsLight ? "Светлый" : "Тёмный"),
                new("Акцент на «Пуске» и панели задач", t.AccentOnTaskbar ? "Включён" : "Выключен"),
                new("Эффекты прозрачности", t.Transparency ? "Включены" : "Выключены"),
                new("Высокая контрастность", t.HighContrast ? "Включена" : "Выключена"),
                new("Панель задач для оверлея", surface),
                new("Цвет подписей", $"{Contrast.ToHex(p.Label)} · контраст {p.LabelContrast:0.0}:1", Frozen(p.Label)),
                new("Цвет значений", $"{Contrast.ToHex(p.Value)} · контраст {p.ValueContrast:0.0}:1", Frozen(p.Value)),
                new("Высокая / критическая нагрузка", $"{Contrast.ToHex(p.Caution)} / {Contrast.ToHex(p.Critical)}", Frozen(p.Caution)),
            ];
        }
    }

    public string ThemeSummary => ActiveSurface switch
    {
        TaskbarSurface.Light => "Сейчас панель задач светлая — текст тёмный, подписи в тёмном оттенке акцента.",
        TaskbarSurface.Accent => "Сейчас панель задач окрашена в акцент — текст светлый.",
        TaskbarSurface.HighContrast => "Включена высокая контрастность — используются системные цвета.",
        _ => "Сейчас панель задач тёмная — текст светлый, подписи в светлом оттенке акцента.",
    };

    private void NotifyAppearance()
    {
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(Palette));
        OnPropertyChanged(nameof(ActiveSurface));
        OnPropertyChanged(nameof(DarkPreview));
        OnPropertyChanged(nameof(LightPreview));
        OnPropertyChanged(nameof(AccentPreview));
        OnPropertyChanged(nameof(AccentSwatches));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(ThemeSummary));
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    internal bool WelcomeShown => _s.WelcomeShown;

    internal void MarkWelcomeShown()
    {
        _s.WelcomeShown = true;
        _store.Save(_s);
    }

    private bool Update<T>(T current, T value, Action<T> assign, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;
        assign(value);
        OnPropertyChanged(name);
        ScheduleSave();
        NotifyAppearance();
        RaiseOverlayChanged();
        return true;
    }

    private void UpdateColor(string current, string value, Action<string> assign, [CallerMemberName] string? name = null)
    {
        if (!Contrast.TryParse(value, out var color))
        {
            OnPropertyChanged(name);
            return;
        }
        Update(current, Contrast.ToHex(color), assign, name);
    }

    // Several properties often change together (reset, thresholds): notify the overlay once.
    private void RaiseOverlayChanged()
    {
        if (_overlayChangePending)
            return;
        _overlayChangePending = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _overlayChangePending = false;
            OverlayChanged?.Invoke();
        });
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static bool IsFontAvailable(string name) =>
        name == "Inter" || Fonts.SystemFontFamilies.Any(f => f.FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
        || name.StartsWith("Segoe UI Variable", StringComparison.Ordinal) && Fonts.SystemFontFamilies.Any(f => f.Source.StartsWith("Segoe UI Variable", StringComparison.Ordinal));

    private void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(ex);
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(ex);
        }
    }

    public void Dispose()
    {
        _theme.Changed -= OnThemeChanged;
        _telemetry.Updated -= OnTelemetryUpdated;
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            _store.Save(_s);
        }
    }
}
