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

        public TargetService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.navigationService = new NavigationService(webView, logTextBox);
        }

        private static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task RandomHumanPauseAsync(CancellationToken token, int minShort = 500, int maxShort = 2000, double longPauseChance = 0.08, int minLong = 10000, int maxLong = 60000)
        {
            if (rand.NextDouble() < longPauseChance)
            {
                int longDelay = rand.Next(minLong, maxLong);
                logTextBox.AppendText($"[HUMAN PAUSE] Long distraction pause: {longDelay}ms\r\n");
                await Task.Delay(longDelay, token);
            }
            else
            {
                int shortDelay = rand.Next(minShort, maxShort);
                logTextBox.AppendText($"[HUMAN PAUSE] Short pause: {shortDelay}ms\r\n");
                await Task.Delay(shortDelay, token);
            }
        }

        private async Task RandomHumanNoiseAsync(CancellationToken token)
        {
            if (rand.NextDouble() < 0.3)  // 30% chance for noise
            {
                logTextBox.AppendText("[HUMAN NOISE] Adding idle scroll or hover...\r\n");

                var noiseScript = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  // Random scroll
  window.scrollBy(0, Math.random() * 200 - 100);  // Small up/down scroll
  await sleep(500);

  // Hover over a random element (e.g., a post or button without clicking)
  var elements = document.querySelectorAll('a, button, div[role=""button""]');
  if (elements.length > 0) {
    var randomEl = elements[Math.floor(Math.random() * elements.length)];
    var rect = randomEl.getBoundingClientRect();
    var x = rect.left + rect.width / 2;
    var y = rect.top + rect.height / 2;
    randomEl.dispatchEvent(new MouseEvent('mouseover', {bubbles: true, clientX: x, clientY: y}));
    await sleep(Math.random() * 1000 + 500);  // Hover 0.5-1.5s
    randomEl.dispatchEvent(new MouseEvent('mouseleave', {bubbles: true, clientX: x, clientY: y}));
  }
  return 'NOISE_ADDED';
})()";
                var noiseResult = await webView.ExecuteScriptAsync(noiseScript);
                logTextBox.AppendText($"[NOISE] {noiseResult}\r\n");
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

            if (!JsBoolIsTrue(checkReels))
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

            if (!JsBoolIsTrue(checkClosed))
            {
                logTextBox.AppendText("[NAV] ✗ Modal did not close\r\n");
                return false;
            }

            logTextBox.AppendText("[NAV] ✓ Modal closed\r\n");
            return true;
        }

        public async Task RunAsync(CancellationToken token = default)
        {
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

                    var targetsPath = Path.Combine(dataDir, "targets.txt");
                    var doneTargetsPath = Path.Combine(dataDir, "done_targets.txt");

                    var targets = new System.Collections.Generic.List<string>();
                    var doneTargets = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Charger les targets.txt
                    if (File.Exists(targetsPath))
                    {
                        targets = File.ReadAllLines(targetsPath)
                                      .Where(line => !string.IsNullOrWhiteSpace(line))
                                      .Select(line => line.Trim())
                                      .ToList();
                    }
                    else
                    {
                        logTextBox.AppendText($"Fichier targets.txt non trouvé à {targetsPath} !\r\n");
                    }

                    // Charger done_targets.txt
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
                        logTextBox.AppendText($"Fichier done_targets.txt créé à {doneTargetsPath}.\r\n");
                    }

                    // Filtrer les targets déjà traités
                    var pendingTargets = targets.Where(t => !doneTargets.Contains(t)).ToList();

                    if (!pendingTargets.Any())
                    {
                        logTextBox.AppendText("Aucun nouveau target à traiter — tous sont déjà dans done_targets.txt.\r\n");
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

                    foreach (var target in targets)
                    {
                        token.ThrowIfCancellationRequested();

                        var currentTarget = target.Trim();
                        logTextBox.AppendText($"[TARGET] Processing {currentTarget}\r\n");

                        int maxReels = rand.Next(4, 6);
                        logTextBox.AppendText($"[TARGET] Will process {maxReels} reels for this target.\r\n");

                        // Human-like navigation to profile via search
                        bool navigationSuccess = await navigationService.NavigateToProfileViaSearchAsync(currentTarget, token);
                        if (!navigationSuccess)
                        {
                            logTextBox.AppendText($"[TARGET] Failed to navigate to profile '{currentTarget}'\r\n");
                            continue;
                        }

                        await RandomHumanPauseAsync(token);

                        await RandomHumanNoiseAsync(token);

                        // Click on Reels tab
                        bool reelsSuccess = await ClickReelsTabAsync(currentTarget, lang, token);
                        if (!reelsSuccess)
                        {
                            logTextBox.AppendText($"[TARGET] Failed to navigate to reels for '{currentTarget}'\r\n");
                            continue;
                        }

                        // Check for reels feed to load
                        bool isLoaded = false;
                        int loadRetries = 0;
                        while (!isLoaded && loadRetries < 5)
                        {
                            var loadCheck = await webView.ExecuteScriptAsync("document.querySelectorAll('a[href*=\"/reel/\"]').length > 0 ? 'true' : 'false';");
                            isLoaded = JsBoolIsTrue(loadCheck);
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

                        await RandomHumanNoiseAsync(token);

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

                        if (!JsBoolIsTrue(openedCheck))
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

                        if (!JsBoolIsTrue(openedCheck))
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

                            // Extract reel ID
                            var reelId = await webView.ExecuteScriptAsync(reelIdScript);
                            reelId = reelId?.Trim('"').Trim();
                            logTextBox.AppendText($"[REEL_ID] {reelId}\r\n");

                            // Extraction date
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

                            await RandomHumanPauseAsync(token);

                            // Petit filtre pour skipper les vieux reels avec 67% de chance
                            bool shouldComment = false;
                            bool isOld = false;
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
                                            if (age.TotalHours < 24)
                                            {
                                                shouldComment = true;
                                            }
                                            else
                                            {
                                                isOld = true;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logTextBox.AppendText($"[DATE_PARSE_ERROR] {ex.Message}\r\n");
                                }
                            }

                            while (isOld && rand.NextDouble() < 0.67 && reelNum < maxReels)
                            {
                                int skipDelay = rand.Next(500, 2501);
                                logTextBox.AppendText($"[SKIP] Waiting {skipDelay}ms then skipping old reel...\r\n");
                                await Task.Delay(skipDelay, token);

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
                                    nextScript = $@"
(async function(){{
  try {{
    var info = [];
    
    var dialog = document.querySelector('div[role=""dialog""]');
    if (!dialog) return 'NO_DIALOG';
    
    // Stratégie 1: Chercher par aria-label
    var nextBtn = Array.from(dialog.querySelectorAll('button')).find(b => {{
      var label = (b.getAttribute('aria-label') || '').toLowerCase();
      return /next|suivant|nextpage|nextitem|flèche/.test(label);
    }});
    info.push('Stratégie 1 (aria-label): ' + (nextBtn ? 'FOUND' : 'NOT_FOUND'));
    
    // Stratégie 2: Boutons de taille 32x32
    if (!nextBtn) {{
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {{
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0 && Math.round(rect.width) === 32 && Math.round(rect.height) === 32;
      }});
      if (allBtns.length >= 1) {{
        // Trier par position x pour prendre le plus à droite (next)
        allBtns.sort((a, b) => a.getBoundingClientRect().left - b.getBoundingClientRect().left);
        nextBtn = allBtns[allBtns.length - 1]; // Prendre le dernier (plus à droite)
        info.push('Stratégie 2 (32x32 buttons): FOUND ' + allBtns.length + ', picked rightmost');
      }} else {{
        info.push('Stratégie 2 (32x32 buttons): NOT_FOUND');
      }}
    }}
    
    // Stratégie 3: 2e bouton visible si toujours pas trouvé
    if (!nextBtn) {{
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {{
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0;
      }});
      if (allBtns.length >= 2) {{
        nextBtn = allBtns[1];
        info.push('Stratégie 3 (2nd button): FOUND');
      }} else {{
        info.push('Stratégie 3 (2nd button): NOT_FOUND (only ' + allBtns.length + ' visible buttons)');
      }}
    }}
    
    if (!nextBtn) {{
      info.push('RESULT: NO_NEXT_BUTTON');
      return info.join('\\n');
    }}
    
    var rect = nextBtn.getBoundingClientRect();
    info.push('\\nButton details:');
    info.push('  Size: ' + Math.round(rect.width) + 'x' + Math.round(rect.height));
    info.push('  Position: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    info.push('  Visible: ' + (rect.width > 0 && rect.height > 0));
    
    // Scroll into view
    nextBtn.scrollIntoView({{block: 'nearest', inline: 'nearest'}});
    await new Promise(r => setTimeout(r, 100));
    
    // Recalculer après scroll
    rect = nextBtn.getBoundingClientRect();
    info.push('  After scroll: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    
    // Calculer les coordonnées du click
    var centerX = rect.left + rect.width / 2;
    var centerY = rect.top + rect.height / 2;
    var offsetX = (Math.random() - 0.5) * 10;
    var offsetY = (Math.random() - 0.5) * 10;
    var clientX = Math.floor(centerX + offsetX);
    var clientY = Math.floor(centerY + offsetY);
    
    // Clamp dans les limites
    clientX = Math.max(rect.left + 2, Math.min(clientX, rect.left + rect.width - 2));
    clientY = Math.max(rect.top + 2, Math.min(clientY, rect.top + rect.height - 2));
    
    info.push('\\nClick coordinates:');
    info.push('  Center: ' + Math.round(centerX) + ',' + Math.round(centerY));
    info.push('  Final: ' + clientX + ',' + clientY);
    
    // Mouvement souris simulé
    var startX = clientX + (Math.random() * 60 - 30);
    var startY = clientY + (Math.random() * 60 - 30);
    
    for (let i = 1; i <= 4; i++) {{
      var moveX = startX + (clientX - startX) * (i / 4);
      var moveY = startY + (clientY - startY) * (i / 4);
      nextBtn.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
      await new Promise(r => setTimeout(r, Math.random() * 30 + 20));
    }}
    
    await new Promise(r => setTimeout(r, Math.random() * 100 + 50));
    
    // Events
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    nextBtn.dispatchEvent(new MouseEvent('mouseenter', opts));
    nextBtn.dispatchEvent(new MouseEvent('mouseover', opts));
    nextBtn.dispatchEvent(new MouseEvent('mousedown', opts));
    await new Promise(r => setTimeout(r, Math.random() * 80 + 30));
    nextBtn.dispatchEvent(new MouseEvent('mouseup', opts));
    nextBtn.dispatchEvent(new MouseEvent('click', opts));
    await new Promise(r => setTimeout(r, Math.random() * 50 + 25));
    nextBtn.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    info.push('\\nCLICK_SENT');
    return info.join('\\n');
  }} catch(e) {{
    return 'EXCEPTION: ' + e.message;
  }}
}})()";
                                }

                                var nextTry = await webView.ExecuteScriptAsync(nextScript);
                                logTextBox.AppendText($"[SKIP] {nextTry}\r\n");

                                

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

                                    if (newReelId != reelId && newReelId != "NO_ID" && JsBoolIsTrue(checkAdvanced))
                                    {
                                        logTextBox.AppendText("[SKIP] ✓ Successfully advanced to next reel\r\n");
                                        break;
                                    }

                                    logTextBox.AppendText($"[SKIP RETRY {retryCount + 1}] Stuck on {reelId}, retrying...\r\n");

                                    logTextBox.AppendText("[SKIP RETRY] Forcing ArrowRight fallback...\r\n");
                                    nextScript = @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_RETRY';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";

                                    nextTry = await webView.ExecuteScriptAsync(nextScript);
                                    logTextBox.AppendText($"[SKIP RETRY] {nextTry}\r\n");


                                    retryCount++;
                                }

                                if (retryCount >= maxRetries)
                                {
                                    logTextBox.AppendText($"[SKIP ERROR] Max retries reached, stuck on {reelId}. Stopping reel loop.\r\n");
                                    break;
                                }

                                // Re-extract id and date
                                reelId = newReelId;
                                logTextBox.AppendText($"[REEL_ID] New: {reelId}\r\n");

                                reelDateRaw = await webView.ExecuteScriptAsync(dateScript);
                                try
                                {
                                    reelDate = JsonSerializer.Deserialize<string>(reelDateRaw);
                                }
                                catch (JsonException ex)
                                {
                                    logTextBox.AppendText($"[DATE_DESERIALIZE_ERROR] {ex.Message}\r\n");
                                    reelDate = "NO_DATE_FOUND";
                                }
                                logTextBox.AppendText($"[DATE] New: {reelDate}\r\n");

                                // Re-compute shouldComment and isOld
                                shouldComment = false;
                                isOld = false;
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
                                                if (age.TotalHours < 24)
                                                {
                                                    shouldComment = true;
                                                }
                                                else
                                                {
                                                    isOld = true;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logTextBox.AppendText($"[DATE_PARSE_ERROR] {ex.Message}\r\n");
                                    }
                                }
                            }

                            // Watch delay with possible pause
                            int watchTime = rand.Next(5000, 10001);
                            await Task.Delay(watchTime / 2, token);  // Watch half first

                            if (rand.NextDouble() < 0.15)  // 15% chance to pause mid-watch
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

                                await Task.Delay(rand.Next(2000, 8000), token);  // Pause for 2-8s

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

                            await Task.Delay(watchTime / 2, token);  // Finish watching

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

                            // Comment (si < 24h)
                            if (shouldComment)
                            {
                                string randomComment = comments[rand.Next(comments.Count)];
                                logTextBox.AppendText($"[COMMENT] Sélectionné: '{randomComment}'\r\n");
                                logTextBox.AppendText($"[TYPING] Starting...\r\n");

                                // Échapper TOUS les types d'apostrophes et guillemets
                                var escapedComment = randomComment
                                    .Replace("\\", "\\\\")
                                    .Replace("\u2018", "'")
                                    .Replace("\u2019", "'")
                                    .Replace("'", "\\'");

                                // SCRIPT EXACTEMENT COMME TESTSERVICE - QUI FONCTIONNE
                                var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  // Utilitaire pour générer un délai aléatoire entre min et max ms
  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}
  
  const dlg = document.querySelector('div[role=""dialog""]');
  const root = dlg || document;
  
  // Texte à taper
  const text = '{escapedComment}';
  const chars = Array.from(text);
  
  // Focus initial
  let ta = (root.querySelector('div[role=""dialog""] form textarea'))
        || (root.querySelector('form textarea'))
        || root.querySelector('textarea');
  let ce = null;
  
  if (!ta) {{
    ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
    if (!ce) return 'NO_COMPOSER_INITIAL';
  }}
  
  const initialTarget = ta || ce;
  
  initialTarget.scrollIntoView({{behavior:'smooth', block:'center'}});
  await sleep(randomDelay(200, 400));
  
  const rect = initialTarget.getBoundingClientRect();
  var marginX = rect.width * 0.2;
  var marginY = rect.height * 0.2;
  var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
  var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
  const clientX = rect.left + offsetX;
  const clientY = rect.top + offsetY;
  
  // Simulate mouse approach: 3-5 move events towards the target
  var startX = clientX + (Math.random() * 100 - 50);  // Start offset
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
  
  for (let i = 0; i < chars.length; i++) {{
    const char = chars[i];
    
    // Re-trouver l'élément à chaque itération
    ta = (root.querySelector('div[role=""dialog""] form textarea'))
          || (root.querySelector('form textarea'))
          || root.querySelector('textarea');
    ce = null;
    
    if (!ta) {{
      ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
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
    
    // Délai entre les caractères (vitesse de frappe variable)
    let delay;
    
    // Pause plus longue après ponctuation
    if (char === ',' || char === ';') {{
      delay = randomDelay(200, 400);
    }} else if (char === '.' || char === '!' || char === '?') {{
      delay = randomDelay(300, 500);
    }} else if (char === ' ') {{
      delay = randomDelay(80, 150);
    }} else {{
      // Caractères normaux: vitesse variable
      delay = randomDelay(50, 150);
    }}
    
    // Chance de faire une erreur (5%)
    if (Math.random() < 0.05 && i < chars.length - 1) {{
      await sleep(delay);
      
      // Taper un mauvais caractère
      const wrongChars = 'qwertyuiopasdfghjklzxcvbnm';
      const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
      
      // Re-trouver pour erreur
      ta = (root.querySelector('div[role=""dialog""] form textarea'))
            || (root.querySelector('form textarea'))
            || root.querySelector('textarea');
      ce = null;
      
      if (!ta) {{
        ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
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
      
      // Petit délai avant de réaliser l'erreur
      await sleep(randomDelay(100, 250));
      
      // Supprimer le mauvais caractère (Backspace)
      // Re-trouver pour delete
      ta = (root.querySelector('div[role=""dialog""] form textarea'))
            || (root.querySelector('form textarea'))
            || root.querySelector('textarea');
      ce = null;
      
      if (!ta) {{
        ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
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
      
      // Petit délai après correction
      await sleep(randomDelay(50, 120));
    }}
    
    // Hésitation aléatoire (2% de chance)
    if (Math.random() < 0.02) {{
      await sleep(randomDelay(400, 800));
    }}
    
    await sleep(delay);
  }}
  
  // Petit délai final
  await sleep(randomDelay(300, 600));
  
  return 'TYPED_SUCCESSFULLY';
}})()";

                                // Calculer temps d'attente
                                int charCount = randomComment.Length;
                                int baseTime = charCount * 100;
                                int punctuationCount = randomComment.Count(c => ".!?,;".Contains(c));
                                int punctuationDelay = punctuationCount * 300;
                                int errorDelay = (int)(charCount * 0.05 * 500);
                                int totalTime = baseTime + punctuationDelay + errorDelay + 3000;

                                // Lancer le script et attendre
                                var typingTask = webView.ExecuteScriptAsync(typingScript);

                                logTextBox.AppendText($"[TYPING] Attente de {totalTime}ms...\r\n");
                                await Task.Delay(totalTime, token);

                                var typingResult = await typingTask;
                                logTextBox.AppendText($"[TYPING] Résultat: {typingResult}\r\n");

                                // Attendre que le bouton se débloque
                                await Task.Delay(rand.Next(1500, 2500), token);

                                // PUBLISH
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
    const wrap = form.querySelector('.x13fj5qh');
    if (wrap){{
      const inside = [...wrap.querySelectorAll('button,[role=""button""]')].find(Boolean);
      if (inside) return inside;
    }}
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

  const dlg = document.querySelector('div[role=""dialog""]');
  const root = dlg || document;
  const form = root.querySelector('form');
  
  const ctrl = findPublishControl(form);
  if (!ctrl) return 'NO_CTRL';
  
  const ok = await waitEnabled(ctrl, 10000);
  if (!ok) return 'CTRL_DISABLED_TIMEOUT';
  
  const btnRect = ctrl.getBoundingClientRect();
  var marginX = btnRect.width * 0.2;
  var marginY = btnRect.height * 0.2;
  var offsetX = marginX + Math.random() * (btnRect.width - 2 * marginX);
  var offsetY = marginY + Math.random() * (btnRect.height - 2 * marginY);
  const btnX = btnRect.left + offsetX;
  const btnY = btnRect.top + offsetY;
  
  // Simulate mouse approach: 3-5 move events towards the target
  var startX = btnX + (Math.random() * 100 - 50);  // Start offset
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
  
  const t0 = performance.now();
  while (performance.now() - t0 < 12000) {{
    const scope = document.querySelector('div[role=""dialog""]') || document;
    const ta2 = scope.querySelector('form textarea');
    if (!ta2 || ta2.value.trim().length === 0) break;
    await sleep(220);
  }}
  
  return 'PUBLISHED';
}})()";

                                var publishResult = await webView.ExecuteScriptAsync(publishScript);
                                logTextBox.AppendText($"[PUBLISH] {publishResult}\r\n");

                                await Task.Delay(rand.Next(1200, 2201), token);
                            }
                            else
                            {
                                logTextBox.AppendText("[COMMENT] Skipped: Reel older than 24 hours or no date.\r\n");
                            }

                            await RandomHumanPauseAsync(token);

                            await RandomHumanNoiseAsync(token);

                            // NEXT si pas le dernier
                            if (reelNum < maxReels)
                            {
                                // Attendre que le modal soit stable (surtout important pour le 1er reel)
                                if (reelNum == 1)
                                {
                                    await Task.Delay(rand.Next(800, 1500), token);
                                }

                                nextClickCounter++;

                                // Délai pré-action aléatoire
                                int preDelay = rand.Next(800, 2000);
                                logTextBox.AppendText($"[NEXT] Waiting {preDelay}ms before action...\r\n");
                                await Task.Delay(preDelay, token);

                                // D'abord: debug pour voir quels boutons existent
                                var debugScript = $@"
(function(){{
  try{{
    var scope = document.querySelector('div[role=""dialog""]') || document;
    
    // Chercher tous les boutons possibles
    var allButtons = Array.from(scope.querySelectorAll('button, [role=""button""]'));
    var buttonInfo = allButtons.map(b => {{
      return {{
        tag: b.tagName,
        ariaLabel: b.getAttribute('aria-label') || 'NO_LABEL',
        text: b.innerText?.substring(0, 20) || '',
        visible: b.offsetWidth > 0
      }};
    }});
    
    return JSON.stringify(buttonInfo);
  }} catch(e){{
    return 'DEBUG_ERR:' + String(e);
  }}
}})()";

                                var debugResult = await webView.ExecuteScriptAsync(debugScript);
                                logTextBox.AppendText($"[NEXT] Debug buttons: {debugResult}\r\n");

                                // 1 fois sur 15, utiliser ArrowRight, sinon clic humain
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
                                    // Clic humain sur le bouton avec coordonnées aléatoires
                                    nextScript = $@"
(async function(){{
  try {{
    var info = [];
    
    var dialog = document.querySelector('div[role=""dialog""]');
    if (!dialog) return 'NO_DIALOG';
    
    // Stratégie 1: Chercher par aria-label
    var nextBtn = Array.from(dialog.querySelectorAll('button')).find(b => {{
      var label = (b.getAttribute('aria-label') || '').toLowerCase();
      return /next|suivant|nextpage|nextitem|flèche/.test(label);
    }});
    info.push('Stratégie 1 (aria-label): ' + (nextBtn ? 'FOUND' : 'NOT_FOUND'));
    
    // Stratégie 2: Boutons de taille 32x32
    if (!nextBtn) {{
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {{
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0 && Math.round(rect.width) === 32 && Math.round(rect.height) === 32;
      }});
      if (allBtns.length >= 1) {{
        // Trier par position x pour prendre le plus à droite (next)
        allBtns.sort((a, b) => a.getBoundingClientRect().left - b.getBoundingClientRect().left);
        nextBtn = allBtns[allBtns.length - 1]; // Prendre le dernier (plus à droite)
        info.push('Stratégie 2 (32x32 buttons): FOUND ' + allBtns.length + ', picked rightmost');
      }} else {{
        info.push('Stratégie 2 (32x32 buttons): NOT_FOUND');
      }}
    }}
    
    // Stratégie 3: 2e bouton visible si toujours pas trouvé
    if (!nextBtn) {{
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {{
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0;
      }});
      if (allBtns.length >= 2) {{
        nextBtn = allBtns[1];
        info.push('Stratégie 3 (2nd button): FOUND');
      }} else {{
        info.push('Stratégie 3 (2nd button): NOT_FOUND (only ' + allBtns.length + ' visible buttons)');
      }}
    }}
    
    if (!nextBtn) {{
      info.push('RESULT: NO_NEXT_BUTTON');
      return info.join('\\n');
    }}
    
    var rect = nextBtn.getBoundingClientRect();
    info.push('\\nButton details:');
    info.push('  Size: ' + Math.round(rect.width) + 'x' + Math.round(rect.height));
    info.push('  Position: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    info.push('  Visible: ' + (rect.width > 0 && rect.height > 0));
    
    // Scroll into view
    nextBtn.scrollIntoView({{block: 'nearest', inline: 'nearest'}});
    await new Promise(r => setTimeout(r, 100));
    
    // Recalculer après scroll
    rect = nextBtn.getBoundingClientRect();
    info.push('  After scroll: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    
    // Calculer les coordonnées du click
    var centerX = rect.left + rect.width / 2;
    var centerY = rect.top + rect.height / 2;
    var offsetX = (Math.random() - 0.5) * 10;
    var offsetY = (Math.random() - 0.5) * 10;
    var clientX = Math.floor(centerX + offsetX);
    var clientY = Math.floor(centerY + offsetY);
    
    // Clamp dans les limites
    clientX = Math.max(rect.left + 2, Math.min(clientX, rect.left + rect.width - 2));
    clientY = Math.max(rect.top + 2, Math.min(clientY, rect.top + rect.height - 2));
    
    info.push('\\nClick coordinates:');
    info.push('  Center: ' + Math.round(centerX) + ',' + Math.round(centerY));
    info.push('  Final: ' + clientX + ',' + clientY);
    
    // Mouvement souris simulé
    var startX = clientX + (Math.random() * 60 - 30);
    var startY = clientY + (Math.random() * 60 - 30);
    
    for (let i = 1; i <= 4; i++) {{
      var moveX = startX + (clientX - startX) * (i / 4);
      var moveY = startY + (clientY - startY) * (i / 4);
      nextBtn.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
      await new Promise(r => setTimeout(r, Math.random() * 30 + 20));
    }}
    
    await new Promise(r => setTimeout(r, Math.random() * 100 + 50));
    
    // Events
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    nextBtn.dispatchEvent(new MouseEvent('mouseenter', opts));
    nextBtn.dispatchEvent(new MouseEvent('mouseover', opts));
    nextBtn.dispatchEvent(new MouseEvent('mousedown', opts));
    await new Promise(r => setTimeout(r, Math.random() * 80 + 30));
    nextBtn.dispatchEvent(new MouseEvent('mouseup', opts));
    nextBtn.dispatchEvent(new MouseEvent('click', opts));
    await new Promise(r => setTimeout(r, Math.random() * 50 + 25));
    nextBtn.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    info.push('\\nCLICK_SENT');
    return info.join('\\n');
  }} catch(e) {{
    return 'EXCEPTION: ' + e.message;
  }}
}})()";
                                }

                                var nextTry = await webView.ExecuteScriptAsync(nextScript);
                                logTextBox.AppendText($"[NEXT] {nextTry}\r\n");

                                // Attendre le chargement du reel suivant
                                int loadDelay = rand.Next(2500, 4500);
                                logTextBox.AppendText($"[NEXT] Waiting {loadDelay}ms for next reel to load...\r\n");
                                await Task.Delay(loadDelay, token);

                                // Post-next verification
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

                                    if (newReelId != previousReelId && newReelId != "NO_ID" && JsBoolIsTrue(checkAdvanced))
                                    {
                                        logTextBox.AppendText("[NEXT] ✓ Successfully advanced to next reel\r\n");
                                        break;
                                    }

                                    logTextBox.AppendText($"[NEXT RETRY {retryCount + 1}] Stuck on {previousReelId}, retrying...\r\n");

                                    // Retry debug
                                    debugResult = await webView.ExecuteScriptAsync(debugScript);
                                    logTextBox.AppendText($"[NEXT RETRY] Debug buttons: {debugResult}\r\n");

                                    // Sur retry, forcer le fallback ArrowRight
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

                                    // Retry load delay
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

                                // Vérifier modal toujours ouvert
                                var stillOpened = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  return hasDialog.toString();
})()");
                                if (!JsBoolIsTrue(stillOpened))
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
                        try
                        {
                            File.AppendAllText(doneTargetsPath, currentTarget + Environment.NewLine);
                            logTextBox.AppendText($"[DONE_TARGETS] Ajouté à done_targets.txt : {currentTarget}\r\n");
                        }
                        catch (Exception ex)
                        {
                            logTextBox.AppendText($"[DONE_TARGETS ERROR] Impossible d'ajouter {currentTarget} : {ex.Message}\r\n");
                        }

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
    }
}