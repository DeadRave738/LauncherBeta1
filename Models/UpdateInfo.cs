namespace MinecraftLauncher.Models
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public bool CriticalUpdate { get; set; }
    }
}