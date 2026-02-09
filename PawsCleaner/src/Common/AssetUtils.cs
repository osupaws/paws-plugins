namespace PawsCleaner.Common
{
    public static class AssetUtils
    {
        public static bool IsVideo(string extension)
        {
            var ext = extension.ToLowerInvariant();
            return ext == ".avi" || ext == ".mp4" || ext == ".mkv" || ext == ".flv" || ext == ".m4v";
        }

        public static bool IsAudio(string extension) // Assuming hit sounds / effects
        {
            var ext = extension.ToLowerInvariant();
            return ext == ".wav" || ext == ".mp3" || ext == ".ogg";
        }

        public static bool IsStoryboard(string extension)
        {
            return extension.Equals(".osb", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSkinImage(string extension)
        {
            var ext = extension.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        // Helper to normalize extension checks
        public static string GetExtension(string filename)
        {
            return Path.GetExtension(filename).ToLowerInvariant();
        }
    }
}
