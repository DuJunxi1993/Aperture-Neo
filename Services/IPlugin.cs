using System;
using System.Collections.Generic;

namespace ApertureNeo.Services;

public interface IPlugin
{
    string Name { get; }

    string Description { get; }

    void Initialize(IPluginContext context);
}

public interface IPluginContext
{
    string? CurrentImagePath { get; }

    IReadOnlyList<string> SelectedImagePaths { get; }

    event EventHandler<string?>? CurrentImageChanged;

    void RegisterMenuItem(System.Windows.Controls.MenuItem item);

    void RegisterContextMenuItem(System.Windows.Controls.MenuItem item);
}

public sealed record PluginLoadResult(IPlugin? Plugin, string AssemblyPath, Exception? LoadError)
{
    public bool Success => LoadError == null && Plugin != null;
}
