# Paws Plugin Development Guide

This guide provides a comprehensive overview of the Paws Plugin Architecture and step-by-step instructions for creating plugins. It serves as the **single source of truth** for developers and AI agents.

## 1. Architecture Overview

A Paws plugin consists of two main parts packaged together:

1.  **Backend (C#)**: A .NET 8 Class Library (DLL) responsible for logic, database access, and native operations.
2.  **Frontend (Vue 3)**: A web interface built with Vue 3 and `paws-ui`, hosted in a sandboxed `<iframe>`.

### Hosting & Isolation

- **Backend**: Loaded into a custom `AssemblyLoadContext` for deep isolation.
- **Frontend**: Hosted in a sandboxed `<iframe>` within the main Paws application.

### Zero-Boilerplate UI Integration (Vue 3)

The `@osupaws/paws-ui` library provides essential components to bridge your Vue 3 frontend with the Paws Core seamlessly.

**1. `PawsPluginShell` (Required Root Component)**
Every plugin's `App.vue` **must** be wrapped in a `<PawsPluginShell>`. This structural boundary handles:

- **Automatic Initialization**: Sends the `paws:client-ready` IPC signal upon mounting, automatically dismissing the Paws loading screen.
- **Viewport Constraints**: Forces a strict `100vw/100vh` layout with `overflow: hidden`, ensuring the iframe boundaries are respected.
- **Reactive State (`PawsShellStateKey`)**: Subscribes to global system events (theme changes, window focus/blur, game mode switches) and provides a reactive `shellState` via Vue's `inject`:
  ```vue
  <script setup lang="ts">
  import { PawsPluginShell, PawsShellStateKey } from "@osupaws/paws-ui";
  import { inject } from "vue";
  // Reactive object: { theme: 'dark'|'light', mode: 'lazer'|'stable', isFocused: boolean }
  const shellState = inject(PawsShellStateKey);
  </script>
  <template>
    <PawsPluginShell> <!-- Your content here --> </PawsPluginShell>
  </template>
  ```
  _Note: Padding or layouts inside the shell should be handled by your own container elements (`<div>`), as the Shell is a pure structural boundary._

> [!WARNING]
> **Do not rely on `shellState.mode` for critical business logic.** The IPC `mode-changed` event suffers from race conditions and might leave `shellState.mode` as `"unknown"`. Delegate mode checks (Lazer vs. Stable ruleset filtering) and decisions completely to the C# Backend using `_host.IsLegacyMode`.

**2. `PawsModal` (Recommended Overlays)**
When building settings panels or popups within your plugin, use the `<PawsModal>` component native to `paws-ui`.

- **Auto-Dismissal**: When the user opens the global Paws App menu or clicks outside the plugin bounds, the core broadcasts a `CustomEvent("paws:close-modals")`. `PawsModal` listens to this and automatically fires its `@close` event, allowing your Vue state to stay synchronized without writing custom IPC listeners.

**3. `PawsCard` & Scroll Layouts (v0.4.0+)**
The `<PawsCard>` component controls structural backgrounds. Use the `mode` prop (`empty`, `simple`, or `titled`). If you need a title, you **must** use `mode="titled"` and the `#heading` slot.

> [!IMPORTANT]
> When using `<PawsCard mode="titled">` holding scrollable content, the inner container `.contentTitled` is **not** a flexbox by default. You **must** add this deep CSS override to your Vue component to restore bounded scrolling inside the card:
>
> ```css
> /* Example targeting a card with class "worker-card" */
> .worker-card :deep(> div:last-child) {
>   flex: 1;
>   display: flex;
>   flex-direction: column;
>   min-height: 0;
> }
> ```
>
> Use `<PawsEdgeGradient>` to add stylish scroll shadows.

---

## 2. Standard Project Structure

We strictly recommend the **Strategy Pattern** for plugins that interact with both **osu!stable** and **osu!lazer**. This keeps logic isolated and maintainable.

### Recommended Folder Layout

```
MyPlugin/
├── src/
│ ├── Abstractions/                 # Interfaces (e.g., IMyStrategy)
│ ├── Common/                       # Shared utilities (Helpers, Constants, Enum Extensions)
│ ├── Models/                       # Shared DTOs (Data Transfer Objects for UI communication)
│ ├── Strategies/
│ │ ├── Lazer/                      # Lazer-specific logic & schemas
│ │ │ ├── LazerCleanerStrategy.cs
│ │ │ ├── LazerSchema.cs            # Realm models for local caching
│ │ │ └── Components/               # Focused logic components (Assets, Backgrounds)
│ │ │ └── LazerAssetCleaner.cs
│ │ └── Stable/                     # Stable-specific logic & schemas
│ │ │ ├── StableCleanerStrategy.cs
│ │ │ ├── StableSchema.cs           # Realm models for indexing
│ │ │ └── Components/               # Focused logic components (Indexers, Cleaners)
│ │ │ ├── StableIndexer.cs
│ │ │ └── StableAssetCleaner.cs
│ ├── MyPlugin.cs                   # Main entry point (Router/Controller)
│ └── MyPlugin.csproj               # Project file
├── ui/                             # Vue 3 Frontend
├── plugin.json                     # Manifest
└── build.ps1                       # Build script
```

### The "Router" Pattern

The main plugin class (`MyPlugin.cs`) should implement **`IPawsPlugin`** act **only** as a router. It should:

1.  Receive the request from the UI.
2.  Deserialize the payload options.
3.  Instantiate the correct Strategy (Lazer or Stable).
4.  Delegate execution to the strategy.

```csharp
public class MyPlugin : IPawsPlugin
{
    private IHost _host;

    public void Initialize(IHost host)
    {
        _host = host;
    }

    // IHost Definition:
    // interface IHost {
    //    ILogger Logger { get; }
    //    ILazerService Lazer { get; }
    //    IStableService Stable { get; }
    //    IStorageService Storage { get; }
    //    bool IsLegacyMode { get; }
    // }

    public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
    {
        var options = JsonSerializer.Deserialize<MyOptions>(...);

        // Determine mode via Interface property (Core V2+)
        bool isLegacy = _host.IsLegacyMode;

        // Or use dynamic fallback if developing against older Core
        // bool isLegacy = ((dynamic)_host).IsLegacyMode;

        if (isLegacy)
        {
             // Instantiate Stable Strategy Components...
             var strategy = new StableCleanerStrategy(_host);
             return await strategy.CleanAsync(options);
        }
        else
        {
             // Instantiate Lazer Strategy Components...
             var strategy = new LazerCleanerStrategy(_host);
             return await strategy.CleanAsync(options);
        }
    }
}
```

---

## 3. Caching & Database Guidelines

Paws Plugins often need to process large amounts of data (thousands of beatmaps). To ensure performance, use **Local Caching via Realm**.

### Lazer Caching Strategy

osu!lazer already has a database, so you don't need to index everything. However, you should **cache processing results** to skip unchanged items.

1.  **Create a Schema**: Define a `CachedSet` model in `LazerSchema.cs` inheriting `IRealmObject`.
2.  **Cumulative Caching (AppliedFeaturesMask)**:
    - Instead of a binary "Processed" state, use a bitmask to track _which features_ have been cleaned (e.g., Videos=1, Storyboards=2).
    - If `(CurrentOptionsMask & ~Cached.AppliedFeaturesMask) == 0`, all requested features are already done -> **SKIP**.
3.  **Options Hashing**:
    - For complex options (like Background Replacement modes), store a hash of the settings (`OptionsHash`).
    - Even if "Backgrounds" are marked as done in the mask, if the `OptionsHash` differs, it forces a re-run.
4.  **Thread Safety**:
    - Open Realm instances (`Realm.GetInstance(config)`) locally within `using` blocks or strictly scoped to the thread.
    - **Never** keep a Realm instance open across `await` calls in asynchronous methods to avoid `RealmException`.

### Stable Caching Strategy

osu!stable (`osu!.db`) is a flat list and does not track file usage (assets). You must parse `.osu` files manually.
**Do not parse files on every run.**

1.  **Index to Realm**: Create a `StableSchema.cs` to store `IndexedBeatmap`.
2.  **ContentMask**: Ideally, index _what exists_ in the map (Videos, Skins, etc.). If a map has no videos, mark it as "Cleaned" for Videos immediately.
3.  **Re-indexing**: Check if the folder's `LastWriteTime` OR the `.osu` file's `LastWriteTime` has changed. Editor saves update files but not always folders.
4.  **Read from Realm**: During cleaning/processing, read from your local Realm index instead of scanning disk.

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

### 🚫 Forbidden Namespaces & IStorageService

Paws enforces a strict security sandbox. **Direct access to `System.IO` is blocked.** You must use `_host.Storage` for all file operations.

**Blocked:**

- `System.IO.File`, `System.IO.Directory`, `System.IO.Path` (partial)

**Allowed Replacement (`IStorageService`):**

- `_host.Storage.FileExists(path)`
- `_host.Storage.DirectoryExists(path)`
- `_host.Storage.GetFiles(path, pattern, option)`
- `_host.Storage.OpenFile(path, mode, access)` -> Returns `Stream`
- `_host.Storage.GetPluginDataPath()` -> Your private data folder.

**Temp Storage Bridge (zero-copy):**

- `Stream OpenTempStream(string handle)`: Read a temp file uploaded by UI.
- `void MoveTempToData(string handle, string targetPath)`: Move temp file to persistent storage efficiently.

### Image Processing (IImageProcessor)

Plugins have access to **Magick.NET** via `_host.Image`. This allows for high-performance image resizing and format conversion without adding external dependencies.

```csharp
using (var sourceStream = _host.Storage.OpenFile(path, FileMode.Open, FileAccess.Read))
{
    // Resize/Convert
    var options = new ImageProcessOptions
    {
        TargetFormat = "jpg",
        Quality = 85,
        // (Optional) Resize
        // ResizeWidth = 1920
    };

    using (var resultStream = await _host.Image.ProcessImageAsync(sourceStream, options))
    using (var dest = _host.Storage.OpenFile(destPath, FileMode.Create, FileAccess.Write))
    {
        await resultStream.CopyToAsync(dest);
    }
}
```

### Dynamic Core Access

The Paws Core API is evolving. While strict interfaces (`IHost`, `ILazerContext`) are preferred, you can use `dynamic` casting to access new features not yet exposed in the interface.

```csharp
var context = _host.GetLazerContext();

// Example: Accessing a new property that was just added to Core but not yet in Interface
string newFeature = ((dynamic)context).NewFeatureProperty;
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
> The `id` in `plugin.json` must be a **unique string** (e.g., `author.pluginname`) and it MUST match exactly the `Id` property in your main C# class.

```json
{
  "id": "myplugin.example",
  "name": "My Plugin",
  "version": "1.0.0",
  "entryPoint": "MyPlugin.dll",
  "ui": {
    "entry": "ui/index.html"
  },
  "permissions": [
    "filesystem-osu" // basic access to osu! folders
  ]
}
```

### Valid Permissions

- **(empty)**: Sandboxed to plugin's own Data/Temp folders.
- **`filesystem-osu`**: Read/Write access to the user's osu! (Lazer/Stable) storage.
- **`filesystem-ext`**: Full system access (Requires user audit).
- **`unsafe-access`**: Bypasses security scanner. **Security Risk**. Plugin will likely be flagged.
