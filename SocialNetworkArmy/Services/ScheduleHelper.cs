using SocialNetworkArmy.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SocialNetworkArmy.Services
{
    public static class ScheduleHelper
    {
        private static string CSV_PATH = Path.Combine("Data", "Schedule.csv");
        private static string PROFILES_PATH = Path.Combine("Data", "profiles.json");

        public class ScheduleMatch
        {
            public string MediaPath { get; set; }
            public string Description { get; set; }
            public string Activity { get; set; }
            public bool IsGroup { get; set; }
            public string AccountOrGroup { get; set; }
        }

        public static ScheduleMatch GetTodayMediaForAccount(
            string accountName,
            string platform,
            string activity,
            DateTime? targetDate = null)
        {
            Console.WriteLine($"\n========== SCHEDULE SEARCH ==========");
            Console.WriteLine($"Looking for:");
            Console.WriteLine($"  Account: '{accountName}'");
            Console.WriteLine($"  Platform: '{platform}'");
            Console.WriteLine($"  Activity: '{activity}'");

            // ✅ TOUJOURS utiliser DateTime.Today.Date (ignorer l'heure complètement)
            var searchDate = (targetDate ?? DateTime.Today).Date;
            Console.WriteLine($"  Date: {searchDate:yyyy-MM-dd} (time ignored)");
            Console.WriteLine($"======================================\n");

            if (string.IsNullOrWhiteSpace(accountName))
            {
                Console.WriteLine("[Schedule] ✗ Account name is empty");
                return null;
            }

            // ✅ Chercher le CSV
            string csvPath = CSV_PATH;
            if (!File.Exists(csvPath))
            {
                var dir = Path.GetDirectoryName(csvPath) ?? ".";
                var filename = Path.GetFileName(csvPath);

                if (Directory.Exists(dir))
                {
                    var found = Directory.GetFiles(dir, filename, SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (found != null)
                    {
                        csvPath = found;
                        Console.WriteLine($"[Schedule] ✓ Found CSV at: {csvPath}");
                    }
                    else
                    {
                        Console.WriteLine($"[Schedule] ✗ CSV not found in: {dir}");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine($"[Schedule] ✗ Directory not found: {dir}");
                    return null;
                }
            }

            Console.WriteLine($"[Schedule] ✓ CSV found, searching for: {accountName} | {platform} | {activity}");
            Console.WriteLine($"[Schedule] Target date: {searchDate:yyyy-MM-dd}");

            var allProfiles = LoadProfiles();
            Console.WriteLine($"[Schedule] Loaded {allProfiles.Count} profiles");

            try
            {
                var lines = File.ReadAllLines(csvPath);
                Console.WriteLine($"[Schedule] CSV has {lines.Length} lines (including header)");

                if (lines.Length < 2)
                {
                    Console.WriteLine("[Schedule] ✗ CSV is empty (no data rows)");
                    return null;
                }

                var headers = SplitCsvLine(lines[0]);
                int iDate = IndexOfHeader(headers, "Date");
                int iPlatform = IndexOfHeader(headers, "Plateform", "Platform");
                int iAccount = IndexOfHeader(headers, "Account/Group", "Account", "Group", "Compte");
                int iActivity = IndexOfHeader(headers, "Activity", "Activité");
                int iPath = IndexOfHeader(headers, "Path", "Media", "MediaPath");
                int iDesc = IndexOfHeader(headers, "Post Description", "Description", "Caption");

                if (iDate < 0 || iPlatform < 0 || iAccount < 0 || iActivity < 0 || iPath < 0)
                {
                    Console.WriteLine("[Schedule] ✗ Missing required columns");
                    return null;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var cols = SplitCsvLine(line);
                    if (cols.Length <= Math.Max(Math.Max(iDate, iPlatform), Math.Max(iAccount, iActivity)))
                        continue;

                    Console.WriteLine($"\n[Schedule] ========== LINE {i} ==========");
                    Console.WriteLine($"[Schedule] Raw: {line}");

                    // ✅ EXTRAIRE LA DATE (ignorer complètement l'heure)
                    string dateTimeStr = cols[iDate].Trim();
                    string dateOnly = dateTimeStr.Contains(" ") ? dateTimeStr.Split(' ')[0] : dateTimeStr;
                    Console.WriteLine($"[Schedule] Date string: '{dateTimeStr}' → extracted: '{dateOnly}'");

                    if (!DateTime.TryParseExact(dateOnly, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        Console.WriteLine($"[Schedule] ✗ Date parse FAILED");
                        continue;
                    }

                    Console.WriteLine($"[Schedule] CSV date: {parsedDate:yyyy-MM-dd}");
                    Console.WriteLine($"[Schedule] Search date: {searchDate:yyyy-MM-dd}");

                    // ✅ FIX: Comparer UNIQUEMENT les dates (l'heure est complètement ignorée)
                    if (parsedDate.Date != searchDate)
                    {
                        Console.WriteLine($"[Schedule] ✗ DATE MISMATCH - SKIP (CSV: {parsedDate:yyyy-MM-dd} vs Search: {searchDate:yyyy-MM-dd})");
                        continue;
                    }

                    Console.WriteLine($"[Schedule] ✓ Date matches! (both are {searchDate:yyyy-MM-dd})");

                    // ✅ Vérifier platform
                    string csvPlatform = cols[iPlatform].Trim();
                    Console.WriteLine($"[Schedule] Platform: '{csvPlatform}' vs '{platform}'");

                    if (!csvPlatform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Schedule] ✗ PLATFORM MISMATCH - SKIP");
                        continue;
                    }

                    Console.WriteLine($"[Schedule] ✓ Platform matches!");

                    // ✅ Vérifier activity
                    string csvActivity = cols[iActivity].Trim();
                    Console.WriteLine($"[Schedule] Activity: '{csvActivity}' vs '{activity}'");

                    if (!csvActivity.Equals(activity, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Schedule] ✗ ACTIVITY MISMATCH - SKIP");
                        continue;
                    }

                    Console.WriteLine($"[Schedule] ✓ Activity matches!");

                    // ✅ Vérifier account/group
                    string accountOrGroup = cols[iAccount].Trim();
                    Console.WriteLine($"[Schedule] Account/Group: '{accountOrGroup}' vs '{accountName}'");

                    // ✅ NOUVELLE LOGIQUE: Comparaison directe d'abord, puis groupes

                    // 1) ✅ MATCH DIRECT: Comparer directement le CSV avec le compte actuel
                    if (accountOrGroup.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Schedule] ✓✓ DIRECT MATCH! Account name matches!");

                        string mediaPath = iPath < cols.Length && cols[iPath] != null
                            ? cols[iPath].Trim()
                            : null;

                        Console.WriteLine($"[Schedule] Media path: '{mediaPath}'");

                        if (string.IsNullOrWhiteSpace(mediaPath))
                        {
                            Console.WriteLine($"[Schedule] ⚠ Empty media path for {accountName}");
                            continue;
                        }

                        if (!File.Exists(mediaPath))
                        {
                            Console.WriteLine($"[Schedule] ✗ File not found: {mediaPath}");
                            continue;
                        }

                        Console.WriteLine($"[Schedule] ✓✓✓ MATCH FOUND! → {Path.GetFileName(mediaPath)}");

                        return new ScheduleMatch
                        {
                            MediaPath = mediaPath,
                            Description = iDesc < cols.Length ? cols[iDesc].Trim() : "",
                            Activity = csvActivity,
                            IsGroup = false,
                            AccountOrGroup = accountOrGroup
                        };
                    }

                    // 2) ✅ GROUP MATCH: Vérifier si c'est un groupe contenant le compte actuel
                    Console.WriteLine($"[Schedule] No direct match, checking if '{accountOrGroup}' is a group...");

                    var groupProfiles = allProfiles
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name) &&
                                   !string.IsNullOrWhiteSpace(p.GroupName) &&
                                   p.GroupName.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Console.WriteLine($"[Schedule] Found {groupProfiles.Count} profiles in group '{accountOrGroup}'");

                    if (groupProfiles.Any())
                    {
                        var matchingProfile = groupProfiles.FirstOrDefault(p =>
                            !string.IsNullOrWhiteSpace(p.Name) &&
                            p.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));

                        if (matchingProfile != null)
                        {
                            Console.WriteLine($"[Schedule] ✓ Found account in group: {matchingProfile.Name}");

                            string baseMediaPath = iPath < cols.Length && cols[iPath] != null
                                ? cols[iPath].Trim()
                                : null;

                            if (string.IsNullOrWhiteSpace(baseMediaPath))
                            {
                                Console.WriteLine($"[Schedule] ⚠ Empty media path for group {accountOrGroup}");
                                continue;
                            }

                            string accountMediaPath = GetAccountSpecificPath(
                                baseMediaPath,
                                groupProfiles,
                                matchingProfile);

                            Console.WriteLine($"[Schedule] Base path: {baseMediaPath}");
                            Console.WriteLine($"[Schedule] Mapped path: {accountMediaPath}");

                            if (!File.Exists(accountMediaPath))
                            {
                                Console.WriteLine($"[Schedule] ⚠ Mapped file not found: {accountMediaPath}");
                                accountMediaPath = baseMediaPath;
                            }

                            if (!File.Exists(accountMediaPath))
                            {
                                Console.WriteLine($"[Schedule] ✗ No valid file found for {accountName}");
                                continue;
                            }

                            Console.WriteLine($"[Schedule] ✓✓✓ GROUP MATCH FOUND! → {Path.GetFileName(accountMediaPath)}");

                            return new ScheduleMatch
                            {
                                MediaPath = accountMediaPath,
                                Description = iDesc < cols.Length ? cols[iDesc].Trim() : "",
                                Activity = csvActivity,
                                IsGroup = true,
                                AccountOrGroup = accountOrGroup
                            };
                        }
                        else
                        {
                            Console.WriteLine($"[Schedule] ✗ Account '{accountName}' not found in group '{accountOrGroup}'");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Schedule] ✗ '{accountOrGroup}' is neither the current account nor a group containing it");
                    }
                }

                Console.WriteLine($"[Schedule] ✗ No match found");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Schedule] Error: {ex.Message}");
                return null;
            }
        }

        private static string GetAccountSpecificPath(
            string basePath,
            List<Profile> groupProfiles,
            Profile currentProfile)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                basePath,
                @"(account|compte)\s+(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (!match.Success)
                return basePath;

            var sortedProfiles = groupProfiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .OrderBy(p => p.Name)
                .ToList();

            int accountIndex = sortedProfiles.FindIndex(p =>
                !string.IsNullOrWhiteSpace(p.Name) &&
                p.Name.Equals(currentProfile.Name, StringComparison.OrdinalIgnoreCase));

            if (accountIndex < 0)
                return basePath;

            string prefix = match.Groups[1].Value;
            string sourcePattern = match.Value;
            string targetPattern = $"{prefix} {accountIndex + 1}";

            string newPath = System.Text.RegularExpressions.Regex.Replace(
                basePath,
                System.Text.RegularExpressions.Regex.Escape(sourcePattern),
                targetPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            return newPath;
        }

        private static List<Profile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(PROFILES_PATH))
                    return new List<Profile>();

                var json = File.ReadAllText(PROFILES_PATH);
                var profiles = System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(json)
                              ?? new List<Profile>();

                return profiles.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            }
            catch
            {
                return new List<Profile>();
            }
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = "";
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            result.Add(current);
            return result.ToArray();
        }

        private static int IndexOfHeader(string[] headers, params string[] names)
        {
            if (headers == null) return -1;

            for (int i = 0; i < headers.Length; i++)
            {
                var h = (headers[i] ?? "").Trim();
                foreach (var name in names)
                {
                    if (h.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }
    }
}