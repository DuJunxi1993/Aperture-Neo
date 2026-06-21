using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApertureNeo.Plugins.Ocr.Core.Models;

namespace ApertureNeo.Views;

/// <summary>
/// Multi-file OCR result window for `aperture ocr -g a.jpg b.png ...`.
/// Shows every file's result in a stacked, scrollable list with a
/// single "复制全部" button that concatenates all successful results.
/// Mirrors the design language of the single-file OcrResultWindow
/// (Linear tokens, ShadowDialog template, CornerRadius=8) so the
/// two windows feel like one cohesive surface.
///
/// The per-file 换行/不换行 + 有空格/清除空格 toggles from the
/// single-file window are intentionally omitted here: the OCR
/// pipeline already produces \n-separated FullText, and most users
/// running multi-file OCR want to skim + copy, not transform each
/// file's text.
/// </summary>
public partial class HeadlessOcrResultWindow : Window
{
    private List<(string Path, OcrResult Result)> _entries = new();

    public HeadlessOcrResultWindow()
    {
        InitializeComponent();
    }

    public void SetResults(IReadOnlyList<(string Path, OcrResult Result)> entries)
    {
        _entries = entries.ToList();
        HeaderCount.Text = $"({_entries.Count} 个文件)";
        BuildSections();
        UpdateSubtitle();
    }

    private void BuildSections()
    {
        FilesPanel.Children.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            FilesPanel.Children.Add(BuildFileSection(_entries[i], i));
            if (i < _entries.Count - 1)
            {
                FilesPanel.Children.Add(BuildDivider());
            }
        }
    }

    private FrameworkElement BuildFileSection((string Path, OcrResult Result) entry, int index)
    {
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BorderStandard"),
            BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("SurfaceElevated"),
            CornerRadius = (CornerRadius)FindResource("RadiusCard"),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var outer = new StackPanel { Orientation = Orientation.Vertical };

        // Header row: status dot + filename + per-file copy button
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Style = (Style)FindResource("LinearDot"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        dot.Background = entry.Result.IsSuccess
            ? (Brush)FindResource("StatusGreen")
            : (Brush)FindResource("StatusRed");
        Grid.SetColumn(dot, 0);

        var fileName = new TextBlock
        {
            Text = Path.GetFileName(entry.Path),
            FontFamily = (FontFamily)FindResource("FontPrimary"),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(fileName, 1);

        var copyBtn = new Button
        {
            Style = (Style)FindResource("LinearGhostButton"),
            Content = "复制",
            Tag = index,
            VerticalAlignment = VerticalAlignment.Center,
        };
        copyBtn.Click += PerFileCopy_Click;
        Grid.SetColumn(copyBtn, 2);

        header.Children.Add(dot);
        header.Children.Add(fileName);
        header.Children.Add(copyBtn);

        outer.Children.Add(header);

        // Body: success shows OCR text in an editable TextBox;
        // failure shows the error message in red.
        if (entry.Result.IsSuccess)
        {
            var box = new TextBox
            {
                Text = entry.Result.FullText,
                IsReadOnly = false,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0, 10, 0, 0),
                FontFamily = (FontFamily)FindResource("FontPrimary"),
                FontSize = 13,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                SpellCheck = { IsEnabled = false },
            };
            outer.Children.Add(box);

            // Per-file meta (lines · ms) under the text box, mirroring
            // the single-file window's "N 行 · Xms" line.
            var meta = new TextBlock
            {
                Text = $"{entry.Result.Lines.Count} 行 · {entry.Result.ElapsedMs:F0} ms",
                FontFamily = (FontFamily)FindResource("FontMono"),
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                Foreground = (Brush)FindResource("TextTertiary"),
                Margin = new Thickness(0, 8, 0, 0),
            };
            outer.Children.Add(meta);
        }
        else
        {
            var err = new TextBlock
            {
                Text = entry.Result.ErrorMessage ?? "识别失败",
                FontFamily = (FontFamily)FindResource("FontPrimary"),
                FontSize = 12,
                Foreground = (Brush)FindResource("StatusRed"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            };
            outer.Children.Add(err);
        }

        card.Child = outer;
        return card;
    }

    private static FrameworkElement BuildDivider()
    {
        return new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.FindResource("BorderSubtle"),
            Margin = new Thickness(0, 10, 0, 10),
        };
    }

    private void UpdateSubtitle()
    {
        int succeeded = _entries.Count(e => e.Result.IsSuccess);
        int failed = _entries.Count - succeeded;
        double totalMs = _entries.Sum(e => e.Result.ElapsedMs);

        SubtitleText.Text = failed == 0
            ? $"已完成 {succeeded}/{_entries.Count} 个文件"
            : $"已完成 {succeeded}/{_entries.Count} 个文件 (失败 {failed})";
        MetaText.Text = $"总计 {totalMs:F0} ms";

        StatusDot.Background = failed == 0
            ? (Brush)FindResource("StatusGreen")
            : (failed == _entries.Count ? (Brush)FindResource("StatusRed") : (Brush)FindResource("StatusRed"));
    }

    private void PerFileCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int index) return;
        if (index < 0 || index >= _entries.Count) return;
        var (path, result) = _entries[index];
        if (!result.IsSuccess) return;

        var text = FindTextBoxForIndex(index)?.Text ?? result.FullText;
        try { Clipboard.SetText(text); } catch { }
    }

    private TextBox? FindTextBoxForIndex(int index)
    {
        // Walk the visual tree of the Nth section to find its
        // editable TextBox. The header Grid has 3 children (dot,
        // filename TextBlock, copy Button); the body comes after
        // and is the first TextBox descendant in that section.
        if (index < 0 || index >= FilesPanel.Children.Count) return null;
        if (FilesPanel.Children[index] is not FrameworkElement section) return null;
        return FindVisualChild<TextBox>(section);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deeper = FindVisualChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _entries.Count; i++)
        {
            var (path, result) = _entries[i];
            sb.Append("==== ").Append(Path.GetFileName(path)).AppendLine(" ====");
            if (result.IsSuccess)
            {
                var text = FindTextBoxForIndex(i)?.Text ?? result.FullText;
                sb.AppendLine(text);
            }
            else
            {
                sb.Append("[失败] ").AppendLine(result.ErrorMessage ?? "未知错误");
            }
            sb.AppendLine();
        }

        try { Clipboard.SetText(sb.ToString()); } catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsClickOnButton(e.OriginalSource)) return;
        if (e.ClickCount == 2) return;
        try { DragMove(); } catch { /* ignore — released outside title bar */ }
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
}