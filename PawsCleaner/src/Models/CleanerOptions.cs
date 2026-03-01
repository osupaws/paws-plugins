namespace PawsCleaner.Models
{
    public class CleanerOptions
    {
        public string? Mode { get; set; }
        public RulesetOptions? Rulesets { get; set; }
        public AssetOptions? Assets { get; set; }
    }

    public class RulesetOptions
    {
        public bool Osu { get; set; }
        public bool Taiko { get; set; }
        public bool Catch { get; set; }
        public bool Mania { get; set; }
    }

    public class AssetOptions
    {
        public bool Skins { get; set; }
        public bool Sounds { get; set; }
        public bool Videos { get; set; }
        public bool Storyboards { get; set; }

        public string? BackgroundMode { get; set; }
        public string? CustomBackgroundAssetId { get; set; }
    }
}
