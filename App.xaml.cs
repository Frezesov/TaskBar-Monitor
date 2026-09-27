using System.Windows;
using System.Windows.Threading;
using TaskbarMonitor.Core;
using TaskbarMonitor.Overlay;
using TaskbarMonitor.Telemetry;
using TaskbarMonitor.Theming;
using TaskbarMonitor.ViewModels;
using TaskbarMonitor.Views;

namespace TaskbarMonitor;

public partial class App : Application
{
    private SingleInstance? _instance;
    private ThemeWatcher? _theme;
    private TelemetryService? _telemetry;
    private GlobalHotkey? _hotkey;
    private SettingsViewModel? _vm;
    private OverlayWindow? _overlay;
    private SettingsWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool atLogon = e.Args.Any(a => string.Equals(a, AutostartService.StartupArgument, StringComparison.OrdinalIgnoreCase));

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            // A second launch opens the settings of the running copy (but not the one started at logon).
            if (!atLogon)
            {
                Native.AllowSetForegroundWindowAny();
                _instance.SignalFirstInstance();
            }
            Shutdown();
            return;
        }

        // A few hundred pixels of text do not need a Direct3D device; skipping it saves the device's
        // driver memory in a process that runs all day.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ErrorLog.Write((Exception)args.ExceptionObject);

        // Respect "Animation effects" off in Windows settings; template content resolves this key lazily.
        if (!SystemParameters.ClientAreaAnimation)
            Resources["MotionFast"] = new Duration(TimeSpan.Zero);

        var store = new SettingsStore();
        var settings = store.Load();
        _theme = new ThemeWatcher();
        _telemetry = new TelemetryService(new TelemetryOptions(new HashSet<MetricKind>(), "", "", "C:\\", settings.UpdateIntervalMs));
        _hotkey = new GlobalHotkey();
        _vm = new SettingsViewModel(settings, store, _theme, _telemetry, _hotkey);
        _vm.OpenSettingsRequested += ShowSettings;
        _vm.ExitRequested += ExitApp;
        _telemetry.Configure(_vm.TelemetryOptions);
        _telemetry.Start();

        _overlay = new OverlayWindow(_vm);
        _overlay.Start();

        try
        {
            AutostartService.RefreshPath();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            ErrorLog.Write(ex);
        }

        _instance.ListenForSignals(() => Dispatcher.BeginInvoke(ShowSettings));

        if (!atLogon || !_vm.WelcomeShown)
            ShowSettings();
        if (!_vm.WelcomeShown)
            _vm.MarkWelcomeShown();
    }

    private void ShowSettings()
    {
        if (_vm is null)
            return;
        if (_window is null)
        {
            _window = new SettingsWindow(_vm);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _window?.Close();
        _overlay?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _hotkey?.Dispose();
        _telemetry?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception);
        e.Handled = true;
    }
}
