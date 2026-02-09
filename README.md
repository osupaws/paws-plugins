# Paws Plugins Workspace

Welcome to the **Paws Plugins** development workspace. This repository contains the source code for official and community plugins for the Paws ecosystem.

## 📚 Documentation

For comprehensive guides on creating, building, and publishing plugins, please refer to the documentation:

### [📖 Plugin Development Guide](Docs/Plugin_Development_Guide.md)

Start here to learn how to create a new plugin, understand the architecture, and set up your environment.

### Additional Resources

- **[Lazer Database API](Docs/Lazer_Database_Access.md)**: How to interact with osu!lazer data.
- **[Stable Database API](Docs/Stable_Database_Access.md)**: How to interact with osu!stable data.
- **[Realm Schema Reference](Docs/Realm_Schema.md)**: Detailed schema of the internal database.

## 🚀 Quick Start

1.  **Clone** this repository.
2.  **Open** `PawsPlugins.sln` in VS Code or Visual Studio.
3.  **Run** the build script for any plugin:
    ```powershell
    cd PawsCleaner
    .\build.ps1
    ```

## 📂 Repository Structure

- **`PluginTemplate/`**: A starter template for new plugins.
- **`PawsCleaner/`**: Reference implementation of a complex cleaning plugin.
- **`DbTest/`**: Example plugin demonstrating database access.
- **`Libs/`**: Shared libraries and core abstractions.
- **`Docs/`**: Developer documentation.
