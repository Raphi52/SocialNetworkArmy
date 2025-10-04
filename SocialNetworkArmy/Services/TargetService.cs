using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Linq;
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

        public async Task RunAsync()
        {
            // Sécurité : s’assurer que CoreWebView2 est prêt même si on est appelé tôt
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Target");

                try
                {
                    // 1) Charger la liste des cibles
                    var targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "targets.txt");
                    var targets = Helpers.LoadTargets(targetsPath);
                    if (!targets.Any())
                    {
                        logTextBox.AppendText("Aucun target trouvé dans targets.txt !\r\n");
                        form.StopScript();
                        return;
                    }

                    // 1bis) Charger commentaires
                    var commentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "comments.txt");
                    var comments = Helpers.LoadTargets(commentsPath);
                    if (!comments.Any())
                    {
                        logTextBox.AppendText("Aucun commentaire trouvé dans comments.txt ! Utilisation de commentaires par défaut.\r\n");
                        // Ajouter des commentaires par défaut si vide
                        comments = new string[] { "Super ! ??", "J'adore ! ??", "Trop cool ! ??", "Impressionnant !", "Bien vu ! ??", "Top ! ??" }.ToList();
                    }

                    // 2) (Optionnel) ouvrir DevTools
                    try { webView.CoreWebView2?.OpenDevToolsWindow(); } catch { /* ignore */ }

                    // 3) Aller sur la page Reels du 1er target
                    var firstTarget = targets.First().Trim();
                    var targetUrl = $"https://www.instagram.com/{firstTarget}/reels/";
                    logTextBox.AppendText($"[NAV] Vers {targetUrl}\r\n");
                    webView.CoreWebView2.Navigate(targetUrl);

                    // 4) Attendre un peu que la navigation se stabilise
                    await Task.Delay(3000);

                    // 5) Lire l’URL et le titre (diagnostic)
                    var url = await webView.ExecuteScriptAsync("window.location.href");
                    var title = await webView.ExecuteScriptAsync("document.title");
                    logTextBox.AppendText($"[NAV] url={url}, title={title}\r\n");

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
                    logTextBox.AppendText($"[SELECTOR] 1er reel href (avant scroll) = {reelHref}\r\n");

                    // 8) Lazy-load par scroll si rien
                    if (reelHref == "null")
                    {
                        logTextBox.AppendText("[SCROLL] Lazy-load…\r\n");
                        await webView.ExecuteScriptAsync(@"
(async function(){
  for(let i=0;i<6;i++){
    window.scrollBy(0, window.innerHeight);
    await new Promise(r => setTimeout(r, 800));
  }
  return true;
})()");
                        await Task.Delay(1000);

                        reelHref = await webView.ExecuteScriptAsync(findReelScript);
                        logTextBox.AppendText($"[SELECTOR] 1er reel href (après scroll) = {reelHref}\r\n");
                    }

                    // 9) Si toujours rien ? abort propre
                    if (reelHref == "null")
                    {
                        logTextBox.AppendText("[ERREUR] Aucun Reel détecté sur la page du profil.\r\n");
                        form.StopScript();
                        return;
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
                    logTextBox.AppendText($"[CLICK_SIMPLE] résultat={clickSimple}\r\n");

                    // 11) Check ouverture (URL / modal / vidéo)
                    await Task.Delay(3000); // Délai augmenté pour chargement
                    var openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                    logTextBox.AppendText($"[CHECK_OPENED] après click simple => {openedCheck}\r\n");

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
                        logTextBox.AppendText($"[CLICK_MOUSE] résultat={clickMouseEvents}\r\n");

                        // Re-vérifier ouverture
                        await Task.Delay(3000); // Délai augmenté
                        openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                        logTextBox.AppendText($"[CHECK_OPENED] après mouse events => {openedCheck}\r\n");
                    }

                    if (!JsBoolIsTrue(openedCheck))
                    {
                        logTextBox.AppendText("[KO] Impossible d’ouvrir le 1er Reel (sélecteur/clic).\r\n");
                        form.StopScript();
                        return;
                    }

                    // ======================= 12) BOUCLE POUR REELS (LIKE + COMMENT + NEXT) =======================
                    // Refactorisé en boucle C# pour plus de contrôle et logs par étape (moins fragile)
                    int maxReels = 2; // But ultime : 1er (like+comment) + nav vers 2ème
                    Random rand = new Random();
                    for (int reel = 1; reel <= maxReels; reel++)
                    {
                        logTextBox.AppendText($"[REEL {reel}/{maxReels}] Début interaction...\r\n");

                        // Watch delay (5-10s random)
                        await Task.Delay(rand.Next(5000, 10001));

                        // LIKE (greffé du code qui marche)
                        logTextBox.AppendText("[LIKE] DIAG candidats…\r\n");
                        var likeDiag = await webView.ExecuteScriptAsync(@"
(function(){
  try{
    var scope = document.querySelector('div[role=""dialog""]') || document;

    function sig(el){
      if(!el) return 'null';
      var id = el.id ? '#' + el.id : '';
      var cls = el.classList && el.classList.length ? ('.' + Array.from(el.classList).join('.')) : '';
      var role = el.getAttribute && el.getAttribute('role') ? '[role='+el.getAttribute('role')+']' : '';
      return el.tagName + id + cls + role;
    }
    function aria(el){ try{ return (el && el.getAttribute) ? (el.getAttribute('aria-label')||'') : ''; }catch(_){ return ''; } }
    function hasHeartPath(el){ try{ return !!(el.querySelector && el.querySelector('svg path[d^=""M16.792 3.904""]')); }catch(_){ return false; } }
    function getSvgAria(el){ try{ var svg = el.querySelector('svg[aria-label]'); return svg ? svg.getAttribute('aria-label') : ''; }catch(_){ return ''; } }

    var btns = Array.prototype.slice.call(scope.querySelectorAll('button,[role=""button""],div[role=""button""],span[role=""button""]'));
    var cands = [];

    for (var i=0;i<btns.length;i++){
      var b = btns[i];
      var lab = (aria(b)||'').toLowerCase();
      var txt = (b.textContent||'').toLowerCase();
      var ok = false;

      if (lab.indexOf('like')>=0 || lab.indexOf('aime')>=0) ok = true;
      if (!ok) {
        try{
          var svg = b.querySelector && b.querySelector('svg[aria-label]');
          var sl = svg ? (svg.getAttribute('aria-label')||'').toLowerCase() : '';
          if (sl.indexOf('like')>=0 || sl.indexOf('aime')>=0) ok = true;
        }catch(_){}
      }
      if (!ok && hasHeartPath(b)) ok = true;

      if (ok) cands.push(b);
    }

    if (cands.length===0){
      try{
        var t = Array.from(scope.querySelectorAll('svg title')).find(function(tt){ return /like|aime/i.test((tt.textContent||'').toLowerCase()); });
        if (t){
          var svg = t.closest('svg');
          var p = svg && (svg.closest('button,[role=""button""],div[role=""button""],span[role=""button""]') || svg.parentElement);
          if (p) cands.push(p);
        }
      }catch(_){}
    }

    var out = cands.slice(0,10).map(function(el){
      var r = el.getBoundingClientRect();
      return {sig:sig(el), aria:aria(el), svgAria:getSvgAria(el), hasHeart:hasHeartPath(el), w:Math.round(r.width), h:Math.round(r.height)};
    });

    return JSON.stringify({count:cands.length, sample: out});
  }catch(e){
    return JSON.stringify({error: String(e && e.message || e)});
  }
})()");
                        logTextBox.AppendText($"[LIKE][DIAG] {likeDiag}\r\n");

                        var likeTry = await webView.ExecuteScriptAsync(@"
(function(){
  try {
    var scope = document.querySelector('div[role=""dialog""]') || document;

    function sig(el){
      if(!el) return 'null';
      var id = el.id ? '#' + el.id : '';
      var cls = el.classList && el.classList.length ? ('.' + Array.from(el.classList).join('.')) : '';
      var role = el.getAttribute && el.getAttribute('role') ? '[role='+el.getAttribute('role')+']' : '';
      return el.tagName + id + cls + role;
    }

    function getSvgAria(el){ try{ var svg = el.querySelector('svg[aria-label]'); return svg ? (svg.getAttribute('aria-label')||'') : ''; }catch(_){ return ''; } }

    function liked(){
      var s = scope;
      if (s.querySelector('svg[aria-label=""Je n\u2019aime plus""], svg[aria-label=""Unlike""], svg[aria-label=""Je n\\\'aime plus""]')) return true;
      if (s.querySelector('button[aria-pressed=""true""], [role=""button""][aria-pressed=""true""]')) return true;
      if (s.querySelector('svg[color=""rgb(255, 48, 64)""], svg[fill=""rgb(237, 73, 86)""], svg path[d^=""M12 21.35""]')) return true;
      return false;
    }

    var svg = scope.querySelector('svg[aria-label=""J\u2019aime""], svg[aria-label=""Like""], svg[aria-label=""Je n\u2019aime plus""], svg[aria-label=""Unlike""]');
    if (!svg) return 'NO_SVG_FOUND';

    var svgAria = svg.getAttribute('aria-label') || '';
    var isAlreadyLiked = /n\u2019aime plus|unlike/i.test(svgAria);

    var el = svg.closest('button,[role=""button""],div[role=""button""],span[role=""button""]') || svg.parentElement;
    if (!el) return 'NO_BUTTON_PARENT';

    var picked_info = 'PICKED: ' + sig(el) + ' svgAria:' + svgAria + ' w:' + Math.round(el.getBoundingClientRect().width) + ' h:' + Math.round(el.getBoundingClientRect().height);

    if (isAlreadyLiked) return 'ALREADY_LIKED ' + picked_info;

    try{ el.scrollIntoView({behavior:'smooth', block:'center'}); }catch(_){}
    try{ el.focus(); }catch(_){}
    try{ el.style.border = '2px solid red'; setTimeout(function(){ try{ el.style.border = ''; }catch(_){} }, 5000); }catch(_){}

    try{ el.click(); }catch(_){}
    if (liked()) return 'OK:CLICK ' + picked_info;

    // Fallback elementFromPoint
    try{
      var r = el.getBoundingClientRect(), x = Math.floor(r.left + r.width/2), y = Math.floor(r.top + r.height/2);
      var topEl = document.elementFromPoint(x, y) || el;
      topEl.click();
    }catch(_){}
    if (liked()) return 'OK:ELEMENTFROMPOINT ' + picked_info;

    return 'FAIL ' + picked_info;
  } catch(e){
    return 'JSERR: ' + (e && e.message ? e.message : String(e));
  }
})()");
                        logTextBox.AppendText($"[LIKE][TRY] {likeTry}\r\n");

                        await Task.Delay(2000);

                        var unlikeSeen = await webView.ExecuteScriptAsync(@"
(function(){
  try{
    var sc = document.querySelector('div[role=""dialog""]') || document;
    return (!!sc.querySelector('svg[aria-label*=""Unlike"" i], svg[aria-label*=""Je n\\u2019aime plus"" i], svg[aria-label*=""Je n\\'aime plus"" i], button[aria-pressed=""true""], [role=""button""][aria-pressed=""true""], svg[color=""rgb(255, 48, 64)""], svg[fill=""rgb(237, 73, 86)""], svg path[d^=""M12 21.35""]')).toString();
  }catch(e){ return 'ERR:' + String(e); }
})()");
                        logTextBox.AppendText($"[LIKE][STATE] unlikeSeen = {unlikeSeen}\r\n");

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
    const match = candidates.find(el => /publier|post|envoyer|send/i.test((el.textContent||'').trim()));
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
                        logTextBox.AppendText($"[COMMENT][TRY] {commentTry}\r\n");

                        await Task.Delay(rand.Next(1200, 2201)); // Délai après comment

                        // NEXT si pas le dernier
                        if (reel < maxReels)
                        {
                            logTextBox.AppendText("[NEXT] Tentative passage au suivant...\r\n");
                            var nextTry = await webView.ExecuteScriptAsync(@"
(function(){
  try{
    var scope = document.querySelector('div[role=""dialog""]') || document;
    var next = scope.querySelector('button[aria-label=""Next""]')
            || scope.querySelector('button[aria-label=""Suivant""]')
            || scope.querySelector('[role=""button""][aria-label*=""Next""]')
            || scope.querySelector('[role=""button""][aria-label*=""Suivant""]');

    if (next){
      next.dispatchEvent(new MouseEvent('mousedown', {bubbles:true}));
      next.dispatchEvent(new MouseEvent('mouseup',   {bubbles:true}));
    } else {
      document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', bubbles:true}));
      document.body.dispatchEvent(new KeyboardEvent('keyup',   {key:'ArrowRight', code:'ArrowRight', bubbles:true}));
    }
    return 'OK';
  }catch(e){ return 'JSERR: ' + String(e); }
})()");
                            logTextBox.AppendText($"[NEXT][TRY] {nextTry}\r\n");

                            await Task.Delay(rand.Next(600, 1001)); // Délai après next

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
                    }

                    logTextBox.AppendText("[FLOW] Terminé (LIKE + COMMENT + NEXT).\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                    Logger.LogError($"TargetService.RunAsync/inner: {ex}");
                }
                finally
                {
                    // Laisse l’utilisateur gérer Stop via le bouton
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