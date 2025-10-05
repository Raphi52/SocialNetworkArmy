// Services/ProfileService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class ProfileService
    {
        private readonly string rootDir;
        private readonly string profilesFile;

        public ProfileService()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var binDir = Path.GetDirectoryName(assemblyPath);
            rootDir = Path.GetFullPath(Path.Combine(binDir, "../../../")); // Up three levels to project root
            profilesFile = Path.Combine(rootDir, "Data", "profiles.json");
        }

        public List<Profile> LoadProfiles()
        {
            if (File.Exists(profilesFile))
            {
                try
                {
                    var json = File.ReadAllText(profilesFile);
                    var loaded = JsonConvert.DeserializeObject<List<Profile>>(json) ?? new List<Profile>();
                    Logger.LogInfo($"Profils chargés : {loaded.Count}.");
                    return loaded;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Erreur chargement : {ex.Message}");
                    return new List<Profile>();
                }
            }
            return new List<Profile>();
        }

        public void SaveProfiles(List<Profile> profiles)
        {
            var dataDir = Path.GetDirectoryName(profilesFile);
            try
            {
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                var json = JsonConvert.SerializeObject(profiles, Formatting.Indented);
                File.WriteAllText(profilesFile, json);
                Logger.LogInfo($"Profils sauvés : {profiles.Count}.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Erreur sauvegarde : {ex.Message}.");
                throw;
            }
        }
    }
}