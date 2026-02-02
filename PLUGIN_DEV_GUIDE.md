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
    - **CRITICAL**: Your `index.html` MUST include the Paws theme links and API script.

    ```html
    <!-- Theme Links -->
    <link rel="stylesheet" href="paws-app://paws-theme-base.css" />
    <link id="paws-theme-base-link" rel="stylesheet" href="" />
    <link id="paws-theme-custom-link" rel="stylesheet" href="" />
    <!-- API Script -->
    <script src="paws-app://paws-frontend-api.js"></script>
    ```

    - Build:
      You can use the universal build script included in the template:

    ```powershell
    .\build.ps1
    ```

    Or build manually:

    ```bash
    cd ui
    pnpm install
    pnpm build
    cd ../src
    dotnet build -c Release
    ```

### Using Paws UI

In your Vue components:

```typescript
import { PawsButton, PawsCard } from "@osupaws/paws-ui";
```

---

## 5. Manifest (`plugin.json`)

> [!IMPORTANT]
> The `id` in `plugin.json` MUST be a **GUID** and it MUST match exactly the `Id` property in your main C# class. **Do not use string IDs** like `com.example.plugin` yet.

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
