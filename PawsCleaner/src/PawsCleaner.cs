using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Models;
using Paws.Core.Abstractions.Exceptions;
using PawsCleaner.Abstractions;
using PawsCleaner.Models;
using PawsCleaner.Strategies.Lazer;
using PawsCleaner.Strategies.Stable;
using System.Text.Json;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IPawsPlugin, ISupportsLifecycle
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
            _host.Logger.LogMessage($"{Name} initialized (V3 Architecture)!", PawsLogLvl.Information, Name);
            return Task.CompletedTask;
        }


        public Task OnUiWakeAsync()
        {
            _host?.Logger.LogMessage($"{Name} Wake Up!", PawsLogLvl.Information, Name);
            return Task.CompletedTask;
        }

        public Task OnUiSleepAsync()
        {
            _host?.Logger.LogMessage($"{Name} Sleeping...", PawsLogLvl.Information, Name);
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
                    _host.Logger.LogMessage("Could not check IsLegacyMode, defaulting to False/Lazer", PawsLogLvl.Warning, Name);
                }

                string targetMode = options.Mode ?? (isLegacy ? "Stable" : "Lazer");
                _host!.Logger.LogMessage($"Cleaning Mode: {targetMode} (Host Legacy: {isLegacy})", PawsLogLvl.Information, Name);

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
                catch (StableIsRunningException ex)
                {
                    _host.Logger.LogMessage($"Cleanup aborted: {ex.Message}", PawsLogLvl.Warning, Name);
                    return new { Success = false, Message = $"Cannot clean while osu!stable is running. Please close the game." };
                }
                catch (LazerIsRunningException ex)
                {
                    _host.Logger.LogMessage($"Cleanup aborted: {ex.Message}", PawsLogLvl.Warning, Name);
                    return new { Success = false, Message = $"Cannot clean while osu!lazer is running. Please close the game." };
                }
                catch (Exception ex)
                {
                    // If it's wrapped in AggregateException or TargetInvocationException due to async lambda
                    var inner = ex.InnerException ?? ex;
                    if (inner is StableIsRunningException || inner is LazerIsRunningException)
                    {
                        return new { Success = false, Message = $"Cannot clean while game is running. Please close it first." };
                    }

                    _host.Logger.LogMessage($"Strategy execution failed: {ex.Message}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Critical Error: {inner.Message}" };
                }
            }

            if (commandName == "importCustomBackgroundTemp")
            {
                if (_host == null) return new { Success = false };

                var options = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload ?? new object()));
                string? tempHandle = options.TryGetProperty("tempHandle", out var p) ? p.GetString() : null;

                if (string.IsNullOrEmpty(tempHandle)) return new { Success = false, Message = "No TempHandle provided." };

                try
                {
                    // 1. Get the stream from Temp Storage
                    using (var inputStream = _host.Storage.OpenTempStream(tempHandle))
                    {
                        // 2. Auto-compress to 1080p height for efficiency
                        var processOptions = new ImageProcessOptions
                        {
                            Height = 1080,
                            TargetFormat = "jpg",
                            Quality = 95
                        };

                        using (var processedStream = await _host.Image.ProcessImageAsync(inputStream, processOptions))
                        {
                            if (processedStream.CanSeek) processedStream.Position = 0;

                            string dataDir = _host.Storage.GetPluginDataPath();
                            string sourcePath = Path.Combine(dataDir, "custom_bg.src");

                            using (var fileStream = _host.Storage.OpenFile(sourcePath, FileMode.Create, FileAccess.Write))
                            {
                                await processedStream.CopyToAsync(fileStream);
                            }
                        }
                    }

                    _host.Logger.LogMessage($"[BG] Custom background imported and resized to 1080p", PawsLogLvl.Information, Name);
                    return new { Success = true };
                }
                catch (Exception ex)
                {
                    _host.Logger.LogMessage($"[BG ERROR] Import failed: {ex.Message}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = ex.Message };
                }
            }
            if (commandName == "checkCustomBackground")
            {
                if (_host == null) return new { Success = false };
                string dataDir = _host.Storage.GetPluginDataPath();
                string sourcePath = Path.Combine(dataDir, "custom_bg.src");
                return new { Success = true, Exists = _host.Storage.FileExists(sourcePath) };
            }
            return null;
        }
    }
}
