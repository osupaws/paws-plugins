using Realms;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawsCleaner.Models;

namespace PawsCleaner.Strategies.Lazer
{
    /// <summary>
    /// Represents a cached processing result for a Lazer beatmap set.
    /// If the set hash and cleaner options hash match, we can skip processing.
    /// </summary>
    public partial class CachedLazerSet : IRealmObject
    {
        [PrimaryKey]
        public string SetId { get; set; } = ""; // Maps to BeatmapSet.ID.ToString()

        public string SetHash { get; set; } = ""; // Hash of the beatmap set (files/beatmaps in it)

        public string OptionsHash { get; set; } = ""; // Hash of the options used for cleaning

        public int AppliedFeaturesMask { get; set; } // Bitmask of CleanerFeatures

        public DateTimeOffset LastCleanTime { get; set; }

        public static int ComputeFeaturesMask(CleanerOptions options)
        {
            int mask = 0;
            if (options.Assets?.Videos == true) mask |= 1;
            if (options.Assets?.Storyboards == true) mask |= 2;
            if (options.Assets?.Skins == true) mask |= 4;
            if (options.Assets?.Sounds == true) mask |= 8;

            if (options.Rulesets?.Osu == true) mask |= 16;
            if (options.Rulesets?.Taiko == true) mask |= 32;
            if (options.Rulesets?.Catch == true) mask |= 64;
            if (options.Rulesets?.Mania == true) mask |= 128;

            // BG (256) removed from mask to ensure re-runs if BG options change
            // if (options.Assets?.BackgroundMode != null && options.Assets.BackgroundMode.ToLowerInvariant() != "keep")
            //    mask |= 256;

            return mask;
        }

        public static string ComputeOptionsHash(CleanerOptions options)
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = false });
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(bytes);
        }
    }
}
