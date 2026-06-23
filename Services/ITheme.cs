using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ApertureNeo.Services;

/// <summary>
/// Strongly-typed access to design tokens. Replaces the
/// runtime lookups via <c>FindResource("...")</c> and
/// <c>(Brush)Application.Current.Resources["..."]</c> in
/// C# code — those work but couple the call site to the
/// string key + the static <see cref="System.Windows.Application"/>
/// instance, both of which make refactors and unit tests
/// harder.
///
/// XAML <c>{StaticResource Xxx}</c> bindings stay as-is:
/// they're resolved at parse time against
/// <see cref="App.xaml"/>'s merged dictionaries, which is
/// the right tool for declarative styling. The ITheme
/// abstraction targets the cases where C# code needs a
/// brush / color / value at runtime (e.g. animations,
/// dynamic per-item brushes in code-behind).
///
/// P3: the surface is intentionally minimal — only the
/// tokens that are actually looked up at runtime are
/// exposed. As more code migrates to use ITheme, more
/// properties get added. Adding a new theme variant later
/// (dark mode, etc.) means adding a new ITheme
/// implementation; the call sites don't change.
/// </summary>
public interface ITheme
{
    // Surfaces — used by TransitionToFullscreen's background
    // cross-fade animation.
    Brush SurfaceBlack { get; }
    Brush SurfaceElevated { get; }

    // Status indicators — used by the plugin status dot in
    // the title bar 插件 submenu (GetStatusBrush in
    // MainWindow.xaml.cs).
    Brush StatusGreen { get; }
    Brush StatusRed { get; }

    /// <summary>Gray brush for "Disabled" plugin status and
    /// other neutral indicators.</summary>
    Brush TextTertiary { get; }
}