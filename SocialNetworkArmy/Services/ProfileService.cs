using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class ProfileService
    {
        private readonly string profilesFile;

        public ProfileService()
        {
            // Répertoire de base de l’exécutable
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // On garde la casse réelle du dossier
            var dataDir = Path.Combine(baseDir, "Data");

            // Création auto du dossier s’il n’existe pas
            Directory.CreateDirectory(dataDir);

            // Fichier JSON complet
            profilesFile = Path.Combine(dataDir, "profiles.json");
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

            Logger.LogInfo("Aucun fichier de profils trouvé (Data/profiles.json).");
            return new List<Profile>();
        }

        public void SaveProfiles(List<Profile> profiles)
        {
            try
            {
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
