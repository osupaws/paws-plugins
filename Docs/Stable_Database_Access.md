# Paws Stable Plugin Access Guide

This guide explains how plugins interact with the **osu!stable** installation using the **Paws Core Abstractions**.

## 1. Getting Access

Use the `IHost.GetStableContext()` method. This returns a stateless context object that serves as a factory/gateway for reading stable files.

```csharp
// In your strategy:
var stable = _host.GetStableContext();

// Run within a Safe Write Block (Ensures osu! is closed if needed)
await _host.PerformStableWriteAsync(stablePath =>
{
    var dbPath = Path.Combine(stablePath, "osu!.db");

    // Dynamic access to helper methods if not in Interface yet
    string songsPath = ((dynamic)stable).GetSongsPath();

    // Read the DB
    var db = stable.ReadOsuDatabase(dbPath);
});
```

## 2. Using the Asset Scanner

Paws provides a built-in utility to identify **all files used by a map**.

```csharp
var songPath = Path.Combine(stablePath, "Songs", "12345 My Song");

// Returns HashSet<string> of used assets
var usedAssets = stable.GetUsedAssets(songPath);
```

## 3. Parsing Individual Files

If you need to peek into a `.osu` or `.osb` file without loading the whole DB:

```csharp
var osuFile = Path.Combine(songPath, "MyMap.osu");
var mapContent = stable.ParseBeatmap(osuFile);

Console.WriteLine($"Audio: {mapContent.AudioFilename}");
```

## 4. Performance & Caching

Parsing `.osu` files is slow. If your plugin scans thousands of maps (like a Cleaner), do **not** do this on every run.

**Best Practice:**

1.  Create a local **Realm** database in your plugin's data folder.
2.  Store an index of parsed results (`IndexedBeatmap`).
3.  Only re-parse if the folder's `LastWriteTime` OR the individual `.osu` file's `LastWriteTime` has changed (as modifying a map in the editor updates the file but sometimes not the folder timestamp).

See `PawsCleaner` source code for a reference implementation of this Indexing pattern.
