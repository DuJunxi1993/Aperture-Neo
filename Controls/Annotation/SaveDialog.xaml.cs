using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ApertureNeo.Controls.Annotation;

/// <summary>
/// Save dialog for annotation output (used by both the screenshot
/// editor and the main app's image viewer). Two save actions
/// in the same dialog:
///   - "另存为…" (default, always available): writes to
///     FolderBox + FilenameBox, with a Browse button that
///     opens <see cref="OpenFolderDialog"/> (.NET 8+).
///   - "覆盖原图" (only enabled when the host passed
///     <c>currentFilePath</c>): writes directly to the
///     original file path, no dialog interaction needed.
///
/// Plus a "设为默认保存目录" checkbox; the host's save
/// service reads <see cref="DefaultDirectoryToSet"/> to
/// persist the user's preference.
///
/// Resources (DesignTokens.xaml + Styles/Annotation.xaml) are
/// loaded by pack URI so the standalone ScreenshotTool.exe
/// resolves them from the ApertureNeo assembly.
/// </summary>
public partial class SaveDialog : Window
{
    /// <summary>Full save path (folder + filename) the user
    /// chose via "另存为…". Null if cancelled or the user
    /// used "覆盖原图" instead.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>Full path when the user used "覆盖原图". Null
    /// if "另存为…" was used or the dialog was cancelled.
    /// The host's save service uses this to know which
    /// action the user took (overwrite vs new file).</summary>
    public string? OverwritePath { get; private set; }

    /// <summary>If the user checked "设为默认保存目录", this
    /// is the folder to persist. Null if unchecked.</summary>
    public string? DefaultDirectoryToSet { get; private set; }

    public SaveDialog(string? initialFolder, string initialFilename, string? currentFilePath)
    {
        InitializeComponent();

        CurrentFilePath = currentFilePath;
        FolderBox.Text = initialFolder ?? DefaultFallbackFolder();
        FilenameBox.Text = initialFilename;

        // "覆盖原图" is only meaningful when there's an
        // original file to overwrite. The editor has no
        // currentFilePath (its output is always a new file);
        // the viewer always has one.
        OverwriteBtn.IsEnabled = !string.IsNullOrEmpty(currentFilePath);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择保存目录",
            InitialDirectory = SafeInitialDir(FolderBox.Text),
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
            FolderBox.Text = dlg.FolderName;
    }

    /// <summary>另存为… button: write to FolderBox + FilenameBox.</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        var filename = FilenameBox.Text.Trim();

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("目录不存在或不可访问", "保存图片",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            FolderBox.Focus();
            FolderBox.SelectAll();
            return;
        }
        if (string.IsNullOrEmpty(filename))
        {
            MessageBox.Show("文件名不能为空", "保存图片",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            FilenameBox.Focus();
            FilenameBox.SelectAll();
            return;
        }
        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("文件名包含非法字符", "保存图片",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            FilenameBox.Focus();
            FilenameBox.SelectAll();
            return;
        }

        SelectedPath = Path.Combine(folder, filename);
        if (SetDefaultCheck.IsChecked == true)
        {
            DefaultDirectoryToSet = folder;
        }
        DialogResult = true;
        Close();
    }

    /// <summary>覆盖原图 button: write to the original file
    /// path. No dialog interaction. The host detects this via
    /// <see cref="OverwritePath"/> being non-null.</summary>
    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        // The host passes the original path via OverwriteBtn's
        // Tag (we set it from the constructor). We can also
        // recover it from the path stored in the dialog's
        // Tag; but the cleaner approach is to have the host
        // listen for the "OverwriteClicked" event and use its
        // own knowledge of the original path. We expose the
        // intent via OverwritePath being set to FolderBox +
        // FilenameBox (matching what "另存为" would do), and
        // the host checks FolderBox+FilenameBox against the
        // currentFilePath it knows about.
        //
        // Simpler: the dialog already has the filename. If
        // OverwriteBtn is enabled, the host passed currentFilePath.
        // We can store the currentFilePath in the dialog's
        // DataContext or via a property. Let's add a property
        // and set it from the constructor.
        OverwritePath = CurrentFilePath;
        if (SetDefaultCheck.IsChecked == true)
        {
            DefaultDirectoryToSet = Path.GetDirectoryName(CurrentFilePath);
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>The original file path passed by the host, when
    /// the "覆盖原图" action should be enabled. Null for the
    /// editor (no original file).</summary>
    public string? CurrentFilePath { get; set; }

    private static string DefaultFallbackFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(pictures, "ApertureNeo", "Screenshots");
    }

    private static string SafeInitialDir(string? current)
    {
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            return current;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Directory.Exists(pictures) ? pictures : Environment.CurrentDirectory;
    }
}
