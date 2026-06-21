using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApertureNeo.Plugins.Ocr.Models;

namespace ApertureNeo.Plugins.Ocr.Ui;

public partial class OcrResultWindow : Window
{
    public OcrResultWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // The title bar's MouseLeftButtonDown bubbles from any child, including
        // the close button. DragMove() would consume the MouseUp and prevent
        // the button's Click from firing. Skip dragging when the click
        // originated on a button so it can handle its own click.
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.ClickCount == 2) return;
        try { DragMove(); } catch { /* DragMove throws if released outside the title bar — safe to ignore */ }
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
        FileNameText.Text = fileName;
        StatusText.Text = "完成";
        StatusDot.Background = (Brush)FindResource("StatusGreen");
        MetaText.Text = $"{result.Lines.Count} 行 · {result.ElapsedMs:F0} ms";
        ErrorText.Visibility = Visibility.Collapsed;
        // Default mode is 换行 (preserves \n from OCR); 不换行 strips them.
        // Both modes render via TextWrapping=Wrap, so a single long line
        // still wraps at the window edge in 不换行 mode.
        if (RadioNoWrap?.IsChecked == true)
            ResultBox.Text = result.FullText.Replace("\n", "").Replace("\r", "");
        else
            ResultBox.Text = result.FullText;
    }

    private void RadioWrap_Checked(object sender, RoutedEventArgs e)
    {
        // 换行 mode: the text already has \n from OCR. The TextBox renders
        // each line separately and wraps long lines at the window edge.
        // No text transformation needed.
    }

    private void RadioNoWrap_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return; // XAML load fires this before wiring
        // 不换行 mode: strip \n from the current text so it becomes a single
        // long line. TextWrapping=Wrap still wraps it visually at the
        // window edge (like Notepad's word-wrap on a single line).
        ResultBox.Text = ResultBox.Text.Replace("\n", "").Replace("\r", "");
    }

    private void BtnClearSpaces_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null || string.IsNullOrEmpty(ResultBox.Text)) return;
        // Delete all ASCII spaces and tabs; keep newlines so the row
        // structure is preserved.
        ResultBox.Text = ResultBox.Text.Replace(" ", "").Replace("\t", "");
    }

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null || string.IsNullOrEmpty(ResultBox.Text)) return;
        try { Clipboard.SetText(ResultBox.Text); } catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
