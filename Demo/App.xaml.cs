using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using SQLitePCL;

namespace ApertureNeo;

public partial class App : Application
{
    public static ThumbnailCache ThumbnailCache { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;
    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "ApertureNeoLinearDemo", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("Dispatcher.UnhandledException", args.Exception);
            args.Handled = true;
        };

        Batteries_V2.Init();

        Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

        // Demo uses its own cache/settings dirs so it never collides with the main app.
        var cacheDir = Path.Combine(Path.GetTempPath(), "ApertureNeoLinearDemo", "thumbs");
        Directory.CreateDirectory(cacheDir);
        ThumbnailCache = new ThumbnailCache(Path.Combine(cacheDir, "cache.db"));

        SettingsStore = new SettingsStore();
        SettingsStore.Load();

        // Lock to Linear dark theme. No Mica — pure #08090a canvas.
        try
        {
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                Wpf.Ui.Appearance.ApplicationTheme.Dark,
                Wpf.Ui.Controls.WindowBackdropType.None,
                true);
        }
        catch { }

        var mainWindow = e.Args.Length > 0
            && File.Exists(e.Args[0])
            && FormatHelper.IsSupported(e.Args[0])
            ? new MainWindow(e.Args[0])
            : new MainWindow();

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore?.Save();
        ThumbnailCache?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {source}: {ex}\n");
        }
        catch { }
    }
}
