using Realms;
using System;
using System.Collections.Generic;

namespace PawsCleaner.Stable
{
    public partial class IndexedBeatmap : IRealmObject
    {
        [PrimaryKey]
        public string Hash { get; set; } = ""; // MD5 from osu!.db

        public string FolderPath { get; set; } = "";
        
        public DateTimeOffset LastIndexedTime { get; set; } // For detecting folder changes (re-download check)
        
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
    }
}
