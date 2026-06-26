// ComInterop.cs — minimal COM interface declarations for the
// IExplorerCommand shell extension. IIDs and method signatures
// come from the Windows SDK header shobjidl.h (and the IID
// for IExplorerCommand specifically from
// https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorercommand).
//
// We declare the interfaces manually rather than depending on
// Microsoft.Windows.SDK.NET.Ref (the only mature package is for
// older .NET) or Microsoft.WindowsAppSDK (which pulls in WinUI
// 3 and a lot of baggage). The interfaces we need are stable
// since Windows 7 and small enough to inline.
//
// Three interfaces are declared:
//   - IShellItemArray: gives us the items the user selected in
//     Explorer (via IShellItemArray.GetCount / GetItemAt).
//   - IShellItem: lets us read the filesystem path of each item
//     via IShellItem.GetDisplayName(SIGDN_FILESYSPATH, ...).
//   - IBindCtx: passed to IExplorerCommand.Invoke but unused — we
//     just need the type to exist for the interface signature.

using System;
using System.Runtime.InteropServices;

namespace ApertureNeo.Plugins.Ocr.ShellExt;

/// <summary>
/// Win11 modern (compact) context menu command interface. The
/// shell calls into this when the user right-clicks an image
/// file in Explorer. The verb we register appears in the
/// top-level menu (not under "Show more options") because
/// IExplorerCommand is the API Win11 uses for the new menu.
/// IID: {A1EFC120-5D8B-4310-B1E5-22A3B3A12233}
/// </summary>
[ComImport]
[Guid("A1EFC120-5D8B-4310-B1E5-22A3B3A12233")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    [PreserveSig] int GetTitle(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItemArray psiArray,
        [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    [PreserveSig] int GetIcon(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItemArray psiArray,
        [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);

    [PreserveSig] int GetToolTip(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItemArray psiArray,
        [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);

    [PreserveSig] int GetCanonicalName(
        out Guid pguidCommandName);

    [PreserveSig] int GetState(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItemArray psiArray,
        [In, MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow,
        out uint pState);

    [PreserveSig] int Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItemArray psiArray,
        [In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc);

    [PreserveSig] int GetFlags(out uint pFlags);

    [PreserveSig] int EnumSubCommands(
        [MarshalAs(UnmanagedType.Interface)] out IntPtr ppEnum);
}

/// <summary>
/// IShellItemArray — collection of items the user right-clicked.
/// IID: {B63EA76D-1F85-456F-A19C-48159EFA66A9}
/// </summary>
[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA66A9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig] int BindToHandler(
        IntPtr pbc,
        [In] ref Guid bhid,
        [In] ref Guid riid,
        out IntPtr ppv);

    [PreserveSig] int GetPropertyStore(
        int flags,
        [In] ref Guid riid,
        out IntPtr ppv);

    [PreserveSig] int GetCount(out uint pdwNumItems);

    [PreserveSig] int GetItemAt(
        uint dwIndex,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [PreserveSig] int EnumItems(
        [MarshalAs(UnmanagedType.Interface)] out IntPtr ppenumShellItems);
}

/// <summary>
/// IShellItem — single shell item (file / folder / virtual
/// object). We use it to extract the filesystem path via
/// GetDisplayName(SIGDN_FILESYSPATH). IID:
/// {43826D1E-E718-42EE-BC55-A1E261C37BFE}
/// </summary>
[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig] int BindToHandler(
        IntPtr pbc,
        [In] ref Guid bhid,
        [In] ref Guid riid,
        out IntPtr ppv);

    [PreserveSig] int GetParent(
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

    [PreserveSig] int GetDisplayName(
        uint sigdnName,
        out IntPtr ppszName);

    [PreserveSig] int GetAttributes(
        uint sfgaoMask,
        out uint psfgaoAttribs);

    [PreserveSig] int Compare(
        [In, MarshalAs(UnmanagedType.Interface)] IShellItem psi,
        uint hint,
        out int piOrder);
}

/// <summary>
/// IBindCtx — passed to IExplorerCommand.Invoke but ignored. The
/// type only needs to exist for the interface signature to
/// marshal correctly. IID: {0000000E-0000-0000-C000-000000000046}
/// </summary>
[ComImport]
[Guid("0000000E-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IBindCtx { }

/// <summary>
/// Expcmdstate / expcmdflags bit values used by IExplorerCommand.
/// From shobjidl.h. We expose them as a static class with
/// constants so the impl file doesn't need to import the
/// native header.
/// </summary>
public static class ExplorerCommandState
{
    // GetState return values (bitfield)
    public const uint ECSB_ENABLED   = 0x00;
    public const uint ECSB_DISABLED  = 0x01;
    public const uint ECSB_HIDDEN    = 0x02;     // not currently shown
    public const uint ECSB_CHECKBOX  = 0x04;     // deprecated
    public const uint ECSB_CHECKED   = 0x08;     // deprecated
    public const uint ECSB_BUTTON    = 0x10;     // deprecated
    public const uint ECSB_HIDDENBYENFORCEMENT = 0x100;  // app compat block

    // Convenience combos used in our impl
    public const uint Visible = 0x00;             // visible
    public const uint Enabled = 0x00;             // enabled
    public const uint VisibleAndEnabled = ECSB_ENABLED; // both bits clear
}

/// <summary>
/// SIGDN constants used with IShellItem.GetDisplayName. We only
/// need SIGDN_FILESYSPATH (the actual filesystem path) — the
/// other values are documented for completeness.
/// </summary>
public static class ShellItemDisplayName
{
    public const uint SIGDN_NORMALDISPLAY   = 0x00000000;
    public const uint SIGDN_PARENTRELATIVE  = 0x80018001;
    public const uint SIGDN_PARENTRELATIVEEDITING = 0x80031C01;
    public const uint SIGDN_DESKTOPABSOLUTEEDITING = 0x8004C002;
    public const uint SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007C001;
    public const uint SIGDN_PARENTRELATIVEFORUI = 0x80094001;
    public const uint SIGDN_FILESYSPATH    = 0x80058000;  // what we want
    public const uint SIGDN_URL             = 0x80068000;
    public const uint SIGDN_PARENTRELATIVEPERSIST = 0x80090001;
    public const uint SIGDN_PARENTRELATIVEINVOKING = 0x800A0001;
}
