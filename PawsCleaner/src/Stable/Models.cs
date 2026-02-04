using Realms;
using System.Collections.Generic;

namespace PawsCleaner.Stable
{
    public class IndexedBeatmap : RealmObject
    {
        [PrimaryKey]
        public string Hash { get; set; } = ""; // MD5 from osu!.db

        public string FolderPath { get; set; } = "";
        
        public IList<IndexedFile> Files { get; } = null!;
    }

    public class IndexedFile : RealmObject
    {
        public string Filename { get; set; } = "";
        
        // If true, this file is referenced by the map (Background, Audio, Video, Storyboard, HitSound)
        // If false, it exists in the folder but is NOT used (likely skin element or garbage)
        public bool IsUsed { get; set; } 
        
        public string Extension { get; set; } = ""; // Normalized, e.g. ".jpg"
    }
}
