// Models/Profile.cs (add StorageState serialized)
using System;
using Newtonsoft.Json;

namespace SocialNetworkArmy.Models
{
    public class Profile
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("proxy")]
        public string Proxy { get; set; }

        [JsonProperty("fingerprint")]
        public string Fingerprint { get; set; }

        [JsonProperty("cookies")]
        public string Cookies { get; set; } // Legacy, peut virer si StorageState suffit

        [JsonProperty("storageState")]
        public string StorageState { get; set; } // Nouveau : Full session JSON

        public Profile() { }
    }
}