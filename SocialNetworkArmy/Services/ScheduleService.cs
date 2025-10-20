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
        private const string CSV_PATH = "Data/schedule.csv";

        private readonly TextBox logTextBox;
        private readonly ProfileService profileService;

        private CancellationTokenSource _cts;
        private FileSystemWatcher _fileWatcher;
        private readonly object _lockObj = new object();

        private volatile bool _isRunning = false;

        // Clés:
        //   Bot:   "<Platform>_<Account>"
        //   Story: "<Platform>_<Account>_story"
        private readonly Dictionary<string, Form> _activeForms = new Dictionary<string, Form>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _runningActivities = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private List<ScheduledTask> _tasks = new List<ScheduledTask>();

        public ScheduleService(TextBox logTextBox, ProfileService profileService)
        {
            this.logTextBox = logTextBox;
            this.profileService = profileService;
        }

        public bool IsRunning => _isRunning;

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
            lock (_lockObj)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }

            LogToUI("[Schedule] ========================================");
            LogToUI("[Schedule] Stopping Global Scheduler...");
            LogToUI("[Schedule] ========================================");

            try
            {
                _cts?.Cancel();
                _cts = null;

                _fileWatcher?.Dispose();
                _fileWatcher = null;

                // Fermer toutes les fenêtres actives (bot + story)
                var snapshot = _activeForms.ToArray();
                foreach (var kv in snapshot)
                {
                    await CloseFormForKeyAsync(kv.Key);
                }

                _activeForms.Clear();
                _runningActivities.Clear();

                LogToUI("[Schedule] ✓ All windows closed");
                LogToUI("[Schedule] ✓ Scheduler STOPPED");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] Stop error: {ex.Message}");
            }
        }

        // ---------------------------
        // Core
        // ---------------------------
        private void EnsureCsvExists()
        {
            if (!File.Exists(CSV_PATH))
            {
                LogToUI($"[Schedule] CSV not found at {CSV_PATH}. Creating template...");
                Directory.CreateDirectory(Path.GetDirectoryName(CSV_PATH) ?? ".");
                File.WriteAllText(CSV_PATH, "Date,Plateform,Account,Activity,Path,Post Description\r\n");
                LogToUI("[Schedule] ✓ Template created. Fill it and start again.");
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

                            if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd HH:mm",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: invalid date '{parts[0]}' (format: yyyy-MM-dd HH:mm)");
                                lineNumber++;
                                continue;
                            }

                            var task = new ScheduledTask
                            {
                                Date = date,
                                Platform = parts[1].Trim(),
                                Account = parts[2].Trim(),
                                Activity = parts[3].Trim().ToLowerInvariant(),
                                MediaPath = parts.Length > 4 ? parts[4].Trim() : "",
                                Description = parts.Length > 5 ? parts[5].Trim() : "",
                                Executed = false
                            };

                            var valid = new[] { "publish", "scroll", "target", "dm", "download", "story", "stop", "close" };
                            if (!valid.Contains(task.Activity))
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: unknown activity '{task.Activity}'");
                                lineNumber++;
                                continue;
                            }

                            newTasks.Add(task);
                        }
                        catch (Exception exLine)
                        {
                            LogToUI($"[Schedule] Line {lineNumber} parse error: {exLine.Message}");
                        }

                        lineNumber++;
                    }

                    // Conserver l'état Executed pour les tâches déjà rencontrées
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

                        var baseKey = $"{task.Platform}_{task.Account}";
                        if (_runningActivities.TryGetValue(baseKey, out var busy) && busy)
                        {
                            LogToUI($"[Schedule] ⏭️ Skipping {baseKey} - activity already running");
                            continue;
                        }

                        LogToUI($"[Schedule] ⚡ EXEC: {task.Date:HH:mm} | {task.Platform}/{task.Account} | {task.Activity.ToUpperInvariant()}");

                        lock (_lockObj) task.Executed = true;
                        _runningActivities[baseKey] = true;

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
                            finally
                            {
                                _runningActivities[baseKey] = false;
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
            var baseKey = $"{task.Platform}_{task.Account}";

            switch (task.Activity)
            {
                case "stop":
                    await StopAccountActivityAsync(baseKey);
                    return;

                case "close":
                    await CloseFormForKeyAsync(baseKey);            // ferme bot
                    await CloseFormForKeyAsync(baseKey + "_story"); // ferme story
                    return;

                case "story":
                    {
                        var form = await GetOrCreateStoryFormAsync(task.Platform, task.Account);
                        if (form == null)
                        {
                            LogToUI($"[Schedule] ❌ Cannot create story form for {baseKey}");
                            return;
                        }

                        // Laisse le temps au WebView2
                        await Task.Delay(5000, token);

                        // Lance la publication de la story
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
                                else
                                {
                                    LogToUI($"[Schedule] ❌ Wrong form type: {form.GetType().Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToUI($"[Schedule] ❌ Story exec error: {ex.Message}");
                            }
                            finally
                            {
                                tcs.TrySetResult(true);
                            }
                        });
                        await tcs.Task;
                        return;
                    }

                // autres activités → InstagramBotForm via réflexion (ton comportement existant)
                case "publish":
                case "scroll":
                case "target":
                    {
                        var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
                        if (botForm == null)
                        {
                            LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                            return;
                        }

                        await Task.Delay(1500, token);

                        // Passer le chemin personnalisé via réflexion
                        var tcs = new TaskCompletionSource<bool>();
                        RunOnUiThread(async () =>
                        {
                            try
                            {
                                if (botForm is InstagramBotForm ig)
                                {
                                    // Récupérer le TargetService via réflexion
                                    var targetServiceField = ig.GetType().GetField("targetService",
                                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                                    if (targetServiceField != null)
                                    {
                                        var targetService = targetServiceField.GetValue(ig) as TargetService;

                                        if (targetService != null)
                                        {
                                            string customPath = !string.IsNullOrWhiteSpace(task.MediaPath)
                                                ? task.MediaPath
                                                : null;

                                            if (customPath != null)
                                            {
                                                LogToUI($"[Schedule] ✓ Launching target with custom file: {customPath}");
                                            }
                                            else
                                            {
                                                LogToUI($"[Schedule] ✓ Launching target with default file");
                                            }

                                            // Lancer avec le chemin personnalisé
                                            await targetService.RunAsync(token, customPath);

                                            LogToUI($"[Schedule] ✓ Target completed for {baseKey}");
                                        }
                                        else
                                        {
                                            LogToUI($"[Schedule] ❌ TargetService is null");
                                        }
                                    }
                                    else
                                    {
                                        LogToUI($"[Schedule] ❌ TargetService field not found");
                                    }
                                }
                                else
                                {
                                    LogToUI($"[Schedule] ❌ Form is not InstagramBotForm");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToUI($"[Schedule] ❌ Target exec error: {ex.Message}");
                            }
                            finally
                            {
                                tcs.TrySetResult(true);
                            }
                        });
                        await tcs.Task;
                        return;
                    }
                case "dm":
                case "download":
                    {
                        var botForm = await GetOrCreateBotFormAsync(task.Platform, task.Account);
                        if (botForm == null)
                        {
                            LogToUI($"[Schedule] ❌ Cannot create bot form for {baseKey}");
                            return;
                        }

                        await Task.Delay(1500, token);
                        ExecuteActivityOnForm(botForm, task.Activity);
                        LogToUI($"[Schedule] ✓ Task completed: {baseKey} - {task.Activity}");
                        return;
                    }

                default:
                    LogToUI($"[Schedule] ❌ Unsupported activity: {task.Activity}");
                    return;
            }
        }

        // ---------------------------
        // Forms management
        // ---------------------------
        private async Task<Form> GetOrCreateBotFormAsync(string platform, string account)
        {
            var key = $"{platform}_{account}";

            if (_activeForms.TryGetValue(key, out var existing)
                && existing != null && !existing.IsDisposed)
            {
                RunOnUiThread(() =>
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                        existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                });
                return existing;
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
                        _activeForms.Remove(key);
                        _runningActivities.Remove(key);
                        LogToUI($"[Schedule] Bot window closed: {key}");
                    };

                    _activeForms[key] = form;
                    form.Show();
                    form.BringToFront();
                    LogToUI($"[Schedule] ✓ Bot window shown for {key}");
                    tcs.TrySetResult(form);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Bot form creation error: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }

        private async Task<Form> GetOrCreateStoryFormAsync(string platform, string account)
        {
            var key = $"{platform}_{account}_story";

            if (_activeForms.TryGetValue(key, out var existing)
                && existing != null && !existing.IsDisposed)
            {
                RunOnUiThread(() =>
                {
                    if (existing.WindowState == FormWindowState.Minimized)
                        existing.WindowState = FormWindowState.Normal;
                    existing.BringToFront();
                });
                return existing;
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
                        LogToUI($"[Schedule] ⚠ Platform not implemented for story: {platform}");
                        tcs.TrySetResult(null);
                        return;
                    }

                    var form = new StoryPosterForm(profile);
                    form.Text = $"Story - {account}";

                    form.FormClosed += (s, e) =>
                    {
                        _activeForms.Remove(key);
                        _runningActivities.Remove(key);
                        LogToUI($"[Schedule] Story window closed: {key}");
                    };

                    _activeForms[key] = form;
                    form.Show();
                    form.BringToFront();
                    LogToUI($"[Schedule] ✓ Story window shown for {key}");
                    tcs.TrySetResult(form);
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Story form creation error: {ex.Message}");
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }

        private async Task CloseFormForKeyAsync(string key)
        {
            if (!_activeForms.TryGetValue(key, out var form) || form == null || form.IsDisposed)
            {
                LogToUI($"[Schedule] (close) No active window for {key}");
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            RunOnUiThread(() =>
            {
                try
                {
                    LogToUI($"[Schedule] Closing {key}...");
                    form.Close();
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Close error for {key}: {ex.Message}");
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });
            await tcs.Task;

            _activeForms.Remove(key);
            _runningActivities.Remove(key);
            LogToUI($"[Schedule] ✓ Closed {key}");
        }

        private async Task StopAccountActivityAsync(string baseKey)
        {
            LogToUI($"[Schedule] 🛑 STOP requested for {baseKey}");
            if (_activeForms.TryGetValue(baseKey, out var form) && form != null && !form.IsDisposed)
            {
                RunOnUiThread(() =>
                {
                    try
                    {
                        if (form is InstagramBotForm ig)
                        {
                            TriggerButton(ig, "stopButton");
                            LogToUI($"[Schedule] ✓ Stop triggered for {baseKey}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"[Schedule] Stop error: {ex.Message}");
                    }
                });
            }
            _runningActivities[baseKey] = false;
        }

        // ---------------------------
        // Actions on InstagramBotForm
        // ---------------------------
        private void ExecuteActivityOnForm(Form form, string activity)
        {
            if (form is InstagramBotForm ig)
            {
                LogToUI($"[Schedule] Form type confirmed: InstagramBotForm");
                switch (activity)
                {
                    case "target": TriggerButton(ig, "targetButton"); break;
                    case "scroll": TriggerButton(ig, "scrollButton"); break;
                    case "publish": TriggerButton(ig, "publishButton"); break;
                    case "dm": TriggerButton(ig, "dmButton"); break;
                    case "download": TriggerButton(ig, "downloadButton"); break;
                    default: LogToUI($"[Schedule] ❌ Unknown bot activity: {activity}"); break;
                }
            }
            else
            {
                LogToUI($"[Schedule] ❌ Form is not InstagramBotForm, type: {form.GetType().Name}");
            }
        }

        private void TriggerButton(Form form, string buttonFieldName)
        {
            try
            {
                var formType = form.GetType();
                var field = formType.GetField(buttonFieldName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field == null)
                {
                    var all = formType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                      .Where(f => f.FieldType == typeof(Button))
                                      .Select(f => f.Name);
                    LogToUI($"[Schedule] ❌ Button '{buttonFieldName}' not found. Available: {string.Join(", ", all)}");
                    return;
                }

                if (field.GetValue(form) is Button btn)
                {
                    if (!btn.Enabled) { LogToUI($"[Schedule] ⚠ Button '{buttonFieldName}' disabled"); return; }
                    if (!btn.Visible) { LogToUI($"[Schedule] ⚠ Button '{buttonFieldName}' not visible"); return; }

                    LogToUI($"[Schedule] ✓ Clicking '{buttonFieldName}'");
                    btn.PerformClick();
                }
                else
                {
                    LogToUI($"[Schedule] ❌ Field '{buttonFieldName}' is not a Button or is null");
                }
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ❌ TriggerButton error: {ex.Message}");
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
