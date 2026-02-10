using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Interfaces.Services;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Realms;
using System.Text;

namespace PawsCleaner.Strategies.Stable.Components
{
    public class StableIndexer
    {
        private readonly IHost _host;
        private readonly string _name;

        public StableIndexer(IHost host, string strategyName)
        {
            _host = host;
            _name = strategyName;
        }

        public void IndexMaps(Realm realm, IStableContext stable, List<dynamic> maps, string songDir)
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
                            _host.LogMessage($"[Index] Failed to parse {fname}: {ex.Message}", PawsLogLvl.Error, _name);
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

                // Calculate ContentMask
                int contentMask = 0;
                foreach (var kvp in assetsUsage)
                {
                    // If usage has Video (4), add to mask
                    if ((kvp.Value & 4) != 0) contentMask |= 1; // 1 in FeatureMask = Video
                    // If usage has SB (8), add to mask
                    if ((kvp.Value & 8) != 0) contentMask |= 2; // 2 in FeatureMask = SB
                }

                // Refinining ContentMask logic requires iterating ALL files
                bool hasSkinnable = false;
                bool hasExtraSounds = false;

                foreach (var filePath in allFiles)
                {
                    string f = Path.GetFileName(filePath);
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    bool isUsageAudio = false;
                    if (assetsUsage.TryGetValue(f, out int u))
                    {
                        isUsageAudio = (u & 2) != 0;
                    }

                    if (KnownFiles.IsSkinnable(f))
                    {
                        hasSkinnable = true;
                        if (AssetUtils.IsAudio(ext) && !isUsageAudio) hasExtraSounds = true;
                    }
                }

                if (hasSkinnable) contentMask |= 4; // Skins
                if (hasExtraSounds) contentMask |= 8; // Sounds


                realm.Write(() =>
                {
                    var existing = realm.Find<IndexedBeatmap>(map.MD5Hash);
                    if (existing != null) realm.Remove(existing);

                    var indexedMap = new IndexedBeatmap
                    {
                        Hash = map.MD5Hash,
                        FolderPath = map.FolderName,
                        LastIndexedTime = DateTimeOffset.UtcNow,
                        ContentMask = contentMask
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
                if (i % 50 == 0) _host.LogMessage($"Indexed {i}/{maps.Count}...", PawsLogLvl.Information, _name);
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
