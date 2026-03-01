# Paws Plugin Database Access Guide

This guide explains how plugins interact with the osu!lazer database using the **Paws Core Abstractions**.

## 1. Accessing the Game Database

Do **NOT** attempt to open the game's `client.realm` file directly. This will cause locking issues and crash the game. Instead, use `LazerContext` provided by the host.

```csharp
// 1. Obtain the context (IHost provides GetLazerContext)
var context = _host.GetLazerContext();

// 2. Read data (Safe, Detached)
var sets = context.GetAllBeatmapSets();
```

### Dynamic Access (Advanced)

The Paws Core API is evolving. If you need to access methods or properties that are present in the underlying Core but not yet exposed in the `ILazerContext` interface, you can use `dynamic` casting.

```csharp
// Example: Importing a file
await context.ImportFile("C:/path/to/image.jpg", "image.jpg");

// Example: accessing a raw Hash property
string hash = ((dynamic)beatmapSet).Hash;
```

> **Warning:** Dynamic access bypasses compile-time checks. Ensure you wrap these calls in `try-catch` blocks to handle potential missing members gracefully.

---

## 2. Local Database (Caching)

Plugins are allowed (and encouraged) to create their **own** local databases for caching or indexing purposes. This is separate from the game's database.

We recommend using **Realm** for this purpose, as it is already loaded in the process.

### usage Example: Caching Processing Results

To avoid re-processing thousands of items, store a hash of the item and the options used.

1.  **Define Schema**: Create a class inheriting from `IRealmObject`.
2.  **Open Realm**: Use `Realm.GetInstance(config)`.
3.  **Store/Check**:

```csharp
var config = new RealmConfiguration("my_plugin_cache.realm");
using var cache = Realm.GetInstance(config);

var cachedItem = cache.Find<CachedItem>("id_123");

if (cachedItem != null && cachedItem.Hash == currentHash)
{
    // Skip processing
}
```

---

## 3. Reading Data (Standard API)

Read operations use standard DTOs/POCOs.

```csharp
var sets = context.GetAllBeatmapSets();

foreach (var set in sets)
{
    Console.WriteLine($"Set: {set.ID}");

    foreach (var map in set.Beatmaps)
    {
        Console.WriteLine($" - Map: {map.RulesetID}"); // 0=Osu, 1=Taiko...
    }
}
```

### identifying Assets

### identifying Assets

Files in osu!lazer are hashed and stored in the `files/` directory.

To resolve a filename (like "bg.jpg") to a physical path:

1.  Get the content hash from the `Metadata` or `Files` list.
2.  Use `context.GetFilePath(hash)`.

```csharp
var backgroundFilename = map.Metadata?.BackgroundFile;
var fileUsage = set.Files.FirstOrDefault(f => f.Filename == backgroundFilename);

if (fileUsage != null)
{
    // Get absolute physical path on disk
    string physicalPath = context.GetFilePath(fileUsage.File.Hash);

    // Now you can open 'physicalPath' using _host.Storage.OpenFile()
    // IF you have permission (filesystem-osu).
}
```

## 4. Persisting Changes

In Paws Core, changes to beatmap sets (e.g., removing files) are **NOT** automatically saved. You must explicitly call `UpdateBeatmapSet`.

```csharp
// 1. Modify the DTO
set.Files.Remove(fileToDelete);

// 2. Persist the changes
context.UpdateBeatmapSet(set);
```

This ensures the database remains in sync with the file system.

## 5. File Operations

The `ILazerContext` provides methods to manage physical files:

- **Import File**: `await context.ImportFile("C:/plugin/data/image.jpg", "image.jpg");`
  - Returns the hash of the imported file.
- **Delete Files**: `context.DeleteFiles(listOfHashes);`
  - Removes the physical files from the `files/` directory.

### Orphan Cleanup Warning

**CRITICAL WARNING:** Do **NOT** use `context.GetSafeOrphanHashes()` or manually orchestrate orphan file deletion in your plugins.
The current Paws Core implementation of `GetSafeOrphanHashes` is fundamentally flawed: it only checks `BeatmapSet` references and completely ignores references held by `SkinInfo` and `ScoreInfo`. Calling this and deleting the returned "orphans" will wipe out mandatory game assets (like default skins) and crash the game on startup.

**Best Practice:** Rely entirely on `osu!lazer`'s native garbage collection. Lazer performs complete and safe orphan cleanup automatically upon startup. When you want to remove an asset from a map, just remove its hash from `BeatmapSet.Files` and update the set. Let the game engine handle the physical file deletion.
