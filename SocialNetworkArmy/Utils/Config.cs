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

        private static readonly string ConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "config.json");

        public static Config GetConfig()
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
            }
            return new Config();
        }

        public void SaveConfig()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigFile, json);
        }
    }
}