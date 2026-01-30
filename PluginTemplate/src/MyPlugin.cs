using Paws.Core.Abstractions;
using System;
using System.Threading.Tasks;

// NAMESPACE: Rename this to match your plugin (e.g., namespace CoolCleaner)
namespace MyPlugin
{
    // CLASS: Rename this class (e.g., public class CleanerPlugin)
    public class MyPlugin : IFunctionalExplicitPlugin
    {
        // ID: Must match the "id" in plugin.json exactly!
        public Guid Id => Guid.Parse("00000000-0000-0000-0000-000000000000");

        public string Name => "My Plugin";
        public string Description => "A template plugin description.";
        public string Version => "1.0.0";
        public string IconName => "extension"; // Optional: Material Icon name

        private IHostServices? _host;

        /// <summary>
        /// Called when the plugin is loaded by Paws.
        /// Use this to get access to logging, database, and file services.
        /// </summary>
        public void Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            _host.LogMessage($"{Name} v{Version} initialized!");
        }

        /// <summary>
        /// Handles commands sent from the Frontend UI.
        /// </summary>
        public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
        {
            if (_host == null) throw new InvalidOperationException("Plugin not initialized");

            switch (commandName)
            {
                case "example_command":
                    _host.LogMessage($"Frontend sent: {payload}");
                    return new { success = true, reply = "Hello from Backend!" };

                default:
                    // Return null or throw exception for unknown commands
                    _host.LogMessage($"Unknown command: {commandName}", PawsLogLvl.Warning);
                    return null;
            }
        }
    }
}
