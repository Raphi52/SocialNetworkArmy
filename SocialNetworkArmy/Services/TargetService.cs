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
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;

        public TargetService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
        }


        private static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            // Sécurité : s’assurer que CoreWebView2 est prêt même si on est appelé tôt
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Target");
                var localToken = form.GetCancellationToken(); // Récupérer le token depuis le form
                token = localToken; // Utiliser ce token pour la cancellation

                try
                {
                    // 1) Charger la liste des cibles
                    var targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "targets.txt");
                    var targets = new System.Collections.Generic.List<string>();
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
                    if (!targets.Any())
                    {
                        logTextBox.AppendText("Aucun target trouvé dans targets.txt !\r\n");
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
                        // Ajouter des commentaires par défaut si vide
                        comments = new string[] { "Super ! ??", "J'adore ! ??", "Trop cool ! ??", "Impressionnant !", "Bien vu ! ??", "Top ! ??" }.ToList();
                    }

                    // Charger le schedule CSV sans helper (lecture simple des lignes, parsing manuel si besoin)
                    var schedulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "publish_schedule.csv");
                    var scheduleLines = new System.Collections.Generic.List<string>();
                    if (File.Exists(schedulePath))
                    {
                        scheduleLines = File.ReadAllLines(schedulePath)
                                            .Where(line => !string.IsNullOrWhiteSpace(line))
                                            .Select(line => line.Trim())
                                            .ToList();
                        logTextBox.AppendText($"[SCHEDULE] Chargé {scheduleLines.Count} lignes du CSV publish_schedule.csv.\r\n");
                        // Si besoin de parser en objets, ajoutez ici un parsing manuel (ex: split par virgule)
                        // Par exemple:
                        // foreach (var line in scheduleLines.Skip(1)) { var parts = line.Split(','); ... }
                    }
                    else
                    {
                        logTextBox.AppendText($"[SCHEDULE] Fichier publish_schedule.csv non trouvé à {schedulePath} !\r\n");
                    }

                    Random rand = new Random();

                    // Detect language by navigating to home page
                    webView.CoreWebView2.Navigate("https://www.instagram.com/");
                    await Task.Delay(rand.Next(4000, 7001), token);
                    var langResult = await webView.ExecuteScriptAsync("document.documentElement.lang;");
                    var lang = langResult?.Trim('"') ?? "en";
                    logTextBox.AppendText($"[LANG] Detected language: {lang}\r\n");

                    string likeSelectors = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"svg[aria-label=""J\u2019aime""], svg[aria-label=""Je n\u2019aime plus""]" : @"svg[aria-label=""Like""], svg[aria-label=""Unlike""]";
                    string unlikeSelectors = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"svg[aria-label=""Je n\u2019aime plus""], svg[aria-label=""Je n'aime plus""]" : @"svg[aria-label=""Unlike""]";
                    string unlikeTest = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? @"n\u2019aime plus" : "unlike";
                    string publishPattern = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "publier|envoyer" : "post|send";
                    string nextLabel = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Suivant" : "Next";
                    string commentLabel = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Commenter" : "Comment"; // In case needed later

                    foreach (var target in targets)
                    {
                        token.ThrowIfCancellationRequested();

                        var currentTarget = target.Trim();
                        logTextBox.AppendText($"[TARGET] Processing {currentTarget}\r\n");

                        int maxReels = rand.Next(5, 11); // Random between 5 and 10 inclusive
                        logTextBox.AppendText($"[TARGET] Will process {maxReels} reels for this target.\r\n");

                        // 3) Aller sur la page Reels du target
                        var targetUrl = $"https://www.instagram.com/{currentTarget}/reels/";
                        webView.CoreWebView2.Navigate(targetUrl);

                        // 4) Attendre un peu que la navigation se stabilise
                        await Task.Delay(3000, token);

                        // Check for reels feed to load before proceeding
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

                        // 6) Vérifier login (mur de connexion)
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

                        // 7) Sélecteur 1er Reel (priorité grid <article>)
                        var findReelScript = @"
(function(){
  const a = document.querySelector('article a[href*=""/reel/""]')
        || document.querySelector('a[href*=""/reel/""]');
  return a ? a.href : null;
})()";
                        var reelHref = await webView.ExecuteScriptAsync(findReelScript);

                        // 8) Lazy-load par scroll si rien
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

                        // 9) Si toujours rien ? abort propre
                        if (reelHref == "null")
                        {
                            logTextBox.AppendText("[ERREUR] Aucun Reel détecté sur la page du profil.\r\n");
                            continue; // Passer au target suivant
                        }

                        // 10) Tentative 1 : click simple (peut suffire à ouvrir le MODAL)
                        var clickSimple = await webView.ExecuteScriptAsync(@"
(function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  el.click();
  return 'CLICKED';
})()");

                        // 11) Check ouverture (URL / modal / vidéo)
                        await Task.Delay(3000, token); // Délai augmenté pour chargement
                        var openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");

                        if (!JsBoolIsTrue(openedCheck))
                        {
                            // 12bis) Sinon, MouseEvents SANS 'click' (comportement qui t’allait bien)
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

                            // Re-vérifier ouverture
                            await Task.Delay(3000, token); // Délai augmenté
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
                            logTextBox.AppendText("[KO] Impossible d’ouvrir le 1er Reel (sélecteur/clic).\r\n");
                            continue; // Passer au target suivant
                        }

                        // ======================= 12) BOUCLE POUR REELS (LIKE + COMMENT + NEXT) =======================
                        // Refactorisé en boucle C# pour plus de contrôle et logs par étape (moins fragile)
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

                            // Extraction de la date
                            var reelDateRaw = await webView.ExecuteScriptAsync(dateScript);

                            // Fix: Properly deserialize the returned JSON string to get the unescaped inner JSON
                            string reelDate;
                            try
                            {
                                reelDate = JsonSerializer.Deserialize<string>(reelDateRaw);
                            }
                            catch (JsonException ex)
                            {
                                logTextBox.AppendText($"[DATE_DESERIALIZE_ERROR] {ex.Message}\r\n");
                                reelDate = "NO_DATE_FOUND"; // Fallback
                            }

                            logTextBox.AppendText($"[DATE] {reelDate}\r\n");

                            // Watch delay (5-10s random)
                            await Task.Delay(rand.Next(5000, 10001), token);

                            // Décider si liker (9% de chance)
                            bool shouldLike = rand.NextDouble() < 0.09;
                            if (shouldLike)
                            {
                                // LIKE (greffé du code qui marche)
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
    try{{ el.style.border = '2px solid red'; setTimeout(function(){{ try{{ el.style.border = ''; }}catch(_){{}} }}, 5000); }}catch(_){{}}
    
    try{{ el.click(); }}catch(_){{}}
    if (liked()) return 'OK:CLICK ' + picked_info;

    // Fallback elementFromPoint
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

                            // Déterminer si on doit commenter
                            bool shouldComment = false;
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
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logTextBox.AppendText($"[DATE_PARSE_ERROR] {ex.Message}\r\n");
                                }
                            }

                            if (shouldComment)
                            {
                                // COMMENT (adapté du code qui marche, avec random de txt ou default)
                                string randomComment = comments[rand.Next(comments.Count)];
                                logTextBox.AppendText($"[COMMENT] Sélectionné: '{randomComment}'\r\n");

                                var commentTry = await webView.ExecuteScriptAsync($@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  const rand = (a,b) => Math.floor(a + Math.random()*(b-a+1));

  function setNativeValue(el, value){{
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype
                : el instanceof HTMLInputElement   ? HTMLInputElement.prototype
                : null;
    if (!proto) {{ el.value = value; el.dispatchEvent(new Event('input',{{bubbles:true}})); return; }}
    const desc = Object.getOwnPropertyDescriptor(proto, 'value');
    desc.set.call(el, value);
    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
  }}

  async function clickCenter(el){{
    el.scrollIntoView({{behavior:'smooth', block:'center'}});
    await sleep(120);
    const r = el.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    const opts = {{bubbles:true, cancelable:true, clientX:x, clientY:y, button:0}};
    el.dispatchEvent(new MouseEvent('mousedown', opts));
    el.dispatchEvent(new MouseEvent('mouseup',   opts));
    el.dispatchEvent(new MouseEvent('click',     opts));
    return 'OK';
  }}

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

  async function waitEnabled(el, timeout=8000){{
    const t0 = performance.now();
    while (performance.now() - t0 < timeout){{
      if (btnEnabled(el)) return true;
      await sleep(120);
    }}
    return false;
  }}

  const dlg  = document.querySelector('div[role=""dialog""]');
  const root = dlg || document;

  let ta = (root.querySelector('div[role=""dialog""] form textarea'))
        || (root.querySelector('form textarea'))
        || root.querySelector('textarea');
  let ce = null;
  if (!ta){{
    ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
    if (!ce) return 'NO_COMPOSER';
  }}

  const form = (ta && ta.closest('form')) || (ce && ce.closest('form')) || root.querySelector('form');
  const text = '{randomComment.Replace("'", "\\'")}';

  if (ta){{
    if (await clickCenter(ta)==='STOP') return 'STOP';
    ta.focus();
    setNativeValue(ta, text);
  }} else {{
    if (await clickCenter(ce)==='STOP') return 'STOP';
    ce.focus();
    document.execCommand('insertText', false, text);
    ce.dispatchEvent(new Event('input', {{bubbles:true}}));
  }}

  await sleep(rand(180, 360));

  const ctrl = findPublishControl(form);
  if (!ctrl) return 'NO_CTRL';
  const ok = await waitEnabled(ctrl, 8000);
  if (!ok) return 'CTRL_DISABLED';

  await clickCenter(ctrl);

  const t0 = performance.now();
  while (performance.now() - t0 < 12000){{
    const scope = document.querySelector('div[role=""dialog""]') || document;
    const ta2 = scope.querySelector('form textarea');
    if (!ta2 || ta2.value.trim().length === 0) break;
    await sleep(220);
  }}
  return 'COMMENTED';
}})();");
                                logTextBox.AppendText($"[COMMENT] {commentTry}\r\n");

                                await Task.Delay(rand.Next(1200, 2201), token); // Délai après comment
                            }
                            else
                            {
                                logTextBox.AppendText("[COMMENT] Skipped: Reel older than 24 hours or no date.\r\n");
                            }

                            // NEXT si pas le dernier
                            if (reelNum < maxReels)
                            {
                                var nextScript = $@"
(function(){{
  try{{
    var scope = document.querySelector('div[role=""dialog""]') || document;
    var next = scope.querySelector('button[aria-label=""{nextLabel}""]')
            || scope.querySelector('[role=""button""][aria-label*=""{nextLabel}""]');

    if (next){{
      next.dispatchEvent(new MouseEvent('mousedown', {{bubbles:true}}));
      next.dispatchEvent(new MouseEvent('mouseup',   {{bubbles:true}}));
    }} else {{
      document.body.dispatchEvent(new KeyboardEvent('keydown', {{key:'ArrowRight', code:'ArrowRight', bubbles:true}}));
      document.body.dispatchEvent(new KeyboardEvent('keyup',   {{key:'ArrowRight', code:'ArrowRight', bubbles:true}}));
    }}
    return 'OK';
  }}catch(e){{ return 'JSERR: ' + String(e); }}
}})()";
                                var nextTry = await webView.ExecuteScriptAsync(nextScript);
                                logTextBox.AppendText($"[NEXT] {nextTry}\r\n");

                                await Task.Delay(rand.Next(600, 1001), token); // Délai après next

                                // Post-next verification
                                int retryCount = 0;
                                const int maxRetries = 3;
                                string newReelId = null;
                                while (retryCount < maxRetries)
                                {
                                    await Task.Delay(rand.Next(1500, 3000), token); // Wait for load
                                    newReelId = await webView.ExecuteScriptAsync(reelIdScript);
                                    newReelId = newReelId?.Trim('"').Trim();

                                    if (newReelId != previousReelId && newReelId != "NO_ID")
                                    {
                                        break; // Successfully advanced
                                    }

                                    logTextBox.AppendText($"[NEXT RETRY {retryCount + 1}] Stuck on {previousReelId}, retrying...\r\n");

                                    // Retry next
                                    nextTry = await webView.ExecuteScriptAsync(nextScript);
                                    logTextBox.AppendText($"[NEXT RETRY] {nextTry}\r\n");

                                    retryCount++;
                                }

                                if (retryCount >= maxRetries)
                                {
                                    logTextBox.AppendText($"[NEXT ERROR] Max retries reached, still stuck on {previousReelId}. Stopping reel loop.\r\n");
                                    break; // Break the reel loop
                                }

                                // Vérifier si toujours en modal
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

                            previousReelId = reelId; // Update for next iteration
                        }

                        logTextBox.AppendText($"[TARGET] Terminé pour {currentTarget}.\r\n");

                        // Délai entre targets
                        await Task.Delay(rand.Next(5000, 15000), token);
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