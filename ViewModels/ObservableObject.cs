using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApertureNeo.ViewModels;

/// <summary>
/// Re-export of <see cref="ObservableObject"/> with the
/// <c>ApertureNeo.ViewModels</c> namespace so user code can
/// <c>using ApertureNeo.ViewModels;</c> and have access to
/// the base class + the source-generated
/// <c>[ObservableProperty]</c> / <c>[RelayCommand]</c> attributes
/// (which are picked up via partial classes).
///
/// CommunityToolkit.Mvvm 8.x ships the
/// <c>CommunityToolkit.Mvvm.ComponentModel</c> namespace with
/// the base class; this is a thin re-export so VMs can
/// <c>: ObservableObject</c> without an extra using.
/// </summary>
public abstract class ObservableObject : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
}