using Paws.Core.Abstractions;
using PawsCleaner.Strategies.Lazer.Components;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Models;
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
        private readonly IHost _host;
        private readonly LazerAssetCleaner _assetCleaner;
        public string Name => "Lazer Cleaner";

        private const string CACHE_FILENAME = "lazer_cache.realm";

        public LazerCleanerStrategy(IHost host)
        {
            _host = host;
            _assetCleaner = new LazerAssetCleaner(host, Name);
        }

        public async Task<object> CleanAsync(CleanerOptions options)
        {
            if (_host == null) return new { Success = false, Message = "Host not initialized." };

            return await Task.Run(async () =>
            {
                var context = _host.Lazer.GetLazerContext();
                if (context == null) return new { Success = false, Message = "Failed to access ILazerContext (Core V3)." };

                _host.Logger.LogMessage("Starting Lazer cleanup (Core V3)...", PawsLogLvl.Information, Name);
                _host.Logger.LogMessage($"[CONFIG] Mode: {options.Mode}", PawsLogLvl.Information, Name);

                // --- CACHE SETUP ---
                string appData = _host.Storage.GetPluginDataPath();
                string cachePath = Path.Combine(appData, CACHE_FILENAME);

                // Initialize Realm Cache
                // Using Schema explicitly
                var realmConfig = new RealmConfiguration(cachePath)
                {
                    SchemaVersion = 1,
                    Schema = new[] { typeof(CachedLazerSet) }
                };


                string currentOptionsHash = CachedLazerSet.ComputeOptionsHash(options);
                int currentFeaturesMask = CachedLazerSet.ComputeFeaturesMask(options);
                int skippedByCache = 0;

                int setsProcessed = 0;
                int mapsDeleted = 0;

                // Detailed Stats
                int delOsu = 0, delTaiko = 0, delCatch = 0, delMania = 0, delOther = 0;
                int srcOsu = 0, srcTaiko = 0, srcCatch = 0, srcMania = 0, srcOther = 0;

                try
                {
                    // 1. Get DTOs (Safe, Detached)
                    var sets = context.GetAllBeatmapSets();

                    var setsToDelete = new List<string>();
                    var mapsToDelete = new List<string>();

                    var setsToProcess = new List<dynamic>(); // Using dynamic to hold references

                    // --- FILTERING STEP ---
                    foreach (var set in sets)
                    {
                        if (set.DeletePending)
                        {
                            skippedByCache++; // Count as skipped to avoid "Processing 0 sets" confusion if many are pending
                            continue;
                        }


                        // Scoped Realm Access for Filtering
                        bool cacheHit = false;
                        try
                        {
                            using var filterRealm = Realm.GetInstance(realmConfig);
                            string setIdStr = set.Id.ToString();
                            string? setHash = set.Hash;

                            if (!string.IsNullOrEmpty(setHash))
                            {
                                var cached = filterRealm.Find<CachedLazerSet>(setIdStr);
                                if (cached != null && cached.SetHash == setHash)
                                {
                                    bool featuresCovered = (currentFeaturesMask & ~cached.AppliedFeaturesMask) == 0;
                                    if (featuresCovered)
                                    {
                                        bool bgRequested = options.Assets?.BackgroundMode?.ToLowerInvariant() != "keep";
                                        if (!bgRequested || cached.OptionsHash == currentOptionsHash)
                                        {
                                            skippedByCache++;
                                            cacheHit = true;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // If realm fails, process anyway
                            _host.Logger.LogMessage($"[CACHE] Error accessing cache for set {set.Id}: {ex.Message}", PawsLogLvl.Warning, Name);
                        }

                        if (!cacheHit) setsToProcess.Add(set);
                    }

                    if (skippedByCache > 0)
                        _host.Logger.LogMessage($"[CACHE] Skipped {skippedByCache} clean beatmap sets.", PawsLogLvl.Information, Name);


                    // --- PREPARE ASSETS (BG Replacement) ---
                    // Using Component
                    // Using Component
                    (string? importedJpg, string? importedPng, bool bgImported) = await _assetCleaner.PrepareBackgroundsAsync(context, options);

                    // --- PROCESSING LOOP ---
                    _host.Logger.LogMessage($"Processing {setsToProcess.Count} sets...", PawsLogLvl.Information, Name);

                    var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var bgFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var set in setsToProcess)
                    {
                        if (set.Files == null || set.Files.Count == 0) continue;

                        int mapsInSetToDelete = 0;

                        try
                        {
                            // A. Ruleset Logic
                            foreach (var map in set.Beatmaps)
                            {
                                int rid = map.RulesetID;
                                // Statistics
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
                                    mapsToDelete.Add(map.Id);
                                    mapsDeleted++;
                                    mapsInSetToDelete++;

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
                                setsToDelete.Add(set.Id);
                                continue;
                            }

                            // B. Asset Cleaning via Component
                            // B.1 Populate protected lists from metadata
                            protectedFiles.Clear();
                            bgFiles.Clear();
                            foreach (var map in set.Beatmaps)
                            {
                                if (map.Metadata == null) continue;
                                string? audio = map.Metadata?.AudioFile;
                                if (!string.IsNullOrEmpty(audio)) protectedFiles.Add(audio);
                                string? bg = map.Metadata?.BackgroundFile;
                                if (!string.IsNullOrEmpty(bg))
                                {
                                    bgFiles.Add(bg);
                                    if (!bgImported) protectedFiles.Add(bg);
                                }
                            }

                            int assetRemovals = _assetCleaner.Execute(context, set, options, importedJpg, importedPng, bgFiles, protectedFiles, bgImported);
                            if (assetRemovals > 0)
                            {
                                setsProcessed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _host.Logger.LogMessage($"[Lazer] Error {ex.Message}", PawsLogLvl.Error, Name);
                        }

                        // --- UPDATE CACHE ---
                        // Only if not deleted
                        if (!setsToDelete.Contains(set.Id))
                        {
                            // Retrieve updated hash
                            string setIdStr = set.Id.ToString();
                            string? newHash = set.Hash;

                            if (!string.IsNullOrEmpty(newHash))
                            {
                                try
                                {
                                    using var updateRealm = Realm.GetInstance(realmConfig);
                                    updateRealm.Write(() =>
                                    {
                                        var existing = updateRealm.Find<CachedLazerSet>(setIdStr);
                                        int mergedMask = currentFeaturesMask;

                                        // If same version, accumulate features
                                        if (existing != null && existing.SetHash == newHash)
                                        {
                                            mergedMask |= existing.AppliedFeaturesMask;
                                        }

                                        updateRealm.Add(new CachedLazerSet
                                        {
                                            SetId = setIdStr,
                                            SetHash = newHash,
                                            AppliedFeaturesMask = mergedMask,
                                            OptionsHash = currentOptionsHash,
                                            LastCleanTime = DateTimeOffset.UtcNow
                                        }, update: true);
                                    });
                                }
                                catch (Exception ex)
                                {
                                    _host.Logger.LogMessage($"[CACHE] Error updating cache for set {set.Id}: {ex.Message}", PawsLogLvl.Warning, Name);
                                }
                            }
                        }
                    }

                    // --- EXECUTE MASS DELETIONS ---
                    if (mapsToDelete.Count > 0)
                    {
                        context.DeleteBeatmaps(mapsToDelete);
                    }

                    if (setsToDelete.Count > 0)
                    {
                        context.DeleteBeatmapSets(setsToDelete);
                        // Clean cache for deleted sets
                        try
                        {
                            using var deleteRealm = Realm.GetInstance(realmConfig);
                            deleteRealm.Write(() =>
                            {
                                foreach (var sid in setsToDelete)
                                {
                                    var obj = deleteRealm.Find<CachedLazerSet>(sid.ToString());
                                    if (obj != null) deleteRealm.Remove(obj);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            _host.Logger.LogMessage($"[CACHE] Error cleaning deletion cache: {ex.Message}", PawsLogLvl.Warning, Name);
                        }
                    }

                    // --- ORPHANS ---
                    try
                    {
                        List<string> safeOrphans = context.GetSafeOrphanHashes();
                        if (safeOrphans.Count > 0)
                        {
                            context.DeleteFiles(safeOrphans);
                        }
                    }
                    catch { }

                    string stats = $"Osu: {delOsu}, Taiko: {delTaiko}, Catch: {delCatch}, Mania: {delMania}, Other: {delOther}";
                    string msg = $"Cleanup Complete. Processed {setsProcessed} sets (Skipped {skippedByCache}). Deleted {mapsDeleted} maps. ({stats})";

                    return new { Success = true, Message = msg };
                }
                catch (Exception ex)
                {
                    _host.Logger.LogMessage($"Lazer cleanup error: {ex}", PawsLogLvl.Error, Name);
                    return new { Success = false, Message = $"Error: {ex.Message}" };
                }
            });
        }
    }
}


