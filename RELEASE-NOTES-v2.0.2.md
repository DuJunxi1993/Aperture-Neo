# Aperture Neo 2.0.2

## What's new in 2.0.2

**New feature**
- Right-click context menu on Recent / Favorites tree entries → **跳转到目录** (Jump to directory). Drills from this-PC through the matching drive and subfolders, highlights the target folder in its parent view, and loads its images into the main viewer.

**Bug fixes**
- Tree "jump to directory" click was silently broken on cold start (race against the async drive loader). Fixed by synchronously constructing the `DriveItemNode` from `DriveInfo` when it isn't in `Items` yet, and by skipping the deferred `SelectFirstNode` that was overriding our drill.
- After using "jump to directory", the drilled view was polluted with C:/D:/E: drive rows. Fixed `LoadDrivesAsync` to bail when the "此电脑" section header is missing (drill mode), and added re-trigger logic in `NavigateBack` when the root view ended up without drives.
- Selected folder in the tree was not scrolled into the viewport after JumpToDirectory. Replaced the unreliable `BringIntoView` (which left the row half-clipped at the viewport's bottom edge) with an explicit adaptive scroll: small items are centered, large items are top-aligned with a 24px margin.

**Files**
- `ApertureNeo-Setup-v2.0.2.exe` — 91.12 MB, self-contained win-x64