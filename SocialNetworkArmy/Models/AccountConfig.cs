using System.Collections.Generic;
using Newtonsoft.Json;

namespace SocialNetworkArmy.Models
{
    public class AccountConfig
    {
        [JsonProperty("accountName")]
        public string AccountName { get; set; }

        [JsonProperty("minCommentsToAddToFutureTargets")]
        public int MinCommentsToAddToFutureTargets { get; set; } = 300;

        [JsonProperty("targetLanguages")]
        public List<string> TargetLanguages { get; set; } = new List<string> { "Any" };

        [JsonProperty("maxPostAgeHours")]
        public int MaxPostAgeHours { get; set; } = 24;

        [JsonProperty("niche")]
        public string Niche { get; set; } = "Girls"; // "Any", "Girls", future: "Fitness", "Gaming", etc.

        public AccountConfig()
        {
        }

        public AccountConfig(string accountName)
        {
            AccountName = accountName;
        }

        // Helper to check if a language is targeted
        public bool IsLanguageTargeted(string language)
        {
            if (TargetLanguages == null || TargetLanguages.Count == 0)
                return true; // If no languages set, accept all

            if (TargetLanguages.Contains("Any"))
                return true;

            return TargetLanguages.Contains(language);
        }

        // Helper to check if niche filter should be applied
        public bool ShouldApplyNicheFilter()
        {
            return Niche != null && Niche != "Any";
        }
    }
}
