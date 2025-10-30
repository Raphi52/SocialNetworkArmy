using SocialNetworkArmy.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

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
            public DateTime ScheduledTime { get; set; }
        }

        public static ScheduleMatch GetTodayMediaForAccount(
            string accountName,
            string platform,
            string activity,
            DateTime? targetDate = null,
            TextBox log = null)
        {
            void Log(string message)
            {
                Console.WriteLine(message);
                log?.Invoke((Action)(() => log.AppendText(message + "\r\n")));
            }

            Log($"╔══════════════════════════════════════════════════════════╗");
            Log($"║           SCHEDULE SEARCH - DETAILED LOGS                ║");
            Log($"╚══════════════════════════════════════════════════════════╝");
            Log($"[PARAMS] Account: '{accountName}'");
            Log($"[PARAMS] Platform: '{platform}'");
            Log($"[PARAMS] Activity: '{activity}'");

            var searchDate = (targetDate ?? DateTime.Today).Date;
            Log($"[PARAMS] Search Date: {searchDate:yyyy-MM-dd} (time component stripped)");
            Log($"[PARAMS] Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (string.IsNullOrWhiteSpace(accountName))
            {
                Log($"[ERROR] ✗ Account name is NULL or EMPTY");
                return null;
            }

            if (string.IsNullOrWhiteSpace(platform))
            {
                Log($"[ERROR] ✗ Platform is NULL or EMPTY");
                return null;
            }

            if (string.IsNullOrWhiteSpace(activity))
            {
                Log($"[ERROR] ✗ Activity is NULL or EMPTY");
                return null;
            }

            Log($"");
            Log($"[CSV] Searching for CSV file...");
            Log($"[CSV] Default path: {CSV_PATH}");
            Log($"[CSV] Full path: {Path.GetFullPath(CSV_PATH)}");

            string csvPath = CSV_PATH;
            if (!File.Exists(csvPath))
            {
                Log($"[CSV] ✗ File not found at default location");

                var dir = Path.GetDirectoryName(csvPath) ?? ".";
                var filename = Path.GetFileName(csvPath);

                Log($"[CSV] Searching in directory: {dir}");
                Log($"[CSV] Filename pattern: {filename}");

                if (Directory.Exists(dir))
                {
                    Log($"[CSV] ✓ Directory exists");
                    var allFiles = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                    Log($"[CSV] Files in directory: {allFiles.Length}");

                    foreach (var f in allFiles)
                    {
                        Log($"[CSV]   - {Path.GetFileName(f)}");
                    }

                    var found = Directory.GetFiles(dir, filename, SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (found != null)
                    {
                        csvPath = found;
                        Log($"[CSV] ✓ Found CSV at: {csvPath}");
                    }
                    else
                    {
                        Log($"[CSV] ✗ CSV file '{filename}' not found in directory");
                        return null;
                    }
                }
                else
                {
                    Log($"[CSV] ✗ Directory does not exist: {dir}");
                    return null;
                }
            }
            else
            {
                Log($"[CSV] ✓ File exists at default location");
            }

            Log($"");
            Log($"[PROFILES] Loading profiles from: {PROFILES_PATH}");
            var allProfiles = LoadProfiles();
            Log($"[PROFILES] ✓ Loaded {allProfiles.Count} profile(s)");

            foreach (var p in allProfiles)
            {
                Log($"[PROFILES]   - Name: '{p.Name}', Group: '{p.GroupName ?? "(none)"}'");
            }

            try
            {
                // ✅ Lecture avec encodage UTF-8
                var lines = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8);
                Log($"");
                Log($"[CSV] ✓ Read {lines.Length} line(s) from file (UTF-8 encoding)");

                if (lines.Length < 2)
                {
                    Log($"[CSV] ✗ CSV is empty (only header or no data rows)");
                    return null;
                }

                // ✅ AJOUT: Détection automatique du séparateur
                char separator = DetectCSVSeparator(lines[0]);
                Log($"[CSV] Detected separator: '{separator}'");

                Log($"");
                Log($"[HEADER] Parsing header row...");

                // ✅ Utiliser le séparateur détecté au lieu de SplitCsvLine (qui utilise ',')
                var headers = SplitCSVLine(lines[0], separator);
                Log($"[HEADER] Detected {headers.Length} column(s):");
                for (int h = 0; h < headers.Length; h++)
                {
                    Log($"[HEADER]   [{h}] '{headers[h]}'");
                }

                int iDate = IndexOfHeader(headers, "Date");
                int iPlatform = IndexOfHeader(headers, "Plateform", "Platform");
                int iAccount = IndexOfHeader(headers, "Account/Group", "Account", "Group", "Compte");
                int iActivity = IndexOfHeader(headers, "Activity", "Activité");
                int iPath = IndexOfHeader(headers, "Path", "Media", "MediaPath");
                

                Log($"");
                Log($"[HEADER] Column indices:");
                Log($"[HEADER]   Date: {iDate}");
                Log($"[HEADER]   Platform: {iPlatform}");
                Log($"[HEADER]   Account/Group: {iAccount}");
                Log($"[HEADER]   Activity: {iActivity}");
                Log($"[HEADER]   Path: {iPath}");
               

                if (iDate < 0 || iPlatform < 0 || iAccount < 0 || iActivity < 0 || iPath < 0)
                {
                    Log($"[HEADER] ✗ MISSING REQUIRED COLUMNS!");
                    Log($"[HEADER] Required: Date, Platform/Plateform, Account/Group, Activity, Path");
                    return null;
                }

                Log($"[HEADER] ✓ All required columns found");

                var candidateMatches = new List<ScheduleMatch>();
                int dataRowCount = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        Log($"");
                        Log($"[LINE {i}] ⊘ Empty line, skipping");
                        continue;
                    }

                    Log($"");
                    Log($"[LINE {i}] ╔═══════════════════════════════════════════════════════╗");
                    Log($"[LINE {i}] ║ Processing data row #{++dataRowCount}");
                    Log($"[LINE {i}] ╚═══════════════════════════════════════════════════════╝");
                    Log($"[LINE {i}] Raw: {line}");

                    var cols = SplitCSVLine(line, separator);
                    Log($"[LINE {i}] Parsed into {cols.Length} column(s)");

                    int maxRequiredIndex = Math.Max(Math.Max(iDate, iPlatform), Math.Max(iAccount, iActivity));
                    if (cols.Length <= maxRequiredIndex)
                    {
                        Log($"[LINE {i}] ✗ SKIP: Insufficient columns (has {cols.Length}, needs {maxRequiredIndex + 1})");
                        continue;
                    }

                    string dateTimeStr = cols[iDate].Trim();
                    string csvPlatform = cols[iPlatform].Trim();
                    string accountOrGroup = cols[iAccount].Trim();
                    string csvActivity = cols[iActivity].Trim();
                    string mediaPath = iPath < cols.Length ? cols[iPath].Trim() : "";

                    Log($"[LINE {i}] VALUES:");
                    Log($"[LINE {i}]   Date/Time: '{dateTimeStr}'");
                    Log($"[LINE {i}]   Platform: '{csvPlatform}'");
                    Log($"[LINE {i}]   Account/Group: '{accountOrGroup}'");
                    Log($"[LINE {i}]   Activity: '{csvActivity}'");
                    Log($"[LINE {i}]   Path: '{mediaPath}'");

                    Log($"[LINE {i}] Parsing datetime: '{dateTimeStr}'");
                    DateTime parsedDateTime;

                    if (!DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDateTime))
                    {
                        Log($"[LINE {i}] ⚠ Failed to parse as 'yyyy-MM-dd HH:mm', trying date-only");
                        string dateOnly = dateTimeStr.Contains(" ") ? dateTimeStr.Split(' ')[0] : dateTimeStr;
                        Log($"[LINE {i}] Extracted date part: '{dateOnly}'");

                        if (!DateTime.TryParseExact(dateOnly, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDateTime))
                        {
                            Log($"[LINE {i}] ✗ SKIP: DateTime parse FAILED completely");
                            continue;
                        }
                        Log($"[LINE {i}] ✓ Parsed as date-only (midnight assumed)");
                    }
                    else
                    {
                        Log($"[LINE {i}] ✓ Parsed as full datetime");
                    }

                    Log($"[LINE {i}] Parsed DateTime: {parsedDateTime:yyyy-MM-dd HH:mm:ss}");
                    Log($"[LINE {i}] Search Date:    {searchDate:yyyy-MM-dd HH:mm:ss}");

                    if (parsedDateTime.Date != searchDate)
                    {
                        Log($"[LINE {i}] ✗ SKIP: DATE MISMATCH");
                        Log($"[LINE {i}]   CSV date: {parsedDateTime.Date:yyyy-MM-dd}");
                        Log($"[LINE {i}]   Target:   {searchDate:yyyy-MM-dd}");
                        continue;
                    }
                    Log($"[LINE {i}] ✓ Date matches!");

                    Log($"[LINE {i}] Comparing platforms:");
                    Log($"[LINE {i}]   CSV: '{csvPlatform}'");
                    Log($"[LINE {i}]   Search: '{platform}'");

                    if (!csvPlatform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[LINE {i}] ✗ SKIP: PLATFORM MISMATCH");
                        continue;
                    }
                    Log($"[LINE {i}] ✓ Platform matches!");

                    Log($"[LINE {i}] Comparing activities:");
                    Log($"[LINE {i}]   CSV: '{csvActivity}'");
                    Log($"[LINE {i}]   Search: '{activity}'");

                    if (!csvActivity.Equals(activity, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[LINE {i}] ✗ SKIP: ACTIVITY MISMATCH");
                        continue;
                    }
                    Log($"[LINE {i}] ✓ Activity matches!");

                    Log($"[LINE {i}] Comparing account/group:");
                    Log($"[LINE {i}]   CSV: '{accountOrGroup}'");
                    Log($"[LINE {i}]   Search: '{accountName}'");

                    if (accountOrGroup.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[LINE {i}] ✓✓ DIRECT ACCOUNT MATCH!");

                        if (string.IsNullOrWhiteSpace(mediaPath))
                        {
                            Log($"[LINE {i}] ✗ SKIP: Empty media path");
                            continue;
                        }

                        Log($"[LINE {i}] Checking file existence: {mediaPath}");
                        if (!File.Exists(mediaPath))
                        {
                            Log($"[LINE {i}] ✗ SKIP: File not found: {mediaPath}");
                            continue;
                        }

                        Log($"[LINE {i}] ✓ File exists!");
                        Log($"[LINE {i}] ★★★ MATCH FOUND (DIRECT) ★★★");

                        // ✅ Lire la description depuis le fichier mapping
                        candidateMatches.Add(new ScheduleMatch
                        {
                            MediaPath = mediaPath,
                            Description = null, // ✅ Sera chargé au moment du publish
                            Activity = csvActivity,
                            IsGroup = false,
                            AccountOrGroup = accountOrGroup,
                            ScheduledTime = parsedDateTime
                        });

                        continue;
                    }

                    Log($"[LINE {i}] No direct match, checking groups...");
                    var groupProfiles = allProfiles
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name) &&
                                   !string.IsNullOrWhiteSpace(p.GroupName) &&
                                   p.GroupName.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Log($"[LINE {i}] Found {groupProfiles.Count} profile(s) in group '{accountOrGroup}'");

                    if (groupProfiles.Any())
                    {
                        foreach (var gp in groupProfiles)
                        {
                            Log($"[LINE {i}]   - {gp.Name}");
                        }

                        var matchingProfile = groupProfiles.FirstOrDefault(p =>
                            !string.IsNullOrWhiteSpace(p.Name) &&
                            p.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));

                        if (matchingProfile != null)
                        {
                            Log($"[LINE {i}] ✓ Found account '{matchingProfile.Name}' in group!");

                            if (string.IsNullOrWhiteSpace(mediaPath))
                            {
                                Log($"[LINE {i}] ✗ SKIP: Empty base media path for group");
                                continue;
                            }

                            string accountMediaPath = GetAccountSpecificPath(
                                mediaPath,
                                groupProfiles,
                                matchingProfile);

                            Log($"[LINE {i}] Path mapping:");
                            Log($"[LINE {i}]   Base: {mediaPath}");
                            Log($"[LINE {i}]   Mapped: {accountMediaPath}");

                            if (!File.Exists(accountMediaPath))
                            {
                                Log($"[LINE {i}] ⚠ Mapped file not found, trying base path");
                                accountMediaPath = mediaPath;
                            }

                            if (!File.Exists(accountMediaPath))
                            {
                                Log($"[LINE {i}] ✗ SKIP: No valid file found");
                                continue;
                            }

                            Log($"[LINE {i}] ✓ File exists!");
                            Log($"[LINE {i}] ★★★ MATCH FOUND (GROUP) ★★★");
                            string description = MappingService.GetDescriptionForMedia(accountMediaPath,
                               msg => log?.Invoke((Action)(() => log.AppendText(msg + "\r\n"))));
                            candidateMatches.Add(new ScheduleMatch
                            {
                                MediaPath = accountMediaPath,
                                Description = null, // ✅ Du mapping
                                Activity = csvActivity,
                                IsGroup = true,
                                AccountOrGroup = accountOrGroup,
                                ScheduledTime = parsedDateTime
                            });
                            continue;
                        }
                        else
                        {
                            Log($"[LINE {i}] ✗ Account '{accountName}' not found in group");
                        }
                    }
                    else
                    {
                        Log($"[LINE {i}] ✗ '{accountOrGroup}' is not a known group");
                    }
                }

                Log($"");
                Log($"╔══════════════════════════════════════════════════════════╗");
                Log($"║                    FINAL RESULTS                         ║");
                Log($"╚══════════════════════════════════════════════════════════╝");
                Log($"[RESULT] Processed {dataRowCount} data row(s)");
                Log($"[RESULT] Found {candidateMatches.Count} candidate match(es)");

                if (candidateMatches.Count == 0)
                {
                    Log($"[RESULT] ✗ NO MATCH FOUND");
                    Log($"[RESULT] Double-check:");
                    Log($"[RESULT]   - Account name: '{accountName}'");
                    Log($"[RESULT]   - Platform: '{platform}'");
                    Log($"[RESULT]   - Activity: '{activity}'");
                    Log($"[RESULT]   - Date: {searchDate:yyyy-MM-dd}");
                    return null;
                }

                var now = DateTime.Now;
                Log($"");
                Log($"[SELECTION] Current time: {now:HH:mm:ss}");
                Log($"[SELECTION] Candidates:");

                foreach (var c in candidateMatches.OrderBy(x => x.ScheduledTime))
                {
                    var status = c.ScheduledTime <= now ? "PAST" : "FUTURE";
                    Log($"[SELECTION]   - {c.ScheduledTime:HH:mm} [{status}] → {Path.GetFileName(c.MediaPath)}");
                }

                var pastTasks = candidateMatches
                    .Where(c => c.ScheduledTime <= now)
                    .OrderByDescending(c => c.ScheduledTime)
                    .ToList();

                ScheduleMatch bestMatch;

                if (pastTasks.Any())
                {
                    bestMatch = pastTasks.First();
                    Log($"[SELECTION] ✓ Selected MOST RECENT past task: {bestMatch.ScheduledTime:HH:mm}");
                }
                else
                {
                    bestMatch = candidateMatches.OrderBy(c => c.ScheduledTime).First();
                    Log($"[SELECTION] ✓ Selected NEXT upcoming task: {bestMatch.ScheduledTime:HH:mm}");
                }

                Log($"");
                Log($"[FINAL] ★★★ SELECTED ★★★");
                Log($"[FINAL]   File: {bestMatch.MediaPath}");
                Log($"[FINAL]   Time: {bestMatch.ScheduledTime:HH:mm}");
                Log($"[FINAL]   Type: {(bestMatch.IsGroup ? "Group" : "Direct")}");
                Log($"[FINAL]   Account/Group: {bestMatch.AccountOrGroup}");
                Log($"═══════════════════════════════════════════════════════════");

                return bestMatch;
            }
            catch (Exception ex)
            {
                Log($"");
                Log($"[EXCEPTION] ✗ ERROR: {ex.Message}");
                Log($"[EXCEPTION] Stack trace:");
                Log(ex.StackTrace);
                return null;
            }
        }
        private static int IndexOfHeader(string[] headers, params string[] possibleNames)
{
    for (int i = 0; i < headers.Length; i++)
    {
        foreach (var name in possibleNames)
        {
            if (headers[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
    }
    return -1;
}
        // ✅ AJOUTER CETTE MÉTHODE
        private static char DetectCSVSeparator(string firstLine)
        {
            // Compter les virgules et points-virgules hors guillemets
            int commas = 0, semicolons = 0;
            bool inQuotes = false;

            foreach (char c in firstLine)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (!inQuotes)
                {
                    if (c == ',') commas++;
                    else if (c == ';') semicolons++;
                }
            }

            // Retourner le séparateur le plus fréquent
            return semicolons > commas ? ';' : ',';
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

                var json = File.ReadAllText(PROFILES_PATH, System.Text.Encoding.UTF8);
                var profiles = System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(json)
                              ?? new List<Profile>();

                return profiles.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            }
            catch
            {
                return new List<Profile>();
            }
        }

        // ✅ MÉTHODE MODERNE (avec paramètre separator)
        private static string[] SplitCSVLine(string line, char separator = ',')
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
                else if (c == separator && !inQuotes)
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

       
    }
}