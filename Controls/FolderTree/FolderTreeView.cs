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
using Microsoft.Extensions.DependencyInjection;

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
    // Round X (tree-stack split): the second slot now carries the
    // directory-tree PARENT path — the directory the user is about to
    // drill INTO, which becomes the parent of the new Items view.
    // NavigateBack reads this to load that parent folder's images so
    // the thumbnail grid stays in sync with the tree. Previously the
    // slot was (items, loadedFolder) and NavigateBack loaded the
    // folder the user was viewing BEFORE the drill — which is a
    // history-stack semantics (resource-manager "back") leaking into
    // the "up one level" button.
    // Round Z: the browse history (back/forward) now lives in
    // NavigationService (visit list + cursor, see RecordVisit + GoBack
    // / GoForward) — recording inside this control could only ever see
    // tree-initiated navigation, while NavigationService.LoadFolder is
    // the funnel every navigation source passes through. This control
    // is now pure UI: it emits FolderSelected with a recordHistory flag
    // that the host threads through to LoadFolder. Back/Forward are
    // driven by MainWindow (GoBack/GoForward + ReDrillToPath) and the
    // "up" button re-records the parent as a fresh visit so Back after
    // Up returns to the child.
    private readonly Stack<(List<TreeNodeBase> items, string? parentPath)> _navStack = new();

    /// <summary>
    /// Pop the drill stack back to the top-level
    /// (Favorites/Recent/ThisPC) view. Fires
    /// <see cref="DrillModeChanged"/> if the stack was non-empty
    /// before the call so the host window can update the
    /// "back to root" floating chip. Pass
    /// <paramref name="skipAutoSelect"/>=true to suppress the
    /// deferred <c>SelectFirstNode</c> — useful when the caller
    /// will drive selection itself (e.g.
    /// <see cref="JumpToDirectory"/>).
    /// </summary>
    public void ReturnToRoot(bool skipAutoSelect = false)
    {
        bool wasInDrill = _navStack.Count > 0;
        _navStack.Clear();
        // Round Z: home no longer clears the browse history —
        // Explorer keeps its back/forward trail when the user returns
        // to a top-level location, so Back stays available after
        // home and after a favorites jump. The "home = fresh start"
        // behaviour (clear first drill stack only) lives here.
        _pendingRecentRefresh = false;
        Init(skipAutoSelect);
        if (wasInDrill) DrillModeChanged?.Invoke();

        // P2 fix: reload the most-recent folder's images so the
        // thumbnail grid updates when the user returns to the
        // root view. SelectFirstNode (called by Init) only sets
        // SelectedNode on the Recent SECTION HEADER — a section
        // header click is a no-op per HandleClick, so no
        // FolderSelected would fire. We fire explicitly here
        // using SettingsStore.Recent[0] (newest first), guarded
        // on skipAutoSelect so an out-of-drill programmatic call
        // (e.g. JumpToDirectory's pre-clear, which passes
        // skipAutoSelect:true) doesn't clobber the
        // currently-loaded folder. Round AA: the wasInDrill gate
        // is gone — Back into the home view arrives with the
        // drill stack already reset (ReDrillToPath), so the
        // "most recent = last genuine visit" restore must run
        // regardless of drill state.
        //
        // Round X: do NOT record this in the browse history — it's
        // an automatic restore that mirrors what the user just saw
        // before drilling, so pushing it would let "back" send them
        // right back into the drill they just exited (no-op loop).
        // It also never counts as a recent visit (trackRecent:false)
        // — only genuine user navigations enter the recent list.
        if (!skipAutoSelect)
        {
            var mostRecent = _settingsStore.Recent.FirstOrDefault();
            if (mostRecent != null && Directory.Exists(mostRecent.Path))
            {
                NavigateTo(FolderSource.Recent, mostRecent.Path, recordHistory: false, trackRecent: false);
            }
        }
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
        bool missingContainer = false;
        for (int i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is not FolderItemContainer fc)
            {
                // Not realized yet — schedule one follow-up pass so a
                // lazily realized container still picks up the current
                // selection.
                missingContainer = true;
                continue;
            }
            fc.IsSelected = (fc.DataContext == SelectedNode);
        }
        if (missingContainer)
        {
            Dispatcher.BeginInvoke(new Action(RefreshContainerSelection),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Raised on every emitted folder change. Parameters:
    /// source, path, <c>recordHistory</c> (history recording in
    /// NavigationService via LoadFolder) and <c>trackRecent</c>
    /// (whether the folder counts as a genuine user visit for the
    /// "最近访问" list — mouse clicks, hotkey navigation, jumps;
    /// NOT Up/Back/Forward restores, which are excluded so the
    /// recent list + home highlight reflect only what the user
    /// chose to browse).
    /// </summary>
    public event Action<FolderSource, string, bool, bool>? FolderSelected;

    /// <summary>
    /// WPF / XAML instantiation path. Delegates to the
    /// DI-injected constructor by resolving the default
    /// <see cref="ISettingsStore"/> from <see cref="AppHost.Services"/>.
    /// Throws at design time / before the host is built — that's
    /// intentional (the tree is meaningless without settings).
    /// </summary>
    public FolderTreeView() : this(AppHost.Services?.GetService<ISettingsStore>()
        ?? throw new InvalidOperationException(
            "FolderTreeView requires ISettingsStore from AppHost; " +
            "ensure App.OnStartup called AppHost.Build() before any tree is instantiated."))
    {
    }

    /// <summary>
    /// Test-friendly ctor. Production code goes through the
    /// parameterless ctor; tests pass a mock
    /// <see cref="ISettingsStore"/> directly.
    /// </summary>
    internal FolderTreeView(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        ItemsSource = Items;
        Loaded += (_, _) => Init();
        _settingsStore.FavoritesChanged += RefreshFavorites;
        _settingsStore.RecentChanged += RefreshRecent;
        // P2 fix: self-subscribe so CurrentLoadedFolder tracks
        // every FolderSelected fire (direct click, drill,
        // JumpToDirectory, PageUp/Down) without per-site updates.
        // Self-subscription is added FIRST here, so this handler
        // runs before the external subscribers (FolderTreePanelVM,
        // MainWindow) when FolderSelected fires — the external
        // handlers see CurrentLoadedFolder already updated.
        FolderSelected += (_, path, _, _) => CurrentLoadedFolder = path;
    }

    private readonly ISettingsStore _settingsStore;

    /// <summary>The folder whose images are currently loaded in
    /// the thumbnail grid. Updated automatically by the ctor's
    /// self-subscription to <see cref="FolderSelected"/>. Read by
    /// <see cref="NavigateInto"/> (pushed onto the drill stack)
    /// and <see cref="NavigateBack"/> (popped and re-fired) so
    /// back-navigation restores the previous folder's images.
    /// <see cref="ReturnToRoot"/> also reads it to decide whether
    /// the most-recent folder reload is meaningful.</summary>
    public string? CurrentLoadedFolder { get; private set; }

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
            NavigateTo(ResolveSourceForNode(node), node.Path!, recordHistory: true, trackRecent: true);
            return;
        }

        // Leaf directories — load images without drilling
        if (!HasSubdirectories(node.Path))
        {
            // Round X (tree-stack split): in drill mode, the leaf
            // click is a "virtual drill" — push a frame whose
            // parentPath is the leaf's parent (= the directory the
            // user is currently looking at) so that "up one level"
            // returns here. Previously the parentPath slot held
            // loadedFolder (history semantics) which made "up one
            // level" sometimes skip the actual parent. The at-root
            // leaf / Recent / Favorite case is handled by the first
            // if (gated on !IsInDrillMode), so this push only fires
            // when the user is genuinely inside a drill hierarchy.
            if (IsInDrillMode)
            {
                _navStack.Push((Items.ToList(), CurrentLoadedFolder));
            }
            NavigateTo(ResolveSourceForNode(node), node.Path!, recordHistory: true, trackRecent: true);
            return;
        }

        NavigateInto(node);
    }

    /// <summary>
    /// Single funnel for all FolderSelected emissions. Routes
    /// through <see cref="FolderSelected"/> and carries two flags
    /// to the host:
    ///   - <paramref name="recordHistory"/>: whether
    ///     <c>NavigationService.LoadFolder</c> records this as a
    ///     browse-history visit (true = user navigation / Up;
    ///     false = automatic restore by Back / Forward / home);
    ///   - <paramref name="trackRecent"/>: whether this counts as
    ///     a genuine user visit for the "最近访问" list (true =
    ///     tree clicks, drills, jumps, PageUp/Down; false = Up
    ///     parent restore and Back/Forward re-drills — a button-
    ///     navigated folder must not become a "recent visit").
    ///
    /// Round Z: this control no longer records history itself — the
    /// visit list + cursor lives in NavigationService and every
    /// navigation source funnels through its LoadFolder, so a tree-
    /// local stack could only ever see a subset of navigations.
    /// </summary>
    private void NavigateTo(FolderSource source, string path, bool recordHistory, bool trackRecent)
    {
        FolderSelected?.Invoke(source, path, recordHistory, trackRecent);
    }

    /// <summary>
    /// Rebuild the tree so <paramref name="path"/> becomes the
    /// current browse location: tree drilled to the target's parent
    /// level with the target node selected + scrolled into view and
    /// the target's images loaded through <see cref="FolderSelected"/>
    /// (recordHistory:false — retracing must not re-record). Called by
    /// the host (MainWindow) after
    /// <c>NavigationService.GoBack()</c> / <c>GoForward()</c>, which
    /// own the browse-history cursor (Round Z). When the target can't
    /// be re-drilled (deleted folder, UNC path with no drive tree) the
    /// tree falls back to the root view but the folder is STILL loaded
    /// — Explorer opens such folders too, it just can't sync the tree.
    /// </summary>
    public bool ReDrillToPath(string path)
    {
        // ResetTreeForDrill must NOT touch the folder navigation —
        // history is external now, but the drill stack reset + root
        // view are still this control's job.
        ResetTreeForDrill();
        bool drilled = DrillToPathSelectable(path);
        NavigateTo(FolderSource.Subdirectory, path, recordHistory: false, trackRecent: false);
        return drilled;
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

    private void NavigateInto(TreeNodeBase node, bool fireFolderSelected = true)
    {
        SelectedNode = node;
        // Round X (tree-stack split): push a frame whose parentPath
        // is the directory being drilled INTO (= the new view's
        // parent in tree terms). NavigateBack loads that path on
        // pop so "up one level" lands on the actual directory-tree
        // parent, regardless of what the user was viewing before
        // the drill. Previous behavior used CurrentLoadedFolder
        // (history semantics) which let "up one level" skip the
        // parent.
        _navStack.Push((Items.ToList(), node.Path));
        Items.Clear();
        // "Back" is no longer injected as a fake tree node — the
        // floating chip in the sidebar (BtnTreeBack) handles the
        // back action and is always visible while we're in drill
        // mode, so duplicating it inside the tree list is redundant.
        var children = GetChildren(node);
        foreach (var c in children) Items.Add(c);
        var path = node.Path;
        if (path != null && fireFolderSelected)
        {
            NavigateTo(FolderSource.Subdirectory, path, recordHistory: true, trackRecent: true);
        }
        DrillModeChanged?.Invoke();
    }

    /// <summary>
    /// Navigate the tree to <paramref name="path"/> by drilling
    /// from root through the matching drive and subdirectory nodes
    /// until the deepest existing ancestor is reached. The target
    /// folder becomes the selected node and its images are loaded
    /// via the <see cref="FolderSelected"/> event. If the path is
    /// no longer reachable (folder renamed/deleted, drive missing)
    /// the tree stops at the deepest ancestor that still exists —
    /// no error is shown.
    /// </summary>
    /// <param name="path">Absolute path of a folder under one of
    /// the currently-mounted drives. Typically the
    /// <see cref="TreeNodeBase.Path"/> of a <see cref="RecentNode"/>
    /// or a favorite <see cref="FolderItemNode"/>.</param>
    public void JumpToDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!Directory.Exists(path)) return;

        // Jump = fresh drill from the root view. Round Z: the browse
        // history is NOT cleared — Explorer keeps the trail when the
        // user jumps to a favorite, so Back returns to the location
        // before the jump. The final NavigateTo(recordHistory:true)
        // records the target as a normal visit via LoadFolder.
        ReturnToRoot(skipAutoSelect: true);

        if (DrillToPathSelectable(path))
        {
            // Load the target folder's images into the main viewer.
            // Use the original path so casing/whitespace is preserved.
            NavigateTo(FolderSource.Subdirectory, path, recordHistory: true, trackRecent: true);
        }
    }

    /// <summary>
    /// Rebuild the ''navStack'' drill chain so that
    /// <paramref name="path"/> becomes the selected, visible node:
    /// from the (already-reset) root view, drill through the drive
    /// and every path segment except the last, then select the
    /// target and push the final frame. Does NOT fire
    /// <see cref="FolderSelected"/> for the target — the caller
    /// does (once, with its own recordHistory flag). Returns false
    /// when the path is unreachable (missing drive, deleted
    /// segment, drive root with no images), leaving the tree in
    /// whatever drill state the failure allowed.
    /// </summary>
    private bool DrillToPathSelectable(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

        var driveRoot = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(driveRoot)) return false;

        DriveItemNode? driveNode = null;
        foreach (var item in Items)
        {
            if (item is DriveItemNode d && d.Path != null &&
                d.Path.Equals(driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                driveNode = d;
                break;
            }
        }

        // Cold-start race: LoadDrivesAsync runs on a background task
        // and may not have populated Items yet when the user clicks
        // immediately after launch. Build the DriveItemNode
        // synchronously from DriveInfo so the jump still works.
        if (driveNode == null)
        {
            try
            {
                var di = new DriveInfo(driveRoot);
                if (di.IsReady) driveNode = new DriveItemNode(di);
            }
            catch { /* unknown drive letter / IO error — give up */ }
        }
        if (driveNode == null) return false;

        if (!HasSubdirectories(driveNode.Path!)) return false;
        NavigateInto(driveNode, fireFolderSelected: false);

        var relative = path.Substring(driveRoot.Length)
                           .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = string.IsNullOrEmpty(relative)
            ? Array.Empty<string>()
            : relative.Split(new[] {
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            // Target is the drive root itself (e.g. "C:\"). Nothing
            // to highlight — the drive is no longer in Items after
            // the drill, and a drive root has no images to load.
            return false;
        }

        // Drill through every segment EXCEPT the last. After this
        // loop, Items = children of the target's parent, and the
        // target folder itself is one of those children — so it can
        // be selected, scrolled into view, and acted on. Drilling
        // into the target itself would remove it from Items and
        // make it unselectable.
        //
        // For a 1-segment path (e.g. "C:\Users") the loop body
        // never runs; the target sits in Items as a direct child
        // of the drive, which is the same Items we just drilled
        // into above. Same outcome. In that case parentMatchForLastPush
        // stays null and we fall back to the drive root below.
        string? parentMatchForLastPush = null;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            FolderItemNode? match = null;
            foreach (var item in Items)
            {
                if (item is FolderItemNode f &&
                    string.Equals(System.IO.Path.GetFileName(f.Path), seg, StringComparison.OrdinalIgnoreCase))
                {
                    match = f;
                    break;
                }
            }
            if (match == null) return false;
            if (!HasSubdirectories(match.Path!)) return false;
            NavigateInto(match, fireFolderSelected: false);
            // P2 fix: self-subscribe only updates
            // CurrentLoadedFolder when FolderSelected fires, but
            // these intermediate drills suppress the fire (to
            // avoid loading the intermediate folders' images).
            // Update explicitly here so the NEXT push (and the
            // final target-push below) captures this drill
            // level as the "loaded folder" — i.e. the level
            // the user would have been viewing if the jump
            // had been a manual drill.
            CurrentLoadedFolder = match.Path;
            parentMatchForLastPush = match.Path;
        }

        // Find the target folder as a child of the current Items.
        var lastSeg = segments[segments.Length - 1];
        bool targetFound = false;
        foreach (var item in Items)
        {
            if (item is FolderItemNode f &&
                string.Equals(System.IO.Path.GetFileName(f.Path), lastSeg, StringComparison.OrdinalIgnoreCase))
            {
                SelectedNode = f;
                ScrollSelectedIntoView();
                targetFound = true;
                break;
            }
        }
        if (!targetFound) return false;

        // P2 fix: push a virtual frame for the final target so
        // back-navigation from the target returns to the drill
        // level (the target's parent in the tree), not the
        // level the loop just drilled into. Round X: the
        // parentPath slot now holds the directory-tree parent of
        // the target (= segments[last-1] when there are >=2
        // segments, else the drive root). Mirrors 2aeeaf6's
        // leaf-in-drill push: any "entry into a folder from the
        // tree" leaves a back frame whose parentPath is the
        // directory-tree parent.
        _navStack.Push((Items.ToList(), parentMatchForLastPush ?? driveNode.Path));
        return true;
    }

    /// <summary>
    /// Reset the tree-drill state to the root view WITHOUT touching
    /// the folder navigation (the browse-history cursor lives in
    /// NavigationService, Round Z). Shares <see cref="ReturnToRoot"/>'s
    /// early steps but skips its most-recent-folder auto-restore —
    /// used by <see cref="ReDrillToPath"/> so a Back/Forward re-drill
    /// starts from a clean root view before drilling to the target.
    /// </summary>
    private void ResetTreeForDrill()
    {
        bool wasInDrill = _navStack.Count > 0;
        _navStack.Clear();
        _pendingRecentRefresh = false;
        Init(skipAutoSelect: true);
        if (wasInDrill) DrillModeChanged?.Invoke();
    }

    /// <summary>
    /// Pop one frame off the navigation stack and restore the
    /// previous tree contents. Public so external controls (e.g.
    /// the sidebar "up one level" floating chip in MainWindow.xaml)
    /// can drive the same action without going through the
    /// deprecated BackNode tree entry. Idempotent when called at the
    /// root (no-op).
    /// </summary>
    /// <remarks>
    /// Round X: previously this method also re-loaded the folder the
    /// user was viewing BEFORE the drill (history semantics — Round Z
    /// moved that to NavigationService's browse-history cursor, with
    /// Back/Forward orchestrated by MainWindow). Now it loads the
    /// directory-tree PARENT (the node that was drilled INTO) so the
    /// thumbnail grid stays in sync with the restored tree view.
    /// The semantics is now strictly "up one directory level" rather
    /// than the previous "back to where I was browsing from" (which
    /// could skip levels when the user drilled into a sibling).
    /// Round Y: the restored parent node is also SELECTED in the
    /// restored view, so the tree highlights the folder whose
    /// thumbnails are shown.
    /// </remarks>
    public void NavigateBack()
    {
        if (_navStack.Count == 0) return;
        var stackBefore = _navStack.Count;
        var (previous, parentPath) = _navStack.Pop();
        Items.Clear();
        SelectedNode = null;
        foreach (var r in previous) Items.Add(r);
        if (_pendingRecentRefresh) RefreshRecent();
        DrillModeChanged?.Invoke();

        // Load the directory-tree parent's images so the thumbnail
        // grid follows the restored tree view. Round Z: recordHistory
        // is TRUE — Explorer treats "up one level" as a fresh visit,
        // so the next Back returns to the child directory the user
        // just left instead of re-loading the same parent (no-op).
        // The visit-list cursor in NavigationService dedupes when the
        // parent is already at the cursor.
        //
        // Round Y: the parentPath node IS in the restored Items
        // (it's the folder that was drilled INTO), so highlight it
        // — the thumbnail grid then corresponds to the highlighted
        // tree row. Previously selection was always nulled here
        // (Round X stale logic from when the slot held the pre-drill
        // folder, which was NOT in the restored view). Virtual
        // leaf-drill frames have parentPath = the level being
        // viewed, which has no row in its own children list — in
        // that case leaving selection null matches the "viewing
        // this folder's contents" state.
        bool highlighted = false;
        if (!string.IsNullOrEmpty(parentPath) && Directory.Exists(parentPath))
        {
            foreach (var item in Items)
            {
                if (item.Path != null &&
                    item.Path.Equals(parentPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedNode = item;
                    ScrollSelectedIntoView();
                    highlighted = true;
                    break;
                }
            }
            NavigateTo(FolderSource.Subdirectory, parentPath, recordHistory: true, trackRecent: false);
        }

        // Popped back to root view (stack now empty): run the same
        // Recent-priority auto-select that Init() uses, so the user
        // lands on the most-recent Recent node rather than a blank
        // selection — unless Up already highlighted the folder it
        // returned to (e.g. drilling back out to the root from a
        // drive: the drive node is in the root view and already
        // selected above; SelectFirstNode would override it).
        if (stackBefore == 1 && !highlighted)
        {
            // If drives never made it into the root view (because
            // LoadDrivesAsync bailed during the preceding drill),
            // re-trigger the load now so 此电脑 isn't an empty section.
            bool hasDrives = false;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is DriveItemNode) { hasDrives = true; break; }
            }
            if (!hasDrives)
            {
                int newGen = Interlocked.Increment(ref _initGeneration);
                _ = LoadDrivesAsync(newGen);
            }

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
            var jump = new MenuItem { Header = "跳转到目录" };
            jump.Click += (_, _) => JumpToDirectory(node.Path!);
            menu.Items.Add(jump);
            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
            remove.Click += (_, _) => _settingsStore.RemoveFavorite(node.Path);
            menu.Items.Add(remove);
        }
        else if (section == FolderSource.Recent)
        {
            var jump = new MenuItem { Header = "跳转到目录" };
            jump.Click += (_, _) => JumpToDirectory(node.Path!);
            menu.Items.Add(jump);
            menu.Items.Add(new Separator());
            if (_settingsStore.IsFavorite(node.Path))
            {
                var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
                remove.Click += (_, _) => _settingsStore.RemoveFavorite(node.Path);
                menu.Items.Add(remove);
            }
            else
            {
                var add = new MenuItem { Header = "添加到收藏夹" };
                add.Click += (_, _) => _settingsStore.AddFavorite(node.Path);
                menu.Items.Add(add);
            }
            menu.Items.Add(new Separator());
            var removeFromRecent = new MenuItem { Header = "从最近访问移除", Tag = "destructive" };
            removeFromRecent.Click += (_, _) => _settingsStore.RemoveRecent(node.Path);
            menu.Items.Add(removeFromRecent);
        }
        else
        {
            if (_settingsStore.IsFavorite(node.Path))
            {
                var remove = new MenuItem { Header = "从收藏夹移除", Tag = "destructive" };
                remove.Click += (_, _) => _settingsStore.RemoveFavorite(node.Path);
                menu.Items.Add(remove);
            }
            else
            {
                var add = new MenuItem { Header = "添加到收藏夹" };
                add.Click += (_, _) => _settingsStore.AddFavorite(node.Path);
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
        foreach (var p in _settingsStore.Favorites)
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
        foreach (var e in _settingsStore.Recent)
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
        if (path != null) NavigateTo(ResolveSourceForNode(target), path, recordHistory: true, trackRecent: true);
        return true;
    }

    private void ScrollSelectedIntoView()
    {
        if (SelectedNode == null) return;
        int idx = -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] == SelectedNode) { idx = i; break; }
        }
        if (idx < 0) return;

        if (ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            EnsureContainerVisible(idx);
        }
        else
        {
            EventHandler? handler = null;
            handler = (s, e) =>
            {
                if (ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
                ItemContainerGenerator.StatusChanged -= handler!;
                EnsureContainerVisible(idx);
            };
            ItemContainerGenerator.StatusChanged += handler;
        }
    }

    /// <summary>
    /// Bring the target row into view via WPF's
    /// <c>FrameworkElement.BringIntoView</c>, which internally
    /// detects whether the item is already visible and only
    /// scrolls if not — no tree-jitter when the selection was
    /// already in the viewport.
    ///
    /// All previous attempts (BeginInvoke at Background /
    /// ContextIdle, direct <c>ScrollToVerticalOffset</c> with
    /// <c>TransformToAncestor</c>) failed because they ran
    /// before the ScrollViewer's cascade Measure pass had
    /// refreshed its <c>ExtentHeight</c>. The two-stage wait
    /// below is what fixes it:
    /// 1. StatusChanged → wait for the ItemContainerGenerator to
    ///    finish producing containers.
    /// 2. LayoutUpdated → wait until the container's ActualHeight
    ///    is non-zero (its own measure ran).
    /// 3. <c>UpdateLayout()</c> — synchronous; forces the entire
    ///    visual tree (including the ancestor ScrollViewer) to
    ///    re-measure, so its <c>ExtentHeight</c> is current.
    /// 4. <c>BringIntoView()</c> — finally safe to call.
    /// </summary>
    private void EnsureContainerVisible(int idx)
    {
        if (ItemContainerGenerator.ContainerFromIndex(idx) is not FrameworkElement fe) return;

        if (fe.ActualHeight <= 0)
        {
            EventHandler? layoutHandler = null;
            layoutHandler = (s, e) =>
            {
                if (fe.ActualHeight <= 0) return;
                LayoutUpdated -= layoutHandler!;
                UpdateLayout();
                ScrollIntoViewAdaptive(fe);
            };
            LayoutUpdated += layoutHandler;
            return;
        }

        UpdateLayout();
        ScrollIntoViewAdaptive(fe);
    }

    /// <summary>
    /// Scroll the ancestor ScrollViewer so the target row is fully
    /// visible. Adaptive strategy:
    ///   - already fully visible: no-op
    ///   - small item (h + TopMargin + BottomMargin &lt;= vpH): centered
    ///   - large item: top-aligned with 24px margin (bottom may clip)
    ///
    /// Replaces <c>FrameworkElement.BringIntoView</c> which only
    /// does the minimum scroll and was leaving the row half-clipped
    /// at the viewport's bottom edge.
    ///
    /// Assumes <c>TransformToAncestor</c> returns viewport
    /// coordinates (D - VerticalOffset). If it returns document
    /// coordinates instead, the centering math lands wrong but the
    /// scroll still happens (scroll offset stays stable across
    /// re-invocation, so no infinite loop).
    /// </summary>
    private void ScrollIntoViewAdaptive(FrameworkElement fe)
    {
        var scrollViewer = FindAncestorScrollViewer(this);
        if (scrollViewer == null) { fe.BringIntoView(); return; }

        var pt = fe.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
        double itemH = fe.ActualHeight;
        double vpH = scrollViewer.ViewportHeight;
        double curOffset = scrollViewer.VerticalOffset;
        double itemTop = pt.Y;
        double itemBottom = pt.Y + itemH;

        // Already fully visible — skip the scroll entirely so we
        // don't get a "tree jumps after I clicked" jitter when the
        // selection was already in the viewport.
        if (itemTop >= 0 && itemBottom <= vpH)
            return;

        const double TopMargin = 24;
        const double BottomMargin = 24;

        // itemDocTop is the item's top in the document coordinate
        // space. TransformToAncestor returns the viewport position
        // (D - O), so adding the current offset back yields D.
        double itemDocTop = curOffset + itemTop;
        double newOffset;

        if (itemH + TopMargin + BottomMargin <= vpH)
        {
            // Small item — center it in the viewport
            newOffset = itemDocTop - (vpH - itemH) / 2;
        }
        else
        {
            // Large item — top-align with margin (bottom may clip;
            // adaptive strategy accepts that for oversized items)
            newOffset = itemDocTop - TopMargin;
        }

        newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableHeight));
        scrollViewer.ScrollToVerticalOffset(newOffset);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
    {
        var current = start;
        while (current != null)
        {
            if (current is ScrollViewer sv) return sv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ---- Init ----

    private int _initGeneration;

    private void Init(bool skipAutoSelect = false)
    {
        var gen = Interlocked.Increment(ref _initGeneration);

        Items.Clear();
        var fav = new SectionHeaderNode("收藏夹") { IsFirstSection = true }; fav.Icon = "";
        var rec = new SectionHeaderNode("最近访问"); rec.Icon = "";
        var pc = new SectionHeaderNode("此电脑"); pc.Icon = "";

        Items.Add(fav);
        int addedFavs = 0;
        foreach (var p in _settingsStore.Favorites)
        {
            if (Directory.Exists(p)) { Items.Add(new FolderItemNode(p)); addedFavs++; }
        }
        if (addedFavs == 0) Items.Add(new EmptyHintNode("暂无收藏"));

        Items.Add(rec);
        int addedRecs = 0;
        foreach (var e in _settingsStore.Recent)
        {
            if (Directory.Exists(e.Path)) { Items.Add(new RecentNode(e)); addedRecs++; }
        }
        if (addedRecs == 0) Items.Add(new EmptyHintNode("暂无最近访问"));

        Items.Add(pc);

        _ = LoadDrivesAsync(gen);

        // Auto-select the most relevant top-level node. Prefer the
        // first Recent ("where was I just?") over the first Favorite.
        // Callers that drive selection themselves (e.g.
        // JumpToDirectory) pass skipAutoSelect=true to suppress this
        // queued action — otherwise it would fire after the caller
        // has drilled into a subfolder and overwrite the selection.
        if (!skipAutoSelect)
        {
            Dispatcher.BeginInvoke(new Action(SelectFirstNode), System.Windows.Threading.DispatcherPriority.Background);
        }
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
            int ins = FindSection("此电脑");
            if (ins < 0)
            {
                // Items is in drill mode (no "此电脑" section header).
                // Skip — inserting drives at position 0 would pollute
                // the drilled view. NavigateBack re-triggers us when
                // the user returns to root.
                return;
            }
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i] is DriveItemNode) Items.RemoveAt(i);
            ins++;
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
