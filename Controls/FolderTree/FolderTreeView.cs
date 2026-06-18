using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Models;
using ApertureNeo.Services;
using ApertureNeo.Helpers;

namespace ApertureNeo.Controls.FolderTree;

/// <summary>
/// Where a folder node lives in the tree. Drives the
/// "should this be added to recent / favorites?" decision
/// in the navigation service — only <see cref="Subdirectory"/>
/// rows go through that gate.
/// </summary>
public enum FolderSource
{
    Favorite,
    Recent,
    Drive,
    Subdirectory
}

/// <summary>
/// Three-section folder tree (Favorites / Recent / ThisPC) with
/// drill-into-subfolder navigation. Drilling pushes the current
/// view onto an internal stack; <see cref="NavigateBack"/> pops.
/// Right-click on a node shows an "add/remove favorite" context
/// menu. A read-only <see cref="SyncFrom"/> clone is used inside
/// the floating tree popup.
/// </summary>
public class FolderTreeView : ItemsControl
{
    public new ObservableCollection<TreeNodeBase> Items { get; } = new();
    private readonly Stack<(List<TreeNodeBase> items, string? parentPath)> _navStack = new();

    /// <summary>
    /// Pop the drill stack back to the top-level
    /// (Favorites/Recent/ThisPC) view. Fires
    /// <see cref="DrillModeChanged"/> if the stack was non-empty
    /// before the call so the host window can update the
    /// "back to root" floating chip.
    /// </summary>
    public void ReturnToRoot()
    {
        bool wasInDrill = _navStack.Count > 0;
        _navStack.Clear();
        _pendingRecentRefresh = false;
        Init();
        if (wasInDrill) DrillModeChanged?.Invoke();
    }

    /// <summary>
    /// No-op in this control (drilling is the navigation primitive,
    /// not tree expand/collapse) but exposed so the title bar's
    /// "collapse all" affordance can hook into a sensible handler
    /// when the tree model grows one. Today it just returns to root.
    /// </summary>
    public void CollapseAll() => ReturnToRoot();

    /// <summary>
    /// When true, the control renders items but ignores clicks.
    /// Used by the floating-popup clone so the inline tree remains
    /// the single source of truth for selection.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Mirror the visible node list of another tree so this control
    /// can act as a read-only clone (e.g. inside a floating popup).
    /// The clone keeps its own Items collection and its own
    /// SelectedNode pointer; the rest of the application talks to
    /// the inline tree, not the clone, so the clone is just a
    /// view. Callers should not subscribe to the clone's events.
    /// </summary>
    /// <summary>
    /// Mirror the visible node list and selection of another
    /// tree into this one, then mark this control as read-only.
    /// Used by the floating tree popup so it can render the same
    /// items the inline tree shows without subscribing to
    /// favorite/recent change events.
    /// </summary>
    public void SyncFrom(FolderTreeView source)
    {
        if (source == null || ReferenceEquals(source, this)) return;
        IsReadOnly = true;
        Items.Clear();
        foreach (var n in source.Items) Items.Add(n);
        SelectedNode = source.SelectedNode;
    }

    public bool IsInDrillMode => _navStack.Count > 0;
    public event Action? DrillModeChanged;

    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.Register(nameof(SelectedNode), typeof(TreeNodeBase), typeof(FolderTreeView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedNodeChanged));

    public TreeNodeBase? SelectedNode
    {
        get => (TreeNodeBase?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    private static void OnSelectedNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderTreeView fv) fv.RefreshContainerSelection();
    }

    private void RefreshContainerSelection()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is FolderItemContainer fc)
                fc.IsSelected = (fc.DataContext == SelectedNode);
        }
        Dispatcher.BeginInvoke(new Action(RefreshContainerSelection),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    public event Action<FolderSource, string>? FolderSelected;

    public FolderTreeView()
    {
        ItemsSource = Items;
        Loaded += (_, _) => Init();
        App.SettingsStore.FavoritesChanged += RefreshFavorites;
        App.SettingsStore.RecentChanged += RefreshRecent;
    }

    private bool _pendingRecentRefresh;

    protected override bool IsItemItsOwnContainerOverride(object item) => item is FolderItemContainer;
    protected override DependencyObject GetContainerForItemOverride() => new FolderItemContainer();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is FolderItemContainer fc)
            fc.IsSelected = (item == SelectedNode);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsReadOnly) { base.OnPreviewMouseLeftButtonDown(e); return; }
        base.OnPreviewMouseLeftButtonDown(e);
        var el = e.OriginalSource as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is TreeNodeBase node)
            {
                HandleClick(node);
                e.Handled = true;
                return;
            }
            el = VisualTreeHelper.GetParent(el);
        }
    }

    private void HandleClick(TreeNodeBase node)
    {
        if (node.IsSectionHeader) return;
        if (node is EmptyHintNode) return;
        if (string.IsNullOrEmpty(node.Path)) return;

        SelectedNode = node;

        // Favorites / Recent items or leaf directories — direct load, no drill
        if (!IsInDrillMode && !(node is DriveItemNode))
        {
            FolderSelected?.Invoke(ResolveSourceForNode(node), node.Path!);
            return;
        }

        // Leaf directories — load images without drilling
        if (!HasSubdirectories(node.Path))
        {
            FolderSelected?.Invoke(ResolveSourceForNode(node), node.Path!);
            return;
        }

        NavigateInto(node);
    }

    private static bool HasSubdirectories(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            return Directory.EnumerateDirectories(path).Any();
        }
        catch { return false; }
    }

    private void NavigateInto(TreeNodeBase node)
    {
        SelectedNode = node;
        _navStack.Push((Items.ToList(), null));
        Items.Clear();
        // "Back" is no longer injected as a fake tree node — the
        // floating chip in the sidebar (BtnTreeBack) handles the
        // back action and is always visible while we're in drill
        // mode, so duplicating it inside the tree list is redundant.
        var children = GetChildren(node);
        foreach (var c in children) Items.Add(c);
        var path = node.Path;
        if (path != null)
        {
            FolderSelected?.Invoke(FolderSource.Subdirectory, path);
        }
        DrillModeChanged?.Invoke();
    }

    /// <summary>
    /// Pop one frame off the navigation stack and restore the previous
    /// tree contents. Public so external controls (e.g. the sidebar
    /// "Back" floating chip in MainWindow.xaml) can drive the same
    /// back action without going through the deprecated BackNode tree
    /// entry. Idempotent when called at the root (no-op).
    /// </summary>
    public async void NavigateBack()
    {
        if (_navStack.Count == 0) return;
        var stackBefore = _navStack.Count;
        var (previous, _) = _navStack.Pop();
        Items.Clear();
        SelectedNode = null;
        foreach (var r in previous) Items.Add(r);
        if (_pendingRecentRefresh) RefreshRecent();
        DrillModeChanged?.Invoke();

        // Popped back to root view (stack now empty): run the same
        // Recent-priority auto-select that Init() uses, so the user
        // lands on the most-recent Recent node rather than a blank
        // selection. Mid-drill pops (stack still non-empty after the
        // pop) leave SelectedNode = null — the current folder isn't
        // in the parent's children list, so there's nothing useful
        // to highlight.
        if (stackBefore == 1)
        {
            Dispatcher.BeginInvoke(new Action(SelectFirstNode), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Auto-select the most relevant top-level node. Prefers the
    /// first Recent (matches "where was I just?") over the first
    /// Favorite. Falls back to the first non-header item if Recent
    /// is empty. Used by both <see cref="Init"/> and
    /// <see cref="NavigateBack"/> (when the back stack empties).
    /// </summary>
    private void SelectFirstNode()
    {
        foreach (var item in Items)
        {
            if (item is RecentNode)
            {
                SelectedNode = item;
                ScrollSelectedIntoView();
                return;
            }
        }
        foreach (var item in Items)
        {
            if (item is FolderItemNode || item is RecentNode || item is DriveItemNode)
            {
                SelectedNode = item;
                ScrollSelectedIntoView();
                return;
            }
        }
    }

    private static IEnumerable<TreeNodeBase> GetChildren(TreeNodeBase node)
    {
        var list = new List<TreeNodeBase>();
        try
        {
            if (string.IsNullOrEmpty(node.Path) || !Directory.Exists(node.Path)) return list;
            var dirs = Directory.EnumerateDirectories(node.Path)
                .Where(p => (new DirectoryInfo(p).Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs) list.Add(new FolderItemNode(d));
        }
        catch { }
        return list;
    }

    private FolderSource? FindParentSection(TreeNodeBase node)
    {
        int idx = -1;
        for (int i = 0; i < Items.Count; i++)
            if (Items[i] == node) { idx = i; break; }
        if (idx < 0) return null;
        for (int i = idx - 1; i >= 0; i--)
            if (Items[i] is SectionHeaderNode sh)
            {
                return sh.DisplayName switch
                {
                    "收藏夹" => FolderSource.Favorite,
                    "最近访问" => FolderSource.Recent,
                    "此电脑" => FolderSource.Drive,
                    _ => null
                };
            }
        return null;
    }

    private FolderSource ResolveSourceForNode(TreeNodeBase node)
    {
        var section = FindParentSection(node);
        if (section.HasValue) return section.Value;
        return FolderSource.Subdirectory;
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (IsReadOnly) { base.OnPreviewMouseRightButtonUp(e); return; }
        base.OnPreviewMouseRightButtonUp(e);
        var el = e.OriginalSource as DependencyObject;
        TreeNodeBase? node = null;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.DataContext is TreeNodeBase n) { node = n; break; }
            el = VisualTreeHelper.GetParent(el);
        }
        if (node == null || string.IsNullOrEmpty(node.Path)) return;

        var menu = new ContextMenu();
        var section = FindParentSection(node);

        if (section == FolderSource.Favorite)
        {
            var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
            remove.Click += (_, _) => App.SettingsStore.RemoveFavorite(node.Path);
            menu.Items.Add(remove);
        }
        else if (section == FolderSource.Recent)
        {
            if (App.SettingsStore.IsFavorite(node.Path))
            {
                var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
                remove.Click += (_, _) => App.SettingsStore.RemoveFavorite(node.Path);
                menu.Items.Add(remove);
            }
            else
            {
                var add = new MenuItem { Header = "添加到收藏夹" };
                add.Click += (_, _) => App.SettingsStore.AddFavorite(node.Path);
                menu.Items.Add(add);
            }
            menu.Items.Add(new Separator());
            var removeFromRecent = new MenuItem { Header = "从最近访问移除", Tag = "destructive" };
            removeFromRecent.Click += (_, _) => App.SettingsStore.RemoveRecent(node.Path);
            menu.Items.Add(removeFromRecent);
        }
        else
        {
            if (App.SettingsStore.IsFavorite(node.Path))
            {
                var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
                remove.Click += (_, _) => App.SettingsStore.RemoveFavorite(node.Path);
                menu.Items.Add(remove);
            }
            else
            {
                var add = new MenuItem { Header = "添加到收藏夹" };
                add.Click += (_, _) => App.SettingsStore.AddFavorite(node.Path);
                menu.Items.Add(add);
            }
        }

        var open = new MenuItem { Header = "在资源管理器中打开" };
        open.Click += (_, _) => ShellHelper.RevealInExplorer(node.Path!);
        menu.Items.Add(open);

        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ---- Section refresh ----

    private void RefreshFavorites()
    {
        if (IsInDrillMode) return;
        int idx = FindSection("收藏夹");
        if (idx < 0) { Init(); return; }
        int next = FindNextSection(idx);
        for (int i = next - 1; i > idx; i--) Items.RemoveAt(i);
        int ins = idx + 1;
        int addedFavs = 0;
        foreach (var p in App.SettingsStore.Favorites)
        {
            if (Directory.Exists(p)) { Items.Insert(ins++, new FolderItemNode(p)); addedFavs++; }
        }
        if (addedFavs == 0) Items.Insert(ins, new EmptyHintNode("暂无收藏"));
    }

    private void RefreshRecent()
    {
        if (IsInDrillMode)
        {
            _pendingRecentRefresh = true;
            return;
        }
        _pendingRecentRefresh = false;
        int idx = FindSection("最近访问");
        if (idx < 0) { Init(); return; }
        int next = FindNextSection(idx);
        for (int i = next - 1; i > idx; i--) Items.RemoveAt(i);
        int ins = idx + 1;
        int addedRecs = 0;
        foreach (var e in App.SettingsStore.Recent)
        {
            if (Directory.Exists(e.Path)) { Items.Insert(ins++, new RecentNode(e)); addedRecs++; }
        }
        if (addedRecs == 0) Items.Insert(ins, new EmptyHintNode("暂无最近访问"));
    }

    private int FindSection(string name)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i] is SectionHeaderNode sh && sh.DisplayName == name) return i;
        return -1;
    }

    private int FindNextSection(int from)
    {
        for (int i = from + 1; i < Items.Count; i++)
            if (Items[i] is SectionHeaderNode) return i;
        return Items.Count;
    }

    // ---- Page Up/Down ----

    /// <summary>
    /// Walk the visible (top-level) folder list to find
    /// <paramref name="currentPath"/>, then select the next or
    /// previous folder (wrapping). Returns false if
    /// <paramref name="currentPath"/> isn't in the list. Bound to
    /// PageUp/PageDown in the host window's key handler.
    /// </summary>
    public bool NavigateToAdjacentFolder(string currentPath, bool forward)
    {
        var all = Items.Where(i => i.Path != null && !(i is SectionHeaderNode)).ToList();
        if (all.Count == 0) return false;
        int idx = -1;
        for (int i = 0; i < all.Count; i++)
            if (all[i].Path!.Equals(currentPath, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        if (idx < 0) return false;
        int t = forward ? (idx + 1) % all.Count : (idx - 1 + all.Count) % all.Count;
        var target = all[t];
        SelectedNode = target;
        ScrollSelectedIntoView();
        var path = target.Path;
        if (path != null) FolderSelected?.Invoke(ResolveSourceForNode(target), path);
        return true;
    }

    private void ScrollSelectedIntoView()
    {
        if (SelectedNode == null) return;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] == SelectedNode)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                        fe.BringIntoView();
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
        }
    }

    // ---- Init ----

    private int _initGeneration;

    private void Init()
    {
        var gen = Interlocked.Increment(ref _initGeneration);

        Items.Clear();
        var fav = new SectionHeaderNode("收藏夹") { IsFirstSection = true }; fav.Icon = "";
        var rec = new SectionHeaderNode("最近访问"); rec.Icon = "";
        var pc = new SectionHeaderNode("此电脑"); pc.Icon = "";

        Items.Add(fav);
        int addedFavs = 0;
        foreach (var p in App.SettingsStore.Favorites)
        {
            if (Directory.Exists(p)) { Items.Add(new FolderItemNode(p)); addedFavs++; }
        }
        if (addedFavs == 0) Items.Add(new EmptyHintNode("暂无收藏"));

        Items.Add(rec);
        int addedRecs = 0;
        foreach (var e in App.SettingsStore.Recent)
        {
            if (Directory.Exists(e.Path)) { Items.Add(new RecentNode(e)); addedRecs++; }
        }
        if (addedRecs == 0) Items.Add(new EmptyHintNode("暂无最近访问"));

        Items.Add(pc);

        _ = LoadDrivesAsync(gen);

        // Auto-select the most relevant top-level node. Prefer the
        // first Recent ("where was I just?") over the first Favorite.
        Dispatcher.BeginInvoke(new Action(SelectFirstNode), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task LoadDrivesAsync(int gen)
    {
        var drives = await Task.Run(() =>
            DriveInfo.GetDrives()
                     .Where(d => d.IsReady)
                     .Select(d => new DriveItemNode(d))
                     .ToList());

        await Dispatcher.InvokeAsync(() =>
        {
            if (gen != Volatile.Read(ref _initGeneration)) return;
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i] is DriveItemNode) Items.RemoveAt(i);
            int ins = FindSection("此电脑") + 1;
            foreach (var d in drives)
                Items.Insert(ins++, d);
        });
    }
}

internal sealed class RecentNode : TreeNodeBase
{
    public RecentNode(RecentEntry entry)
    {
        Path = entry.Path;
        DisplayName = System.IO.Path.GetFileName(entry.Path);
        if (string.IsNullOrEmpty(DisplayName)) DisplayName = entry.Path;
        Icon = "📁";
    }
}

internal sealed class FolderItemContainer : ContentControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(FolderItemContainer),
            new PropertyMetadata(false));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public FolderItemContainer() { Focusable = false; }
}

internal sealed class EmptyHintNode : TreeNodeBase
{
    public EmptyHintNode(string text) { DisplayName = text; Path = null; IsSectionHeader = true; IsEmptyHint = true; }
}
