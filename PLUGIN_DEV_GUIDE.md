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

## 2. Standard Tech Stack

All plugins must strictly adhere to the following stack to ensure visual consistency and stability:

- **Frontend**: Vue 3 + TypeScript
- **Package Manager**: `pnpm`
- **UI Library**: `@osupaws/paws-ui` (via GitHub Packages)
- **Backend**: .NET 8.0

**Visual Consistency Policy**:
Plugins must use `paws-ui` components (e.g., `<PawsButton>`, `<PawsCard>`). Do not invent custom styles unless absolutely necessary. The UI library automatically inherits the user's active theme.

---

## 3. Creating a Plugin

1.  Copy the official `PluginTemplate` folder.
2.  **Dependencies Setup**:
    - Ensure `Libs/Paws.Core.Abstractions.dll` exists in the repository root.
    - Ensure `.npmrc` is configured in your UI folder to point `@osupaws` to `https://npm.pkg.github.com`.
3.  **Frontend Setup**:
    ```bash
    cd ui
    pnpm install
    pnpm build  # Compiles Vue to static HTML/JS in '../ui-dist'
    ```
4.  **Backend Package**:
    ```bash
    cd ../src
    dotnet build -c Release # Packs 'ui-dist' and DLLs into a .pawsplugin
    ```

### Using Paws UI

In your Vue components:

```typescript
import { PawsButton, PawsCard } from "@osupaws/paws-ui";
// Global styles are imported in main.ts
```

**Note**: Since `paws-ui` is hosted on GitHub Packages, you may need to authenticate or ensure your environment can access public GitHub packages.

---

## 4. Development Reference

### Backend (C#)

Implement `IFunctionalExplicitPlugin`.
Access `IHostServices` for:

- `LogMessage(string)`
- `GetLazerContext()`
- `PerformStableWriteAsync(action)`

### Frontend API

Use the helper provided in the templates:

```javascript
// Send command to C# Backend
await Paws.sendCommand("my_command", { some: "data" });
```

---

## 5. Manifest (`plugin.json`)

```json
{
  "id": "com.example.myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "entryPoint": "MyPlugin.dll",
  "ui": {
    "entry": "ui/index.html"
  }
}
```
