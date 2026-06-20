using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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

        foreach (var dll in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            PluginLoadResult result;
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (pluginType == null)
                {
                    result = new PluginLoadResult(null, dll, new InvalidOperationException("no IPlugin implementation found"));
                }
                else
                {
                    var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;
                    plugin.Initialize(context);
                    result = new PluginLoadResult(plugin, dll, null);
                }
            }
            catch (Exception ex)
            {
                result = new PluginLoadResult(null, dll, ex);
            }

            if (!result.Success)
                DebugLog.Write("Plugin", $"load failed: {Path.GetFileName(dll)}: {result.LoadError?.Message}");
            else
                DebugLog.Write("Plugin", $"loaded: {Path.GetFileName(dll)} -> {result.Plugin!.Name}");

            results.Add(result);
        }

        return results;
    }
}
