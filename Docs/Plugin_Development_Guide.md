# Paws Plugin Development Guide

This guide provides a comprehensive overview of the Paws Plugin Architecture and step-by-step instructions for creating plugins. It serves as the **single source of truth** for developers and AI agents.

## 1. Architecture Overview

A Paws plugin consists of two main parts packaged together:

1.  **Backend (C#)**: A .NET 8 Class Library (DLL) responsible for logic, database access, and native operations.
2.  **Frontend (Vue 3)**: A web interface built with Vue 3 and `paws-ui`, hosted in a sandboxed `<iframe>`.

### Hosting & Isolation

- **Backend**: Loaded into a custom `AssemblyLoadContext` for deep isolation.
- **Frontend**: Hosted in a sandboxed `<iframe>` within the main Paws application.

---

## 2. Standard Project Structure

We strictly recommend the **Strategy Pattern** for plugins that interact with both **osu!stable** and **osu!lazer**. This keeps logic isolated and maintainable.

### Recommended Folder Layout

```
MyPlugin/
├── src/
│   ├── Abstractions/       # Interfaces (e.g., IMyStrategy)
│   ├── Common/             # Shared utilities (Helpers, Constants, Enum Extensions)
│   ├── Models/             # Shared DTOs (Data Transfer Objects for UI communication)
│   ├── Strategies/
│   │   ├── Lazer/          # Lazer-specific logic & schemas
│   │   │   ├── LazerStrategy.cs
│   │   │   └── LazerSchema.cs  # Realm models for local caching
│   │   └── Stable/         # Stable-specific logic & schemas
│   │       ├── StableStrategy.cs
│   │       └── StableSchema.cs # Realm models for indexing
│   ├── MyPlugin.cs         # Main entry point (Router/Controller)
│   └── MyPlugin.csproj     # Project file
├── ui/                     # Vue 3 Frontend
├── plugin.json             # Manifest
└── build.ps1               # Build script
```

### The "Router" Pattern

The main plugin class (`MyPlugin.cs`) should act **only** as a router. It should:

1.  Receive the request from the UI.
2.  Deserialize the payload options.
3.  Instantiate the correct Strategy (Lazer or Stable).
4.  Delegate execution to the strategy.

**Example:**

```csharp
public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
{
    var options = JsonSerializer.Deserialize<MyOptions>(...);

    // Determine mode (Legacy = Stable, otherwise Lazer)
    bool isLegacy = ((dynamic)_host).IsLegacyMode;

    IStrategy strategy = isLegacy
        ? new StableStrategy(_host)
        : new LazerStrategy(_host);

    return await strategy.ExecuteAsync(options);
}
```

---

## 3. Caching & Database Guidelines

Paws Plugins often need to process large amounts of data (thousands of beatmaps). To ensure performance, use **Local Caching via Realm**.

### Lazer Caching Strategy

osu!lazer already has a database, so you don't need to index everything. However, you should **cache processing results** to skip unchanged items.

1.  **Create a Schema**: Define a `CachedSet` model in `LazerSchema.cs`.
    - `SetId` (PrimaryKey)
    - `SetHash` (Hash of the beatmap set content)
    - `OptionsHash` (Hash of the settings used during processing)
2.  **Check Cache**: Before processing a set:
    - Calculate current `OptionsHash`.
    - Get the set's `Hash` from Lazer Core.
    - If `CachedSet.SetHash == CurrentHash` AND `CachedSet.OptionsHash == CurrentOptionsHash`, **SKIP** processing.
3.  **Update Cache**: After successful processing, write the new hashes to your local Realm.

### Stable Caching Strategy

osu!stable (`osu!.db`) is a flat list and does not track file usage (assets). You must parse `.osu` files manually.
**Do not parse files on every run.**

1.  **Index to Realm**: Create a `StableSchema.cs` to store `IndexedBeatmap` and `IndexedFile`.
2.  **Parse Once**: When a user selects "Scan", parse `.osu` files and save usage data (e.g., "bg.jpg is Background") to your local Realm.
3.  **Read from Realm**: During cleaning/processing, read from your local Realm instead of the file system.

> **More Details**:
>
> - [Lazer Database Access Guide](Lazer_Database_Access.md)
> - [Stable Database Access Guide](Stable_Database_Access.md)

---

## 4. Coding Standards

### Implicit Usings

Our project templates enable **ImplicitUsings**. You do **not** need to explicitly include:

- `using System;`
- `using System.Collections.Generic;`
- `using System.IO;`
- `using System.Linq;`
- `using System.Threading.Tasks;`

Keep your code clean by removing these redundant directives.

### Dynamic Core Access

The Paws Core API is evolving. To maintain compatibility and access new features without waiting for library updates, use `dynamic` casting for Context objects.

```csharp
var context = _host.GetLazerContext();
// Use dynamic to access new methods not yet in the Interface
((dynamic)context).ImportFile("path/to/file");
```

---

## 5. Build System

Adding a new plugin to the workspace?

1.  **Solution**: Ensure the project is added to `PawsPlugins.sln`.
2.  **Build**: Use the `build.ps1` script in the plugin root. It builds both Backend and UI and packages them into a `.pawsplugin` file in `dist/`.

```powershell
.\build.ps1
```

---

## 6. Manifest (`plugin.json`)

> [!IMPORTANT]
> The `id` in `plugin.json` MUST be a **GUID** and it MUST match exactly the `Id` property in your main C# class.

```json
{
  "id": "11111111-1111-1111-1111-111111111111",
  "name": "My Plugin",
  "version": "1.0.0",
  "entryPoint": "MyPlugin.dll",
  "ui": {
    "entry": "ui/index.html"
  }
}
```
