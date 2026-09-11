# Nearby Player List

A Dalamud plugin for FFXIV: a compact, clickable list of nearby targetable
players, for healing and raising in Field Operations (Bozja, Eureka, Occult
Crescent, the Hunt, Diadem).

## Working agreement

**Never `git push` or create a release without being asked to.** Commits are
fine to make locally; the point of working here rather than in the chat window
is that changes get reviewed before they reach GitHub.

## The two-repo setup

This repo holds the **code**. The plugin's **distribution manifest** lives in a
separate repo:

| Repo | Local path | Holds |
|---|---|---|
| `joshua88wa/FFXIV-Dalamud-NearbyPlayerList` | `C:\Utilities\FFXIV\repos\FFXIV-Dalamud-NearbyPlayerList` | plugin source, releases |
| `joshua88wa/FFXIV` | `C:\Utilities\FFXIV\repos\FFXIV` | `Dalamud/repo.json` — the custom plugin repo users add to Dalamud |

Shipping any version touches **both**. Bumping the version here without
updating `repo.json` there means nobody receives the update.

## Branches

- `testing` — active development. Test builds are cut from here.
- `main` — stable. Receives a merge from `testing` when a version goes stable.

## Version numbers live in three places

They must match, or Dalamud rejects the plugin:

1. `NearbyPlayerList/NearbyPlayerList.csproj` → `<Version>`
2. `NearbyPlayerList/NearbyPlayerList.json` → `AssemblyVersion`
3. The other repo's `Dalamud/repo.json` → `AssemblyVersion` (stable) or
   `TestingAssemblyVersion` (testing)

## Build

```
dotnet build -c Release
```

Produces `NearbyPlayerList/bin/Release/NearbyPlayerList/latest.zip`, which is
the release asset. It must always be named `latest.zip` — the download links in
`repo.json` depend on that name.

## Releasing

No CI. Releases are cut by hand with `gh release create`.

### Test build

1. On `testing`, bump the 4th version component in the `.csproj` and the
   `.json` (e.g. `0.5.0.6` → `0.5.0.7`).
2. `dotnet build -c Release`
3. Tag as `v<version>-test<N>`, where `N` increments per test build and resets
   after each stable release. Mark it a **pre-release** and attach `latest.zip`.
4. In the `FFXIV` repo, update `Dalamud/repo.json`:
   - `TestingAssemblyVersion` → the new version
   - `TestingChangelog` → what changed
   - `DownloadLinkTesting` → the **exact tag** URL (this is pinned, so it must
     change every single test build)

### Stable release

1. Merge `testing` into `main`.
2. Tag as `v<version>` off `main`, not a pre-release, with `latest.zip`
   attached.
3. In the `FFXIV` repo, update `Dalamud/repo.json`:
   - `AssemblyVersion` → the new version
   - `DownloadLinkInstall` / `DownloadLinkUpdate` need **no change** — they
     point at `releases/latest/download/`, which GitHub resolves on its own.

## Dalamud API level

Currently 15 (`Dalamud.NET.Sdk/15.0.0`). It appears in the `.csproj` SDK
version, `NearbyPlayerList.json`, and both the `DalamudApiLevel` and
`TestingDalamudApiLevel` fields in `repo.json`. All four move together when
Dalamud bumps its API level.
