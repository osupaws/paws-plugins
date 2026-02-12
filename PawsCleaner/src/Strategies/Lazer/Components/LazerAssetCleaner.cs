using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Models;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Strategies.Lazer;
using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Interfaces;
using PawsCleaner.Models;

namespace PawsCleaner.Strategies.Lazer.Components
{
    public class LazerAssetCleaner
    {
        private readonly IHost _host;
        private readonly string _name;

        public LazerAssetCleaner(IHost host, string strategyName)
        {
            _host = host;
            _name = strategyName;
        }

        public int Execute(ILazerContext context, dynamic set, CleanerOptions options, string? importedJpg, string? importedPng, HashSet<string> bgFiles, HashSet<string> protectedFiles, bool bgImported)
        {
            var assets = options.Assets;
            bool setModified = false;
            string bgMode = assets?.BackgroundMode?.ToLowerInvariant() ?? "keep";

            // If options.Assets is null, we can't clean assets
            if (assets == null) return 0;

            int removals = 0;

            // Nuke Check
            bool isNuke = assets.Skins && assets.Sounds && assets.Videos && assets.Storyboards;

            // Iterate backwards to allow removal
            for (int f = set.Files.Count - 1; f >= 0; f--)
            {
                var fileUsage = set.Files[f];
                if (fileUsage == null) continue;

                string? fname = fileUsage.Filename;
                if (string.IsNullOrEmpty(fname)) continue;

                string ext = AssetUtils.GetExtension(fname);
                if (ext == ".osu") continue;

                bool isBg = bgFiles.Contains(fname);
                bool isProtected = protectedFiles.Contains(fname);

                // BG Replace
                if (isBg && bgImported)
                {
                    string? targetHash = (ext == ".png") ? (importedPng ?? importedJpg) : (importedJpg ?? importedPng);

                    if (targetHash != null && fileUsage.Hash != targetHash)
                    {
                        // Assign new Hash (but keep original filename)
                        fileUsage.Hash = targetHash;
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
                    if (ext == ".osb" || ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        // Check if file is ONLY used as Storyboard/Skin element
                        // But wait, we don't have usage bitmask here like in Stable.
                        // We rely on 'protectedFiles' which contains explicit BG/Audio/Video references.
                        // If it's not protected, and it's an image... it MIGHT be SB.
                        // But it could also be a Skin element.
                        // Or unused.
                        shouldUnlink = true;
                    }
                }

                if (assets?.Skins == true && AssetUtils.IsSkinImage(ext)) shouldUnlink = true;
                if (assets?.Sounds == true && AssetUtils.IsAudio(ext)) shouldUnlink = true;

                // Nuke Override
                if (isNuke) shouldUnlink = true;

                // Safety: Don't unlink if it's explicitly protected (Audio/BG)
                // But we already checked `isProtected` above.

                if (shouldUnlink)
                {
                    set.Files.RemoveAt(f);
                    setModified = true;
                    removals++;
                }
            }

            if (setModified)
            {
                context.UpdateBeatmapSet(set);
                return 1; // Count as 1 set modified
            }

            return 0;
        }

        public async Task<(string? importedJpg, string? importedPng, bool bgImported)> PrepareBackgroundsAsync(ILazerContext context, CleanerOptions options)
        {
            string? importedJpg = null;
            string? importedPng = null;
            bool bgImported = false;
            var assets = options.Assets;
            string bgMode = assets?.BackgroundMode?.ToLowerInvariant() ?? "keep";

            if (bgMode == "white" || bgMode == "custom")
            {
                try
                {
                    string dataDir = _host.Storage.GetPluginDataPath();
                    string tempDir = _host.Storage.GetPluginTempPath();

                    string jpgPath;
                    string pngPath;

                    if (bgMode == "white")
                    {
                        jpgPath = Path.Combine(dataDir, "white_bg.jpg");
                        pngPath = Path.Combine(dataDir, "white_bg.png");

                        // Generate white backgrounds ONCE if they don't exist in Data
                        if (!_host.Storage.FileExists(jpgPath) || !_host.Storage.FileExists(pngPath))
                        {
                            _host.Logger.LogMessage("[BG] Generating persistent white backgrounds...", PawsLogLvl.Information, _name);
                            // We use a 1x1 white pixel stream for efficiency
                            byte[] whiteJpg = { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x05, 0x03, 0x03, 0x03, 0x03, 0x03, 0x06, 0x04, 0x04, 0x03, 0x05, 0x07, 0x06, 0x07, 0x07, 0x07, 0x06, 0x07, 0x07, 0x08, 0x09, 0x0B, 0x09, 0x08, 0x08, 0x0A, 0x08, 0x07, 0x07, 0x0A, 0x0D, 0x0A, 0x0A, 0x0B, 0x0C, 0x0C, 0x0C, 0x0C, 0x07, 0x09, 0x0E, 0x0F, 0x0D, 0x0C, 0x0E, 0x0B, 0x0C, 0x0C, 0x0C, 0xFF, 0xC2, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x7F, 0x3F, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x01, 0x3F, 0x00, 0x7F, 0xFF, 0xD9 };
                            byte[] whitePng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x37, 0x6E, 0xF9, 0x24, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0xDA, 0x63, 0x68, 0x00, 0x00, 0x00, 0x82, 0x00, 0x81, 0xDA, 0x45, 0x08, 0x3B, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

                            using (var msJ = new MemoryStream(whiteJpg))
                            using (var target = _host.Storage.OpenFile(jpgPath, FileMode.Create, FileAccess.Write))
                            {
                                await msJ.CopyToAsync(target);
                            }

                            using (var msP = new MemoryStream(whitePng))
                            using (var target = _host.Storage.OpenFile(pngPath, FileMode.Create, FileAccess.Write))
                            {
                                await msP.CopyToAsync(target);
                            }
                        }
                    }
                    else // Custom
                    {
                        jpgPath = Path.Combine(dataDir, "custom_bg.jpg");
                        pngPath = Path.Combine(dataDir, "custom_bg.png");
                        string sourcePath = Path.Combine(dataDir, "custom_bg.src");

                        if (!_host.Storage.FileExists(sourcePath)) return (null, null, false);

                        // If jpg/png missing or source is newer, re-process
                        bool needsProcess = !_host.Storage.FileExists(jpgPath) || !_host.Storage.FileExists(pngPath);
                        if (!needsProcess)
                        {
                            var srcTime = _host.Storage.GetLastWriteTimeUtc(sourcePath);
                            var jpgTime = _host.Storage.GetLastWriteTimeUtc(jpgPath);
                            if (srcTime > jpgTime) needsProcess = true;
                        }

                        if (needsProcess)
                        {
                            _host.Logger.LogMessage("[BG] Processing custom background source...", PawsLogLvl.Information, _name);

                            // Process source to JPG
                            using (var srcStream = _host.Storage.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
                            using (var jpgStream = await _host.Image.ProcessImageAsync(srcStream, new ImageProcessOptions { TargetFormat = "jpg", Quality = 85 }))
                            using (var target = _host.Storage.OpenFile(jpgPath, FileMode.Create, FileAccess.Write))
                            {
                                await jpgStream.CopyToAsync(target);
                            }

                            // Process source to PNG
                            using (var srcStream = _host.Storage.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
                            using (var pngStream = await _host.Image.ProcessImageAsync(srcStream, new ImageProcessOptions { TargetFormat = "png" }))
                            using (var target = _host.Storage.OpenFile(pngPath, FileMode.Create, FileAccess.Write))
                            {
                                await pngStream.CopyToAsync(target);
                            }
                        }
                    }

                    importedJpg = await context.ImportFile(jpgPath, "background.jpg");
                    importedPng = await context.ImportFile(pngPath, "background.png");
                    bgImported = true;
                }
                catch (Exception ex)
                {
                    _host.Logger.LogMessage($"[BG ERROR] BG Prep failed: {ex.Message}", PawsLogLvl.Error, _name);
                }
            }
            return (importedJpg, importedPng, bgImported);
        }
    }
}
