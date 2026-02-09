# Paws Plugin Database Access Guide

This guide explains how plugins interact with the osu!lazer database using the **Paws Core Abstractions**.

## 1. Accessing the Game Database

Do **NOT** attempt to open the game's `client.realm` file directly. This will cause locking issues and crash the game. Instead, use `LazerContext` provided by the host.

```csharp
// 1. Obtain the context
var context = _host.GetLazerContext();

// 2. Read data (Safe, Detached)
var sets = context.GetBeatmapSets();
```

### Dynamic Access (Advanced)

The Paws Core API is evolving. If you need to access methods or properties that are present in the underlying Core but not yet exposed in the `ILazerContext` interface, you can use `dynamic` casting.

```csharp
// Example: Importing a file (method not yet in Interface)
((dynamic)context).ImportFile("C:/path/to/image.jpg");

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
var sets = context.GetBeatmapSets();

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

Files in osu!lazer are hashed. To find which file is the background or audio, use `Metadata`.

```csharp
var audioFile = map.Metadata?.AudioFile;
var backgroundFile = map.Metadata?.BackgroundFile;
```
