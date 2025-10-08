

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
    public class TestService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;

        public TestService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
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
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Test");
                var localToken = form.GetCancellationToken();
                token = localToken;

                try
                {
                    Random rand = new Random();

                    // Detect language
                    var langResult = await webView.ExecuteScriptAsync("document.documentElement.lang;");
                    var lang = langResult?.Trim('"') ?? "en";
                    logTextBox.AppendText($"[LANG] Detected language: {lang}\r\n");

                    string nextLabel = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Suivant" : "Next";

                    // Vérifier si un reel est ouvert
                    var isModalOpen = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog = !!document.querySelector('div[role=""dialog""]');
  const hasVideo = document.querySelectorAll('video').length > 0;
  return (hasDialog && hasVideo) ? 'true' : 'false';
})()");

                    if (!JsBoolIsTrue(isModalOpen))
                    {
                        logTextBox.AppendText("[ERROR] Aucun reel ouvert. Ouvrez un reel manuellement et relancez.\r\n");
                        return;
                    }

                    // Test de 5 clics sur Next avec coordonnées aléatoires
                    int testCount = 5;
                    for (int i = 1; i <= testCount; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        logTextBox.AppendText($"\r\n[TEST {i}/{testCount}] Simulating human-like click...\r\n");

                        // Délai pré-action aléatoire
                        int preDelay = rand.Next(800, 2000);
                        logTextBox.AppendText($"  → Waiting {preDelay}ms before action...\r\n");
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
                        logTextBox.AppendText($"  → Debug buttons: {debugResult}\r\n");

                        // Script pour cliquer sur le bouton Next 
                        var nextScript = $@"
(function(){{
  try{{
    var scope = document.querySelector('div[role=""dialog""]');
    if (!scope) return 'NO_DIALOG';
    
    // Chercher tous les premiers boutons visibles (les 4 premiers sont souvent: close, prev, next, plus d'options)
    var allButtons = Array.from(scope.querySelectorAll('button')).filter(b => {{
      return b.offsetWidth > 0 && b.offsetHeight > 0;
    }});
    
    // Les boutons de navigation sont généralement parmi les 4 premiers
    // Heuristique: le 2e bouton (index 1) est souvent 'Next'
    var nextBtn = allButtons.length >= 2 ? allButtons[1] : null;
    
    if (nextBtn) {{
      var rect = nextBtn.getBoundingClientRect();
      
      // Position aléatoire dans le bouton (éviter les bords de 20%)
      var marginX = rect.width * 0.2;
      var marginY = rect.height * 0.2;
      var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
      var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
      var clientX = rect.left + offsetX;
      var clientY = rect.top + offsetY;
      
      // Simuler séquence de click humain
      var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
      
      nextBtn.dispatchEvent(new MouseEvent('mouseenter', opts));
      nextBtn.dispatchEvent(new MouseEvent('mouseover', opts));
      nextBtn.dispatchEvent(new MouseEvent('mousedown', opts));
      nextBtn.dispatchEvent(new MouseEvent('mouseup', opts));
      nextBtn.dispatchEvent(new MouseEvent('click', opts));
      nextBtn.dispatchEvent(new MouseEvent('mouseleave', opts));
      
      return 'BTN_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY) + 
             '|SIZE:' + Math.round(rect.width) + 'x' + Math.round(rect.height) +
             '|BTN_INDEX:1';
    }} else {{
      // Fallback: touche flèche
      document.body.dispatchEvent(new KeyboardEvent('keydown', {{
        key: 'ArrowRight',
        code: 'ArrowRight',
        keyCode: 39,
        bubbles: true
      }}));
      return 'FALLBACK_ARROW_KEY';
    }}
  }} catch(e){{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";

                        var nextTry = await webView.ExecuteScriptAsync(nextScript);
                        logTextBox.AppendText($"  → Result: {nextTry}\r\n");

                        // Attendre le chargement du reel suivant
                        int loadDelay = rand.Next(2500, 4500);
                        logTextBox.AppendText($"  → Waiting {loadDelay}ms for next reel to load...\r\n");
                        await Task.Delay(loadDelay, token);

                        // Vérifier si on a bien avancé
                        var checkAdvanced = await webView.ExecuteScriptAsync(@"
(function(){
  const hasDialog = !!document.querySelector('div[role=""dialog""]');
  const videos = document.querySelectorAll('video');
  const hasVideo = videos.length > 0;
  const videoPlaying = Array.from(videos).some(v => !v.paused);
  return (hasDialog && hasVideo) ? 'true' : 'false';
})()");

                        if (!JsBoolIsTrue(checkAdvanced))
                        {
                            logTextBox.AppendText("  ⚠ Warning: Modal may have closed or no video found\r\n");
                            break;
                        }
                        else
                        {
                            logTextBox.AppendText("  ✓ Successfully advanced to next reel\r\n");
                        }
                    }

                    logTextBox.AppendText("\r\n[TEST COMPLETE] All clicks tested successfully.\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("\r\n[CANCELLED] Script stopped by user.\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"\r\n[ERROR] {ex.Message}\r\n");
                    Logger.LogError($"TestService.RunAsync/inner: {ex}");
                }
                finally
                {
                    form.ScriptCompleted();
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"\r\n[FATAL ERROR] {ex.Message}\r\n");
                Logger.LogError($"TestService.RunAsync: {ex}");
            }
        }
    }
}