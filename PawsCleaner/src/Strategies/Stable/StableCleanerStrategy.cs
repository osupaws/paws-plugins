using Paws.Core.Abstractions;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Realms;
using Realms.Schema;

namespace PawsCleaner.Strategies.Stable
{
    public class StableCleanerStrategy : ICleanerStrategy
    {
        private readonly IHostServices _host;
        private readonly string _indexDbPath;
        public string Name => "Stable Cleaner";

        public StableCleanerStrategy(IHostServices host)
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
                var realmConfig = new RealmConfiguration(_indexDbPath) { SchemaVersion = 3 };

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
                        IndexMaps(realm, stable, mapsToIndex, songDir);
                    }

                    // Execute Cleaning
                    ExecuteAssetCleaning(realm, options, songDir, ref deletedFiles, ref freedBytes);
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

        private void ExecuteAssetCleaning(Realm realm, CleanerOptions options, string songDir, ref int deletedFiles, ref long freedBytes)
        {
            var assets = options.Assets;
            if (assets == null) return;

            string pluginDataDir = Path.GetDirectoryName(_indexDbPath) ?? string.Empty;
            if (string.IsNullOrEmpty(pluginDataDir)) pluginDataDir = Path.GetTempPath();

            string srcJpg = Path.Combine(pluginDataDir, "paws_stable_src.jpg");
            string srcPng = Path.Combine(pluginDataDir, "paws_stable_src.png");
            string bgMode = assets.BackgroundMode?.ToLowerInvariant() ?? "keep";
            bool srcCreated = false;

            try
            {
                if (bgMode == "white" || bgMode == "custom")
                {
                    try { if (File.Exists(srcJpg)) File.Delete(srcJpg); } catch { }
                    try { if (File.Exists(srcPng)) File.Delete(srcPng); } catch { }

                    if (bgMode == "white")
                    {
                        string whiteJpgB64 = "/9j/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/wgALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/aAAgBAQAAAAB/P//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Af//Z";
                        string whitePngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=";

                        File.WriteAllBytes(srcJpg, Convert.FromBase64String(whiteJpgB64));
                        File.WriteAllBytes(srcPng, Convert.FromBase64String(whitePngB64));
                        srcCreated = true;
                    }
                    else if (bgMode == "custom")
                    {
                        if (!string.IsNullOrEmpty(assets.CustomBackgroundJpg))
                        {
                            try
                            {
                                string b64 = assets.CustomBackgroundJpg.Contains(",") ? assets.CustomBackgroundJpg.Split(',')[1] : assets.CustomBackgroundJpg;
                                File.WriteAllBytes(srcJpg, Convert.FromBase64String(b64));
                                srcCreated = true;
                            }
                            catch { }
                        }
                        if (!string.IsNullOrEmpty(assets.CustomBackgroundPng))
                        {
                            try
                            {
                                string b64 = assets.CustomBackgroundPng.Contains(",") ? assets.CustomBackgroundPng.Split(',')[1] : assets.CustomBackgroundPng;
                                File.WriteAllBytes(srcPng, Convert.FromBase64String(b64));
                                srcCreated = true;
                            }
                            catch { }
                        }
                    }
                }

                var allIndexed = realm.All<IndexedBeatmap>().ToList();
                int itemsProcessed = 0;

                bool isNuke = assets.Skins && assets.Sounds && assets.Videos && assets.Storyboards;
                _host.LogMessage($"Asset Cleaning Options: Skins={assets.Skins}, Sounds={assets.Sounds}, Videos={assets.Videos}, SB={assets.Storyboards}, BGMode={assets.BackgroundMode}, Nuke={isNuke}", PawsLogLvl.Information, Name);

                if (options.DryRun)
                {
                    _host.LogMessage("--- DRY RUN STARTED ---", PawsLogLvl.Warning, Name);
                }

                foreach (var map in allIndexed)
                {
                    var mapFolder = Path.Combine(songDir, map.FolderPath);
                    if (!Directory.Exists(mapFolder)) continue;

                    foreach (var file in map.Files)
                    {
                        bool shouldDelete = false;
                        bool isReplacement = false;
                        string replacementSource = "";

                        // Bitmask Usage: 1=BG, 2=Audio, 4=Video, 8=SB, 16=Script
                        bool isBg = (file.UsageType & 1) != 0;
                        bool isScript = (file.UsageType & 16) != 0;
                        bool isAudio = (file.UsageType & 2) != 0;
                        bool isVideo = (file.UsageType & 4) != 0;
                        bool isSb = (file.UsageType & 8) != 0;

                        // --- Background Replacement Logic ---
                        if (isBg && (bgMode == "white" || bgMode == "custom") && srcCreated)
                        {
                            string targetExt = file.Extension.ToLower();
                            replacementSource = (targetExt == ".png") ? srcPng : srcJpg;
                            if (!File.Exists(replacementSource)) replacementSource = srcJpg;

                            if (File.Exists(replacementSource))
                            {
                                shouldDelete = true;
                                isReplacement = true;
                            }
                        }

                        // --- Nuke Mode vs Granular Logic ---
                        if (isNuke)
                        {
                            if (isBg)
                            {
                                if (bgMode == "keep")
                                {
                                    shouldDelete = false;
                                    isReplacement = false;
                                }
                            }
                            else if (isScript || isAudio)
                            {
                                shouldDelete = false;
                            }
                            else
                            {
                                shouldDelete = true;
                            }
                        }
                        else if (!isBg) // Normal Mode (Non-BG items)
                        {
                            if (assets.Storyboards && isSb) shouldDelete = true;
                            if (assets.Videos && isVideo) shouldDelete = true;
                            if (assets.Skins && file.IsSkinnable && (AssetUtils.IsSkinImage(file.Extension))) shouldDelete = true;
                            if (assets.Sounds && file.IsSkinnable && (AssetUtils.IsAudio(file.Extension)))
                            {
                                if (!isAudio) shouldDelete = true;
                            }
                        }

                        if (shouldDelete)
                        {
                            var fullPath = Path.Combine(mapFolder, file.Filename);
                            if (File.Exists(fullPath))
                            {
                                if (!options.DryRun)
                                {
                                    try
                                    {
                                        var fi = new FileInfo(fullPath);
                                        freedBytes += fi.Length;
                                        File.Delete(fullPath);
                                        deletedFiles++;

                                        if (isReplacement && File.Exists(replacementSource))
                                        {
                                            try
                                            {
                                                File.CreateSymbolicLink(fullPath, replacementSource);
                                            }
                                            catch
                                            {
                                                File.Copy(replacementSource, fullPath, true);
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    itemsProcessed++;
                }

                if (options.DryRun)
                {
                    _host.LogMessage("--- DRY RUN FINISHED ---", PawsLogLvl.Warning, Name);
                }
            }
            catch (Exception ex)
            {
                _host.LogMessage($"Error in asset cleaning: {ex.Message}", PawsLogLvl.Error, Name);
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

                void Mark(string f, int m) => MarkUsage(assetsUsage, f, m);

                foreach (var file in allFiles)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    string fname = Path.GetFileName(file);

                    if (ext == ".osu")
                    {
                        try
                        {
                            Mark(fname, 16);
                            var beatmap = stable.ParseBeatmap(file);

                            if (!string.IsNullOrEmpty(beatmap.AudioFilename)) Mark(beatmap.AudioFilename, 2);
                            if (!string.IsNullOrEmpty(beatmap.BackgroundImage)) Mark(beatmap.BackgroundImage, 1);
                            if (!string.IsNullOrEmpty(beatmap.Video)) Mark(beatmap.Video, 4);

                            if (beatmap.EventsStoryboard != null)
                            {
                                foreach (var sbFile in beatmap.EventsStoryboard.GetAllReferencedFiles())
                                {
                                    Mark(sbFile, 8);
                                }
                            }

                            foreach (var sample in beatmap.GetHitSoundSamples())
                            {
                                Mark(sample, 2);
                            }
                        }
                        catch (Exception ex)
                        {
                            _host.LogMessage($"[Index] Failed to parse {fname}: {ex.Message}", PawsLogLvl.Error, Name);
                        }
                    }
                    else if (ext == ".osb")
                    {
                        try
                        {
                            Mark(fname, 16);
                            var sb = stable.ParseStoryboard(file);
                            foreach (var sbFile in sb.GetAllReferencedFiles())
                            {
                                Mark(sbFile, 8);
                            }
                        }
                        catch { }
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

                        bool isSkinnable = KnownFiles.IsSkinnable(fileName);

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
                if (i % 50 == 0) _host.LogMessage($"Indexed {i}/{maps.Count}...", PawsLogLvl.Information, Name);
            }
        }

        private void MarkUsage(Dictionary<string, int> dict, string filename, int mask)
        {
            string cleanName = filename.Replace("\"", "").Trim();
            if (string.IsNullOrEmpty(cleanName)) return;

            if (dict.TryGetValue(cleanName, out int currentMask))
                dict[cleanName] = currentMask | mask;
            else
                dict[cleanName] = mask;
        }
    }
}
