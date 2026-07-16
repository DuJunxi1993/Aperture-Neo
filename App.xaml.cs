using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using ApertureNeo.Cli;
using ApertureNeo.Helpers;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;
using ApertureNeo.Views;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;

namespace ApertureNeo;

/// <summary>
/// Application entry point. Owns the SQLite native init and
/// the global exception handlers that keep the process alive
/// across screenshot-tool reentrancy. Long-lived services
/// (SettingsStore, ThumbnailCache, NavigationService, etc.)
/// are owned by <see cref="AppHost"/>; consumers resolve them
/// from <see cref="AppHost.Services"/> via field initializers
/// or ctor injection.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// DI composition root. Set in <see cref="OnStartup"/>
    /// after <see cref="AppHost.Build"/> runs. WPF UserControls
    /// that need service lookup without ctor injection read
    /// this directly (see FolderTreeView, ThumbnailGrid).
    /// </summary>
    public static IServiceProvider Host => AppHost.Services
        ?? throw new InvalidOperationException("AppHost not built — call AppHost.Build() in App.OnStartup first.");

    /// <summary>Process-level single-instance gate. Held by the
    /// primary instance for the lifetime of the process; null
    /// for secondary instances that have already forwarded
    /// and shut down. Disposed in <see cref="OnExit"/>.</summary>
    private SingleInstance? _instance;

    /// <summary>
    /// First-position argument tokens that route the process into the
    /// CLI dispatcher instead of the image viewer. Used as a fast
    /// first-arg check; System.CommandLine does the rest of the
    /// parsing inside InvokeAsync (including --help / --version and
    /// any long-form subcommand like --plugin-list that doesn't
    /// match a token here).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CommandTokens =
        new(System.StringComparer.OrdinalIgnoreCase) { "ocr", "plugin-list", "help" /*, "convert" (future) */ };

    // P2 fix: WinExe (ApertureNeo.csproj: <OutputType>WinExe</OutputType>)
    // doesn't allocate a console on launch — Console.Out writes to a
    // null stream by default. When the user runs the .exe from a parent
    // terminal (PowerShell / cmd.exe / Windows Terminal), the parent
    // console is NOT automatically inherited (because the child is a
    // GUI-mode process), so System.CommandLine's IConsole output goes
    // to the void. AttachConsole(ATTACH_PARENT_PROCESS) hooks the child
    // process up to the parent's console so output reaches the terminal.
    // Returns false when the parent has no console (Explorer double-
    // click, Task Scheduler, service host) — in that case we just
    // continue as a GUI app with no console, which is the intended
    // WinExe behavior.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Single-instance gate. Must run before EVERYTHING else —
        // before AppHost.Build (which constructs the SQLite
        // ThumbnailCache), before the tray icon, before
        // GlobalHotkeyService.Initialize (which claims a hotkey
        // via Win32 RegisterHotKey). If another instance of
        // ApertureNeo.exe is already running in this Windows
        // session, we forward our argv to it (so a second
        // `ApertureNeo.exe C:\path\to\image.jpg` still opens
        // the image in the running instance) and then exit
        // before doing any of the heavy startup work.
        //
        // We do this BEFORE AttachConsole because the secondary
        // instance may want to print to a console (e.g. for
        // diagnostics), and AFTER the WPF Application is
        // initialised by `base.OnStartup` (which the async
        // OnStartupCore calls). To do it cleanly we need
        // Application.Current to be non-null, which it is by
        // the time OnStartup is invoked.
        try
        {
            _instance = SingleInstance.EnsurePrimary();
            if (!_instance.IsPrimary)
            {
                // Secondary. Push args to primary and exit.
                // Skip AttachConsole rebinding and the entire
                // OnStartupCore — the running primary owns
                // the tray icon, the hotkey slot, and the
                // SQLite cache. We just need to talk to it.
                bool forwarded = _instance.ForwardArgsAndExit(e.Args);
                // Whether or not forwarding succeeded, the
                // right thing to do is exit. The primary
                // handles the args (or ignores them if we
                // failed to forward).
                DebugLog.Write("App",
                    forwarded
                        ? "secondary instance: args forwarded, shutting down"
                        : "secondary instance: forward failed, shutting down anyway");
                _instance.Dispose();
                _instance = null;
                Shutdown(0);
                return;
            }
            // Primary: wire the forwarded-args handler now so
            // it's ready by the time a secondary forwards.
            _instance.ArgsReceived += OnSecondaryArgsReceived;
        }
        catch (Exception ex)
        {
            // Never let the single-instance layer prevent
            // the app from starting — fall through to the
            // normal startup path if the gate itself throws
            // (corrupted ACL on the mutex name, etc.).
            DebugLog.Write("App",
                $"SingleInstance gate threw: {ex.GetType().Name}: {ex.Message}");
        }

        // P2 fix: attach to the parent process's console (if any)
        // and re-bind Console.Out/Error so CLI invocations
        // (`--help`, `plugin-list`, `ocr …`, etc.) actually reach
        // the terminal. See the AttachConsole P/Invoke comment for
        // the WinExe background. Must run before OnStartupCore so
        // the rebinding is in place when IsCliInvocation dispatches
        // to RunCliAsync → root.InvokeAsync → IConsole.Write.
        // AttachConsole is a no-op when the parent has no console
        // (e.g. Explorer double-click), so this is safe to call
        // unconditionally for every launch.
        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            // After AttachConsole succeeds, the OS-level stdout /
            // stderr handles now point to the parent console.
            // .NET's cached Console.Out / Console.Error still
            // point at the original (closed) handle, so writes
            // would still go nowhere. Re-open via
            // OpenStandardOutput / OpenStandardError (which calls
            // GetStdHandle and returns a stream over the NEW
            // handle) and re-bind via SetOut / SetError. AutoFlush
            // because we want each line visible before the
            // process exits via Shutdown.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }

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
        // Use a deferred root reference for HelpCommand: the
        // HelpCommand itself lives INSIDE the RootCommand, so we
        // cannot pass `root` in the constructor (chicken-and-egg).
        // The provider closure captures the local `root` variable
        // by reference and resolves it after RootCommand is fully
        // constructed. When HelpCommand's handler runs, the root
        // is available.
        Command? rootRef = null;
        var ocr = new OcrCommand(this);
        var pluginList = new PluginListCommand();
        var help = new HelpCommand(() => rootRef!);
        var root = new RootCommand(
            "Aperture Neo - image viewer with OCR.\n\n" +
            "USAGE:\n" +
            "  ApertureNeo [command] [options]\n\n" +
            "EXAMPLES:\n" +
            "  ApertureNeo                          Launch the image viewer (default)\n" +
            "  ApertureNeo ocr photo.jpg            Extract text from photo.jpg to clipboard\n" +
            "  ApertureNeo ocr -g photo.jpg         OCR with the result window\n" +
            "  ApertureNeo plugin-list              List installed plugins\n" +
            "  ApertureNeo help <command>           Show detailed help for a command\n" +
            "  ApertureNeo help                     Show this overview\n\n" +
            "CONFIGURATION:\n" +
            "  Plugins directory  Plugins/  (next to the executable)\n" +
            "  OCR ONNX models    Plugins.Ocr.Core/Assets/models/paddleocr/*.onnx\n" +
            "  User settings      %APPDATA%\\ApertureNeo\\settings.json\n" +
            "  Thumbnail cache    %TEMP%\\ApertureNeo\\thumbs\\cache.db\n\n" +
            "EXIT CODES (CLI subcommands):\n" +
            "  0  Success\n" +
            "  1  Command failed (OCR error, plugin error, etc.)\n" +
            "  2  Invalid arguments")
        {
            help,
            ocr,
            pluginList,
            // future: new ConvertCommand(),
        };
        rootRef = root;
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

        // The light-mode DesignTokens dictionary is loaded statically
        // in App.xaml (MergedDictionaries). There is no runtime theme
        // apply — the app is Linear light mode only (theme switching
        // was retired in Round 30; the v1.0 dark/light toggle menu was
        // removed in the merge).

        // Pick the file to open at startup, in priority order:
        //   1. command-line argument (user passed a file)
        //   2. LastOpenedImage from settings (previous session)
        //   3. none (just open the empty main window)
        // ISettingsStore is resolved from DI here because the
        // startup-file check runs before any window is constructed
        // (and therefore before any field-initializer DI lookup has
        // happened). The service is a singleton so this is the
        // only call site outside MainWindow that needs the instance.
        var settings = AppHost.Services!.GetRequiredService<ISettingsStore>();
        string? startupFile = null;
        if (e.Args.Length > 0 && File.Exists(e.Args[0]) && FormatHelper.IsSupported(e.Args[0]))
        {
            startupFile = e.Args[0];
        }
        else if (!string.IsNullOrEmpty(settings.LastOpenedImage)
                 && File.Exists(settings.LastOpenedImage)
                 && FormatHelper.IsSupported(settings.LastOpenedImage))
        {
            startupFile = settings.LastOpenedImage;
        }

        var mainWindow = startupFile != null
            ? new MainWindow(startupFile)
            : new MainWindow();

        mainWindow.Show();

        // First-run onboarding (Stage 2 + decision B1+B2): shown once
        // after install / upgrade. The user's choices write straight
        // into SettingsStore; the registry write for auto-start
        // happens later (Stage 7 — AutoStartService). The dialog is
        // modal so the main window stays unresponsive until dismissed.
        // CenterScreen positioning lands it on the same screen as the
        // main window without needing Owner (Owner would block Show).
        if (!settings.FirstRunShown)
        {
            var firstRun = new Views.FirstRunDialog
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = mainWindow,
            };
            firstRun.ShowDialog();

            // Persist the choices regardless of OK / 稍后 — the
            // dialog's IsChecked bindings already reflect the user's
            // final state.
            settings.FirstRunShown = true;
            settings.AutoStart = firstRun.EnableAutoStart;
            settings.CloseToTray = firstRun.EnableCloseToTray;
            settings.Save();
        }

        // Tray-resident lifecycle (Stage 1): bring up the system
        // tray icon once the main window is on screen. The icon
        // stays alive even when the user closes the window to the
        // tray; the only way to exit the process is via the tray
        // menu's "退出" item (or the future Ctrl+Alt+Q shortcut).
        var tray = AppHost.Services!.GetRequiredService<TrayService>();

        // Capture service (Stage 5): in-process screenshot flow.
        // The tray's 截图 / OCR menu items call into this directly;
        // the screenshot plugin's hotkeys do the same via
        // IPluginContext.GetService<CaptureService>().
        var captureService = AppHost.Services!.GetRequiredService<CaptureService>();

        WireTray(tray, mainWindow, captureService);
        tray.Show();

        // Global hotkey hook (Stage 3): attach the WM_HOTKEY message
        // hook to the main window's HWND. Must happen after Show so
        // the HWND exists. Plugins call shortcutService.Register(...)
        // during their Activate() (which runs in RestoreEnabledPlugins
        // below) and the callbacks fire from here on.
        var shortcutService = AppHost.Services!.GetRequiredService<IShortcutService>();
        if (shortcutService is GlobalHotkeyService ghk)
            ghk.Initialize(mainWindow);

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

        // First-run bootstrap: if the user has never explicitly
        // enabled or disabled a plugin (the enabled list is
        // empty), opt them into all discovered plugins so the
        // global hotkeys (Ctrl+Alt+A etc.) work out of the box.
        // After the first run the list is non-empty and this
        // branch is a no-op — the user's subsequent choices
        // are respected.
        if (available.Count > 0)
        {
            var settingsStore = AppHost.Services!.GetRequiredService<ISettingsStore>();
            if (!settingsStore.HasAnyExplicitPluginChoice())
            {
                DebugLog.Write("App",
                    "first run: enabling all discovered plugins by default");
                foreach (var p in available)
                    settingsStore.SetPluginEnabled(p.Name, true);
            }
        }

        mainWindow.RestoreEnabledPlugins();
    }

    /// <summary>
    /// Connect the tray service's events to the main window. The
    /// tray stays alive while the main window is hidden; double-click
    /// or "显示主窗口" re-shows it. "退出" terminates the process
    /// (the only path that does so — closing the window via its
    /// own close button just hides, see MainWindow.OnClosingRouteToTray).
    /// </summary>
    private static void WireTray(TrayService tray, MainWindow mainWindow, CaptureService captureService)
    {
        tray.ShowMainRequested += (_, _) =>
        {
            // Un-hide the main window when the user picks
            // "显示主窗口" from the tray. Idempotent.
            if (!mainWindow.IsVisible) mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;
            mainWindow.Focus();
        };

        tray.OpenSettingsRequested += (_, _) =>
        {
            OpenSettingsWindow(mainWindow);
        };

        tray.CaptureAreaRequested += (_, _) =>
        {
            // CaptureService marshals the flow to the WPF UI
            // thread internally, so this handler doesn't need an
            // explicit Dispatcher.BeginInvoke. Calling
            // RunCaptureAreaAsync from the WinForms worker thread
            // is safe — the first thing CaptureService does is
            // hop onto Application.Current.Dispatcher.
            _ = captureService.RunCaptureAreaAsync();
        };

        tray.CaptureOcrRequested += (_, _) =>
        {
            // Same as CaptureAreaRequested: CaptureService handles
            // thread marshaling internally.
            _ = captureService.RunCaptureAreaOcrAsync();
        };

        tray.ExitRequested += (_, _) =>
        {
            // Truly exit the process. TrayService gets disposed in
            // App.OnExit alongside the rest of the singletons. The
            // CloseToTray setting stays true (persisted for the
            // next session), but we temporarily force it false so
            // MainWindow.OnClosingRouteToTray doesn't intercept the
            // shutdown close and hide instead of exiting.
            mainWindow.ForceExitOnClose = true;
            Application.Current.Shutdown();
        };
    }

    /// <summary>
    /// Handle argv forwarded from a secondary <c>ApertureNeo.exe</c>
    /// instance via the <see cref="SingleInstance"/> named pipe.
    /// Already marshalled to the WPF UI thread by the pipe loop.
    ///
    /// Two responsibilities:
    /// <list type="bullet">
    ///   <item>If the main window is hidden (close-to-tray), show
    ///         and activate it so the user can see the result.</item>
    ///   <item>If <paramref name="args"/> contains a supported
    ///         file path, navigate the viewer to it (mirrors
    ///         <see cref="RunViewerAsync"/>'s startup-file
    ///         logic).</item>
    /// </list>
    /// </summary>
    private void OnSecondaryArgsReceived(string[] args)
    {
        try
        {
            DebugLog.Write("App",
                $"OnSecondaryArgsReceived: {args.Length} arg(s) — {(args.Length > 0 ? string.Join(" | ", args) : "(empty)")}");

            var mainWindow = ApertureNeo.MainWindow.Instance;
            if (mainWindow != null)
            {
                // Restore the main window if it was closed to tray.
                // Use the same Show + WindowState.Normal pattern as
                // the tray menu's "显示主窗口" handler.
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
                mainWindow.Focus();
            }

            // If the secondary was launched with a file path,
            // navigate the existing viewer to it. Reuses the same
            // FormatHelper check as the startup path.
            string? targetFile = null;
            foreach (var a in args)
            {
                if (File.Exists(a) && FormatHelper.IsSupported(a))
                {
                    targetFile = a;
                    break;
                }
            }
            if (targetFile != null)
            {
                DebugLog.Write("App",
                    $"OnSecondaryArgsReceived: navigating to {targetFile}");
                try
                {
                    var nav = AppHost.Services?.GetService<INavigationService>();
                    nav?.NavigateTo(targetFile);
                }
                catch (Exception ex)
                {
                    DebugLog.Write("App",
                        $"navigation failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("App",
                $"OnSecondaryArgsReceived threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Show the settings window modally against the main window.
    /// Single-instance: if already open, focus the existing one
    /// rather than stacking duplicates. Stage 8 entry point.
    /// Public so the title bar's "设置..." menu item and the
    /// tray's settings menu can both reach it.
    /// </summary>
    private static SettingsWindow? _openSettings;
    public static void OpenSettingsWindow(Window owner)
    {
        if (_openSettings != null && _openSettings.IsLoaded)
        {
            _openSettings.Activate();
            return;
        }
        var sp = AppHost.Services!;
        // Fully-qualify: Application.MainWindow is a property
        // of the WPF base class, ApertureNeo.MainWindow is our
        // type. Without the namespace prefix the compiler
        // treats MainWindow.Instance as Application.MainWindow
        // (which is a non-static instance property).
        var mainWindow = ApertureNeo.MainWindow.Instance
            ?? throw new InvalidOperationException(
                "MainWindow.Instance not set; OpenSettingsWindow must be called after MainWindow is constructed.");
        var vm = new SettingsViewModel(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IShortcutService>(),
            mainWindow.PluginShell ?? throw new InvalidOperationException(
                "PluginShell not attached; call AttachShell on MainWindow before OpenSettingsWindow."),
            sp.GetRequiredService<ITheme>());
        _openSettings = new SettingsWindow(vm)
        {
            Owner = owner,
        };
        _openSettings.Closed += (_, _) => _openSettings = null;
        _openSettings.ShowDialog();
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
        // Save the settings + dispose the SQLite-backed thumbnail
        // cache. We resolve from the AppHost service provider so
        // the exit path doesn't depend on any window instance still
        // being alive (which is the case for the Shutdown(0) /
        // unhandled-exception paths that go through here too).
        if (AppHost.Services is { } sp)
        {
            (sp.GetService<ISettingsStore>() as SettingsStore)?.Save();
            (sp.GetService<IThumbnailCache>() as ThumbnailCache)?.Dispose();
        // TrayService owns the NotifyIcon. Dispose removes it
        // from the system tray; otherwise the icon would linger
        // for ~10s after process exit (Windows uses the icon's
        // owner process for shell notifications).
        (sp.GetService<TrayService>())?.Dispose();
        // GlobalHotkeyService releases the Win32 hotkey slots.
        (sp.GetService<IShortcutService>() as IDisposable)?.Dispose();
        }
        // Single-instance gate: release the named mutex so a
        // future launch can become primary. Runs even when
        // AppHost.Services is null (secondary-instance early
        // exit path: _instance is already disposed there, so
        // this is a no-op).
        try { _instance?.Dispose(); } catch { }
        _instance = null;
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

        // The SettingsStore DI factory in AppHost is lazy (it calls
        // s.Load() the first time anyone requests the service, which
        // is MainWindow's field initializer). At this point in
        // startup AppHost hasn't been built, and no consumer has
        // resolved ISettingsStore yet, so there's no pre-loaded cache
        // to invalidate — the first Load() call will see the
        // just-migrated file at its new path. No Reload() needed.
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