using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using ApertureNeo.Services;

namespace ApertureNeo.Cli;

/// <summary>
/// `aperture plugin-list` — diagnostic command that lists all
/// plugins discovered in the Plugins/ folder, with their
/// status. Useful for troubleshooting when the 插件 submenu
/// appears empty in the viewer.
///
/// Walks the same code path as the viewer's startup:
///   1. Construct the same Plugins/ directory the viewer uses
///      (AppContext.BaseDirectory + "Plugins").
///   2. Call PluginLoader.Discover() — same ALC isolation,
///      same IPlugin instantiation.
///   3. Print each plugin's Name, Description, Status, and the
///      path it was loaded from.
///
/// The 插件 submenu only shows entries when the discovery
/// succeeded AND the IPlugin type was found AND the
/// instantiation didn't throw. This command surfaces each
/// step's failure mode to stdout, bypassing the GUI.
/// </summary>
public sealed class PluginListCommand : Command
{
    public PluginListCommand()
        : base("plugin-list",
              "List all discovered plugins and their status. Diagnostic; " +
              "bypasses the GUI so it can be used from scripts.\n\n" +
              "OUTPUT:\n" +
              "  For each plugin: [status] name (vversion)\n" +
              "    status is one of:\n" +
              "      ON   plugin is enabled (loaded + heavy resources ready)\n" +
              "      off  plugin is discovered but disabled by user\n" +
              "      ERR  plugin cannot run (missing ONNX model files, etc.)\n\n" +
              "EXAMPLES:\n" +
              "  ApertureNeo plugin-list                    List all discovered plugins\n" +
              "  ApertureNeo plugin-list | grep -i ocr      Check if the OCR plugin is present")
    {
        this.SetHandler((InvocationContext _) =>
        {
            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
            Console.WriteLine($"Plugins directory: {pluginsDir}");
            Console.WriteLine($"Directory exists: {Directory.Exists(pluginsDir)}");
            if (Directory.Exists(pluginsDir))
            {
                var entries = Directory.GetFiles(pluginsDir, "*.dll");
                Console.WriteLine($"DLL count: {entries.Length}");
                foreach (var f in entries)
                    Console.WriteLine($"  - {Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)");
            }
            Console.WriteLine();

            var plugins = PluginLoader.Discover(pluginsDir);
            Console.WriteLine($"Discovered {plugins.Count} plugin(s):");
            foreach (var info in plugins)
            {
                var status = info.Instance.Status;
                var version = info.Instance.GetType().Assembly.GetName().Version?.ToString() ?? "?";
                Console.WriteLine($"  [{(status == PluginStatus.Enabled ? "ON " :
                                       status == PluginStatus.Disabled ? "off" : "ERR")}] " +
                                  $"{info.Name} (v{version})");
                Console.WriteLine($"      {info.Description}");
                Console.WriteLine($"      Assembly: {info.AssemblyPath}");
            }
        });
    }
}