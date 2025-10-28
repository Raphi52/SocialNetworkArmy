using System;
using System.IO;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;

namespace SocialNetworkArmy.Services
{
    public static class ConfigService
    {
        private static readonly string CONFIG_DIR = Path.Combine("Data", "Configs");

        static ConfigService()
        {
            // Ensure config directory exists
            if (!Directory.Exists(CONFIG_DIR))
            {
                Directory.CreateDirectory(CONFIG_DIR);
            }
        }

        public static AccountConfig LoadConfig(string accountName)
        {
            try
            {
                string filePath = GetConfigPath(accountName);

                if (!File.Exists(filePath))
                {
                    // Return default config if file doesn't exist
                    return new AccountConfig(accountName);
                }

                string json = File.ReadAllText(filePath);

                // ✅ FIX: Force proper deserialization with ObjectCreationHandling
                var settings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                var config = JsonConvert.DeserializeObject<AccountConfig>(json, settings);

                // Ensure accountName is set
                if (string.IsNullOrEmpty(config.AccountName))
                    config.AccountName = accountName;

                // ✅ FIX: Ensure TargetLanguages is never null
                if (config.TargetLanguages == null)
                    config.TargetLanguages = new List<string> { "Any" };

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config for {accountName}: {ex.Message}");
                return new AccountConfig(accountName);
            }
        }

        public static void SaveConfig(AccountConfig config)
        {
            try
            {
                if (string.IsNullOrEmpty(config.AccountName))
                    throw new ArgumentException("AccountName cannot be empty");

                string filePath = GetConfigPath(config.AccountName);
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving config: {ex.Message}");
            }
        }

        private static string GetConfigPath(string accountName)
        {
            // Sanitize account name for filename
            string safeFileName = string.Join("_", accountName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(CONFIG_DIR, $"{safeFileName}.json");
        }

        public static bool ConfigExists(string accountName)
        {
            return File.Exists(GetConfigPath(accountName));
        }
    }
}
