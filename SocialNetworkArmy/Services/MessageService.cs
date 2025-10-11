using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    public class MessageService
    {
        private readonly WebView2 _webView;
        private readonly Action<string> _log;
        private readonly Random _rng = new Random();

        public MessageService(WebView2 webView, Action<string> log)
        {
            _webView = webView;
            _log = log;
        }

        // ====== EXECUTION JAVASCRIPT ======
        private async Task<string> ExecJsAsync(string js, CancellationToken token, string tag = null)
        {
            var res = await ExecuteScriptWithCancellationAsync(_webView, js, token);

            if (res != null && res.StartsWith("\"") && res.EndsWith("\""))
            {
                res = res.Substring(1, res.Length - 2);
                res = res.Replace("\\\"", "\"");
            }

            var result = res ?? "ERROR_NO_RESPONSE";
            if (!string.IsNullOrEmpty(tag)) _log(tag + result);
            return result;
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
        // ====== FRAPPE HUMANISÉE - VERSION CORRIGÉE ======
        // ====== FRAPPE HUMANISÉE - VERSION CORRIGÉE ======
        public async Task<bool> TypeMessageImprovedAsync(string text, CancellationToken token)
        {
            string escapedMsg = text
                .Replace("\\", "\\\\")
                .Replace("\u2018", "'")
                .Replace("\u2019", "'")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "");

            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();

                if (attempt > 0)
                {
                    _log($"  → Retry {attempt}/3...");
                    await ExecJsAsync(@"
(function(){ 
  var scope = document.querySelector('div[role=""dialog""]') || document;
  var ta = scope.querySelector('textarea');
  var ce = scope.querySelector('div[contenteditable=""true""][role=""textbox""]');
  var input = ta || ce;
  if (input) { 
    if (ta) { 
      ta.value = ''; 
      ta.dispatchEvent(new Event('input', {bubbles: true})); 
    } else { 
      ce.textContent = ''; 
      ce.dispatchEvent(new Event('input', {bubbles: true})); 
    } 
  } 
  return 'cleared'; 
})();", token);
                    await Task.Delay(800, token);
                }

                var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}
  
  const text = '{escapedMsg}';
  const chars = Array.from(text);
  
  // CORRECTION 1: Détecter l'input AVANT de commencer la frappe
  let input = document.querySelector('[data-test-input=""true""]');
  if (!input) {{
    var allInputs = document.querySelectorAll('textarea, div[contenteditable=""true""], input[type=""text""], div[role=""textbox""]');
    
    var messageInputs = [];
    for (var i = 0; i < allInputs.length; i++) {{
        var el = allInputs[i];
        var rect = el.getBoundingClientRect();
        var style = getComputedStyle(el);
        
        if (rect.width === 0 || rect.height === 0 || 
            style.display === 'none' || style.visibility === 'hidden') {{
            continue;
        }}
        
        var placeholder = (el.getAttribute('placeholder') || '').toLowerCase();
        var ariaLabel = (el.getAttribute('aria-label') || '').toLowerCase();
        
        if (placeholder.indexOf('message') !== -1 || 
            placeholder.indexOf('écrire') !== -1 ||
            ariaLabel.indexOf('message') !== -1 ||
            ariaLabel.indexOf('écrire') !== -1) {{
            
            messageInputs.push({{
                index: i,
                tag: el.tagName,
                contenteditable: el.getAttribute('contenteditable')
            }});
        }}
    }}
    
    if (messageInputs.length === 0) {{
        return 'NO_INPUT';
    }}
    
    var targetIndex = messageInputs[0].index;
    allInputs[targetIndex].setAttribute('data-test-input', 'true');
    
    input = allInputs[targetIndex];
  }}
  
  const isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
  
  // CORRECTION 2: Scroll et focus AVANT de commencer à taper
  input.scrollIntoView({{behavior:'instant', block:'center'}});
  await sleep(randomDelay(150, 250));
  
  const rect = input.getBoundingClientRect();
  var marginX = rect.width * 0.2;
  var marginY = rect.height * 0.2;
  var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
  var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
  const clientX = rect.left + offsetX;
  const clientY = rect.top + offsetY;
  
  // Mouvement de souris progressif
  var startX = clientX + (Math.random() * 100 - 50);
  var startY = clientY + (Math.random() * 100 - 50);
  for (let i = 1; i <= 5; i++) {{
    var moveX = startX + (clientX - startX) * (i / 5);
    var moveY = startY + (clientY - startY) * (i / 5);
    input.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
  }}
  
  const opts = {{bubbles:true, cancelable:true, clientX:clientX, clientY:clientY, button:0}};
  
  input.dispatchEvent(new MouseEvent('mousedown', opts));
  input.dispatchEvent(new MouseEvent('mouseup', opts));
  input.dispatchEvent(new MouseEvent('click', opts));
  
  // CORRECTION 3: Délai après le clic pour laisser l'UI réagir
  await sleep(randomDelay(200, 350));
  input.focus();
  
  // CORRECTION 4: Vérifier que l'input est bien focus avant de commencer
  if (document.activeElement !== input) {{
    return 'FOCUS_FAILED';
  }}
  
  // CORRECTION 5: Délai après le focus pour stabiliser
  await sleep(randomDelay(150, 250));
  
  // CORRECTION 6: VIDER L'INPUT AVANT DE COMMENCER (c'est ça le vrai problème!)
  if (isTextarea) {{
    const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const desc = Object.getOwnPropertyDescriptor(proto, 'value');
    desc.set.call(input, '');
    input.dispatchEvent(new Event('input', {{bubbles: true}}));
  }} else {{
    input.textContent = '';
    input.dispatchEvent(new Event('input', {{bubbles: true}}));
  }}
  
  // CORRECTION 7: Délai final avant de commencer à taper (pour être SÛR!)
  await sleep(randomDelay(250, 400));
  
  // Maintenant on commence la frappe
  for (let i = 0; i < chars.length; i++) {{
    const char = chars[i];
    
    // CORRECTION 5: Re-vérifier l'input à chaque caractère
    input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_LOST_AT_' + i;
    
    // CORRECTION 6: Re-focus si nécessaire (sans délai pour ne pas ralentir)
    if (document.activeElement !== input) {{
      input.focus();
    }}
    
    try {{
      if (isTextarea) {{
        const currentValue = input.value;
        const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        const desc = Object.getOwnPropertyDescriptor(proto, 'value');
        desc.set.call(input, currentValue + char);
        
        input.dispatchEvent(new Event('input', {{bubbles: true}}));
        input.dispatchEvent(new Event('change', {{bubbles: true}}));
      }} else {{
        document.execCommand('insertText', false, char);
      }}
    }} catch(e) {{
      return 'TYPE_ERROR_AT_' + i + ': ' + (e.message || String(e));
    }}
    
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
    
    // Simulation d'erreurs de frappe occasionnelles
    if (Math.random() < 0.05 && i < chars.length - 1) {{
      await sleep(delay);
      
      const wrongChars = 'qwertyuiopasdfghjklzxcvbnm';
      const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
      
      input = document.querySelector('[data-test-input=""true""]');
      if (!input) return 'INPUT_LOST_ERROR_AT_' + i;
      
      try {{
        if (isTextarea) {{
          const currentValue = input.value;
          const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(input, currentValue + wrongChar);
          input.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('insertText', false, wrongChar);
        }}
      }} catch(e) {{
        return 'ERROR_TYPE_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(100, 250));
      
      input = document.querySelector('[data-test-input=""true""]');
      if (!input) return 'INPUT_LOST_DELETE_AT_' + i;
      
      try {{
        if (isTextarea) {{
          const currentValue = input.value;
          const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(input, currentValue.slice(0, -1));
          input.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('delete', false);
        }}
      }} catch(e) {{
        return 'DELETE_ERROR_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(50, 120));
    }}
    
    // Pause aléatoire occasionnelle
    if (Math.random() < 0.02) {{
      await sleep(randomDelay(400, 800));
    }}
    
    await sleep(delay);
  }}
  
  await sleep(randomDelay(300, 600));
  
  return 'TYPED_SUCCESSFULLY';
}})()";

                int charCount = text.Length;
                int baseTime = charCount * 100;
                int punctuationCount = text.Count(c => ".!?,;".Contains(c));
                int punctuationDelay = punctuationCount * 300;
                int errorDelay = (int)(charCount * 0.05 * 500);
                int totalTime = baseTime + punctuationDelay + errorDelay + 4500;

                _log($"[TYPING] Starting... Attente maximale de {totalTime}ms...");

                // CORRECTION CRITIQUE: Pattern du TestService
                var typingTask = _webView.ExecuteScriptAsync(typingScript);

                _log($"[TYPING] Attente de {totalTime}ms...");
                await Task.Delay(totalTime, token);

                var typingResult = await typingTask;

                _log($"[TYPING] Résultat brut: {typingResult}");

                // Nettoyer la réponse (WebView2 peut retourner avec ou sans guillemets)
                var cleanResult = typingResult?.Trim('"') ?? "";
                _log($"[TYPING] Résultat nettoyé: {cleanResult}");

                if (cleanResult == "TYPED_SUCCESSFULLY")
                {
                    _log($"[TYPING] ✓ Succès");
                    return true;
                }
            }

            _log("[TYPING] ✗ Échec après 3 tentatives");
            return false;
        }

        // ====== FRAPPE PRO MODAL - VERSION CORRIGÉE ======
        public async Task<bool> TypeInProModalAsync(string text, CancellationToken token)
        {
            string escapedMsg = text
                .Replace("\\", "\\\\")
                .Replace("\u2018", "'")
                .Replace("\u2019", "'")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "");

            var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}
  
  const text = '{escapedMsg}';
  const chars = Array.from(text);
  
  let input = document.querySelector('[data-test-input=""true""]');
  if (!input) {{
    var allInputs = document.querySelectorAll('textarea, div[contenteditable=""true""], input[type=""text""], div[role=""textbox""]');
    
    var messageInputs = [];
    for (var i = 0; i < allInputs.length; i++) {{
        var el = allInputs[i];
        var rect = el.getBoundingClientRect();
        var style = getComputedStyle(el);
        
        if (rect.width === 0 || rect.height === 0 || 
            style.display === 'none' || style.visibility === 'hidden') {{
            continue;
        }}
        
        var placeholder = (el.getAttribute('placeholder') || '').toLowerCase();
        var ariaLabel = (el.getAttribute('aria-label') || '').toLowerCase();
        
        if (placeholder.indexOf('message') !== -1 || 
            placeholder.indexOf('écrire') !== -1 ||
            ariaLabel.indexOf('message') !== -1 ||
            ariaLabel.indexOf('écrire') !== -1) {{
            
            messageInputs.push({{
                index: i,
                tag: el.tagName,
                contenteditable: el.getAttribute('contenteditable')
            }});
        }}
    }}
    
    if (messageInputs.length === 0) {{
        return 'NO_INPUT';
    }}
    
    var targetIndex = messageInputs[0].index;
    allInputs[targetIndex].setAttribute('data-test-input', 'true');
    
    input = allInputs[targetIndex];
  }}
  
  const isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
  
  // Correction bloqueurs
  var parent = input;
  while (parent && parent !== document.body) {{
    if (parent.getAttribute('aria-hidden') === 'true') {{
      parent.setAttribute('aria-hidden', 'false');
    }}
    var pStyle = getComputedStyle(parent);
    if (pStyle.pointerEvents === 'none') {{
      parent.style.pointerEvents = 'auto';
    }}
    parent = parent.parentElement;
  }}
  
  input.scrollIntoView({{behavior: 'instant', block: 'center'}});
  
  var rect = input.getBoundingClientRect();
  var clickX = rect.left + rect.width / 2;
  var clickY = rect.top + rect.height / 2;
  
  var opts = {{bubbles: true, cancelable: true, view: window, clientX: clickX, clientY: clickY, button: 0, buttons: 1}};
  
  input.dispatchEvent(new PointerEvent('pointerover', opts));
  input.dispatchEvent(new MouseEvent('mouseenter', opts));
  input.dispatchEvent(new MouseEvent('mouseover', opts));
  input.dispatchEvent(new PointerEvent('pointerdown', opts));
  input.dispatchEvent(new MouseEvent('mousedown', opts));
  input.dispatchEvent(new PointerEvent('pointerup', opts));
  input.dispatchEvent(new MouseEvent('mouseup', opts));
  input.dispatchEvent(new MouseEvent('click', opts));
  
  // CORRECTION 1: Délai après le clic
  await sleep(randomDelay(200, 350));
  
  input.focus();
  input.focus({{preventScroll: false}});
  input.focus({{preventScroll: true}});
  input.click();
  
  if (document.activeElement !== input) {{
    input.tabIndex = 0;
    input.focus();
  }}
  
  var isFocused = document.activeElement === input;
  if (!isFocused) return 'FOCUS_FAILED';
  
  // CORRECTION 2: Délai après le focus pour stabiliser
  await sleep(randomDelay(150, 250));
  
  // CORRECTION 3: Délai final avant de taper (pour être SÛR!)
  await sleep(randomDelay(250, 400));
  
  for (let i = 0; i < chars.length; i++) {{
    const char = chars[i];
    
    input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_LOST_AT_' + i;
    
    if (document.activeElement !== input) {{
      input.focus();
    }}
    
    try {{
      if (isTextarea) {{
        const currentValue = input.value;
        const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        const desc = Object.getOwnPropertyDescriptor(proto, 'value');
        desc.set.call(input, currentValue + char);
        
        input.dispatchEvent(new Event('input', {{bubbles: true}}));
        input.dispatchEvent(new Event('change', {{bubbles: true}}));
      }} else {{
        document.execCommand('insertText', false, char);
      }}
    }} catch(e) {{
      return 'TYPE_ERROR_AT_' + i + ': ' + (e.message || String(e));
    }}
    
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
    
    if (Math.random() < 0.05 && i < chars.length - 1) {{
      await sleep(delay);
      
      const wrongChars = 'qwertyuiopasdfghjklzxcvbnm';
      const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
      
      input = document.querySelector('[data-test-input=""true""]');
      if (!input) return 'INPUT_LOST_ERROR_AT_' + i;
      
      try {{
        if (isTextarea) {{
          const currentValue = input.value;
          const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(input, currentValue + wrongChar);
          input.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('insertText', false, wrongChar);
        }}
      }} catch(e) {{
        return 'ERROR_TYPE_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(100, 250));
      
      input = document.querySelector('[data-test-input=""true""]');
      if (!input) return 'INPUT_LOST_DELETE_AT_' + i;
      
      try {{
        if (isTextarea) {{
          const currentValue = input.value;
          const proto = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(input, currentValue.slice(0, -1));
          input.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('delete', false);
        }}
      }} catch(e) {{
        return 'DELETE_ERROR_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(50, 120));
    }}
    
    if (Math.random() < 0.02) {{
      await sleep(randomDelay(400, 800));
    }}
    
    await sleep(delay);
  }}
  
  await sleep(randomDelay(300, 600));
  
  return 'TYPED_SUCCESSFULLY';
}})()";

            int charCount = text.Length;
            int baseTime = charCount * 100;
            int punctuationCount = text.Count(c => ".!?,;".Contains(c));
            int punctuationDelay = punctuationCount * 300;
            int errorDelay = (int)(charCount * 0.05 * 500);
            int totalTime = baseTime + punctuationDelay + errorDelay + 4500;

            _log($"[OVERLAY TYPING] Starting... Attente maximale de {totalTime}ms...");

            // CORRECTION CRITIQUE: Pattern du TestService
            var typingTask = _webView.ExecuteScriptAsync(typingScript);

            _log($"[OVERLAY TYPING] Attente de {totalTime}ms...");
            await Task.Delay(totalTime, token);

            var typingResult = await typingTask;

            _log($"[OVERLAY TYPING] Résultat: {typingResult}");

            _log($"[OVERLAY TYPING] Résultat brut: {typingResult}");

            var checkContent = await ExecJsAsync(@"
(function() {
    var input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_DISAPPEARED';
    
    var isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
    var content = isTextarea ? input.value : (input.textContent || input.innerText || '');
    
    return 'LENGTH:' + content.length;
})()
", token);

            _log($"[OVERLAY TYPING] Contenu: {checkContent}");

            if (checkContent.Contains("LENGTH:") && !checkContent.Contains("LENGTH:0"))
            {
                _log($"[OVERLAY TYPING] ✓ Succès - Texte présent dans l'input");
                return true;
            }

            _log($"[OVERLAY TYPING] ✗ Échec");
            return false;
        }

        // ====== BOUTON MESSAGE (PRO) - VERSION CORRIGÉE ======
        public async Task<string> TryClickMessageButtonAsync(CancellationToken token)
        {
            const string clickJs = @"
(function(){
  try {
    var norm = function(s) { return (s || '').replace(/\u00A0/g, ' ').trim().toLowerCase(); };
    var isVis = function(el) {
      if (!el) return false;
      try {
        var r = el.getBoundingClientRect();
        var cs = getComputedStyle(el);
        return r.width > 0 && r.height > 0 && cs.display !== 'none' && cs.visibility !== 'hidden';
      } catch(e) { return false; }
    };
    
    var msgBtn = null;
    var buttons = document.querySelectorAll('button, div[role=""button""], a');
    
    for (var i = 0; i < buttons.length; i++) {
      var b = buttons[i];
      if (!isVis(b)) continue;
      var text = norm(b.textContent);
      var label = norm(b.getAttribute('aria-label') || '');
      if (text.indexOf('message') !== -1 || text.indexOf('contacter') !== -1 || 
          text.indexOf('envoyer') !== -1 || text.indexOf('send') !== -1 ||
          label.indexOf('message') !== -1 || label.indexOf('contacter') !== -1 ||
          label.indexOf('envoyer') !== -1 || label.indexOf('send') !== -1) {
        msgBtn = b;
        break;
      }
    }
    
    if (!msgBtn) {
      var svgs = document.querySelectorAll('svg[aria-label]');
      for (var i = 0; i < svgs.length; i++) {
        var svg = svgs[i];
        var label = norm(svg.getAttribute('aria-label') || '');
        if (label.indexOf('message') !== -1 || label.indexOf('contacter') !== -1) {
          var btn = svg.closest('button, div[role=""button""], a');
          if (btn && isVis(btn)) {
            msgBtn = btn;
            break;
          }
        }
      }
    }
    
    if (!msgBtn) {
      var directLinks = document.querySelectorAll('a[href*=""/direct/""]');
      for (var i = 0; i < directLinks.length; i++) {
        if (isVis(directLinks[i])) {
          msgBtn = directLinks[i];
          break;
        }
      }
    }
    
    if (!msgBtn) return 'no_button_found';
    
    var isLink = msgBtn.tagName === 'A';
    var href = msgBtn.getAttribute('href') || '';
    
    var rect = msgBtn.getBoundingClientRect();
    if (rect.top < 0 || rect.bottom > window.innerHeight) {
      msgBtn.scrollIntoView({behavior: 'instant', block: 'center'});
      rect = msgBtn.getBoundingClientRect();
    }
    
    var clickX = rect.left + rect.width / 2;
    var clickY = rect.top + rect.height / 2;
    
    var opts = {bubbles: true, cancelable: true, view: window, clientX: clickX, clientY: clickY, button: 0, buttons: 1};
    
    msgBtn.dispatchEvent(new PointerEvent('pointerdown', opts));
    msgBtn.dispatchEvent(new MouseEvent('mousedown', opts));
    msgBtn.dispatchEvent(new PointerEvent('pointerup', opts));
    msgBtn.dispatchEvent(new MouseEvent('mouseup', opts));
    msgBtn.dispatchEvent(new MouseEvent('click', opts));
    
    if (isLink && href) return 'clicked_link:' + href;
    
    return 'clicked';
  } catch(err) {
    return 'error:' + (err.message || 'unknown');
  }
})();";

            try
            {
                _log("[BUTTON] Recherche et clic sur bouton Message...");
                var clickResult = await ExecJsAsync(clickJs, token);
                await Task.Delay(3000, token);
                if (string.IsNullOrWhiteSpace(clickResult) || clickResult == "null") return "no_button_found";

                _log($"[BUTTON] Clic: {clickResult}");

                if (clickResult.StartsWith("clicked_link:"))
                {
                    var url = clickResult.Substring("clicked_link:".Length);
                    _log($"[BUTTON] Navigation forcée vers: {url}");
                    await _webView.CoreWebView2.ExecuteScriptAsync($"window.location.href = '{url}';");
                    await Task.Delay(3000, token);
                    return "redirected_to_direct";
                }

                if (clickResult != "clicked") return clickResult;

                await Task.Delay(2500, token);
                _log("[BUTTON] Pas de détection → on suppose modale pro ouverte (comme TestService)");
                return "pro_modal_opened";
              
            }
            catch (Exception ex)
            {
                _log($"[BUTTON] Exception: {ex.Message}");
                return "error_csharp:" + ex.Message;
            }
        }

        // ====== VÉRIFICATION PAGE DM ======
        public async Task<string> EnsureOnDmPageAsync(CancellationToken token, int timeoutMs, string tag)
        {
            const string js = @"
(function(){
  if (location.pathname.indexOf('/direct/') !== -1) {
    var loader = document.querySelector('[role=""progressbar""], .loading, [aria-label*=""Loading"" i], [aria-label*=""Chargement"" i]');
    if (loader) {
      var rect = loader.getBoundingClientRect();
      var style = getComputedStyle(loader);
      if (rect.width > 0 && rect.height > 0 && style.display !== 'none') return 'loading';
    }
    
    var editors = function() {
      var sels = ['textarea', 'div[contenteditable=""true""][role=""textbox""]', '[data-lexical-editor] div[contenteditable=""true""]'];
      for (var i = 0; i < sels.length; i++) {
        var found = document.querySelectorAll(sels[i]);
        for (var j = 0; j < found.length; j++) {
          var e = found[j];
          var r = e.getBoundingClientRect();
          var cs = getComputedStyle(e);
          if (r.width > 0 && r.height > 0 && cs.display !== 'none' && cs.visibility !== 'hidden') return true;
        }
      }
      return false;
    };

    if (editors()) return 'editor';
    
    var conversationsList = document.querySelector('[role=""list""]');
    if (conversationsList) {
      var items = conversationsList.querySelectorAll('[role=""listitem""], a[href*=""/direct/t/""]');
      if (items.length > 0) return 'conversation_list';
    }
    
    return 'url_only';
  }

  var modal = document.querySelector('[role=""dialog""], [aria-modal=""true""]');
  if (modal) {
    var editors = function() {
      var sels = ['textarea', 'div[contenteditable=""true""][role=""textbox""]', 'div[contenteditable=""true""]'];
      for (var i = 0; i < sels.length; i++) {
        var found = modal.querySelectorAll(sels[i]);
        for (var j = 0; j < found.length; j++) {
          var e = found[j];
          var r = e.getBoundingClientRect();
          var cs = getComputedStyle(e);
          if (r.width > 0 && r.height > 0 && cs.display !== 'none' && cs.visibility !== 'hidden') return true;
        }
      }
      return false;
    };
    
    if (editors()) return 'editor';
    return 'dialog';
  }

  return 'no';
})()";

            var start = Environment.TickCount;
            string last = null;
            int noChangeCount = 0;

            while (Environment.TickCount - start < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                var res = await ExecJsAsync(js, token);

                if (res != last)
                {
                    _log(tag + res);
                    last = res;
                    noChangeCount = 0;
                }
                else noChangeCount++;

                if (res == "editor" || res == "conversation_list" || res == "dialog") return res;

                if (res == "url_only" && noChangeCount > 10)
                {
                    _log(tag + "stuck_on_url_only → forcing refresh");
                    await ExecJsAsync(@"window.scrollTo(0, 100); window.scrollTo(0, 0);", token);
                    await Task.Delay(500, token);
                }

                if (res == "loading")
                {
                    _log(tag + "page_loading...");
                    await Task.Delay(800, token);
                    continue;
                }

                await Task.Delay(300, token);
            }

            _log(tag + "timeout (last state: " + last + ")");
            return "timeout";
        }

        // ====== OUVRIR THREAD ======
        public async Task<bool> EnsureThreadOpenAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var hasEditor = !!(document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
                     document.querySelector('div[contenteditable=""true""][role=""textbox""]'));
  if (hasEditor) return 'has_editor';
  
  var item = document.querySelector('a[href*=""/direct/t/""]:not([aria-hidden=""true""])') ||
             document.querySelector('[role=""row""], [role=""listitem""]');
  if (!item) return 'no_item';
  
  var r = item.getBoundingClientRect();
  if (r.top < 0 || r.bottom > innerHeight) item.scrollIntoView({block: 'center'});
  
  var events = ['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'];
  for (var i = 0; i < events.length; i++) {
    item.dispatchEvent(new MouseEvent(events[i], {bubbles: true, cancelable: true}));
  }
  
  return 'clicked';
})()";

            var start = Environment.TickCount;
            while (Environment.TickCount - start < 4000)
            {
                var res = await ExecJsAsync(js, token);
                if (res == "has_editor") return true;
                if (res == "clicked")
                {
                    await Task.Delay(300, token);
                    var check = await ExecJsAsync(@"!!(document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') || document.querySelector('div[contenteditable=""true""][role=""textbox""]'))", token);
                    if (check == "true") return true;
                }
                await Task.Delay(200, token);
            }
            return false;
        }

    

        // ====== GÉNÉRATEUR TIMINGS ======
        private List<int> GenerateHumanTypingTimings(int length)
        {
            var timings = new List<int>();
            int baseDelay = 50 + _rng.Next(0, 30);

            for (int i = 0; i < length; i++)
            {
                int delay = baseDelay;

                if (_rng.Next(100) < 5) delay += _rng.Next(200, 600);
                if (i > 2 && _rng.Next(100) < 30) delay -= _rng.Next(10, 25);
                if (i > 0 && i % _rng.Next(10, 16) == 0) delay += _rng.Next(100, 300);

                delay += _rng.Next(-20, 20);
                if (delay < 30) delay = 30;
                if (delay > 800) delay = 800;

                timings.Add(delay);
            }

            return timings;
        }

        // ====== ENVOI ======
        public async Task<bool> ClickSendAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var input = document.querySelector('textarea, div[contenteditable=""true""][role=""textbox""]');
  if (!input) return 'no_input';
  
  input.focus();
  
  // Simple Enter press comme TestService
  var opts = {key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true};
  input.dispatchEvent(new KeyboardEvent('keydown', opts));
  input.dispatchEvent(new KeyboardEvent('keypress', opts));
  input.dispatchEvent(new KeyboardEvent('keyup', opts));
  
  return 'enter_sent';
})()";

            for (int i = 0; i < 3; i++)
            {
                var r = await ExecJsAsync(js, token);
                if (r == "enter_sent")
                {
                    await Task.Delay(1500, token);
                    var check = await ExecJsAsync(@"(function(){ var ta = document.querySelector('textarea, div[contenteditable=""true""][role=""textbox""]'); if (!ta) return 'no_input'; return (ta.value || ta.textContent || '').trim().length === 0 ? 'cleared' : 'not_cleared'; })();", token);
                    if (check == "cleared" || check == "no_input") return true;
                }
                await Task.Delay(500, token);
            }
            return false;
        }

        // ====== ENVOI PRO MODAL ======
        // ====== ENVOI PRO MODAL - VERSION SANS SETTIMEOUT (FIABLE) ======
        public async Task<bool> SendInProModalAsync(CancellationToken token)
        {
            _log("[OVERLAY SEND] Étape 1: Envoi Enter...");

            // ÉTAPE 1: Enter
            const string jsEnter = @"
(function(){
  var input = document.querySelector('[data-test-input=""true""]');
  if (!input) return 'NO_INPUT';
  
  input.focus();
  
  var opts = {key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true};
  input.dispatchEvent(new KeyboardEvent('keydown', opts));
  input.dispatchEvent(new KeyboardEvent('keypress', opts));
  input.dispatchEvent(new KeyboardEvent('keyup', opts));
  
  return 'ENTER_SENT';
})()";

            var enterResult = await ExecJsAsync(jsEnter, token);
            _log($"[OVERLAY SEND] Enter: {enterResult}");

            await Task.Delay(300, token);

            // ÉTAPE 2: Chercher et cliquer bouton Send (texte)
            _log("[OVERLAY SEND] Étape 2: Recherche bouton Send (texte)...");

            const string jsButtonText = @"
(function(){
  var buttons = document.querySelectorAll('button, [role=""button""]');
  for (var i = 0; i < buttons.length; i++) {
    var btn = buttons[i];
    var text = (btn.textContent || '').toLowerCase();
    var label = (btn.getAttribute('aria-label') || '').toLowerCase();
    
    if (text === 'send' || text === 'envoyer' || 
        label.indexOf('send') !== -1 || label.indexOf('envoyer') !== -1) {
      var rect = btn.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        var clickOpts = {bubbles: true, cancelable: true, view: window, clientX: rect.left + rect.width/2, clientY: rect.top + rect.height/2};
        btn.dispatchEvent(new MouseEvent('mousedown', clickOpts));
        btn.dispatchEvent(new MouseEvent('mouseup', clickOpts));
        btn.dispatchEvent(new MouseEvent('click', clickOpts));
        return 'BUTTON_CLICKED';
      }
    }
  }
  return 'NO_BUTTON';
})()";

            var buttonResult = await ExecJsAsync(jsButtonText, token);
            _log($"[OVERLAY SEND] Bouton texte: {buttonResult}");

            await Task.Delay(300, token);

            // ÉTAPE 3: Chercher et cliquer SVG Send
            _log("[OVERLAY SEND] Étape 3: Recherche SVG Send...");

            const string jsSvg = @"
(function(){
  var svgs = document.querySelectorAll('svg[aria-label]');
  for (var i = 0; i < svgs.length; i++) {
    var label = (svgs[i].getAttribute('aria-label') || '').toLowerCase();
    if (label.indexOf('send') !== -1 || label.indexOf('envoyer') !== -1) {
      var parent = svgs[i].closest('button, [role=""button""], div');
      if (parent) {
        var rect = parent.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
          var clickOpts = {bubbles: true, cancelable: true, view: window, clientX: rect.left + rect.width/2, clientY: rect.top + rect.height/2};
          parent.dispatchEvent(new MouseEvent('mousedown', clickOpts));
          parent.dispatchEvent(new MouseEvent('mouseup', clickOpts));
          parent.dispatchEvent(new MouseEvent('click', clickOpts));
          return 'SVG_CLICKED';
        }
      }
    }
  }
  return 'NO_SVG';
})()";

            var svgResult = await ExecJsAsync(jsSvg, token);
            _log($"[OVERLAY SEND] SVG: {svgResult}");

            // ÉTAPE 4: Attendre et vérifier
            _log("[OVERLAY SEND] Étape 4: Vérification finale...");
            await Task.Delay(2000, token);

            const string jsCheck = @"
(function(){
  var input = document.querySelector('[data-test-input=""true""]');
  if (!input) return 'INPUT_DISAPPEARED';
  
  var isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
  var content = isTextarea ? input.value : (input.textContent || input.innerText || '');
  
  if (content.trim().length === 0) {
    return 'MESSAGE_SENT_INPUT_CLEARED';
  } else {
    return 'MESSAGE_NOT_SENT_STILL_PRESENT';
  }
})()";

            var check = await ExecJsAsync(jsCheck, token);
            _log($"[OVERLAY SEND] Vérification: {check}");

            var success = check.Contains("CLEARED") || check.Contains("DISAPPEARED");

            if (success)
            {
                _log("[OVERLAY SEND] ✓ Message envoyé avec succès!");
            }
            else
            {
                _log("[OVERLAY SEND] ✗ Le message n'a pas été envoyé");
            }

            return success;
        }


        // ====== MÉTHODES MANQUANTES POUR DirectMessageService ======

        public async Task<bool> TryOpenKebabMenuAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var svgs = document.querySelectorAll('svg[aria-label]');
  var s = null;
  
  for (var i = 0; i < svgs.length; i++) {
    var label = svgs[i].getAttribute('aria-label') || '';
    if (/options|plus|more|menu/i.test(label)) {
      s = svgs[i];
      break;
    }
  }
  
  if (!s) return 'false';
  
  var el = s.closest('button,[role=button]') || s.closest('div');
  if (!el) return 'false';
  
  var r = el.getBoundingClientRect();
  if (r.top < 0 || r.bottom > innerHeight) {
    el.scrollIntoView({block: 'center'});
  }
  
  var events = ['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'];
  for (var i = 0; i < events.length; i++) {
    el.dispatchEvent(new MouseEvent(events[i], {bubbles: true, cancelable: true}));
  }
  
  var hasMenu = !!document.querySelector('[role=""dialog""],[aria-modal=""true""],div[role=""menu""]');
  return hasMenu ? 'true' : 'false';
})()";

            var res = await ExecJsAsync(js, token);
            return res == "true";
        }

        public async Task<bool> ClickSendItemInMenuAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var menus = document.querySelectorAll('[role=""dialog""],[aria-modal=""true""],div[role=""menu""]');
  var scope = menus.length > 0 ? menus[menus.length - 1] : document;
  
  var re = /(Envoyer un message|Send message|Message|Chat)/i;
  var buttons = scope.querySelectorAll('button,[role=button],a[role=button]');
  var el = null;
  
  for (var i = 0; i < buttons.length; i++) {
    var b = buttons[i];
    var text = (b.textContent || '').trim();
    var label = b.getAttribute('aria-label') || '';
    if (re.test(text) || re.test(label)) {
      el = b;
      break;
    }
  }
  
  if (!el) return 'not_found';
  
  var r = el.getBoundingClientRect();
  var events = ['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'];
  for (var i = 0; i < events.length; i++) {
    el.dispatchEvent(new MouseEvent(events[i], {bubbles: true, cancelable: true, clientX: r.left + r.width/2, clientY: r.top + r.height/2}));
  }
  
  return 'clicked';
})()";

            for (int i = 0; i < 8; i++)
            {
                var r = await ExecJsAsync(js, token);
                if (r == "clicked") return true;
                await Task.Delay(200, token);
            }
            return false;
        }

        public async Task<bool> AdvanceDialogIfNeededAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var dialogs = document.querySelectorAll('[role=""dialog""],[aria-modal=""true""]');
  var dlg = dialogs.length > 0 ? dialogs[dialogs.length - 1] : null;
  
  if (!dlg) return 'no_dialog';
  
  var re = /(Suivant|Next|Chat|Message|Continuer|Continue)/i;
  var buttons = dlg.querySelectorAll('button,[role=button]');
  var btn = null;
  
  for (var i = 0; i < buttons.length; i++) {
    var b = buttons[i];
    var text = (b.textContent || '').trim();
    var label = b.getAttribute('aria-label') || '';
    if (re.test(text) || re.test(label)) {
      btn = b;
      break;
    }
  }
  
  if (!btn) return 'no_btn';
  
  var r = btn.getBoundingClientRect();
  var events = ['pointerover','mouseover','mouseenter','pointerdown','mousedown','pointerup','mouseup','click'];
  for (var i = 0; i < events.length; i++) {
    btn.dispatchEvent(new MouseEvent(events[i], {bubbles: true, cancelable: true, clientX: r.left + r.width/2, clientY: r.top + r.height/2}));
  }
  
  return 'clicked';
})()";

            for (int i = 0; i < 8; i++)
            {
                var r = await ExecJsAsync(js, token);
                if (r == "clicked") return true;
                await Task.Delay(300, token);
            }
            return false;
        }

        public async Task<bool> WaitEditorHydratedAsync(CancellationToken token)
        {
            const string js = @"
(function(){
  var el = document.querySelector('[data-lexical-editor] div[contenteditable=""true""][role=""textbox""]') ||
           document.querySelector('div[contenteditable=""true""][role=""textbox""]');
  if (!el) return 'no_el';
  var host = el.closest('[data-lexical-editor]');
  return (!!host && !!host.__lexicalEditor) ? 'ok' : 'no_lexical';
})()";

            var start = Environment.TickCount;
            const int EditorHydrateMs = 3000;
            while (Environment.TickCount - start < EditorHydrateMs)
            {
                var r = await ExecJsAsync(js, token);
                if (r == "ok") return true;
                if (r == "no_el") return false;
                await Task.Delay(120, token);
            }
            return false;
        }

        // ====== UTILITAIRES ======
        public async Task CloseAnyModalAsync(CancellationToken token)
        {
            await ExecJsAsync(@"(function(){ var m = document.querySelector('[role=""dialog""]'); if (m) { var closeBtn = m.querySelector('button[aria-label*=""Close"" i], button[aria-label*=""Fermer"" i]'); if (closeBtn) closeBtn.click(); else document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', keyCode: 27})); } })();", token);
            await Task.Delay(500, token);
        }
    }
}