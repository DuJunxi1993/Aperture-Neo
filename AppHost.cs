using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using ApertureNeo.Controls;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;

namespace ApertureNeo;

/// <summary>
/// Composition root. Builds the <see cref="IServiceProvider"/> once
/// at <see cref="App.OnStartup"/>, registers every long-lived
/// service in the DI container, and exposes the root <see cref="Host"/>
/// for WPF-created UserControls that need to look up services via
/// constructor-less instantiation.
///
/// DI completeness note: every long-lived service the main window
/// uses — SettingsStore, ThumbnailCache, IUiState, INavigationService,
/// ITheme, SlideshowService, ThumbnailLoadCoordinator — is registered
/// here. MainWindow resolves them via field initializers that read
/// <see cref="Services"/>. The old <c>App.SettingsStore</c> /
/// <c>App.ThumbnailCache</c> static forwarders were removed in a
/// cleanup pass; the only consumers (MainWindow + its partials) now
/// take the services via DI like the VMs do.
/// </summary>
public static class AppHost
{
    private static IServiceProvider? _services;

    /// <summary>
    /// The composed service provider. Null before <see cref="Build"/>
    /// runs (e.g. design-time XAML preview) and after the app
    /// shuts down. WPF UserControls that need DI lookup call
    /// <c>AppHost.Services?.GetService&lt;...&gt;()</c> from a
    /// fallback constructor.
    /// </summary>
    public static IServiceProvider? Services => _services;

    /// <summary>
    /// Build the service provider. Called once from
    /// <c>App.OnStartup</c> after the global exception handler
    /// and SQLite init, but before any window or service consumer
    /// is created. The returned provider is stored on
    /// <see cref="Services"/> for static lookups.
    /// </summary>
    /// <param name="configure">Optional pre-build hook to add
    /// plugin services (P4) or test doubles.</param>
    public static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        if (_services != null) return _services;

        var services = new ServiceCollection();

        // -- Singletons -----------------------------------------------
        // SettingsStore + ThumbnailCache are stateful and process-
        // scoped (the SQLite connection must outlive any single
        // window). They live for the entire process lifetime.
        services.AddSingleton<ISettingsStore>(sp =>
        {
            var s = new SettingsStore();
            s.Load();
            return s;
        });
        services.AddSingleton<IThumbnailCache>(sp =>
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "ApertureNeo", "thumbs");
            Directory.CreateDirectory(cacheDir);
            return new ThumbnailCache(Path.Combine(cacheDir, "cache.db"));
        });

        // SlideshowService owns the slideshow tick timer + the
        // NextRequested event the main window subscribes to.
        // Singleton because the timer must survive across the
        // view-model lifetime (the user can toggle it on / off
        // repeatedly during a single window's life).
        services.AddSingleton<SlideshowService>();

        // ThumbnailLoadCoordinator batches thumbnail decode
        // requests at 8 concurrent workers. The IThumbnailCache
        // dependency is resolved from the singleton registered
        // above. Per-window was considered (each window has its
        // own visible-range) but the cache + work queue are
        // shared across windows anyway, so a single coordinator
        // is the simpler choice and matches NavigationService's
        // singleton scope.
        services.AddSingleton<ThumbnailLoadCoordinator>(sp =>
            new ThumbnailLoadCoordinator(
                sp.GetRequiredService<IThumbnailCache>(),
                maxConcurrent: 8));

        // -- Transient / per-use -------------------------------------
        // NavigationService holds an ObservableCollection bound to
        // the UI; per-window makes sense once P2 introduces VMs
        // (the navigator is owned by the shell). For P0 we keep
        // it singleton so existing single-instance state survives
        // across the few partial-class controllers that touch it.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IImageLoader, ImageLoader>();

        // Cross-VM shared state (current image, fullscreen flag,
        // etc.). Singleton because all VMs observe the same
        // instance; one VM writes, others react.
        services.AddSingleton<IUiState, UiState>();

        // P3: design-token access. Singleton; one theme
        // instance per process. Used by MainWindow code-behind
        // (fullscreen transition animation, plugin status dot)
        // and any future C# code that needs a brush / color /
        // value at runtime instead of fishing it out of
        // Application.Current.Resources by string key.
        services.AddSingleton<ITheme, LinearTheme>();

        // ViewModels. P2: per-window scope (Transient). Each new
        // MainWindow gets a fresh set of VMs; services stay
        // singletons so VMs are cheap to construct.
        services.AddTransient<TitleBarViewModel>();
        services.AddTransient<FloatingBarViewModel>();
        services.AddTransient<ImageViewerPanelViewModel>();
        services.AddTransient<InfoPillViewModel>();
        services.AddTransient<InfoPopoverViewModel>();
        services.AddTransient<EdgeNavViewModel>();
        services.AddTransient<ExitFullscreenHintViewModel>();
        services.AddTransient<FolderTreePanelViewModel>();
        services.AddTransient<ThumbnailPanelViewModel>();

        // Plugin-side services (P4) hook in here. The optional
        // configure delegate is used by tests and by the future
        // IPluginModule registration path.
        configure?.Invoke(services);

        _services = services.BuildServiceProvider();
        return _services;
    }

    /// <summary>Reset the host (used in tests).</summary>
    public static void Reset()
    {
        (_services as IDisposable)?.Dispose();
        _services = null;
    }
}