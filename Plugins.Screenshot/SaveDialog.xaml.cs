using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ApertureNeo.Plugins.Screenshot;

/// <summary>
/// Save dialog for the screenshot editor. Lets the user pick a
/// folder (with a Browse button opening <see cref="OpenFolderDialog"/>)
/// and a filename, and optionally persist the chosen folder as the
/// default for future saves via
/// <see cref="ApertureNeo.Services.ISettingsStore.DefaultScreenshotSaveDirectory"/>.
///
/// The dialog is self-contained (loads EditorStyles.xaml as a merged
/// resource dictionary) so it can be used standalone by the
/// screenshot plugin without depending on the main app's resource
/// chain.
/// </summary>
public partial class SaveDialog : Window
{
    /// <summary>Full save path (folder + filename) the user chose.
    /// Null if the user cancelled.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>If the user checked "set as default", the folder to
    /// persist. Null if the checkbox was unchecked.</summary>
    public string? DefaultDirectoryToSet { get; private set; }

    /// <param name="initialFolder">Pre-fill the folder field. Pass
    /// the user's preferred default (from settings) or null for
    /// the hard-coded fallback.</param>
    /// <param name="initialFilename">Pre-fill the filename field.
    /// Typically a timestamp-based suggestion.</param>
    public SaveDialog(string? initialFolder, string initialFilename)
    {
        InitializeComponent();
        FolderBox.Text = initialFolder ?? DefaultFallbackFolder();
        FilenameBox.Text = initialFilename;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        var filename = FilenameBox.Text.Trim();

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("目录不存在或不可访问", "保存截图",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            FolderBox.Focus();
            FolderBox.SelectAll();
            return;
        }
        if (string.IsNullOrEmpty(filename))
        {
            MessageBox.Show("文件名不能为空", "保存截图",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            FilenameBox.Focus();
            FilenameBox.SelectAll();
            return;
        }
        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("文件名包含非法字符", "保存截图",
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string DefaultFallbackFolder()
    {
        // Per the screenshot tool's spec, the first-run fallback
        // is C:\Users\<user>\Pictures\ApertureNeo\Screenshots.
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var fallback = Path.Combine(pictures, "ApertureNeo", "Screenshots");
        return fallback;
    }

    private static string SafeInitialDir(string? current)
    {
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            return current;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Directory.Exists(pictures) ? pictures : Environment.CurrentDirectory;
    }
}
