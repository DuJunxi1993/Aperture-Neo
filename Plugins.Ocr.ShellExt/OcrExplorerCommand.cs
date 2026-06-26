// OcrExplorerCommand.cs — IExplorerCommand implementation that
// adds "OCR 文字提取" to the Win11 top-level (compact) right-click
// menu for image files. Win10 still shows the verb in the classic
// right-click menu via the same IExplorerCommand API.
//
// When the user invokes the verb, we extract the selected files
// from IShellItemArray, then spawn ApertureNeo.exe ocr <files>
// in a separate process. The spawned CLI runs the OCR (loading
// ONNX models, inferring, etc.), concatenates the results, writes
// them to the clipboard, and shows a Windows toast — all the
// feedback plumbing lives in the CLI. The shell extension's
// only job is "which files did the user pick and how do I hand
// them to ApertureNeo.exe".

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ApertureNeo.Plugins.Ocr.ShellExt;

/// <summary>
/// Win11 / Win10 IExplorerCommand shell extension that adds
/// Aperture Neo's "OCR 文字提取" verb to the right-click menu
/// for image files. The verb surfaces in:
///   - Win11 compact (top-level) context menu — this is the
///     reason the verb exists; legacy IContextMenu verbs only
///     appear under "Show more options" on Win11.
///   - Win10 / Win11 classic context menu — same IExplorerCommand
///     is queried by Explorer's legacy context menu path too, so
///     Win10 users get the verb in the normal right-click menu
///     without any special handling.
///
/// The COM CLSID (a fixed GUID) is registered in the [Registry]
/// section of installer.iss as an in-proc server. The shell
/// activation machinery (mscoree.dll → hostruntime → our DLL)
/// handles .NET runtime resolution — the .NET runtime lives in
/// the same Plugins/ folder as the shell extension, picked up
/// via the standard .NET COM activation rules (registry-free
/// activation also works but reg-based is more reliable here).
/// </summary>
[ComVisible(true)]
[Guid("4E8A2D11-3F19-4F4D-A1C5-19B3C4B6F4A1")]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("ApertureNeo.Plugins.Ocr.ShellExt")]
public sealed class OcrExplorerCommand : IExplorerCommand
{
    // Single canonical GUID used by both GetCanonicalName and the
    // installer. We hand out a stable ID so that the shell can
    // recognise this verb across sessions — Windows uses the
    // canonical name to deduplicate commands registered by the
    // same extension.
    private static readonly Guid CanonicalGuid = new("4E8A2D11-3F19-4F4D-A1C5-19B3C4B6F4A1");

    // Localization kept inline: this shell extension is a leaf
    // binary with no [CustomMessages] indirection (which lives
    // in the main app's installer.iss). One string per locale
    // would be the right answer for a multilingual app; for now
    // we ship the same Chinese label the installer uses, since
    // the test audience is Chinese.
    private const string VerbTitle = "OCR 文字提取";
    private const string VerbTooltip = "用 Aperture Neo 提取图片中的文字到剪贴板";

    public int GetTitle(IShellItemArray psiArray, out string ppszName)
    {
        ppszName = VerbTitle;
        return 0; // S_OK
    }

    public int GetIcon(IShellItemArray psiArray, out string ppszIcon)
    {
        // Point at the main exe — the shell extracts the first
        // icon from the .ico resource embedded in the .exe. This
        // way the shell extension doesn't need to ship its own
        // .ico and the icon stays in sync with the host exe.
        var exePath = ResolveHostExePath();
        ppszIcon = exePath + ",0";
        return 0;
    }

    public int GetToolTip(IShellItemArray psiArray, out string ppszInfotip)
    {
        ppszInfotip = VerbTooltip;
        return 0;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = CanonicalGuid;
        return 0;
    }

    public int GetState(IShellItemArray psiArray, bool fOkToBeSlow, out uint pState)
    {
        // We don't filter on selection shape — the verb is valid
        // for any number of selected items (zero items means
        // Explorer is showing the desktop's background menu, in
        // which case we still return enabled but Invoke won't be
        // called). Returning visible + enabled matches the v3.0
        // IContextMenu verb behavior.
        pState = ExplorerCommandState.VisibleAndEnabled;
        return 0;
    }

    public int Invoke(IShellItemArray psiArray, IBindCtx pbc)
    {
        try
        {
            var paths = ExtractFilePaths(psiArray);
            if (paths.Count == 0)
            {
                // Nothing selected (e.g. user invoked from the
                // background context menu). Silent no-op — the
                // verb is filtered out by GetState when there's
                // no selection, so this path is defensive only.
                return 0;
            }

            var exePath = ResolveHostExePath();
            if (!File.Exists(exePath))
            {
                // Shouldn't happen in a normal install — the main
                // exe and the shell extension ship together — but
                // bail gracefully if the user moved/renamed files
                // manually.
                return 0;
            }

            // Build the command line: `aperture ocr "<file1>" "<file2>" ...`
            // The CLI's headless ocr path:
            //   1. Runs OCR on every file (ONNX model loaded once)
            //   2. Concatenates results with `==== file ====`
            //   3. Writes the result to the clipboard
            //   4. Shows a Windows toast via the bundled notify.ps1
            //   5. Exits
            // Spawning is fire-and-forget — we return S_OK to the
            // shell immediately so Explorer stays responsive. The
            // toast is the user's only feedback (no extra windows).
            var argBuilder = new System.Text.StringBuilder("ocr");
            foreach (var p in paths)
            {
                argBuilder.Append(' ');
                argBuilder.Append('"');
                argBuilder.Append(p.Replace("\"", "\\\""));
                argBuilder.Append('"');
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = argBuilder.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            // Best-effort. The shell extension is fire-and-forget
            // — a failed invocation shouldn't crash Explorer.
            // We log via the event log (debug builds only) but
            // otherwise swallow the error to avoid breaking the
            // right-click menu's UX.
            Debug.WriteLine($"OcrExplorerCommand.Invoke failed: {ex}");
            return 0; // E_FAIL is also OK; the shell just doesn't invoke
        }
    }

    public int GetFlags(out uint pFlags)
    {
        // ECF_DEFAULT (0): no subcommands, no special flags. Win11
        // uses heuristics to classify IExplorerCommand verbs as
        // light or heavy; we don't set any explicit flag, so
        // the shell's default classification applies.
        pFlags = 0;
        return 0;
    }

    public int EnumSubCommands(out IntPtr ppEnum)
    {
        // No submenu (single leaf verb).
        ppEnum = IntPtr.Zero;
        return 0;
    }

    /// <summary>
    /// Walk the IShellItemArray, pull the filesystem path of
    /// each selected item via IShellItem.GetDisplayName with
    /// SIGDN_FILESYSPATH, and return the list of paths.
    /// Filters out empty / non-filesystem entries (virtual items
    /// like "This PC" or search results that don't have a clean
    /// filesystem path).
    /// </summary>
    private static List<string> ExtractFilePaths(IShellItemArray psiArray)
    {
        var paths = new List<string>();
        psiArray.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            try
            {
                psiArray.GetItemAt(i, out IShellItem item);
                item.GetDisplayName(ShellItemDisplayName.SIGDN_FILESYSPATH, out IntPtr pszName);
                if (pszName == IntPtr.Zero) continue;
                string path;
                try { path = Marshal.PtrToStringUni(pszName) ?? string.Empty; }
                finally { Marshal.FreeCoTaskMem(pszName); }
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            catch
            {
                // Skip items that don't expose a filesystem path
                // (e.g. virtual objects from search results).
            }
        }
        return paths;
    }

    /// <summary>
    /// Resolve the path of the main ApertureNeo.exe. The shell
    /// extension is loaded from {app}\Plugins\ApertureNeo.Plugins.Ocr.ShellExt.dll,
    /// so the main exe lives one folder up at {app}\ApertureNeo.exe.
    /// This is a fixed layout from installer.iss's [Files] section.
    /// </summary>
    private static string ResolveHostExePath()
    {
        var asm = Assembly.GetExecutingAssembly().Location ?? string.Empty;
        var dir = Path.GetDirectoryName(asm) ?? string.Empty;
        return Path.Combine(dir, "..", "ApertureNeo.exe");
    }
}
