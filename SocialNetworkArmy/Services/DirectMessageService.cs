using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class DirectMessageService
    {
        private readonly WebView2 _webView;
        private readonly System.Windows.Forms.TextBox _log;
        private readonly Profile _profile;
        private readonly InstagramBotForm _form;

        // Timings
        private const int WaitAfterDirectMs = 5000;
        private const int WaitAfterKItemMs = 5000;
        private const int InterProfileMs = 5000;
        private const int ReadyTimeoutMs = 12000;
        private const int EditorHydrateMs = 3000;
        private const int ThreadOpenMaxMs = 4000;

        public DirectMessageService(WebView2 webView, System.Windows.Forms.TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            _webView = webView;
            _log = logTextBox;
            _profile = profile;
            _form = form;
        }

        private void Log(string m) => _log?.AppendText("[DM] " + m + Environment.NewLine);

        // ---------- JS safe wrapper ----------
        private async Task<string> ExecJs(string js, CancellationToken token, string tag = null)
        {
            string wrapped =
@"(function(){
  try {
    var __val = (function(){ " + js + @" })();
    if (typeof __val === 'undefined') return JSON.stringify('undefined');
    if (typeof __val === 'string') return JSON.stringify(__val);
    try { return JSON.stringify(__val); } catch(e) { return JSON.stringify(String(__val)); }
  } catch(e) {
    try { return JSON.stringify('ERR:'+(e && e.message ? e.message : e)); } catch(_){
      return JSON.stringify('ERR');
    }
  }
})();";
            var res = await ExecuteScriptWithCancellationAsync(_webView, wrapped, token);
            if (!string.IsNullOrEmpty(tag)) Log(tag + res);
            return res ?? "\"undefined\"";
        }

        private static async Task<string> ExecuteScriptWithCancellationAsync(WebView2 webView, string script, CancellationToken token)
        {
            if (webView?.CoreWebView2 == null) throw new InvalidOperationException("WebView2 non initialisé.");
            var execTask = webView.ExecuteScriptAsync(script);
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (token.Register(() =>
            {
                try { webView.CoreWebView2.Stop(); } catch { }
                cancelTcs.TrySetResult(true);
            }))
            {
                var done = await Task.WhenAny(execTask, cancelTcs.Task).ConfigureAwait(true);
                if (done == cancelTcs.Task) throw new OperationCanceledException(token);
                return await execTask.ConfigureAwait(true);
            }
        }

        private static string UnQ(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();

            // Enlever les guillemets JSON (simples ou échappés)
            while (s.Length >= 2 &&
                   ((s[0] == '"' && s[s.Length - 1] == '"') ||
                    (s.StartsWith("\\\"") && s.EndsWith("\\\""))))
            {
                if (s.StartsWith("\\\"") && s.EndsWith("\\\""))
                    s = s.Substring(2, s.Length - 4);
                else
                    s = s.Substring(1, s.Length - 2);
                s = s.Trim();
            }

            return s;
        }

        private static Task HumanPauseAsync(int ms, CancellationToken token) => Task.Delay(ms, token);

        // ============= ENTRY =============
        public async Task RunAsync(CancellationToken token = default)
        {
            await _form.StartScriptAsync("Direct Messages");
            var runToken = token.CanBeCanceled ? token : _form.GetCancellationToken();
            var rng = new Random();

            try
            {
                var dataDir = "data";
                var messagesPath = Path.Combine(dataDir, "dm_messages.txt");
                var targetsPath = Path.Combine(dataDir, "dm_targets.txt");

                if (!File.Exists(messagesPath)) { Log("Fichier dm_messages.txt manquant."); return; }
                var messages = File.ReadAllLines(messagesPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (messages.Count == 0) { Log("Aucun message valide."); return; }

                if (!File.Exists(targetsPath)) { Log("Fichier dm_targets.txt manquant."); return; }
                var targets = File.ReadAllLines(targetsPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (targets.Count == 0) { Log("Aucune cible valide."); return; }

                targets = targets.OrderBy(_ => rng.Next()).ToList();

                foreach (var target in targets)
                {
                    runToken.ThrowIfCancellationRequested();
                    Log($"Profil : {target}");

                    _webView.CoreWebView2.Navigate($"https://www.instagram.com/{target}/");
                    await WaitForNavigationCompletedAsync(runToken);
                    await Task.Delay(400, runToken);

                    // 1) Bouton direct
                    bool dmReady = false;
                    var directClicked = await TryClickDirectButtonAsync(runToken);
                    Log(directClicked ? "[DIRECT] clicked" : "[DIRECT] not_found");

                    if (directClicked)
                    {
                        await HumanPauseAsync(WaitAfterDirectMs, runToken);
                        var st = await EnsureOnDmPageAsync(runToken, ReadyTimeoutMs, "[READY-DIRECT] ");
                        dmReady = st == "editor" || st == "url" || st == "dialog";
                    }

                    // 2) Kebab
                    if (!dmReady)
                    {
                        var opened = await TryOpenKebabMenuAsync(runToken);
                        Log(opened ? "[KEBAB] ouvert" : "[KEBAB] introuvable");
                        if (!opened) { await HumanPauseAsync(InterProfileMs, runToken); continue; }

                        var itemClicked = await ClickSendItemInMenuAsync(runToken);
                        Log(itemClicked ? "[K-ITEM] clicked" : "[K-ITEM] not_found (on continue quand même)");
                        // ON VIRE LE CONTINUE ICI - le kebab marche même si le log dit not_found

                        await HumanPauseAsync(WaitAfterKItemMs, runToken);
                        var st = await EnsureOnDmPageAsync(runToken, ReadyTimeoutMs, "[READY-ITEM] ");
                        if (st == "dialog")
                        {
                            if (await AdvanceDialogIfNeededAsync(runToken))
                            {
                                await Task.Delay(900, runToken);
                                st = await EnsureOnDmPageAsync(runToken, ReadyTimeoutMs, "[READY-AFTER-ADV] ");
                            }
                        }
                        dmReady = st == "editor" || st == "url" || st == "dialog";
                        if (!dmReady) { Log("DM non prêt après item → suivant."); await HumanPauseAsync(InterProfileMs, runToken); continue; }
                    }

                    // 3) Thread
                    var threadOk = await EnsureThreadOpenAsync(runToken);
                    Log(threadOk ? "[THREAD] ok" : "[THREAD] fallback/no thread");

                    // 4) Hydratation
                    var hydrated = await WaitEditorHydratedAsync(runToken, EditorHydrateMs);
                    Log(hydrated ? "[EDITOR] hydrated" : "[EDITOR] no-lexical");

                    // 5) FRAPPE (NOUVELLE VERSION)
                    var msg = messages[rng.Next(messages.Count)];
                    var typed = await TypeMessageImprovedAsync(msg, runToken);
                    Log(typed ? "[TYPE] ok" : "[TYPE] ko");
                    if (!typed) { await HumanPauseAsync(InterProfileMs, runToken); continue; }

                    await Task.Delay(300 + rng.Next(400), runToken);

                    var sent = await ClickSendAsync(runToken);
                    Log(sent ? "[SEND] ok" : "[SEND] ko");

                    await HumanPauseAsync(InterProfileMs, runToken);
                }

                Log("Tous les messages ont été traités.");
            }
            catch (OperationCanceledException)
            {
                try { _webView?.CoreWebView2?.Stop(); } catch { }
                Log("Script annulé par l'utilisateur.");
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                Logger.LogError("Erreur dans DirectMessageService : " + ex);
            }
            finally
            {
                _form.ScriptCompleted();
            }
        }

        // ---------- Navigation ----------
        private async Task WaitForNavigationCompletedAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2NavigationCompletedEventArgs> h = null;
            h = (s, e) =>
            {
                _webView.NavigationCompleted -= h;
                if (e.IsSuccess) tcs.TrySetResult(true);
                else tcs.TrySetException(new Exception($"Navigation failed: {e.WebErrorStatus}"));
            };
            _webView.NavigationCompleted += h;
            using (token.Register(() => tcs.TrySetCanceled(token)))
                await tcs.Task;

            await Task.Delay(300, token);
        }

        // ---------- DIRECT BUTTON ----------
        private async Task<bool> TryClickDirectButtonAsync(CancellationToken token)
        {
            const string js = @"
const norm=s=>(s||'').replace(/\u00A0/g,' ').trim();
const isVis=el=>{const r=el.getBoundingClientRect(),cs=getComputedStyle(el);return r.width>0&&r.height>0&&cs.display!=='none'&&cs.visibility!=='hidden';};
const re=/(Message|Envoyer un message|Envoyer|Contacter|Contact|Send message|Send|DM|Nachricht|Mensaje|Messaggio)/i;
let el=[...document.querySelectorAll('button,a,div[role=""button""]')].filter(isVis).find(n=>re.test(norm(n.textContent))||re.test(n.getAttribute('aria-label')||'')) ||
        document.querySelector('button:has(svg[aria-label*=""message"" i]),div[role=button]:has(svg[aria-label*=""message"" i])') ||
        document.querySelector('a[href*=""/direct/""]');
if(!el) return 'no_direct';
const r=el.getBoundingClientRect();
if(r.top<0||r.left<0||r.bottom>innerHeight||r.right>innerWidth) el.scrollIntoView({block:'center',inline:'center'});
['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'].forEach(t=>el.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2})));
return 'clicked';";
            var r = UnQ(await ExecJs(js, token));
            return r == "clicked";
        }

        // ---------- KEBAB ----------
        private async Task<bool> TryOpenKebabMenuAsync(CancellationToken token)
        {
            const string js = @"
const s=[...document.querySelectorAll('svg[aria-label]')].find(x=>/options|plus|more|menu/i.test(x.getAttribute('aria-label')||'')); 
if(!s) return false;
const el=s.closest('button,[role=button]')||s.closest('div');
if(!el) return false;
const r=el.getBoundingClientRect();
if(r.top<0||r.bottom>innerHeight||r.left<0||r.right>innerWidth) el.scrollIntoView({block:'center'});
['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'].forEach(t=>el.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true})));
return !!document.querySelector('[role=""dialog""],[aria-modal=""true""],div[role=""menu""]');";
            var res = UnQ(await ExecJs(js, token));
            return res == "true";
        }

        private async Task<bool> ClickSendItemInMenuAsync(CancellationToken token)
        {
            const string js = @"
const scope=[...document.querySelectorAll('[role=""dialog""],[aria-modal=""true""],div[role=""menu""]')].pop()||document;
const re=/(Envoyer un message|Send message|Message|Messaggio|Mensaje|Nachricht|Chat)/i;
const el=[...scope.querySelectorAll('button,[role=button],a[role=button]')]
 .find(e=> re.test((e.textContent||'').trim()) || re.test(e.getAttribute('aria-label')||'') );
if(!el) return 'not_found';
const r=el.getBoundingClientRect();
['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click']
  .forEach(t=>el.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2})));
return 'clicked';";
            for (int i = 0; i < 8; i++)
            {
                var r = UnQ(await ExecJs(js, token));
                if (r == "clicked") return true;
                await Task.Delay(200, token);
            }
            return false;
        }

        // ---------- DM readiness ----------
        private async Task<string> EnsureOnDmPageAsync(CancellationToken token, int timeoutMs, string tag)
        {
            const string js = @"
const editors=()=>{
  const sels=[
    '[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]',
    'div[contenteditable=""true""][role=""textbox""]',
    'div[contenteditable=""true""][aria-label*=""message"" i]',
    'div[contenteditable=""true""][aria-placeholder*=""message"" i]',
    'textarea[placeholder]'
  ];
  let arr=[]; for(const s of sels) arr.push(...document.querySelectorAll(s));
  arr=Array.from(new Set(arr)).filter(e=>{const r=e.getBoundingClientRect(),cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.display!=='none'&&cs.visibility!=='hidden';});
  return arr;
};
if(location.pathname.includes('/direct/')){
  const eds=editors();
  if(eds.length) return 'editor';
  return 'url';
}
if(document.querySelector('[role=""dialog""],[aria-modal=""true""]')){
  const eds=editors();
  if(eds.length) return 'editor';
  return 'dialog';
}
return 'no';";
            var start = Environment.TickCount;
            string last = null;
            while (Environment.TickCount - start < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                var res = UnQ(await ExecJs(js, token));
                if (res != last) { Log(tag + res); last = res; }
                if (res == "editor" || res == "url" || res == "dialog") return res;
                await Task.Delay(300, token);
            }
            Log(tag + "timeout");
            return "timeout";
        }

        // ---------- Thread ----------
        private async Task<bool> EnsureThreadOpenAsync(CancellationToken token)
        {
            const string js = @"
const hasEditor = !!(document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
                     document.querySelector('div[contenteditable=""true""][role=""textbox""]'));
if(hasEditor) return 'has_editor';

let item = document.querySelector('a[href*=""/direct/t/""]:not([aria-hidden=""true""])') ||
           document.querySelector('[role=""row""], [role=""listitem""]');
if(!item) return 'no_item';
const r=item.getBoundingClientRect();
if(r.top<0||r.bottom>innerHeight||r.left<0||r.right>innerWidth) item.scrollIntoView({block:'center'});
['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'].forEach(t=>item.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true})));
return 'clicked';";
            var start = Environment.TickCount;
            while (Environment.TickCount - start < ThreadOpenMaxMs)
            {
                var res = UnQ(await ExecJs(js, token));
                if (res == "has_editor") return true;
                if (res == "clicked")
                {
                    await Task.Delay(300, token);
                    var check = UnQ(await ExecJs(
                        @"!!(document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
                            document.querySelector('div[contenteditable=""true""][role=""textbox""]'))", token));
                    if (check == "true") return true;
                }
                await Task.Delay(200, token);
            }
            return false;
        }

        // ---------- Hydratation ----------
        private async Task<bool> WaitEditorHydratedAsync(CancellationToken token, int maxMs)
        {
            const string js = @"
const el=document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
                       document.querySelector('div[contenteditable=""true""][role=""textbox""]');
if(!el) return 'no_el';
const host=el.closest('[data-lexical-editor]');
return (!!host && !!host.__lexicalEditor) ? 'ok' : 'no_lexical';";
            var start = Environment.TickCount;
            while (Environment.TickCount - start < maxMs)
            {
                var r = UnQ(await ExecJs(js, token));
                if (r == "ok") return true;
                if (r == "no_el") return false;
                await Task.Delay(120, token);
            }
            return false;
        }

        // ---------- TYPE AVEC RETRIES ----------
        private async Task<bool> TypeMessageImprovedAsync(string text, CancellationToken token)
        {
            string msg = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");

            // Essayer 3 fois avec des délais croissants
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    Log($"  → Retry {attempt}...");
                    await Task.Delay(800, token);
                }

                // Trouver l'éditeur (d'après ton screenshot il a data-lexical-editor directement)
                string js = @"
const msg = '" + msg + @"';

// Chercher tous les éditeurs possibles
const el = document.querySelector('div[data-lexical-editor=""true""][contenteditable=""true""]') ||
           document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
           document.querySelector('div[contenteditable=""true""][role=""textbox""]');

if(!el) return 'no_editor';

// Scroller vers l'élément et focus
const rect = el.getBoundingClientRect();
if (rect.top < 0 || rect.bottom > window.innerHeight) {
    el.scrollIntoView({block: 'center', behavior: 'smooth'});
}

// Focus + click physique
el.focus();
el.click();

// Attendre 50ms que le focus soit actif
setTimeout(() => {}, 50);

// Clipboard paste
try {
    const dt = new DataTransfer();
    dt.setData('text/plain', msg);
    const pasteEvent = new ClipboardEvent('paste', {
        clipboardData: dt, 
        bubbles: true, 
        cancelable: true
    });
    el.dispatchEvent(pasteEvent);
    
    // Trigger input event aussi
    el.dispatchEvent(new Event('input', {bubbles: true}));
} catch(e) {
    return 'paste_error:' + e.message;
}

return 'paste_sent';
";

                var res = UnQ(await ExecJs(js, token));
                Log($"  → Paste: {res}");

                if (res == "no_editor")
                {
                    await Task.Delay(500, token);
                    continue;
                }

                // Attendre que le paste s'applique
                await Task.Delay(600, token);

                // Vérifier
                var verify = UnQ(await ExecJs($@"
const el = document.querySelector('div[data-lexical-editor=""true""][contenteditable=""true""]') ||
           document.querySelector('div[contenteditable=""true""][role=""textbox""]');
if (!el) return 'no_editor';

const content = (el.textContent || el.innerText || '').trim();
return content.includes('{msg.Replace("'", "\\'")}') ? 'found' : 'not_found';
", token));

                Log($"  → Verify: {verify}");

                if (verify == "found") return true;

                // Fallback: forcer en DOM direct
                if (attempt == 2)
                {
                    Log("  → Force final...");
                    var force = await ExecJs($@"
const msg = '{msg}';
const el = document.querySelector('div[data-lexical-editor=""true""][contenteditable=""true""]') ||
           document.querySelector('div[contenteditable=""true""][role=""textbox""]');

if (!el) return 'no_editor';

try {{
    el.focus();
    el.innerHTML = '';
    
    // Créer paragraphe + span comme dans ton screenshot
    const p = document.createElement('p');
    p.className = 'xat24cr xdj266r';
    p.dir = 'auto';
    
    const span = document.createElement('span');
    span.className = 'x3jgonx';
    span.setAttribute('data-lexical-text', 'true');
    span.textContent = msg;
    
    p.appendChild(span);
    el.appendChild(p);
    
    // Events
    el.dispatchEvent(new Event('input', {{bubbles:true}}));
    el.dispatchEvent(new Event('change', {{bubbles:true}}));
    
    return 'forced';
}} catch(e) {{
    return 'error:' + e.message;
}}
", token);

                    await Task.Delay(300, token);

                    var finalCheck = UnQ(await ExecJs($@"
const el = document.querySelector('div[contenteditable=""true""][role=""textbox""]');
return el && el.textContent.includes('{msg}') ? 'found' : 'not_found';
", token));

                    Log($"  → Final: {finalCheck}");
                    return finalCheck == "found";
                }
            }

            return false;
        }

        // ---------- SEND ----------
        private async Task<bool> ClickSendAsync(CancellationToken token)
        {
            const string js = @"
const isVis=el=>{const r=el.getBoundingClientRect(),cs=getComputedStyle(el);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
const svg=[...document.querySelectorAll('svg[aria-label]')].find(s=>/send|envoyer/i.test(s.getAttribute('aria-label')||'')); 
let btn = svg ? svg.closest('button,[role=button]') : null;
if(!btn){
  btn=[...document.querySelectorAll('button,[role=button]')].filter(isVis)
    .find(b=>/send|envoyer/i.test(b.getAttribute('aria-label')||'')||/(^|\s)(Send|Envoyer)(\s|$)/i.test(b.textContent||''));}
if(btn){
  const r=btn.getBoundingClientRect(); btn.scrollIntoView({block:'center',inline:'center'});
  ['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click']
    .forEach(t=>btn.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2})));
  return {ok:true, via:'button'};
}else{
  let edit=document.activeElement&&(document.activeElement.isContentEditable||document.activeElement.getAttribute('role')==='textbox')
    ? document.activeElement
    : document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""], div[contenteditable=""true""][role=""textbox""]');
  if(edit){
    const kd=new KeyboardEvent('keydown',{key:'Enter',keyCode:13,which:13,bubbles:true});
    const ku=new KeyboardEvent('keyup'  ,{key:'Enter',keyCode:13,which:13,bubbles:true});
    edit.dispatchEvent(kd); edit.dispatchEvent(ku);
    return {ok:true, via:'enter'};
  } else return {ok:false};
}";
            for (int i = 0; i < 5; i++)
            {
                var r = await ExecJs(js, token);
                if (r.IndexOf("\"ok\":true", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                await Task.Delay(250, token);
            }
            return false;
        }

        // ---------- Dialog advance ----------
        private async Task<bool> AdvanceDialogIfNeededAsync(CancellationToken token)
        {
            const string js = @"
const dlg=[...document.querySelectorAll('[role=""dialog""],[aria-modal=""true""]')].pop();
if(!dlg) return 'no_dialog';
const re=/(Suivant|Next|Chat|Message|Continuer|Continue)/i;
const btn=[...dlg.querySelectorAll('button,[role=button]')].find(b=>re.test((b.textContent||'').trim())||re.test(b.getAttribute('aria-label')||''));
if(!btn) return 'no_btn';
const r=btn.getBoundingClientRect();
['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click']
  .forEach(t=>btn.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2})));
return 'clicked';";
            for (int i = 0; i < 8; i++)
            {
                var r = UnQ(await ExecJs(js, token));
                if (r == "clicked") return true;
                await Task.Delay(300, token);
            }
            return false;
        }
    }
}