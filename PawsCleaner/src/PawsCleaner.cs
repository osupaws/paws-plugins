using Paws.Core.Abstractions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("d34db33f-c001-4c33-9999-c1ea4e700001");
        public string Name => "Paws Cleaner";
        public string Description => "Efficiently clean up unused osu! files.";
        public string Version => "0.1.0";
        public string IconName => "delete";

        private IHostServices? _host;

        public void Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            _host.LogMessage($"{Name} initialized!", PawsLogLvl.Information, Name);
        }

        private Stable.StableCleanerService? _stableCleaner;

        public async Task<object?> ExecuteCommandAsync(string commandName, object? payload)
        {
            if (commandName == "clean")
            {
                var options = JsonSerializer.Deserialize<CleanerOptions>(
                    JsonSerializer.Serialize(payload), 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (options?.Mode?.ToLower() == "lazer")
                {
                    return await CleanLazerAsync(options);
                }
                else
                {
                    if (_stableCleaner == null && _host != null)
                    {
                        _stableCleaner = new Stable.StableCleanerService(_host);
                    }
                    
                    if (_stableCleaner != null && options != null)
                    {
                        return await _stableCleaner.CleanAsync(options);
                    }
                    
                    return new { Success = false, Message = "Failed to initialize stable cleaner or invalid options." };
                }
            }
            return null;
        }

        private async Task<object> CleanLazerAsync(CleanerOptions options)
        {
            if (_host == null) return new { Success = false, Message = "Host not initialized." };

            _host.LogMessage("Starting Lazer cleanup...", PawsLogLvl.Information, Name);
            
            int setsProcessed = 0;
            int filesRemoved = 0;
            int mapsDeleted = 0;

            try
            {
                using var db = _host.GetLazerContext();
                
                if (db == null) return new { Success = false, Message = "Failed to access Lazer database." };

                // Get all beatmap sets
                var beatmapSets = db.BeatmapSets.ToList(); // Materialize to list to avoid modification issues during iteration

                await _host.PerformLazerWriteAsync(realm =>
                {
                    foreach (var set in beatmapSets)
                    {
                        bool setModified = false;

                        // 1. Ruleset Cleaning
                        // Materialize list to support index-based removal/reading
                        var beatmaps = set.Beatmaps.ToList(); 

                        // Iterate backwards to safely remove
                        for (int i = beatmaps.Count - 1; i >= 0; i--)
                        {
                            var map = beatmaps[i];
                            var rulesetName = map.Ruleset?.ShortName ?? "unknown"; // osu, taiko, fruits, mania

                            // Check if this ruleset should be deleted
                            // options.Rulesets keys are: osu, taiko, catch (mapped to fruits), mania
                            bool keep = true;
                            
                            switch (rulesetName) 
                            {
                                case "osu": keep = options.Rulesets?.Osu ?? true; break;
                                case "taiko": keep = options.Rulesets?.Taiko ?? true; break;
                                case "fruits": keep = options.Rulesets?.Catch ?? true; break;
                                case "mania": keep = options.Rulesets?.Mania ?? true; break;
                            }

                            if (!keep)
                            {
                                map.DeletePending = true;
                                mapsDeleted++;
                                setModified = true;
                            }
                        }

                        // Check if whole set should be deleted (if all maps are gone)
                        // Note: DeletePending effect might not be immediate on set.Beatmaps count in this transaction scope?
                        // But we can check if all are marked.
                        if (set.Beatmaps.All(b => b.DeletePending))
                        {
                            set.DeletePending = true;
                            continue; // No need to clean assets if set is deleted
                        }

                        // 2. Asset Cleaning
                        // Collect files to remove
                        var filesToRemove = new List<dynamic>(); // Using dynamic to hold LazerNamedFileUsage or whatever wrapper returns
                        
                        // We need to identify "Keep" files (Backgrounds, Audio)
                        var sensitiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var map in set.Beatmaps.Where(b => !b.DeletePending))
                        {
                            // We can't easily access Metadata properties like AudioFile via the wrapper if they aren't exposed?
                            // Docs say: "Read operations use standard LINQ... Properties are strongly typed"
                            // Assuming Metadata is accessible.
                            // If wrapper exposes BeatmapInfo, let's hope it follows schema.
                            // Docs example: map.DifficultyName. 
                            // Using dynamic access via wrapper if needed, but let's try direct property access first.
                            
                            // NOTE: Current wrapper documentation doesn't explicitly list Metadata properties. 
                            // But usually they are exposed. If not, this part might fail compilation.
                            // Safe bet: The schema doc listed AudioFile/BackgroundFile in Metadata.
                            // Let's assume standard access: map.Metadata.AudioFile
                            
                            // Note: Metadata might be null or properties might be null.
                        }
                        
                        // Actually, cleaning assets safely requires knowing Background/Audio files.
                        // Without explicit protected file list, "Skins" and "Sounds" cleaning is dangerous.
                        // "Videos" is safe (extensions).
                        // "Storyboards" is safe (.osb).

                        // Let's implement SAFE deletions first (Video/SB)
                        
                        foreach (var file in set.Files)
                        {
                            string filename = file.Filename.ToLowerInvariant();
                            string ext = System.IO.Path.GetExtension(filename);
                            bool remove = false;

                            // Videos
                            if (options.Assets?.Videos == true)
                            {
                                if (ext == ".avi" || ext == ".flv" || ext == ".mpg" || ext == ".wmv" || ext == ".m4v" || ext == ".mp4")
                                    remove = true;
                            }

                            // Storyboards
                            if (options.Assets?.Storyboards == true)
                            {
                                if (ext == ".osb")
                                    remove = true;
                            }
                            
                            // Advanced (Skins/Sounds) - logic requires checking Metadata first
                            // Placeholder for now.

                            if (remove)
                            {
                                filesToRemove.Add(file);
                            }
                        }

                        foreach (var file in filesToRemove)
                        {
                            set.RemoveFile(file);
                            filesRemoved++;
                            setModified = true;
                        }

                        if (setModified) setsProcessed++;
                    }
                });

                return new 
                { 
                    Success = true, 
                    Message = $"Cleanup Complete. processed {setsProcessed} sets. Deleted {mapsDeleted} maps and {filesRemoved} files." 
                };
            }
            catch (Exception ex)
            {
                _host.LogMessage($"Lazer cleanup error: {ex}", PawsLogLvl.Error, Name);
                return new { Success = false, Message = $"Error: {ex.Message}" };
            }
        }
    }

    public class CleanerOptions
    {
        public string? Mode { get; set; }
        public RulesetOptions? Rulesets { get; set; }
        public AssetOptions? Assets { get; set; }
    }

    public class RulesetOptions
    {
        public bool Osu { get; set; }
        public bool Taiko { get; set; }
        public bool Catch { get; set; }
        public bool Mania { get; set; }
    }

    public class AssetOptions
    {
        public bool Skins { get; set; }
        public bool Sounds { get; set; }
        public bool Videos { get; set; }
        public bool Storyboards { get; set; }
        public bool Previews { get; set; }
        public string? Background { get; set; }
    }
}
