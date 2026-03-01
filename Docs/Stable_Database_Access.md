# Paws Stable Plugin Access Guide

This guide explains how plugins interact with the **osu!stable** installation using the **Paws Core Abstractions**.

## 1. Getting Access

Use the `IHost.GetStableContext()` method. This returns a stateless context object that serves as a factory/gateway for reading stable files.

```csharp
// 1. Safe Access Wrapper
// Use this wrapper to ensure file operations are safe (it may close osu! processes if needed).
await _host.Stable.PerformStableWriteAsync(stablePath =>
{
    // stablePath is provided by the wrapper (e.g. "C:/osu!")

    // 2. Factory / Utilities
    var stable = _host.Stable.GetStableContext();
    var dbPath = Path.Combine(stablePath, "osu!.db");

    // 3. Read Database
    var db = stable.ReadOsuDatabase(dbPath);

    // ... operations ...
});
```

## 2. Using the Asset Scanner

Paws provides a built-in utility to identify **all files used by a map**.

```csharp
var songPath = Path.Combine(stablePath, "Songs", "12345 My Song");

// Check the interface definition or use standard file scanning if needed.
// var usedAssets = stable.GetUsedAssets(songPath);
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

1.  Use `Realm` for caching parsed results.
2.  Use `_host.Storage.GetLastWriteTimeUtc()` on both folders and files to detect changes.

See `PawsCleaner` source code for a reference implementation of this Indexing pattern.

## 5. File Operations

To modify files in the Stable directory (`Songs`, `Skins`):

- **Delete**: Use `_host.Storage.DeleteFile(path)`.
  - **Note**: You must use `Storage`, not `System.IO`.
- **Symlink**: `stable.CreateSymlink(source, dest)` (if supported by OS/Context).
