using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Forms;

namespace SocialNetworkArmy.Services
{
    public class ScheduleService
    {
        private const string CSV_PATH = "Data/schedule.csv";
        private readonly TextBox logTextBox;
        private readonly ProfileService profileService;
        private CancellationTokenSource _cts;
        private FileSystemWatcher _fileWatcher;
        private bool _isRunning = false;
        private readonly object _lockObj = new object();

        // Track active bot forms per account/platform
        private readonly Dictionary<string, Form> _activeForms = new Dictionary<string, Form>();
        private readonly Dictionary<string, bool> _runningActivities = new Dictionary<string, bool>();

        private List<ScheduledTask> _tasks = new List<ScheduledTask>();

        public ScheduleService(TextBox logTextBox, ProfileService profileService)
        {
            this.logTextBox = logTextBox;
            this.profileService = profileService;
        }

        public bool IsRunning => _isRunning;

        public async Task ToggleAsync()
        {
            if (_isRunning)
            {
                await StopAsync();
            }
            else
            {
                await StartAsync();
            }
        }

        private async Task StartAsync()
        {
            lock (_lockObj)
            {
                if (_isRunning)
                {
                    LogToUI("[Schedule] Already running!");
                    return;
                }
                _isRunning = true;
            }

            LogToUI("[Schedule] ========================================");
            LogToUI("[Schedule] Starting Global Scheduler...");
            LogToUI("[Schedule] ========================================");

            if (!File.Exists(CSV_PATH))
            {
                LogToUI($"[Schedule] ERROR: CSV not found at {CSV_PATH}");
                LogToUI("[Schedule] Creating empty CSV template...");
                Directory.CreateDirectory(Path.GetDirectoryName(CSV_PATH));
                File.WriteAllText(CSV_PATH, "Date,Plateform,Account,Activity,Media Path,Post Description\n");
                LogToUI("[Schedule] ✓ Template created. Add tasks and restart.");
                _isRunning = false;
                return;
            }

            // Load initial tasks
            LoadTasksFromCSV();

            if (_tasks.Count == 0)
            {
                LogToUI("[Schedule] WARNING: No valid tasks found in CSV");
                LogToUI("[Schedule] Scheduler will wait for CSV updates...");
            }

            // Start file watcher
            SetupFileWatcher();

            // Start scheduler loop
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => SchedulerLoop(_cts.Token));

            LogToUI("[Schedule] ✓ Scheduler ACTIVE - Monitoring CSV for changes");
            LogToUI("[Schedule] ✓ Bot windows will open automatically when needed");
        }

        private async Task StopAsync()
        {
            lock (_lockObj)
            {
                if (!_isRunning)
                {
                    return;
                }
                _isRunning = false;
            }

            LogToUI("[Schedule] ========================================");
            LogToUI("[Schedule] Stopping Global Scheduler...");
            LogToUI("[Schedule] ========================================");

            _cts?.Cancel();
            _fileWatcher?.Dispose();
            _fileWatcher = null;

            // Close all active bot forms
            var formsToClose = _activeForms.Values.ToList();
            foreach (var form in formsToClose)
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Invoke(new Action(() =>
                        {
                            LogToUI($"[Schedule] Closing {form.Text}...");
                            form.Close();
                        }));
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Error closing form: {ex.Message}");
                }
            }

            _activeForms.Clear();
            _runningActivities.Clear();

            LogToUI("[Schedule] ✓ All bot windows closed");
            LogToUI("[Schedule] ✓ Scheduler STOPPED");
        }

        private void SetupFileWatcher()
        {
            try
            {
                var fullPath = Path.GetFullPath(CSV_PATH);
                var dir = Path.GetDirectoryName(fullPath);
                var file = Path.GetFileName(fullPath);

                _fileWatcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                DateTime lastReload = DateTime.MinValue;
                _fileWatcher.Changed += (s, e) =>
                {
                    // Debounce: ignore if last reload was less than 2 seconds ago
                    if ((DateTime.Now - lastReload).TotalSeconds < 2)
                        return;

                    lastReload = DateTime.Now;
                    Task.Delay(500).ContinueWith(_ =>
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
                LogToUI($"[Schedule] FileWatcher setup error: {ex.Message}");
            }
        }

        private void LoadTasksFromCSV()
        {
            lock (_lockObj)
            {
                try
                {
                    if (!File.Exists(CSV_PATH))
                    {
                        LogToUI("[Schedule] CSV file not found!");
                        return;
                    }

                    var lines = File.ReadAllLines(CSV_PATH)
                        .Skip(1) // Skip header
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

                    var newTasks = new List<ScheduledTask>();
                    int lineNumber = 2; // Start at 2 (after header)

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
                                LogToUI($"[Schedule] Line {lineNumber} skipped: invalid date format '{parts[0]}'");
                                lineNumber++;
                                continue;
                            }

                            var task = new ScheduledTask
                            {
                                Date = date,
                                Platform = parts[1].Trim(),
                                Account = parts[2].Trim(),
                                Activity = parts[3].Trim().ToLower(),
                                MediaPath = parts.Length > 4 ? parts[4].Trim() : "",
                                Description = parts.Length > 5 ? parts[5].Trim() : "",
                                Executed = false
                            };

                            // Validate activity
                            var validActivities = new[] { "publish", "scroll", "target", "dm", "download", "stop", "close" };
                            if (!validActivities.Contains(task.Activity))
                            {
                                LogToUI($"[Schedule] Line {lineNumber} skipped: unknown activity '{task.Activity}'");
                                lineNumber++;
                                continue;
                            }

                            newTasks.Add(task);
                        }
                        catch (Exception ex)
                        {
                            LogToUI($"[Schedule] Line {lineNumber} parse error: {ex.Message}");
                        }
                        lineNumber++;
                    }

                    // Preserve execution status for existing tasks (avoid re-executing)
                    foreach (var newTask in newTasks)
                    {
                        var existing = _tasks.FirstOrDefault(t =>
                            t.Date == newTask.Date &&
                            t.Platform == newTask.Platform &&
                            t.Account == newTask.Account &&
                            t.Activity == newTask.Activity);

                        if (existing != null)
                        {
                            newTask.Executed = existing.Executed;
                        }
                    }

                    _tasks = newTasks.OrderBy(t => t.Date).ToList();

                    var pendingTasks = _tasks.Where(t => !t.Executed && t.Date > DateTime.Now).ToList();
                    var pastTasks = _tasks.Where(t => !t.Executed && t.Date <= DateTime.Now).ToList();

                    LogToUI($"[Schedule] ✓ Loaded {_tasks.Count} total tasks");
                    LogToUI($"[Schedule]   → {pastTasks.Count} ready to execute NOW");
                    LogToUI($"[Schedule]   → {pendingTasks.Count} scheduled for later");

                    var next = pendingTasks.FirstOrDefault();
                    if (next != null)
                    {
                        var timeUntil = next.Date - DateTime.Now;
                        LogToUI($"[Schedule] Next task in {timeUntil.TotalMinutes:F1} min:");
                        LogToUI($"[Schedule]   → {next.Date:HH:mm} | {next.Platform}/{next.Account} | {next.Activity}");
                    }
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] CSV loading error: {ex.Message}");
                }
            }
        }

        private string[] SplitCSVLine(string line)
        {
            // Simple CSV parser handling commas in quotes
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

        private async Task SchedulerLoop(CancellationToken token)
        {
            LogToUI("[Schedule] Scheduler loop started - Checking every 2 seconds");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token); // Check every 2 seconds

                    var now = DateTime.Now;
                    List<ScheduledTask> tasksToExecute;

                    lock (_lockObj)
                    {
                        tasksToExecute = _tasks
                           .Where(t => !t.Executed && t.Date <= now && t.Date > now.AddMinutes(-2))
                            .OrderBy(t => t.Date)
                            .ToList();
                    }

                    foreach (var task in tasksToExecute)
                    {
                        if (token.IsCancellationRequested) break;

                        var key = GetTaskKey(task);

                        // Check if already running activity for this account/platform
                        if (_runningActivities.ContainsKey(key) && _runningActivities[key])
                        {
                            LogToUI($"[Schedule] ⏭️ Skipping {key} - activity already in progress");
                            continue;
                        }

                        LogToUI($"[Schedule] ⚡ EXECUTING: {task.Date:HH:mm} | {task.Platform}/{task.Account} | {task.Activity.ToUpper()}");

                        lock (_lockObj)
                        {
                            task.Executed = true;
                        }

                        _ = Task.Run(async () => await ExecuteTaskAsync(task, token), token);
                    }
                }
                catch (TaskCanceledException)
                {
                    LogToUI("[Schedule] Scheduler loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Loop error: {ex.Message}");
                    await Task.Delay(5000, token); // Wait before retry
                }
            }

            LogToUI("[Schedule] Scheduler loop exited");
        }

        private async Task ExecuteTaskAsync(ScheduledTask task, CancellationToken token)
        {
            var key = GetTaskKey(task);

            try
            {
                if (task.Activity == "stop")
                {
                    await StopAccountActivityAsync(key);
                    return;
                }

                if (task.Activity == "close")
                {
                    await CloseAccountFormAsync(key);
                    return;
                }

                // Mark as running
                _runningActivities[key] = true;

                // Get or create bot form
                var form = await GetOrCreateBotFormAsync(task.Platform, task.Account);
                if (form == null)
                {
                    LogToUI($"[Schedule] ❌ ERROR: Cannot create form for {key}");
                    _runningActivities[key] = false;
                    return;
                }

                // Wait for form to be ready
                await Task.Delay(2000, token);

                // Execute activity via the form's service
                await ExecuteActivityOnFormAsync(form, task, token);

                LogToUI($"[Schedule] ✓ Task completed: {key} - {task.Activity}");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ❌ Task execution error ({key}): {ex.Message}");
            }
            finally
            {
                if (_runningActivities.ContainsKey(key))
                {
                    _runningActivities[key] = false;
                }
            }
        }

        private async Task<Form> GetOrCreateBotFormAsync(string platform, string account)
        {
            var key = $"{platform}_{account}";

            // Check if form already exists and is valid
            if (_activeForms.ContainsKey(key) && _activeForms[key] != null && !_activeForms[key].IsDisposed)
            {
                var existingForm = _activeForms[key];
                LogToUI($"[Schedule] ✓ Using existing window for {account}");

                try
                {
                    existingForm.Invoke(new Action(() =>
                    {
                        if (existingForm.WindowState == FormWindowState.Minimized)
                        {
                            existingForm.WindowState = FormWindowState.Normal;
                            LogToUI($"[Schedule] Window restored for {account}");
                        }
                        existingForm.BringToFront();
                    }));
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] Error restoring window: {ex.Message}");
                }

                return existingForm;
            }

            // Load profile
            var profiles = profileService.LoadProfiles();
            var profile = profiles.FirstOrDefault(p => p.Name == account);

            if (profile == null)
            {
                LogToUI($"[Schedule] ❌ Profile not found: {account}");
                LogToUI($"[Schedule] Create the profile '{account}' in MainForm first!");
                return null;
            }

            Form form = null;
            var formReadyTcs = new TaskCompletionSource<bool>();

            // Create form based on platform - IMPORTANT: Do this on UI thread
            if (platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
            {
                LogToUI($"[Schedule] 🚀 Creating Instagram bot window for {account}...");

                try
                {
                    // Create form on UI thread using logTextBox
                    logTextBox.Invoke(new Action(() =>
                    {
                        try
                        {
                            form = new InstagramBotForm(profile);
                        }
                        catch (Exception ex)
                        {
                            LogToUI($"[Schedule] ❌ Error creating form: {ex.Message}");
                        }
                    }));
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Error invoking form creation: {ex.Message}");
                }

                if (form == null)
                {
                    LogToUI($"[Schedule] ❌ Failed to create form for {account}");
                    return null;
                }
            }
            else if (platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
            {
                LogToUI($"[Schedule] ⚠️ TikTok support not yet implemented");
                return null;
            }
            else
            {
                LogToUI($"[Schedule] ❌ Unknown platform: {platform}");
                return null;
            }

            if (form != null)
            {
                // Handle form closing
                form.FormClosed += (s, e) =>
                {
                    LogToUI($"[Schedule] Window closed: {account}");
                    _activeForms.Remove(key);
                    _runningActivities.Remove(key);
                };

                // Handle form shown (to know when it's ready)
                form.Shown += (s, e) =>
                {
                    LogToUI($"[Schedule] ✓ Window fully loaded for {account}");
                    formReadyTcs.TrySetResult(true);
                };

                _activeForms[key] = form;

                // Show form on UI thread
                try
                {
                    logTextBox.Invoke(new Action(() =>
                    {
                        form.Show();
                        LogToUI($"[Schedule] ✓ Window shown for {account}");
                    }));
                }
                catch (Exception ex)
                {
                    LogToUI($"[Schedule] ❌ Error showing form: {ex.Message}");
                    return null;
                }

                // Wait for form to be fully shown OR timeout after 5 seconds
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(formReadyTcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    LogToUI($"[Schedule] ⚠️ Form shown but waiting for full initialization...");
                }

                // Wait additional time for WebView2 initialization
                LogToUI($"[Schedule] Waiting for WebView2 initialization (5 seconds)...");
                await Task.Delay(5000);

                LogToUI($"[Schedule] ✓ Window ready for {account}");
            }

            return form;
        }

        private async Task ExecuteActivityOnFormAsync(Form form, ScheduledTask task, CancellationToken token)
        {
            if (form.IsDisposed || form == null)
            {
                LogToUI($"[Schedule] ❌ Form is disposed, cannot execute activity");
                return;
            }

            LogToUI($"[Schedule] Preparing to execute {task.Activity} on {task.Account}...");

            try
            {
                if (form.InvokeRequired)
                {
                    form.Invoke(new Action(() =>
                    {
                        ExecuteActivityOnFormInternal(form, task);
                    }));
                }
                else
                {
                    ExecuteActivityOnFormInternal(form, task);
                }

                // Give some time for the activity to start
                await Task.Delay(1000, token);
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ❌ Activity execution error: {ex.Message}");
                LogToUI($"[Schedule] Stack trace: {ex.StackTrace}");
            }
        }

        private void ExecuteActivityOnFormInternal(Form form, ScheduledTask task)
        {
            if (form is InstagramBotForm instagramForm)
            {
                LogToUI($"[Schedule] Form type confirmed: InstagramBotForm");

                switch (task.Activity)
                {
                    case "target":
                        LogToUI($"[Schedule] Triggering TARGET button...");
                        TriggerButton(instagramForm, "targetButton");
                        break;

                    case "scroll":
                        LogToUI($"[Schedule] Triggering SCROLL button...");
                        TriggerButton(instagramForm, "scrollButton");
                        break;

                    case "publish":
                        LogToUI($"[Schedule] Triggering PUBLISH button...");
                        TriggerButton(instagramForm, "publishButton");
                        break;

                    case "dm":
                        LogToUI($"[Schedule] Triggering DM button...");
                        TriggerButton(instagramForm, "dmButton");
                        break;

                    case "download":
                        LogToUI($"[Schedule] Triggering DOWNLOAD button...");
                        TriggerButton(instagramForm, "downloadButton");
                        break;

                    default:
                        LogToUI($"[Schedule] ❌ Unknown activity: {task.Activity}");
                        break;
                }
            }
            else
            {
                LogToUI($"[Schedule] ❌ Form is not InstagramBotForm, type: {form.GetType().Name}");
            }
        }

        private void TriggerButton(Form form, string buttonName)
        {
            try
            {
                LogToUI($"[Schedule] Looking for button: {buttonName}");

                var formType = form.GetType();
                var buttonField = formType.GetField(buttonName,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (buttonField == null)
                {
                    LogToUI($"[Schedule] ❌ Button field '{buttonName}' not found");

                    // Try to list all fields for debugging
                    var allFields = formType.GetFields(
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)
                        .Where(f => f.FieldType == typeof(Button))
                        .Select(f => f.Name)
                        .ToList();

                    LogToUI($"[Schedule] Available button fields: {string.Join(", ", allFields)}");
                    return;
                }

                var button = buttonField.GetValue(form) as Button;

                if (button == null)
                {
                    LogToUI($"[Schedule] ❌ Button '{buttonName}' is null");
                    return;
                }

                LogToUI($"[Schedule] Button found - Enabled: {button.Enabled}, Visible: {button.Visible}");

                if (!button.Enabled)
                {
                    LogToUI($"[Schedule] ⚠️ Button '{buttonName}' is DISABLED - waiting for WebView2 initialization");
                    LogToUI($"[Schedule] TIP: The browser might still be loading. Try increasing wait time.");
                    return;
                }

                if (!button.Visible)
                {
                    LogToUI($"[Schedule] ⚠️ Button '{buttonName}' is not visible");
                    return;
                }

                LogToUI($"[Schedule] ✓ Clicking button '{buttonName}'...");
                button.PerformClick();
                LogToUI($"[Schedule] ✓ Button clicked successfully!");
            }
            catch (Exception ex)
            {
                LogToUI($"[Schedule] ❌ Error triggering '{buttonName}': {ex.Message}");
                LogToUI($"[Schedule] Stack trace: {ex.StackTrace}");
            }
        }
        private async Task CloseAccountFormAsync(string key)
        {
            LogToUI($"[Schedule] ❌ CLOSE requested for {key}");

            if (_activeForms.ContainsKey(key))
            {
                var form = _activeForms[key];
                if (form != null && !form.IsDisposed)
                {
                    form.Invoke(new Action(() =>
                    {
                        LogToUI($"[Schedule] Closing form for {key}...");
                        form.Close();
                    }));

                    _activeForms.Remove(key);
                    _runningActivities.Remove(key);
                    LogToUI($"[Schedule] ✓ Form closed for {key}");
                }
            }
            else
            {
                LogToUI($"[Schedule] ⚠️ No active window found for {key}");
            }
        }

        private async Task StopAccountActivityAsync(string key)
        {
            LogToUI($"[Schedule] 🛑 STOP requested for {key}");

            if (_activeForms.ContainsKey(key))
            {
                var form = _activeForms[key];
                if (form != null && !form.IsDisposed)
                {
                    form.Invoke(new Action(() =>
                    {
                        if (form is InstagramBotForm instagramForm)
                        {
                            TriggerButton(instagramForm, "stopButton");
                            LogToUI($"[Schedule] ✓ Stop button triggered for {key}");
                        }
                    }));
                }
            }
            else
            {
                LogToUI($"[Schedule] ⚠️ No active window found for {key}");
            }

            _runningActivities[key] = false;
        }

        private string GetTaskKey(ScheduledTask task)
        {
            return $"{task.Platform}_{task.Account}";
        }

        private void LogToUI(string message)
        {
            try
            {
                if (logTextBox.InvokeRequired)
                {
                    logTextBox.Invoke(new Action(() =>
                    {
                        logTextBox.AppendText($"{message}\r\n");
                        logTextBox.SelectionStart = logTextBox.Text.Length;
                        logTextBox.ScrollToCaret();
                    }));
                }
                else
                {
                    logTextBox.AppendText($"{message}\r\n");
                    logTextBox.SelectionStart = logTextBox.Text.Length;
                    logTextBox.ScrollToCaret();
                }
            }
            catch
            {
                // Ignore if UI is disposed
            }
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
