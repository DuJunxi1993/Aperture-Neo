using System;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;

namespace ApertureNeo.Controls.Annotation;

/// <summary>
/// Top-of-viewer annotation toolbar: Clear / Undo / Color /
/// Size / Save. The host (screenshot editor or the main app's
/// image viewer in annotation mode) sets the shared
/// <see cref="State"/> via the <see cref="StateProperty"/>
/// dependency property, which is bound from XAML or set in
/// code. The overlay doesn't know which host it's in — it just
/// modifies the shared <see cref="AnnotationState"/> and fires
/// the <see cref="SaveRequested"/> event for the host to handle.
/// </summary>
public partial class AnnotationOverlay : UserControl
{
    /// <summary>The shared annotation state. Color / size /
    /// undo / clear all route through this; the OverlayBitmap
    /// it owns is what the SkiaImageViewer renders as the
    /// overlay layer. As a DependencyProperty so XAML binding
    /// (and runtime swap on image change) just works.</summary>
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(AnnotationState),
            typeof(AnnotationOverlay),
            new PropertyMetadata(null));

    public AnnotationState? State
    {
        get => (AnnotationState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Raised when the user clicks Save. The host
    /// should compose the final image (original + overlay) and
    /// persist it via <see cref="AnnotationSaveService"/> or
    /// its own save logic.</summary>
    public event EventHandler? SaveRequested;

    public AnnotationOverlay()
    {
        InitializeComponent();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        State?.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        State?.Undo();
    }

    private void ColorBtn_Click(object sender, RoutedEventArgs e)
    {
        // RadioButton (which is a ToggleButton subclass) is the
        // GroupName="AnnotationColor" parent; GroupName keeps the
        // check state exclusive, so we just read the Tag.
        if (sender is RadioButton btn && btn.Tag is string hex
            && !string.IsNullOrEmpty(hex) && State != null)
        {
            State.CurrentColor = SKColor.Parse(hex);
        }
    }

    private void SizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn
            && int.TryParse(btn.Content?.ToString(), out int size)
            && State != null)
        {
            State.CurrentSize = size;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}
