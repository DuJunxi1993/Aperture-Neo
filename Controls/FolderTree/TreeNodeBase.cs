using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ApertureNeo.Controls.FolderTree;

public abstract class TreeNodeBase : INotifyPropertyChanged
{
    private string _displayName = "";
    private string? _path;
    private string _icon = "📁";
    private bool _isSectionHeader;
    
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public string? Path { get => _path; set { _path = value; OnPropertyChanged(); } }
    public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }
    public double IconFontSize { get; set; } = 14.0;
    public FontWeight DisplayFontWeight { get; set; } = FontWeights.Normal;
    public bool IsSectionHeader { get => _isSectionHeader; set { _isSectionHeader = value; OnPropertyChanged(); } }
    public bool IsFirstSection { get; set; }
    public bool IsLeaf { get; set; }
    public ObservableCollection<TreeNodeBase> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class SectionHeaderNode : TreeNodeBase
{
    public SectionHeaderNode(string text) { DisplayName = text; IsSectionHeader = true; IsLeaf = true; DisplayFontWeight = FontWeights.Bold; Path = null; }
}

public sealed class DriveItemNode : TreeNodeBase
{
    public DriveItemNode(DriveInfo drive)
    {
        Path = drive.RootDirectory.FullName;
        DisplayName = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
        Icon = drive.DriveType switch
        {
            DriveType.Fixed => "💽", DriveType.Removable => "💾", DriveType.Network => "🌐", DriveType.CDRom => "💿", _ => "📁"
        };
    }
}

public sealed class FolderItemNode : TreeNodeBase
{
    public FolderItemNode(string path) { Path = path; DisplayName = System.IO.Path.GetFileName(path); Icon = "📁"; }
}
