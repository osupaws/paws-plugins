# Paws Plugins Workspace

This repository contains the source code for official plugins for the Paws framework and documentation for community plugin development.

## Documentation

For comprehensive guides on creating plugins please refer to the documentation:

### [Plugin Dev Guide](Docs/Plugin_Development_Guide.md)

Start here to learn how to create a new plugin, understand the architecture, and set up your environment.

### Additional Resources

- **[Lazer Database API](Docs/Lazer_Database_Access.md)**: How to interact with osu!lazer data.
- **[Stable Database API](Docs/Stable_Database_Access.md)**: How to interact with osu!stable data.
- **[Realm Schema Reference](Docs/Realm_Schema.md)**: Detailed schema of the game's database.

## Quick Start: Creating a New Plugin

1.  **Clone** this repository.
2.  **Duplicate** the `PluginTemplate` folder and rename it (e.g., `MyCoolPlugin`).
3.  **Update Project**:
    - Rename `src/MyVuePlugin.csproj` to `src/MyCoolPlugin.csproj`.
    - Rename `MyVuePlugin.cs` to `MyCoolPlugin.cs` (and update the class name inside).
    - Update `plugin.json` with a **unique GUID** and name.
4.  **Register**: Add your new project to the solution:
    ```powershell
    dotnet sln add MyCoolPlugin/src/MyCoolPlugin.csproj
    ```
5.  **Build**: Open the new folder and run the build script:
    ```powershell
    cd MyCoolPlugin
    .\build.ps1
    ```
    This will compile the C# backend and Vue frontend into a `.pawsplugin` package.

## Repository Structure

- **`PluginTemplate/`**: A fully configured starter template implementing the **Strategy Pattern**.
- **`PawsCleaner/`**: Reference implementation of a complex cleaning plugin.
- **`DbTest/`**: This is the development artifact and will be removed in the future, you don't need it, trust me.
- **`Libs/`**: Shared libraries and core abstractions.
- **`Docs/`**: Developer documentation.
