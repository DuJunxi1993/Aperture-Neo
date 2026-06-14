<#
.SYNOPSIS
Comprehensive XAML resource usage scanner.

.DESCRIPTION
Scans all .xaml and .cs files in the project for 9 access patterns
that could reference a XAML resource key. For each key defined in
the project's resource dictionaries, outputs one of:

  HIGH_CONFIDENCE_UNUSED  - 0 hits across all 9 patterns
  POSSIBLY_USED           - 1+ hit(s), but the script cannot prove the
                           hit is real (might be a comment, string, etc.)
  MISSED_PATTERN_RISK     - key name contains non-word characters and
                           may have been missed by the regex sweep

Writes:
  Tools/scan-output.txt   - human-readable, category-grouped
  Tools/scan-output.json  - machine-readable, full detail

Invoke from repo root:  .\Tools\Scan-XamlResources.ps1
#>

param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..").Path,
    [string]$OutputTxt = "$PSScriptRoot\scan-output.txt",
    [string]$OutputJson = "$PSScriptRoot\scan-output.json"
)

$ErrorActionPreference = "Stop"

# --- 1. Collect all defined keys (from resource dictionaries) ---
$definedKeys = [ordered]@{}
$keySources = @(
    "DesignTokens.xaml"
    "Styles\Buttons.xaml"
    "Styles\ContextMenu.xaml"
    "Styles\Popover.xaml"
    "Styles\Splitter.xaml"
    "Styles\Tag.xaml"
    "Styles\Text.xaml"
    "Styles\Thumbnail.xaml"
    "Styles\TreeView.xaml"
    "MainWindow.xaml"
    "AboutWindow.xaml"
)
foreach ($rel in $keySources) {
    $path = Join-Path $Root $rel
    if (-not (Test-Path $path)) { continue }
    $matches = Select-String -Path $path -Pattern 'x:Key="([^"]+)"' -AllMatches
    foreach ($m in $matches) {
        $key = $m.Matches.Groups[1].Value
        if (-not $definedKeys.Contains($key)) {
            $definedKeys[$key] = [PSCustomObject]@{
                File = $rel
                Line = $m.LineNumber
            }
        }
    }
}

# --- 2. Build file list ---
$xamlFiles = Get-ChildItem -Path $Root -Recurse -Filter "*.xaml" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$csFiles = Get-ChildItem -Path $Root -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

Write-Host "Scanning $($xamlFiles.Count) XAML + $($csFiles.Count) C# files for $($definedKeys.Count) defined keys..."

# --- 3. Pattern definitions ---
# 9 patterns covering StaticResource / DynamicResource / ResourceKey / FindResource /
# TryFindResource / Application.Current.Resources[...] / arbitrary Resources[...] /
# GetResourceStream / reflection access.
$patterns = @(
    @{ Id = "P1_StaticResource";        Regex = 'StaticResource\s+(\w+)\b';                                    FileType = "xaml" },
    @{ Id = "P2_DynamicResource";       Regex = 'DynamicResource\s+(\w+)\b';                                   FileType = "xaml" },
    @{ Id = "P3_ResourceKey";           Regex = 'ResourceKey="([^"]+)"';                                       FileType = "xaml" },
    @{ Id = "P4_FindResource";          Regex = 'FindResource\(\s*"([^"]+)"\s*\)';                             FileType = "cs" },
    @{ Id = "P5_TryFindResource";       Regex = 'TryFindResource\(\s*"([^"]+)"\s*\)';                          FileType = "cs" },
    # P6: explicit Application.Current.Resources[...] - the pattern we missed last time.
    @{ Id = "P6_AppCurrentResources";   Regex = 'Application\.Current\.Resources\[\s*"([^"]+)"\s*\]';           FileType = "cs" },
    # P9: any other Resources[...] indexer on a DependencyObject (negative lookbehind
    # ensures we don't match a substring like 'MyResources' or 'ResourceStream').
    @{ Id = "P9_ResourcesIndexer";      Regex = '(?<![A-Za-z0-9_])Resources\[\s*"([^"]+)"\s*\]';                FileType = "cs" },
    @{ Id = "P10_GetResourceStream";    Regex = 'GetResourceStream\(\s*"([^"]+)"\s*\)';                        FileType = "cs" },
    @{ Id = "P11_Reflection";           Regex = 'Get(?:Field|Property)\(\s*"([^"]+)"\s*\)';                    FileType = "cs" }
)

# --- 4. Scan all files for all patterns ---
$usageMap = @{}
foreach ($key in $definedKeys.Keys) {
    $usageMap[$key] = [ordered]@{}
}

foreach ($pat in $patterns) {
    $fileSet = if ($pat.FileType -eq "xaml") { $xamlFiles } else { $csFiles }
    foreach ($file in $fileSet) {
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }
        $regexMatches = [regex]::Matches($content, $pat.Regex)
        foreach ($m in $regexMatches) {
            $key = $m.Groups[1].Value
            if ($usageMap.ContainsKey($key)) {
                if (-not $usageMap[$key].Contains($pat.Id)) {
                    $usageMap[$key][$pat.Id] = 0
                }
                $usageMap[$key][$pat.Id] += 1
            }
        }
    }
}

# --- 5. Categorize each key ---
$results = @()
foreach ($key in $definedKeys.Keys) {
    $patternsHit = @($usageMap[$key].Keys)
    $totalHits = if ($usageMap[$key].Count -gt 0) {
        ($usageMap[$key].Values | Measure-Object -Sum).Sum
    } else { 0 }
    $hasSpecialChars = $key -notmatch '^\w+$'

    if ($patternsHit.Count -eq 0 -and -not $hasSpecialChars) {
        $category = "HIGH_CONFIDENCE_UNUSED"
    }
    elseif ($hasSpecialChars) {
        $category = "MISSED_PATTERN_RISK"
    }
    else {
        $category = "POSSIBLY_USED"
    }

    $patternDetails = ($usageMap[$key].GetEnumerator() |
        Sort-Object Name |
        ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ";"

    $results += [PSCustomObject]@{
        Key            = $key
        Category       = $category
        DefinedIn      = $definedKeys[$key].File
        DefinedAtLine  = $definedKeys[$key].Line
        PatternsHit    = ($patternsHit -join ",")
        TotalHits      = $totalHits
        PatternDetails = $patternDetails
    }
}

# --- 6. Output ---
$summary = $results | Group-Object Category
$summaryLines = @()
$summaryLines += "Scan @ $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$summaryLines += "Root: $Root"
$summaryLines += "Files: $($xamlFiles.Count) XAML, $($csFiles.Count) C#"
$summaryLines += "Defined keys: $($definedKeys.Count)"
$summaryLines += ""
$summaryLines += "=== SUMMARY ==="
foreach ($cat in @("HIGH_CONFIDENCE_UNUSED", "POSSIBLY_USED", "MISSED_PATTERN_RISK")) {
    $count = ($summary | Where-Object { $_.Name -eq $cat }).Count
    $summaryLines += ("  {0,-28} {1}" -f $cat, $count)
}
$summaryLines += ""
$summaryLines += "=== HIGH_CONFIDENCE_UNUSED (safe to delete) ==="
$summaryLines += ""
foreach ($r in ($results | Where-Object { $_.Category -eq "HIGH_CONFIDENCE_UNUSED" } | Sort-Object DefinedIn, Key)) {
    $summaryLines += "  [$($r.DefinedIn):$($r.DefinedAtLine)]  $($r.Key)"
}
$summaryLines += ""
$summaryLines += "=== POSSIBLY_USED (manual review required) ==="
$summaryLines += ""
foreach ($r in ($results | Where-Object { $_.Category -eq "POSSIBLY_USED" } | Sort-Object DefinedIn, Key)) {
    $summaryLines += "  [$($r.DefinedIn):$($r.DefinedAtLine)]  $($r.Key)  hits=$($r.TotalHits)  ($($r.PatternDetails))"
}
$summaryLines += ""
$summaryLines += "=== MISSED_PATTERN_RISK (special chars in name, DO NOT delete without manual check) ==="
$summaryLines += ""
foreach ($r in ($results | Where-Object { $_.Category -eq "MISSED_PATTERN_RISK" } | Sort-Object DefinedIn, Key)) {
    $summaryLines += "  [$($r.DefinedIn):$($r.DefinedAtLine)]  '$($r.Key)'  hits=$($r.TotalHits)  ($($r.PatternDetails))"
}

$summaryLines -join "`n" | Out-File -FilePath $OutputTxt -Encoding utf8
$results | Sort-Object Category, DefinedIn, Key | ConvertTo-Json -Depth 5 | Out-File -FilePath $OutputJson -Encoding utf8

# --- 7. Console summary ---
Write-Host ""
Write-Host "=== SUMMARY ==="
foreach ($cat in @("HIGH_CONFIDENCE_UNUSED", "POSSIBLY_USED", "MISSED_PATTERN_RISK")) {
    $count = ($summary | Where-Object { $_.Name -eq $cat }).Count
    Write-Host ("  {0,-28} {1}" -f $cat, $count)
}
Write-Host ""
Write-Host "Output written to:"
Write-Host "  $OutputTxt"
Write-Host "  $OutputJson"
