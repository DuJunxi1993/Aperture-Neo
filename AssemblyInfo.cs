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
// the [assembly: AssemblyVersion("3.0.0.0")] attribute that the
// <AssemblyVersion> property normally produces, so the published
// ApertureNeo.dll ends up with Version=0.0.0.0 — but the WPF markup
// compiler (reading <AssemblyVersion> from the csproj) embeds
// /ApertureNeo;V3.0.0.0;component/app.xaml in the auto-generated
// App.g.cs's pack URI. At runtime, the WPF resource manager tries
// to load ApertureNeo, Version=3.0.0.0 to satisfy that URI, fails
// (the actual assembly is 0.0.0.0), and App.Main() throws
// FileNotFoundException during InitializeComponent().
//
// This file re-introduces the AssemblyVersion + FileVersion +
// AssemblyFileVersion + AssemblyInformationalVersion attributes
// so the final DLL identity matches what WPF expects, without
// re-enabling GenerateAssemblyInfo (and re-introducing CS0579
// on a clean rebuild of the wpftmp intermediate).

using System.Reflection;

[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0.0")]
