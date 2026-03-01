using Paws.Core.Abstractions;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OsuParsers.Database;
using OsuParsers.Database.Objects;
using OsuParsers.Decoders;
using Realms;
using Paws.Core.Abstractions.Enums;

using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Interfaces.Services;

namespace DbTestPlugin;

/// <summary>
/// A simple plugin to test the framework's ability to interact with the osu!lazer and osu!stable databases.
/// </summary>
public class DbTestPlugin : IPawsPlugin, ISupportsLifecycle
{
    private IHost? _host;

    // --- IPlugin Properties ---
    public string Id => "osupaws.dbtest";
    public string Name => "DB Test";
    public string Description => "A plugin to test reading/writing to Lazer and Stable databases based on the selected mode.";
    public string Version => "0.0.2";
    public string IconName => "database";

    /// <summary>
    /// This method is called by the Paws framework when the plugin is loaded.
    /// It's the entry point for the plugin's backend logic.
    /// </summary>
    public Task Initialize(IHost host)
    {
        // We receive the host services from the framework and store the reference.
        // This is how the plugin will talk to the rest of Paws.
        _host = host;
        _host.Logger.LogMessage("DB Test Plugin Initialized (V3)!", PawsLogLvl.Information, Name);
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

    /// <summary>
    /// This method is called by the framework when the plugin's UI sends a command.
    /// It acts as a router to the correct internal method based on the current client mode.
    /// </summary>
    public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
    {
        return commandName switch
        {
            "test-stable-db" => await TestStableDbAsync(),
            "test-stable-scores" => await TestStableScoresAsync(),
            "test-stable-parse" => await TestStableParseAsync(),
            "test-stable-scan" => await TestStableScanAsync(),

            "test-lazer-db" => await TestLazerDbAsync(),
            "test-lazer-files" => await TestLazerFilesAsync(),

            _ => throw new ArgumentException($"Unknown command received: {commandName}"),
        };
    }

    // --- Stable Tests ---

    private async Task<object> TestStableDbAsync()
    {
        try
        {
            var result = "";
            await _host!.Stable.PerformStableWriteAsync(root =>
            {
                var context = _host.Stable.GetStableContext();
                var dbPath = Path.Combine(root, "osu!.db");
                var db = context.ReadOsuDatabase(dbPath);

                result = $"osu!.db Info:\n" +
                         $"- Version: {db.OsuVersion}\n" +
                         $"- Player: {db.PlayerName}\n" +
                         $"- Beatmaps: {db.Beatmaps.Count()}\n" +
                         $"- First Map: {db.Beatmaps.FirstOrDefault()?.Artist} - {db.Beatmaps.FirstOrDefault()?.Title}";
            });
            return result;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<object> TestStableScoresAsync()
    {
        try
        {
            // Scores DB is usually accessed via direct Host method as it might not be in StableContext wrapper yet
            var scoresDb = (ScoresDatabase?)await _host!.Stable.GetStableScoresDbAsync();
            if (scoresDb == null) return "ScoreDB result was null. This suggests 'scores.db' is missing or unreadable in your osu! folder. If you haven't played any maps, this file might not exist yet.";

            // OsuParsers ScoresDatabase object
            // Scores propery is List<Tuple<string, List<Score>>>
            var firstMapScores = scoresDb.Scores.FirstOrDefault();
            var firstScore = firstMapScores?.Item2.FirstOrDefault();

            return $"scores.db Info:\n" +
                   $"- Version: {scoresDb.OsuVersion}\n" +
                   $"- Beatmaps with Scores: {scoresDb.Scores.Count}\n" +
                   $"- First Score Player: {firstScore?.PlayerName ?? "N/A"}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<object> TestStableParseAsync()
    {
        try
        {
            var result = "";
            await _host!.Stable.PerformStableWriteAsync(root =>
            {
                var context = _host.Stable.GetStableContext();
                var db = context.ReadOsuDatabase(Path.Combine(root, "osu!.db"));

                var maps = db.Beatmaps.ToList();
                if (maps.Count == 0) { result = "No maps found in DB."; return; }

                // Pick random map
                var random = new Random();
                var map = maps[random.Next(maps.Count)];

                var songsDir = Path.Combine(root, "Songs");
                var osuPath = Path.Combine(songsDir, map.FolderName, map.FileName);

                var parsedMap = context.ParseBeatmap(osuPath);

                result = $"Parsed '{map.Artist} - {map.Title}':\n" +
                         $"- Audio: {parsedMap.AudioFilename}\n" +
                         $"- Background: {parsedMap.BackgroundImage}\n" +
                         $"- HitSounds Samples: {parsedMap.GetHitSoundSamples().Count()}\n" +
                         $"- Storyboard Present in .osu: {(parsedMap.EventsStoryboard != null && parsedMap.EventsStoryboard.GetAllReferencedFiles().Any())}";
            });
            return result;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<object> TestStableScanAsync()
    {
        try
        {
            var result = "";
            await _host!.Stable.PerformStableWriteAsync(root =>
            {
                var context = _host.Stable.GetStableContext();
                var db = context.ReadOsuDatabase(Path.Combine(root, "osu!.db"));

                var maps = db.Beatmaps.ToList();
                if (maps.Count == 0) { result = "No maps found."; return; }

                // Pick random map
                var random = new Random();
                var map = maps[random.Next(maps.Count)];

                var folderPath = Path.Combine(root, "Songs", map.FolderName);
                var assets = context.GetUsedAssets(folderPath);

                result = $"Scanned '{map.FolderName}':\n" +
                         $"- Total Used Assets: {assets.Count}\n" +
                         $"- Sample: {string.Join(", ", assets.Take(5))}";
            });
            return result;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // --- Lazer Tests ---

    private Task<object> TestLazerDbAsync()
    {
        try
        {
            var context = _host!.Lazer.GetLazerContext();
            // Note: ILazerContext in V3 might not expose sets directly. 
            // Disabling detailed output for now to allow compilation.
            return Task.FromResult<object>("Lazer Context Accessed (Stats disabled pending API update)");
        }
        catch (Exception ex) { return Task.FromResult<object>($"Error: {ex.Message}"); }
    }

    private Task<object> TestLazerFilesAsync()
    {
        try
        {
            var context = _host!.Lazer.GetLazerContext();
            return Task.FromResult<object>("Lazer Files Context Accessed (Stats disabled)");
        }
        catch (Exception ex) { return Task.FromResult<object>($"Error: {ex.Message}"); }
    }

    /// <summary>
    /// A simple private record used to deserialize the JSON payload from the frontend.
    /// This makes accessing payload properties clean and type-safe.
    /// </summary>
    private record CommandPayload(string Mode);
}
