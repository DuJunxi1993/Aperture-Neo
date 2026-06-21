using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ApertureNeo.Services;
using Wpf.Ui.Controls;

namespace ApertureNeo;

/// <summary>
/// "About" dialog. Hosts the version-check + update flow:
///   - On open, silently queries GitHub Releases for a newer
///     version of the main app.
///   - If one exists, shows current vs. latest + a "更新" button.
///   - Clicking the button switches to a progress view with a
///     "取消" cancel button; the download is streamed to a temp
///     file and reported as percent in real time.
///   - On success, the installer is launched via ShellExecute and
///     the app calls Application.Current.Shutdown() so the new
///     installer (which uninstalls the old version first) doesn't
///     collide with a running process.
///
/// Raises <see cref="UpdateAvailableChanged"/> on every check so
/// the parent MainWindow can append a "（有版本更新）" suffix to
/// its 关于 menu item.
/// </summary>
public partial class AboutWindow : FluentWindow
{
    private readonly UpdateChecker _checker = new();
    private readonly UpdateDownloader _downloader = new();
    private CancellationTokenSource? _downloadCts;
    private UpdateInfo? _pendingUpdate;

    public AboutWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Loaded += AboutWindow_Loaded;
    }

    private async void AboutWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CurrentVersionText.Text = $"v{_checker.CurrentVersion.ToString(3)}  当前版本";
        await CheckForUpdateAsync(forceRefresh: false);
    }

    private async Task CheckForUpdateAsync(bool forceRefresh)
    {
        UpdateStatusText.Text = "正在检查更新…";
        UpdateDetailText.Text = "";
        BtnUpdate.Visibility = Visibility.Collapsed;
        BtnCheck.IsEnabled = false;

        var result = forceRefresh
            ? await _checker.ForceCheckMainAppUpdateAsync()
            : await _checker.CheckMainAppUpdateAsync();

        BtnCheck.IsEnabled = true;

        if (!result.Checked) return;   // cache hit + not forced; shouldn't happen on forced path

        if (result.Info == null)
        {
            UpdateStatusText.Text = "无法检查更新";
            UpdateDetailText.Text = result.Error ?? "网络错误";
            _pendingUpdate = null;
            UpdateAvailableChanged?.Invoke(this, false);
            return;
        }

        if (result.Info.IsNewerThan(_checker.CurrentVersion))
        {
            _pendingUpdate = result.Info;
            UpdateStatusText.Text = $"发现新版本  v{result.Info.LatestVersion.ToString(3)}";
            UpdateDetailText.Text = string.IsNullOrWhiteSpace(result.Info.ReleaseNotes)
                ? "（暂无更新说明）"
                : result.Info.ReleaseNotes;
            BtnUpdate.Visibility = Visibility.Visible;
            UpdateAvailableChanged?.Invoke(this, true);
        }
        else
        {
            UpdateStatusText.Text = "已是最新版本";
            UpdateDetailText.Text = "";
            _pendingUpdate = null;
            UpdateAvailableChanged?.Invoke(this, false);
        }
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        // Switch from idle to downloading view
        UpdatePanel.Visibility = Visibility.Collapsed;
        DownloadPanel.Visibility = Visibility.Visible;
        BtnOk.IsEnabled = false;
        BtnClose.IsEnabled = false;

        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<long>(bytes =>
        {
            var pct = _pendingUpdate.FileSize > 0
                ? (int)(bytes * 100 / _pendingUpdate.FileSize)
                : 0;
            // Clamp in case the server reports a slightly larger
            // file than the asset metadata said.
            if (pct > 100) pct = 100;
            DownloadProgress.Value = pct;
            DownloadPercentText.Text = $"{pct}%";
            DownloadStatusText.Text =
                $"下载中…  {FormatBytes(bytes)} / {FormatBytes(_pendingUpdate.FileSize)}";
        });

        var result = await _downloader.DownloadAsync(
            _pendingUpdate.DownloadUrl,
            _pendingUpdate.FileSize,
            progress,
            _downloadCts.Token);

        if (result.Success)
        {
            DownloadStatusText.Text = "下载完成，正在启动安装程序…";
            try
            {
                Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
                // Shutdown so the installer (which calls the old
                // version's uninstaller first) doesn't conflict with
                // a still-running ApertureNeo.exe file handle.
                Application.Current.Shutdown();
                return;
            }
            catch (Exception ex)
            {
                // Couldn't launch — restore the update panel so the
                // user can retry.
                DownloadStatusText.Text = "启动安装程序失败";
                System.Windows.MessageBox.Show($"无法启动安装程序：{ex.Message}", "更新失败",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        else if (result.Error == "已取消")
        {
            // Cancelled — just restore the panel, leave _pendingUpdate
            // so the user can click 更新 again.
        }
        else
        {
            // Download error (network, disk, etc.) — show in panel.
            DownloadStatusText.Text = "下载失败";
            UpdateDetailText.Text = result.Error;
        }

        // Restore UI (any non-success path that didn't already close).
        DownloadPanel.Visibility = Visibility.Collapsed;
        UpdatePanel.Visibility = Visibility.Visible;
        BtnOk.IsEnabled = true;
        BtnClose.IsEnabled = true;
        _downloadCts?.Dispose();
        _downloadCts = null;
    }

    private void BtnCancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
    }

    private async void BtnCheck_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdateAsync(forceRefresh: true);
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Close(); return; }
        try { DragMove(); } catch { /* release outside title bar */ }
    }

    /// <summary>
    /// Raised whenever the update-available state changes (e.g.
    /// after a check finds a new version, or when this window
    /// closes and the cache is reset). MainWindow subscribes to
    /// show "（有版本更新）" next to its 关于 menu item.
    /// </summary>
    public event EventHandler<bool>? UpdateAvailableChanged;

    protected override void OnClosed(EventArgs e)
    {
        // Make sure the menu badge gets cleared on close, so any
        // session-level "update available" hint doesn't outlive the
        // dialog. We re-raise with the last-known state, but the
        // false-vs-true answer is up to the consumer; here we just
        // signal "done".
        UpdateAvailableChanged?.Invoke(this, _pendingUpdate != null);
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
        base.OnClosed(e);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
