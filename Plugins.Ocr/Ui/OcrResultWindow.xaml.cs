using System.Linq;
using System.Windows;
using ApertureNeo.Plugins.Ocr.Models;

namespace ApertureNeo.Plugins.Ocr.Ui;

public partial class OcrResultWindow : Window
{
    private OcrResult? _result;

    public OcrResultWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
        StatusText.Foreground = System.Windows.Media.Brushes.SteelBlue;
        ErrorText.Visibility = Visibility.Collapsed;
        ResultBox.Text = string.Empty;
        MetaText.Text = string.Empty;
    }

    public void SetError(string message)
    {
        StatusText.Text = "失败";
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ResultBox.Text = string.Empty;
        MetaText.Text = string.Empty;
    }

    public void SetResult(OcrResult result, string fileName)
    {
        _result = result;
        FileNameText.Text = fileName;
        StatusText.Text = "完成";
        StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        ResultBox.Text = result.FullText;
        MetaText.Text = $"{result.Lines.Count} 行 · {result.ElapsedMs:F0} ms";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultBox.Text)) return;
        try { Clipboard.SetText(ResultBox.Text); } catch { }
    }

    private void BtnCopyLines_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null || _result.Lines.Count == 0) return;
        var text = string.Join("\n", _result.Lines.Select(l => l.Text));
        try { Clipboard.SetText(text); } catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
