











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
                    var openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                    logTextBox.AppendText($"[CHECK_OPENED] après click simple => {openedCheck}\r\n");

                    // 12) Séquence : regarder 1..4 (5-10s), like+comment 1 et 4
                    string runSequenceJs = @"
(function(){
  if (window.SNA_seqRunning) return 'BUSY';
  window.SNA_seqRunning = true;

  const alive = () => (typeof window !== 'undefined' && window.isRunning !== false);
  const sleepC = (ms) => new Promise((resolve) => {
    const start = performance.now();
    function tick(){
      if (!alive()) return resolve('STOP');
      if (performance.now() - start >= ms) return resolve('OK');
      setTimeout(tick, 120);
    }
    tick();
  });
  const rand  = (a,b)=> Math.floor(a + Math.random()*(b-a+1));

  async function clickCenter(el){
    if (!alive()) return 'STOP';
    el.scrollIntoView({behavior:'smooth', block:'center'});
    if (await sleepC(120)==='STOP') return 'STOP';
    const r = el.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    const opts = {bubbles:true, cancelable:true, clientX:x, clientY:y, button:0};
    el.dispatchEvent(new MouseEvent('mousedown', opts));
    el.dispatchEvent(new MouseEvent('mouseup',   opts));
    el.dispatchEvent(new MouseEvent('click',     opts));
    return 'OK';
  }

  function setNativeValue(el, value){
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype
                : el instanceof HTMLInputElement   ? HTMLInputElement.prototype
                : null;
    if (!proto) { el.value = value; el.dispatchEvent(new Event('input',{bubbles:true})); return; }
    const desc = Object.getOwnPropertyDescriptor(proto, 'value');
    desc.set.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function btnEnabled(b){
    if (!b) return false;
    if (b.disabled) return false;
    const ad = b.getAttribute('aria-disabled');
    if (ad && ad.toString().toLowerCase() === 'true') return false;
    const st = getComputedStyle(b);
    return !(st.pointerEvents === 'none' || st.display === 'none' || st.visibility === 'hidden');
  }

  function findPublishControl(form){
    if (!form) return null;
    let btn = form.querySelector('button[type=""submit""]');
    if (btn) return btn;
    const candidates = [...form.querySelectorAll('button,[role=""button""]')];
    const match = candidates.find(el => /publier|post|envoyer|send/i.test((el.textContent||'').trim()));
    if (match) return match;
    const wrap = form.querySelector('.x13fj5qh');
    if (wrap){
      const inside = [...wrap.querySelectorAll('button,[role=""button""]')].find(Boolean);
      if (inside) return inside;
    }
    return null;
  }

  async function waitEnabled(el, timeout=8000){
    const t0 = performance.now();
    while (performance.now() - t0 < timeout){
      if (!alive()) return false;
      if (btnEnabled(el)) return true;
      if (await sleepC(120)==='STOP') return false;
    }
    return false;
  }

  function pickComment(){
    const arr = ['Super ! ??','J\\u0027adore ! ??','Trop cool ! ??','Impressionnant !','Bien vu ! ??','Top ! ??'];
    return arr[Math.floor(Math.random()*arr.length)];
  }

  async function commentOnce(){
    if (!alive()) return 'STOP';
    const dlg  = document.querySelector('div[role=""dialog""]');
    const root = dlg || document;

    let ta = (root.querySelector('div[role=""dialog""] form textarea'))
          || (root.querySelector('form textarea'))
          || root.querySelector('textarea');
    let ce = null;
    if (!ta){
      ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
      if (!ce) return 'NO_COMPOSER';
    }

    const form = (ta && ta.closest('form')) || (ce && ce.closest('form')) || root.querySelector('form');
    const text = pickComment();

    if (ta){
      if (await clickCenter(ta)==='STOP') return 'STOP';
      ta.focus();
      setNativeValue(ta, text);
    } else {
      if (await clickCenter(ce)==='STOP') return 'STOP';
      ce.focus();
      document.execCommand('insertText', false, text);
      ce.dispatchEvent(new Event('input', {bubbles:true}));
    }

    if (await sleepC(rand(180, 360))==='STOP') return 'STOP';

    const ctrl = findPublishControl(form);
    if (!ctrl) return 'NO_CTRL';
    const ok = await waitEnabled(ctrl, 8000);
    if (!ok) return 'CTRL_DISABLED';

    if (await clickCenter(ctrl)==='STOP') return 'STOP';

    const t0 = performance.now();
    while (performance.now() - t0 < 12000){
      if (!alive()) return 'STOP';
      const scope = document.querySelector('div[role=""dialog""]') || document;
      const ta2 = scope.querySelector('form textarea');
      if (!ta2 || ta2.value.trim().length === 0) break;
      if (await sleepC(220)==='STOP') return 'STOP';
    }
    return 'COMMENTED';
  }

  async function goNext(){
    if (!alive()) return 'STOP';
    const inModal = !!document.querySelector('div[role=""dialog""]');
    const scope = inModal ? (document.querySelector('div[role=""dialog""]')) : document;

    let next = scope.querySelector('button[aria-label=""Next""]')
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
    await sleepC(rand(600, 1000));
    return 'OK';
  }

  (async () => {
    const result = {status:'STARTED', steps:[]};
    try{
      for (let i=1; i<=4; i++){
        if (await sleepC(rand(5000,10000))==='STOP'){ result.status='STOP'; result.at=i; break; }

        if (i === 1 || i === 4){
          const c = await commentOnce();
          if (c==='STOP'){ result.status='STOP'; result.at=i; break; }
          result.steps.push({i, comment:c});
          if (await sleepC(rand(1200,2200))==='STOP'){ result.status='STOP'; result.at=i; break; }
        }

        if (i < 4){
          const n = await goNext();
          if (n==='STOP'){ result.status='STOP'; result.at=i; break; }
        }
      }
      if (result.status==='STARTED') result.status='DONE';
    }catch(e){
      result.status='JS_ERROR';
      result.error = String(e && e.message || e);
    }finally{
      window.SNA_lastSeq = result;
      window.SNA_seqRunning = false;
    }
  })();

  return 'STARTED';
})();";

                    // 12) Si déjà ouvert ? lancer DIRECT la séquence
                    if (JsBoolIsTrue(openedCheck))
                    {
                        logTextBox.AppendText("[STATE] Déjà ouvert : lancement séquence.\r\n");
                        var seqRes1 = await webView.ExecuteScriptAsync(runSequenceJs);
                        logTextBox.AppendText($"[SEQUENCE] résultat = {seqRes1}\r\n");
                    }
                    else
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
                        openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                        logTextBox.AppendText($"[CHECK_OPENED] après mouse events => {openedCheck}\r\n");

                        if (JsBoolIsTrue(openedCheck))
                        {
                            logTextBox.AppendText("[STATE] Ouvert après mouse events : lancement séquence.\r\n");
                            var seqRes2 = await webView.ExecuteScriptAsync(runSequenceJs);
                            logTextBox.AppendText($"[SEQUENCE] résultat = {seqRes2}\r\n");
                        }
                        else
                        {
                            logTextBox.AppendText("[KO] Impossible d’ouvrir le 1er Reel (sélecteur/clic).?\r\n");
                        }
                    }
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
