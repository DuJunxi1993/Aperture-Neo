using System;
using System.IO;
using System.Windows;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using SQLitePCL;
using Wpf.Ui;

namespace ApertureNeo;

public partial class App : Application
{
    public static ThumbnailCache ThumbnailCache { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Batteries_V2.Init();

        MigrateLegacyData();

        var cacheDir = Path.Combine(Path.GetTempPath(), "ApertureNeo", "thumbs");
        Directory.CreateDirectory(cacheDir);
        ThumbnailCache = new ThumbnailCache(Path.Combine(cacheDir, "cache.db"));

        SettingsStore = new SettingsStore();
        SettingsStore.Load();

        ApertureNeo.Services.ThemeService.StartSystemThemeWatcher();
        ApertureNeo.Services.ThemeService.Apply(SettingsStore.Theme);

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
