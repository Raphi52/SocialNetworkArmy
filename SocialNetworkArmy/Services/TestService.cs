using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class TestService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly Random _rng = new Random();

        public TestService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
        }

        private void Log(string message)
        {
            logTextBox?.AppendText($"[TEST] {message}{Environment.NewLine}");
        }

        private async Task<string> ExecJsAsync(string js, CancellationToken token)
        {
            var res = await webView.ExecuteScriptAsync(js);

            if (res != null && res.StartsWith("\"") && res.EndsWith("\""))
            {
                res = res.Substring(1, res.Length - 2);
                res = res.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "");
            }

            return res ?? "ERROR_NO_RESPONSE";
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            await form.StartScriptAsync("Test Auto Message");
            var runToken = token.CanBeCanceled ? token : form.GetCancellationToken();

            try
            {
                Log("=== TEST AUTOMATIQUE COMPLET ===");
                Log("Ce test va:");
                Log("1. Cliquer sur le bouton Message");
                Log("2. Écrire dans l'overlay/modal");
                Log("3. Envoyer le message");
                Log("");

                // ========== ÉTAPE 1: CLIC SUR BOUTON MESSAGE ==========
                Log("--- Étape 1: Clic sur bouton Message ---");

                var clickResult = await ExecJsAsync(@"
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
    
    if (!msgBtn) return 'NO_BUTTON';
    
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
    
    return 'CLICKED';
  } catch(err) {
    return 'ERROR:' + (err.message || 'unknown');
  }
})();", runToken);

                Log($"Résultat clic: {clickResult}");

                if (clickResult != "CLICKED")
                {
                    Log("✗ Impossible de cliquer sur le bouton Message");
                    return;
                }

                Log("✓ Bouton cliqué, attente de l'overlay...");
                await Task.Delay(2500, runToken);

                // ========== ÉTAPE 2: RECHERCHE INPUT ==========
                Log("");
                Log("--- Étape 2: Recherche de l'input ---");

                var findInput = await ExecJsAsync(@"
(function() {
    var allInputs = document.querySelectorAll('textarea, div[contenteditable=""true""], input[type=""text""], div[role=""textbox""]');
    
    var messageInputs = [];
    for (var i = 0; i < allInputs.length; i++) {
        var el = allInputs[i];
        var rect = el.getBoundingClientRect();
        var style = getComputedStyle(el);
        
        if (rect.width === 0 || rect.height === 0 || 
            style.display === 'none' || style.visibility === 'hidden') {
            continue;
        }
        
        var placeholder = (el.getAttribute('placeholder') || '').toLowerCase();
        var ariaLabel = (el.getAttribute('aria-label') || '').toLowerCase();
        
        if (placeholder.indexOf('message') !== -1 || 
            placeholder.indexOf('écrire') !== -1 ||
            ariaLabel.indexOf('message') !== -1 ||
            ariaLabel.indexOf('écrire') !== -1) {
            
            messageInputs.push({
                index: i,
                tag: el.tagName,
                contenteditable: el.getAttribute('contenteditable')
            });
        }
    }
    
    if (messageInputs.length === 0) {
        return 'NO_INPUT';
    }
    
    var targetIndex = messageInputs[0].index;
    allInputs[targetIndex].setAttribute('data-test-input', 'true');
    allInputs[targetIndex].style.border = '3px solid lime';
    
    return 'FOUND:' + messageInputs.length + '|tag=' + messageInputs[0].tag + '|ce=' + messageInputs[0].contenteditable;
})()
", runToken);

                Log($"Recherche input: {findInput}");

                if (findInput.StartsWith("NO_INPUT"))
                {
                    Log("✗ Aucun input trouvé");
                    return;
                }

                Log("✓ Input trouvé (bordure verte)");

                // ========== ÉTAPE 3: CORRECTION BLOQUEURS + FOCUS ==========
                Log("");
                Log("--- Étape 3: Focus et correction ---");

                var focusResult = await ExecJsAsync(@"
(function() {
    var input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_LOST';
    
    // Correction des bloqueurs
    var parent = input;
    while (parent && parent !== document.body) {
        if (parent.getAttribute('aria-hidden') === 'true') {
            parent.setAttribute('aria-hidden', 'false');
        }
        var pStyle = getComputedStyle(parent);
        if (pStyle.pointerEvents === 'none') {
            parent.style.pointerEvents = 'auto';
        }
        parent = parent.parentElement;
    }
    
    // Scroll
    input.scrollIntoView({behavior: 'instant', block: 'center'});
    
    // Clic réaliste
    var rect = input.getBoundingClientRect();
    var x = rect.left + rect.width / 2;
    var y = rect.top + rect.height / 2;
    
    var opts = {
        bubbles: true,
        cancelable: true,
        view: window,
        clientX: x,
        clientY: y,
        button: 0,
        buttons: 1
    };
    
    input.dispatchEvent(new PointerEvent('pointerover', opts));
    input.dispatchEvent(new MouseEvent('mouseenter', opts));
    input.dispatchEvent(new MouseEvent('mouseover', opts));
    input.dispatchEvent(new PointerEvent('pointerdown', opts));
    input.dispatchEvent(new MouseEvent('mousedown', opts));
    input.dispatchEvent(new PointerEvent('pointerup', opts));
    input.dispatchEvent(new MouseEvent('mouseup', opts));
    input.dispatchEvent(new MouseEvent('click', opts));
    
    input.focus();
    input.focus({preventScroll: false});
    input.focus({preventScroll: true});
    input.click();
    
    if (document.activeElement !== input) {
        input.tabIndex = 0;
        input.focus();
    }
    
    var isFocused = document.activeElement === input;
    return 'FOCUS:' + (isFocused ? 'OK' : 'FAILED');
})()
", runToken);

                Log($"Focus: {focusResult}");

                if (!focusResult.Contains("OK"))
                {
                    Log("⚠ Focus échoué mais on continue...");
                }

                await Task.Delay(500, runToken);

                // ========== ÉTAPE 4: ÉCRITURE ==========
                Log("");
                Log("--- Étape 4: Écriture du message ---");

                string testMessage = "Salut! Test automatique 🚀";
                string escapedMsg = testMessage
                    .Replace("\\", "\\\\")
                    .Replace("\u2018", "'")
                    .Replace("\u2019", "'")
                    .Replace("'", "\\'");

                Log($"[TYPING] Starting...");

                var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}
  
  const text = '{escapedMsg}';
  const chars = Array.from(text);
  
  let input = document.querySelector('[data-test-input=""true""]');
  if (!input) return 'NO_INPUT';
  
  const isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
  
  input.scrollIntoView({{behavior:'smooth', block:'center'}});
  await sleep(randomDelay(200, 400));
  
  const rect = input.getBoundingClientRect();
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
    input.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
  }}
  
  const opts = {{bubbles:true, cancelable:true, clientX:clientX, clientY:clientY, button:0}};
  
  input.dispatchEvent(new MouseEvent('mousedown', opts));
  input.dispatchEvent(new MouseEvent('mouseup', opts));
  input.dispatchEvent(new MouseEvent('click', opts));
  
  await sleep(randomDelay(100, 250));
  input.focus();
  
  for (let i = 0; i < chars.length; i++) {{
    const char = chars[i];
    
    input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_LOST_AT_' + i;
    
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

                int charCount = testMessage.Length;
                int baseTime = charCount * 100;
                int punctuationCount = testMessage.Count(c => ".!?,;".Contains(c));
                int punctuationDelay = punctuationCount * 300;
                int errorDelay = (int)(charCount * 0.05 * 500);
                int totalTime = baseTime + punctuationDelay + errorDelay + 3000;

                var typingTask = webView.ExecuteScriptAsync(typingScript);

                Log($"[TYPING] Attente de {totalTime}ms...");
                await Task.Delay(totalTime, runToken);

                var writeResult = await typingTask;
                Log($"Écriture: {writeResult}");

                Log("✓ Texte écrit (visible dans WebView)");
                await Task.Delay(1000, runToken);

                // ========== ÉTAPE 5: VÉRIFICATION ==========
                Log("");
                Log("--- Étape 5: Vérification du contenu ---");

                var checkContent = await ExecJsAsync(@"
(function() {
    var input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_DISAPPEARED';
    
    var isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
    var content = isTextarea ? input.value : (input.textContent || input.innerText || '');
    
    return 'CONTENT_LENGTH:' + content.length + '|preview=' + content.substring(0, 50);
})()
", runToken);

                Log($"Contenu: {checkContent}");

                // ========== DEBUG ENVOI ==========
                Log("");
                Log("--- DEBUG: Analyse de l'interface ---");

                var debugSend = await ExecJsAsync(@"
(function(){
  var input = document.querySelector('[data-test-input=""true""]');
  if (!input) return 'NO_INPUT';
  
  var info = [];
  
  info.push('INPUT:');
  info.push('  Tag: ' + input.tagName);
  info.push('  ContentEditable: ' + input.getAttribute('contenteditable'));
  info.push('  Value/Text: ' + (input.value || input.textContent || input.innerText));
  
  info.push('\nBUTTONS:');
  var buttons = document.querySelectorAll('button, [role=""button""]');
  var visibleButtons = [];
  
  for (var i = 0; i < buttons.length; i++) {
    var btn = buttons[i];
    var rect = btn.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) {
      var label = btn.getAttribute('aria-label') || '';
      var text = (btn.textContent || '').trim().substring(0, 30);
      var disabled = btn.disabled || btn.getAttribute('aria-disabled') === 'true';
      visibleButtons.push({
        index: i,
        label: label,
        text: text,
        disabled: disabled,
        size: Math.round(rect.width) + 'x' + Math.round(rect.height)
      });
    }
  }
  
  info.push('  Visible buttons: ' + visibleButtons.length);
  for (var i = 0; i < Math.min(5, visibleButtons.length); i++) {
    var b = visibleButtons[i];
    info.push('  [' + b.index + '] ' + b.label + ' | ' + b.text + ' | disabled:' + b.disabled + ' | ' + b.size);
  }
  
  info.push('\nSVG:');
  var svgs = document.querySelectorAll('svg[aria-label]');
  var sendSvgs = [];
  for (var i = 0; i < svgs.length; i++) {
    var label = (svgs[i].getAttribute('aria-label') || '').toLowerCase();
    if (label.indexOf('send') !== -1 || label.indexOf('envoyer') !== -1 || label.indexOf('envoi') !== -1) {
      sendSvgs.push(label);
    }
  }
  info.push('  Send SVGs found: ' + sendSvgs.length);
  for (var i = 0; i < sendSvgs.length; i++) {
    info.push('  - ' + sendSvgs[i]);
  }
  
  return info.join('\n');
})()
", runToken);

                Log($"Debug:\n{debugSend}");

               
// ========== ÉTAPE 6: ENVOI ==========
Log("");
                Log("--- Étape 6: Envoi du message ---");

                var sendResult = await ExecJsAsync(@"
(function(){
  var input = document.querySelector('[data-test-input=""true""]');
  if (!input) return 'NO_INPUT';
  
  input.focus();
  
  // Essai 1: Enter simple
  var opts = {key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true};
  input.dispatchEvent(new KeyboardEvent('keydown', opts));
  input.dispatchEvent(new KeyboardEvent('keypress', opts));
  input.dispatchEvent(new KeyboardEvent('keyup', opts));
  
  // Essai 2: Chercher bouton Send et cliquer
  setTimeout(function() {
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
          break;
        }
      }
    }
  }, 100);
  
  // Essai 3: Chercher SVG send et cliquer parent
  setTimeout(function() {
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
            break;
          }
        }
      }
    }
  }, 200);
  
  return 'TRIGGERED';
})()
", runToken);

                Log($"Envoi: {sendResult}");
                await Task.Delay(2500, runToken);

                // ========== ÉTAPE 7: VÉRIFICATION FINALE ==========
                Log("");
                Log("--- Étape 7: Vérification finale ---");

                var finalCheck = await ExecJsAsync(@"
(function() {
    var input = document.querySelector('[data-test-input=""true""]');
    if (!input) return 'INPUT_DISAPPEARED';
    
    var isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
    var content = isTextarea ? input.value : (input.textContent || input.innerText || '');
    
    if (content.trim().length === 0) {
        return 'MESSAGE_SENT_INPUT_CLEARED';
    } else {
        return 'MESSAGE_NOT_SENT_STILL_PRESENT';
    }
})()
", runToken);

                Log($"État final: {finalCheck}");

                if (finalCheck.Contains("CLEARED") || finalCheck.Contains("DISAPPEARED"))
                {
                    Log("");
                    Log("========================================");
                    Log("✓✓✓ TEST RÉUSSI : MESSAGE ENVOYÉ ! ✓✓✓");
                    Log("========================================");
                }
                else
                {
                    Log("");
                    Log("========================================");
                    Log("⚠ RÉSULTAT INCERTAIN");
                    Log("Vérifiez manuellement si le message est parti");
                    Log("========================================");
                }

            }
            catch (OperationCanceledException)
            {
                Log("Test annulé par l'utilisateur.");
            }
            catch (Exception ex)
            {
                Log($"Erreur: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log($"Cause: {ex.InnerException.Message}");
                }
                Logger.LogError($"Erreur TestService: {ex}");
            }
            finally
            {
                form.ScriptCompleted();
            }
        }
    }
}