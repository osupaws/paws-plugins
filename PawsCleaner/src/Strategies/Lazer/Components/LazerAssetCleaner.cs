using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using Paws.Core.Abstractions.Interfaces.Contexts;
using Paws.Core.Abstractions.Models;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Strategies.Lazer;
using Paws.Core.Abstractions.Interfaces.Services;
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
                    if (!options.DryRun)
                    {
                        set.Files.RemoveAt(f);
                        setModified = true;
                        removals++;
                    }
                }
            }

            if (setModified && !options.DryRun)
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
            string bgMode = options.Assets?.BackgroundMode?.ToLowerInvariant() ?? "keep";

            if ((bgMode == "white" || bgMode == "custom") && !options.DryRun)
            {
                // (Same BG logic as before)
                try
                {
                    string sourceJpg = "";
                    string sourcePng = "";
                    bool tempJpgCreated = false;
                    bool tempPngCreated = false;

                    if (bgMode == "white")
                    {
                        string whiteJpgB64 = "/9j/2wBDAAIBAQIBAQICAgICAgICAwUDAwMDAwYEBAMFBwYHBwcGBwcICQsJCAgKCAcHCg0KCgsMDAwMBwkODw0MDgsMDAz/wgALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/aAAgBAQAAAAB/P//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Af//Z";
                        string whitePngB64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABAQAAAAA3bvkkAAAACklEQVR42mNoAAAAggCB2kUIOwAAAABJRU5ErkJggg==";

                        sourceJpg = Path.Combine(Path.GetTempPath(), "paws_white.jpg");
                        sourcePng = Path.Combine(Path.GetTempPath(), "paws_white.png");

                        await File.WriteAllBytesAsync(sourceJpg, Convert.FromBase64String(whiteJpgB64));
                        await File.WriteAllBytesAsync(sourcePng, Convert.FromBase64String(whitePngB64));
                        tempJpgCreated = true;
                        tempPngCreated = true;
                    }
                    else if (bgMode == "custom" && options.Assets != null)
                    {
                        if (!string.IsNullOrEmpty(options.Assets.CustomBackgroundJpg))
                        {
                            try
                            {
                                string b64 = options.Assets.CustomBackgroundJpg.Contains(",") ? options.Assets.CustomBackgroundJpg.Split(',')[1] : options.Assets.CustomBackgroundJpg;
                                sourceJpg = Path.Combine(Path.GetTempPath(), "paws_custom.jpg");
                                await File.WriteAllBytesAsync(sourceJpg, Convert.FromBase64String(b64));
                                tempJpgCreated = true;
                            }
                            catch { }
                        }
                        if (!string.IsNullOrEmpty(options.Assets.CustomBackgroundPng))
                        {
                            try
                            {
                                string b64 = options.Assets.CustomBackgroundPng.Contains(",") ? options.Assets.CustomBackgroundPng.Split(',')[1] : options.Assets.CustomBackgroundPng;
                                sourcePng = Path.Combine(Path.GetTempPath(), "paws_custom.png");
                                await File.WriteAllBytesAsync(sourcePng, Convert.FromBase64String(b64));
                                tempPngCreated = true;
                            }
                            catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(sourceJpg) && File.Exists(sourceJpg))
                    {
                        importedJpg = await context.ImportFile(sourceJpg, Path.GetFileName(sourceJpg));
                        if (tempJpgCreated) File.Delete(sourceJpg);
                    }
                    if (!string.IsNullOrEmpty(sourcePng) && File.Exists(sourcePng))
                    {
                        importedPng = await context.ImportFile(sourcePng, Path.GetFileName(sourcePng));
                        if (tempPngCreated) File.Delete(sourcePng);
                    }

                    if (importedJpg != null || importedPng != null)
                        bgImported = true;
                }
                catch (Exception ex)
                {
                    _host.LogMessage($"[BG ERROR] BG Prep failed: {ex.Message}", PawsLogLvl.Error, _name);
                }
            }
            return (importedJpg, importedPng, bgImported);
        }
    }
}
