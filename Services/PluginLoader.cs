using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ApertureNeo.Services;

public static class PluginLoader
{
    // Shared ALC for all plugins — assemblies can't be unloaded
    // (managed + native deps would leak), so we keep one context alive
    // for the app's lifetime. The instance cache in PluginInfo lets us
    // re-use the same IPlugin object across discover+activate cycles
    // without re-loading the DLL.
    private static PluginLoadContext? _alc;

    // Tracks which plugins have been Activate()'d so repeated
    // Activate calls are no-ops (each Activate would otherwise
    // re-register menu items).
    private static readonly HashSet<IPlugin> _active = new();

    /// <summary>
    /// Scan <paramref name="pluginsDirectory"/> for IPlugin DLLs, load
    /// each assembly (this brings in native deps like RapidOCR and
    /// OpenCV), and instantiate the IPlugin type. Activate is NOT
    /// called — the plugin's heavy resources (ONNX models, etc.)
    /// stay unloaded until the user opts in.
    /// </summary>
    public static IReadOnlyList<PluginInfo> Discover(string pluginsDirectory)
    {
        var results = new List<PluginInfo>();
        if (!Directory.Exists(pluginsDirectory))
        {
            DebugLog.Write("Plugin", $"plugins directory not found: {pluginsDirectory}");
            return results;
        }

        if (_alc == null) _alc = new PluginLoadContext(pluginsDirectory);

        foreach (var dll in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dll);
            Assembly assembly;
            try
            {
                assembly = _alc.LoadFromAssemblyPath(dll);
            }
            catch (BadImageFormatException)
            {
                continue; // native DLL
            }
            catch (Exception ex)
            {
                DebugLog.Write("Plugin", $"load failed: {name}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            // Per-assembly native DLL resolver so plugin P/Invoke calls
            // (OpenCV's Mat(), etc.) find their native deps in the
            // plugin folder. Install once per assembly; idempotent
            // (SetDllImportResolver throws on second call for the same
            // assembly, so we swallow the error).
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, (libraryName, asm, dllPath) =>
                {
                    var asmDir = Path.GetDirectoryName(asm?.Location ?? dll);
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        var candidate = Path.Combine(asmDir, libraryName);
                        if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
                    }
                    return NativeLibrary.Load(libraryName, asm, dllPath);
                });
            }
            catch { }

            Type? pluginType;
            try
            {
                pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            }
            catch (ReflectionTypeLoadException rtex)
            {
                var inner = rtex.LoaderExceptions.FirstOrDefault()?.Message ?? "loader exceptions";
                DebugLog.Write("Plugin", $"reflection failed: {name}: {inner}");
                continue;
            }

            if (pluginType == null) continue; // transitive dep, not a plugin

            IPlugin plugin;
            try
            {
                plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
            }
            catch (Exception ex)
            {
                DebugLog.Write("Plugin", $"construct failed: {name}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            DebugLog.Write("Plugin", $"discovered: {name} -> {plugin.Name}");
            results.Add(new PluginInfo(plugin.Name, plugin.Description, plugin, dll));
        }

        return results;
    }

    /// <summary>
    /// Activate a discovered plugin — calls its IPlugin.Activate so it
    /// can load heavy resources and register menu items. Idempotent:
    /// a no-op if the plugin is already active.
    /// </summary>
    public static void Activate(PluginInfo info, IPluginContext context)
    {
        if (_active.Contains(info.Instance)) return;
        try
        {
            info.Instance.Activate(context);
            _active.Add(info.Instance);
            DebugLog.Write("Plugin", $"activated: {info.Name}");
        }
        catch (Exception ex)
        {
            DebugLog.Write("Plugin", $"activate failed: {info.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Deactivate an active plugin — calls its IPlugin.Deactivate so
    /// it can release heavy resources and unregister menu items.
    /// </summary>
    public static void Deactivate(PluginInfo info)
    {
        if (!_active.Contains(info.Instance)) return;
        try
        {
            info.Instance.Deactivate();
            _active.Remove(info.Instance);
            DebugLog.Write("Plugin", $"deactivated: {info.Name}");
        }
        catch (Exception ex)
        {
            DebugLog.Write("Plugin", $"deactivate failed: {info.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Isolated AssemblyLoadContext: resolves managed dependencies against
    /// the plugin folder first, then the host (main app) as fallback. Keeps
    /// the plugin's transitive deps (SkiaSharp 3.119, ONNX 1.26, etc.)
    /// from clashing with the main app's versions.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginDir;
        private readonly string _mainDir;

        public PluginLoadContext(string pluginDir) : base(isCollectible: false)
        {
            _pluginDir = pluginDir;
            _mainDir = AppContext.BaseDirectory;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var asmName = assemblyName.Name + ".dll";
            // 1) Look in the plugin folder first.
            var pluginCandidate = Path.Combine(_pluginDir, asmName);
            if (File.Exists(pluginCandidate) && TryLoadFromPath(pluginCandidate, out var pluginAsm))
                return pluginAsm;

            // 2) Fall back to the main app folder (for the IPlugin interface
            //    type, which lives in ApertureNeo.dll).
            var mainCandidate = Path.Combine(_mainDir, asmName);
            if (File.Exists(mainCandidate))
            {
                if (string.Equals(asmName, "ApertureNeo.dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Always reuse the already-loaded main assembly — types must
                    // match the IPlugin interface defined in it.
                    return Default.LoadFromAssemblyName(assemblyName);
                }
                if (TryLoadFromPath(mainCandidate, out var mainAsm))
                    return mainAsm;
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            // Search native deps in the plugin folder first, then main app dir.
            var pluginCandidate = Path.Combine(_pluginDir, unmanagedDllName);
            if (NativeLibrary.TryLoad(pluginCandidate, out var pluginHandle)) return pluginHandle;
            var mainCandidate = Path.Combine(_mainDir, unmanagedDllName);
            if (NativeLibrary.TryLoad(mainCandidate, out var mainHandle)) return mainHandle;
            return IntPtr.Zero;
        }

        private bool TryLoadFromPath(string path, out Assembly? assembly)
        {
            try
            {
                assembly = LoadFromAssemblyPath(path);
                return true;
            }
            catch
            {
                assembly = null;
                return false;
            }
        }
    }
}
