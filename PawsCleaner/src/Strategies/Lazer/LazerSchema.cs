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

        public DateTimeOffset LastCleanTime { get; set; }

        public static string ComputeOptionsHash(CleanerOptions options)
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = false });
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToBase64String(bytes);
        }
    }
}
