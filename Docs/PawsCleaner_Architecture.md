# PawsCleaner Architecture Logic

This document describes the internal architecture of the **PawsCleaner** plugin, specifically the `LazerCleanerStrategy` and `StableCleanerStrategy` implementations.

## 1. Modular Design

To improve maintainability and testability, the monolithic strategy classes have been refactored into smaller, focused components:

### Lazer Strategy (`LazerCleanerStrategy.cs`)

- **Main Logic:** Orchestrates the cleaning process, caching, and beatmap set iteration.
- **Component:** `LazerAssetCleaner.cs`
  - Handles detailed asset identification (Skins, Storyboards, Videos).
  - Manages background import/replacement logic.
  - Executes file removal.

### Stable Strategy (`StableCleanerStrategy.cs`)

- **Main Logic:** Orchestrates the process.
- **Component:** `StableIndexer.cs`
  - Parses `.osu` and `.osb` files.
  - Builds an `IndexedBeatmap` cache with `ContentMask` (what assets exist).
- **Component:** `StableAssetCleaner.cs`
  - Uses the index to skip maps efficiently.
  - Handles file deletion and background replacement.

## 2. Advanced Caching Strategy

PawsCleaner uses a **Cumulative Caching** approach to avoid re-processing thousands of beatmaps unnecessarily.

### AppliedFeaturesMask (Lazer & Stable)

Each cached entry (`CachedLazerSet` or `IndexedBeatmap`) stores an `AppliedFeaturesMask` integer. This is a bitmask representing the features that have _already been cleaned_ on this map.

**Mask Bits:**

- `1`: Videos
- `2`: Storyboards
- `4`: Skins
- `8`: Sounds
- `16`: Osu Ruleset
- `32`: Taiko Ruleset
- `64`: Catch Ruleset
- `128`: Mania Ruleset
- `256`: Backgrounds (Legacy/Applied indicator)

**Optimization Logic:**
When a cleaning run starts, we compute the `CurrentFeaturesMask` based on user options.
If `(CurrentFeaturesMask & ~Cached.AppliedFeaturesMask) == 0`, it means **all requested features have already been processed**.

### OptionsHash (Lazer & Stable)

To safely handle scenarios where options change (e.g., switching from `BackgroundMode="white"` to `BackgroundMode="custom"`), we also store an `OptionsHash`.

If Background Replacement is requested, we check `Cached.OptionsHash == CurrentOptionsHash`. If they differ, we assume the user wants to re-apply the background logic, even if the "Backgrounds" bit (256) is set.

### ContentMask (Stable Specific)

The Stable Indexer computes a `ContentMask` for each map, representing what assets effectively _exist_ in the folder.

**Optimization Logic:**
Even if a map hasn't been cleaned before, if the user asks to clean "Videos" but the map's `ContentMask` says it has no videos (`(ContentMask & 1) == 0`), we can mark it as cleaned immediately without scanning files.

## 3. Thread Safety (Lazer)

The plugin uses **Realm** for its local cache. To ensure thread safety in asynchronous workflows (e.g., waiting for background import):

- The `LazerCleanerStrategy` does **not** keep a long-lived Realm instance open across async boundaries.
- It uses short-lived, scoped Realm instances for:
  - Initial filtering check.
  - Updating the cache after processing a set.
  - Cleaning up deleted sets.

This prevents `RealmException: Realm accessed from incorrect thread`.
