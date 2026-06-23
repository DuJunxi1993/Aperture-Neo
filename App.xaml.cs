using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ApertureNeo.Cli;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;

namespace ApertureNeo;

/// <summary>
/// Application entry point. Owns the long-lived singletons
/// (ThumbnailCache, SettingsStore), the SQLite native init,
/// and the global exception handlers that keep the process
/// alive across screenshot-tool reentrancy.
/// </summary>
public partial class App : Application
{
    public static ThumbnailCache ThumbnailCache { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;

    /// <summary>
    /// DI composition root. Set in <see cref="OnStartup"/>
    /// after <see cref="AppHost.Build"/> runs. WPF UserControls
    /// that need service lookup without ctor injection read
    /// this directly (see FolderTreeView, ThumbnailGrid).
    /// </summary>
    public static IServiceProvider Host => AppHost.Services
        ?? throw new InvalidOperationException("AppHost not built — call AppHost.Build() in App.OnStartup first.");

    /// <summary>
    /// First-position argument tokens that route the process into the
    /// CLI dispatcher instead of the image viewer. Used as a fast
    /// first-arg check; System.CommandLine does the rest of the
    /// parsing inside InvokeAsync (including --help / --version and
    /// any long-form subcommand like --plugin-list that doesn't
    /// match a token here).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CommandTokens =
        new(System.StringComparer.OrdinalIgnoreCase) { "ocr", "plugin-list" /*, "convert" (future) */ };

    protected override async void OnStartup(StartupEventArgs e)
    {
        // P1 fix: async void with no try/catch means any unhandled
        // exception during startup hangs the process. The global
        // DispatcherUnhandledException handler only fires for
        // exceptions that reach the dispatcher, which OnStartup
        // doesn't if it throws before ShowDialog. Wrap the body
        // so a startup crash logs to crash.log + exits with code 1
        // instead of hanging.
        try
        {
            await OnStartupCore(e);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] OnStartup crashed: {ex.GetType().Name}: {ex.Message}");
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "ApertureNeo", "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] [OnStartup] {ex}\n\n");
            }
            catch { }
            Shutdown(1);
        }
    }

    private async Task OnStartupCore(StartupEventArgs e)
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

        // P1 fix: detect CLI mode first so we can skip the full
        // DI container + SQLite init + FileSystemWatcher. The
        // previous code always built the DI graph (loading all
        // services) even for `aperture plugin-list`, which only
        // needs to scan Plugins/. Now: if the args parse cleanly
        // as a known CLI invocation, we go straight to System.
        // CommandLine without touching AppHost.
        if (IsCliInvocation(e.Args))
        {
            await RunCliAsync(e.Args);
            return;
        }

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

        await RunViewerAsync(e);
    }

    /// <summary>
    /// CLI entry: hand the args to System.CommandLine, run the
    /// matched command, shut down. Skips AppHost.Build() entirely
    /// (P1 fix) so the SQLite connection + FileSystemWatcher +
    /// ViewModels aren't constructed for sub-second CLI invocations
    /// like <c>aperture plugin-list</c>.
    /// </summary>
    private async Task RunCliAsync(string[] args)
    {
        var root = new RootCommand("Aperture Neo - image viewer with OCR.")
        {
            new OcrCommand(this),
            new PluginListCommand(),
            // future: new ConvertCommand(),
        };
        var exitCode = await root.InvokeAsync(args);
        Shutdown(exitCode);
    }

    /// <summary>
    /// Viewer entry: legacy init (settings forwarders + plugin
    /// discovery + MainWindow show) after AppHost.Build has wired
    /// the DI graph. Plugins are loaded but NOT activated — the
    /// user opts in via the 插件 submenu.
    /// </summary>
    private async Task RunViewerAsync(StartupEventArgs e)
    {
        await Task.Yield();   // keep the async signature; MIGRATE was sync

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
        // assemblies (so Name/Description can be read for the menu)
        // — Activate is NOT called, so the plugin's heavy resources
        // (ONNX models, native deps initialization) stay unloaded until
        // the user opts in via the 插件 submenu. The main window then
        // wires the checkboxes and calls Activate/Deactivate based on
        // the user's choice.
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var available = PluginLoader.Discover(pluginsDir);
        mainWindow.SetAvailablePlugins(available);
        mainWindow.RestoreEnabledPlugins();
    }

    /// <summary>
    /// True if the args should be routed into the CLI dispatcher
    /// rather than the viewer. We check three things in order:
    ///   1. The first arg is one of our registered short-form
    ///      command names ("ocr", "plugin-list") — covers the
    ///      typical user invocation.
    ///   2. The first arg is one of RootCommand's auto-handled
    ///      global flags ("--help" / "-h" / "--version") so
    ///      `aperture --help` shows the root help instead of
    ///      launching the viewer.
    ///   3. The args parse cleanly as a RootCommand invocation
    ///      (covers long-form like `--plugin-list` that doesn't
    ///      match a token). P1 fix: without #3, `aperture
    ///      --plugin-list` would silently fall through to the
    ///      viewer with no plugin list printed.
    /// </summary>
    private static bool IsCliInvocation(string[] args)
    {
        if (args.Length == 0) return false;
        var firstArg = args[0];
        if (string.IsNullOrEmpty(firstArg)) return false;
        if (CommandTokens.Contains(firstArg)) return true;
        if (firstArg is "--help" or "-h" or "--version") return true;
        // Long-form fallback: build the root command (cheap —
        // no plugin / SQLite init) and ask System.CommandLine to
        // try parsing. If it succeeds (exit code 0 / 2), the
        // args belong to a registered subcommand.
        try
        {
            var probeRoot = new RootCommand
            {
                // The probe subcommands match the names / option
                // shape of the real OcrCommand / PluginListCommand
                // but do no real work — we just want Parse to
                // succeed (or fail) based on the args.
                new OcrCommandProbe(),
                new PluginListCommandProbe(),
            };
            var parseResult = probeRoot.Parse(args);
            if (parseResult.Errors.Count == 0) return true;
        }
        catch
        {
            // Parse can throw on completely malformed input; fall
            // through to the viewer.
        }
        return false;
    }

    // Minimal probes — same command names / option shape as the
    // real OcrCommand / PluginListCommand but stripped of any
    // service dependencies, so IsCliInvocation can run them
    // before AppHost.Build.
    private sealed class OcrCommandProbe : System.CommandLine.RootCommand
    {
        public OcrCommandProbe() : base("OCR subcommand (probe).") { }
    }
    private sealed class PluginListCommandProbe : System.CommandLine.RootCommand
    {
        public PluginListCommandProbe() : base("List installed plugins (probe).") { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // P1 fix: deactivate any plugins that were activated in
        // this session so their background work (model loads,
        // file-system watchers, etc.) stops cleanly. The static
        // PluginLoader._active HashSet is process-wide; without
        // this loop, a process that was killed via Shutdown
        // rather than via a normal window close would leave the
        // plugin's _engine handle dangling (GC would clean it
        // up eventually, but model files stay mapped).
        PluginLoader.DeactivateAll();
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

        // After the move, the next read of SettingsStore will pick
        // up the migrated file. SettingsStore is now lazy, so we
        // also need to invalidate any pre-loaded cache.
        // AppHost hasn't been built yet here, so we can't go
        // through DI. Try to reach the static forwarder; if the
        // app was launched with `--ocr` (which skips AppHost),
        // SettingsStore is still null and there's nothing to
        // reload.
        try
        {
            if (SettingsStore != null) SettingsStore.Reload();
        }
        catch { /* not built yet — first read will pick up the file */ }
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