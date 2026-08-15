using System.Globalization;
using System.Resources;

namespace ApertureNeo.Strings;

/// <summary>
/// Programmatic accessors for the localised strings in
/// Strings.resx. XAML prefers <c>{DynamicResource KeyName}</c>
/// (resolves at parse time without compile-time type lookup);
/// code-behind uses these properties which read through
/// <see cref="ResourceManager"/>.
///
/// The resx is the source of truth — these properties mirror
/// it. The fallback value (the right side of <c>??</c>) is
/// what gets returned if the resx is missing (design-time
/// tooling, etc.) so the app never crashes on startup just
/// because a resource file isn't available yet.
///
/// P5 follow-up: extracted from ApertureNeo/Properties/Strings.cs
/// into a standalone library so the Plugins.Ocr.Ui DLL (loaded
/// via ALC) can localise too. The main WPF app and the plugin
/// both reference this single .resx — there's no risk of the
/// two getting out of sync (the resx is compiled into the
/// single ApertureNeo.Strings.dll and consumed by every
/// referencing project).
///
/// Named <c>SR</c> (not <c>Strings</c>) so callers can write
/// <c>SR.AboutWindow_Title</c> — keeping the class name
/// identical to the enclosing namespace would make
/// <c>Strings.AboutWindow_Title</c> ambiguous to the C#
/// compiler (it'd see ApertureNeo.Strings.AboutWindow_Title
/// as a nested-namespace member, not as a static-class
/// property).
/// </summary>
public static class SR
{
    private static readonly ResourceManager _manager =
        new ResourceManager("ApertureNeo.Strings.Strings", typeof(SR).Assembly);

    public static string AboutWindow_Title => Get("AboutWindow_Title", "关于 Aperture Neo");
    public static string AboutWindow_About => Get("AboutWindow_About", "关于");
    public static string AboutWindow_VersionLabel => Get("AboutWindow_VersionLabel", "当前版本");
    public static string AboutWindow_CheckingUpdate => Get("AboutWindow_CheckingUpdate", "正在检查更新…");
    public static string AboutWindow_UpdateButton => Get("AboutWindow_UpdateButton", "更新");
    public static string AboutWindow_RecheckButton => Get("AboutWindow_RecheckButton", "重新检查");
    public static string AboutWindow_UpdateCheckFailed => Get("AboutWindow_UpdateCheckFailed", "无法检查更新");
    public static string AboutWindow_NetworkError => Get("AboutWindow_NetworkError", "网络错误");
    public static string AboutWindow_NewVersionFound => Get("AboutWindow_NewVersionFound", "发现新版本  v{0}");
    public static string AboutWindow_NoReleaseNotes => Get("AboutWindow_NoReleaseNotes", "（暂无更新说明）");
    public static string AboutWindow_UpToDate => Get("AboutWindow_UpToDate", "已是最新版本");
    public static string AboutWindow_Downloading => Get("AboutWindow_Downloading", "下载中…");
    public static string AboutWindow_DownloadingWithProgress => Get("AboutWindow_DownloadingWithProgress", "下载中…  {0} / {1}");
    public static string AboutWindow_DownloadComplete => Get("AboutWindow_DownloadComplete", "下载完成，正在启动安装程序…");
    public static string AboutWindow_InstallLaunchFailed => Get("AboutWindow_InstallLaunchFailed", "启动安装程序失败");
    public static string AboutWindow_InstallLaunchFailedMessage => Get("AboutWindow_InstallLaunchFailedMessage", "无法启动安装程序：{0}");
    public static string AboutWindow_UpdateFailedTitle => Get("AboutWindow_UpdateFailedTitle", "更新失败");
    public static string AboutWindow_DownloadCancelled => Get("AboutWindow_DownloadCancelled", "已取消");
    public static string AboutWindow_DownloadFailed => Get("AboutWindow_DownloadFailed", "下载失败");
    public static string AboutWindow_CloseButton => Get("AboutWindow_CloseButton", "关闭");

    public static string TitleBar_UpdateAvailableSuffix => Get("TitleBar_UpdateAvailableSuffix", "（有版本更新）");
    public static string TitleBar_Open => Get("TitleBar_Open", "打开");
    public static string TitleBar_ClearCache => Get("TitleBar_ClearCache", "清除缓存");
    public static string TitleBar_ClearThumbCache => Get("TitleBar_ClearThumbCache", "缩略图缓存");
    public static string TitleBar_ClearRecent => Get("TitleBar_ClearRecent", "最近访问记录");
    public static string TitleBar_Plugins => Get("TitleBar_Plugins", "插件");
    public static string TitleBar_About => Get("TitleBar_About", "关于");

    public static string ImageCtx_CopyPath => Get("ImageCtx_CopyPath", "复制图片路径");
    public static string ImageCtx_OpenInExplorer => Get("ImageCtx_OpenInExplorer", "在资源管理器中打开");
    public static string ImageCtx_Print => Get("ImageCtx_Print", "打印");
    public static string ImageCtx_SetWallpaper => Get("ImageCtx_SetWallpaper", "设为桌面壁纸");

    public static string InfoPill_File => Get("InfoPill_File", "文件");
    public static string InfoPill_Size => Get("InfoPill_Size", "大小");
    public static string InfoPill_Dimensions => Get("InfoPill_Dimensions", "尺寸");
    public static string InfoPill_Camera => Get("InfoPill_Camera", "相机");
    public static string InfoPill_Lens => Get("InfoPill_Lens", "镜头");
    public static string InfoPill_DateTaken => Get("InfoPill_DateTaken", "拍摄");

    public static string Fullscreen_ExitHint => Get("Fullscreen_ExitHint", "退出全屏 (Esc / Ctrl+F)");

    // Round X (tree-stack split): the previous "返回上一层" button
    // became "上一级" (drill-stack pop, strictly directory parent)
    // and a separate "后退" button (history-stack pop, previous
    // browsing location) was added. Round Y adds a matching
    // "前进" (forward) button retracing a "后退" step.
    public static string FolderTree_DrillChipUp => Get("FolderTree_DrillChipUp", "上一级");
    public static string FolderTree_DrillChipBack => Get("FolderTree_DrillChipBack", "后退");
    public static string FolderTree_DrillChipForward => Get("FolderTree_DrillChipForward", "前进");
    public static string FolderTree_DrillChipReturnToRoot => Get("FolderTree_DrillChipReturnToRoot", "返回主页");
    public static string FolderTree_Favorites => Get("FolderTree_Favorites", "收藏夹");
    public static string FolderTree_Recent => Get("FolderTree_Recent", "最近访问");
    public static string FolderTree_ThisPC => Get("FolderTree_ThisPC", "此电脑");
    public static string FolderTree_EmptyFavorites => Get("FolderTree_EmptyFavorites", "暂无收藏");
    public static string FolderTree_EmptyRecent => Get("FolderTree_EmptyRecent", "暂无最近访问");
    public static string FolderTreeCtx_JumpTo => Get("FolderTreeCtx_JumpTo", "跳转到目录");
    public static string FolderTreeCtx_AddToFavorites => Get("FolderTreeCtx_AddToFavorites", "添加到收藏夹");
    public static string FolderTreeCtx_RemoveFromFavorites => Get("FolderTreeCtx_RemoveFromFavorites", "从收藏夹移除");
    public static string FolderTreeCtx_RemoveFromRecent => Get("FolderTreeCtx_RemoveFromRecent", "从最近访问移除");
    public static string FolderTreeCtx_OpenInExplorer => Get("FolderTreeCtx_OpenInExplorer", "在资源管理器中打开");

    public static string Thumb_EmptyStateTitle => Get("Thumb_EmptyStateTitle", "此文件夹没有图片");
    public static string Thumb_EmptyStateFormats => Get("Thumb_EmptyStateFormats", "支持 JPG、PNG、GIF、WEBP、BMP、ICO");

    public static string FileFilter_ImageFiles => Get("FileFilter_ImageFiles", "图片文件");
    public static string FileFilter_ImagePattern => Get("FileFilter_ImagePattern", "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif;*.webp;*.heic;*.heif;*.avif");
    public static string FileFilter_AllFiles => Get("FileFilter_AllFiles", "所有文件");
    public static string FileFilter_AllPattern => Get("FileFilter_AllPattern", "*.*");

    public static string OpenFileDialogFilter =>
        $"{FileFilter_ImageFiles}|{FileFilter_ImagePattern}|{FileFilter_AllFiles}|{FileFilter_AllPattern}";

    public static string HeadlessOcr_Heading => Get("HeadlessOcr_Heading", "OCR 结果");
    public static string HeadlessOcr_SummaryLabel => Get("HeadlessOcr_SummaryLabel", "概要");
    public static string HeadlessOcr_Ready => Get("HeadlessOcr_Ready", "就绪");
    public static string HeadlessOcr_CopyAll => Get("HeadlessOcr_CopyAll", "复制全部");
    public static string HeadlessOcr_Close => Get("HeadlessOcr_Close", "关闭");
    public static string HeadlessOcr_FileCount => Get("HeadlessOcr_FileCount", "({0} 个文件)");
    public static string HeadlessOcr_Copy => Get("HeadlessOcr_Copy", "复制");
    public static string HeadlessOcr_LinesMeta => Get("HeadlessOcr_LinesMeta", "{0} 行 · {1:F0} ms");
    public static string HeadlessOcr_RecognitionFailed => Get("HeadlessOcr_RecognitionFailed", "识别失败");
    public static string HeadlessOcr_FilesCompleted => Get("HeadlessOcr_FilesCompleted", "已完成 {0}/{1} 个文件");
    public static string HeadlessOcr_FilesCompletedWithFailures => Get("HeadlessOcr_FilesCompletedWithFailures", "已完成 {0}/{1} 个文件 (失败 {2})");
    public static string HeadlessOcr_TotalTime => Get("HeadlessOcr_TotalTime", "总计 {0:F0} ms");
    public static string HeadlessOcr_FailurePrefix => Get("HeadlessOcr_FailurePrefix", "[失败]");
    public static string HeadlessOcr_UnknownError => Get("HeadlessOcr_UnknownError", "未知错误");

    // OcrResultWindow strings. P5 follow-up: previously hard-coded
    // in OcrResultWindow.xaml + .cs; now centralised here so the
    // plugin UI can be translated without touching the main app.
    public static string OcrWindow_Title => Get("OcrWindow_Title", "文字提取");
    public static string OcrWindow_StatusLabel => Get("OcrWindow_StatusLabel", "状态");
    public static string OcrWindow_StatusReady => Get("OcrWindow_StatusReady", "就绪");
    public static string OcrWindow_StatusFailed => Get("OcrWindow_StatusFailed", "失败");
    public static string OcrWindow_StatusCompleted => Get("OcrWindow_StatusCompleted", "完成");
    public static string OcrWindow_LayoutLabel => Get("OcrWindow_LayoutLabel", "排版");
    public static string OcrWindow_LayoutWrap => Get("OcrWindow_LayoutWrap", "换行");
    public static string OcrWindow_LayoutNoWrap => Get("OcrWindow_LayoutNoWrap", "不换行");
    public static string OcrWindow_SpaceLabel => Get("OcrWindow_SpaceLabel", "空格");
    public static string OcrWindow_SpaceKeep => Get("OcrWindow_SpaceKeep", "有空格");
    public static string OcrWindow_SpaceStrip => Get("OcrWindow_SpaceStrip", "清除空格");
    public static string OcrWindow_EditHint => Get("OcrWindow_EditHint", "文本可直接编辑");
    public static string OcrWindow_Copy => Get("OcrWindow_Copy", "复制");
    public static string OcrWindow_CopyAndExit => Get("OcrWindow_CopyAndExit", "复制并退出");
    public static string OcrWindow_LinesMeta => Get("OcrWindow_LinesMeta", "{0} 行 · {1:F0} ms");

    private static string Get(string key, string fallback)
        => _manager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
}
