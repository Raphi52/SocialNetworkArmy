using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
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

        // =============== PUBLIC ENTRY ===============
        public async Task RunAsync(string[] filePaths, string? caption = null, bool autoPublish = false, CancellationToken token = default)
        {
            // === [NOUVEAU] Vérifie s’il y a un schedule dans data/schedule.csv ===
            try
            {
                string schedInfo;
                if (!TryApplyScheduleCsv(ref filePaths, ref caption, out schedInfo))
                {
                    log.AppendText("[SCHEDULE] " + schedInfo + "\r\n");
                    return; // pas de correspondance → on arrête le flux
                }
                if (!string.IsNullOrEmpty(schedInfo))
                    log.AppendText("[SCHEDULE] " + schedInfo + "\r\n");
            }
            catch (Exception ex)
            {
                log.AppendText("[SCHEDULE] " + ex.Message + "\r\n");
            }

            if (filePaths == null || filePaths.Length == 0) throw new ArgumentException("Aucun fichier.");
            for (int i = 0; i < filePaths.Length; i++)
            {
                filePaths[i] = Path.GetFullPath(filePaths[i]);
                if (!File.Exists(filePaths[i])) throw new FileNotFoundException(filePaths[i]);
            }

            await webView.EnsureCoreWebView2Async();
            // UA “classique” pour éviter certains drapeaux anti-bot
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
                webView.CoreWebView2.Navigate("https://www.instagram.com/");
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
                if (!await ClickPublicationLikeConsole(token))
                {
                    log.AppendText("[MENU] Publication introuvable.\r\n");
                    return;
                }
                await Slow(token);

                // 3) UI upload prête ?
                var ui = await WaitUploadUi(token);
                log.AppendText(ui ? "[UPLOAD UI] détectée.\r\n" : "[UPLOAD UI] non détectée.\r\n");
                await Slow(token);

                // 4) Injecter fichiers (nodeId)
                var inputNodeId = await GetInputNodeId();
                if (inputNodeId == null)
                {
                    log.AppendText("[NODE] input[type=file] introuvable.\r\n");
                    return;
                }
                if (!await SetFiles(inputNodeId.Value, filePaths))
                {
                    log.AppendText("[SET_FILES] KO.\r\n");
                    return;
                }
                log.AppendText("[SET_FILES] OK.\r\n");
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
                await Cdp("DOM.setFileInputFiles", new { files, nodeId });

                // Déclencher input/change sur CET élément
                var resolve = await Cdp("DOM.resolveNode", new { nodeId, objectGroup = "upload" });
                string? objectId = null;
                try
                {
                    using var d = JsonDocument.Parse(resolve);
                    if (d.RootElement.TryGetProperty("object", out var o) && o.TryGetProperty("objectId", out var oid))
                        objectId = oid.GetString();
                }
                catch { /* ignore */ }

                if (!string.IsNullOrEmpty(objectId))
                {
                    await Cdp("Runtime.callFunctionOn", new
                    {
                        objectId,
                        functionDeclaration = @"function(){
try{ this.dispatchEvent(new Event('input',{bubbles:true})); }catch(e){}
try{ this.dispatchEvent(new Event('change',{bubbles:true})); }catch(e){}
}",
                        returnByValue = true,
                        silent = true
                    });
                }
                else
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(@"
(() => { const i=document.querySelector('[role=""dialog""] input[type=""file""], input[type=""file""]');
if(i){ i.dispatchEvent(new Event('input',{bubbles:true})); i.dispatchEvent(new Event('change',{bubbles:true})); } })()");
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
            x += rng.Next(-5, 6);
            y += rng.Next(-5, 6);
            await SimulateMouseMove(x, y, token);
            await Cdp("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
            await Task.Delay(rng.Next(100, 300), token);
            await Cdp("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
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

        private async Task<bool> ClickPublicationLikeConsole(CancellationToken token)
        {
            var pubRes = await GetButtonPosition(@"
const el = document.querySelector('[aria-label=""Publication"" i]') ||
[...document.querySelectorAll('[role=""menuitem""],[role=""button""],button')].find(e=>{
const t=(e.textContent||'').toLowerCase();
const a=(e.getAttribute && e.getAttribute('aria-label')||'').toLowerCase();
return /publication|post/.test(t+a);
});
if(!el) return {ok:false};
el.scrollIntoView({block:'center', inline:'center'});
const r = el.getBoundingClientRect();
return {ok:true, x:Math.round(r.left + r.width * (0.2 + Math.random() * 0.6)), y:Math.round(r.top + r.height * (0.2 + Math.random() * 0.6))};
");
            if (!pubRes.ok) { log.AppendText("[MENU] NO_EL\r\n"); return false; }
            await SimulateClick(pubRes.x, pubRes.y, token);
            log.AppendText("[MENU] HIT\r\n");
            return true;
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
        // [ADD] Lecture/filtrage du schedule.csv et surcharge des paramètres si match trouvé.
        // Retourne true = continuer le flux, false = stopper (pas de match ou problème bloquant).
        private bool TryApplyScheduleCsv(ref string[] filePaths, ref string? caption, out string info)
        {
            info = string.Empty;

            // Emplacement du CSV: variable d'env SNA_SCHEDULE prioritaire, sinon fichier "schedule.csv" à côté de l'exe.
            string schedulePath = Environment.GetEnvironmentVariable("SNA_SCHEDULE");
            if (string.IsNullOrWhiteSpace(schedulePath))
                schedulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "schedule.csv");


            if (!File.Exists(schedulePath))
            {
                info = "schedule.csv introuvable → on continue en mode manuel.";
                return true; // pas de schedule → on laisse la logique actuelle
            }

            // On essaie de connaître le compte courant depuis le formulaire (sans dépendre d'un nom précis, via réflexion souple).
            string? currentAccount = GetCurrentAccountNameSafe();

            var lines = File.ReadAllLines(schedulePath);
            if (lines.Length < 2)
            {
                info = "schedule.csv vide.";
                return false; // fichier présent mais vide → on stoppe (aucune tâche à exécuter)
            }

            // Mapping d'en-têtes (tolérant)
            var headers = SplitCsvLine(lines[0]);
            int iDate = IndexOfHeader(headers, "Date");
            int iAccount = IndexOfHeader(headers, "Account", "Compte", "Name", "Profil", "Profile");
            int iPlatform = IndexOfHeader(headers, "Plateforme", "Platform");
            int iMediaPath = IndexOfHeader(headers, "Media Path", "Media", "Path", "MediaPath", "File", "Fichier");
            int iDesc = IndexOfHeader(headers, "Description", "Caption", "Texte");

            if (iDate < 0 || iAccount < 0 || iPlatform < 0 || iMediaPath < 0)
            {
                info = "schedule.csv headers invalides (attendus au minimum: Date, Account, Plateforme/Platform, Media Path).";
                return false;
            }

            var today = DateTime.Today;
            // on parcourt toutes les lignes à la recherche de la 1ʳᵉ correspondance stricte
            for (int li = 1; li < lines.Length; li++)
            {
                var cols = SplitCsvLine(lines[li]);
                int needed = Math.Max(Math.Max(iDate, iPlatform), Math.Max(iMediaPath, iAccount));
                if (cols.Length <= needed) continue;

                string dateStr = (cols[iDate] ?? "").Trim();
                DateTime date;
                bool okDate =
                    DateTime.TryParseExact(dateStr, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "yyyy/MM/dd" },
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out date);

                if (!okDate || date.Date != today) continue;

                string platform = (cols[iPlatform] ?? "").Trim();
                if (!platform.Equals("Instagram", StringComparison.OrdinalIgnoreCase)) continue;

                string account = (cols[iAccount] ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(currentAccount) &&
                    !account.Equals(currentAccount, StringComparison.OrdinalIgnoreCase))
                {
                    // si on connaît le compte courant, on exige l'égalité stricte
                    continue;
                }

                string media = (cols[iMediaPath] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(media))
                {
                    info = "MEDIA_PATH vide dans schedule.csv.";
                    return false;
                }

                string fullMedia = Path.GetFullPath(media);
                if (!File.Exists(fullMedia))
                {
                    info = "MEDIA introuvable: " + fullMedia;
                    return false;
                }

                // OK, on applique
                filePaths = new[] { fullMedia };
                if (iDesc >= 0 && iDesc < cols.Length)
                {
                    string desc = cols[iDesc];
                    if (!string.IsNullOrWhiteSpace(desc)) caption = desc;
                }

                info = $"match: {today:yyyy-MM-dd} / {account} / {platform} → override media & description.";
                return true; // continuer le flux normal (on a rempli filePaths+caption)
            }

            // schedule présent mais aucune ligne ne correspond pour aujourd'hui/compte
            info = "NO_MATCH (aucune ligne pour aujourd’hui et ce compte) → arrêt.";
            return false; // on stoppe : rien à publier
        }

        // [ADD] Essaie de deviner le nom du compte courant exposé par le formulaire (réflexion souple, non bloquant).
        private string? GetCurrentAccountNameSafe()
        {
            try
            {
                var t = form.GetType();

                // propriétés directes de type string possibles
                string[] propNames =
                {
            "SelectedProfileName","ActiveProfileName","CurrentProfileName",
            "ProfileName","AccountName","SelectedName","Name"
        };
                for (int i = 0; i < propNames.Length; i++)
                {
                    var p = t.GetProperty(propNames[i],
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (p != null && p.PropertyType == typeof(string))
                    {
                        var val = p.GetValue(form) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }

                // méthodes qui renvoient string
                string[] methodNames = { "GetCurrentProfileName", "GetSelectedProfileName", "GetProfileName" };
                for (int i = 0; i < methodNames.Length; i++)
                {
                    var m = t.GetMethod(methodNames[i],
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                    if (m != null && m.ReturnType == typeof(string))
                    {
                        var val = m.Invoke(form, null) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }

                // objet "SelectedProfile"/"CurrentProfile"/"ActiveProfile" possédant une propriété Name/name
                var pObj = t.GetProperty("SelectedProfile") ??
                           t.GetProperty("CurrentProfile") ??
                           t.GetProperty("ActiveProfile");
                if (pObj != null)
                {
                    var obj = pObj.GetValue(form);
                    if (obj != null)
                    {
                        var nameProp = obj.GetType().GetProperty("Name") ?? obj.GetType().GetProperty("name");
                        if (nameProp != null)
                        {
                            var val = nameProp.GetValue(obj) as string;
                            if (!string.IsNullOrWhiteSpace(val)) return val;
                        }
                    }
                }

                // variable d'environnement en dernier recours
                var env = Environment.GetEnvironmentVariable("SNA_ACCOUNT");
                if (!string.IsNullOrWhiteSpace(env)) return env;
            }
            catch { /* non bloquant */ }
            return null; // inconnu → on ne bloque pas, TryApplyScheduleCsv gère le cas
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
                        // double guillemet "" -> guillemet littéral
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
            await Task.Delay(rng.Next(3000, 7000), ct); // 3–7s pour simuler l’humain
    }
}