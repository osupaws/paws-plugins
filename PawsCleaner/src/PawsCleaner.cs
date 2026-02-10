using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Interfaces.Services;
using PawsCleaner.Abstractions;
using PawsCleaner.Models;
using PawsCleaner.Strategies.Lazer;
using PawsCleaner.Strategies.Stable;
using System.Text.Json;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IPawsPlugin
    {
        public string Id => "osupaws.cleaner";
        public string Name => "Paws Cleaner";
        public string Description => "Efficiently clean up unused osu! files.";
        public string Version => "0.0.1";
        public string IconName => "delete";

        private IHost? _host;

        public Task Initialize(IHost host)
        {
            _host = host;
            _host.LogMessage($"{Name} initialized (Strategy Pattern)!", PawsLogLvl.Information, Name);
            return Task.CompletedTask;
        }

        public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
        {
            if (commandName == "clean")
            {
                if (_host == null)
                    return new { Success = false, Message = "Host not initialized." };

                var options = JsonSerializer.Deserialize<CleanerOptions>(
                    JsonSerializer.Serialize(payload ?? new object()),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (options == null)
                    return new { Success = false, Message = "Invalid payload options." };

                // Determine Mode
                bool isLegacy = false;
                try
                {
                    if (_host != null)
                    {
                        isLegacy = _host.IsLegacyMode;
                    }
                }
                catch
                {
                    _host.LogMessage("Could not check IsLegacyMode, defaulting to False/Lazer", PawsLogLvl.Warning, Name);
                }

                string targetMode = options.Mode ?? (isLegacy ? "Stable" : "Lazer");
                _host!.LogMessage($"Cleaning Mode: {targetMode} (Host Legacy: {isLegacy})", PawsLogLvl.Information, Name);

                ICleanerStrategy strategy;

                if (targetMode.Equals("Lazer", StringComparison.OrdinalIgnoreCase))
                {
                    strategy = new LazerCleanerStrategy(_host);
                }
                else
                {
                    strategy = new StableCleanerStrategy(_host);
                }

                try
                {
                    return await strategy.CleanAsync(options);
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"Strategy execution failed: {ex.Message}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Critical Error: {ex.Message}" };
                }
            }
            return null;
        }
    }
}
