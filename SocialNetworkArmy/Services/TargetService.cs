using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class TargetService
    {
        private readonly NavigationService navigationService;
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly Random rand = new Random();
        private readonly HumanBehaviorSimulator humanBehavior;

        public TargetService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.navigationService = new NavigationService(webView, logTextBox);
            this.humanBehavior = new HumanBehaviorSimulator(webView);
        }


        private void MarkTargetAsDone(string target, string doneTargetsPath, string reason = "")
        {
            try
            {
                // Vérifier si déjà présent pour éviter les doublons
                var existingDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(doneTargetsPath))
                {
                    existingDone = new HashSet<string>(
                        File.ReadAllLines(doneTargetsPath)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(line => line.Trim()),
                        StringComparer.OrdinalIgnoreCase
                    );
                }

                if (!existingDone.Contains(target))
                {
                    File.AppendAllText(doneTargetsPath, target + Environment.NewLine);
                    string emoji = reason.Contains("succès") || reason.Contains("success") ? "✓" : "⚠️";
                    string fileName = Path.GetFileName(doneTargetsPath); // ✅ Récupérer le vrai nom
                    logTextBox.AppendText($"[DONE_TARGETS] {emoji} Added to {fileName}: {target} {reason}\r\n");
                }
                else
                {
                    string fileName = Path.GetFileName(doneTargetsPath); // ✅ Récupérer le vrai nom
                    logTextBox.AppendText($"[DONE_TARGETS] ℹ️ Already present in {fileName}: {target}\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[DONE_TARGETS ERROR] Unable to add {target}: {ex.Message}\r\n");
            }
        }

        private async Task<bool> ClickReelsTabAsync(string username, string lang, CancellationToken token = default)
        {
            logTextBox.AppendText("[NAV] Clicking Reels tab...\r\n");

            var reelsScript = $@"
(function(){{
  try{{
    var reelsEl = document.querySelector('a[href=""/{username}/reels/""]');
    if (!reelsEl) {{
      reelsEl = document.querySelector('div[role=""tablist""] div:nth-child(2)'); // Fallback to second tab if href not found
    }}
    if (!reelsEl) return 'NO_REELS_ELEMENT';
    
    if (reelsEl.offsetWidth === 0 || reelsEl.offsetHeight === 0) {{
      return 'REELS_NOT_VISIBLE';
    }}
    
    reelsEl.scrollIntoView({{behavior:'smooth', block:'center'}});
    
    var rect = reelsEl.getBoundingClientRect();
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    var clientX = rect.left + offsetX;
    var clientY = rect.top + offsetY;
    
    // Simulate mouse approach: 3-5 move events towards the target
    var startX = clientX + (Math.random() * 100 - 50);  // Start offset
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      reelsEl.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    
    reelsEl.dispatchEvent(new MouseEvent('mouseenter', opts));
    reelsEl.dispatchEvent(new MouseEvent('mouseover', opts));
    reelsEl.dispatchEvent(new MouseEvent('mousedown', opts));
    reelsEl.dispatchEvent(new MouseEvent('mouseup', opts));
    reelsEl.dispatchEvent(new MouseEvent('click', opts));
    reelsEl.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    return 'REELS_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY);
  }} catch(e){{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";

            var reelsResult = await webView.ExecuteScriptAsync(reelsScript);
            logTextBox.AppendText($"[NAV] Reels tab click: {reelsResult}\r\n");

            if (!reelsResult.Contains("REELS_CLICKED"))
            {
                logTextBox.AppendText("[NAV] ✗ Failed to click Reels tab\r\n");
                return false;
            }

            // Wait for reels page to load
            await Task.Delay(rand.Next(2000, 4000), token);

            // Check if on reels feed
            var checkReels = await webView.ExecuteScriptAsync(@"
(function(){
  var url = window.location.href;
  return url.includes('/reels/') ? 'true' : 'false';
})()");

            if (!TargetJavaScriptHelper.JsBoolIsTrue(checkReels))
            {
                logTextBox.AppendText("[NAV] ✗ Reels tab did not load\r\n");
                return false;
            }

            logTextBox.AppendText("[NAV] ✓ Reels tab loaded\r\n");
            return true;
        }

        private async Task<bool> CloseReelModalAsync(string lang, CancellationToken token = default)
        {
            logTextBox.AppendText("[NAV] Closing reel modal...\r\n");

            string closeLabel = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Fermer" : "Close";

            var closeScript = $@"
(function(){{
  try{{
    var closeSvg = document.querySelector('svg[aria-label=""{closeLabel}""]');
    if (!closeSvg) return 'NO_CLOSE_ELEMENT';
    
    var closeEl = closeSvg.closest('button, div[role=""button""]');
    if (!closeEl) return 'NO_CLOSE_PARENT';
    
    var rect = closeEl.getBoundingClientRect();
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    var clientX = rect.left + offsetX;
    var clientY = rect.top + offsetY;
    
    // Simulate mouse approach: 3-5 move events towards the target
    var startX = clientX + (Math.random() * 100 - 50);  // Start offset
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      closeEl.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    
    closeEl.dispatchEvent(new MouseEvent('mousedown', opts));
    closeEl.dispatchEvent(new MouseEvent('mouseup', opts));
    closeEl.dispatchEvent(new MouseEvent('click', opts));
    
    return 'CLOSE_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY);
  }} catch(e){{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";

            var closeResult = await webView.ExecuteScriptAsync(closeScript);
            logTextBox.AppendText($"[NAV] Close modal result: {closeResult}\r\n");

            if (!closeResult.Contains("CLOSE_CLICKED"))
            {
                logTextBox.AppendText("[NAV] ✗ Failed to close modal\r\n");
                return false;
            }

            await Task.Delay(rand.Next(1500, 2500), token);

            // Check if modal is closed
            var checkClosed = await webView.ExecuteScriptAsync(@"
(function(){
  return !document.querySelector('div[role=""dialog""]') ? 'true' : 'false';
})()");

            if (!TargetJavaScriptHelper.JsBoolIsTrue(checkClosed))
            {
                logTextBox.AppendText("[NAV] ✗ Modal did not close\r\n");
                return false;
            }

            logTextBox.AppendText("[NAV] ✓ Modal closed\r\n");
            return true;
        }
        private async Task<bool> CheckInstagramLoginAsync()
        {
            try
            {
                string script = @"
            (function() {
                try {
                    const hasCreate = document.querySelector('a[href*=""/create""]') !== null;
                    const hasDirect = document.querySelector('a[href*=""/direct/""]') !== null;
                    return hasCreate || hasDirect;
                } catch(e) {
                    return false;
                }
            })();
        ";

                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                return result.Trim().ToLower() == "true";
            }
            catch
            {
                return false;
            }
        }
        // Dans TargetService.cs, remplacer la méthode GetScheduledPathForToday() par ceci :

        private string GetScheduledPathForToday()
        {
            try
            {
                var schedulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "schedule.csv");
                if (!File.Exists(schedulePath))
                {
                    logTextBox.AppendText("[SCHEDULE] schedule.csv not found\r\n");
                    return null;
                }

                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var lines = File.ReadAllLines(schedulePath).Skip(1); // Skip header

                // ✅ Charger tous les profils une seule fois
                var profilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "profiles.json");
                List<Profile> allProfiles = new List<Profile>();

                if (File.Exists(profilesPath))
                {
                    try
                    {
                        var json = File.ReadAllText(profilesPath);
                        allProfiles = System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(json);
                    }
                    catch (Exception ex)
                    {
                        logTextBox.AppendText($"[SCHEDULE ERROR] Failed to load profiles: {ex.Message}\r\n");
                        return null;
                    }
                }

                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length < 5) continue;

                    var dateStr = parts[0].Trim();
                    var platform = parts[1].Trim();
                    var accountOrGroup = parts[2].Trim();  // ✅ Renommé pour clarté
                    var activity = parts[3].Trim();
                    var path = parts[4].Trim();

                    // Extraire juste la date (ignorer l'heure)
                    string dateOnly = dateStr.Contains(" ") ? dateStr.Split(' ')[0] : dateStr;

                    // Vérifier que c'est bien aujourd'hui, Instagram et target
                    if (dateOnly != today ||
                        !platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase) ||
                        !activity.Equals("target", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    // ✅ DÉTECTION AUTOMATIQUE : Compte ou Groupe

                    // 1) Chercher si c'est un compte exact
                    var singleProfile = allProfiles.FirstOrDefault(p =>
                        p.Name.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase));

                    if (singleProfile != null)
                    {
                        // C'est un compte individuel - vérifier si c'est LE BON profil
                        if (singleProfile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            logTextBox.AppendText($"[SCHEDULE] Found scheduled path for account '{profile.Name}': {path}\r\n");
                            return path;
                        }
                    }
                    else
                    {
                        // 2) Chercher si c'est un groupe
                        var groupProfiles = allProfiles
                            .Where(p => !string.IsNullOrWhiteSpace(p.GroupName) &&
                                       p.GroupName.Equals(accountOrGroup, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (groupProfiles.Any())
                        {
                            // C'est un groupe - vérifier si le profil actuel en fait partie
                            var isInGroup = groupProfiles.Any(p =>
                                p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

                            if (isInGroup)
                            {
                                logTextBox.AppendText($"[SCHEDULE] Found scheduled path for group '{accountOrGroup}' (contains '{profile.Name}'): {path}\r\n");
                                return path;
                            }
                        }
                    }
                }

                logTextBox.AppendText("[SCHEDULE] No scheduled path found for this account/group today\r\n");
                return null;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[SCHEDULE ERROR] {ex.Message}\r\n");
                Logger.LogError($"GetScheduledPathForToday: {ex}");
                return null;
            }
        }
        private string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            // Nettoyer le chemin s'il commence par \ ou /
            if (path.StartsWith("\\") || path.StartsWith("/"))
            {
                path = path.TrimStart('\\', '/');
            }

            // Si ce n'est pas un chemin absolu, le combiner avec BaseDirectory
            if (!Path.IsPathRooted(path) || path.StartsWith("\\Data", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            }

            return path;
        }
        // ✅ NOUVELLE MÉTHODE : Scroll-back avec flèche gauche pour humaniser
        private async Task RandomReelScrollBackAsync(Random rand, CancellationToken token, string currentReelId)  // ✅ Ajouter le paramètre
        {
            // 10% de chance de revenir au reel précédent
            if (rand.NextDouble() < 0.10)
            {
                logTextBox.AppendText("[SCROLL_BACK] Going back to previous reel (humanizing)...\r\n");

                var scrollBackScript = @"/* ... votre script Previous ... */";
                var result = await webView.ExecuteScriptAsync(scrollBackScript);
                logTextBox.AppendText($"[SCROLL_BACK] Previous: {result}\r\n");

                // Attendre que le reel précédent charge
                int loadDelay = rand.Next(2000, 4000);
                logTextBox.AppendText($"[SCROLL_BACK] Waiting {loadDelay}ms for previous reel to load...\r\n");
                await Task.Delay(loadDelay, token);

                // Re-regarder brièvement ce reel
                int reWatchTime = rand.Next(3000, 7000);
                logTextBox.AppendText($"[SCROLL_BACK] Re-watching for {reWatchTime / 1000}s...\r\n");
                await Task.Delay(reWatchTime, token);

                // Revenir en avant
                logTextBox.AppendText("[SCROLL_BACK] Returning forward...\r\n");

                var scrollForwardScript = @"/* ... votre script Next ... */";
                var forwardResult = await webView.ExecuteScriptAsync(scrollForwardScript);
                logTextBox.AppendText($"[SCROLL_BACK] Forward: {forwardResult}\r\n");

                // Attendre le chargement
                await Task.Delay(rand.Next(2000, 3500), token);

                // ✅ VÉRIFIER QU'ON EST BIEN REVENU AU BON REEL
                var checkReelIdScript = @"
(function(){
  const match = window.location.href.match(/\/reel\/([^\/]+)/);
  return match ? match[1] : 'NO_ID';
})()";

                var verifyReelId = await webView.ExecuteScriptAsync(checkReelIdScript);
                verifyReelId = verifyReelId?.Trim('"').Trim();

                if (verifyReelId == currentReelId)
                {
                    logTextBox.AppendText($"[SCROLL_BACK] ✓ Back to current reel ({currentReelId})\r\n");
                }
                else
                {
                    logTextBox.AppendText($"[SCROLL_BACK] ⚠️ Reel mismatch! Expected {currentReelId}, got {verifyReelId}\r\n");
                    logTextBox.AppendText($"[SCROLL_BACK] Clicking Next again to sync...\r\n");

                    // ✅ Cliquer Next une fois de plus pour resynchroniser
                    var resyncScript = TargetJavaScriptHelper.GetNextButtonScript();
                    await webView.ExecuteScriptAsync(resyncScript);
                    await Task.Delay(rand.Next(2000, 3000), token);

                    // Vérifier à nouveau
                    verifyReelId = await webView.ExecuteScriptAsync(checkReelIdScript);
                    verifyReelId = verifyReelId?.Trim('"').Trim();
                    logTextBox.AppendText($"[SCROLL_BACK] After resync: {verifyReelId}\r\n");
                }
            }
        }

        public async Task RunAsync(CancellationToken token = default, string customTargetsPath = null)
        {
            if (webView == null || webView.IsDisposed || webView.CoreWebView2 == null)
            {
                logTextBox.AppendText("[Target] ✗ WebView not ready\r\n");
                return;
            }

            // Vérifier connexion Instagram
            bool isLoggedIn = await CheckInstagramLoginAsync();
            if (!isLoggedIn)
            {
                logTextBox.AppendText("[Target] ✗ Not logged in to Instagram\r\n");
                return;
            }
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Target");
                var localToken = form.GetCancellationToken();
                token = localToken;

                try
                {
                    // 1) Charger la liste des cibles
                    // 1) Charger la liste des cibles
                    var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                    Directory.CreateDirectory(dataDir);

                    string targetsPath;

                    if (string.IsNullOrWhiteSpace(customTargetsPath))
                    {
                        // Pas de path custom fourni, chercher dans le schedule
                        var scheduledPath = GetScheduledPathForToday();

                        if (!string.IsNullOrWhiteSpace(scheduledPath))
                        {
                            // Path trouvé dans schedule.csv
                            targetsPath = NormalizeFilePath(scheduledPath);  // ✅ AVEC =
                            logTextBox.AppendText($"[TARGET] Using scheduled path from schedule.csv: {targetsPath}\r\n");
                        }
                        else
{
    // ✅ ESSAYER Targets.txt PAR DÉFAUT
    var defaultTargetsPath = Path.Combine(dataDir, "Targets.txt");
    
    if (File.Exists(defaultTargetsPath))
    {
        targetsPath = defaultTargetsPath;
        logTextBox.AppendText($"[TARGET] No schedule found, using default Targets.txt: {targetsPath}\r\n");
    }
    else  // ← CE ELSE CONTIENT TOUT LE CODE DE LA SÉLECTION MANUELLE
    {
        // ✅ PAS DE TARGETS.TXT, DEMANDER À L'UTILISATEUR
        logTextBox.AppendText("[TARGET] No scheduled path found. Please select a targets file.\r\n");

        string selectedPath = null;

        // Utiliser Invoke pour l'UI thread
        form.Invoke(new Action(() =>
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = dataDir;
                openFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                openFileDialog.Title = "Select Targets File";
                openFileDialog.RestoreDirectory = true;
                
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedPath = openFileDialog.FileName;
                }
            }
        }));
        
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            logTextBox.AppendText("[TARGET] ❌ No file selected. Stopping.\r\n");
            form.StopScript();
            return;
        }

        targetsPath = NormalizeFilePath(selectedPath);
        logTextBox.AppendText($"[TARGET] Using user-selected file: {targetsPath}\r\n");
    }
}
                    }
                    else
                    {
                        // Path custom fourni (depuis le schedule ou autre)
                        targetsPath = NormalizeFilePath(customTargetsPath);

                        // Vérifier si c'est un chemin relatif OU si c'est juste "\Data\..."
                        logTextBox.AppendText($"[TARGET] Using custom targets file: {targetsPath}\r\n");
                    }

                    // Vérifier que le fichier existe
                    if (!File.Exists(targetsPath))
                    {
                        logTextBox.AppendText($"[TARGET] ❌ Targets file not found: {targetsPath}\r\n");
                        form.StopScript();
                        return;
                    }

                    string groupName = !string.IsNullOrWhiteSpace(profile.GroupName)
     ? profile.GroupName
     : profile.Name;

                    string doneTargetsFileName = $"Done_Targets_{groupName}.txt";
                    var doneTargetsPath = Path.Combine(dataDir, doneTargetsFileName);
                    logTextBox.AppendText($"[GROUP] Using group: '{groupName}'\r\n");
                    logTextBox.AppendText($"[TARGET] Using done file: {doneTargetsFileName}\r\n");
                    
                    

                    var targets = new System.Collections.Generic.List<string>();
                    var doneTargets = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Charger le fichier targets
                    targets = File.ReadAllLines(targetsPath)
                                  .Where(line => !string.IsNullOrWhiteSpace(line))
                                  .Select(line => line.Trim())
                                  .ToList();
                    logTextBox.AppendText($"[TARGET] Loaded {targets.Count} targets from {Path.GetFileName(targetsPath)}\r\n");

                    // Charger done_targets.txt spécifique au profil
                    if (File.Exists(doneTargetsPath))
                    {
                        doneTargets = new System.Collections.Generic.HashSet<string>(
                            File.ReadAllLines(doneTargetsPath)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Select(line => line.Trim()),
                            StringComparer.OrdinalIgnoreCase
                        );
                    }
                    else
                    {
                        File.Create(doneTargetsPath).Close();
                        logTextBox.AppendText($"[TARGET] Created {doneTargetsFileName}\r\n");
                    }

                    // Filtrer les targets déjà traités
                    var pendingTargets = targets.Where(t => !doneTargets.Contains(t)).ToList();
                    logTextBox.AppendText($"[TARGET] Total targets: {targets.Count}\r\n");
                    logTextBox.AppendText($"[TARGET] Already done by group '{groupName}': {doneTargets.Count}\r\n");
                    logTextBox.AppendText($"[TARGET] Pending targets: {pendingTargets.Count}\r\n");

                    // ✅ FALLBACK: Si Targets.txt est vide, lire FutureTargets.txt
                    if (pendingTargets.Count == 0)
                    {
                        var futureTargetsPath = Path.Combine(dataDir, "FutureTargets.txt");
                        if (File.Exists(futureTargetsPath))
                        {
                            logTextBox.AppendText($"[TARGET] Targets.txt is empty, checking FutureTargets.txt...\r\n");

                            var futureTargets = File.ReadAllLines(futureTargetsPath)
                                                    .Where(line => !string.IsNullOrWhiteSpace(line))
                                                    .Select(line => line.Trim())
                                                    .ToList();

                            // Filtrer ceux déjà traités
                            pendingTargets = futureTargets.Where(t => !doneTargets.Contains(t)).ToList();

                            logTextBox.AppendText($"[TARGET] Loaded {futureTargets.Count} from FutureTargets.txt\r\n");
                            logTextBox.AppendText($"[TARGET] Pending from FutureTargets: {pendingTargets.Count}\r\n");
                        }
                        else
                        {
                            logTextBox.AppendText($"[TARGET] No FutureTargets.txt found\r\n");
                        }
                    }

                    // ✅ DISTRIBUTION ENTRELACÉE DES TARGETS PAR GROUPE
                    // Pour récupérer les autres profils du groupe, on lit directement le JSON
                    var profilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "profiles.json");
                    List<Profile> groupProfiles = new List<Profile>();

                    if (File.Exists(profilesPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(profilesPath);
                            var allProfiles = System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(json);

                            if (allProfiles != null)
                            {
                                groupProfiles = allProfiles
                                    .Where(p => !string.IsNullOrWhiteSpace(p.GroupName) && p.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(p => p.Name)  // Important: ordre alphabétique pour cohérence
                                    .ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            logTextBox.AppendText($"[GROUP ERROR] Failed to load profiles: {ex.Message}\r\n");
                        }
                    }

                    if (groupProfiles.Count > 1)
                    {
                        // ✅ ACQUÉRIR LE LOCK DU GROUPE AVANT DE TRAITER
                        var groupLock = ScheduleService.GetGroupLock(groupName);

                        logTextBox.AppendText($"[GROUP] {groupProfiles.Count} accounts detected in group '{groupName}'\r\n");
                        logTextBox.AppendText($"[GROUP] Waiting for group lock...\r\n");

                        await groupLock.WaitAsync(token);

                        try
                        {
                            logTextBox.AppendText($"[GROUP] Lock acquired ✓\r\n");

                            // Trouver l'index de ce profil dans le groupe (ordre alphabétique)
                            int profileIndex = groupProfiles.FindIndex(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

                            if (profileIndex == -1)
                            {
                                logTextBox.AppendText($"[GROUP ERROR] Current profile not found in group list!\r\n");
                            }
                            else
                            {
                                // ✅ DISTRIBUTION ENTRELACÉE
                                var myTargets = pendingTargets
                                    .Where((t, index) => index % groupProfiles.Count == profileIndex)
                                    .ToList();

                                logTextBox.AppendText($"[GROUP SPLIT] Position {profileIndex + 1}/{groupProfiles.Count} in '{groupName}'\r\n");
                                logTextBox.AppendText($"[GROUP SPLIT] Original: {pendingTargets.Count} targets\r\n");
                                logTextBox.AppendText($"[GROUP SPLIT] Assigned: {myTargets.Count} targets (interleaved)\r\n");

                                // Remplacer la liste des targets
                                pendingTargets = myTargets;
                            }
                        }
                        finally
                        {
                            // ✅ LIBÉRER LE LOCK APRÈS LE TRAITEMENT
                            groupLock.Release();
                            logTextBox.AppendText($"[GROUP] Lock released ✓\r\n");
                        }
                    }
                    else
                    {
                        logTextBox.AppendText($"[GROUP] Single account or no group - processing all targets\r\n");
                    }
                    if (!pendingTargets.Any())
                    {
                        logTextBox.AppendText("[TARGET] ✓ No new targets to process — all done.\r\n");
                        form.StopScript();
                        return;
                    }
                    // 1bis) Charger commentaires
                    var commentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "comments.txt");
                    var comments = new System.Collections.Generic.List<string>();
                    if (File.Exists(commentsPath))
                    {
                        comments = File.ReadAllLines(commentsPath)
                                       .Where(line => !string.IsNullOrWhiteSpace(line))
                                       .Select(line => line.Trim())
                                       .ToList();
                    }
                    else
                    {
                        logTextBox.AppendText($"Fichier comments.txt non trouvé à {commentsPath} ! Utilisation de commentaires par défaut.\r\n");
                    }
                    if (!comments.Any())
                    {
                        logTextBox.AppendText("Aucun commentaire trouvé dans comments.txt ! Utilisation de commentaires par défaut.\r\n");
                        comments = new string[] { "Super ! 🔥", "J'adore ! ❤️", "Trop cool ! ✨", "Impressionnant !", "Bien vu ! 👍", "Top ! 🎯" }.ToList();
                    }

                    // Detect language by navigating to home page
                    await navigationService.NavigateToHomeAsync(token);
                    var lang = await navigationService.DetectLanguageAsync(token);

                    string likeSelectors = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"svg[aria-label=""J\u2019aime""], svg[aria-label=""Je n\u2019aime plus""]" : @"svg[aria-label=""Like""], svg[aria-label=""Unlike""]";
                    string unlikeSelectors = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"svg[aria-label=""Je n\u2019aime plus""], svg[aria-label=""Je n'aime plus""]" : @"svg[aria-label=""Unlike""]";
                    string unlikeTest = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"n\u2019aime plus" : "unlike";
                    string publishPattern = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "publier|envoyer" : "post|send";

                    // Compteur global pour le fallback Arrow (1 fois sur 15)
                    int nextClickCounter = 0;

                    foreach (var target in pendingTargets)
                    {
                        token.ThrowIfCancellationRequested();

                        var currentTarget = target.Trim();
                        logTextBox.AppendText($"[TARGET] Processing {currentTarget}\r\n");

                        // ⚠️ MARQUER COMME DONE DÈS LE DÉBUT ⚠️
                        MarkTargetAsDone(currentTarget, doneTargetsPath, "(en cours)");

                        int maxReels = rand.Next(4, 6);
                        logTextBox.AppendText($"[TARGET] Will process {maxReels} reels for this target.\r\n");

                        // Human-like navigation to profile via search
                        bool navigationSuccess = await navigationService.NavigateToProfileViaSearchAsync(currentTarget, token);
                        if (!navigationSuccess)
                        {
                            logTextBox.AppendText($"[TARGET] Failed to navigate to profile '{currentTarget}'\r\n");
                            // ❌ NE PLUS MARQUER ICI - Déjà marqué au début
                            continue;
                        }

                        await humanBehavior.RandomHumanNoiseAsync(token);
                        await humanBehavior.RandomHumanNoiseAsync(token);

                        // Click on Reels tab
                        bool reelsSuccess = await ClickReelsTabAsync(currentTarget, lang, token);
                        if (!reelsSuccess)
                        {
                            logTextBox.AppendText($"[TARGET] Failed to navigate to reels for '{currentTarget}'\r\n");
                            // ❌ NE PLUS MARQUER ICI - Déjà marqué au début
                            continue;
                        }

                        // Check for reels feed to load
                        bool isLoaded = false;
                        int loadRetries = 0;
                        while (!isLoaded && loadRetries < 5)
                        {
                            var loadCheck = await webView.ExecuteScriptAsync("document.querySelectorAll('a[href*=\"/reel/\"]').length > 0 ? 'true' : 'false';");
                            isLoaded = TargetJavaScriptHelper.JsBoolIsTrue(loadCheck);
                            if (!isLoaded)
                            {
                                await Task.Delay(2000, token);
                                loadRetries++;
                            }
                        }
                        if (!isLoaded)
                        {
                            logTextBox.AppendText($"[ERROR] Reels feed failed to load for {currentTarget}.\r\n");
                            continue;
                        }

                        // Vérifier login
                        var loginWall = await webView.ExecuteScriptAsync(@"
(function(){
  return !!document.querySelector('[href*=""/accounts/login/""], .login-button');
})()");
                        if (string.Equals(loginWall, "true", StringComparison.OrdinalIgnoreCase))
                        {
                            logTextBox.AppendText("[CHECK] Login requis : connecte-toi dans la WebView puis relance.\r\n");
                            form.StopScript();
                            return;
                        }

                        await humanBehavior.RandomHumanNoiseAsync(token);

                        // Sélecteur 1er Reel
                        var findReelScript = @"
(function(){
  const a = document.querySelector('article a[href*=""/reel/""]')
        || document.querySelector('a[href*=""/reel/""]');
  return a ? a.href : null;
})()";
                        var reelHref = await webView.ExecuteScriptAsync(findReelScript);

                        // Lazy-load si rien
                        if (reelHref == "null")
                        {
                            await webView.ExecuteScriptAsync(@"
(async function(){
  for(let i=0;i<6;i++){
    window.scrollBy(0, window.innerHeight);
    await new Promise(r => setTimeout(r, 800));
  }
  return true;
})()");
                            await Task.Delay(1000, token);
                            reelHref = await webView.ExecuteScriptAsync(findReelScript);
                        }

                        if (reelHref == "null")
                        {
                            logTextBox.AppendText("[ERREUR] Aucun Reel détecté sur la page du profil.\r\n");
                            continue;
                        }

                        // Click pour ouvrir
                        var clickSimple = await webView.ExecuteScriptAsync(@"
(function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  el.click();
  return 'CLICKED';
})()");

                        await Task.Delay(3000, token);
                        var openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");

                        if (!TargetJavaScriptHelper.JsBoolIsTrue(openedCheck))
                        {
                            var clickMouseEvents = await webView.ExecuteScriptAsync(@"
(async function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  await new Promise(r=>setTimeout(r,500));
  const r = el.getBoundingClientRect();
  const x = r.left + r.width/2, y = r.top + r.height/2;
  el.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,clientX:x,clientY:y}));
  el.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,clientX:x,clientY:y}));
  return 'MOUSE_EVENTS_SENT';
})()");

                            await Task.Delay(3000, token);
                            openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                        }

                        if (!TargetJavaScriptHelper.JsBoolIsTrue(openedCheck))
                        {
                            logTextBox.AppendText("[KO] Impossible d'ouvrir le 1er Reel.\r\n");
                            continue;
                        }

                        // ======================= BOUCLE REELS =======================
                        string previousReelId = null;
                        var reelIdScript = @"
(function(){
  const match = window.location.href.match(/\/reel\/([^\/]+)/);
  return match ? match[1] : 'NO_ID';
})()";
                        var dateScript = @"
(function(){
  const timeEl = document.querySelector('time.x1p4m5qa');
  if (timeEl) {
    const datetime = timeEl.getAttribute('datetime') || 'NO_DATETIME';
    const text = timeEl.textContent || 'NO_TEXT';
    return JSON.stringify({datetime: datetime, text: text});
  } else {
    return 'NO_DATE_FOUND';
  }
})()";

                        for (int reelNum = 1; reelNum <= maxReels; reelNum++)
                        {
                            token.ThrowIfCancellationRequested();

                            logTextBox.AppendText($"[REEL {reelNum}/{maxReels}] Début interaction...\r\n");

                            // ✅ EXTRACTION ID & DATE **AU DÉBUT DE CHAQUE ITÉRATION**
                            var reelId = await webView.ExecuteScriptAsync(reelIdScript);
                            reelId = reelId?.Trim('"').Trim();
                            logTextBox.AppendText($"[REEL_ID] {reelId}\r\n");

                            var reelDateRaw = await webView.ExecuteScriptAsync(dateScript);
                            string reelDate;
                            try
                            {
                                reelDate = JsonSerializer.Deserialize<string>(reelDateRaw);
                            }
                            catch (JsonException ex)
                            {
                                logTextBox.AppendText($"[DATE_DESERIALIZE_ERROR] {ex.Message}\r\n");
                                reelDate = "NO_DATE_FOUND";
                            }
                            logTextBox.AppendText($"[DATE] {reelDate}\r\n");

                            // ✅ CALCUL DE L'ÂGE ET DÉCISION
                            bool shouldComment = false;
                            bool shouldSkip = false;
                            double ageHours = -1;

                            if (reelDate != "NO_DATE_FOUND")
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(reelDate);
                                    string datetimeStr = doc.RootElement.GetProperty("datetime").GetString();
                                    if (datetimeStr != "NO_DATETIME")
                                    {
                                        if (DateTimeOffset.TryParse(datetimeStr, out var reelTime))
                                        {
                                            var now = DateTimeOffset.UtcNow;
                                            var age = now - reelTime;
                                            ageHours = age.TotalHours;

                                            logTextBox.AppendText($"[AGE] {ageHours:F1}h\r\n");

                                            // ✅ LOGIQUE CLAIRE ET STRICTE
                                            if (ageHours < 24)
                                            {
                                                shouldComment = true;
                                                shouldSkip = false;
                                                logTextBox.AppendText("[DECISION] < 24h → COMMENTER\r\n");
                                            }
                                            else  // ≥ 24h
                                            {
                                                shouldComment = false;
                                                shouldSkip = (rand.NextDouble() < 0.80);  // 80% de skip
                                                logTextBox.AppendText($"[DECISION] ≥ 24h → {(shouldSkip ? "SKIP (80%)" : "NO COMMENT (like 9%)")}\r\n");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logTextBox.AppendText($"[DATE_PARSE_ERROR] {ex.Message}\r\n");
                                }
                            }

                            // ✅ SI SKIP DÉCIDÉ, PASSER AU SUIVANT IMMÉDIATEMENT
                            if (shouldSkip && reelNum < maxReels)
                            {
                                int skipDelay = rand.Next(800, 2000);
                                logTextBox.AppendText($"[SKIP] Waiting {skipDelay}ms then skipping to next reel...\r\n");
                                await Task.Delay(skipDelay, token);

                                // Cliquer Next
                                nextClickCounter++;
                                bool useArrowKey = (nextClickCounter % 15 == 0);
                                string nextScript;

                                if (useArrowKey)
                                {
                                    logTextBox.AppendText("[SKIP] Using ArrowRight fallback (1/15)\r\n");
                                    nextScript = @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_USED';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";
                                }
                                else
                                {
                                    nextScript = TargetJavaScriptHelper.GetNextButtonScript();
                                }


                                var nextTry = await webView.ExecuteScriptAsync(nextScript);

                                // ✅ PARSER ET AFFICHER PROPREMENT LE RÉSULTAT
                                if (string.IsNullOrWhiteSpace(nextTry) || nextTry == "{}" || nextTry == "\"{}\"")
                                {
                                    logTextBox.AppendText($"[SKIP] ⚠️ Empty response from JavaScript\r\n");
                                }
                                else if (nextTry.Contains("NEXT_CLICKED:"))
                                {
                                    try
                                    {
                                        var parts = nextTry.Split(new[] { "NEXT_CLICKED:" }, StringSplitOptions.None);
                                        if (parts.Length > 1)
                                        {
                                            var coords = parts[1].Trim('"', ' ').Split(',');
                                            if (coords.Length >= 2)
                                            {
                                                string clickX = coords[0].Trim();
                                                string clickY = coords[1].Trim();
                                                logTextBox.AppendText($"[SKIP] 🖱️ Button clicked at X={clickX}, Y={clickY}\r\n");
                                            }
                                            else
                                            {
                                                logTextBox.AppendText($"[SKIP] ⚠️ Malformed: {nextTry}\r\n");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logTextBox.AppendText($"[SKIP] ⚠️ Parse error: {ex.Message}\r\n");
                                    }
                                }
                                else if (nextTry.Contains("ARROW_KEY_USED"))
                                {
                                    logTextBox.AppendText($"[SKIP] ⌨️ ArrowRight key used\r\n");
                                }
                                else if (nextTry.Contains("NO_DIALOG") || nextTry.Contains("NO_NEXT_BUTTON"))
                                {
                                    logTextBox.AppendText($"[SKIP] ❌ {nextTry}\r\n");
                                }
                                else
                                {
                                    logTextBox.AppendText($"[SKIP] ℹ️ {nextTry}\r\n");
                                }
                                // Attendre que le reel change avec retry
                                int retryCount = 0;
                                const int maxRetries = 3;
                                string newReelId = null;

                                while (retryCount < maxRetries)
                                {
                                    await Task.Delay(rand.Next(1500, 3000), token);
                                    newReelId = await webView.ExecuteScriptAsync(reelIdScript);
                                    newReelId = newReelId?.Trim('"').Trim();

                                    var checkAdvanced = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog = !!document.querySelector('div[role=""dialog""]');
  const videos = document.querySelectorAll('video');
  const hasVideo = videos.length > 0;
  const videoPlaying = Array.from(videos).some(v => !v.paused);
  return (hasDialog && hasVideo) ? 'true' : 'false';
})()");

                                    if (newReelId != reelId && newReelId != "NO_ID" && TargetJavaScriptHelper.JsBoolIsTrue(checkAdvanced))
                                    {
                                        logTextBox.AppendText("[SKIP] ✓ Successfully advanced to next reel\r\n");
                                        break;
                                    }

                                    logTextBox.AppendText($"[SKIP RETRY {retryCount + 1}] Stuck on {reelId}, retrying...\r\n");

                                    nextScript = @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_RETRY';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";

                                    nextTry = await webView.ExecuteScriptAsync(nextScript);
                                    if (nextTry.Contains("ARROW_KEY_RETRY"))
                                    {
                                        logTextBox.AppendText($"[SKIP RETRY] ⌨️ ArrowRight used\r\n");
                                    }
                                    else
                                    {
                                        logTextBox.AppendText($"[SKIP RETRY] {nextTry}\r\n");
                                    }
                                   
                                    retryCount++;
                                }

                                if (retryCount >= maxRetries)
                                {
                                    logTextBox.AppendText($"[SKIP ERROR] Max retries reached, stuck on {reelId}. Stopping reel loop.\r\n");
                                    break;
                                }

                                // ✅ CONTINUER LA BOUCLE (ne pas traiter ce reel)
                                continue;
                            }

                            // ✅ SINON, TRAITER NORMALEMENT (WATCH, LIKE, COMMENT SI shouldComment)
                            await humanBehavior.RandomHumanPauseAsync(token);

                            // Watch delay with possible pause
                            int watchTime = rand.Next(5000, 10001);
                            await Task.Delay(watchTime / 2, token);

                            if (rand.NextDouble() < 0.15)
                            {
                                logTextBox.AppendText("[HUMAN] Pausing reel mid-watch...\r\n");
                                var pauseScript = @"
(function(){
  var video = document.querySelector('video');
  if (video && !video.paused) {
    video.pause();
    return 'PAUSED';
  }
  return 'NO_VIDEO';
})()";
                                var pauseResult = await webView.ExecuteScriptAsync(pauseScript);
                                logTextBox.AppendText($"[PAUSE] {pauseResult}\r\n");

                                await Task.Delay(rand.Next(2000, 8000), token);

                                var playScript = @"
(function(){
  var video = document.querySelector('video');
  if (video && video.paused) {
    video.play();
    return 'RESUMED';
  }
  return 'NO_VIDEO';
})()";
                                var playResult = await webView.ExecuteScriptAsync(playScript);
                                logTextBox.AppendText($"[RESUME] {playResult}\r\n");
                            }

                            await Task.Delay(watchTime / 2, token);

                            // Like (9%)
                            bool shouldLike = rand.NextDouble() < 0.09;
                            if (shouldLike)
                            {
                                var likeTry = await webView.ExecuteScriptAsync($@"
(function(){{
  try {{
    var scope = document.querySelector('div[role=""dialog""]') || document;

    function sig(el){{
      if(!el) return 'null';
      var id = el.id ? '#' + el.id : '';
      var cls = el.classList && el.classList.length ? ('.' + Array.from(el.classList).join('.')) : '';
      var role = el.getAttribute && el.getAttribute('role') ? '[role='+el.getAttribute('role')+']' : '';
      return el.tagName + id + cls + role;
    }}

    function getSvgAria(el){{ try{{ var svg = el.querySelector('svg[aria-label]'); return svg ? (svg.getAttribute('aria-label')||'') : ''; }}catch(_){{ return ''; }} }}

    function liked(){{
      var s = scope;
      if (s.querySelector('{unlikeSelectors}')) return true;
      if (s.querySelector('button[aria-pressed=""true""], [role=""button""][aria-pressed=""true""]')) return true;
      if (s.querySelector('svg[color=""rgb(255, 48, 64)""], svg[fill=""rgb(237, 73, 86)""], svg path[d^=""M12 21.35""]')) return true;
      return false;
    }}

    var svg = scope.querySelector('{likeSelectors}');
    if (!svg) return 'NO_SVG_FOUND';

    var svgAria = svg.getAttribute('aria-label') || '';
    var isAlreadyLiked = /{unlikeTest}/i.test(svgAria);

    var el = svg.closest('button,[role=""button""],div[role=""button""],span[role=""button""]') || svg.parentElement;
    if (!el) return 'NO_BUTTON_PARENT';

    var picked_info = 'PICKED: ' + sig(el) + ' svgAria:' + svgAria + ' w:' + Math.round(el.getBoundingClientRect().width) + ' h:' + Math.round(el.getBoundingClientRect().height);

    if (isAlreadyLiked) return 'ALREADY_LIKED ' + picked_info;

    try{{ el.scrollIntoView({{behavior:'smooth', block:'center'}}); }}catch(_){{}}
    try{{ el.focus(); }}catch(_){{}}
    
    try{{ el.click(); }}catch(_){{}}
    if (liked()) return 'OK:CLICK ' + picked_info;

    try{{
      var r = el.getBoundingClientRect(), x = Math.floor(r.left + r.width/2), y = Math.floor(r.top + r.height/2);
      var topEl = document.elementFromPoint(x, y) || el;
      topEl.click();
    }}catch(_){{}}
    if (liked()) return 'OK:ELEMENTFROMPOINT ' + picked_info;

    return 'FAIL ' + picked_info;
  }} catch(e){{
    return 'JSERR: ' + (e && e.message ? e.message : String(e));
  }}
}})();");
                                logTextBox.AppendText($"[LIKE] {likeTry}\r\n");
                                await Task.Delay(2000, token);
                            }
                            else
                            {
                                logTextBox.AppendText("[LIKE] Skipped (random 9%)\r\n");
                            }
                            await RandomReelScrollBackAsync(rand, token, reelId);
                            // ✅ COMMENTAIRE SEULEMENT SI shouldComment == true
                            if (shouldComment)
                            {
                                string randomComment = comments[rand.Next(comments.Count)];
                                logTextBox.AppendText($"[COMMENT] Sélectionné: '{randomComment}' (âge: {ageHours:F1}h)\r\n");
                                logTextBox.AppendText($"[TYPING] Starting...\r\n");

                                var escapedComment = randomComment
                                    .Replace("\\", "\\\\")
                                    .Replace("\u2018", "'")
                                    .Replace("\u2019", "'")
                                    .Replace("'", "\\'");

                                var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}
  
  try {{
    const videos = document.querySelectorAll('video');
    let visibleVideo = null;
    let maxVisible = 0;
    
    videos.forEach(video => {{
      const rect = video.getBoundingClientRect();
      const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visible > maxVisible) {{
        maxVisible = visible;
        visibleVideo = video;
      }}
    }});
    
    if (!visibleVideo) return 'NO_VIDEO';
    
    const article = visibleVideo.closest('article');
    if (!article) return 'NO_ARTICLE';
    
    const text = '{escapedComment}';
    const chars = Array.from(text);
    
    // ✅ CHERCHER textarea OU contenteditable
    let ta = article.querySelector('textarea');
    let ce = null;
    
    if (!ta) {{
      ce = article.querySelector('div[role=""textbox""][contenteditable=""true""]');
      if (!ce) return 'NO_COMPOSER_INITIAL';
    }}
    
    const initialTarget = ta || ce;
    
    initialTarget.scrollIntoView({{behavior:'smooth', block:'center'}});
    await sleep(randomDelay(200, 400));
    
    // ✅ CLIC HUMAIN SUR LE COMPOSER
    const rect = initialTarget.getBoundingClientRect();
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    const clientX = rect.left + offsetX;
    const clientY = rect.top + offsetY;
    
    var startX = clientX + (Math.random() * 100 - 50);
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      initialTarget.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    const opts = {{bubbles:true, cancelable:true, clientX:clientX, clientY:clientY, button:0}};
    initialTarget.dispatchEvent(new MouseEvent('mousedown', opts));
    initialTarget.dispatchEvent(new MouseEvent('mouseup', opts));
    initialTarget.dispatchEvent(new MouseEvent('click', opts));
    
    await sleep(randomDelay(100, 250));
    initialTarget.focus();
    
    // ✅ TYPING CARACTÈRE PAR CARACTÈRE
    for (let i = 0; i < chars.length; i++) {{
      const char = chars[i];
      
      // Re-chercher le composer à chaque caractère (au cas où il change)
      ta = article.querySelector('textarea');
      ce = null;
      
      if (!ta) {{
        ce = article.querySelector('div[role=""textbox""][contenteditable=""true""]');
        if (!ce) return 'NO_COMPOSER_AT_' + i;
      }}
      
      const currentTarget = ta || ce;
      
      try {{
        if (ta) {{
          const currentValue = ta.value;
          const proto = HTMLTextAreaElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(ta, currentValue + char);
          
          ta.dispatchEvent(new Event('input', {{bubbles: true}}));
          ta.dispatchEvent(new Event('change', {{bubbles: true}}));
        }} else {{
          document.execCommand('insertText', false, char);
        }}
      }} catch(e) {{
        return 'TYPE_ERROR_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      // ✅ DÉLAIS VARIABLES SELON LE CARACTÈRE
      let delay;
      if (char === ',' || char === ';') {{
        delay = randomDelay(200, 400);
      }} else if (char === '.' || char === '!' || char === '?') {{
        delay = randomDelay(300, 500);
      }} else if (char === ' ') {{
        delay = randomDelay(80, 150);
      }} else {{
        delay = randomDelay(50, 150);
      }}
      
      // ✅ 5% DE CHANCE DE FAIRE UNE TYPO + CORRECTION
      if (Math.random() < 0.05 && i < chars.length - 1) {{
        await sleep(delay);
        
        const wrongChars = 'qwertyuiopasdfghjklzxcvbnm';
        const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
        
        ta = article.querySelector('textarea');
        ce = null;
        if (!ta) {{
          ce = article.querySelector('div[role=""textbox""][contenteditable=""true""]');
          if (!ce) return 'NO_COMPOSER_ERROR_AT_' + i;
        }}
        
        const currentTargetError = ta || ce;
        
        try {{
          if (ta) {{
            const currentValue = currentTargetError.value;
            const proto = HTMLTextAreaElement.prototype;
            const desc = Object.getOwnPropertyDescriptor(proto, 'value');
            desc.set.call(ta, currentValue + wrongChar);
            ta.dispatchEvent(new Event('input', {{bubbles: true}}));
          }} else {{
            document.execCommand('insertText', false, wrongChar);
          }}
        }} catch(e) {{
          return 'ERROR_TYPE_AT_' + i + ': ' + (e.message || String(e));
        }}
        
        await sleep(randomDelay(100, 250));
        
        ta = article.querySelector('textarea');
        ce = null;
        if (!ta) {{
          ce = article.querySelector('div[role=""textbox""][contenteditable=""true""]');
          if (!ce) return 'NO_COMPOSER_DELETE_AT_' + i;
        }}
        
        const currentTargetDelete = ta || ce;
        
        try {{
          if (ta) {{
            const currentValue = currentTargetDelete.value;
            const proto = HTMLTextAreaElement.prototype;
            const desc = Object.getOwnPropertyDescriptor(proto, 'value');
            desc.set.call(ta, currentValue.slice(0, -1));
            ta.dispatchEvent(new Event('input', {{bubbles: true}}));
          }} else {{
            document.execCommand('delete', false);
          }}
        }} catch(e) {{
          return 'DELETE_ERROR_AT_' + i + ': ' + (e.message || String(e));
        }}
        
        await sleep(randomDelay(50, 120));
      }}
      
      // ✅ 2% DE CHANCE DE PAUSE LONGUE (RÉFLEXION)
      if (Math.random() < 0.02) {{
        await sleep(randomDelay(400, 800));
      }}
      
      await sleep(delay);
    }}
    
    await sleep(randomDelay(300, 600));
    return 'TYPED_SUCCESSFULLY';
  }} catch(e) {{
    return 'ERROR: ' + e.message;
  }}
}})()";

                                int charCount = randomComment.Length;
                                int baseTime = charCount * 100;
                                int punctuationCount = randomComment.Count(c => ".!?,;".Contains(c));
                                int punctuationDelay = punctuationCount * 300;
                                int errorDelay = (int)(charCount * 0.05 * 500);
                                int totalTime = baseTime + punctuationDelay + errorDelay + 3000;

                                var typingTask = webView.ExecuteScriptAsync(typingScript);

                                logTextBox.AppendText($"[TYPING] Attente de {totalTime}ms...\r\n");
                                await Task.Delay(totalTime, token);

                                var typingResult = await typingTask;
                                logTextBox.AppendText($"[TYPING] Résultat: {typingResult}\r\n");

                                await Task.Delay(rand.Next(1500, 2500), token);

                                var publishScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function btnEnabled(b){{
    if (!b) return false;
    if (b.disabled) return false;
    const ad = b.getAttribute('aria-disabled');
    if (ad && ad.toString().toLowerCase() === 'true') return false;
    const st = getComputedStyle(b);
    return !(st.pointerEvents === 'none' || st.display === 'none' || st.visibility === 'hidden');
  }}

  function findPublishControl(form){{
    if (!form) return null;
    let btn = form.querySelector('button[type=""submit""]');
    if (btn) return btn;
    const candidates = [...form.querySelectorAll('button,[role=""button""]')];
    const match = candidates.find(el => /{publishPattern}/i.test((el.textContent||'').trim()));
    if (match) return match;
    return null;
  }}

  async function waitEnabled(el, timeout=10000){{
    const t0 = performance.now();
    while (performance.now() - t0 < timeout){{
      if (btnEnabled(el)) return true;
      await sleep(150);
    }}
    return false;
  }}

  try {{
    const videos = document.querySelectorAll('video');
    let visibleVideo = null;
    let maxVisible = 0;
    
    videos.forEach(video => {{
      const rect = video.getBoundingClientRect();
      const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visible > maxVisible) {{
        maxVisible = visible;
        visibleVideo = video;
      }}
    }});
    
    if (!visibleVideo) return 'NO_VIDEO';
    
    const article = visibleVideo.closest('article');
    if (!article) return 'NO_ARTICLE';
    
    const form = article.querySelector('form');
    if (!form) return 'NO_FORM';
    
    const ctrl = findPublishControl(form);
    if (!ctrl) return 'NO_CTRL';
    
    const ok = await waitEnabled(ctrl, 10000);
    if (!ok) return 'CTRL_DISABLED_TIMEOUT';
    
    // ✅ CLIC HUMAIN SUR LE BOUTON PUBLIER
    const btnRect = ctrl.getBoundingClientRect();
    var marginX = btnRect.width * 0.2;
    var marginY = btnRect.height * 0.2;
    var offsetX = marginX + Math.random() * (btnRect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (btnRect.height - 2 * marginY);
    const btnX = btnRect.left + offsetX;
    const btnY = btnRect.top + offsetY;
    
    var startX = btnX + (Math.random() * 100 - 50);
    var startY = btnY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (btnX - startX) * (i / 5);
      var moveY = startY + (btnY - startY) * (i / 5);
      ctrl.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    const btnOpts = {{bubbles:true, cancelable:true, clientX:btnX, clientY:btnY, button:0}};
    ctrl.dispatchEvent(new MouseEvent('mousedown', btnOpts));
    ctrl.dispatchEvent(new MouseEvent('mouseup', btnOpts));
    ctrl.dispatchEvent(new MouseEvent('click', btnOpts));
    
    // ✅ ATTENDRE QUE LE COMMENTAIRE SOIT ENVOYÉ (textarea vide)
    const t0 = performance.now();
    while (performance.now() - t0 < 12000) {{
      const ta2 = article.querySelector('textarea');
      const ce2 = article.querySelector('div[role=""textbox""][contenteditable=""true""]');
      const target = ta2 || ce2;
      
      if (!target) break;  // Plus de composer = envoyé
      
      const content = ta2 ? ta2.value : (ce2 ? ce2.textContent : '');
      if (content.trim().length === 0) break;  // Vide = envoyé
      
      await sleep(220);
    }}
    
    return 'PUBLISHED';
  }} catch(e) {{
    return 'ERROR: ' + e.message;
  }}
}})()";

                                var publishResult = await webView.ExecuteScriptAsync(publishScript);
                                logTextBox.AppendText($"[PUBLISH] {publishResult}\r\n");

                                await Task.Delay(rand.Next(1200, 2201), token);
                            }
                            else
                            {
                                logTextBox.AppendText($"[COMMENT] Skipped: Reel is {ageHours:F1}h old (not < 24h)\r\n");
                            }

                            await humanBehavior.RandomHumanPauseAsync(token);
                            await humanBehavior.RandomHumanNoiseAsync(token);

                            // NEXT si pas le dernier
                            if (reelNum < maxReels)
                            {
                                if (reelNum == 1)
                                {
                                    await Task.Delay(rand.Next(800, 1500), token);
                                }

                                nextClickCounter++;

                                int preDelay = rand.Next(800, 2000);
                                logTextBox.AppendText($"[NEXT] Waiting {preDelay}ms before action...\r\n");
                                await Task.Delay(preDelay, token);

                                bool useArrowKey = (nextClickCounter % 15 == 0);

                                string nextScript;
                                if (useArrowKey)
                                {
                                    logTextBox.AppendText("[NEXT] Using ArrowRight fallback (1/15)\r\n");
                                    nextScript = @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_USED';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";
                                }
                                else
                                {
                                    nextScript = TargetJavaScriptHelper.GetNextButtonScript();
                                }

                                var nextTry = await webView.ExecuteScriptAsync(nextScript);
                                if (string.IsNullOrWhiteSpace(nextTry) || nextTry == "{}" || nextTry == "\"{}\"")
                                {
                                    logTextBox.AppendText($"[NEXT] ⚠️ Empty response\r\n");
                                }
                                else if (nextTry.Contains("NEXT_CLICKED:"))
                                {
                                    try
                                    {
                                        var parts = nextTry.Split(new[] { "NEXT_CLICKED:" }, StringSplitOptions.None);
                                        if (parts.Length > 1)
                                        {
                                            var coords = parts[1].Trim('"', ' ').Split(',');
                                            if (coords.Length >= 2)
                                            {
                                                string clickX = coords[0].Trim();
                                                string clickY = coords[1].Trim();
                                                logTextBox.AppendText($"[NEXT] 🖱️ Clicked at X={clickX}, Y={clickY}\r\n");
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        logTextBox.AppendText($"[NEXT] ⚠️ Parse error: {nextTry}\r\n");
                                    }
                                }
                                else if (nextTry.Contains("ARROW_KEY"))
                                {
                                    logTextBox.AppendText($"[NEXT] ⌨️ Keyboard used\r\n");
                                }
                                else if (nextTry.Contains("NO_DIALOG") || nextTry.Contains("NO_NEXT_BUTTON"))
                                {
                                    logTextBox.AppendText($"[NEXT] ❌ {nextTry}\r\n");
                                }
                                else
                                {
                                    logTextBox.AppendText($"[NEXT] ℹ️ {nextTry}\r\n");
                                }

                                int loadDelay = rand.Next(2500, 4500);
                                logTextBox.AppendText($"[NEXT] Waiting {loadDelay}ms for next reel to load...\r\n");
                                await Task.Delay(loadDelay, token);

                                int retryCount = 0;
                                const int maxRetries = 3;
                                string newReelId = null;
                                while (retryCount < maxRetries)
                                {
                                    await Task.Delay(rand.Next(1500, 3000), token);
                                    newReelId = await webView.ExecuteScriptAsync(reelIdScript);
                                    newReelId = newReelId?.Trim('"').Trim();

                                    var checkAdvanced = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog = !!document.querySelector('div[role=""dialog""]');
  const videos = document.querySelectorAll('video');
  const hasVideo = videos.length > 0;
  const videoPlaying = Array.from(videos).some(v => !v.paused);
  return (hasDialog && hasVideo) ? 'true' : 'false';
})()");

                                    if (newReelId != previousReelId && newReelId != "NO_ID" && TargetJavaScriptHelper.JsBoolIsTrue(checkAdvanced))
                                    {
                                        logTextBox.AppendText("[NEXT] ✓ Successfully advanced to next reel\r\n");
                                        break;
                                    }

                                    logTextBox.AppendText($"[NEXT RETRY {retryCount + 1}] Stuck on {previousReelId}, retrying...\r\n");

                                    logTextBox.AppendText("[NEXT RETRY] Forcing ArrowRight fallback...\r\n");
                                    nextScript = @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_RETRY';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";

                                    nextTry = await webView.ExecuteScriptAsync(nextScript);
                                    logTextBox.AppendText($"[NEXT RETRY] {nextTry}\r\n");

                                    loadDelay = rand.Next(2500, 4500);
                                    logTextBox.AppendText($"[NEXT RETRY] Waiting {loadDelay}ms for next reel to load...\r\n");
                                    await Task.Delay(loadDelay, token);

                                    retryCount++;
                                }

                                if (retryCount >= maxRetries)
                                {
                                    logTextBox.AppendText($"[NEXT ERROR] Max retries reached, stuck on {previousReelId}. Stopping reel loop.\r\n");
                                    break;
                                }

                                var stillOpened = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  return hasDialog.toString();
})()");
                                if (!TargetJavaScriptHelper.JsBoolIsTrue(stillOpened))
                                {
                                    logTextBox.AppendText("[NEXT] Plus de modal, arrêt boucle.\r\n");
                                    break;
                                }
                            }

                            previousReelId = reelId;
                        }

                        // Close the reel modal after processing all reels
                        await CloseReelModalAsync(lang, token);

                        logTextBox.AppendText($"[TARGET] Terminé pour {currentTarget}.\r\n");
                       
                        

                        await RandomHumanPauseAsync(token, 5000, 15000, 0.1, 30000, 120000);
                    }

                    logTextBox.AppendText("[FLOW] Tous les targets traités.\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("Script annulé par l'utilisateur.\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                    Logger.LogError($"TargetService.RunAsync/inner: {ex}");
                }
                finally
                {
                    form.ScriptCompleted();
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                Logger.LogError($"TargetService.RunAsync: {ex}");
            }

        }
        public async Task RandomHumanPauseAsync(
            CancellationToken token,
            int minShort = 500,
            int maxShort = 2000,
            double longPauseChance = 0.04,
            int minLong = 10000,
            int maxLong = 60000)
        {
            if (rand.NextDouble() < longPauseChance)
            {
                // Pause longue (rare)
                int longDelay = rand.Next(minLong, maxLong);
                await Task.Delay(longDelay, token);
            }
            else
            {
                // Pause courte (fréquent)
                int shortDelay = rand.Next(minShort, maxShort);
                await Task.Delay(shortDelay, token);
            }
        }
    }
}