using System;

namespace MinecraftLauncher.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public DateTime RegisteredAt { get; set; }
        public bool IsAgreedToLicense { get; set; }

        // Дополнительные поля для Minecraft
        public string MinecraftUsername { get; set; }
        public string AccessToken { get; set; }
        public string ClientToken { get; set; }
    }
}