// Services/LimitsService.cs - Limites quotidiennes
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class DailyLimits
    {
        public int MaxLikes { get; set; } = 50;
        public int MaxComments { get; set; } = 20;
        public int MaxPosts { get; set; } = 10;
        public int MaxFollows { get; set; } = 20;
        public DateTime ResetDate { get; set; } = DateTime.Today;
        public int LikesCount { get; set; } = 0; // Compteur actuel
        public int CommentsCount { get; set; } = 0;
        // Ajoute d'autres compteurs si besoin
    }

    public class LimitsService
    {
        private readonly string limitsFile;
        private Dictionary<string, DailyLimits> profileLimits; // Key: action type, Value: limits

        public LimitsService(string profileName)
        {
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            limitsFile = Path.Combine(dataDir, $"{profileName}_limits.json");
            LoadLimits();
        }

        private void LoadLimits()
        {
            if (File.Exists(limitsFile))
            {
                var json = File.ReadAllText(limitsFile);
                profileLimits = JsonConvert.DeserializeObject<Dictionary<string, DailyLimits>>(json) ?? new Dictionary<string, DailyLimits>();
            }
            else
            {
                profileLimits = new Dictionary<string, DailyLimits>();
            }

            // Reset if new day
            if (profileLimits.Values.FirstOrDefault()?.ResetDate < DateTime.Today)
            {
                ResetDailyLimits();
            }
        }

        public void SaveLimits()
        {
            var json = JsonConvert.SerializeObject(profileLimits, Formatting.Indented);
            File.WriteAllText(limitsFile, json);
        }

        public bool CanPerformAction(string actionType, string subAction = "likes") // ex: "target", sub="likes"
        {
            if (!profileLimits.ContainsKey(subAction))
            {
                profileLimits[subAction] = new DailyLimits();
            }

            var limit = profileLimits[subAction];
            if (limit.LikesCount >= limit.MaxLikes) // Exemple pour likes ; adapte
            {
                Logger.LogWarning($"Limite atteinte pour {subAction}: {limit.LikesCount}/{limit.MaxLikes} par jour.");
                return false;
            }

            return true;
        }

        public void IncrementAction(string actionType, string subAction = "likes")
        {
            if (profileLimits.ContainsKey(subAction))
            {
                profileLimits[subAction].LikesCount++; // Exemple
                SaveLimits();
            }
        }

        private void ResetDailyLimits()
        {
            foreach (var kvp in profileLimits.ToList())
            {
                kvp.Value.ResetDate = DateTime.Today;
                kvp.Value.LikesCount = 0; // Reset compteur
                // Reset autres...
            }
            SaveLimits();
            Logger.LogInfo("Limites quotidiennes réinitialisées.");
        }
    }
}