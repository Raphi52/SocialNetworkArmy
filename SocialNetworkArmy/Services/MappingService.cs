using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Service pour lire et parser les fichiers "Descriptions Mapping.txt"
    /// Format attendu: PATH|||DESCRIPTION###PATH|||DESCRIPTION###
    /// </summary>
    public static class MappingService
    {
        private const string MAPPING_FILENAME = "Descriptions Mapping.txt";
        private const string MAPPING_FILENAME_NOSPACE = "DescriptionsMapping.txt";

        // Description par défaut si aucune description trouvée
        private const string DEFAULT_DESCRIPTION = "Check out this amazing moment! What do you think? 🔥";

        /// <summary>
        /// Trouve et lit le fichier mapping dans le dossier du média
        /// </summary>
        /// <param name="mediaPath">Chemin complet vers le fichier média</param>
        /// <param name="log">Action pour logger (optionnel)</param>
        /// <returns>Description trouvée ou description par défaut</returns>
        public static string GetDescriptionForMedia(string mediaPath, Action<string> log = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mediaPath))
                {
                    log?.Invoke("[MAPPING] ✗ Media path is null or empty");
                    return DEFAULT_DESCRIPTION;
                }

                if (!File.Exists(mediaPath))
                {
                    log?.Invoke($"[MAPPING] ✗ Media file not found: {mediaPath}");
                    return DEFAULT_DESCRIPTION;
                }

                // Obtenir le dossier du fichier média
                string mediaDirectory = Path.GetDirectoryName(mediaPath);
                if (string.IsNullOrWhiteSpace(mediaDirectory))
                {
                    log?.Invoke("[MAPPING] ✗ Cannot determine media directory");
                    return DEFAULT_DESCRIPTION;
                }

                log?.Invoke($"[MAPPING] Searching in directory: {mediaDirectory}");

                // Chercher le fichier mapping (avec ou sans espace)
                string mappingPath = FindMappingFile(mediaDirectory, log);
                if (mappingPath == null)
                {
                    log?.Invoke($"[MAPPING] ✗ Mapping file not found in {mediaDirectory}");
                    log?.Invoke($"[MAPPING] → Using default description");
                    return DEFAULT_DESCRIPTION;
                }

                log?.Invoke($"[MAPPING] ✓ Found mapping file: {Path.GetFileName(mappingPath)}");

                // Lire et parser le fichier mapping
                var descriptions = ParseMappingFile(mappingPath, log);

                // Obtenir le nom du fichier média
                string mediaFileName = Path.GetFileName(mediaPath);
                log?.Invoke($"[MAPPING] Looking for: {mediaFileName}");

                // Chercher la description correspondante
                if (descriptions.TryGetValue(mediaFileName, out string description))
                {
                    log?.Invoke($"[MAPPING] ✓✓✓ MATCH FOUND!");
                    log?.Invoke($"[MAPPING] Description: {TruncateForLog(description)}");
                    return description;
                }

                // Fallback: chercher sans extension
                string mediaFileNameNoExt = Path.GetFileNameWithoutExtension(mediaPath);
                var matchNoExt = descriptions.FirstOrDefault(kvp =>
                    Path.GetFileNameWithoutExtension(kvp.Key).Equals(mediaFileNameNoExt, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(matchNoExt.Value))
                {
                    log?.Invoke($"[MAPPING] ✓ Match found (without extension): {matchNoExt.Key}");
                    log?.Invoke($"[MAPPING] Description: {TruncateForLog(matchNoExt.Value)}");
                    return matchNoExt.Value;
                }

                log?.Invoke($"[MAPPING] ✗ No match found for {mediaFileName}");
                log?.Invoke($"[MAPPING] Available files in mapping:");
                foreach (var key in descriptions.Keys.Take(5))
                {
                    log?.Invoke($"[MAPPING]   - {key}");
                }
                if (descriptions.Count > 5)
                {
                    log?.Invoke($"[MAPPING]   ... and {descriptions.Count - 5} more");
                }

                log?.Invoke($"[MAPPING] → Using default description");
                return DEFAULT_DESCRIPTION;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[MAPPING] ✗ ERROR: {ex.Message}");
                log?.Invoke($"[MAPPING] → Using default description");
                return DEFAULT_DESCRIPTION;
            }
        }

        /// <summary>
        /// Cherche le fichier mapping dans le dossier (avec ou sans espace)
        /// </summary>
        private static string FindMappingFile(string directory, Action<string> log)
        {
            try
            {
                // 1. Chercher avec espace
                string pathWithSpace = Path.Combine(directory, MAPPING_FILENAME);
                if (File.Exists(pathWithSpace))
                {
                    return pathWithSpace;
                }

                // 2. Chercher sans espace
                string pathNoSpace = Path.Combine(directory, MAPPING_FILENAME_NOSPACE);
                if (File.Exists(pathNoSpace))
                {
                    log?.Invoke("[MAPPING] Found filename without space");
                    return pathNoSpace;
                }

                // 3. Chercher case-insensitive
                if (Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, "*mapping*.txt", SearchOption.TopDirectoryOnly);
                    var found = files.FirstOrDefault(f =>
                    {
                        string name = Path.GetFileName(f);
                        return name.Equals(MAPPING_FILENAME, StringComparison.OrdinalIgnoreCase) ||
                               name.Equals(MAPPING_FILENAME_NOSPACE, StringComparison.OrdinalIgnoreCase);
                    });

                    if (found != null)
                    {
                        log?.Invoke($"[MAPPING] Found via case-insensitive search: {Path.GetFileName(found)}");
                        return found;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[MAPPING] Error searching for mapping file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse le fichier mapping
        /// Format: PATH|||DESCRIPTION###PATH|||DESCRIPTION###
        /// </summary>
        private static Dictionary<string, string> ParseMappingFile(string mappingPath, Action<string> log)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // ✅ Lire avec UTF-8 pour supporter les accents
                string content = File.ReadAllText(mappingPath, Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(content))
                {
                    log?.Invoke("[MAPPING] ✗ Mapping file is empty");
                    return result;
                }

                // Séparer par ###
                var entries = content.Split(new[] { "###" }, StringSplitOptions.RemoveEmptyEntries);
                log?.Invoke($"[MAPPING] Found {entries.Length} mapping entries");

                int validCount = 0;
                foreach (var entry in entries)
                {
                    var trimmedEntry = entry.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedEntry))
                        continue;

                    // Séparer par |||
                    var parts = trimmedEntry.Split(new[] { "|||" }, StringSplitOptions.None);

                    if (parts.Length < 2)
                    {
                        log?.Invoke($"[MAPPING] ⚠ Invalid entry (no ||| separator): {TruncateForLog(trimmedEntry)}");
                        continue;
                    }

                    string path = parts[0].Trim();
                    string description = parts[1].Trim();

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        log?.Invoke($"[MAPPING] ⚠ Invalid entry (empty path)");
                        continue;
                    }

                    // Extraire juste le nom du fichier si c'est un chemin complet
                    string fileName = Path.GetFileName(path);

                    // Gérer les descriptions vides
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = DEFAULT_DESCRIPTION;
                        log?.Invoke($"[MAPPING] ⚠ Empty description for {fileName}, using default");
                    }

                    // Nettoyer les retours à la ligne littéraux \n en vrais retours
                    description = description.Replace(@"\n", "\n");

                    result[fileName] = description;
                    validCount++;
                }

                log?.Invoke($"[MAPPING] ✓ Parsed {validCount} valid mappings");

                if (validCount == 0)
                {
                    log?.Invoke("[MAPPING] ⚠ WARNING: No valid mappings found in file");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[MAPPING] ✗ Parse error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Tronque un texte pour le log (max 100 caractères)
        /// </summary>
        private static string TruncateForLog(string text, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            text = text.Replace("\n", "\\n").Replace("\r", "");

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Obtenir la description par défaut
        /// </summary>
        public static string GetDefaultDescription()
        {
            return DEFAULT_DESCRIPTION;
        }
    }
}