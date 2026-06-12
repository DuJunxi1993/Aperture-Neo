using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using SQLitePCL;

namespace ApertureNeo;

public partial class App : Application
{
    public static ThumbnailCache ThumbnailCache { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global last-resort exception handler. WPF's PrintWindow / DWM
        // thumbnail APIs (used by Windows Snip, Snipaste, etc.) can
        // reenter the visual tree render path while our SKSurface is
        // mid-draw, throwing a render-thread exception that propagates
        // here. Without a handler the process exits. Swallowing the
        // exception is the best we can do — the screenshot will capture
        // the previous frame and the app stays alive. We log the
        // failure to %TEMP%/ApertureNeo/crash.log so it can be
        // diagnosed post-mortem.
        DispatcherUnhandledException += (s, args) =>
        {
            Debug.WriteLine($"[App] DispatcherUnhandledException: {args.Exception.GetType().Name}: {args.Exception.Message}");
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "ApertureNeo", "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {args.Exception}\n\n");
            }
            catch { }
            args.Handled = true;  // prevent process exit
        };

        Batteries_V2.Init();

        // v2.0: Migrate legacy data from the two pre-rename
        // app names ("ImageViewerNeo" and "HighSpeedImageViewer") to
        // the current "ApertureNeo" paths. One-shot on first run
        // after the rename — subsequent runs are no-ops.
        MigrateLegacyData();

        var cacheDir = Path.Combine(Path.GetTempPath(), "ApertureNeo", "thumbs");
        Directory.CreateDirectory(cacheDir);
        ThumbnailCache = new ThumbnailCache(Path.Combine(cacheDir, "cache.db"));

        SettingsStore = new SettingsStore();
        SettingsStore.Load();

        // The light-mode DesignTokens dictionary is loaded statically
        // in App.xaml (MergedDictionaries). There is no runtime theme
        // apply — the app is Linear light mode only (theme switching
        // was retired in Round 30; the v1.0 dark/light toggle menu was
        // removed in the merge).

        // Pick the file to open at startup, in priority order:
        //   1. command-line argument (user passed a file)
        //   2. LastOpenedImage from settings (previous session)
        //   3. none (just open the empty main window)
        string? startupFile = null;
        if (e.Args.Length > 0 && File.Exists(e.Args[0]) && FormatHelper.IsSupported(e.Args[0]))
        {
            startupFile = e.Args[0];
        }
        else if (!string.IsNullOrEmpty(SettingsStore.LastOpenedImage)
                 && File.Exists(SettingsStore.LastOpenedImage)
                 && FormatHelper.IsSupported(SettingsStore.LastOpenedImage))
        {
            startupFile = SettingsStore.LastOpenedImage;
        }

        var mainWindow = startupFile != null
            ? new MainWindow(startupFile)
            : new MainWindow();

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore?.Save();
        ThumbnailCache?.Dispose();
        base.OnExit(e);
    }

    private static void MigrateLegacyData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var temp = Path.GetTempPath();

        TryMove(
            Path.Combine(temp, "ImageViewerNeo", "thumbs", "cache.db"),
            Path.Combine(temp, "ApertureNeo", "thumbs", "cache.db"));

        TryMove(
            Path.Combine(temp, "HighSpeedImageViewer", "thumbs", "cache.db"),
            Path.Combine(temp, "ApertureNeo", "thumbs", "cache.db"));

        TryMove(
            Path.Combine(appData, "ImageViewerNeo", "settings.json"),
            Path.Combine(appData, "ApertureNeo", "settings.json"));

        TryMove(
            Path.Combine(appData, "HighSpeedImageViewer", "settings.json"),
            Path.Combine(appData, "ApertureNeo", "settings.json"));
    }

    private static void TryMove(string src, string dst)
    {
        try
        {
            if (!File.Exists(src) || File.Exists(dst)) return;
            var dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
            File.Move(src, dst);

            var srcDir = Path.GetDirectoryName(src);
            TryRemoveEmptyDir(srcDir);
            if (srcDir != null) TryRemoveEmptyDir(Path.GetDirectoryName(srcDir));
        }
        catch
        {
        }
    }

    private static void TryRemoveEmptyDir(string? dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch
        {
        }
    }
}
