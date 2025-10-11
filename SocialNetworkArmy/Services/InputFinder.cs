using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    public class InputFinder
    {
        private readonly WebView2 _webView;
        private readonly Action<string> _log;
        private readonly JsExecutor _jsExecutor;

        public InputFinder(WebView2 webView, Action<string> log)
        {
            _webView = webView;
            _log = log;
            _jsExecutor = new JsExecutor(webView, log);
        }

        public async Task<(int x, int y)> FindInputCoordinatesAsync(CancellationToken token)
        {
            const string findInputScript = @"
(function(){
  try {
    const selectors = [
      'div[contenteditable=""true""][aria-label*=""message"" i]',
      'div[contenteditable=""true""][aria-label*=""écrire"" i]',
      'div[contenteditable=""true""][placeholder*=""message"" i]',
      'textarea[placeholder*=""message"" i]',
      'textarea[aria-label*=""message"" i]',
      'textarea',
      'div[contenteditable=""true""][role=""textbox""]',
      'div[role=""textbox""][contenteditable=""true""]',
      'div[contenteditable=""true""]',
      'input[type=""text""]'
    ];
    
    let input = null;
    let foundSelector = '';
    let allCandidates = [];
    
    for (const sel of selectors) {
      try {
        const elements = document.querySelectorAll(sel);
        for (const el of elements) {
          const rect = el.getBoundingClientRect();
          const style = getComputedStyle(el);
          
          const ariaLabel = el.getAttribute('aria-label') || '';
          const placeholder = el.getAttribute('placeholder') || '';
          
          allCandidates.push({
            selector: sel,
            visible: rect.width > 0 && rect.height > 0,
            display: style.display,
            visibility: style.visibility,
            ariaLabel: ariaLabel,
            placeholder: placeholder
          });
          
          if (rect.width > 0 && rect.height > 0 && 
              style.display !== 'none' && style.visibility !== 'hidden') {
            input = el;
            foundSelector = sel;
            break;
          }
        }
        if (input) break;
      } catch(e) {
        allCandidates.push({error: e.message});
      }
    }
    
    if (!input) {
      return 'ERROR:NO_INPUT:' + JSON.stringify(allCandidates);
    }
    
    const rect = input.getBoundingClientRect();
    const marginX = rect.width * 0.25;
    const marginY = rect.height * 0.3;
    const randomX = marginX + Math.random() * (rect.width - 2 * marginX);
    const randomY = marginY + Math.random() * (rect.height - 2 * marginY);
    const x = Math.round(rect.left + randomX);
    const y = Math.round(rect.top + randomY);
    
    const ariaLabel = input.getAttribute('aria-label') || 'none';
    const isTextarea = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
    
    return 'SUCCESS:' + x + ',' + y + ',' + input.tagName + ',' + foundSelector + ',' + ariaLabel + ',' + (isTextarea ? 'textarea' : 'contenteditable');
  } catch(err) {
    return 'ERROR:EXCEPTION:' + err.message;
  }
})()";

            var result = await _jsExecutor.ExecJsAsync(findInputScript, token);

            if (result.Length > 200)
            {
                _log($"[INPUT] Recherche: {result.Substring(0, 200)}...");
            }
            else
            {
                _log($"[INPUT] Recherche: {result}");
            }

            if (result.StartsWith("ERROR:NO_INPUT:"))
            {
                _log($"[INPUT] ✗ Aucun input trouvé");
                return (-1, -1);
            }

            if (result.StartsWith("ERROR:"))
            {
                _log($"[INPUT] ✗ Erreur: {result}");
                return (-1, -1);
            }

            if (!result.StartsWith("SUCCESS:"))
            {
                _log($"[INPUT] ✗ Format inattendu: {result}");
                return (-1, -1);
            }

            var parts = result.Substring(8).Split(',');
            if (parts.Length < 2)
            {
                _log($"[INPUT] ✗ Impossible de parser");
                return (-1, -1);
            }

            if (int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
            {
                string tag = parts.Length > 2 ? parts[2] : "?";
                string selector = parts.Length > 3 ? parts[3] : "?";
                string ariaLabel = parts.Length > 4 ? parts[4] : "?";
                string inputType = parts.Length > 5 ? parts[5] : "contenteditable";

                _log($"[INPUT] ✓ Input trouvé: {tag}");
                _log($"[INPUT]   Sélecteur: {selector}");
                _log($"[INPUT]   Aria-label: {ariaLabel}");
                _log($"[INPUT]   Type: {inputType}");
                _log($"[INPUT]   Coordonnées: ({x}, {y})");

                return (x, y);
            }

            _log($"[INPUT] ✗ Erreur de conversion des coordonnées");
            return (-1, -1);
        }
    }
}