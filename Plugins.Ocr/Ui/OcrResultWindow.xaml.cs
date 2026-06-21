using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApertureNeo.Plugins.Ocr.Core.Models;

namespace ApertureNeo.Plugins.Ocr.Ui;

public partial class OcrResultWindow : Window
{
    public OcrResultWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // If the click originated on a Button (or any descendant of a
        // Button — the × glyph is a TextBlock inside the close button),
        // let the button handle its own Click. DragMove() would otherwise
        // consume the MouseUp and the button's Click would never fire.
        if (IsClickOnButton(e.OriginalSource)) return;
        if (e.ClickCount == 2) return;
        try { DragMove(); } catch { /* DragMove throws if released outside the title bar — safe to ignore */ }
    }

    private static bool IsClickOnButton(object source)
    {
        var dep = source as DependencyObject;
        while (dep != null)
        {
            if (dep is System.Windows.Controls.Button) return true;
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
        return false;
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
        StatusDot.Background = (Brush)FindResource("FocusRing");
        ErrorText.Visibility = Visibility.Collapsed;
        ResultBox.Text = string.Empty;
        MetaText.Text = string.Empty;
    }

    public void SetError(string message)
    {
        StatusText.Text = "失败";
        StatusDot.Background = (Brush)FindResource("StatusRed");
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ResultBox.Text = string.Empty;
        MetaText.Text = string.Empty;
    }

    public void SetResult(OcrResult result, string fileName)
    {
        HeaderFileName.Text = fileName;
        StatusText.Text = "完成";
        StatusDot.Background = (Brush)FindResource("StatusGreen");
        MetaText.Text = $"{result.Lines.Count} 行 · {result.ElapsedMs:F0} ms";
        ErrorText.Visibility = Visibility.Collapsed;
        // Cache the wrapped text so toggling back from 不换行 restores
        // the original \n structure. The 当前 toggle state determines
        // whether to display wrapped or unwrapped.
        _wrappedText = result.FullText;
        _withSpacesText = result.FullText;
        ApplyCurrentMode();
    }

    private void ApplyCurrentMode()
    {
        if (ResultBox == null || _wrappedText == null) return;
        var text = _withSpacesText ?? _wrappedText;
        if (RadioClearSpaces?.IsChecked == true)
            text = text.Replace(" ", "").Replace("\t", "");
        if (RadioNoWrap?.IsChecked == true)
            text = text.Replace("\n", "").Replace("\r", "");
        ResultBox.Text = text;
    }

    // 换行/不换行 toggle — 换行 restores the cached wrapped text so
    // toggling back recovers the OCR \n structure.
    private void RadioWrap_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return;
        if (_wrappedText != null)
        {
            _withSpacesText = _wrappedText;
            ApplyCurrentMode();
        }
    }

    private void RadioNoWrap_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return; // XAML load fires this before wiring
        // 不换行: strip \n from current text. We deliberately don't save
        // this as a new baseline — toggling back to 换行 should restore
        // the OCR original, not the user's nowrap-edited version.
        ResultBox.Text = ResultBox.Text.Replace("\n", "").Replace("\r", "");
    }

    // 有空格/清除空格 toggle — 清除空格 stores the current (with-spaces)
    // text and displays a stripped version. 有空格 restores it.
    private void RadioWithSpaces_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return;
        if (_withSpacesText != null)
        {
            ResultBox.Text = _withSpacesText;
        }
    }

    private void RadioClearSpaces_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return;
        _withSpacesText = ResultBox.Text;
        ResultBox.Text = ResultBox.Text.Replace(" ", "").Replace("\t", "");
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox != null && !string.IsNullOrEmpty(ResultBox.Text))
        {
            try { Clipboard.SetText(ResultBox.Text); } catch { }
        }
    }

    private void BtnCopyAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox != null && !string.IsNullOrEmpty(ResultBox.Text))
        {
            try { Clipboard.SetText(ResultBox.Text); } catch { }
        }
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Cached OCR outputs used by the toggles. Null until SetResult runs.
    private string? _wrappedText;   // contains the \n separators
    private string? _withSpacesText; // contains the original spaces/tabs
}
