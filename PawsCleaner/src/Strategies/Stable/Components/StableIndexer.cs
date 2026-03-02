using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Interfaces;
using Paws.Core.Abstractions.Enums;
using PawsCleaner.Common;
using Realms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PawsCleaner.Strategies.Stable.Components
{
    public class StableIndexer
    {
        private readonly Paws.Core.Abstractions.Interfaces.Services.IHost _host;
        private readonly string _name;

        public StableIndexer(Paws.Core.Abstractions.Interfaces.Services.IHost host, string strategyName)
        {
            _host = host;
            _name = strategyName;
        }

        public List<string> IndexFolders(Realm realm, IStableContext stable, List<string> folderNames, string songsDir, Dictionary<string, int> fileRulesetIds)
        {
            var errors = new List<string>();
            int i = 0;

            foreach (var folderName in folderNames)
            {
                string folderPath = Path.Combine(songsDir, folderName);
                if (!_host.Storage.DirectoryExists(folderPath)) continue;

                // 1. Scan ALL files recursively
                var allFiles = _host.Storage.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                var assetsUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                void Mark(string f, int m) => MarkUsage(assetsUsage, f, m);

                int contentMask = 0;

                // 2. Heavy Analysis via Core (Detached DTO Pattern)
                // GetUsedAssets returns a HashSet of relative paths to ALL referenced assets in the folder.
                var usedAssets = stable.GetUsedAssets(folderPath);

                foreach (var asset in usedAssets)
                {
                    string ext = Path.GetExtension(asset).ToLowerInvariant();
                    int mask = 8; // Default: Storyboard/Asset

                    if (AssetUtils.IsAudio(ext)) mask = 2; // Audio
                    else if (AssetUtils.IsSkinImage(ext))
                    {
                        mask = 1; // Background/Image
                        contentMask |= 1; // Background
                    }
                    else if (AssetUtils.IsVideo(ext))
                    {
                        mask = 4; // Video
                        contentMask |= 1; // Video bit in ContentMask
                    }

                    Mark(asset, mask);
                }

                // Mark scripts and detect SB presence
                foreach (var file in allFiles)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string relPath = Path.GetRelativePath(folderPath, file).Replace('\\', '/');

                    if (ext == ".osu" || ext == ".osb")
                    {
                        Mark(relPath, 16); // Script mask
                        if (ext == ".osb") contentMask |= 2; // Storyboard bit
                    }
                }

                // 3. Collect Folder Stats for UI
                bool hasSkinnable = false;
                bool hasExtraSounds = false;

                foreach (var filePath in allFiles)
                {
                    string relPath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                    string ext2 = Path.GetExtension(relPath).ToLowerInvariant();
                    string fnameOnly = Path.GetFileName(relPath);

                    bool isUsageAudio = false;
                    if (assetsUsage.TryGetValue(relPath, out int u))
                    {
                        isUsageAudio = (u & 2) != 0;
                    }

                    if (KnownFiles.IsSkinnable(fnameOnly))
                    {
                        hasSkinnable = true;
                        if (AssetUtils.IsAudio(ext2) && !isUsageAudio) hasExtraSounds = true;
                    }
                }

                if (hasSkinnable) contentMask |= 4; // Skins
                if (hasExtraSounds) contentMask |= 8; // Sounds

                // 4. Save to Realm
                realm.Write(() =>
                {
                    var existing = realm.Find<IndexedBeatmap>(folderName);

                    long lastClean = 0;
                    int appliedFeatures = 0;
                    string optionsHash = "";

                    if (existing != null)
                    {
                        lastClean = existing.LastCleanTime.ToUnixTimeMilliseconds();
                        appliedFeatures = existing.AppliedFeaturesMask;
                        optionsHash = existing.OptionsHash;
                    }

                    var indexedSet = new IndexedBeatmap
                    {
                        FolderPath = folderName,
                        LastIndexedTime = DateTimeOffset.UtcNow,
                        LastFolderWriteTime = _host.Storage.GetLastWriteTimeUtc(folderPath),
                        ContentMask = contentMask,
                        AppliedFeaturesMask = appliedFeatures,
                        OptionsHash = optionsHash,
                        LastCleanTime = DateTimeOffset.FromUnixTimeMilliseconds(lastClean)
                    };

                    foreach (var filePath in allFiles)
                    {
                        string relPath = Path.GetRelativePath(folderPath, filePath).Replace('\\', '/');
                        string fileExt = Path.GetExtension(relPath).ToLowerInvariant();
                        string fnameOnly = Path.GetFileName(relPath);

                        int usage = 0;
                        if (assetsUsage.TryGetValue(relPath, out var u)) usage = u;

                        bool isSkinnable = KnownFiles.IsSkinnable(fnameOnly);

                        int rulesetId = -1;
                        if (fileRulesetIds.TryGetValue(relPath, out var r)) rulesetId = r;

                        indexedSet.Files.Add(new IndexedFile
                        {
                            Filename = relPath,
                            Extension = fileExt,
                            UsageType = usage,
                            IsSkinnable = isSkinnable,
                            RulesetId = rulesetId
                        });
                    }

                    realm.Add(indexedSet, update: true);
                });

                i++;
                if (i % 50 == 0) _host.Logger.LogMessage($"Indexed {i}/{folderNames.Count} folders...", PawsLogLvl.Information, _name);
            }

            return errors;
        }

        private void MarkUsage(Dictionary<string, int> dict, string filename, int mask)
        {
            if (string.IsNullOrEmpty(filename)) return;
            string cleanName = filename.Replace("\"", "").Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(cleanName)) return;

            if (dict.TryGetValue(cleanName, out int currentMask))
                dict[cleanName] = currentMask | mask;
            else
                dict[cleanName] = mask;
        }
    }
}
