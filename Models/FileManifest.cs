using System.Collections.Generic;

namespace MinecraftLauncher.Models
{
    public class FileManifest
    {
        public string Version { get; set; }
        public List<FileEntry> Files { get; set; }
    }

    public class FileEntry
    {
        public string Path { get; set; }
        public string Hash { get; set; }
        public long Size { get; set; }
        public bool Required { get; set; } = true;
    }
}