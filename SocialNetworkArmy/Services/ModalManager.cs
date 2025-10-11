using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    public class ModalManager
    {
        private readonly WebView2 _webView;
        private readonly Action<string> _log;
        private readonly JsExecutor _jsExecutor;

        public ModalManager(WebView2 webView, Action<string> log)
        {
            _webView = webView;
            _log = log;
            _jsExecutor = new JsExecutor(webView, log);
        }

        public async Task CloseAnyModalAsync(CancellationToken token)
        {
            const string js = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  const modals = document.querySelectorAll('div[role=""dialog""], [aria-modal=""true""]');
  if (modals.length === 0) return 'no_modal';
  
  let closed = 0;
  
  for (const modal of modals) {
    let closeBtn = modal.querySelector('button[aria-label*=""Close"" i], button[aria-label*=""Fermer"" i], svg[aria-label*=""Close"" i], svg[aria-label*=""Fermer"" i]');
    if (closeBtn && closeBtn.tagName === 'svg') {
      closeBtn = closeBtn.closest('button, [role=""button""]');
    }
    
    if (!closeBtn) {
      const buttons = [...modal.querySelectorAll('button, [role=""button""]')];
      closeBtn = buttons.find(b => {
        const rect = b.getBoundingClientRect();
        const modalRect = modal.getBoundingClientRect();
        return rect.top < modalRect.top + 100 && rect.right > modalRect.right - 100;
      });
    }
    
    if (closeBtn) {
      const rect = closeBtn.getBoundingClientRect();
      const opts = {
        bubbles: true,
        cancelable: true,
        clientX: rect.left + rect.width / 2,
        clientY: rect.top + rect.height / 2,
        button: 0
      };
      
      ['mouseenter', 'mouseover', 'mousedown', 'mouseup', 'click'].forEach(type => {
        closeBtn.dispatchEvent(new MouseEvent(type, opts));
      });
      
      closed++;
      await sleep(300);
    }
  }
  
  if (closed === 0) {
    document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', keyCode: 27, bubbles: true}));
    await sleep(300);
  }
  
  await sleep(500);
  const remaining = document.querySelectorAll('div[role=""dialog""], [aria-modal=""true""]').length;
  return remaining === 0 ? 'all_closed' : 'closed_' + closed + '_remaining_' + remaining;
})()";

            for (int i = 0; i < 3; i++)
            {
                var result = await _jsExecutor.ExecJsAsync(js, token);
                _log($"[CLOSE_MODAL] Tentative {i + 1}/3: {result}");

                if (result == "all_closed" || result == "no_modal")
                {
                    return;
                }

                await Task.Delay(500, token);
            }

            _log("[CLOSE_MODAL] ⚠ Certaines modales peuvent rester ouvertes");
        }
    }
}