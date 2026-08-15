// Manual AssemblyInfo to work around a WPF pack-URI bug.
//
// The csproj sets <GenerateAssemblyInfo>false</GenerateAssemblyInfo> +
// <GenerateTargetFrameworkAttribute=false> to avoid CS0579 ("duplicate
// 'AssemblyVersion' attribute") that fires on a clean rebuild — the WPF
// markup compiler emits its own AssemblyInfo.cs in the
// ApertureNeo_xxx_wpftmp intermediate project, and the SDK's auto-generated
// AssemblyInfo.cs (driven by <Version>/<AssemblyVersion>/<FileVersion>)
// would conflict with it.
//
// The downside: with GenerateAssemblyInfo=false, the SDK doesn't emit
// the [assembly: AssemblyVersion("X.Y.Z.0")] attribute that the
// <AssemblyVersion> property normally produces, so the published
// ApertureNeo.dll ends up with Version=0.0.0.0 — but the WPF markup
// compiler (reading <AssemblyVersion> from the csproj) embeds
// /ApertureNeo;vX.Y.Z.0;component/app.xaml in the auto-generated
// App.g.cs's pack URI. At runtime, the WPF resource manager tries
// to load ApertureNeo, Version=X.Y.Z.0 to satisfy that URI, fails
// (the actual assembly is 0.0.0.0), and App.Main() throws
// FileNotFoundException during InitializeComponent().
//
// This file re-introduces the AssemblyVersion + FileVersion +
// AssemblyInformationalVersion attributes so the final DLL identity
// matches what WPF expects, without re-enabling GenerateAssemblyInfo
// (and re-introducing CS0579 on a clean rebuild of the wpftmp
// intermediate).
//
// IMPORTANT: the version strings here MUST match <Version> /
// <AssemblyVersion> / <FileVersion> in ApertureNeo.csproj. A
// mismatch causes a FileNotFoundException at WPF resource load
// (ApertureNeo, Version=WPF-expects-Y, actual assembly Version=Z).
// When bumping the version: update ApertureNeo.csproj FIRST, then
// update the strings below to match.
//
// v4.1.0 (bump): AssemblyVersion 4.0.7.0 → 4.1.0.0, InformationalVersion 4.0.7 → 4.1.0.
// v3.1.0 (bump): AssemblyVersion 3.0.0.0 → 3.1.0.0, InformationalVersion 3.0.0 → 3.1.0.
// Bug: the previous hard-coded 3.0.0.0 leaked into the v3.1.0
// publish (csproj was at 3.1.0 but this file was 3.0.0.0), so
// App.xaml's pack URI pointed at Version=3.1.0.x while the actual
// assembly was 3.0.0.0 — every WPF resource load failed with
// FileNotFoundException at startup.

using System.Reflection;

[assembly: AssemblyVersion("4.1.0.0")]
[assembly: AssemblyFileVersion("4.1.0.0")]
[assembly: AssemblyInformationalVersion("4.1.0")]
