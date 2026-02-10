using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Interfaces.Services;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Realms;
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var pluginData = Path.Combine(appData, "PawsCleaner");
            Directory.CreateDirectory(pluginData);
            _indexDbPath = Path.Combine(pluginData, "stable_index.realm");
            _indexer = new StableIndexer(host, Name);
            _assetCleaner = new StableAssetCleaner(host, Name);
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
                string songDir = "";
                try { songDir = ((dynamic)stable).GetSongsPath(); }
                catch { songDir = Path.Combine(stablePath, "Songs"); } // Fallback

                _host.LogMessage($"[CONFIG] Mode: {options.Mode}, DryRun: {options.DryRun}", PawsLogLvl.Information, Name);
                _host.LogMessage($"[CONFIG] Delete Rulesets? Osu: {options.Rulesets?.Osu ?? false}, Taiko: {options.Rulesets?.Taiko ?? false}, Catch: {options.Rulesets?.Catch ?? false}, Mania: {options.Rulesets?.Mania ?? false}", PawsLogLvl.Information, Name);
                _host.LogMessage("Reading osu!.db...", PawsLogLvl.Information, Name);

                var db = stable.ReadOsuDatabase(dbPath);

                // --- 1. Ruleset Cleaning ---
                var mapsToRemove = new List<dynamic>();
                var dbBeatmaps = db.Beatmaps.ToList();
                _host.LogMessage($"[ANALYSIS] Found {dbBeatmaps.Count} total beatmaps in osu!.db.", PawsLogLvl.Information, Name);

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

                        if (options.DryRun)
                        {
                            deletedMaps++;
                            mapsToRemove.Add(map);
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
                    _host.LogMessage($"Removing {mapsToRemove.Count} maps from DB...", PawsLogLvl.Information, Name);
                    foreach (var m in mapsToRemove) db.RemoveBeatmap(m);
                    stable.WriteOsuDatabase(db, dbPath);
                }
                else if (options.DryRun)
                {
                    string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
                    string srcStats = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";

                    _host.LogMessage($"[ANALYSIS] Source Distribution: {srcStats}", PawsLogLvl.Information, Name);
                    _host.LogMessage($"[DRY RUN SUMMARY] Found {mapsToRemove.Count} maps to delete.", PawsLogLvl.Information, Name);
                    _host.LogMessage($"[DRY RUN STATS] Breakdown: {stats}", PawsLogLvl.Information, Name);
                }


                // --- 2. Asset Cleaning ---
                var realmConfig = new RealmConfiguration(_indexDbPath) { SchemaVersion = 4 };

                try
                {
                    var schemaBuilder = new RealmSchema.Builder();
                    schemaBuilder.Add(typeof(IndexedBeatmap));
                    schemaBuilder.Add(typeof(IndexedFile));
                    realmConfig.Schema = schemaBuilder.Build();
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"[Error] Failed to build Realm schema: {ex.Message}", PawsLogLvl.Error, Name);
                }

                Realm realm;
                try
                {
                    realm = Realm.GetInstance(realmConfig);
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"Realm Open Failed: {ex.Message}. Recreating Index...", PawsLogLvl.Warning, Name);
                    try { File.Delete(_indexDbPath); } catch { }
                    try { Directory.Delete(Path.Combine(_indexDbPath, "management"), true); } catch { }

                    realm = Realm.GetInstance(realmConfig);
                }

                using (realm)
                {
                    _host.LogMessage($"Realm Opened! Schema Count: {realm.Schema.Count}", PawsLogLvl.Information, Name);

                    var validHashes = new HashSet<string>();
                    var mapsToIndex = new List<dynamic>();

                    _host.LogMessage("Verifying index integrity...", PawsLogLvl.Information, Name);

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
                            var mapFolder = Path.Combine(songDir, map.FolderName);
                            if (Directory.Exists(mapFolder))
                            {
                                var lastWrite = Directory.GetLastWriteTimeUtc(mapFolder);

                                // Check .osu file timestamp too, as folder time doesn't change on file content edit
                                var osuPath = Path.Combine(mapFolder, map.FileName);
                                if (File.Exists(osuPath))
                                {
                                    var osuWrite = File.GetLastWriteTimeUtc(osuPath);
                                    if (osuWrite > lastWrite) lastWrite = osuWrite;
                                }

                                if (lastWrite > indexed.LastIndexedTime.AddSeconds(5))
                                    needsIndex = true;
                            }
                        }

                        if (needsIndex) mapsToIndex.Add(map);
                    }

                    // Cleanup Realm
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

                    // Indexing
                    if (mapsToIndex.Count > 0)
                    {
                        _host.LogMessage($"Indexing {mapsToIndex.Count} maps...", PawsLogLvl.Information, Name);
                        _indexer.IndexMaps(realm, stable, mapsToIndex, songDir);
                    }

                    // Execute Cleaning
                    _assetCleaner.ExecuteAssetCleaning(realm, options, songDir, ref deletedFiles, ref freedBytes);
                }
            });

            string statsStr = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
            string srcStatsStr = $"Osu: {srcOsu}, Taiko: {srcTaiko}, Catch: {srcCatch}, Mania: {srcMania}, Other: {srcOther}";

            string msg = options.DryRun
                ? $"[DRY RUN] Found {deletedMaps} maps ({statsStr}) to delete. (Source: {srcStatsStr})"
                : $"Cleanup Complete. Deleted {deletedMaps} maps ({statsStr}), {deletedFiles} files. (Source: {srcStatsStr})";

            return new
            {
                Success = true,
                Message = msg + (options.DryRun ? "" : $" Freed {freedBytes / 1024 / 1024} MB.")
            };
        }

    }
}
