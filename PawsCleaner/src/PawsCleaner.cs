using Paws.Core.Abstractions;
using PawsCleaner.Abstractions;
using PawsCleaner.Models;
using PawsCleaner.Strategies.Lazer;
using PawsCleaner.Strategies.Stable;
using System.Text.Json;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("d34db33f-c001-4c33-9999-c1ea4e700001");
        public string Name => "Paws Cleaner";
        public string Description => "Efficiently clean up unused osu! files.";
        public string Version => "0.0.1";
        public string IconName => "delete";

        private IHostServices? _host;

        public async Task Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            _host.LogMessage($"{Name} initialized (Strategy Pattern)!", PawsLogLvl.Information, Name);
            await Task.CompletedTask;
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
                        var dynHost = (dynamic)_host;
                        isLegacy = dynHost.IsLegacyMode;
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
