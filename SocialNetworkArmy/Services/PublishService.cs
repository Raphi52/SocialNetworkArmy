using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    /// Publish Instagram via WebView2/CDP — léger (depth:1), pas-à-pas (3–7s),
    /// setFileInputFiles par nodeId, recadrage 4:5 (via <svg><title>…</title>).
    public class PublishService
    {
        private readonly WebView2 webView;
        private readonly TextBox log;
        private readonly InstagramBotForm form;
        private readonly Random rng = new Random();

        public PublishService(WebView2 webView, TextBox log, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
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
        // =============== PUBLIC ENTRY ===============
        public async Task RunAsync(string[] filePaths, string? caption = null, bool autoPublish = false, CancellationToken token = default)
        {
            try
            {
                form.MaximizeWindowSafe();

                // Attendre que la fenêtre soit bien maximisée et que le DOM se stabilise
                await Task.Delay(500, token);
            }
            catch (Exception ex)
            {
                log.AppendText($"[WINDOW] Failed to maximize: {ex.Message}\r\n");
                // Continue quand même
            }
            // Vérifier connexion Instagram
            bool isLoggedIn = await CheckInstagramLoginAsync();

            // ✅ NOUVEAU: Tenter d'appliquer le schedule
            try
            {
                var scheduleApplied = TryApplyScheduleCsv(ref filePaths, ref caption, out string schedInfo);

                if (!string.IsNullOrWhiteSpace(schedInfo))
                {
                    log.AppendText("[SCHEDULE] " + schedInfo + "\r\n");
                }

                // Si aucun fichier après schedule ET aucun fichier fourni manuellement
                if (filePaths == null || filePaths.Length == 0)
                {
                    log.AppendText("[Publish] ERROR: Aucun fichier (schedule ou manuel).\r\n");
                    return;
                }
            }
            catch (Exception ex)
            {
                log.AppendText("[SCHEDULE] Error: " + ex.Message + "\r\n");

                // Si erreur schedule ET pas de fichiers manuels
                if (filePaths == null || filePaths.Length == 0)
                {
                    log.AppendText("[Publish] ERROR: Aucun fichier.\r\n");
                    return;
                }
            }

            // ✅ Validation des fichiers
            for (int i = 0; i < filePaths.Length; i++)
            {
                filePaths[i] = Path.GetFullPath(filePaths[i]);
                if (!File.Exists(filePaths[i]))
                {
                    log.AppendText($"[Publish] ERROR: File not found: {filePaths[i]}\r\n");
                    return;
                }
            }

            log.AppendText($"[Publish] ✓ Files ready: {string.Join(", ", filePaths.Select(Path.GetFileName))}\r\n");

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

            await form.StartScriptAsync("Publish");
            token = form.GetCancellationToken();

            try
            {
                // CDP minimal
                await Cdp("Runtime.enable");
                await Cdp("DOM.enable");
                await Cdp("Page.enable");
                await Cdp("Network.enable");

                log.AppendText("[PUBLISH] Démarrage…\r\n");
                await Slow(token);

                // Mur de login ?
                if (await ExecBool(@"!!document.querySelector('[href*=""/accounts/login/""]')"))
                {
                    log.AppendText("[CHECK] Login requis.\r\n");
                    return;
                }

                // 1) Créer
                if (!await ClickCreateLikeConsole(token))
                {
                    log.AppendText("[CREATE] introuvable.\r\n");
                    return;
                }
                await Slow(token);

                // 2) Publication
                if (!await ClickPublication(token))
                {
                    log.AppendText("[MENU] Publication introuvable.\r\n");
                    return;
                }
                await Slow(token);

                // 3) UI upload prête ?
                var ui = await WaitUploadUi(token);
                log.AppendText(ui ? "[UPLOAD UI] détectée.\r\n" : "[UPLOAD UI] non détectée.\r\n");
                await Slow(token);

                // >>> [ADD] clic utilisateur sur “Sélectionner depuis l’ordinateur”, puis petite pause humaine
                // await ClickSelectFromComputerAsync(token);
                // await HumanUploadPauseAsync(token);

                // 4) Injecter fichiers (nodeId)
                if (!await SetFilesViaJsAsync(filePaths, token))
                {
                    // Fallback CDP si vraiment nécessaire
                    var inputNodeId = await GetInputNodeId();
                    if (inputNodeId == null) { log.AppendText("[NODE] input[type=file] introuvable.\r\n"); return; }
                    if (!await SetFiles(inputNodeId.Value, filePaths))
                    {
                        log.AppendText("[SET_FILES] KO (JS+CDP).\r\n");
                        return;
                    }
                    log.AppendText("[SET_FILES/CDP] OK (fallback).\r\n");
                }
                else
                {
                    log.AppendText("[SET_FILES_JS] OK.\r\n");
                }
                await Slow(token);

                // 5) Composer prêt ? sinon tenter “Réessayer”
                var ready = await WaitComposerReady(token);
                if (!ready && await ClickRetryIfError(token))
                {
                    await Slow(token);
                    ready = await WaitComposerReady(token);
                }
                if (!ready)
                {
                    log.AppendText("[READY] Pas de bouton Suivant.\r\n");
                    return;
                }
                await Slow(token);

                // 6) Recadrage 4:5 si dispo
                var cropRes = await GetAndClickCropButton(token);
                log.AppendText(cropRes.ok ? "[CROP] 4:5 OK\r\n" : "[CROP] 4:5 non trouvé (ok).\r\n");
                await Slow(token);

                // 7) Suivant → (filtres) → Suivant
                await ClickNext(token);
                await Slow(token);
                await ClickNext(token);
                await Slow(token);

                // 8) Légende + Partager (toujours)
                log.AppendText("[CAPTION] écran prêt.\r\n");

                // si rien fourni, on met "test"
                var captionToUse = string.IsNullOrWhiteSpace(caption) ? "test" : caption;

                // saisir la légende (même si “test”)
                if (!await WriteCaptionExecCmdAsync(captionToUse!, token))
                {
                    log.AppendText("[CAPTION] KO.\r\n");
                    return;
                }
                await Slow(token);

                // attendre que le bouton Partager/Publier soit cliquable
                var publishReady = await WaitPublishReady(token);
                if (!publishReady)
                {
                    log.AppendText("[PUBLISH_READY] Bouton non prêt.\r\n");
                    return;
                }

                // cliquer “Partager”
                if (!await ClickShareXPathAsync(token))
                {
                    log.AppendText("[PUBLISH] KO.\r\n");
                    return;
                }
                await Slow(token);

                // petite fenêtre pour une éventuelle alerte + retry si besoin
                await Task.Delay(2000, token);
                if (await ClickRetryIfError(token))
                {
                    await Slow(token);
                    if (!await ClickShareXPathAsync(token))
                    {
                        log.AppendText("[PUBLISH] KO après retry.\r\n");
                        return;
                    }
                }

                log.AppendText("[FLOW] Publication envoyée.\r\n");
            }
            finally
            {
                form.ScriptCompleted();
            }
        }



        // =============== CDP WRAPPER (léger) ===============
        private async Task<string> Cdp(string method, object? args = null)
        {
            var json = args == null ? "{}" : JsonSerializer.Serialize(args);
            try
            {
                var res = await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, json);
                return string.IsNullOrEmpty(res) ? "{}" : res;
            }
            catch (Exception ex)
            {
                log.AppendText($"[CDP:{method}] {ex.Message}\r\n");
                return "{}";
            }
        }

        // =============== SMALL JSON READERS (peu profonds) ===============
        private static int? ReadRootNodeId(string domGetDocumentJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(domGetDocumentJson);
                return doc.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
            }
            catch { return null; }
        }
        private static int? ReadNodeId(string querySelectorJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(querySelectorJson);
                var id = doc.RootElement.GetProperty("nodeId").GetInt32();
                return id == 0 ? (int?)null : id;
            }
            catch { return null; }
        }

        // =============== INPUT LOOKUP (depth:1) ===============
        private async Task<int?> GetInputNodeId()
        {
            var docJson = await Cdp("DOM.getDocument", new { depth = 1, pierce = false });
            var rootId = ReadRootNodeId(docJson);
            if (rootId == null) return null;

            var q1 = await Cdp("DOM.querySelector", new { nodeId = rootId, selector = "[role=dialog] input[type=file]" });
            var n1 = ReadNodeId(q1);
            if (n1 != null) return n1;

            var q2 = await Cdp("DOM.querySelector", new { nodeId = rootId, selector = "input[type=file]" });
            return ReadNodeId(q2);
        }

        private async Task<bool> SetFiles(int nodeId, string[] files)
        {
            try
            {
                // Resolve node => objectId
                var resolve = await Cdp("DOM.resolveNode", new { nodeId, objectGroup = "upload" });
                string? objectId = null;
                try
                {
                    using var d = JsonDocument.Parse(resolve);
                    if (d.RootElement.TryGetProperty("object", out var o) && o.TryGetProperty("objectId", out var oid))
                        objectId = oid.GetString();
                }
                catch { /* ignore */ }

                // Pre-click already done in flow: but ensure input is visible + enabled
                // Poll JS to confirm input exists & !disabled & visible
                bool found = await WaitFor(async () =>
                {
                    var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const i = document.querySelector('[role=""dialog""] input[type=""file""], input[type=""file""]');
  if (!i) return 'NO';
  const style = window.getComputedStyle(i);
  const visible = style && style.display !== 'none' && style.visibility !== 'hidden' && i.offsetWidth > 0 && i.offsetHeight > 0;
  return (visible && !i.disabled) ? 'OK' : 'NO';
})()");
                    return Trim(r) == "OK";
                }, 3000, 200, CancellationToken.None);



                // small human jitter BEFORE setFileInputFiles
                await Task.Delay(rng.Next(400, 900));

                // attempt to set files (with simple retry loop)
                int attempts = 0;
                bool setOk = false;
                Exception lastEx = null;
                while (attempts < 3 && !setOk)
                {
                    attempts++;
                    try
                    {
                        await Cdp("DOM.setFileInputFiles", new { files, nodeId });
                        setOk = true;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        await Task.Delay(300 * attempts);
                    }
                }
                if (!setOk)
                {
                    log.AppendText("[SET_FILES] DOM.setFileInputFiles failed: " + (lastEx?.Message ?? "unknown") + "\r\n");
                    return false;
                }

                // small pause after injection
                await Task.Delay(rng.Next(300, 1000));

                // dispatch input/change on resolved object if possible
                if (!string.IsNullOrEmpty(objectId))
                {
                    await Cdp("Runtime.callFunctionOn", new
                    {
                        objectId,
                        functionDeclaration = @"function(){
  try{ this.dispatchEvent(new Event('input',{bubbles:true})); }catch(e){}
  try{ this.dispatchEvent(new Event('change',{bubbles:true})); }catch(e){}
}",
                        silent = true
                    });
                }
                else
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const i=document.querySelector('[role=""dialog""] input[type=""file""], input[type=""file""]');
  if(i){ i.dispatchEvent(new Event('input',{bubbles:true})); i.dispatchEvent(new Event('change',{bubbles:true})); }
})()");
                }

                // attendre que l'UI prenne en compte l'upload (par ex. bouton "Suivant" qui apparaît)
                bool uiReady = await WaitFor(async () =>
                {
                    var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const dlg = document.querySelector('[role=""dialog""]') || document;
  const hasNext = [...dlg.querySelectorAll('button,[role=""button""]')].some(b => /suivant|next/i.test((b.textContent||'') + ((b.getAttribute&&b.getAttribute('aria-label'))||'')));
  return hasNext ? 'true' : 'false';
})()");
                    return Trim(r) == "true";
                }, 8000, 400, CancellationToken.None);

                if (!uiReady)
                {
                    log.AppendText("[SET_FILES] Warning: UI didn't respond after file injection (possible detection).\r\n");
                    // don't outright fail — caller will catch downstream if needed
                }

                return true;
            }
            catch (Exception ex)
            {
                log.AppendText("[SET_FILES] " + ex.Message + "\r\n");
                return false;
            }
        }


        // =============== IG UI POLLS ===============
        private async Task<bool> WaitUploadUi(CancellationToken token)
        {
            return await WaitFor(async () =>
            {
                var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const s=document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
const hasBtn=[...s.querySelectorAll('button,[role=""button""],a,[tabindex]')].some(e=>{
const t=(e.textContent||'').toLowerCase();
const a=(e.getAttribute && e.getAttribute('aria-label')||'').toLowerCase();
return (/sélectionner|select/.test(t+a) && (/ordinateur|computer/.test(t+a)));
});
const hasInput=!!s.querySelector('input[type=""file""]');
return (hasBtn || hasInput) ? 'true' : 'false';
})()");
                return Trim(r) == "true";
            }, 15000, 300, token);
        }

        private async Task<bool> WaitComposerReady(CancellationToken token)
        {
            return await WaitFor(async () =>
            {
                var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const s = document.querySelector('[role=""dialog""],[aria-modal=""true""]') || document;
const has=[...s.querySelectorAll('button,[role=""button""]')].some(b => {
const t=(b.textContent||'').toLowerCase();
const a=(b.getAttribute && b.getAttribute('aria-label')||'').toLowerCase();
return /suivant|next/.test(t+a);
});
return has ? 'true' : 'false';
})()");
                return Trim(r) == "true";
            }, 25000, 400, token);
        }

        private async Task<bool> WaitPublishReady(CancellationToken token)
        {
            return await WaitFor(async () =>
            {
                var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const s = document.querySelector('[role=""dialog""],[aria-modal=""true""]') || document;
const has = [...s.querySelectorAll('button,[role=""button""]')].some(b => {
const t = (b.textContent || '').toLowerCase();
const a = (b.getAttribute && b.getAttribute('aria-label') || '').toLowerCase();
return /publier|partager|share|post/.test(t + a) && !b.disabled;
});
return has ? 'true' : 'false';
})()");
                return Trim(r) == "true";
            }, 15000, 400, token);
        }

        // =============== ERRORS / RETRY ===============
        private async Task<bool> ClickRetryIfError(CancellationToken token)
        {
            var retryRes = await GetButtonPosition(@"
const dlg=document.querySelector('[role=""dialog""]'); if(!dlg) return {ok:false, msg:'NO_DLG'};
const t=(dlg.innerText||'').toLowerCase();
if(!/(une erreur|s\u2019est produite|s'est produite|an error|error occurred|impossible)/.test(t)) return {ok:false, msg:'NO_ERR'};
const btn=[...dlg.querySelectorAll('button,[role=""button""]')].find(b=>{
const s=(b.innerText||'')+((b.getAttribute&&b.getAttribute('aria-label'))||'');
return /r\u00E9essayer|reessayer|retry|try again/i.test(s);
});
if(!btn) return {ok:false, msg:'NO_BTN'};
btn.scrollIntoView({block:'center'});
const r=btn.getBoundingClientRect();
return {ok:true, x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!retryRes.ok) return false;
            await SimulateClick(retryRes.x, retryRes.y, token);
            log.AppendText("[RETRY] clic Réessayer.\r\n");
            return true;
        }

        // =============== CLICK HELPERS ===============
        private async Task<(bool ok, int x, int y, string msg)> GetButtonPosition(string jsFindButton)
        {
            var res = await webView.CoreWebView2.ExecuteScriptAsync($@"(() => {{ {jsFindButton} }})()");
            try
            {
                using var d = JsonDocument.Parse(res);
                var root = d.RootElement;
                if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                {
                    int x = root.GetProperty("x").GetInt32();
                    int y = root.GetProperty("y").GetInt32();
                    string msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() ?? "" : "";
                    return (true, x, y, msg);
                }
            }
            catch { }
            return (false, 0, 0, "");
        }

        private async Task SimulateMouseMove(double x, double y, CancellationToken token)
        {
            await Cdp("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
            await Task.Delay(rng.Next(50, 150), token);
        }

        private async Task SimulateClick(double x, double y, CancellationToken token)
        {
            // Clic JS plus “natif” que CDP, déclenche les handlers React
            string js = $@"
(() => {{
  const el = document.elementFromPoint({x}, {y});
  if (!el) return 'NO_ELEMENT';
  const r = el.getBoundingClientRect();
  const cx = r.left + r.width/2, cy = r.top + r.height/2;
  ['pointerdown','mousedown','mouseup','pointerup','click'].forEach(ev =>
    el.dispatchEvent(new MouseEvent(ev, {{bubbles:true, clientX:cx, clientY:cy, button:0}}))
  );
  return 'CLICKED';
}})();";
            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText("[SIMCLICK] " + res + "\r\n");
            await Task.Delay(rng.Next(200, 400), token);
        }

        private async Task<bool> ClickCreateLikeConsole(CancellationToken token)
        {
            bool found = await WaitFor(async () =>
            {
                var r = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const rm = s => (s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase();
const wants = ['creer','nouvelle publication','create','new post','post','publication'];
const el = [...document.querySelectorAll('[aria-label]')].find(e => {
const lab = rm((e.getAttribute('aria-label') || '').toLowerCase());
return wants.some(k => lab.includes(k));
});
return el ? 'true':'false';
})()");
                return Trim(r) == "true";
            }, 15000, 300, token);
            if (!found) return false;

            var createRes = await GetButtonPosition(@"
const rm = s => (s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase();
const wants = ['creer','nouvelle publication','create','new post','post','publication'];
const el = [...document.querySelectorAll('[aria-label]')].find(e => {
const lab = rm((e.getAttribute('aria-label') || '').toLowerCase());
return wants.some(k => lab.includes(k));
});
if (!el) return {ok:false};
el.scrollIntoView({block:'center', inline:'center'});
const r = el.getBoundingClientRect();
return {ok:true, x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!createRes.ok) { log.AppendText("[CREATE] NOT_FOUND\r\n"); return false; }
            await SimulateClick(createRes.x, createRes.y, token);
            log.AppendText("[CREATE] OK\r\n");
            return true;
        }

        private async Task<bool> ClickPublication(CancellationToken token)
        {
            log.AppendText("[MENU] Recherche du bouton 'Publication/Post' (multi-fallback)…\r\n");

            string js = @"
(() => {
  // ===== STRATÉGIE 1: SVG <title> EXACT =====
  let titles = [...document.querySelectorAll('svg title')];
  let t = titles.find(n => /^(publication|post)$/i.test((n.textContent||'').trim()));
  
  if (t) {
    const svg = t.closest('svg');
    const btn = svg ? (svg.closest('[role=""menuitem""],[role=""button""],button,a,[tabindex],div') || svg) : null;
    if (btn) {
      console.log('[MENU] Found via SVG title (exact)');
      btn.scrollIntoView({block:'center', inline:'center'});
      const r = btn.getBoundingClientRect();
      const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
      ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
      );
      return 'SVG_EXACT';
    }
  }

  // ===== STRATÉGIE 2: SVG <title> PARTIEL (contient 'post' ou 'publication') =====
  t = titles.find(n => /(publication|post|créer|create)/i.test((n.textContent||'').trim()));
  if (t) {
    const svg = t.closest('svg');
    const btn = svg ? (svg.closest('[role=""menuitem""],[role=""button""],button,a,[tabindex],div') || svg) : null;
    if (btn) {
      console.log('[MENU] Found via SVG title (partial)');
      btn.scrollIntoView({block:'center', inline:'center'});
      const r = btn.getBoundingClientRect();
      const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
      ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
      );
      return 'SVG_PARTIAL';
    }
  }

  // ===== STRATÉGIE 3: SPAN EXACT =====
  let span = [...document.querySelectorAll('span')]
    .find(s => /^(publication|post)$/i.test((s.textContent||'').trim()));
  
  if (span) {
    const btn = span.closest('[role=""menuitem""],[role=""button""],button,a,[tabindex],div');
    if (btn) {
      console.log('[MENU] Found via SPAN (exact)');
      btn.scrollIntoView({block:'center', inline:'center'});
      const r = btn.getBoundingClientRect();
      const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
      ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
      );
      return 'SPAN_EXACT';
    }
  }

  // ===== STRATÉGIE 4: ARIA-LABEL =====
  const ariaBtn = [...document.querySelectorAll('[aria-label]')]
    .find(el => /(publication|post|créer|create|nouveau|new)/i.test(el.getAttribute('aria-label')||''));
  
  if (ariaBtn) {
    console.log('[MENU] Found via aria-label');
    ariaBtn.scrollIntoView({block:'center', inline:'center'});
    const r = ariaBtn.getBoundingClientRect();
    const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
    ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
      ariaBtn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'ARIA_LABEL';
  }

  // ===== STRATÉGIE 5: TEXTE DANS MENUITEM/BUTTON =====
  const menuBtn = [...document.querySelectorAll('[role=""menuitem""],[role=""button""],button,a')]
    .find(el => /(publication|post|créer|create)/i.test(el.textContent||''));
  
  if (menuBtn) {
    console.log('[MENU] Found via menuitem text');
    menuBtn.scrollIntoView({block:'center', inline:'center'});
    const r = menuBtn.getBoundingClientRect();
    const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
    ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
      menuBtn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'MENUITEM_TEXT';
  }

  // ===== STRATÉGIE 6: POSITION MENU (3ème élément du menu) =====
  // Instagram place souvent 'Publication' en 3ème position: Home, Recherche, Publication, Reels...
  const navItems = [...document.querySelectorAll('nav [role=""menuitem""], nav a, nav [role=""button""]')];
  if (navItems.length >= 3) {
    const thirdItem = navItems[2]; // Index 2 = 3ème élément
    console.log('[MENU] Trying 3rd menu item (positional fallback)');
    thirdItem.scrollIntoView({block:'center', inline:'center'});
    const r = thirdItem.getBoundingClientRect();
    const x = Math.round(r.left + r.width/2), y = Math.round(r.top + r.height/2);
    ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev =>
      thirdItem.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'POSITION_3RD';
  }

  return 'NO_BTN';
})();
";

            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText($"[MENU/CLICK_POST] Result: {res}\r\n");

            if (res == "NO_BTN")
            {
                // Log détaillé pour debugging
                log.AppendText("[MENU] ✗ ÉCHEC - Diagnostic du DOM:\r\n");
                var debug = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const svgTitles = [...document.querySelectorAll('svg title')].map(t => t.textContent).slice(0, 10);
  const spans = [...document.querySelectorAll('span')].map(s => s.textContent?.trim()).filter(t => t && t.length < 30).slice(0, 10);
  const ariaLabels = [...document.querySelectorAll('[aria-label]')].map(el => el.getAttribute('aria-label')).filter(t => t).slice(0, 10);
  return JSON.stringify({svgTitles, spans, ariaLabels});
})()");
                log.AppendText($"[MENU] Debug info: {debug}\r\n");
                await Task.Delay(1500, token);
                return false;
            }

            await Task.Delay(1500, token);
            return true;
        }
        private async Task<bool> SetFilesViaJsAsync(string[] files, CancellationToken token)
        {
            try
            {
                // Vérifie qu'on a bien un input dans le dialog actuel
                var ok = Trim(await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const dlg = document.querySelector('[role=""dialog""],[aria-modal=""true""]') || document;
  return dlg.querySelector('input[type=""file""]') ? 'OK' : 'NO';
})()")) == "OK";
                if (!ok) { log.AppendText("[SET_FILES_JS] input introuvable.\r\n"); return false; }

                for (int i = 0; i < files.Length; i++)
                {
                    var path = files[i];
                    var bytes = await File.ReadAllBytesAsync(path, token);
                    var b64 = Convert.ToBase64String(bytes);
                    var ext = Path.GetExtension(path)?.ToLowerInvariant();
                    var mime = ext == ".png" ? "image/png"
                             : (ext == ".mp4" || ext == ".mov") ? "video/mp4"
                             : "image/jpeg";
                    var name = Path.GetFileName(path).Replace("'", "_");

                    string inject = $@"
(() => {{
  const b64 = '{b64}';
  const mime = '{mime}';
  const name = '{name}';
  const bin = atob(b64);
  const u8  = new Uint8Array(bin.length);
  for (let i=0;i<bin.length;i++) u8[i] = bin.charCodeAt(i);
  const blob = new Blob([u8], {{ type: mime }});
  const file = new File([blob], name, {{ type: mime, lastModified: Date.now() }});

  const dlg   = document.querySelector('[role=""dialog""],[aria-modal=""true""]') || document;
  const input = dlg.querySelector('input[type=""file""]');
  if (!input) return 'NO_INPUT';

  const dt = new DataTransfer();
  dt.items.add(file);

  try {{
    Object.defineProperty(input, 'files', {{ configurable:true, writable:false, value: dt.files }});
  }} catch(e) {{
    input.files = dt.files;
  }}
  input.dispatchEvent(new Event('input',  {{ bubbles:true }}));
  input.dispatchEvent(new Event('change', {{ bubbles:true }}));
  return 'OK';
}})();";

                    var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(inject));
                    if (res != "OK")
                    {
                        log.AppendText("[SET_FILES_JS] " + res + "\r\n");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                log.AppendText("[SET_FILES_JS] " + ex.Message + "\r\n");
                return false;
            }
        }

        private async Task ClickNext(CancellationToken token)
        {
            var nextRes = await GetButtonPosition(@"
const sc=document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
const btn=[...sc.querySelectorAll('[role=""button""],button,a,[tabindex]')]
.find(e => /suivant|next/i.test((e.textContent||'')) || /suivant|next/i.test((e.getAttribute&&e.getAttribute('aria-label')||'')));
if(!btn) return {ok:false};
btn.scrollIntoView({block:'center'});
const r=btn.getBoundingClientRect();
return {ok:true, x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!nextRes.ok) { log.AppendText("[NEXT] NO_BTN\r\n"); return; }
            await SimulateClick(nextRes.x, nextRes.y, token);
            log.AppendText("[NEXT] OK\r\n");
        }

        // ======== Recadrage 4:5 (nouvelle implémentation) ========
        private async Task<(bool ok, string msg)> OpenFormatPanelAsync(CancellationToken token)
        {
            string js = @"
(() => {
  const dlg = document.querySelector('[role=""dialog""][aria-modal=""true""]') || document;
  // essai par <svg><title>…</title>
  let t = [...dlg.querySelectorAll('svg title')].find(x => /sélectionner.*format|select.*format|ratio|aspect/i.test(x.textContent||''));
  let btn = t ? t.closest('button,[role=""button""],[tabindex]') : null;
  if (!btn) {
    // fallback: vise le coin bas-gauche de la modale
    const r = dlg.getBoundingClientRect();
    btn = document.elementFromPoint(r.left + 34, r.bottom - 34)?.closest('button,[role=""button""],[tabindex]') || null;
  }
  if (!btn) { return 'NO_BTN'; }
  const b = btn.getBoundingClientRect(), x = b.left + b.width/2, y = b.top + b.height/2;
  ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(t =>
    btn.dispatchEvent(new MouseEvent(t,{bubbles:true,clientX:x,clientY:y,button:0}))
  );
  return 'OPENED';
})()";
            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText($"[CROP] OPEN {res}\r\n");
            return (res == "OPENED", res);
        }

        private async Task<(bool ok, string msg)> ClickPortraitCropAsync(CancellationToken token)
        {
            string js = @"
(() => {
  const dlg=document.querySelector('[role=""dialog""][aria-modal=""true""]')||document;
  const t=[...dlg.querySelectorAll('svg title')].find(x=>/ic[oô]ne.*rog(n|m)age.*portrait/i.test(x.textContent||''));
  const btn=t?t.closest('button,[role=""button""],[tabindex]'):null;
  if(!btn) return 'NO_SVG_TITLE';
  const r=btn.getBoundingClientRect(), x=r.left+r.width/2, y=r.top+r.height/2;
  ['pointerdown','mousedown','pointerup','mouseup','click'].forEach(ev=>btn.dispatchEvent(new MouseEvent(ev,{bubbles:true,clientX:x,clientY:y,button:0})));
  return 'OK_SVG_TITLE';
})()";
            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText($"[CROP] PICK {res}\r\n");
            return (res == "OK_SVG_TITLE", res);
        }

        private async Task<(bool ok, string msg)> GetAndClickCropButton(CancellationToken token)
        {
            // Étape A : ouvrir le sélecteur de format
            var open = await OpenFormatPanelAsync(token);
            await Slow(token); // laisser l’UI s’animer

            // Étape B : cliquer l’icône “portrait” (4:5)
            var pick = await ClickPortraitCropAsync(token);
            if (pick.ok) return (true, "4:5");

            // Fallback : ancienne heuristique par libellé
            var cropRes = await GetButtonPosition(@"
const s = document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
const btn = [...s.querySelectorAll('button,[role=""button""],[tabindex]')].find(e=>{
const t=(e.textContent||'').toLowerCase();
const a=(e.getAttribute&&e.getAttribute('aria-label')||'').toLowerCase();
return /4:?5/.test(t) || /4:?5/.test(a) || /portrait/.test(t+a);
});
if(!btn) return {ok:false, msg:'NO_CROP'};
btn.scrollIntoView({block:'center'});
const r=btn.getBoundingClientRect();
return {ok:true, msg:'CROP_CLICKED', x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!cropRes.ok) return (false, cropRes.msg);
            await SimulateClick(cropRes.x, cropRes.y, token);
            return (true, "fallback");
        }

        // =============== Caption & Publish ===============
        private async Task TypeCaption(string caption, CancellationToken token)
        {
            var capRes = await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
const s = document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
let box = s.querySelector('textarea[aria-label], textarea');
if (!box) box = [...s.querySelectorAll('[role=""textbox""],[contenteditable=""true""]')].find(e=>e.contentEditable==='true');
if (!box) return JSON.stringify({ok:false});
box.scrollIntoView({block:'center'});
const r = box.getBoundingClientRect();
return JSON.stringify({ok:true, x:Math.round(r.left + r.width / 2), y:Math.round(r.top + r.height / 2), isTextarea: !!('value' in box)});
})()");
            using var d = JsonDocument.Parse(capRes);
            if (!d.RootElement.GetProperty("ok").GetBoolean()) { log.AppendText("[CAPTION] zone introuvable.\r\n"); return; }
            double cx = d.RootElement.GetProperty("x").GetDouble();
            double cy = d.RootElement.GetProperty("y").GetDouble();

            await SimulateClick(cx, cy, token);

            foreach (var ch in caption)
            {
                token.ThrowIfCancellationRequested();
                var jsCh = JsonSerializer.Serialize(ch.ToString());
                await webView.CoreWebView2.ExecuteScriptAsync($@"
(() => {{
const s=document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
let b=s.querySelector('textarea[aria-label], textarea');
if(!b) b=[...s.querySelectorAll('[role=""textbox""],[contenteditable=""true""]')].find(e=>e.contentEditable==='true');
if(!b) return;
if('value' in b) {{ b.value += {jsCh}; b.dispatchEvent(new Event('input',{{bubbles:true}})); }}
else {{ document.execCommand('insertText', false, {jsCh}); }}
}})()");
                await Task.Delay(rng.Next(20, 60), token);
                if (rng.NextDouble() < 0.06) await Task.Delay(rng.Next(120, 240), token);
            }
            log.AppendText("[CAPTION] saisi.\r\n");
        }

        private async Task ClickPublish(CancellationToken token)
        {
            var pubRes = await GetButtonPosition(@"
const s = document.querySelector('[role=""dialog""],[aria-modal=""true""]') || document;
const btn = [...s.querySelectorAll('[role=""button""],button,a,[tabindex]')]
.find(e => /publier|partager|share|post/i.test((e.textContent||'')) || /publier|partager|share|post/i.test((e.getAttribute&&e.getAttribute('aria-label')||'')));
if (!btn) return {ok:false};
btn.scrollIntoView({block: 'center'});
const r = btn.getBoundingClientRect();
return {ok:true, x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!pubRes.ok) { log.AppendText("[PUBLISH] NO_BTN\r\n"); return; }
            await Task.Delay(rng.Next(500, 1500), token);
            await SimulateClick(pubRes.x, pubRes.y, token);
            log.AppendText("[PUBLISH] OK\r\n");
        }

        // =============== GENERIC UTILS ===============
        private async Task<bool> ExecBool(string js)
        {
            var r = await webView.CoreWebView2.ExecuteScriptAsync($"({js}) ? 'true' : 'false'");
            return Trim(r) == "true";
        }

        private async Task<bool> WaitFor(Func<Task<bool>> predicate, int timeoutMs, int everyMs, CancellationToken ct)
        {
            var t0 = Environment.TickCount;
            while (Environment.TickCount - t0 < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                if (await predicate()) return true;
                await Task.Delay(everyMs, ct);
            }
            return false;
        }

        // [ADD] Script console: ECRIRE DANS DESCRIPTION (execCommand)
        private async Task<bool> WriteCaptionExecCmdAsync(string text, CancellationToken token)
        {
            string js = @"
(() => {
  const dlg = document.querySelector('[role=""dialog""][aria-modal=""true""]') || document;
  const box =
    dlg.querySelector('[aria-label*=""légende"" i]') ||
    dlg.querySelector('div[role=""textbox""][contenteditable=""true""]');

  if (!box) return 'NO_BOX';
  box.scrollIntoView({block:'center'}); box.focus();
  document.execCommand('selectAll', false, null);
  document.execCommand('delete', false, null);
  document.execCommand('insertText', false, " + JsonSerializer.Serialize(text) + @");
  box.dispatchEvent(new Event('input', {bubbles:true}));
  return 'OK';
})();";
            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText("[CAPTION/execCommand] " + res + "\r\n");
            return res == "OK";
        }

        // [ADD] Script console: CLICKER SUR PARTAGER (XPath exact)
        private async Task<bool> ClickShareXPathAsync(CancellationToken token)
        {
            string js = @"
(() => {
  const dlg = document.querySelector('[role=""dialog""][aria-modal=""true""]') || document;
  const it = document.evaluate(
    "".//*[(@role='button' or self::button or @tabindex) and normalize-space(text())='Partager']"",
    dlg, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null
  ).singleNodeValue;
  if (!it) return 'NO_BTN_XPATH';
  it.scrollIntoView({block:'center', inline:'center'});
  const r = it.getBoundingClientRect(), x = r.left + r.width/2, y = r.top + r.height/2;
  ['pointerdown','mousedown','pointerup','mouseup','click']
    .forEach(ev => it.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0})));
  return 'OK_XPATH';
})();";
            var res = Trim(await webView.CoreWebView2.ExecuteScriptAsync(js));
            log.AppendText("[PUBLISH/XPath] " + res + "\r\n");
            return res == "OK_XPATH";
        }


        private string? GetCurrentAccountNameSafe()
        {
            try
            {
                // ✅ 1. ESSAYER LA PROPRIÉTÉ PUBLIQUE (AJOUTÉE CI-DESSUS)
                var t = form.GetType();
                var prop = t.GetProperty("CurrentAccountName",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                if (prop != null && prop.PropertyType == typeof(string))
                {
                    var val = prop.GetValue(form) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        log.AppendText($"[ACCOUNT] ✓ Trouvé via CurrentAccountName: {val}\r\n");
                        return val;
                    }
                }

                // ✅ 2. FALLBACK: EXTRAIRE DEPUIS LE DOM INSTAGRAM
                try
                {
                    var jsResult = webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
    try {
        // Méthode 1: Username dans la barre de navigation
        const profileLink = document.querySelector('a[href*=""instagram.com/""]');
        if (profileLink) {
            const match = profileLink.href.match(/instagram\.com\/([^/?]+)/);
            if (match) return match[1];
        }
        
        // Méthode 2: Meta tags
        const metaUsername = document.querySelector('meta[property=""og:title""]');
        if (metaUsername) {
            const content = metaUsername.getAttribute('content');
            const match = content.match(/@([a-zA-Z0-9._]+)/);
            if (match) return match[1];
        }
        
        return '';
    } catch(e) { return ''; }
})()
            ").GetAwaiter().GetResult();

                    var username = jsResult?.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        log.AppendText($"[ACCOUNT] ✓ Trouvé via DOM: {username}\r\n");
                        return username;
                    }
                }
                catch (Exception ex)
                {
                    log.AppendText($"[ACCOUNT] DOM extraction failed: {ex.Message}\r\n");
                }

                // ✅ 3. FALLBACK: VARIABLE D'ENVIRONNEMENT
                var env = Environment.GetEnvironmentVariable("SNA_ACCOUNT");
                if (!string.IsNullOrWhiteSpace(env))
                {
                    log.AppendText($"[ACCOUNT] ✓ Trouvé via ENV: {env}\r\n");
                    return env;
                }

                log.AppendText("[ACCOUNT] ✗ Impossible de déterminer le compte actuel\r\n");
            }
            catch (Exception ex)
            {
                log.AppendText($"[ACCOUNT] Erreur: {ex.Message}\r\n");
            }

            return null;
        }

        // [ADD] utilitaire: index d'un header parmi plusieurs alias
        private static int IndexOfHeader(string[] headers, params string[] names)
        {
            if (headers == null) return -1;
            for (int i = 0; i < headers.Length; i++)
            {
                var h = (headers[i] ?? "").Trim();
                for (int j = 0; j < names.Length; j++)
                {
                    if (h.Equals(names[j], StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        // [ADD] mini parser CSV (gère les guillemets et les virgules dans les champs)
        private static string[] SplitCsvLine(string line)
        {
            if (line == null) return Array.Empty<string>();
            var list = new System.Collections.Generic.List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else { inQuotes = false; }
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
                    else if (c == '"') { inQuotes = true; }
                    else sb.Append(c);
                }
            }
            list.Add(sb.ToString());
            return list.ToArray();
        }

        private static string Trim(string s) => (s ?? "").Trim().Trim('"');

        private async Task Slow(CancellationToken ct) =>
            await Task.Delay(rng.Next(2000, 5000), ct); // 3–5s pour simuler l’humain
                                                        // Dans PublishService.cs, remplacer la méthode TryApplyScheduleCsv par :

        private bool TryApplyScheduleCsv(ref string[] filePaths, ref string? caption, out string info)
        {
            info = string.Empty;

            log.AppendText("[SCHEDULE] ========================================\r\n");
            log.AppendText("[SCHEDULE] Starting schedule search...\r\n");

            // ✅ RÉCUPÉRER LE NOM DU COMPTE
            string? currentAccount = GetCurrentAccountNameSafe();

            log.AppendText($"[SCHEDULE] Current account detected: '{currentAccount ?? "(null)"}'\r\n");

            if (string.IsNullOrWhiteSpace(currentAccount))
            {
                info = "Cannot identify current account → manual mode.";
                log.AppendText("[SCHEDULE] ✗ " + info + "\r\n");
                log.AppendText("[SCHEDULE] ========================================\r\n");
                return true; // Continue en mode manuel
            }

            try
            {
                log.AppendText($"[SCHEDULE] Searching CSV for:\r\n");
                log.AppendText($"[SCHEDULE]   - Date: {DateTime.Today:yyyy-MM-dd}\r\n");
                log.AppendText($"[SCHEDULE]   - Platform: Instagram\r\n");
                log.AppendText($"[SCHEDULE]   - Account: {currentAccount}\r\n");
                log.AppendText($"[SCHEDULE]   - Activity: publish\r\n");

                // ✅ FIX CRITIQUE: PASSER LE TEXTBOX POUR LES LOGS DÉTAILLÉS
                var match = ScheduleHelper.GetTodayMediaForAccount(
                    currentAccount,
                    "Instagram",
                    "publish",
                    targetDate: null,
                    log: log  // ⬅️ AJOUT MANQUANT!
                );

                if (match == null)
                {
                    info = $"No publish scheduled today for {currentAccount} → manual mode.";
                    log.AppendText("[SCHEDULE] ✗ " + info + "\r\n");
                    log.AppendText("[SCHEDULE] ========================================\r\n");
                    return true; // Continue en mode manuel
                }

                // ✅ VALIDATION DU FICHIER TROUVÉ
                if (!File.Exists(match.MediaPath))
                {
                    info = $"File not found: {match.MediaPath}";
                    log.AppendText($"[SCHEDULE] ✗ ERROR: {info}\r\n");
                    log.AppendText("[SCHEDULE] ========================================\r\n");
                    return true; // Continue en mode manuel
                }

                // ✅ APPLIQUER LE MATCH
                filePaths = new[] { match.MediaPath };

                // ✅ CHARGER LA DESCRIPTION ICI (au moment du publish)
                string description = MappingService.GetDescriptionForMedia(
                    match.MediaPath,
                    msg => log.AppendText($"[MAPPING] {msg}\r\n")
                );

                if (!string.IsNullOrWhiteSpace(description))
                {
                    caption = description;
                    log.AppendText($"[SCHEDULE] ✓ Caption loaded from mapping: {description.Substring(0, Math.Min(50, description.Length))}...\r\n");
                }
                else
                {
                    log.AppendText($"[SCHEDULE] ⚠ No description found in mapping file\r\n");
                }

                info = $"✓ Match: {DateTime.Today:yyyy-MM-dd} / {match.AccountOrGroup} / Instagram → {Path.GetFileName(match.MediaPath)}";
                log.AppendText($"[SCHEDULE] ✓✓✓ MATCH FOUND!\r\n");
                log.AppendText($"[SCHEDULE]   - File: {match.MediaPath}\r\n");
                log.AppendText($"[SCHEDULE]   - Caption: {match.Description}\r\n");
                log.AppendText($"[SCHEDULE] {info}\r\n");

                if (match.IsGroup)
                {
                    log.AppendText($"[SCHEDULE] ✓ Group mode: path auto-mapped for {currentAccount}\r\n");
                }

                log.AppendText("[SCHEDULE] ========================================\r\n");
                return true;
            }
            catch (Exception ex)
            {
                info = $"Schedule error: {ex.Message}";
                log.AppendText("[SCHEDULE] ✗ ERROR: " + info + "\r\n");
                log.AppendText($"[SCHEDULE] Stack: {ex.StackTrace}\r\n");
                log.AppendText("[SCHEDULE] ========================================\r\n");
                return true; // Continue en mode manuel en cas d'erreur
            }
        }

        private async Task<bool> ClickSelectFromComputerAsync(CancellationToken token)
        {
            var res = await GetButtonPosition(@"
const sc=document.querySelector('[role=""dialog""],[aria-modal=""true""]')||document;
const btn=[...sc.querySelectorAll('button,[role=""button""],a,[tabindex]')].find(e=>{
  const t=(e.textContent||'').toLowerCase();
  const a=(e.getAttribute && e.getAttribute('aria-label')||'').toLowerCase();
  return /(sélectionner|selectionner|select).*(ordinateur|computer)/.test(t+a);
});
if(!btn) return {ok:false, msg:'NO_SELECT_BUTTON'};
btn.scrollIntoView({block:'center', inline:'center'});
const r=btn.getBoundingClientRect();
return {
  ok:true,
  msg:'HIT',
  x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)),
  y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))
};
");
            if (!res.ok) { log.AppendText("[UPLOAD] Bouton \"Sélectionner…\" introuvable.\r\n"); return false; }

            await SimulateClick(res.x, res.y, token);
            log.AppendText("[UPLOAD] Click \"Sélectionner depuis l’ordinateur\".\r\n");
            return true;
        }

        // [ADD] Attente humaine courte (0.9–2.1s) pour coller à un vrai clic + ouverture
        private async Task HumanUploadPauseAsync(CancellationToken ct)
        {
            await Task.Delay(rng.Next(900, 2100), ct);
        }
    }
}
