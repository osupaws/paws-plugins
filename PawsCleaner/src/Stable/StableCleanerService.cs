using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Paws.Core.Abstractions;
using Realms;
using Realms.Schema;

namespace PawsCleaner.Stable
{
    public class StableCleanerService
    {
        private readonly IHostServices _host;
        private readonly string _indexDbPath;

        public StableCleanerService(IHostServices host)
        {
            _host = host;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var pluginData = Path.Combine(appData, "PawsCleaner");
            Directory.CreateDirectory(pluginData);
            _indexDbPath = Path.Combine(pluginData, "stable_index.realm");
        }

        public async Task<object> CleanAsync(CleanerOptions options)
        {
            // Stats
            int deletedMaps = 0;
            int deletedFiles = 0;
            // Detailed Stats (Deletion)
            int delOsu = 0, delTaiko = 0, delCatch = 0, delMania = 0, delOther = 0;
            // Detailed Stats (Source DB)
            int srcOsu = 0, srcTaiko = 0, srcCatch = 0, srcMania = 0, srcOther = 0;

            long freedBytes = 0;

            await _host.PerformStableWriteAsync(stablePath =>
            {
                var stable = _host.GetStableContext();
                var dbPath = Path.Combine(stablePath, "osu!.db");
                var songDir = Path.Combine(stablePath, "Songs");

                _host.LogMessage($"[CONFIG] Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, "StableCleaner");
                _host.LogMessage($"[CONFIG] Delete Rulesets? Osu: {options.Rulesets?.Osu ?? false}, Taiko: {options.Rulesets?.Taiko ?? false}, Catch: {options.Rulesets?.Catch ?? false}, Mania: {options.Rulesets?.Mania ?? false}", PawsLogLvl.Information, "StableCleaner");
                _host.LogMessage("Reading osu!.db...", PawsLogLvl.Information, "StableCleaner");

                var db = stable.ReadOsuDatabase(dbPath);

                // --- 1. Ruleset Cleaning ---
                var mapsToRemove = new List<dynamic>(); 
                var dbBeatmaps = db.Beatmaps.ToList();
                _host.LogMessage($"[ANALYSIS] Found {dbBeatmaps.Count} total beatmaps in osu!.db.", PawsLogLvl.Information, "StableCleaner");

                foreach (var map in dbBeatmaps)
                {
                    int rId = (int)map.Ruleset; // Cast assuming Enum or Int

                    // Source Stats
                    switch (rId)
                    {
                        case 0: srcOsu++; break;
                        case 1: srcTaiko++; break;
                        case 2: srcCatch++; break;
                        case 3: srcMania++; break;
                        default: srcOther++; break;
                    }

                    bool keep = true;

                    switch (rId)
                    {
                        // Logic: Option=True (Checked) means DELETE. So Keep = False.
                        case 0: keep = !(options.Rulesets?.Osu ?? false); break;
                        case 1: keep = !(options.Rulesets?.Taiko ?? false); break;
                        case 2: keep = !(options.Rulesets?.Catch ?? false); break;
                        case 3: keep = !(options.Rulesets?.Mania ?? false); break;
                        default: keep = true; break;
                    }

                    if (!keep)
                    {
                         // Deletion Stats
                         switch (rId)
                         {
                            case 0: delOsu++; break;
                            case 1: delTaiko++; break;
                            case 2: delCatch++; break;
                            case 3: delMania++; break;
                            default: delOther++; break;
                         }

                        if (options.DryRun)
                        {
                             // dry run - just count, maybe log detailed if needed but usually summary is enough
                             deletedMaps++;
                             mapsToRemove.Add(map); // Add to list for count, but don't delete
                        }
                        else
                        {
                            mapsToRemove.Add(map);
                            deletedMaps++;
                            
                            try
                            {
                                var osuPath = Path.Combine(songDir, map.FolderName, map.FileName);
                                if (File.Exists(osuPath))
                                {
                                    var fi = new FileInfo(osuPath);
                                    freedBytes += fi.Length;
                                    File.Delete(osuPath);
                                }
                            }
                            catch { /* Ignore IO errors */ }
                        }
                    }
                }

                if (!options.DryRun && mapsToRemove.Count > 0)
                {
                    _host.LogMessage($"Removing {mapsToRemove.Count} maps from DB...", PawsLogLvl.Information, "StableCleaner");
                    foreach (var m in mapsToRemove) db.RemoveBeatmap(m); 
                    stable.WriteOsuDatabase(db, dbPath);
                }
                else if (options.DryRun)
                {
                    // Log summary
                     string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
                     string srcStats = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";
                     
                     _host.LogMessage($"[ANALYSIS] Source Distribution: {srcStats}", PawsLogLvl.Information, "StableCleaner");
                     _host.LogMessage($"[DRY RUN SUMMARY] Found {mapsToRemove.Count} maps to delete.", PawsLogLvl.Information, "StableCleaner");
                     _host.LogMessage($"[DRY RUN STATS] Breakdown: {stats}", PawsLogLvl.Information, "StableCleaner");
                }


                // --- 2. Asset Cleaning (Start Indexing) ---
                var realmConfig = new RealmConfiguration(_indexDbPath) { SchemaVersion = 3 };

                // Explicitly define schema to bypass auto-discovery issues in plugins
                try
                {
                    var schemaBuilder = new RealmSchema.Builder();
                    schemaBuilder.Add(typeof(IndexedBeatmap));
                    schemaBuilder.Add(typeof(IndexedFile));
                    realmConfig.Schema = schemaBuilder.Build();
                }
                catch (Exception ex) 
                {
                    _host.LogMessage($"[Error] Failed to build Realm schema: {ex.Message}", PawsLogLvl.Error, "StableCleaner");
                }

                Realm realm;
                try 
                {
                    realm = Realm.GetInstance(realmConfig);
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"[DEBUG] First Realm Attempt Failed: {ex.Message}. Deleting DB...", PawsLogLvl.Warning, "StableCleaner");
                    // If migration needed or other schema error, delete and retry
                    try { File.Delete(_indexDbPath); } catch {}
                    try { Directory.Delete(Path.Combine(_indexDbPath, "management"), true); } catch {} // Cleanup aux folders if any
                    
                    realm = Realm.GetInstance(realmConfig);
                }
                // Ensure disposal
                using (realm)
                {
                
                _host.LogMessage($"[DEBUG] Realm Opened! Schema Count: {realm.Schema.Count}", PawsLogLvl.Information, "StableCleaner");

                var validHashes = new HashSet<string>();
                var mapsToIndex = new List<dynamic>();

                _host.LogMessage("Verifying index integrity...", PawsLogLvl.Information, "StableCleaner");

                foreach (var map in db.Beatmaps)
                {
                    string hash = map.MD5Hash;
                    validHashes.Add(hash);

                    var indexed = realm.Find<IndexedBeatmap>(hash);
                    bool needsIndex = false;

                    if (indexed == null)
                    {
                        needsIndex = true;
                    }
                    else
                    {
                        // Check if folder modified since last index
                        var mapFolder = Path.Combine(songDir, map.FolderName);
                        if (Directory.Exists(mapFolder))
                        {
                            var lastWrite = Directory.GetLastWriteTimeUtc(mapFolder);
                            // If folder is newer than our index (+ buffer), re-index
                            if (lastWrite > indexed.LastIndexedTime.AddSeconds(5)) 
                                needsIndex = true;
                        }
                    }

                    if (needsIndex) mapsToIndex.Add(map);
                }
                
                // Cleanup Realm (Remove old maps)
                realm.Write(() =>
                {
                    var allIndexed = realm.All<IndexedBeatmap>().ToList();
                    foreach (var indexed in allIndexed)
                    {
                        if (!validHashes.Contains(indexed.Hash))
                        {
                            realm.Remove(indexed);
                        }
                    }
                });

                // Indexing New/Modified Maps
                if (mapsToIndex.Count > 0)
                {
                    _host.LogMessage($"Indexing {mapsToIndex.Count} maps...", PawsLogLvl.Information, "StableCleaner");
                    IndexMaps(realm, stable, mapsToIndex, songDir); // Pass stable context
                }

                // Execute Asset Cleaning
                ExecuteAssetCleaning(realm, options, songDir, ref deletedFiles, ref freedBytes);
                } // End using realm
            });

            string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
            string srcStats = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";

            string msg = options.DryRun 
                ? $"[DRY RUN] Found {deletedMaps} maps ({stats}) to delete. (Source: {srcStats})"
                : $"Cleanup Complete. Deleted {deletedMaps} maps ({stats}), {deletedFiles} files. (Source: {srcStats})";

            return new 
            { 
                Success = true, 
                Message = msg + (options.DryRun ? "" : $" Freed {freedBytes / 1024 / 1024} MB.") 
            };
        }

        private void ExecuteAssetCleaning(Realm realm, CleanerOptions options, string songDir, ref int deletedFiles, ref long freedBytes)
        {
            var assets = options.Assets;
            if (assets == null) return;

            try
            {
                var allIndexed = realm.All<IndexedBeatmap>().ToList();
                int itemsProcessed = 0;

                // Log Asset Options
                _host.LogMessage($"Asset Cleaning Options: Skins={assets.Skins}, Sounds={assets.Sounds}, Videos={assets.Videos}, SB={assets.Storyboards}, BG={assets.Background}, Nuke={assets.Skins == true && assets.Sounds == true && assets.Videos == true && assets.Storyboards == true}", PawsLogLvl.Information, "StableCleaner");

                if (options.DryRun)
                {
                     _host.LogMessage("--- DRY RUN STARTED ---", PawsLogLvl.Warning, "StableCleaner");
                }

                foreach (var map in allIndexed)
                {
                    var mapFolder = Path.Combine(songDir, map.FolderPath);
                    if (!Directory.Exists(mapFolder)) continue;

                    bool nukeMode = assets.Skins && assets.Sounds && assets.Videos && assets.Storyboards;
                    bool deleteBg = assets.Background?.ToLower() == "delete";

                    foreach (var file in map.Files)
                    {
                        bool shouldDelete = false;
                        string debugReason = "";

                        if (nukeMode)
                        {
                            // Keep Scripts, Backgrounds (unless delete requested), Audios
                            bool isScript = (file.UsageType & 16) != 0;
                            bool isBg = (file.UsageType & 1) != 0;
                            bool isAudio = (file.UsageType & 2) != 0;
                            
                            // Essential files checks
                            // Script and Audio are always essential. BG is essential unless deleteBg is true.
                            bool isEssential = isScript || isAudio || (isBg && !deleteBg);

                            if (!isEssential) 
                            {
                                shouldDelete = true;
                                debugReason = "Nuke Mode: Asset";
                            }
                            else if (options.DryRun)
                            {
                                // Log why we kept it
                                string reason = "";
                                if (isScript) reason += "Script ";
                                if (isBg) reason += deleteBg ? "BG (Deletable) " : "BG (Kept) ";
                                if (isAudio) reason += "Audio ";
                                // Logic check: if isEssential is true, we are here.
                                // If isBg && deleteBg -> isEssential is FALSE. So we are NOT here. We are in 'if (!isEssential)'.
                                // So here isBg implies !deleteBg.
                                _host.LogMessage($"[DryRun] KEEP: {file.Filename} ({reason.Trim()})", PawsLogLvl.Information, "StableCleaner");
                            }
                        }
                        else
                        {
                            // Granular Logic
                            bool isVideo = (file.UsageType & 4) != 0;
                            bool isSb = (file.UsageType & 8) != 0;
                            bool isAudio = (file.UsageType & 2) != 0; // Hitsounds might be here if custom
                            
                            // Storyboards
                            if (assets.Storyboards && isSb)
                            {
                                shouldDelete = true;
                                debugReason = "Storyboard";
                            }

                            // Videos
                            if (assets.Videos && isVideo)
                            {
                                shouldDelete = true;
                                debugReason = "Video";
                            }

                            // Skins (Images)
                            // Delete if it is skinnable AND not a background/SB (unless SB is also checked)
                            // Note: Usagetype 1=BG. 
                            if (assets.Skins && file.IsSkinnable && (file.UsageType & 1) == 0 && (file.Extension == ".png" || file.Extension == ".jpg"))
                            {
                                // Safety: Don't delete if it's used as BG (already checked bitmask 1)
                                // If it is used as SB (bitmask 8), only delete if Storyboards are ALSO checked? 
                                // Or does "Clean Skins" imply removing skinnable elements even if used in SB?
                                // Usually SB elements are skinnable. 
                                // Let's simplify: If it's a skin element, and not a BG, delete it.
                                shouldDelete = true;
                                debugReason = "Skin Element";
                            }

                            // Sounds (Skin sounds / Hitsounds)
                            if (assets.Sounds && file.IsSkinnable && (file.Extension == ".wav" || file.Extension == ".mp3" || file.Extension == ".ogg"))
                            {
                                // Don't delete if it's the MAIN audio (Usage 2)
                                if ((file.UsageType & 2) == 0)
                                {
                                    shouldDelete = true;
                                    debugReason = "Sound Element";
                                }
                            }
                        }

                        if (shouldDelete)
                        {
                            var fullPath = Path.Combine(mapFolder, file.Filename);
                            if (File.Exists(fullPath))
                            {
                                if (options.DryRun)
                                {
                                    _host.LogMessage($"[DryRun] Would delete: {file.Filename} ({debugReason}) in {map.FolderPath}", PawsLogLvl.Information, "StableCleaner");
                                }
                                else
                                {
                                    try
                                    {
                                        var fi = new FileInfo(fullPath);
                                        freedBytes += fi.Length;
                                        File.Delete(fullPath);
                                        deletedFiles++;
                                    }
                                    catch {}
                                }
                            }
                        }
                    }
                    itemsProcessed++;
                }
                
                if (options.DryRun)
                {
                     _host.LogMessage("--- DRY RUN FINISHED ---", PawsLogLvl.Warning, "StableCleaner");
                }
            }
            catch (Exception ex)
            {
                 _host.LogMessage($"Error in asset cleaning: {ex.Message}", PawsLogLvl.Error, "StableCleaner");
            }
        }

        private void IndexMaps(Realm realm, StableContext stable, List<dynamic> maps, string songDir)
        {
            int i = 0;
            foreach (var map in maps)
            {
                string folderPath = Path.Combine(songDir, map.FolderName);
                if (!Directory.Exists(folderPath)) continue;

                var allFiles = Directory.GetFiles(folderPath);
                var assetsUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); 

                // scan all .osu and .osb files
                foreach (var file in allFiles)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    string fname = Path.GetFileName(file);
                    
                    if (ext == ".osu")
                    {
                        try
                        {
                            MarkUsage(assetsUsage, fname, 16); // Script

                            // Use Core Wrapper
                            var beatmap = stable.ParseBeatmap(file);
                            
                            // 1. Audio
                            if (!string.IsNullOrEmpty(beatmap.AudioFilename))
                            {
                                MarkUsage(assetsUsage, beatmap.AudioFilename, 2); 
                                // _host.LogMessage($"[Index] Marked Audio: {beatmap.AudioFilename} for {fname}", PawsLogLvl.Information, "StableCleaner");
                            }

                            // 2. Background / Video
                            if (!string.IsNullOrEmpty(beatmap.BackgroundImage))
                                MarkUsage(assetsUsage, beatmap.BackgroundImage, 1); 

                            if (!string.IsNullOrEmpty(beatmap.Video))
                                MarkUsage(assetsUsage, beatmap.Video, 4); 

                            // 3. Storyboard (embedded)
                            if (beatmap.EventsStoryboard != null)
                            {
                                foreach (var sbFile in beatmap.EventsStoryboard.GetAllReferencedFiles())
                                {
                                     MarkUsage(assetsUsage, sbFile, 8);
                                }
                            }
                            
                            // 4. Hitsounds (Optional, but if we wanted to track them explicitly as "Audio")
                            foreach (var sample in beatmap.GetHitSoundSamples())
                            {
                                MarkUsage(assetsUsage, sample, 2); // Treat custom samples as Audio (essential)
                            }
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"[Index] Failed to parse {fname}: {ex.Message}", PawsLogLvl.Error, "StableCleaner");
                        }
                    }
                    else if (ext == ".osb")
                    {
                        try
                        {
                            MarkUsage(assetsUsage, fname, 16); // Script
                            
                            // Use Core Wrapper
                            var sb = stable.ParseStoryboard(file);
                            foreach (var sbFile in sb.GetAllReferencedFiles())
                            {
                                MarkUsage(assetsUsage, sbFile, 8);
                            }
                        }
                        catch {}
                    }
                }

                realm.Write(() =>
                {
                    var existing = realm.Find<IndexedBeatmap>(map.MD5Hash);
                    if (existing != null) realm.Remove(existing);

                    var indexedMap = new IndexedBeatmap 
                    { 
                        Hash = map.MD5Hash, 
                        FolderPath = map.FolderName,
                        LastIndexedTime = DateTimeOffset.UtcNow
                    };

                    foreach (var filePath in allFiles)
                    {
                        string fileName = Path.GetFileName(filePath);
                        string ext = Path.GetExtension(fileName).ToLowerInvariant();
                        
                        int usage = 0;
                        if (assetsUsage.TryGetValue(fileName, out var u)) usage = u;

                        bool isSkinnable = StableKnownFiles.IsSkinnable(fileName);

                        indexedMap.Files.Add(new IndexedFile 
                        {
                            Filename = fileName,
                            Extension = ext,
                            UsageType = usage,
                            IsSkinnable = isSkinnable
                        });
                    }
                    realm.Add(indexedMap);
                });
                
                i++;
                if (i % 50 == 0) _host.LogMessage($"Indexed {i}/{maps.Count}...", PawsLogLvl.Information, "StableCleaner");
            }
        }

        private void MarkUsage(Dictionary<string, int> dict, string filename, int mask)
        {
            string cleanName = filename.Replace("\"", "").Trim(); // Cleanup quotes
            if (string.IsNullOrEmpty(cleanName)) return;
            
            if (dict.ContainsKey(cleanName))
                dict[cleanName] |= mask;
            else
                dict[cleanName] = mask;
        }
    }
}
