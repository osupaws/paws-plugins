using Realms;

namespace PawsCleaner.Strategies.Stable
{
    public partial class IndexedBeatmap : IRealmObject
    {
        [PrimaryKey]
        public string FolderPath { get; set; } = ""; // Relative to Songs folder

        public DateTimeOffset LastIndexedTime { get; set; } // When we last scanned this folder
        public DateTimeOffset LastFolderWriteTime { get; set; } // LastWriteTime of the folder to detect changes

        public DateTimeOffset LastCleanTime { get; set; } // When the cleanup was last performed

        public int AppliedFeaturesMask { get; set; } // Bitmask of CleanerFeatures (matching Lazer bitmask)
        public string OptionsHash { get; set; } = ""; // Hash of cleaner options (for BG replacement detection)

        public int ContentMask { get; set; } // Bitmask of what the map folder ACTUALLY HAS (Videos, SB, etc.)

        public IList<IndexedFile> Files { get; } = null!;
    }

    public partial class IndexedFile : IRealmObject
    {
        public string Filename { get; set; } = "";
        public string Extension { get; set; } = ""; // Normalized, e.g. ".jpg"

        // Usage Bitmask:
        // 1 = Background (Explicitly referenced as BG event)
        // 2 = Audio (Explicitly referenced as AudioFilename)
        // 4 = Video (Explicitly referenced as Video event)
        // 8 = Storyboard (Explicitly referenced in .osb or .osu events)
        // 16 = Script (The .osu or .osb file itself)
        public int UsageType { get; set; }

        // "Skin" is not a UsageType because the map script doesn't say "Use cursor.png".
        // Instead, the GAME engine checks for "cursor.png" automatically.
        // So distinct property: IsSkinnable (true if filename matches standard skin list)
        public bool IsSkinnable { get; set; }

        public int RulesetId { get; set; } = -1; // Only for .osu files (UsageType & 16). 0=osu, 1=taiko, 2=catch, 3=mania
    }
}
