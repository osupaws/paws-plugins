# Paws Plugin Template

This folder contains a "Ready-to-Use" template for creating Paws plugins.

## How to use

1.  **Copy this folder** to a new location (e.g., `MyNewPlugin`).
2.  **Rename files**:
    - Rename `src/MyPlugin.csproj` to `src/YourPluginName.csproj`.
    - Rename `src/MyPlugin.cs` to `src/YourPluginName.cs`.
3.  **Update `plugin.json`**:
    - Open `src/plugin.json` and change the `id`, `name`, and `entryPoint`.
4.  **Update C# Code**:
    - Open the `.cs` file and rename the class / namespace to match your plugin.
    - Update the `Id` property to match your `plugin.json`.
5.  **Build**:
    - Open terminal in `src/` folder.
    - Run `dotnet build -c Release`.
    - **Result**: Your plugin will be automatically packaged into `dist/YourPluginName.pawsplugin`.

## Structure

- `src/`: Contains the C# backend code and project file.
- `ui/`: Contains the frontend HTML/CSS/JS.
- `icon.svg`: The plugin icon (referenced in `plugin.json`).

## Dependencies

You need to reference `Paws.Core.Abstractions.dll`.
The valid path is pre-configured in `.csproj` but might need adjustment if you move this folder far away from the main Paws project.
