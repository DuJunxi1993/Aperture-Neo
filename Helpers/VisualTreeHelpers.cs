using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ApertureNeo.Helpers;

/// <summary>
/// Helpers for walking and inspecting the WPF visual tree. Pure
/// functions — no instance state, no UI access. Safe to call from
/// any thread that owns the visual tree being walked.
/// </summary>
public static class VisualTreeHelpers
{
    /// <summary>
    /// Walk the visual tree depth-first and return the first
    /// descendant of type <typeparamref name="T"/>. Used to
    /// locate the ListBox's internal ScrollViewer from code
    /// without keeping a direct XAML reference to it (its x:Name
    /// is on a ListBox template part, not directly accessible).
    /// </summary>
    public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Return true if <paramref name="node"/> is the same as
    /// <paramref name="ancestor"/> or a descendant of it in the
    /// visual tree. Both null returns false; either null is treated
    /// as "no relationship".
    /// </summary>
    public static bool IsDescendantOf(DependencyObject? node, DependencyObject? ancestor)
    {
        if (node == null || ancestor == null) return false;
        while (node != null)
        {
            if (node == ancestor) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>
    /// Walks the visual tree looking for a FluentWindow.ClientAreaBorder
    /// (an internal WPF-UI class) and returns it. WPF-UI's
    /// OnWindowStateChanged sets Padding to ~5px in Maximized state to
    /// keep the OS's "Aero" border visible — but our viewer is edge-to-
    /// edge, so the ~5px of transparent padding shows the window's
    /// SurfaceCanvas (#fafafa) tone around the white viewer, producing
    /// a 1-2px white-ish seam at every screen edge. We can't reference
    /// the type by name (it's internal), so we match on the class name
    /// in the visual tree and set the public Padding DP inherited from
    /// Border. This is invoked both synchronously inside ToggleFullscreen
    /// (so it beats the FluentWindow padding on the same dispatcher turn)
    /// and on DispatcherPriority.Loaded (catches the case where the
    /// FluentWindow sets padding after we do).
    /// </summary>
    public static Border? FindClientAreaBorder(DependencyObject root)
    {
        if (root == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border b && b.GetType().Name == "ClientAreaBorder")
                return b;
            var deeper = FindClientAreaBorder(child);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
