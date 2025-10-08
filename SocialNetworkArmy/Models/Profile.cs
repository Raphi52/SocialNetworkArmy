// Models/Profile.cs
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

        [JsonProperty("userDataFolderId")]
        public string UserDataFolderId { get; set; }

        [JsonProperty("fingerprint")]
        public string Fingerprint { get; set; }

        [JsonProperty("cookies")]
        public string Cookies { get; set; }

        [JsonProperty("storageState")]
        public string StorageState { get; set; }

        public Profile()
        {
            // Auto-générer un GUID unique si pas encore défini
            if (string.IsNullOrEmpty(UserDataFolderId))
            {
                UserDataFolderId = Guid.NewGuid().ToString();
            }
        }
    }
}