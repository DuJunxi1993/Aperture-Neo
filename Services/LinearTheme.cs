using System.Windows.Media;

namespace ApertureNeo.Services;

/// <summary>
/// Concrete <see cref="ITheme"/> for the Linear light-mode
/// design system. Returns the brushes defined in
/// <see cref="DesignTokens.xaml"/> as C# properties so the
/// runtime lookups don't have to fish them out of
/// <c>Application.Current.Resources</c> by string key.
///
/// P3: this is the only theme implementation — the app is
/// Linear light-only. A future dark-mode variant would add
/// a <c>DarkTheme : ITheme</c> (or similar) and the
/// composition root would pick which one to register
/// based on the user's preference. Call sites stay
/// unchanged.
///
/// Resolution: <see cref="BrushConverter"/> is used to
/// convert each hex string from
/// <see cref="DesignTokens.xaml"/> to a frozen
/// <see cref="SolidColorBrush"/>. The strings are kept in
/// sync with the XAML by hand (both files document the
/// values); a future improvement could generate the C#
/// from the XAML or vice versa, but that's out of scope
/// for the current token set (~5 brushes).
/// </summary>
public sealed class LinearTheme : ITheme
{
    private static readonly BrushConverter Converter = new();

    // Hex values mirror DesignTokens.xaml (light mode only).
    // Keep in sync manually.
    private const string SurfaceBlackHex = "#FF000000";
    private const string SurfaceElevatedHex = "#FFFFFFFF";
    private const string StatusGreenHex = "#FF16a34a";
    private const string StatusRedHex = "#FFdc2626";
    private const string TextTertiaryHex = "#FF8a8f98";

    public Brush SurfaceBlack => Freeze((Brush)Converter.ConvertFromString(SurfaceBlackHex)!);
    public Brush SurfaceElevated => Freeze((Brush)Converter.ConvertFromString(SurfaceElevatedHex)!);
    public Brush StatusGreen => Freeze((Brush)Converter.ConvertFromString(StatusGreenHex)!);
    public Brush StatusRed => Freeze((Brush)Converter.ConvertFromString(StatusRedHex)!);
    public Brush TextTertiary => Freeze((Brush)Converter.ConvertFromString(TextTertiaryHex)!);

    /// <summary>Freeze the brush so WPF can use it across
    /// threads without defensive cloning and so the GC
    /// doesn't have to walk a mutable color value. Matches
    /// the behaviour of the resource brushes loaded from
    /// DesignTokens.xaml (which are also frozen by WPF
    /// when loaded via <c>{StaticResource}</c>).</summary>
    private static Brush Freeze(Brush brush)
    {
        if (!brush.IsFrozen) brush.Freeze();
        return brush;
    }
}