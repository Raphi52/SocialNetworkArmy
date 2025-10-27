using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class ScheduleService
    {
        private static string CSV_PATH = Path.Combine("Data", "Schedule.csv");

        private readonly TextBox logTextBox;
        private readonly ProfileService profileService;

        private CancellationTokenSource _cts;
        private FileSystemWatcher _fileWatcher;

        // ✅ UN SEUL LOCK pour TOUT
        private readonly object _lockObj = new object();

        private volatile bool _isRunning = false;

        // Dictionnaires protégés par _lockObj
        private readonly Dictionary<string, Form> _activeForms = new Dictionary<string, Form>(StringComparer.OrdinalIgnoreCase);

        private List<ScheduledTask> _tasks = new List<ScheduledTask>();

        // ✅ AJOUT: Locks par groupe pour éviter les conflits
        private static readonly Dictionary<string, SemaphoreSlim> _groupLocks = new Dictionary<string, SemaphoreSlim>();
        private static readonly object _lockDictLock = new object();

        public ScheduleService(TextBox logTextBox, ProfileService profileService)
        {
            this.logTextBox = logTextBox;
            this.profileService = profileService;
        }

        public bool IsRunning => _isRunning;

        // ✅ AJOUT: Récupérer le lock d'un groupe
        public static SemaphoreSlim GetGroupLock(string groupName)
        {
            lock (_lockDictLock)
            {
                if (!_groupLocks.ContainsKey(groupName))
                {
                    _groupLocks[groupName] = new SemaphoreSlim(1, 1);
                }
                return _groupLocks[groupName];
            }
        }

        // ---------------------------
        // Public API
        // ---------------------------
        public async Task ToggleAsync()
        {
            if (_isRunning) await StopAsync();
            else await StartAsync();
        }

        public async Task StartAsync()
        {
            lock (_lockObj)
            {
                if (_isRunning)
                {
                    LogToUI("[Schedule] Already running.");
                    return;
                }
                _isRunning = true;
            }

            LogToUI("[Schedule] ========================================");
            LogToUI("[Schedule] Starting Global Scheduler...");
            LogToUI("[Schedule] ========================================");

            try
            {
                EnsureCsvExists();
                LoadTasksFromCSV();
                SetupFileWatcher();

                _cts = new CancellationTokenSource();
                _ = Task.Run(() => SchedulerLoop(_cts.Token));

                LogToUI("[Schedule] ✓ Scheduler ACTIVE - Monitoring CSV for changes");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ERROR during start: {ex.Message}");
                _isRunning = false;
            }
        }

        public async Task StopAsync()
        {
            List<string> keysToClose;
            lock (_lockObj)
            {
                if (!_isRunning) return;
                _isRunning = false;
                keysToClose = _activeForms.Keys.ToList();
            }

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _fileWatcher?.Dispose();
                _fileWatcher = null;

                // ✅ Fermer SANS lock (évite deadlock)
                foreach (var key in keysToClose)
                {
                    await CloseFormForKeyAsync(key);
                }

                LogToUI("[Schedule] ✓ Scheduler STOPPED");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] Stop error: {ex.Message}");
            }
        }

        private void EnsureCsvExists()
        {
            // ✅ CHERCHER LE FICHIER EN IGNORANT LA CASSE
            string csvPath = CSV_PATH;

            if (!File.Exists(csvPath))
            {
                var dir = Path.GetDirectoryName(csvPath) ?? ".";
                var filename = Path.GetFileName(csvPath);

                if (Directory.Exists(dir))
                {
                    var found = Directory.GetFiles(dir, filename, SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();

                    if (found != null)
                    {
                        CSV_PATH = found; // ⚠️ Problème: CSV_PATH est readonly
                        LogToUI($"[Schedule] ✓ Found CSV at: {found}");
                        return;
                    }
                }

                LogToUI($"[Schedule] CSV not found at {csvPath}. Creating template...");
                Directory.CreateDirectory(Path.GetDirectoryName(csvPath) ?? ".");

                string csvTemplate = @"Date,Plateform,Account/Group,Activity,Path,Post Description
2025-10-26 14:30,Instagram,alice_account,target,,Example: single account
2025-10-26 15:00,Instagram,marketing_team,publish,,Example: group (all accounts in group)
";
                File.WriteAllText(csvPath, csvTemplate);
                LogToUI("[Schedule] ✓ Template created with examples.");
            }
        }

        private void SetupFileWatcher()
        {
            try
            {
                var fullPath = Path.GetFullPath(CSV_PATH);
                var dir = Path.GetDirectoryName(fullPath);
                var file = Path.GetFileName(fullPath);

                _fileWatcher = new FileSystemWatcher(dir ?? ".", file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                DateTime lastReload = DateTime.MinValue;
                _fileWatcher.Changed += (s, e) =>
                {
                    if ((DateTime.Now - lastReload).TotalSeconds < 1.5) return;
                    lastReload = DateTime.Now;

                    Task.Delay(300).ContinueWith(_ =>
                    {
                        LogToUI("[Schedule] ----------------------------------------");
                        LogToUI("[Schedule] CSV file modified - Reloading tasks...");
                        LoadTasksFromCSV();
                        LogToUI("[Schedule] ----------------------------------------");
                    });
                };

                LogToUI($"[Schedule] File watcher active on: {fullPath}");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] FileWatcher error: {ex.Message}");
            }
        }

        // Remplacer la méthode LoadTasksFromCSV() par celle-ci :

        private void LoadTasksFromCSV()
        {
            lock (_lockObj)
            {
                var newTasks = new List<ScheduledTask>();
                try
                {
                    if (!File.Exists(CSV_PATH))
                    {
                        LogToUI("[Schedule] CSV file not found.");
                        return;
                    }

                    var lines = File.ReadAllLines(CSV_PATH)
                                    .Skip(1)
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToList();

                    int lineNumber = 2;
                    foreach (var line in lines)
                    {
                        try
                        {
                            var parts = SplitCSVLine(line);
                            if (parts.Length < 4)
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: insufficient columns");
                                lineNumber++;
                                continue;
                            }

                            string dateTimeStr = parts[0].Trim();

                            if (!DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: invalid date '{dateTimeStr}' (format: yyyy-MM-dd HH:mm)");
                                lineNumber++;
                                continue;
                            }

                            string platform = parts[1].Trim();
                            string accountOrGroup = parts[2].Trim();
                            string activity = parts[3].Trim().ToLowerInvariant();
                            string mediaPath = parts.Length > 4 ? parts[4].Trim() : "";
                            string description = parts.Length > 5 ? parts[5].Trim() : "";

                            var valid = new[] { "publish", "reels", "home", "target", "dm", "download", "story", "stop", "close" };
                            if (!valid.Contains(activity))
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: unknown activity '{activity}'");
                                lineNumber++;
                                continue;
                            }

                            // ✅ DÉTECTION AUTOMATIQUE : Compte ou Groupe
                            var profiles = profileService.LoadProfiles();

                            // D'abord chercher un compte exact
                            var singleProfile = profiles.FirstOrDefault(p =>
                                p.Name.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase));

                            if (singleProfile != null)
                            {
                                // ✅ C'EST UN COMPTE INDIVIDUEL
                                var task = new ScheduledTask
                                {
                                    Date = date,
                                    Platform = platform,
                                    Account = accountOrGroup,
                                    Activity = activity,
                                    MediaPath = mediaPath,
                                    Description = description,
                                    Executed = false
                                };
                                newTasks.Add(task);
                            }
                            else
                            {
                                // ✅ CHERCHER UN GROUPE
                                var groupProfiles = profiles
                                    .Where(p => !string.IsNullOrWhiteSpace(p.GroupName) &&
                                               p.GroupName.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                if (!groupProfiles.Any())
                                {
                                    LogToUI($"[Schedule] Line {lineNumber} skipped: no account or group found for '{accountOrGroup}'");
                                    lineNumber++;
                                    continue;
                                }

                                LogToUI($"[Schedule] Line {lineNumber}: Expanding group '{accountOrGroup}' → {groupProfiles.Count} accounts");

                                // ✅ DÉTECTER LE PATTERN DU PATH
                                string baseMediaPath = mediaPath;
                                string accountPattern = null;

                                if (!string.IsNullOrWhiteSpace(baseMediaPath) && File.Exists(baseMediaPath))
                                {
                                    // Chercher un pattern "compte X" dans le path
                                    var match = System.Text.RegularExpressions.Regex.Match(
               baseMediaPath,
               @"(compte|account)\s+(\d+)",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase
           );

                                    if (match.Success)
                                    {
                                        accountPattern = match.Groups[1].Value; // ex: "compte 1"
                                        LogToUI($"[Schedule] Detected pattern '{accountPattern}'");
                                    }
                                }

                                // Créer une tâche par compte du groupe avec offset automatique
                                int accountIndex = 0;
                                var random = new Random();

                                foreach (var profile in groupProfiles.OrderBy(p => p.Name))
                                {
                                    // Ajouter un offset de 2-5 minutes pour chaque compte après le premier
                                    var taskDate = accountIndex == 0
                                        ? date
                                        : date.AddMinutes(accountIndex * random.Next(2, 6));

                                    // ✅ CALCULER LE PATH SPÉCIFIQUE AU COMPTE
                                    string accountMediaPath = mediaPath;

                                    if (accountIndex > 0 && !string.IsNullOrWhiteSpace(accountPattern))
                                    {
                                        string targetPattern = $"compte {accountIndex + 1}";

                                        // Remplacer TOUTES les occurrences de "compte X" par "compte Y"
                                        // Cela gère à la fois le dossier ET le nom de fichier
                                        // Ex: Destination\LunaChica\compte 1\compte 1 photo7.jpg
                                        //  → Destination\LunaChica\compte 2\compte 2 photo7.jpg
                                        accountMediaPath = System.Text.RegularExpressions.Regex.Replace(
                                            baseMediaPath,
                                            System.Text.RegularExpressions.Regex.Escape(accountPattern),
                                            targetPattern,
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                        );

                                        // Vérifier que le fichier existe
                                        if (!File.Exists(accountMediaPath))
                                        {
                                            LogToUI($"[Schedule]   ⚠ File not found for {profile.Name}: {accountMediaPath}");
                                            LogToUI($"[Schedule]   → Using original path: {baseMediaPath}");
                                            accountMediaPath = baseMediaPath;
                                        }
                                        else
                                        {
                                            LogToUI($"[Schedule]   ✓ Mapped to: {Path.GetFileName(accountMediaPath)}");
                                        }
                                    }

                                    var task = new ScheduledTask
                                    {
                                        Date = taskDate,
                                        Platform = platform,
                                        Account = profile.Name,
                                        Activity = activity,
                                        MediaPath = accountMediaPath,
                                        Description = description,
                                        Executed = false
                                    };
                                    newTasks.Add(task);

                                    if (accountIndex > 0)
                                    {
                                        LogToUI($"[Schedule]   → {profile.Name} scheduled at {taskDate:HH:mm} (+{(taskDate - date).TotalMinutes:F0}min)");
                                    }

                                    accountIndex++;
                                }
                            }
                        }
                        catch (Exception exLine)
                        {
                            LogToUI($"[Schedule] Line {lineNumber} parse error: {exLine.Message}");
                        }

                        lineNumber++;
                    }

                    // Conserver l'état Executed des tâches déjà chargées
                    foreach (var t in newTasks)
                    {
                        var old = _tasks.FirstOrDefault(o =>
                            o.Date == t.Date &&
                            o.Platform.Equals(t.Platform, StringComparison.OrdinalIgnoreCase) &&
                            o.Account.Equals(t.Account, StringComparison.OrdinalIgnoreCase) &&
                            o.Activity.Equals(t.Activity, StringComparison.OrdinalIgnoreCase));
                        if (old != null) t.Executed = old.Executed;
                    }

                    _tasks = newTasks.OrderBy(t => t.Date).ToList();

                    var now = DateTime.Now;
                    var pending = _tasks.Where(t => !t.Executed && t.Date > now).ToList();
                    var due = _tasks.Where(t => !t.Executed && t.Date <= now).ToList();

                    LogToUI($"[Schedule] ✓ Loaded {_tasks.Count} tasks");
                    LogToUI($"[Schedule]   → {due.Count} ready to execute NOW");
                    LogToUI($"[Schedule]   → {pending.Count} scheduled for later");

                    var next = pending.FirstOrDefault();
                    if (next != null)
                    {
                        var timeUntil = next.Date - now;
                        LogToUI($"[Schedule] Next: in {timeUntil.TotalMinutes:F1} min: {next.Date:HH:mm} | {next.Platform}/{next.Account} | {next.Activity}");
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] CSV loading error: {ex.Message}");
                }
            }
        }

        // ✅ NOUVELLE MÉTHODE: Appliquer les offsets automatiques
        private void ApplyGroupOffsets(List<ScheduledTask> tasks)
        {
            var rand = new Random();

            // Grouper par (Date exacte, Activity == "target")
            var targetGroups = tasks
                .Where(t => t.Activity == "target")
                .GroupBy(t => new { t.Date })
                .ToList();

            foreach (var dateGroup in targetGroups)
            {
                // Sous-grouper par groupe de profil
                var groupsByProfile = dateGroup
                    .GroupBy(t => GetGroupName(t.Account))
                    .Where(g => g.Count() > 1)  // Seulement si plusieurs comptes dans le même groupe
                    .ToList();

                foreach (var profileGroup in groupsByProfile)
                {
                    var groupTasks = profileGroup.OrderBy(t => t.Account).ToList();

                    LogToUI($"[GROUP OFFSET] Found {groupTasks.Count} accounts in group '{profileGroup.Key}' at {dateGroup.Key:HH:mm}");

                    for (int i = 1; i < groupTasks.Count; i++)
                    {
                        // Décaler de 3-5 minutes par compte (cumulatif)
                        int offsetMinutes = i * rand.Next(3, 6);
                        groupTasks[i].Date = groupTasks[i].Date.AddMinutes(offsetMinutes);

                        LogToUI($"[GROUP OFFSET]   {groupTasks[i].Account}: +{offsetMinutes}min → {groupTasks[i].Date:HH:mm}");
                    }
                }
            }
        }

        // ✅ NOUVELLE MÉTHODE: Récupérer le nom du groupe d'un compte
        private string GetGroupName(string accountName)
        {
            try
            {
                var profiles = profileService.LoadProfiles();
                var profile = profiles.FirstOrDefault(p => p.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));

                return !string.IsNullOrWhiteSpace(profile?.GroupName)
                    ? profile.GroupName
                    : accountName;
            }
            catch
            {
                return accountName;
            }
        }

        private async Task SchedulerLoop(CancellationToken token)
        {
            LogToUI("[Schedule] Scheduler loop started - Checking every 2s");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token);
                    var now = DateTime.Now;

                    List<ScheduledTask> tasksToRun;
                    lock (_lockObj)
                    {
                        tasksToRun = _tasks
                            .Where(t => !t.Executed && t.Date <= now && t.Date > now.AddMinutes(-2))
                            .OrderBy(t => t.Date)
                            .ToList();
                    }

                    foreach (var task in tasksToRun)
                    {
                        if (token.IsCancellationRequested) break;

                        LogToUI($"[Schedule] ⚡ EXEC: {task.Date:HH:mm} | {task.Platform}/{task.Account} | {task.Activity.ToUpperInvariant()}");

                        lock (_lockObj) task.Executed = true;

                        // Exécuter sans bloquer la boucle
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ExecuteTaskAsync(task, token);
                            }
                            catch (Exception ex)
                            {
                                LogToUI($"[Schedule] Task error: {ex.Message}");
                            }
                        }, token);
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Loop error: {ex.Message}");
                }
            }

            LogToUI("[Schedule] Scheduler loop exited");
        }

        private async Task ExecuteTaskAsync(ScheduledTask task, CancellationToken token)
        {
            var baseKey = $"{task.Platform}_{task.Account}".ToLowerInvariant();
            try
            {
                switch (task.Activity)
                {
                    case "stop":
                        await StopAccountActivityAsync(baseKey);
                        break;

                    case "close":
                        var botKey = $"{task.Platform}_{task.Account}".ToLowerInvariant();
                        var storyKey = $"{task.Platform}_{task.Account}_story".ToLowerInvariant();

                        LogToUI($"[Schedule] 🔒 CLOSE requested for {task.Account}");

                        bool hasBotForm = false;
                        bool hasStoryForm = false;

                        lock (_lockObj)
                        {
                            hasBotForm = _activeForms.ContainsKey(botKey);
                            hasStoryForm = _activeForms.ContainsKey(storyKey);
                        }

                        if (hasBotForm)
                        {
                            bool isScriptRunning = false;

                            lock (_lockObj)
                            {
                                if (_activeForms.TryGetValue(botKey, out var form) && form is InstagramBotForm ibf)
                                {
                                    var field = ibf.GetType().GetField("isScriptRunning",
                                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                                    if (field != null)
                                    {
                                        isScriptRunning = (bool)field.GetValue(ibf);
                                    }
                                }
                            }

                            if (isScriptRunning)
                            {
                                LogToUI($"[Schedule] Script running, stopping first...");
                                await StopAccountActivityAsync(botKey);
                                await Task.Delay(1500);
                            }
                            else
                            {
                                LogToUI($"[Schedule] No script running, closing directly...");
                            }

                            await CloseFormForKeyAsync(botKey);
                        }

                        if (hasStoryForm)
                        {
                            await CloseFormForKeyAsync(storyKey);
                        }

                        if (!hasBotForm && !hasStoryForm)
                        {
                            LogToUI($"[Schedule] ℹ️ No windows found for {task.Account}");
                        }
                        else
                        {
                            LogToUI($"[Schedule] ✓ Close completed for {task.Account}");
                        }
                        break;

                    case "story":
                        await ExecuteStoryTaskAsync(task, baseKey, token);
                        break;

                    case "reels":
                        await ExecuteReelsTaskAsync(task, baseKey, token);
                        break;

                    case "home":
                        await ExecuteHomeTaskAsync(task, baseKey, token);
                        break;

                    case "publish":
                        await ExecuteStandardBotTaskAsync(task, baseKey, token);
                        break;

                    case "target":
                        await ExecuteTargetTaskAsync(task, baseKey, token);
                        break;

                    case "dm":
                        await ExecuteReelsTaskAsync(task, baseKey, token);
                        break;

                    case "download":
                        await ExecuteReelsTaskAsync(task, baseKey, token);
                        break;

                    default:
                        LogToUI($"[Schedule] ❌ Unsupported activity: {task.Activity}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ❌ Task error: {ex.Message}");
            }
            finally
            {
                LogNextTask();
            }
        }

        private async Task ExecuteStoryTaskAsync(ScheduledTask task, string baseKey, CancellationToken token)
        {
            var form = await GetOrCreateStoryFormAsync(task.Platform, task.Account);
            if (form == null)
            {
                LogToUI($"[Schedule] ❌ Cannot create story form for {baseKey}");
                return;
            }

            await Task.Delay(5000, token);

            var tcs = new TaskCompletionSource<bool>();
            RunOnUiThread(async () =>
            {
                try
                {
                    if (form is StoryPosterForm spf)
                    {
                        var ok = await spf.PostTodayStoryAsync();
                        LogToUI(ok
                            ? $"[Schedule] ✓ Story posted for {baseKey}"
                            : $"[Schedule] ✗ Story failed for {baseKey}");
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Story error: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });
            await tcs.Task;
        }

        private async Task ExecuteHomeTaskAsync(ScheduledTask task, string baseKey, CancellationToken token)
        {
            var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
            if (botForm == null)
            {
                LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                return;
            }

            LogToUI($"[Schedule] Waiting for services...");

            if (botForm is InstagramBotForm ig)
            {
                int retries = 0;
                while (retries < 20 && !ig.AreServicesReady)
                {
                    await Task.Delay(500, token);
                    retries++;
                }

                if (!ig.AreServicesReady)
                {
                    LogToUI($"[Schedule] ❌ Services not ready after 10s");
                    return;
                }

                LogToUI($"[Schedule] ✓ Services ready - Starting Home scroll");
            }

            var tcs = new TaskCompletionSource<bool>();
            RunOnUiThread(() =>
            {
                try
                {
                    ExecuteActivityOnForm(botForm, "home");
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Execution error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            });

            await tcs.Task;
            LogToUI($"[Schedule] ✓ Home task started: {baseKey}");
        }

        private async Task ExecuteStandardBotTaskAsync(ScheduledTask task, string baseKey, CancellationToken token)
        {
            var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
            if (botForm == null)
            {
                LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                return;
            }

            LogToUI($"[Schedule] Waiting for services...");

            if (botForm is InstagramBotForm ig)
            {
                int retries = 0;
                while (retries < 20 && !ig.AreServicesReady)
                {
                    await Task.Delay(500, token);
                    retries++;
                }

                if (!ig.AreServicesReady)
                {
                    LogToUI($"[Schedule] ❌ Services not ready after 10s");
                    return;
                }

                LogToUI($"[Schedule] ✓ Services ready");
            }

            var tcs = new TaskCompletionSource<bool>();
            RunOnUiThread(() =>
            {
                try
                {
                    ExecuteActivityOnForm(botForm, task.Activity);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Execution error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            });

            await tcs.Task;
            LogToUI($"[Schedule] ✓ Task started: {baseKey} - {task.Activity}");
        }

        private async Task ExecuteReelsTaskAsync(ScheduledTask task, string baseKey, CancellationToken token)
        {
            var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
            if (botForm == null)
            {
                LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                return;
            }

            LogToUI($"[Schedule] Waiting for services...");

            if (botForm is InstagramBotForm ig)
            {
                int retries = 0;
                while (retries < 20 && !ig.AreServicesReady)
                {
                    await Task.Delay(500, token);
                    retries++;
                }

                if (!ig.AreServicesReady)
                {
                    LogToUI($"[Schedule] ❌ Services not ready after 10s");
                    return;
                }

                LogToUI($"[Schedule] ✓ Services ready");

                // ✅ RÉCUPÉRER LE PROFILE
                var profiles = profileService.LoadProfiles();
                var profile = profiles.FirstOrDefault(p => p.Name.Equals(task.Account, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    LogToUI($"[Schedule] ❌ Profile not found: {task.Account}");
                    return;
                }

                // ✅ RÉCUPÉRER WEBVIEW ET LOG
                var webViewField = ig.GetType().GetField("webView",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var logField = ig.GetType().GetField("logTextBox",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (webViewField?.GetValue(ig) is WebView2 webView && logField?.GetValue(ig) is TextBox logBox)
                {
                    // ✅ CRÉER LE SERVICE REELS
                    var reelsService = new ReelsService(webView, logBox, ig, profile);

                    // ✅ PUBLIER LE REEL
                    bool success = await reelsService.PublishScheduledReelAsync(token);

                    LogToUI(success
                        ? $"[Schedule] ✓ Reel published for {baseKey}"
                        : $"[Schedule] ✗ Reel failed for {baseKey}");
                }
                else
                {
                    LogToUI($"[Schedule] ❌ Cannot access WebView2/Log for {baseKey}");
                }
            }
        }

        private async Task ExecuteTargetTaskAsync(ScheduledTask task, string baseKey, CancellationToken token)
        {
            var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
            if (botForm == null)
            {
                LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                return;
            }

            LogToUI($"[Schedule] Waiting for services...");

            var tcs = new TaskCompletionSource<bool>();
            int retries = 0;

            RunOnUiThread(async () =>
            {
                try
                {
                    if (botForm is InstagramBotForm ig)
                    {
                        while (retries < 20 && !ig.AreServicesReady)
                        {
                            await Task.Delay(500, token);
                            retries++;
                        }

                        if (!ig.AreServicesReady)
                        {
                            LogToUI($"[Schedule] ❌ Services timeout");
                            tcs.TrySetResult(false);
                            return;
                        }

                        var targetServiceField = ig.GetType().GetField("targetService",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (targetServiceField?.GetValue(ig) is TargetService targetService)
                        {
                            string customPath = !string.IsNullOrWhiteSpace(task.MediaPath) ? task.MediaPath : null;
                            LogToUI(customPath != null
                                ? $"[Schedule] ✓ Target with custom file: {customPath}"
                                : "[Schedule] ✓ Target with default file");

                            await targetService.RunAsync(token, customPath);
                            LogToUI($"[Schedule] ✓ Target completed for {baseKey}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Target error: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });

            await tcs.Task;
        }

        // ---------------------------
        // Forms Management
        // ---------------------------
        private async Task<Form> GetOrCreateBotFormAsync(string platform, string account)
        {
            var key = $"{platform}_{account}".ToLowerInvariant();

            Form existing = null;
            lock (_lockObj)
            {
                if (_activeForms.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
                {
                    RunOnUiThread(() =>
                    {
                        if (existing.WindowState == FormWindowState.Minimized)
                            existing.WindowState = FormWindowState.Normal;
                        existing.BringToFront();
                    });
                    return existing;
                }
            }

            var profiles = profileService.LoadProfiles();
            var profile = profiles.FirstOrDefault(p => p.Name.Equals(account, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                LogToUI($"[Schedule] ❌ Profile not found: {account}");
                return null;
            }

            var tcs = new TaskCompletionSource<Form>();
            RunOnUiThread(() =>
            {
                try
                {
                    if (!platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
                    {
                        LogToUI($"[Schedule] ⚠ Platform not implemented: {platform}");
                        tcs.TrySetResult(null);
                        return;
                    }

                    var form = new InstagramBotForm(profile);
                    form.Text = profile.Name;

                    form.FormClosed += (s, e) =>
                    {
                        lock (_lockObj)
                        {
                            if (_activeForms.ContainsKey(key))
                            {
                                _activeForms.Remove(key);
                                LogToUI($"[Schedule] Bot window closed: {key}");
                            }
                        }
                    };

                    lock (_lockObj)
                    {
                        _activeForms[key] = form;
                        LogToUI($"[Schedule] ✓ Registered form '{key}' (total forms: {_activeForms.Count})");
                    }

                    form.Show();
                    form.BringToFront();
                    LogToUI($"[Schedule] ✓ Bot window shown for {key}");
                    tcs.TrySetResult(form);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Bot form error: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }

        private async Task<Form> GetOrCreateStoryFormAsync(string platform, string account)
        {
            var key = $"{platform}_{account}_story".ToLowerInvariant();

            Form existing = null;
            lock (_lockObj)
            {
                if (_activeForms.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
                {
                    RunOnUiThread(() =>
                    {
                        if (existing.WindowState == FormWindowState.Minimized)
                            existing.WindowState = FormWindowState.Normal;
                        existing.BringToFront();
                    });
                    return existing;
                }
            }

            var profiles = profileService.LoadProfiles();
            var profile = profiles.FirstOrDefault(p => p.Name.Equals(account, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                LogToUI($"[Schedule] ❌ Profile not found: {account}");
                return null;
            }

            var tcs = new TaskCompletionSource<Form>();
            RunOnUiThread(() =>
            {
                try
                {
                    if (!platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
                    {
                        LogToUI($"[Schedule] ⚠ Platform not implemented: {platform}");
                        tcs.TrySetResult(null);
                        return;
                    }

                    var form = new StoryPosterForm(profile);
                    form.Text = $"Story - {account}";

                    form.FormClosed += (s, e) =>
                    {
                        lock (_lockObj)
                        {
                            _activeForms.Remove(key);
                        }
                        LogToUI($"[Schedule] Story window closed: {key}");
                    };

                    lock (_lockObj)
                    {
                        _activeForms[key] = form;
                    }

                    form.Show();
                    form.BringToFront();
                    LogToUI($"[Schedule] ✓ Story window shown for {key}");
                    tcs.TrySetResult(form);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Story form error: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }

        private async Task CloseFormForKeyAsync(string key)
        {
            Form form = null;
            lock (_lockObj)
            {
                if (!_activeForms.TryGetValue(key, out form) || form == null || form.IsDisposed)
                {
                    _activeForms.Remove(key);
                    return;
                }
            }

            var tcs = new TaskCompletionSource<bool>();
            RunOnUiThread(() =>
            {
                try
                {
                    LogToUI($"[Schedule] Closing {key}...");

                    if (form is InstagramBotForm ibf)
                    {
                        ibf.ForceClose();
                    }
                    else if (form is StoryPosterForm spf)
                    {
                        spf.Close();
                    }
                    else
                    {
                        form.Close();
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Close error: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });

            await tcs.Task;

            lock (_lockObj)
            {
                _activeForms.Remove(key);
            }

            LogToUI($"[Schedule] ✓ Closed {key}");
        }

        private void LogNextTask()
        {
            lock (_lockObj)
            {
                var now = DateTime.Now;
                var next = _tasks
                    .Where(t => !t.Executed && t.Date > now)
                    .OrderBy(t => t.Date)
                    .FirstOrDefault();

                if (next != null)
                {
                    var timeUntil = next.Date - now;
                    LogToUI($"[Schedule] Next: in {timeUntil.TotalMinutes:F1} min: {next.Date:HH:mm} | {next.Platform}/{next.Account} | {next.Activity}");
                }
                else
                {
                    LogToUI($"[Schedule] No more tasks scheduled");
                }
            }
        }

        private async Task StopAccountActivityAsync(string baseKey)
        {
            LogToUI($"[Schedule] 🛑 STOP requested for {baseKey}");

            Form form = null;
            lock (_lockObj)
            {
                _activeForms.TryGetValue(baseKey, out form);
            }

            if (form != null && !form.IsDisposed)
            {
                RunOnUiThread(() =>
                {
                    try
                    {
                        if (form is InstagramBotForm ig)
                        {
                            bool success = TriggerButton(ig, "stopButton", logErrors: false);
                            if (success)
                                LogToUI($"[Schedule] ✓ Stop triggered for {baseKey}");
                            else
                                LogToUI($"[Schedule] ℹ️ Stop button not available for {baseKey}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"[Schedule] Stop error: {ex.Message}");
                    }
                });
            }
            else
            {
                LogToUI($"[Schedule] ℹ️ No active browser found for {baseKey}.");
            }
        }

        // ---------------------------
        // Actions on InstagramBotForm
        // ---------------------------
        private void ExecuteActivityOnForm(Form form, string activity)
        {
            if (form is InstagramBotForm ig)
            {
                try
                {
                    LogToUI($"[Schedule] Executing {activity}...");

                    // ✅ FIX: Case-insensitive activity matching
                    switch (activity?.Trim().ToLower())
                    {
                        case "target":
                            TriggerButton(ig, "targetButton");
                            break;
                        case "reels":
                        case "scrollreels":  // Alias
                            TriggerButton(ig, "scrollButton");
                            break;
                        case "home":
                        case "scrollhome":  // Alias
                            TriggerButton(ig, "scrollHomeButton");
                            break;
                        case "publish":
                        case "post":  // Alias
                            TriggerButton(ig, "publishButton");
                            break;
                        case "dm":
                        case "message":  // Alias
                            TriggerButton(ig, "dmButton");
                            break;
                        case "download":
                            TriggerButton(ig, "downloadButton");
                            break;
                        case "story":
                            // Story is handled separately, but log it
                            LogToUI($"[Schedule] ℹ️ Story activity requires special handling");
                            break;
                        case "close":
                            // Close is handled separately
                            LogToUI($"[Schedule] ℹ️ Close activity requires special handling");
                            break;
                        default:
                            LogToUI($"[Schedule] ❌ Unknown activity: '{activity}'");
                            LogToUI($"[Schedule] Valid activities: target, reels, home, publish, dm, download, story, close");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ {activity} failed: {ex.Message}");
                }
            }
        }

        private bool TriggerButton(Form form, string buttonFieldName, bool logErrors = true)
        {
            try
            {
                var field = form.GetType().GetField(buttonFieldName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field?.GetValue(form) is Button btn && btn.Enabled && btn.Visible)
                {
                    LogToUI($"[Schedule] ✓ Clicking '{buttonFieldName}'");
                    btn.PerformClick();
                    return true;
                }
                else
                {
                    if (logErrors)
                        LogToUI($"[Schedule] ❌ Button '{buttonFieldName}' not found/disabled");
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (logErrors)
                    LogToUI($"[Schedule] ❌ TriggerButton error: {ex.Message}");
                return false;
            }
        }

        // ---------------------------
        // Utils
        // ---------------------------
        private string[] SplitCSVLine(string line)
        {
            var result = new List<string>();
            var current = "";
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current); current = ""; }
                else current += c;
            }
            result.Add(current);
            return result.ToArray();
        }

        private void RunOnUiThread(Action action)
        {
            try
            {
                if (Application.OpenForms.Count > 0)
                {
                    var main = Application.OpenForms[0];
                    if (main != null && !main.IsDisposed)
                    {
                        if (main.InvokeRequired) main.BeginInvoke(action);
                        else action();
                        return;
                    }
                }

                if (logTextBox != null && !logTextBox.IsDisposed)
                {
                    if (logTextBox.InvokeRequired) logTextBox.BeginInvoke(action);
                    else action();
                    return;
                }

                action();
            }
            catch { /* ignore */ }
        }
        private char DetectCSVSeparator(string firstLine)
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
        private void LogToUI(string message)
        {
            try
            {
                if (logTextBox == null || logTextBox.IsDisposed) return;

                if (logTextBox.InvokeRequired)
                {
                    logTextBox.BeginInvoke(new Action(() =>
                    {
                        logTextBox.AppendText(message + Environment.NewLine);
                        logTextBox.SelectionStart = logTextBox.TextLength;
                        logTextBox.ScrollToCaret();
                    }));
                }
                else
                {
                    logTextBox.AppendText(message + Environment.NewLine);
                    logTextBox.SelectionStart = logTextBox.TextLength;
                    logTextBox.ScrollToCaret();
                }
            }
            catch { /* ignore */ }
        }
    }

    public class ScheduledTask
    {
        public DateTime Date { get; set; }
        public string Platform { get; set; }
        public string Account { get; set; }
        public string Activity { get; set; }
        public string MediaPath { get; set; }
        public string Description { get; set; }
        public bool Executed { get; set; }
    }
}