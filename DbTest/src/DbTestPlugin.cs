using Paws.Core.Abstractions;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OsuParsers.Database;
using OsuParsers.Decoders;
using Realms;

namespace DbTestPlugin;

/// <summary>
/// A simple plugin to test the framework's ability to interact with the osu!lazer and osu!stable databases.
/// </summary>
public class DbTestPlugin : IFunctionalExplicitPlugin
{
    private IHostServices _hostServices = null!;

    // --- IPlugin Properties ---
    public Guid Id => new("a1b2c3d4-e5f6-4a9b-8c7d-6e5f4a3b2c1d");
    public string Name => "DB Test";
    public string Description => "A plugin to test reading/writing to Lazer and Stable databases based on the selected mode.";
    public string Version => "0.0.1";
    public string Author => "Paws Team";

    // --- IFunctionalExplicitPlugin Properties ---
    public string IconName => "database"; // A placeholder name for an icon in the UI

    /// <summary>
    /// This method is called by the Paws framework when the plugin is loaded.
    /// It's the entry point for the plugin's backend logic.
    /// </summary>
    public void Initialize(IHostServices hostServices)
    {
        // We receive the host services from the framework and store the reference.
        // This is how the plugin will talk to the rest of Paws.
        _hostServices = hostServices;
        _hostServices.LogMessage("DB Test Plugin Initialized!", PawsLogLvl.Information, Name);
    }

    /// <summary>
    /// This method is called by the framework when the plugin's UI sends a command.
    /// It acts as a router to the correct internal method based on the current client mode.
    /// </summary>
    public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
    {
        // Deserialize the payload to get the mode.
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var commandPayload = JsonSerializer.Deserialize<CommandPayload>(JsonSerializer.Serialize(payload), options);

        // Default to "stable" if the mode is not provided for safety.
        string mode = commandPayload?.Mode ?? "stable";

        return commandName switch
        {
            "test-read" => await TestReadAsync(mode),
            "test-write" => await TestWriteAsync(mode),
            _ => throw new ArgumentException($"Unknown command received: {commandName}"),
        };
    }

    /// <summary>
    /// Performs a read test on the appropriate database based on the provided mode.
    /// </summary>
    private async Task<string> TestReadAsync(string mode)
    {
        if (mode == "lazer")
        {
            using var db = _hostServices.GetLazerDatabase();
            if (db == null) return "Error: Lazer database path is not set or file is inaccessible.";

            var beatmaps = db.DynamicApi.All("Beatmap");
            return $"Lazer Mode: Found {beatmaps.Count} beatmap difficulties.";
        }
        else // Stable Mode
        {
            var stable = _hostServices.GetStableContext();
            string result = "Checking Wrappers...";
            
            await _hostServices.PerformStableWriteAsync(root => {
                 var dbPath = Path.Combine(root, "osu!.db");
                 var db = stable.ReadOsuDatabase(dbPath);
                 result = $"Stable Mode (Wrapper): Found {db.Beatmaps.Count()} beatmap difficulties using GetStableContext().";
            });
            
            return result;
        }
    }

    /// <summary>
    /// Performs a safe write test on the appropriate database based on the provided mode.
    /// </summary>
    private async Task<string> TestWriteAsync(string mode)
    {
        if (mode == "lazer")
        {
            try
            {
                string resultMessage = "Lazer Mode: Could not find an unprotected beatmap set to test with.";
                await _hostServices.PerformLazerWriteAsync(db =>
                {
                    dynamic? firstSet = db.DynamicApi.All("BeatmapSet").Filter("Protected == false").FirstOrDefault();
                    if (firstSet == null) return;

                    // Perform a harmless write operation by flipping a boolean and flipping it back.
                    bool originalValue = firstSet.DeletePending;
                    firstSet.DeletePending = !originalValue;
                    firstSet.DeletePending = originalValue;
                    resultMessage = $"Lazer Mode: Success! Performed a safe test write.";
                });
                return resultMessage;
            }
            catch (Exception ex)
            {
                _hostServices.LogMessage($"Lazer write test failed: {ex.Message}", PawsLogLvl.Error, Name);
                return $"Lazer Mode Error: {ex.Message}";
            }
        }
        else // Stable Mode
        {
            try
            {
                string resultMessage = "Stable Mode Write: Test completed.";
                await _hostServices.PerformStableWriteAsync(stablePath =>
                {
                    // Test Wrapper Writing
                    var stable = _hostServices.GetStableContext();
                    var dbPath = Path.Combine(stablePath, "osu!.db");
                    var db = stable.ReadOsuDatabase(dbPath);
                    
                    var tempPath = Path.Combine(Path.GetTempPath(), "paws_stable_wrapper_test.db");
                    
                    // Use Wrapper to save
                    stable.WriteOsuDatabase(db, tempPath);
                    
                    if (File.Exists(tempPath)) File.Delete(tempPath);

                    resultMessage = "Stable Mode (Wrapper): Success! Read and Write via Wrapper works.";
                });
                return resultMessage;
            }
            catch (Exception ex)
            {
                _hostServices.LogMessage($"Stable write test failed: {ex.Message}", PawsLogLvl.Error, Name);
                return $"Stable Mode Error: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// A simple private record used to deserialize the JSON payload from the frontend.
    /// This makes accessing payload properties clean and type-safe.
    /// </summary>
    private record CommandPayload(string Mode);
}
