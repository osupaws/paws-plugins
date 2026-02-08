using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Lazer;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO;
using Realms;

namespace PawsCleaner
{
    public class PawsCleanerPlugin : IFunctionalExplicitPlugin
    {
        public Guid Id => Guid.Parse("d34db33f-c001-4c33-9999-c1ea4e700001");
        public string Name => "Paws Cleaner";
        public string Description => "Efficiently clean up unused osu! files.";
        public string Version => "0.2.0";
        public string IconName => "delete";

        private IHostServices? _host;

        public async Task Initialize(IHostServices hostServices)
        {
            _host = hostServices;
            _host.LogMessage($"{Name} initialized (Async)!", PawsLogLvl.Information, Name);
            await Task.CompletedTask;
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
                // New Core 2.0 API: ILazerContext
                var context = _host.GetLazerContext();
                if (context == null) return new { Success = false, Message = "Failed to access ILazerContext (Core 2.0)." };

                _host.LogMessage("Starting Lazer cleanup (Core 2.0)...", PawsLogLvl.Information, Name);
                _host.LogMessage($"[CONFIG] Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, Name);

                int setsProcessed = 0;
                int mapsDeleted = 0;
                // Detailed Stats
                int delOsu = 0, delTaiko = 0, delCatch = 0, delMania = 0, delOther = 0;
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
                        int mapsInSetToDelete = 0;

                        // 1. Ruleset Cleaning
                        foreach (var map in set.Beatmaps)
                        {
                            int rid = map.RulesetID;

                            // Gather Source Stats
                            switch (rid)
                            {
                                case 0: srcOsu++; break;
                                case 1: srcTaiko++; break;
                                case 2: srcCatch++; break;
                                case 3: srcMania++; break;
                                default: srcOther++; break;
                            }

                            bool keep = true;
                            switch (rid)
                            {
                                case 0: keep = !(options.Rulesets?.Osu ?? false); break;
                                case 1: keep = !(options.Rulesets?.Taiko ?? false); break;
                                case 2: keep = !(options.Rulesets?.Catch ?? false); break;
                                case 3: keep = !(options.Rulesets?.Mania ?? false); break;
                                default: keep = true; break;
                            }

                            if (!keep)
                            {
                                mapsToDelete.Add(map.ID);
                                mapsDeleted++;
                                mapsInSetToDelete++;
                                setModified = true;

                                switch (rid)
                                {
                                    case 0: delOsu++; break;
                                    case 1: delTaiko++; break;
                                    case 2: delCatch++; break;
                                    case 3: delMania++; break;
                                    default: delOther++; break;
                                }
                            }
                        }

                        // Check if whole set should be deleted
                        if (set.Beatmaps.Count > 0 && mapsInSetToDelete == set.Beatmaps.Count)
                        {
                            setsToDelete.Add(set.ID);
                        }

                        if (setModified) setsProcessed++;
                    }

                    // 2. Background Replacement & Asset Cleaning
                    var assets = options.Assets;
                    string bgMode = assets?.BackgroundMode?.ToLowerInvariant() ?? "keep";
                    string debugAssets = (assets == null) ? "null" : $"BGMode='{assets.BackgroundMode}'";

                    if (!options.DryRun)
                        _host.LogMessage($"[DEBUG] Assets Info: {debugAssets}", PawsLogLvl.Information, Name);

                    // Import BGs if needed
                    dynamic? importedJpg = null;
                    dynamic? importedPng = null;
                    bool bgImported = false;

                    if ((bgMode == "white" || bgMode == "custom") && !options.DryRun)
                    {
                        try
                        {
                            string sourceJpg = "";
                            string sourcePng = "";
                            bool tempJpgCreated = false;
                            bool tempPngCreated = false;

                            if (bgMode == "white")
                            {
                                string whiteJpgB64 = "/9j/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/wgALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/aAAgBAQAAAAB/P//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Af//Z";
                                string whitePngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABAQAAAAA3bvkkAAAACklEQVR42mNoAAAAggCB2kUIOwAAAABJRU5ErkJggg==";

                                sourceJpg = Path.Combine(Path.GetTempPath(), "paws_white.jpg");
                                sourcePng = Path.Combine(Path.GetTempPath(), "paws_white.png");

                                File.WriteAllBytes(sourceJpg, Convert.FromBase64String(whiteJpgB64));
                                File.WriteAllBytes(sourcePng, Convert.FromBase64String(whitePngB64));
                                tempJpgCreated = true;
                                tempPngCreated = true;
                            }
                            else if (bgMode == "custom" && assets != null)
                            {
                                if (!string.IsNullOrEmpty(assets.CustomBackgroundJpg))
                                {
                                    try
                                    {
                                        string b64 = assets.CustomBackgroundJpg.Contains(",") ? assets.CustomBackgroundJpg.Split(',')[1] : assets.CustomBackgroundJpg;
                                        sourceJpg = Path.Combine(Path.GetTempPath(), "paws_custom.jpg");
                                        File.WriteAllBytes(sourceJpg, Convert.FromBase64String(b64));
                                        tempJpgCreated = true;
                                    }
                                    catch { }
                                }

                                if (!string.IsNullOrEmpty(assets.CustomBackgroundPng))
                                {
                                    try
                                    {
                                        string b64 = assets.CustomBackgroundPng.Contains(",") ? assets.CustomBackgroundPng.Split(',')[1] : assets.CustomBackgroundPng;
                                        sourcePng = Path.Combine(Path.GetTempPath(), "paws_custom.png");
                                        File.WriteAllBytes(sourcePng, Convert.FromBase64String(b64));
                                        tempPngCreated = true;
                                    }
                                    catch { }
                                }
                            }

                            if (!string.IsNullOrEmpty(sourceJpg) && File.Exists(sourceJpg))
                            {
                                importedJpg = context.ImportFile(sourceJpg);
                                if (tempJpgCreated) File.Delete(sourceJpg);
                            }
                            if (!string.IsNullOrEmpty(sourcePng) && File.Exists(sourcePng))
                            {
                                importedPng = context.ImportFile(sourcePng);
                                if (tempPngCreated) File.Delete(sourcePng);
                            }

                            if (importedJpg != null || importedPng != null)
                                bgImported = true;
                            else
                                _host.LogMessage("[BG WARNING] BG Mode active but imports failed.", PawsLogLvl.Warning, Name);
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"[BG ERROR] BG Prep failed: {ex.Message}", PawsLogLvl.Error, Name);
                        }
                    }

                    // MAIN CLEANING LOOP (BGs + Assets)
                    if (!options.DryRun)
                    {
                        int updatedSets = 0;
                        _host.LogMessage("[DEBUG] Starting Asset Cleaning Loop...", PawsLogLvl.Information, Name);

                        foreach (var set in beatmapSets)
                        {
                            if (setsToDelete.Contains(set.ID)) continue;

                            bool setModified = false;
                            var filesToRemove = new List<dynamic>(); // Declared at set scope

                            // 1. Identify Protected Files (BGs and Audios of valid maps)
                            var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var bgFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (var map in set.Beatmaps)
                            {
                                try
                                {
                                    string audio = map.Metadata?.AudioFile;
                                    if (!string.IsNullOrEmpty(audio)) protectedFiles.Add(audio);
                                }
                                catch { }

                                string bg = map.Metadata?.BackgroundFile;
                                if (!string.IsNullOrEmpty(bg))
                                {
                                    bgFiles.Add(bg);
                                    // BGs are protected unless we are replacing them
                                    if (!bgImported) protectedFiles.Add(bg);
                                }
                            }

                            if (set.Files != null)
                            {
                                // Note: We iterate a copy or index-based? No, standard foreach on List is fine 
                                // if we don't modify collection. We modify via filesToRemove later.
                                foreach (var fileUsage in set.Files)
                                {
                                    string fname = fileUsage.Filename;
                                    string ext = Path.GetExtension(fname).ToLowerInvariant();

                                    // CRITICAL: Protect .osu files (Difficulties) from asset cleaning!
                                    if (ext == ".osu") continue;

                                    bool isBg = bgFiles.Contains(fname);
                                    bool isProtected = protectedFiles.Contains(fname);

                                    // BG Replacement
                                    if (isBg && bgImported)
                                    {
                                        dynamic? targetReplacement = null;
                                        if (ext == ".png")
                                            targetReplacement = importedPng ?? importedJpg;
                                        else
                                            targetReplacement = importedJpg ?? importedPng;

                                        if (targetReplacement != null && fileUsage.File?.Hash != targetReplacement.Hash)
                                        {
                                            fileUsage.File = targetReplacement;
                                            setModified = true;
                                            _host.LogMessage($"[BG Replace] {fname}", PawsLogLvl.Information, Name);
                                        }
                                        continue; // Done with BG
                                    }

                                    // Asset Cleaning
                                    if (isProtected) continue;
                                    // Note: If isBg=true but bgImported=false, it's added to protectedFiles above.

                                    bool shouldUnlink = false;
                                    string reason = "";

                                    // Video
                                    if (assets?.Videos == true)
                                    {
                                        if (ext == ".avi" || ext == ".mp4" || ext == ".mkv" || ext == ".flv" || ext == ".m4v")
                                        {
                                            shouldUnlink = true;
                                            reason = "Video";
                                        }
                                    }

                                    // Storyboard (OsuParsers via Core)
                                    if (assets?.Storyboards == true)
                                    {
                                        if (ext == ".osb")
                                        {
                                            shouldUnlink = true;
                                            reason = "Storyboard Script";

                                            // Trigger advanced parsing to find dependent assets
                                            try
                                            {
                                                if (fileUsage.File?.Hash != null)
                                                {
                                                    List<string> sbAssets = ((dynamic)context).GetStoryboardAssetPaths(fileUsage.File.Hash);
                                                    if (sbAssets != null && sbAssets.Count > 0)
                                                    {
                                                        // We found assets used by this SB. We must find them in the set and mark them too.
                                                        // Note: We can't modify 'set.Files' while iterating. 
                                                        // But we can add them to 'filesToRemove' if we can find the matching FileUsage object.
                                                        // Iterate set.Files again to find matches? Yes.

                                                        // Helper to find usages matching paths
                                                        var assetUsages = set.Files.Where(u => sbAssets.Contains(u.Filename, StringComparer.OrdinalIgnoreCase)).ToList();

                                                        foreach (var assetUsage in assetUsages)
                                                        {
                                                            // Check protection (BG/Audio) before adding
                                                            string aName = assetUsage.Filename;
                                                            if (!bgFiles.Contains(aName) && !protectedFiles.Contains(aName))
                                                            {
                                                                if (!filesToRemove.Contains(assetUsage))
                                                                {
                                                                    filesToRemove.Add(assetUsage);
                                                                    _host.LogMessage($"[Delete Mark] {aName} (SB Asset from Parser)", PawsLogLvl.Information, Name);
                                                                    setModified = true;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _host.LogMessage($"[Parser Warn] Failed to parse SB {fname}: {ex.Message}", PawsLogLvl.Warning, Name);
                                            }
                                        }
                                        // Still keep the folder heuristic as backup?
                                        else if (fname.StartsWith("sb/", StringComparison.OrdinalIgnoreCase) || fname.StartsWith("sb\\", StringComparison.OrdinalIgnoreCase))
                                        {
                                            shouldUnlink = true;
                                            reason = "Storyboard Asset (sb/ folder)";
                                        }
                                    }

                                    // Skins (Strict Check using Known Files list)
                                    if (assets?.Skins == true)
                                    {
                                        if (StableKnownFiles.IsSkinnable(fname) && !isBg)
                                        {
                                            shouldUnlink = true;
                                            reason = "Skin (Strict)";
                                        }
                                    }

                                    // NUCLEAR MODE (Catch-All)
                                    // If User selected ALL potentially removable assets (Videos + SB + Skins),
                                    // treat this as "Delete Everything Non-Protected".
                                    // This catches edge cases like 'sb/slider.png' that might not be strictly caught above.
                                    if (assets?.Videos == true && assets?.Storyboards == true && assets?.Skins == true)
                                    {
                                        if (!shouldUnlink && !isProtected && !isBg)
                                        {
                                            shouldUnlink = true;
                                            reason = "Nuclear Clean (Unidentified Asset)";
                                        }
                                    }

                                    // Sounds (Audio not Main)
                                    if (assets?.Sounds == true)
                                    {
                                        if ((ext == ".wav" || ext == ".mp3" || ext == ".ogg") && !isProtected)
                                        {
                                            shouldUnlink = true;
                                            reason = "Sound";
                                        }
                                    }

                                    if (shouldUnlink)
                                    {
                                        filesToRemove.Add(fileUsage);
                                        setModified = true;
                                        _host.LogMessage($"[Delete Mark] {fname} ({reason})", PawsLogLvl.Information, Name);
                                    }
                                }

                                // Apply Removals from List (Critical for Core Fix to work)
                                foreach (var f in filesToRemove)
                                {
                                    set.Files.Remove(f);
                                }

                            }

                            if (setModified)
                            {
                                try
                                {
                                    ((dynamic)context).UpdateBeatmapSet(set);
                                    updatedSets++;
                                }
                                catch (Exception ex)
                                {
                                    _host.LogMessage($"[Update ERROR] Set {set.ID}: {ex.Message}", PawsLogLvl.Error, Name);
                                }
                            }
                        }
                        _host.LogMessage($"[DEBUG] Cleanup Loop Finished. Updated {updatedSets} sets.", PawsLogLvl.Information, Name);

                        // --- V4 SAFE ORPHAN CLEANUP ---
                        // Identify safe-to-delete orphans using Core logic (protects Skins, Scores, etc.)
                        try
                        {
                            _host.LogMessage("[ORPHAN CHECK] Requesting safe orphan list from Core...", PawsLogLvl.Information, Name);

                            // Call Core API V4 (Dynamic)
                            // GetSafeOrphanHashes() returns list of hashes NOT used by Beatmaps, Skins, Scores, etc.
                            List<string> safeOrphans = ((dynamic)context).GetSafeOrphanHashes();

                            if (safeOrphans.Count > 0)
                            {
                                if (options.DryRun)
                                {
                                    _host.LogMessage($"[DRY RUN] Would delete {safeOrphans.Count} orphaned files (SAFE list) from Database.", PawsLogLvl.Information, Name);
                                }
                                else
                                {
                                    _host.LogMessage($"Deleting {safeOrphans.Count} orphaned files (SAFE list) from Database...", PawsLogLvl.Information, Name);
                                    ((dynamic)context).DeleteFiles(safeOrphans);
                                    _host.LogMessage("Orphan cleanup complete.", PawsLogLvl.Information, Name);
                                }
                            }
                            else
                            {
                                _host.LogMessage("No orphaned files found (SAFE check).", PawsLogLvl.Information, Name);
                            }
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"[ORPHAN ERROR] Failed to clean orphans: {ex.Message}", PawsLogLvl.Error, Name);
                        }
                    }

                    // Execute Deletions
                    if (mapsToDelete.Count > 0)
                    {
                        if (options.DryRun)
                            _host.LogMessage($"[DRY RUN] Would delete {mapsToDelete.Count} beatmaps.", PawsLogLvl.Information, Name);
                        else
                        {
                            try
                            {
                                _host.LogMessage($"Deleting {mapsToDelete.Count} beatmaps...", PawsLogLvl.Information, Name);
                                context.DeleteBeatmaps(mapsToDelete);
                                _host.LogMessage($"Deletion command sent for {mapsToDelete.Count} maps.", PawsLogLvl.Information, Name);
                            }
                            catch (Exception ex)
                            {
                                _host.LogMessage($"[DEL ERROR] DeleteBeatmaps failed: {ex.Message}", PawsLogLvl.Error, Name);
                            }
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

                    string msg = options.DryRun
                        ? $"[DRY RUN] Found {mapsToDelete.Count} maps and {setsToDelete.Count} sets to delete."
                        : $"Cleanup Complete. Processed {setsProcessed} sets. Deleted {mapsDeleted} maps. ({stats})";

                    return new { Success = true, Message = msg };
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"Lazer cleanup error: {ex}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }

        private string GetRulesetName(int id)
        {
            return id switch
            {
                0 => "Osu",
                1 => "Taiko",
                2 => "Catch",
                3 => "Mania",
                _ => "Other"
            };
        }
    }

    public class CleanerOptions
    {
        public string? Mode { get; set; }
        public bool DryRun { get; set; }
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

        public string? BackgroundMode { get; set; }
        public string? CustomBackgroundPng { get; set; }
        public string? CustomBackgroundJpg { get; set; }
    }
}
