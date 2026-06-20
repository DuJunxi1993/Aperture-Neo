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
    public static IReadOnlyList<PluginLoadResult> LoadAll(string pluginsDirectory, IPluginContext context)
    {
        var results = new List<PluginLoadResult>();
        if (!Directory.Exists(pluginsDirectory))
        {
            DebugLog.Write("Plugin", $"plugins directory not found: {pluginsDirectory}");
            return results;
        }

        // Plugins are loaded into an isolated AssemblyLoadContext so their
        // transitive dependencies (e.g. SkiaSharp 3.119 required by RapidOCR)
        // don't conflict with the main app's own versions (e.g. SkiaSharp 3.116).
        var alc = new PluginLoadContext(pluginsDirectory);
        _ = alc; // kept alive by the static field below via captured references

        foreach (var dll in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dll);
            Assembly assembly;
            try
            {
                assembly = alc.LoadFromAssemblyPath(dll);
            }
            catch (BadImageFormatException)
            {
                continue; // native DLL
            }
            catch (Exception ex)
            {
                DebugLog.Write("Plugin", $"load failed: {name}: {ex.GetType().Name}: {ex.Message}");
                results.Add(new PluginLoadResult(null, dll, ex));
                continue;
            }

            // Install a per-assembly native DLL resolver so P/Invoke calls
            // inside plugin code (e.g. OpenCV's Mat() constructor) can find
            // OpenCvSharpExtern.dll / libSkiaSharp.dll / onnxruntime.dll next
            // to the plugin DLL.
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
                results.Add(new PluginLoadResult(null, dll, rtex));
                continue;
            }

            if (pluginType == null)
            {
                // Not a plugin (transitive dep like RapidOCRSharpOnnx.dll)
                continue;
            }

            IPlugin plugin;
            try
            {
                plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
                plugin.Initialize(context);
            }
            catch (Exception ex)
            {
                DebugLog.Write("Plugin", $"init failed: {name}: {ex.GetType().Name}: {ex.Message}");
                results.Add(new PluginLoadResult(null, dll, ex));
                continue;
            }

            DebugLog.Write("Plugin", $"loaded: {name} -> {plugin.Name}");
            results.Add(new PluginLoadResult(plugin, dll, null));
        }

        return results;
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
            if (NativeLibrary.TryLoad(pluginCandidate, out var handle)) return handle;
            var mainCandidate = Path.Combine(_mainDir, unmanagedDllName);
            if (NativeLibrary.TryLoad(mainCandidate, out handle)) return handle;
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
