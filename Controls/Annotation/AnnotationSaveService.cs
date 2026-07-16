using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using ApertureNeo.Services;
using Microsoft.Win32;
using SkiaSharp;

namespace ApertureNeo.Controls.Annotation;

/// <summary>
/// Save-orchestration service used by both the screenshot editor
/// and the main app's image-viewer annotation mode. Composes the
/// final image (original bitmap + annotation overlay) into a
/// single <see cref="Bitmap"/>, shows a folder-picker dialog, and
/// persists the user's chosen folder as the default save
/// directory via <see cref="ISettingsStore"/>.
///
/// Two save actions are supported:
///   - "另存为..." (default) — opens a SaveDialog with a
///     Browse button, lets the user pick a folder and edit
///     the filename.
///   - "覆盖原图" — available when a <c>currentFilePath</c> is
///     passed (the viewer case). Saves directly to that path
///     with no dialog.
///
/// The service is stateless — all dialog interaction is in
/// <see cref="SaveDialog"/>; this class just composes + persists.
/// </summary>
public sealed class AnnotationSaveService
{
    private readonly ISettingsStore? _settings;

    public AnnotationSaveService(ISettingsStore? settings = null)
    {
        _settings = settings;
    }

    /// <summary>Last save outcome. Drives the host's status
    /// bar text and the toast (if any).</summary>
    public enum SaveOutcome
    {
        Cancelled,
        Saved,
        Failed,
    }

    /// <summary>Last full path written. Null if cancelled.</summary>
    public string? LastSavedPath { get; private set; }

    /// <summary>
    /// Compose original + overlay, then prompt the user for a
    /// destination. <paramref name="currentFilePath"/>, if
    /// non-null, enables the "覆盖原图" button in the dialog.
    /// Otherwise only "另存为..." is available.
    /// </summary>
    public SaveOutcome Save(Bitmap originalBitmap, AnnotationState state, Window owner, string? currentFilePath = null)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        // Build the final composed bitmap: original + overlay.
        // For the editor case, original is the captured bitmap
        // (after potential cropping); for the viewer case it's
        // the loaded source image. In both cases, the overlay is
        // the user's strokes.
        Bitmap final;
        try
        {
            final = ComposeFinal(originalBitmap, state);
        }
        catch (Exception ex)
        {
            LastSavedPath = null;
            return SaveOutcome.Failed;
        }

        var defaultFolder = _settings?.DefaultScreenshotSaveDirectory
            ?? Path.GetDirectoryName(currentFilePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var defaultName = currentFilePath != null
            ? Path.GetFileName(currentFilePath)
            : $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png";

        var dlg = new SaveDialog(defaultFolder, defaultName, currentFilePath) { Owner = owner };
        if (dlg.ShowDialog() != true)
        {
            final.Dispose();
            LastSavedPath = null;
            return SaveOutcome.Cancelled;
        }

        try
        {
            final.Save(dlg.SelectedPath!, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            final.Dispose();
            LastSavedPath = null;
            return SaveOutcome.Failed;
        }
        finally
        {
            final.Dispose();
        }

        // Persist the chosen folder as the new default (if user
        // ticked "设为默认保存目录"). SettingsStore schedules a
        // debounced save, so rapid successive saves don't hammer
        // the disk.
        if (dlg.DefaultDirectoryToSet != null && _settings != null)
        {
            _settings.DefaultScreenshotSaveDirectory = dlg.DefaultDirectoryToSet;
        }

        LastSavedPath = dlg.SelectedPath;
        return SaveOutcome.Saved;
    }

    /// <summary>
    /// Compose <paramref name="original"/> with the overlay
    /// strokes into a new <see cref="Bitmap"/>. The caller owns
    /// the returned bitmap (use `using` or `Dispose`).
    /// </summary>
    private static Bitmap ComposeFinal(Bitmap original, AnnotationState state)
    {
        var result = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(original, 0, 0);
            using (var overlayGdi = SkBitmapToGdi(state.OverlayBitmap))
            {
                g.DrawImage(overlayGdi, 0, 0);
            }
        }
        return result;
    }

    private static unsafe Bitmap SkBitmapToGdi(SKBitmap skBitmap)
    {
        var w = skBitmap.Width;
        var h = skBitmap.Height;
        var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var size = data.Height * data.Stride;
            Buffer.MemoryCopy(skBitmap.GetPixels().ToPointer(), data.Scan0.ToPointer(), size, size);
        }
        finally
        {
            result.UnlockBits(data);
        }
        return result;
    }
}
