using Paws.Core.Abstractions;
using Paws.Core.Abstractions.Enums;
using PawsCleaner.Abstractions;
using PawsCleaner.Common;
using PawsCleaner.Models;
using Paws.Core.Abstractions.Models;
using Realms;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Paws.Core.Abstractions.Interfaces.Services;
using Paws.Core.Abstractions.Interfaces;

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

        public static string ComputeOptionsHash(CleanerOptions options, Paws.Core.Abstractions.Interfaces.Services.IStorageService? storage = null)
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = false });

            if (storage != null && options.Assets?.BackgroundMode?.ToLowerInvariant() == "custom")
            {
                string dataDir = storage.GetPluginDataPath();
                string sourcePath = Path.Combine(dataDir, "custom_bg.src");
                if (storage.FileExists(sourcePath))
                {
                    try
                    {
                        using var stream = storage.OpenFile(sourcePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                        using var hashAlg = SHA256.Create();
                        var hashBytes = hashAlg.ComputeHash(stream);
                        json += $"|C_BG_H:{Convert.ToBase64String(hashBytes)}";
                    }
                    catch { }
                }
            }

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(bytes);
        }

        public async Task<(string? srcJpg, string? srcPng, bool srcCreated)> PrepareBackgroundsAsync(CleanerOptions options)
        {
            var assets = options.Assets;
            if (assets == null) return (null, null, false);

            string bgMode = assets.BackgroundMode?.ToLowerInvariant() ?? "keep";
            if (bgMode != "white" && bgMode != "custom") return (null, null, false);

            try
            {
                string dataDir = _host.Storage.GetPluginDataPath();
                string tempDir = _host.Storage.GetPluginTempPath();

                string? srcJpg = null;
                string? srcPng = null;

                if (bgMode == "white")
                {
                    srcJpg = Path.Combine(dataDir, "white_bg.jpg");
                    srcPng = Path.Combine(dataDir, "white_bg.png");

                    if (!_host.Storage.FileExists(srcJpg) || !_host.Storage.FileExists(srcPng))
                    {
                        _host.Logger.LogMessage("[BG] Generating persistent white backgrounds (Stable)...", PawsLogLvl.Information, _name);
                        byte[] whiteJpg = { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03, 0x05, 0x03, 0x03, 0x03, 0x03, 0x03, 0x06, 0x04, 0x04, 0x03, 0x05, 0x07, 0x06, 0x07, 0x07, 0x07, 0x06, 0x07, 0x07, 0x08, 0x09, 0x0B, 0x09, 0x08, 0x08, 0x0A, 0x08, 0x07, 0x07, 0x0A, 0x0D, 0x0A, 0x0A, 0x0B, 0x0C, 0x0C, 0x0C, 0x0C, 0x07, 0x09, 0x0E, 0x0F, 0x0D, 0x0C, 0x0E, 0x0B, 0x0C, 0x0C, 0x0C, 0xFF, 0xC2, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x7F, 0x3F, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x01, 0x3F, 0x00, 0x7F, 0xFF, 0xD9 };
                        byte[] whitePng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x37, 0x6E, 0xF9, 0x24, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0xDA, 0x63, 0x68, 0x00, 0x00, 0x00, 0x82, 0x00, 0x81, 0xDA, 0x45, 0x08, 0x3B, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

                        using (var msJ = new MemoryStream(whiteJpg))
                        using (var target = _host.Storage.OpenFile(srcJpg, FileMode.Create, FileAccess.Write))
                        {
                            await msJ.CopyToAsync(target);
                        }

                        using (var msP = new MemoryStream(whitePng))
                        using (var target = _host.Storage.OpenFile(srcPng, FileMode.Create, FileAccess.Write))
                        {
                            await msP.CopyToAsync(target);
                        }
                    }
                }
                else // Custom
                {
                    srcJpg = Path.Combine(dataDir, "custom_bg.jpg");
                    srcPng = Path.Combine(dataDir, "custom_bg.png");
                    string sourcePath = Path.Combine(dataDir, "custom_bg.src");

                    if (!_host.Storage.FileExists(sourcePath)) return (null, null, false);

                    // If jpg/png missing or source is newer, re-process
                    bool needsProcess = !_host.Storage.FileExists(srcJpg) || !_host.Storage.FileExists(srcPng);
                    if (!needsProcess)
                    {
                        var srcTime = _host.Storage.GetLastWriteTimeUtc(sourcePath);
                        var jpgTime = _host.Storage.GetLastWriteTimeUtc(srcJpg);
                        if (srcTime > jpgTime) needsProcess = true;
                    }

                    if (needsProcess)
                    {
                        _host.Logger.LogMessage("[BG] Processing custom background source (Stable)...", PawsLogLvl.Information, _name);

                        // Process source to JPG
                        using (var srcStream = _host.Storage.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
                        using (var jpgStream = await _host.Image.ProcessImageAsync(srcStream, new ImageProcessOptions { TargetFormat = "jpg", Quality = 85 }))
                        using (var target = _host.Storage.OpenFile(srcJpg, FileMode.Create, FileAccess.Write))
                        {
                            await jpgStream.CopyToAsync(target);
                        }

                        // Process source to PNG
                        using (var srcStream = _host.Storage.OpenFile(sourcePath, FileMode.Open, FileAccess.Read))
                        using (var pngStream = await _host.Image.ProcessImageAsync(srcStream, new ImageProcessOptions { TargetFormat = "png" }))
                        using (var target = _host.Storage.OpenFile(srcPng, FileMode.Create, FileAccess.Write))
                        {
                            await pngStream.CopyToAsync(target);
                        }
                    }
                }

                return (srcJpg, srcPng, true);
            }
            catch (Exception ex)
            {
                _host.Logger.LogMessage($"[BG ERROR] Stable BG Prep failed: {ex.Message}", PawsLogLvl.Error, _name);
                return (null, null, false);
            }
        }

        public (int deletedFiles, long freedBytes) ExecuteAssetCleaning(Realm realm, CleanerOptions options, string songDir, string? srcJpg, string? srcPng, bool srcCreated)
        {
            int deletedFiles = 0;
            long freedBytes = 0;
            var assets = options.Assets;
            if (assets == null) return (0, 0);

            string bgMode = assets.BackgroundMode?.ToLowerInvariant() ?? "keep";

            try
            {

                var allIndexed = realm.All<IndexedBeatmap>().ToList();
                bool isNuke = assets.Skins && assets.Sounds && assets.Videos && assets.Storyboards;
                string currentOptionsHash = ComputeOptionsHash(options, _host.Storage);
                _host.Logger.LogMessage($"Asset Cleaning Options: Skins={assets.Skins}, Sounds={assets.Sounds}, Videos={assets.Videos}, SB={assets.Storyboards}, BGMode={assets.BackgroundMode}, Nuke={isNuke}", PawsLogLvl.Information, _name);

                int currentFeaturesMask = 0;
                if (assets.Videos) currentFeaturesMask |= 1;
                if (assets.Storyboards) currentFeaturesMask |= 2;
                if (assets.Skins) currentFeaturesMask |= 4;
                if (assets.Sounds) currentFeaturesMask |= 8;
                if (options.Rulesets?.Osu == true) currentFeaturesMask |= 16;
                if (options.Rulesets?.Taiko == true) currentFeaturesMask |= 32;
                if (options.Rulesets?.Catch == true) currentFeaturesMask |= 64;
                if (options.Rulesets?.Mania == true) currentFeaturesMask |= 128;

                foreach (var map in allIndexed)
                {
                    if ((currentFeaturesMask & ~map.AppliedFeaturesMask) == 0 && map.LastCleanTime > map.LastIndexedTime)
                    {
                        if (bgMode == "keep" || map.OptionsHash == currentOptionsHash)
                        {
                            continue;
                        }
                    }

                    int requestedContentFeatures = currentFeaturesMask & 15;
                    if (requestedContentFeatures != 0 && (map.ContentMask & requestedContentFeatures) == 0 && bgMode == "keep")
                    {
                        realm.Write(() =>
                        {
                            map.LastCleanTime = DateTimeOffset.UtcNow;
                            map.AppliedFeaturesMask |= currentFeaturesMask;
                            map.OptionsHash = currentOptionsHash;
                        });
                        continue;
                    }

                    var mapFolder = Path.Combine(songDir, map.FolderPath);
                    if (!_host.Storage.DirectoryExists(mapFolder)) continue;

                    int deletedThisMap = 0;
                    bool bgReplacedForMap = false;

                    foreach (var file in map.Files)
                    {
                        bool shouldDelete = false;
                        bool isReplacement = false;
                        string? replacementSource = null;

                        bool isBg = (file.UsageType & 1) != 0;
                        bool isScript = (file.UsageType & 16) != 0;
                        bool isAudio = (file.UsageType & 2) != 0;
                        bool isVideo = (file.UsageType & 4) != 0;
                        bool isSb = (file.UsageType & 8) != 0;

                        if (isBg && (bgMode == "white" || bgMode == "custom") && srcCreated)
                        {
                            string targetExt = file.Extension.ToLower();
                            replacementSource = (targetExt == ".png") ? srcPng : srcJpg;
                            if (string.IsNullOrEmpty(replacementSource) || !_host.Storage.FileExists(replacementSource))
                                replacementSource = srcJpg;

                            if (!string.IsNullOrEmpty(replacementSource) && _host.Storage.FileExists(replacementSource))
                            {
                                shouldDelete = true;
                                isReplacement = true;
                                bgReplacedForMap = true;
                            }
                        }

                        if (isNuke)
                        {
                            if (isBg)
                            {
                                if (bgMode == "keep") { shouldDelete = false; isReplacement = false; }
                            }
                            else if (isScript || isAudio) { shouldDelete = false; }
                            else { shouldDelete = true; }
                        }
                        else if (!isBg)
                        {
                            if (assets.Storyboards && isSb) shouldDelete = true;
                            if (assets.Videos && isVideo) shouldDelete = true;
                            if (assets.Skins && file.IsSkinnable && (AssetUtils.IsSkinImage(file.Extension))) shouldDelete = true;
                            if (assets.Sounds && file.IsSkinnable && (AssetUtils.IsAudio(file.Extension)) && !isAudio) shouldDelete = true;
                        }

                        if (shouldDelete)
                        {
                            var fullPath = Path.Combine(mapFolder, file.Filename);
                            if (_host.Storage.FileExists(fullPath))
                            {
                                try
                                {
                                    freedBytes += _host.Storage.GetFileLength(fullPath);
                                    _host.Storage.DeleteFile(fullPath);
                                    deletedFiles++;
                                    deletedThisMap++;

                                    if (isReplacement && !string.IsNullOrEmpty(replacementSource) && _host.Storage.FileExists(replacementSource))
                                    {
                                        // Use IStableContext for symlinks if available, or just copy as fallback
                                        try { _host.Stable.GetStableContext().CreateSymlink(replacementSource, fullPath); }
                                        catch { /* Fallback to manual check if needed, but CreateSymlink is in V3 */ }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // Read actual folder timestamp after modifications
                    var postCleanupFolderTime = _host.Storage.GetLastWriteTimeUtc(mapFolder);

                    realm.Write(() =>
                    {
                        map.LastCleanTime = DateTimeOffset.UtcNow;
                        if (deletedThisMap > 0 || bgReplacedForMap)
                        {
                            map.LastIndexedTime = postCleanupFolderTime;
                        }

                        map.AppliedFeaturesMask |= currentFeaturesMask;
                        if (bgMode != "keep") map.AppliedFeaturesMask |= 256;
                        map.OptionsHash = currentOptionsHash;
                    });
                }
            }
            catch (Exception ex)
            {
                _host.Logger.LogMessage($"Error in asset cleaning: {ex.Message}", PawsLogLvl.Error, _name);
            }

            return (deletedFiles, freedBytes);
        }
    }
}
