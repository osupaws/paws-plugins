using Paws.Core.Abstractions;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Realms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PawsCleaner.Strategies.Lazer
{
    public class LazerCleanerStrategy : ICleanerStrategy
    {
        private readonly IHostServices _host;
        public string Name => "Lazer Cleaner";

        private const string CACHE_FILENAME = "lazer_cache.realm";

        public LazerCleanerStrategy(IHostServices host)
        {
            _host = host;
        }

        public async Task<object> CleanAsync(CleanerOptions options)
        {
            if (_host == null) return new { Success = false, Message = "Host not initialized." };

            return await Task.Run(() =>
            {
                var context = _host.GetLazerContext();
                if (context == null) return new { Success = false, Message = "Failed to access ILazerContext (Core 2.0)." };

                _host.LogMessage("Starting Lazer cleanup (Core 2.0)...", PawsLogLvl.Information, Name);
                _host.LogMessage($"[CONFIG] Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, Name);

                // --- CACHE SETUP ---
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PawsCleaner");
                Directory.CreateDirectory(appData);
                string cachePath = Path.Combine(appData, CACHE_FILENAME);

                // Initialize Realm Cache
                // Using Schema explicitly
                var realmConfig = new RealmConfiguration(cachePath)
                {
                    SchemaVersion = 1,
                    Schema = new[] { typeof(CachedLazerSet) }
                };

                Realm? cacheRealm = null;
                try
                {
                    cacheRealm = Realm.GetInstance(realmConfig);
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"[CACHE WARNING] Could not open cache DB: {ex.Message}. Caching disabled.", PawsLogLvl.Warning, Name);
                    try { Realm.DeleteRealm(realmConfig); } catch { } // Safe cleanup if corrupted
                }

                string currentOptionsHash = CachedLazerSet.ComputeOptionsHash(options);
                int skippedByCache = 0;

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

                    var setsToProcess = new List<dynamic>(); // Using dynamic to hold references

                    // --- FILTERING STEP ---
                    foreach (var set in beatmapSets)
                    {
                        if (options.DryRun || cacheRealm == null)
                        {
                            setsToProcess.Add(set);
                            continue;
                        }

                        string setIdStr = set.ID.ToString();
                        string? setHash = null;

                        try { setHash = ((dynamic)set).Hash; } catch { }

                        if (!string.IsNullOrEmpty(setHash))
                        {
                            var cached = cacheRealm.Find<CachedLazerSet>(setIdStr);
                            if (cached != null && cached.SetHash == setHash && cached.OptionsHash == currentOptionsHash)
                            {
                                skippedByCache++;
                                continue;
                            }
                        }
                        setsToProcess.Add(set);
                    }

                    if (skippedByCache > 0 && !options.DryRun)
                        _host.LogMessage($"[CACHE] Skipped {skippedByCache} clean beatmap sets.", PawsLogLvl.Information, Name);


                    // --- PREPARE ASSETS (BG Replacement) ---
                    var assets = options.Assets;
                    string bgMode = assets?.BackgroundMode?.ToLowerInvariant() ?? "keep";
                    dynamic? importedJpg = null;
                    dynamic? importedPng = null;
                    bool bgImported = false;

                    if ((bgMode == "white" || bgMode == "custom") && !options.DryRun && setsToProcess.Count > 0)
                    {
                        // (Same BG import logic as before)
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
                                importedJpg = ((dynamic)context).ImportFile(sourceJpg);
                                if (tempJpgCreated) File.Delete(sourceJpg);
                            }
                            if (!string.IsNullOrEmpty(sourcePng) && File.Exists(sourcePng))
                            {
                                importedPng = ((dynamic)context).ImportFile(sourcePng);
                                if (tempPngCreated) File.Delete(sourcePng);
                            }

                            if (importedJpg != null || importedPng != null)
                                bgImported = true;
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"[BG ERROR] BG Prep failed: {ex.Message}", PawsLogLvl.Error, Name);
                        }
                    }

                    // --- PROCESSING LOOP ---
                    _host.LogMessage($"Processing {setsToProcess.Count} sets...", PawsLogLvl.Information, Name);

                    foreach (var set in setsToProcess)
                    {
                        bool setModified = false;
                        int mapsInSetToDelete = 0;
                        var filesToRemove = new List<dynamic>();

                        // A. Ruleset Logic
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
                            // Check options...
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
                            continue; // Skip asset cleaning for this set
                        }

                        // B. Asset Logic (Only if not deleting whole set)
                        if (!options.DryRun)
                        {
                            // 1. Identify Protected Files
                            var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var bgFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (var map in set.Beatmaps)
                            {
                                try
                                {
                                    string? audio = map.Metadata?.AudioFile;
                                    if (!string.IsNullOrEmpty(audio)) protectedFiles.Add(audio);
                                }
                                catch { }

                                string? bg = map.Metadata?.BackgroundFile;
                                if (!string.IsNullOrEmpty(bg))
                                {
                                    bgFiles.Add(bg);
                                    if (!bgImported) protectedFiles.Add(bg);
                                }
                            }

                            if (set.Files != null)
                            {
                                foreach (var fileUsage in set.Files)
                                {
                                    string? fname = fileUsage.Filename;
                                    if (string.IsNullOrEmpty(fname)) continue;

                                    string ext = AssetUtils.GetExtension(fname);
                                    if (ext == ".osu") continue;

                                    bool isBg = bgFiles.Contains(fname);
                                    bool isProtected = protectedFiles.Contains(fname);

                                    // BG Replace
                                    if (isBg && bgImported)
                                    {
                                        dynamic? targetReplacement = (ext == ".png") ? (importedPng ?? importedJpg) : (importedJpg ?? importedPng);
                                        if (targetReplacement != null && fileUsage.File?.Hash != targetReplacement?.Hash)
                                        {
                                            fileUsage.File = targetReplacement;
                                            setModified = true;
                                        }
                                        continue;
                                    }

                                    if (isProtected) continue;

                                    bool shouldUnlink = false;

                                    // Videos
                                    if (assets?.Videos == true && AssetUtils.IsVideo(ext)) shouldUnlink = true;

                                    // Storyboards
                                    if (assets?.Storyboards == true)
                                    {
                                        if (AssetUtils.IsStoryboard(ext))
                                        {
                                            shouldUnlink = true;
                                        }
                                        else if (fname.StartsWith("sb/", StringComparison.OrdinalIgnoreCase) || fname.StartsWith("sb\\", StringComparison.OrdinalIgnoreCase))
                                        {
                                            shouldUnlink = true;
                                        }
                                    }

                                    // Skins
                                    if (assets?.Skins == true && KnownFiles.IsSkinnable(fname) && !isBg) shouldUnlink = true;

                                    // Nuclear
                                    if (assets?.Videos == true && assets?.Storyboards == true && assets?.Skins == true)
                                    {
                                        if (!shouldUnlink && !isProtected && !isBg) shouldUnlink = true;
                                    }

                                    // Audio
                                    if (assets?.Sounds == true && AssetUtils.IsAudio(ext) && !isProtected) shouldUnlink = true;

                                    if (shouldUnlink)
                                    {
                                        filesToRemove.Add(fileUsage);
                                        setModified = true;
                                    }
                                }

                                foreach (var f in filesToRemove)
                                {
                                    set.Files.Remove(f);
                                }
                            }
                        }

                        // Apply Updates
                        if (setModified)
                        {
                            setsProcessed++;
                            if (!options.DryRun)
                            {
                                try
                                {
                                    ((dynamic)context).UpdateBeatmapSet(set);
                                }
                                catch (Exception ex)
                                {
                                    _host.LogMessage($"[Set Update Error] {set.ID}: {ex.Message}", PawsLogLvl.Error, Name);
                                }
                            }
                        }

                        // --- UPDATE CACHE ---
                        // Only if not dry run, not deleted, and cache is active
                        if (!options.DryRun && cacheRealm != null && !setsToDelete.Contains(set.ID))
                        {
                            // Retrieve updated hash
                            string setIdStr = set.ID.ToString();
                            string? newHash = null;
                            try { newHash = ((dynamic)set).Hash; } catch { }

                            if (!string.IsNullOrEmpty(newHash))
                            {
                                cacheRealm!.Write(() =>
                                {
                                    cacheRealm.Add(new CachedLazerSet
                                    {
                                        SetId = setIdStr,
                                        SetHash = newHash,
                                        OptionsHash = currentOptionsHash,
                                        LastCleanTime = DateTimeOffset.UtcNow
                                    }, update: true);
                                });
                            }
                        }
                    }

                    // --- EXECUTE MASS DELETIONS ---
                    if (mapsToDelete.Count > 0)
                    {
                        if (options.DryRun) _host.LogMessage($"[DRY RUN] Would delete {mapsToDelete.Count} beatmaps.", PawsLogLvl.Information, Name);
                        else
                        {
                            ((dynamic)context).DeleteBeatmaps(mapsToDelete);
                        }
                    }

                    if (setsToDelete.Count > 0)
                    {
                        if (options.DryRun) _host.LogMessage($"[DRY RUN] Would delete {setsToDelete.Count} beatmap sets.", PawsLogLvl.Information, Name);
                        else
                        {
                            ((dynamic)context).DeleteBeatmapSets(setsToDelete);
                            // Clean cache for deleted sets
                            if (cacheRealm != null)
                            {
                                cacheRealm.Write(() =>
                                {
                                    foreach (var sid in setsToDelete)
                                    {
                                        var obj = cacheRealm.Find<CachedLazerSet>(sid.ToString());
                                        if (obj != null) cacheRealm.Remove(obj);
                                    }
                                });
                            }
                        }
                    }

                    // --- ORPHANS ---
                    if (!options.DryRun)
                    {
                        try
                        {
                            List<string> safeOrphans = ((dynamic)context).GetSafeOrphanHashes();
                            if (safeOrphans.Count > 0)
                            {
                                ((dynamic)context).DeleteFiles(safeOrphans);
                            }
                        }
                        catch { }
                    }

                    string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
                    string msg = options.DryRun
                        ? $"[DRY RUN] Found {mapsToDelete.Count} maps and {setsToDelete.Count} sets to delete."
                        : $"Cleanup Complete. Processed {setsProcessed} sets (Skipped {skippedByCache}). Deleted {mapsDeleted} maps. ({stats})";

                    return new { Success = true, Message = msg };
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"Lazer cleanup error: {ex}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Error: {ex.Message}" };
                }
                finally
                {
                    cacheRealm?.Dispose();
                }
            });
        }
    }
}
