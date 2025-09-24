using System;
using System.Configuration;

namespace MinecraftLauncher.Core
{
    public static class ConfigManager
    {
        public static string GetConnectionString()
        {
            return ConfigurationManager.ConnectionStrings["ForgeLauncherDB"].ConnectionString;
        }
        public static string GetAppSetting(string key, string defaultValue = null)
        {
            // Получаем значение из App.config
            return ConfigurationManager.AppSettings[key] ?? defaultValue;
        }
        public static string GetAppSetting(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrEmpty(value))
            {
                throw new ConfigurationErrorsException($"Конфигурационный параметр '{key}' не найден или пуст");
            }
            return value;
        }
        public static int GetAppSettingAsInt(string key, int defaultValue = 0)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (int.TryParse(value, out int result))
            {
                return result;
            }
            return defaultValue;
        }

        public static bool GetAppSettingAsBool(string key, bool defaultValue = false)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (bool.TryParse(value, out bool result))
            {
                return result;
            }
            return defaultValue;
        }
    }
}