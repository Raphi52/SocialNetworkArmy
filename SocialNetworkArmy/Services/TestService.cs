using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
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
                    logTextBox.AppendText("[TEST] === NEXT BUTTON CLICK TEST ===\r\n\r\n");

                    // PHASE 1: Analyser la structure du dialog
                    logTextBox.AppendText("[PHASE 1] Analysing reel modal structure...\r\n");
                    var analysisScript = @"
(function(){
  try {
    var info = [];
    
    var dialog = document.querySelector('div[role=""dialog""]');
    if (!dialog) {
      return 'NO_DIALOG_FOUND';
    }
    
    info.push('✓ Dialog found');
    
    // Chercher tous les boutons dans le dialog
    var buttons = dialog.querySelectorAll('button');
    info.push('Total buttons in dialog: ' + buttons.length);
    
    // Analyser chaque bouton
    for (var i = 0; i < buttons.length; i++) {
      var btn = buttons[i];
      var rect = btn.getBoundingClientRect();
      var ariaLabel = btn.getAttribute('aria-label') || 'NO_LABEL';
      var visible = btn.offsetWidth > 0 && btn.offsetHeight > 0;
      var hasSvg = !!btn.querySelector('svg');
      var text = (btn.innerText || btn.textContent || '').trim().substring(0, 30);
      var disabled = btn.disabled || (btn.getAttribute('aria-disabled') === 'true');
      
      info.push('\n[BTN ' + i + ']');
      info.push('  aria-label: ' + ariaLabel);
      info.push('  size: ' + Math.round(rect.width) + 'x' + Math.round(rect.height));
      info.push('  visible: ' + visible);
      info.push('  hasSVG: ' + hasSvg);
      info.push('  disabled: ' + disabled);
      info.push('  text: ' + text);
      info.push('  position: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    }
    
    return info.join('\n');
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})()";

                    var analysis = await webView.ExecuteScriptAsync(analysisScript);
                    var cleanAnalysis = analysis?.Trim('"').Replace("\\n", "\r\n");
                    logTextBox.AppendText(cleanAnalysis + "\r\n\r\n");

                    // PHASE 2: Tester le click sur le 2e bouton (next)
                    logTextBox.AppendText("[PHASE 2] Testing next button click...\r\n");
                    var clickTestScript = @"
(async function(){
  try {
    var info = [];
    
    var dialog = document.querySelector('div[role=""dialog""]');
    if (!dialog) return 'NO_DIALOG';
    
    // Stratégie 1: Chercher par aria-label
    var nextBtn = Array.from(dialog.querySelectorAll('button')).find(b => {
      var label = (b.getAttribute('aria-label') || '').toLowerCase();
      return /next|suivant|nextpage|nextitem|flèche/.test(label);
    });
    info.push('Stratégie 1 (aria-label): ' + (nextBtn ? 'FOUND' : 'NOT_FOUND'));
    
    // Stratégie 2: Boutons de taille 32x32
    if (!nextBtn) {
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0 && Math.round(rect.width) === 32 && Math.round(rect.height) === 32;
      });
      if (allBtns.length >= 1) {
        // Trier par position x pour prendre le plus à droite (next)
        allBtns.sort((a, b) => a.getBoundingClientRect().left - b.getBoundingClientRect().left);
        nextBtn = allBtns[allBtns.length - 1]; // Prendre le dernier (plus à droite)
        info.push('Stratégie 2 (32x32 buttons): FOUND ' + allBtns.length + ', picked rightmost');
      } else {
        info.push('Stratégie 2 (32x32 buttons): NOT_FOUND');
      }
    }
    
    // Stratégie 3: 2e bouton visible si toujours pas trouvé
    if (!nextBtn) {
      var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {
        var rect = b.getBoundingClientRect();
        return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0;
      });
      if (allBtns.length >= 2) {
        nextBtn = allBtns[1];
        info.push('Stratégie 3 (2nd button): FOUND');
      } else {
        info.push('Stratégie 3 (2nd button): NOT_FOUND (only ' + allBtns.length + ' visible buttons)');
      }
    }
    
    if (!nextBtn) {
      info.push('RESULT: NO_NEXT_BUTTON');
      return info.join('\n');
    }
    
    var rect = nextBtn.getBoundingClientRect();
    info.push('\nButton details:');
    info.push('  Size: ' + Math.round(rect.width) + 'x' + Math.round(rect.height));
    info.push('  Position: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    info.push('  Visible: ' + (rect.width > 0 && rect.height > 0));
    
    // Scroll into view
    nextBtn.scrollIntoView({block: 'nearest', inline: 'nearest'});
    await new Promise(r => setTimeout(r, 100));
    
    // Recalculer après scroll
    rect = nextBtn.getBoundingClientRect();
    info.push('  After scroll: ' + Math.round(rect.left) + ',' + Math.round(rect.top));
    
    // Calculer les coordonnées du click
    var centerX = rect.left + rect.width / 2;
    var centerY = rect.top + rect.height / 2;
    var offsetX = (Math.random() - 0.5) * 10;
    var offsetY = (Math.random() - 0.5) * 10;
    var clientX = Math.floor(centerX + offsetX);
    var clientY = Math.floor(centerY + offsetY);
    
    // Clamp dans les limites
    clientX = Math.max(rect.left + 2, Math.min(clientX, rect.left + rect.width - 2));
    clientY = Math.max(rect.top + 2, Math.min(clientY, rect.top + rect.height - 2));
    
    info.push('\nClick coordinates:');
    info.push('  Center: ' + Math.round(centerX) + ',' + Math.round(centerY));
    info.push('  Final: ' + clientX + ',' + clientY);
    
    // Mouvement souris simulé
    var startX = clientX + (Math.random() * 60 - 30);
    var startY = clientY + (Math.random() * 60 - 30);
    
    for (let i = 1; i <= 4; i++) {
      var moveX = startX + (clientX - startX) * (i / 4);
      var moveY = startY + (clientY - startY) * (i / 4);
      nextBtn.dispatchEvent(new MouseEvent('mousemove', {bubbles: true, clientX: moveX, clientY: moveY}));
      await new Promise(r => setTimeout(r, Math.random() * 30 + 20));
    }
    
    await new Promise(r => setTimeout(r, Math.random() * 100 + 50));
    
    // Events
    var opts = {bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0};
    nextBtn.dispatchEvent(new MouseEvent('mouseenter', opts));
    nextBtn.dispatchEvent(new MouseEvent('mouseover', opts));
    nextBtn.dispatchEvent(new MouseEvent('mousedown', opts));
    await new Promise(r => setTimeout(r, Math.random() * 80 + 30));
    nextBtn.dispatchEvent(new MouseEvent('mouseup', opts));
    nextBtn.dispatchEvent(new MouseEvent('click', opts));
    await new Promise(r => setTimeout(r, Math.random() * 50 + 25));
    nextBtn.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    info.push('\nCLICK_SENT');
    return info.join('\n');
  } catch(e) {
    return 'EXCEPTION: ' + e.message;
  }
})()";

                    var clickResult = await webView.ExecuteScriptAsync(clickTestScript);
                    var cleanClickResult = clickResult?.Trim('"').Replace("\\n", "\r\n");
                    logTextBox.AppendText(cleanClickResult + "\r\n\r\n");

                    // PHASE 3: Vérifier si le reel a changé
                    logTextBox.AppendText("[PHASE 3] Waiting 3s and checking if reel changed...\r\n");
                    await Task.Delay(3000, token);

                    var verifyScript = @"
(function(){
  var info = [];
  
  // Récupérer l'ID du reel actuel
  var match = window.location.href.match(/\/reel\/([^\/]+)/);
  var reelId = match ? match[1] : 'NO_ID';
  info.push('Current reel ID: ' + reelId);
  
  // Vérifier si le dialog est toujours ouvert
  var hasDialog = !!document.querySelector('div[role=""dialog""]');
  info.push('Dialog still open: ' + hasDialog);
  
  // Vérifier s'il y a une vidéo
  var videos = document.querySelectorAll('video');
  info.push('Video elements: ' + videos.length);
  
  return info.join('\n');
})()";

                    var verifyResult = await webView.ExecuteScriptAsync(verifyScript);
                    var cleanVerifyResult = verifyResult?.Trim('"').Replace("\\n", "\r\n");
                    logTextBox.AppendText(cleanVerifyResult + "\r\n\r\n");

                    logTextBox.AppendText("[TEST] === TEST COMPLETED ===\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("\r\n[CANCELLED] Test stopped by user.\r\n");
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