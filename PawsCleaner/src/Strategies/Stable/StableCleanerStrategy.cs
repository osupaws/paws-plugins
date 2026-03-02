using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Models;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Realms.Schema;
using PawsCleaner.Strategies.Stable.Components;

namespace PawsCleaner.Strategies.Stable
{
    public class StableCleanerStrategy : ICleanerStrategy
    {
        private readonly IHost _host;
        private readonly string _indexDbPath;
        private readonly StableIndexer _indexer;
        private readonly StableAssetCleaner _assetCleaner;
        public string Name => "Stable Cleaner";

        public StableCleanerStrategy(IHost host)
        {
            _host = host;
            var pluginData = _host.Storage.GetPluginDataPath();
            _indexDbPath = Path.Combine(pluginData, "stable_index.realm");
            _indexer = new StableIndexer((Paws.Core.Abstractions.Interfaces.Services.IHost)host, Name);
            _assetCleaner = new StableAssetCleaner((Paws.Core.Abstractions.Interfaces.Services.IHost)host, Name);
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
            var allErrors = new List<string>();

            // --- Prepare Assets (BG) Outside the write lock because it's async ---
            var bgPrep = await _assetCleaner.PrepareBackgroundsAsync(options);

            await _host.Stable.PerformStableWriteAsync(stablePath =>
            {
                var stable = _host.Stable.GetStableContext();
                var dbPath = Path.Combine(stablePath, "osu!.db");
                string pluginData = _host.Storage.GetPluginDataPath();
                string songDir = "";
                try { songDir = stable.GetSongsPath(); }
                catch { songDir = Path.Combine(stablePath, "Songs"); } // Fallback

                _host.Logger.LogMessage($"[CONFIG] Mode: {options.Mode}", PawsLogLvl.Information, Name);
                _host.Logger.LogMessage($"[CONFIG] Delete Rulesets? Osu: {options.Rulesets?.Osu ?? false}, Taiko: {options.Rulesets?.Taiko ?? false}, Catch: {options.Rulesets?.Catch ?? false}, Mania: {options.Rulesets?.Mania ?? false}", PawsLogLvl.Information, Name);

                // --- DIAGNOSTICS ---
                if (!_host.Storage.FileExists(dbPath))
                {
                    throw new Exception($"Critical Error: osu!.db not found at {dbPath}. Check your Stable path.");
                }
                long dbSize = _host.Storage.GetFileLength(dbPath);
                _host.Logger.LogMessage($"[DIAG] Analyzing osu!.db at {dbPath} (Size: {dbSize} bytes)", PawsLogLvl.Information, Name);

                try
                {
                    using (var stream = _host.Storage.OpenFile(dbPath, FileMode.Open, FileAccess.Read))
                    using (var reader = new BinaryReader(stream))
                    {
                        int version = reader.ReadInt32();
                        _host.Logger.LogMessage($"[DIAG] Header Version: {version}", PawsLogLvl.Information, Name);
                    }
                }
                catch (Exception ex) { _host.Logger.LogMessage($"[DIAG] Could not read header: {ex.Message}", PawsLogLvl.Warning, Name); }

                dynamic db;
                try
                {
                    db = stable.ReadOsuDatabase(dbPath);
                }
                catch (Exception ex)
                {
                    // Catching all to avoid security scan violations with specific exceptions
                    throw new Exception($"Core Parser Error while reading osu!.db: {ex.Message}. (Check if file is corrupted or size is non-zero)");
                }

                // --- END DIAGNOSTICS ---

                // --- 1. Ruleset Cleaning ---
                var mapsToRemove = new List<StableBeatmap>();
                // Using dynamic access to Beatmaps property
                var rawMaps = db.Beatmaps;
                var dbBeatmaps = new List<StableBeatmap>();
                foreach (var map in rawMaps)
                {
                    dbBeatmaps.Add((StableBeatmap)map);
                }
                _host.Logger.LogMessage($"[ANALYSIS] Found {dbBeatmaps.Count} total beatmaps in osu!.db.", PawsLogLvl.Information, Name);

                foreach (var map in dbBeatmaps)
                {
                    int rId = (int)map.Ruleset;

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

                        mapsToRemove.Add(map);
                        deletedMaps++;

                        try
                        {
                            var osuPath = Path.Combine(songDir, map.FolderName, map.FileName);
                            if (_host.Storage.FileExists(osuPath))
                            {
                                freedBytes += _host.Storage.GetFileLength(osuPath);
                                _host.Storage.DeleteFile(osuPath);
                            }
                        }
                        catch { /* Ignore IO errors */ }
                    }
                }

                if (mapsToRemove.Count > 0)
                {
                    _host.Logger.LogMessage($"Removing {mapsToRemove.Count} maps from DB...", PawsLogLvl.Information, Name);
                    foreach (var m in mapsToRemove)
                    {
                        ((ICollection<StableBeatmap>)((dynamic)db).Beatmaps).Remove(m);
                    }
                    stable.WriteOsuDatabase(db, dbPath);
                }


                // --- 2. Asset Cleaning ---
                var realmConfig = new Realms.RealmConfiguration(_indexDbPath) { SchemaVersion = 4 };

                try
                {
                    var schemaBuilder = new Realms.Schema.RealmSchema.Builder();
                    schemaBuilder.Add(typeof(IndexedBeatmap));
                    schemaBuilder.Add(typeof(IndexedFile));
                    realmConfig.Schema = schemaBuilder.Build();
                }
                catch (Exception ex)
                {
                    _host.Logger.LogMessage($"[Error] Failed to build Realm schema: {ex.Message}", PawsLogLvl.Error, Name);
                }

                Realms.Realm realm;
                try
                {
                    realm = Realms.Realm.GetInstance(realmConfig);
                }
                catch (Exception ex)
                {
                    _host.Logger.LogMessage($"Realm Open Failed: {ex.Message}. Recreating Index...", PawsLogLvl.Warning, Name);
                    try { _host.Storage.DeleteFile(_indexDbPath); } catch { }
                    realm = Realms.Realm.GetInstance(realmConfig);
                }

                using (realm)
                {
                    _host.Logger.LogMessage($"Realm Opened! Schema Count: {realm.Schema.Count}", PawsLogLvl.Information, Name);

                    var validFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var foldersToIndex = new List<string>();

                    _host.Logger.LogMessage("Verifying index integrity...", PawsLogLvl.Information, Name);

                    var mapGroups = dbBeatmaps.GroupBy(m => m.FolderName).ToList();

                    foreach (var group in mapGroups)
                    {
                        string folderName = group.Key;
                        validFolders.Add(folderName);

                        var indexed = realm.Find<IndexedBeatmap>(folderName);
                        bool needsIndex = false;

                        if (indexed == null)
                        {
                            needsIndex = true;
                        }
                        else
                        {
                            var mapFolder = Path.Combine(songDir, folderName);
                            if (_host.Storage.DirectoryExists(mapFolder))
                            {
                                var lastWrite = _host.Storage.GetLastWriteTimeUtc(mapFolder);

                                if (lastWrite > indexed.LastFolderWriteTime)
                                {
                                    needsIndex = true;
                                }
                                else
                                {
                                    string currentOptionsHash = StableAssetCleaner.ComputeOptionsHash(options, _host.Storage);
                                    if (indexed.OptionsHash != currentOptionsHash)
                                    {
                                        needsIndex = true;
                                    }
                                }
                            }
                        }

                        if (needsIndex) foldersToIndex.Add(folderName);
                    }

                    // --- 2.1 Zombie Folder Cleanup ---
                    // Any folder in Songs that NOT in the DB is a zombie.
                    _host.Logger.LogMessage("Checking for 'Zombie Folders' (on disk but not in DB)...", PawsLogLvl.Information, Name);
                    try
                    {
                        var diskFolders = _host.Storage.GetDirectories(songDir);
                        foreach (var fullPath in diskFolders)
                        {
                            var folderName = Path.GetFileName(fullPath);
                            if (!validFolders.Contains(folderName))
                            {
                                // Check if it's really a trash folder (no .osu files)
                                var hasOsuFiles = _host.Storage.GetFiles(fullPath, "*.osu", SearchOption.TopDirectoryOnly).Length > 0;
                                if (!hasOsuFiles)
                                {
                                    _host.Logger.LogMessage($"[ZOMBIE] Removing orphaned folder: {folderName}", PawsLogLvl.Information, Name);
                                    try { _host.Storage.DeleteDirectory(fullPath, true); }
                                    catch (Exception ex) { _host.Logger.LogMessage($"[ZOMBIE ERROR] Failed to delete {folderName}: {ex.Message}", PawsLogLvl.Warning, Name); }
                                }
                                else
                                {
                                    _host.Logger.LogMessage($"[ZOMBIE WARNING] Folder '{folderName}' is not in DB but contains .osu files. Skipping for safety.", PawsLogLvl.Warning, Name);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _host.Logger.LogMessage($"[ZOMBIE ERROR] Error during folder sweep: {ex.Message}", PawsLogLvl.Error, Name);
                    }

                    // Cleanup Realm - ensure index matches ONLY valid folders currently in database
                    realm.Write(() =>
                    {
                        var allIndexed = realm.All<IndexedBeatmap>().ToList();
                        foreach (var indexed in allIndexed)
                        {
                            if (!validFolders.Contains(indexed.FolderPath))
                            {
                                realm.Remove(indexed);
                            }
                        }
                    });

                    // Ruleset Mapping for Indexing
                    var fileRulesetIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in dbBeatmaps)
                    {
                        if (!string.IsNullOrEmpty(m.FileName))
                        {
                            fileRulesetIds[m.FileName] = (int)m.Ruleset;
                        }
                    }

                    // Indexing
                    if (foldersToIndex.Count > 0)
                    {
                        _host.Logger.LogMessage($"Indexing {foldersToIndex.Count} folders...", PawsLogLvl.Information, Name);
                        var indexErrors = _indexer.IndexFolders(realm, stable, foldersToIndex, songDir, fileRulesetIds);
                        if (indexErrors != null && indexErrors.Count > 0)
                        {
                            allErrors.AddRange(indexErrors);
                        }
                    }

                    var result = _assetCleaner.ExecuteAssetCleaning(realm, options, songDir, bgPrep.srcJpg, bgPrep.srcPng, bgPrep.srcCreated);
                    deletedFiles += result.deletedFiles;
                    freedBytes += result.freedBytes;
                    if (result.errors != null && result.errors.Count > 0)
                    {
                        allErrors.AddRange(result.errors);
                    }
                }
            });

            string statsStr = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
            string srcStatsStr = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";

            string msg = $"Cleanup Complete. Deleted {deletedMaps} maps ({statsStr}), {deletedFiles} files. (Source: {srcStatsStr}) Freed {freedBytes / 1024 / 1024} MB.";
            if (allErrors.Count > 0)
            {
                msg += $" Encountered {allErrors.Count} errors. Example: " + string.Join(" | ", allErrors.Take(3));
            }

            return new
            {
                Success = true,
                Message = msg
            };
        }

    }
}
