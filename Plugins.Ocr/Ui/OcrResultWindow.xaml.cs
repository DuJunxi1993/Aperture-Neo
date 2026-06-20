using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApertureNeo.Plugins.Ocr.Models;

namespace ApertureNeo.Plugins.Ocr.Ui;

public partial class OcrResultWindow : Window
{
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    public OcrResultWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
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
        ResultBox.Text = result.FullText;
        MetaText.Text = $"{result.Lines.Count} 行 · {result.ElapsedMs:F0} ms";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void RadioWrap_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return; // XAML load fires this before wiring
        ResultBox.TextWrapping = TextWrapping.Wrap;
        if (TextScrollViewer != null)
            TextScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void RadioNoWrap_Checked(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null) return; // XAML load fires this before wiring
        ResultBox.TextWrapping = TextWrapping.NoWrap;
        if (TextScrollViewer != null)
            TextScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void BtnCompactSpaces_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null || string.IsNullOrEmpty(ResultBox.Text)) return;
        var lines = ResultBox.Text.Split('\n')
            .Select(l => MultiSpace.Replace(l.Trim(), " "));
        ResultBox.Text = string.Join("\n", lines);
    }

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null || string.IsNullOrEmpty(ResultBox.Text)) return;
        try { Clipboard.SetText(ResultBox.Text); } catch { }
    }

    private void BtnCopyLines_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBox == null || string.IsNullOrEmpty(ResultBox.Text)) return;
        var lines = ResultBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        try { Clipboard.SetText(string.Join("\n", lines)); } catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
