using System;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;

namespace ApertureNeo.Controls.Annotation;

/// <summary>
/// Top-of-viewer annotation toolbar: 退出编辑 / 清空 / 撤销 /
/// 颜色 / 马赛克 mode / 粗细 / OCR / 保存. The host (screenshot
/// editor or the main app's image viewer in annotation mode)
/// sets the shared <see cref="State"/> via the
/// <see cref="StateProperty"/> dependency property, which is
/// bound from XAML or set in code. The overlay doesn't know
/// which host it's in — it just modifies the shared
/// <see cref="AnnotationState"/> and fires the
/// <see cref="SaveRequested"/> / <see cref="ExitRequested"/> /
/// <see cref="OcrRequested"/> / <see cref="ModeChanged"/>
/// events for the host to handle.
///
/// 保留以供将来开发：主程序 view 栏的标注模式当前已被
/// EditorWindow 弹出编辑替代，此文件处于闲置状态
/// （IsAnnotating 永远为 false，Visibility 永远为 Collapsed）。
///
/// Mode semantics:
/// <list type="bullet">
///   <item><see cref="AnnotationMode.Color"/> — Pen drawing.
///         Color circles + size buttons drive
///         <see cref="AnnotationState.CurrentColor"/> /
///         <see cref="AnnotationState.CurrentSize"/>.</item>
///   <item><see cref="AnnotationMode.Mosaic"/> — Drag-rectangle
///         mosaic. Size buttons drive the block size (the
///         <see cref="AnnotationState.CurrentSize"/> feeds
///         <c>ApplyMosaicRectangle</c> on the host's
///         mouse-up). The mosaic picker group shows the current
///         block size as a label instead of separate buttons.</item>
/// </list>
///
/// <see cref="ModeChanged"/> fires whenever the user toggles
/// mode, so the host can switch the PenCanvas mouse handler
/// between color-stroke and mosaic-drag behaviour (use
/// <see cref="Views.Shared.PenCanvasBehavior"/>).
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

    /// <summary>Raised when the user clicks 退出编辑. The host
    /// flips <c>IsAnnotating</c> off WITHOUT clearing the
    /// annotation state — so the user can re-enter annotation
    /// mode and resume.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user clicks OCR. The host
    /// (the image viewer's VM) runs the OCR pipeline against
    /// the current image + overlay.</summary>
    public event EventHandler? OcrRequested;

    /// <summary>Raised when the user toggles between Color and
    /// Mosaic mode. The host switches its PenCanvas mouse
    /// handler to match.</summary>
    public event EventHandler<AnnotationMode>? ModeChanged;

    public AnnotationOverlay()
    {
        InitializeComponent();
        ApplyModeVisibility();
    }

    public enum AnnotationMode { Color, Mosaic }

    private AnnotationMode _mode = AnnotationMode.Color;
    public AnnotationMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ApplyModeVisibility();
            ModeChanged?.Invoke(this, value);
        }
    }

    private void ApplyModeVisibility()
    {
        if (ColorPickerGroup != null) ColorPickerGroup.Visibility =
            _mode == AnnotationMode.Color ? Visibility.Visible : Visibility.Collapsed;
        if (MosaicPickerGroup != null) MosaicPickerGroup.Visibility =
            _mode == AnnotationMode.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        if (SizeLabel != null)
            SizeLabel.Text = _mode == AnnotationMode.Mosaic ? "方块大小" : "粗细";
        if (MosaicSizeLabel != null && State != null)
            MosaicSizeLabel.Text = ((int)Math.Round(State.CurrentSize)).ToString();
    }

    private void PenModeColor_Checked(object sender, RoutedEventArgs e)
    {
        Mode = AnnotationMode.Color;
    }

    private void PenModeMosaic_Checked(object sender, RoutedEventArgs e)
    {
        Mode = AnnotationMode.Mosaic;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
        => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OcrBtn_Click(object sender, RoutedEventArgs e)
        => OcrRequested?.Invoke(this, EventArgs.Empty);

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
            // Keep the mosaic block-size label in sync so the
            // user sees the current value while in mosaic mode.
            if (MosaicSizeLabel != null) MosaicSizeLabel.Text = size.ToString();
            if (SizeLabel != null)
                SizeLabel.Text = _mode == AnnotationMode.Mosaic ? "方块大小" : "粗细";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }
}