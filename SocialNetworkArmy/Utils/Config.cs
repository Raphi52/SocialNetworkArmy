// Utils/Config.cs
using System;
using System.IO;
using Newtonsoft.Json;

namespace SocialNetworkArmy.Utils
{
    public class Config
    {
        public int ViewDurationMin { get; set; } = 5;
        public int ViewDurationMax { get; set; } = 10;
        public int LikePercent { get; set; } = 20;
        public int CommentPercent { get; set; } = 30;
        public int MaxReelsPerProfile { get; set; } = 50;
        public int ActionDelayMin { get; set; } = 2000;
        public int ActionDelayMax { get; set; } = 5000;
        public int ScrollDurationMin { get; set; } = 20;
        public int ScrollDurationMax { get; set; } = 40;

        // Limites quotidiennes
        public int MaxLikesPerDay { get; set; } = 50;
        public int MaxCommentsPerDay { get; set; } = 20;
        public int MaxPostsPerDay { get; set; } = 10;
        public double InteractionVariance { get; set; } = 0.2; // Pour random avancé

        private static readonly string ConfigFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "config.json");

        public static Config GetConfig()
        {
            try
            {
                if (!File.Exists(ConfigFile))
                {
                    // si pas de fichier → on génère une config par défaut
                    var defaultConfig = new Config();
                    defaultConfig.SaveConfig();
                    return defaultConfig;
                }

                string json = File.ReadAllText(ConfigFile);
                var cfg = JsonConvert.DeserializeObject<Config>(json) ?? new Config();

                // sécurise les bornes min/max
                cfg.Normalize();

                return cfg;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Erreur lecture config : {ex.Message}");
                // fallback : retourne une config par défaut
                return new Config();
            }
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Erreur écriture config : {ex.Message}");
            }
        }

        public void Normalize()
        {
            if (ViewDurationMin > ViewDurationMax)
                (ViewDurationMin, ViewDurationMax) = (ViewDurationMax, ViewDurationMin);

            if (ActionDelayMin > ActionDelayMax)
                (ActionDelayMin, ActionDelayMax) = (ActionDelayMax, ActionDelayMin);

            if (ScrollDurationMin > ScrollDurationMax)
                (ScrollDurationMin, ScrollDurationMax) = (ScrollDurationMax, ScrollDurationMin);

            // On peut ajouter d’autres corrections ici si nécessaire
        }
    }
}
