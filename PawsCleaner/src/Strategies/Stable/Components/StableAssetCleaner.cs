using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Realms;
using System.Text;
using Paws.Core.Abstractions.Interfaces.Services;

namespace PawsCleaner.Strategies.Stable.Components
{
    public class StableAssetCleaner
    {
        private readonly IHost _host;
        private readonly string _name;

        public StableAssetCleaner(IHost host, string strategyName)
        {
            _host = host;
            _name = strategyName;
        }

        public void ExecuteAssetCleaning(Realm realm, CleanerOptions options, string songDir, ref int deletedFiles, ref long freedBytes)
        {
            var assets = options.Assets;
            if (assets == null) return;

            string pluginDataDir = Path.GetDirectoryName(realm.Config.DatabasePath) ?? string.Empty;
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
                _host.LogMessage($"Asset Cleaning Options: Skins={assets.Skins}, Sounds={assets.Sounds}, Videos={assets.Videos}, SB={assets.Storyboards}, BGMode={assets.BackgroundMode}, Nuke={isNuke}", PawsLogLvl.Information, _name);

                int currentFeaturesMask = 0;
                if (assets.Videos) currentFeaturesMask |= 1;
                if (assets.Storyboards) currentFeaturesMask |= 2;
                if (assets.Skins) currentFeaturesMask |= 4;
                if (assets.Sounds) currentFeaturesMask |= 8;
                // Note: Rulesets (16,32,64,128) handled separately in step 1, but we track them in mask
                if (options.Rulesets?.Osu == true) currentFeaturesMask |= 16;
                if (options.Rulesets?.Taiko == true) currentFeaturesMask |= 32;
                if (options.Rulesets?.Catch == true) currentFeaturesMask |= 64;
                if (options.Rulesets?.Mania == true) currentFeaturesMask |= 128;

                // Note: BG mask (256) removed from generic cache check to allow re-running BG changes
                // But we add it to the 'applied' mask after successful run.

                if (options.DryRun)
                {
                    _host.LogMessage("--- DRY RUN STARTED ---", PawsLogLvl.Warning, _name);
                }

                foreach (var map in allIndexed)
                {
                    // Optimization 1: Skip if map hasn't updated and all requested features (except BG) were already applied
                    // We exclude BG (256) from the check requirement, but include it in the applied mask later
                    if ((currentFeaturesMask & ~map.AppliedFeaturesMask) == 0 && map.LastCleanTime > map.LastIndexedTime)
                    {
                        // Optimization 2: Check if BG needs update?
                        // If BG mode is not keep, we should probably run unless we track BG hash.
                        // For now, if BG is requested, we might skip the optimization check or just check if BG was applied?
                        // User requested masking BG out. Let's assume if user runs cleaner, they might want BG update.
                        // But if BG is "keep", we can skip safely.
                        if (bgMode == "keep")
                        {
                            continue;
                        }
                        // If BG is active, we might want to run.
                    }

                    // Optimization 3: Check ContentMask. If map has NONE of the requested features, skip.
                    // (Ignoring BG potential replacement since every map has a BG slot, though not always a file)
                    int requestedContentFeatures = currentFeaturesMask & 15; // 1 | 2 | 4 | 8 (Video, SB, Skin, Sound)
                    if (requestedContentFeatures != 0)
                    {
                        if ((map.ContentMask & requestedContentFeatures) == 0 && bgMode == "keep")
                        {
                            // Map has none of the assets we want to clean, and we aren't replacing BG.
                            // Just mark as done.
                            if (!options.DryRun)
                            {
                                realm.Write(() =>
                                {
                                    map.LastCleanTime = DateTimeOffset.UtcNow;
                                    map.AppliedFeaturesMask |= currentFeaturesMask;
                                });
                            }
                            continue;
                        }
                    }

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

                    if (!options.DryRun)
                    {
                        realm.Write(() =>
                        {
                            map.LastCleanTime = DateTimeOffset.UtcNow;
                            map.AppliedFeaturesMask |= currentFeaturesMask;
                            if (bgMode != "keep") map.AppliedFeaturesMask |= 256;
                        });
                    }
                    itemsProcessed++;
                }

                if (options.DryRun)
                {
                    _host.LogMessage("--- DRY RUN FINISHED ---", PawsLogLvl.Warning, _name);
                }
            }
            catch (Exception ex)
            {
                _host.LogMessage($"Error in asset cleaning: {ex.Message}", PawsLogLvl.Error, _name);
            }
        }
    }
}
