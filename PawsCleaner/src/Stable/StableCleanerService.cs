using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Paws.Core.Abstractions;
using Realms;

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
            int deletedMaps = 0;
            int deletedFiles = 0;
            long freedBytes = 0;

            await _host.PerformStableWriteAsync(stablePath =>
            {
                var stable = _host.GetStableContext();
                var dbPath = Path.Combine(stablePath, "osu!.db");
                var songDir = Path.Combine(stablePath, "Songs");

                _host.LogMessage("Reading osu!.db...", PawsLogLvl.Information, "StableCleaner");
                var db = stable.ReadOsuDatabase(dbPath);

                // --- 1. Ruleset Cleaning ---
                var mapsToRemove = new List<dynamic>(); // DbBeatmap
                var dbBeatmaps = db.Beatmaps.ToList(); // Materialize

                foreach (var map in dbBeatmaps)
                {
                    // Map Ruleset enum: 0=Std, 1=Taiko, 2=Catch, 3=Mania
                    // Warning: OsuParsers might use different Enum values/types.
                    // Assuming standard cast works or wrapper handles it.
                    // Ruleset is int or Enum. Convert to string for easy switch.
                    int rId = (int)map.Ruleset;
                    bool keep = true;

                    switch (rId)
                    {
                        case 0: keep = options.Rulesets?.Osu ?? true; break;
                        case 1: keep = options.Rulesets?.Taiko ?? true; break;
                        case 2: keep = options.Rulesets?.Catch ?? true; break;
                        case 3: keep = options.Rulesets?.Mania ?? true; break;
                    }

                    if (!keep)
                    {
                        mapsToRemove.Add(map);
                        deletedMaps++;
                        
                        // Delete .osu file
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

                if (mapsToRemove.Count > 0)
                {
                    _host.LogMessage($"Removing {mapsToRemove.Count} maps from DB...", PawsLogLvl.Information, "StableCleaner");
                    // Use the wrapper method as per documentation
                    foreach (var m in mapsToRemove) db.RemoveBeatmap(m); 
                    stable.WriteOsuDatabase(db, dbPath);
                }

                // --- 2. Asset Cleaning (Stateful) ---
                // We open our local Realm
                var realmConfig = new RealmConfiguration(_indexDbPath) { SchemaVersion = 1 };
                using var realm = Realm.GetInstance(realmConfig);

                // Sync Logic: Check which maps need indexing
                // We assume db.Beatmaps is now UPDATED (removed maps are gone).
                // But wait, WriteOsuDatabase was called. We should use the current list.
                // Actually `db.Beatmaps` is a reference to the list in memory.
                
                var validHashes = new HashSet<string>();
                var mapsToIndex = new List<dynamic>();

                foreach (var map in db.Beatmaps)
                {
                    string hash = map.MD5Hash;
                    validHashes.Add(hash);
                    
                    if (realm.Find<IndexedBeatmap>(hash) == null)
                    {
                        mapsToIndex.Add(map);
                    }
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

                // Indexing New Maps
                if (mapsToIndex.Count > 0)
                {
                    _host.LogMessage($"Indexing {mapsToIndex.Count} new maps...", PawsLogLvl.Information, "StableCleaner");
                    int i = 0;
                    foreach (var map in mapsToIndex)
                    {
                        string folderPath = Path.Combine(songDir, map.FolderName);
                        if (!Directory.Exists(folderPath)) continue;

                        try 
                        {
                            // 1. Get Used Assets (via Host Helper)
                            var usedAssets = stable.GetUsedAssets(folderPath); // HashSet<string>

                            // 2. Scan Directory
                            var allFiles = Directory.GetFiles(folderPath);

                            realm.Write(() =>
                            {
                                var indexedMap = new IndexedBeatmap 
                                { 
                                    Hash = map.MD5Hash, 
                                    FolderPath = map.FolderName 
                                };

                                foreach (var filePath in allFiles)
                                {
                                    string fileName = Path.GetFileName(filePath);
                                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                                    bool isUsed = usedAssets.Contains(fileName.ToLowerInvariant());

                                    // Special Case: .osu files are always "Used" (implicitly)
                                    // But Cleaner might have deleted them? No, we filter based on current DB.
                                    if (ext == ".osu") isUsed = true;
                                    if (ext == ".osb") isUsed = true; // Storyboards are used by definition in structure, cleaner logic decides if we keep them.

                                    indexedMap.Files.Add(new IndexedFile 
                                    {
                                        Filename = fileName,
                                        Extension = ext,
                                        IsUsed = isUsed
                                    });
                                }
                                realm.Add(indexedMap);
                            });
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"Failed to index {map.FolderName}: {ex.Message}", PawsLogLvl.Warning, "StableCleaner");
                        }
                        
                        i++;
                        if (i % 50 == 0) _host.LogMessage($"Indexed {i}/{mapsToIndex.Count}...", PawsLogLvl.Information, "StableCleaner");
                    }
                }

                // Execute Asset Cleaning
                if (options.Assets != null)
                {
                    _host.LogMessage("Cleaning Assets...", PawsLogLvl.Information, "StableCleaner");
                    
                    var allMaps = realm.All<IndexedBeatmap>().ToList();
                    foreach (var map in allMaps)
                    {
                        var fullFolder = Path.Combine(songDir, map.FolderPath);
                        if (!Directory.Exists(fullFolder)) continue;

                        foreach (var file in map.Files)
                        {
                            bool shouldDelete = false;

                            // 1. Orphan/Skin Cleaning
                            if (options.Assets.Skins && !file.IsUsed)
                            {
                                shouldDelete = true;
                            }

                            // 2. Video Cleaning
                            if (options.Assets.Videos)
                            {
                                if (file.Extension == ".avi" || file.Extension == ".flv" || file.Extension == ".mp4" || file.Extension == ".mkv")
                                    shouldDelete = true;
                            }

                            // 3. Storyboard Cleaning
                            if (options.Assets.Storyboards)
                            {
                                if (file.Extension == ".osb") shouldDelete = true;
                                // TODO: Delete sprites that are ONLY used in storyboard? 
                                // Current model just says "IsUsed". We don't distinguish "UsedByOSB" vs "UsedByBG".
                                // For V1, we only delete .osb files. Detailed sprite cleaning requires deeper analysis.
                            }

                            if (shouldDelete)
                            {
                                var fullPath = Path.Combine(fullFolder, file.Filename);
                                if (File.Exists(fullPath))
                                {
                                    try 
                                    {
                                        var fi = new FileInfo(fullPath);
                                        freedBytes += fi.Length;
                                        File.Delete(fullPath);
                                        // Update Index? Technically we should remove from Realm, but next sync will handle it.
                                        // Or we can verify existence next time.
                                        deletedFiles++;
                                    }
                                    catch {}
                                }
                            }
                        }
                    }
                }
            });

            return new 
            { 
                Success = true, 
                Message = $"Stable Cleanup Complete. Deleted {deletedMaps} maps, {deletedFiles} files. Freed {freedBytes / 1024 / 1024} MB." 
            };
        }
    }
}
