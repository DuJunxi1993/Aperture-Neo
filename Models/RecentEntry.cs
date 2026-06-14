using System;

namespace ApertureNeo.Models;

/// <summary>
/// One row in the "Recently opened folders" list. <see cref="Path"/>
/// is the absolute folder path; <see cref="LastOpened"/> is the
/// most recent visit (used to sort and to evict the oldest entry
/// when the list exceeds <see cref="Services.SettingsStore.MaxRecentCount"/>).
/// </summary>
public class RecentEntry
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
