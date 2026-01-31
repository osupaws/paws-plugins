# Paws Plugin Template (Vue + Paws UI)

This template uses **Vue 3**, **Vite**, and **TypeScript** to create a rich plugin interface using the official **Paws UI** library.

## Prerequisites

- Node.js (v18+)
- .NET 8 SDK

## Setup & Build

1.  **Install Frontend Dependencies**:

    ```bash
    cd ui
    pnpm install
    ```

    _Note: By default, it links to local `../../DVRSRCS/paws-ui`. If that's missing, update `package.json` to point to the correct registry or path._

2.  **Dev Mode (Hot Reload)**:
    - This is tricky because plugins run in an iframe.
    - For now, usually you build and reload the plugin in Paws.

3.  **Build Plugin**:
    - **Step 1: Frontend**:
      ```bash
      cd ui
      pnpm build
      ```
      (This creates `../ui-dist` with compiled files)
    - **Step 2: Package**:
      ```bash
      cd ../src
      dotnet build -c Release
      ```
      (This takes `ui-dist`, `dll`, and `manifest` and zips them into `dist/MyVuePlugin.pawsplugin`)

## Structure

- `src/`: C# Backend (Standard)
- `ui/`: Vue 3 Project
- `ui-dist/`: **Generated** frontend assets (Do not edit)
- `dist/`: Final `.pawsplugin` output
