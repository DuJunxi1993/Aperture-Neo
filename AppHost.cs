using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
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
/// Migration note: this is the first step in the P0→P3 modularity
/// refactor. After P0 the static <c>App.SettingsStore</c> /
/// <c>App.ThumbnailCache</c> properties are thin forwarders to the
/// DI singleton instances — they exist for backward compatibility
/// with the ~28 existing call sites that haven't been migrated to
/// constructor injection yet. P1/P2 will replace those call sites
/// with VM / UserControl constructors, at which point the static
/// forwarders can be deleted.
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