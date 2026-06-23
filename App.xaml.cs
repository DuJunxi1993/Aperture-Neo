using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using ApertureNeo.Cli;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;

namespace ApertureNeo;

/// <summary>
/// Application entry point. Owns the long-lived singletons
/// (<see cref="ThumbnailCache"/>, <see cref="SettingsStore"/>),
/// the SQLite native init, and the global exception handlers that
/// keep the process alive across screenshot-tool reentrancy.
/// </summary>
public partial class App : Application
{
    public static ThumbnailCache ThumbnailCache { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;

    /// <summary>
    /// DI composition root. Set in <see cref="OnStartup"/>
    /// after <see cref="AppHost.Build"/> runs. WPF UserControls
    /// that need service lookup without ctor injection read
    /// this directly (see <c>FolderTreeView</c>, <c>ThumbnailGrid</c>).
    /// </summary>
    public static IServiceProvider Host => AppHost.Services
        ?? throw new InvalidOperationException("AppHost not built — call AppHost.Build() in App.OnStartup first.");

    /// <summary>
    /// First-position argument tokens that route the process into the
    /// CLI dispatcher instead of the image viewer. We check the
    /// first arg only — System.CommandLine does the rest of the
    /// parsing inside InvokeAsync (including --help / --version,
    /// which RootCommand auto-handles).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CommandTokens =
        new(System.StringComparer.OrdinalIgnoreCase) { "ocr", "plugin-list" /*, "convert" (future) */ };

    protected override async void OnStartup(StartupEventArgs e)
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

        // P0 fix: migrate legacy settings files BEFORE AppHost.Build
        // (which used to construct SettingsStore via a factory that
        // called Load() synchronously, racing with the migration). The
        // new SettingsStore.Load() is lazy + idempotent, so the file
        // IO is deferred until the first read; we just need to
        // ensure the migration runs before any read happens.
        MigrateLegacyData();

        // Build the DI container. Throws if it can't resolve a
        // required service at this point — that's intentional: we'd
        // rather fail loudly at startup than silently produce a
        // broken viewer. SettingsStore.Load() runs lazily on the
        // first access (Favorites / Recent / IsFavorite /
        // IsPluginEnabled) so it picks up any file the migration
        // above just moved into place.
        AppHost.Build();

        // CLI dispatch: if the first arg is a registered subcommand
        // (or --help / --version), hand the full args array to
        // System.CommandLine and skip the viewer init entirely.
        // The command handler is responsible for showing its own
        // window or shutting down. This makes the CLI entry point
        // independent of the in-app OCR plugin's opt-in toggle.
        var firstArg = e.Args.FirstOrDefault();
        if (IsCliInvocation(firstArg))
        {
            var root = new RootCommand("Aperture Neo - image viewer with OCR.")
            {
                new OcrCommand(this),
                new PluginListCommand(),
                // future: new ConvertCommand(),
            };
            var exitCode = await root.InvokeAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        // Viewer mode: plugin discovery (SettingsStore already loaded
        // lazily above). The settings + thumbnail-cache instances are
        // owned by the DI container (AppHost); the static forwarders
        // below expose them to the ~28 existing call sites that
        // haven't been migrated to constructor injection yet (P1 / P2).
        // P0 fix: MigrateLegacyData() moved above AppHost.Build() so
        // its file moves happen before the first read.

        // Static forwarders — read from DI for backward compat.
        // Will be deleted in P2 once all consumers take the
        // services via ctor instead.
        SettingsStore = (SettingsStore)AppHost.Services!.GetRequiredService<ISettingsStore>();
        ThumbnailCache = (ThumbnailCache)AppHost.Services!.GetRequiredService<IThumbnailCache>();

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

        // Discover plugins from bin/Plugins/*.dll. We only LOAD the
        // assemblies (so Name/Description can be read for the menu
        // checkboxes) — Activate is NOT called, so the plugin's
        // heavy resources (ONNX models, native deps initialization)
        // stay unloaded until the user opts in via the 插件 submenu.
        // The main window then wires the checkboxes and calls
        // Activate/Deactivate based on the user's choice.
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var available = PluginLoader.Discover(pluginsDir);
        mainWindow.SetAvailablePlugins(available);
        mainWindow.RestoreEnabledPlugins();
    }

    /// <summary>
    /// True if the process should route into the CLI dispatcher.
    /// Matches the first arg against the registered command tokens
    /// OR the global --help / --version flags that RootCommand
    /// auto-handles.
    /// </summary>
    private static bool IsCliInvocation(string? firstArg)
    {
        if (string.IsNullOrEmpty(firstArg)) return false;
        if (CommandTokens.Contains(firstArg)) return true;
        return firstArg is "--help" or "-h" or "--version";
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
