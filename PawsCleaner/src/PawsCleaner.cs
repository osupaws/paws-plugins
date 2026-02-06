using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Lazer;
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
                if (_host == null)
                    return new { Success = false, Message = "Host not initialized." };

                var options = JsonSerializer.Deserialize<CleanerOptions>(
                    JsonSerializer.Serialize(payload ?? new object()), 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (options == null)
                    return new { Success = false, Message = "Invalid payload options." };

                // Determine Mode: Use Payload override if present, otherwise ask Host
                bool isLegacy = false;
                try
                {
                    // Use dynamic to access IsLegacyMode in case the referenced DLL is stale
                    // but the runtime Host object supports it.
                    if (_host != null)
                    {
                        var dynHost = (dynamic)_host;
                        // Avoid direct property access if we aren't sure it exists (dynamic usually throws RuntimeBinderException if missing)
                        isLegacy = dynHost.IsLegacyMode; 
                    }
                }
                catch
                {
                    // Fallback or log if property missing
                   _host.LogMessage("Could not check IsLegacyMode, defaulting to False/Lazer", PawsLogLvl.Warning, Name);
                }

                string targetMode = options.Mode ?? (isLegacy ? "Stable" : "Lazer");

                _host!.LogMessage($"Cleaning Mode: {targetMode} (Host Legacy: {isLegacy})", PawsLogLvl.Information, Name);

                if (targetMode.Equals("Lazer", StringComparison.OrdinalIgnoreCase))
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
            
            return await Task.Run(() => 
            {
                // Get the new Safe Context
                var context = _host.GetLazerContext();
                if (context == null) return new { Success = false, Message = "Failed to access Lazer context (New API)." };

                _host.LogMessage("Starting Lazer cleanup (Safe Mode)...", PawsLogLvl.Information, Name);
                _host.LogMessage($"[CONFIG] Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, Name);
                _host.LogMessage($"[CONFIG] Delete Rulesets? Osu: {options.Rulesets?.Osu ?? false}, Taiko: {options.Rulesets?.Taiko ?? false}, Catch: {options.Rulesets?.Catch ?? false}, Mania: {options.Rulesets?.Mania ?? false}", PawsLogLvl.Information, Name);
                
                int setsProcessed = 0;
                int mapsDeleted = 0;
                // Detailed Stats
                int delOsu = 0, delTaiko = 0, delCatch = 0, delMania = 0, delOther = 0;
                // Source Stats
                int srcOsu = 0, srcTaiko = 0, srcCatch = 0, srcMania = 0, srcOther = 0;

                try
                {
                    // 1. Get DTOs (Safe, Detached)
                    var beatmapSets = context.GetBeatmapSets();
                    
                    var setsToDelete = new List<Guid>();
                    var mapsToDelete = new List<Guid>();

                    foreach (var set in beatmapSets)
                    {
                        bool setModified = false;
                        
                        // Track how many maps in this set will be deleted
                        int mapsInSetToDelete = 0;

                        // 1. Ruleset Cleaning
                        foreach (var map in set.Beatmaps)
                        {
                            // Gather Source Stats
                            switch (map.RulesetID)
                            {
                                case 0: srcOsu++; break;
                                case 1: srcTaiko++; break;
                                case 2: srcCatch++; break;
                                case 3: srcMania++; break;
                                default: srcOther++; break;
                            }

                            // map.RulesetID: 0=osu, 1=taiko, 2=catch, 3=mania
                            bool keep = true;
                            
                            switch (map.RulesetID) 
                            {
                                // Logic: Option=True (Checked) means DELETE. So Keep = False.
                                // If Option is null/false (Unchecked), Keep = True.
                                case 0: keep = !(options.Rulesets?.Osu ?? false); break;
                                case 1: keep = !(options.Rulesets?.Taiko ?? false); break;
                                case 2: keep = !(options.Rulesets?.Catch ?? false); break;
                                case 3: keep = !(options.Rulesets?.Mania ?? false); break;
                                default: keep = true; break; // Always keep unknown/custom rulesets
                            }

                            if (!keep)
                            {
                                mapsToDelete.Add(map.ID);
                                mapsDeleted++;
                                mapsInSetToDelete++;
                                setModified = true;

                                // Update stats
                                switch (map.RulesetID)
                                {
                                    case 0: delOsu++; break;
                                    case 1: delTaiko++; break;
                                    case 2: delCatch++; break;
                                    case 3: delMania++; break;
                                    default: delOther++; break;
                                }
                            }
                        }

                        // Check if whole set should be deleted (if all maps are gone)
                        // Note: set.Beatmaps is a List in the DTO, so Count is safe.
                        if (set.Beatmaps.Count > 0 && mapsInSetToDelete == set.Beatmaps.Count)
                        {
                            setsToDelete.Add(set.ID);
                        }

                        // 2. Asset Cleaning (Videos/Storyboards)
                        // Current ILazerContext does not support deleting individual files (LazerFiles).
                        // We can only delete Beatmaps or Sets.
                        // Future: UpdateBeatmapSet(set) with removed files.
                        if (options.Assets?.Videos == true || options.Assets?.Storyboards == true)
                        {
                            // _host.LogMessage("Asset cleaning (Videos/Storyboards) is temporarily disabled in Safe Mode.", PawsLogLvl.Warning, Name);
                        }

                        if (setModified) setsProcessed++;
                    }

                    // Execute Deletions
                    if (mapsToDelete.Count > 0)
                    {
                        if (options.DryRun)
                            _host.LogMessage($"[DRY RUN] Would delete {mapsToDelete.Count} beatmaps.", PawsLogLvl.Information, Name);
                        else
                        {
                            _host.LogMessage($"Deleting {mapsToDelete.Count} beatmaps...", PawsLogLvl.Information, Name);
                            context.DeleteBeatmaps(mapsToDelete);
                        }
                    }

                    if (setsToDelete.Count > 0)
                    {
                        if (options.DryRun)
                            _host.LogMessage($"[DRY RUN] Would delete {setsToDelete.Count} beatmap sets.", PawsLogLvl.Information, Name);
                        else
                        {
                            _host.LogMessage($"Deleting {setsToDelete.Count} beatmap sets...", PawsLogLvl.Information, Name);
                            context.DeleteBeatmapSets(setsToDelete);
                        }
                    }

                    string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
                    string srcStats = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";
                    
                    _host.LogMessage($"[ANALYSIS] Source Distribution: {srcStats}", PawsLogLvl.Information, Name);

                    if (options.DryRun)
                    {
                        _host.LogMessage($"[DRY RUN SUMMARY] Maps to Delete: {mapsToDelete.Count}. Sets to Delete: {setsToDelete.Count}.", PawsLogLvl.Information, Name);
                        _host.LogMessage($"[DRY RUN STATS] Breakdown by Ruleset: {stats}", PawsLogLvl.Information, Name);
                        
                        if (options.Assets?.Videos == true || options.Assets?.Storyboards == true)
                        {
                            _host.LogMessage($"[DRY RUN WARN] Asset cleaning is temporarily disabled in this version. Assets would NOT be removed.", PawsLogLvl.Warning, Name);
                        }
                    }

                    string msg = options.DryRun 
                        ? $"[DRY RUN] Found {mapsToDelete.Count} maps ({stats}) and {setsToDelete.Count} sets to delete. (Source: {srcStats})"
                        : $"Cleanup Complete. Processed {setsProcessed} sets. Deleted {mapsDeleted} maps. ({stats})";

                    return new 
                    { 
                        Success = true, 
                        Message = msg + (options.DryRun ? "" : " (Asset cleaning skipped in Safe Mode)") 
                    };
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"Lazer cleanup error: {ex}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }
    }

    public class CleanerOptions
    {
        public string? Mode { get; set; }
        public bool DryRun { get; set; } // For testing safely
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
