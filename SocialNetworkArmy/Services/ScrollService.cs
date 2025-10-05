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
    public class ScrollService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;

        public ScrollService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
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
            // Sécurité : s’assurer que CoreWebView2 est prêt même si on est appelé tôt
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Scroll");
                var localToken = form.GetCancellationToken(); // Récupérer le token depuis le form
                token = localToken; // Utiliser ce token pour la cancellation

                try
                {
                    // 2) (Optionnel) ouvrir DevTools
                    try { webView.CoreWebView2?.OpenDevToolsWindow(); } catch { /* ignore */ }

                    Random rand = new Random();
                    int maxReels = rand.Next(50, 101); // Random between 50 and 100 inclusive
                    logTextBox.AppendText($"[SCROLL] Will process {maxReels} reels.\r\n");

                    // 3) Aller sur la page Reels
                    var targetUrl = $"https://www.instagram.com/reels/";
                    webView.CoreWebView2.Navigate(targetUrl);

                    // 4) Attendre un peu que la navigation se stabilise
                    await Task.Delay(rand.Next(4000, 7001), token);

                    // Initial human-like scroll to load first reel
                    await webView.ExecuteScriptAsync(@"
(async function(){
  const numScrolls = " + rand.Next(2, 4) + @";
  for(let i=0; i<numScrolls; i++){
    const scrollAmt = Math.random() * (window.innerHeight * 0.6) + (window.innerHeight * 0.2);
    window.scrollBy(0, scrollAmt);
    await new Promise(r => setTimeout(r, " + rand.Next(1200, 2501) + @"));
  }
  return true;
})()");
                    await Task.Delay(rand.Next(2500, 4501), token);

                    // Prépare le fichier FutureTargets.txt
                    string dataDir = Path.Combine(Application.StartupPath, "data"); // Assure-toi que c'est le bon chemin
                    Directory.CreateDirectory(dataDir);
                    string targetFile = Path.Combine(dataDir, "FutureTargets.txt");

                    // ======================= BOUCLE POUR REELS (HUMAN SCROLL + WATCH + LIKE + LOG CREATOR/COMMENTS) =======================
                    for (int reelNum = 1; reelNum <= maxReels; reelNum++)
                    {
                        token.ThrowIfCancellationRequested();

                        logTextBox.AppendText($"[REEL {reelNum}/{maxReels}] Début interaction...\r\n");

                        // Extract creator name from first/visible Reel
                        var creatorScript = @"
(function(){
  try {
    var video = document.querySelector('video');
    if (!video) return 'NO_VIDEO';

    var parent3 = video.parentElement.parentElement.parentElement;
    if (!parent3) return 'NO_PARENT3';

    var creatorSpan = parent3.querySelector('span[dir=""auto""]');
    if (creatorSpan && creatorSpan.textContent.trim()) {
      return creatorSpan.textContent.trim();
    }
    return 'NO_CREATOR';
  } catch(e) {
    return 'ERR';
  }
})();
";
                        var creatorName = await webView.ExecuteScriptAsync(creatorScript);
                        logTextBox.AppendText($"[CREATOR] {creatorName}\r\n");

                        // Extract comment count from visible Reel using the working getCurrentComments
                        var commentScript = @"
function getCurrentComments() {
  const commentBtns = document.querySelectorAll('[role=""button""]:has(svg[aria-label=""Commenter""])');
  let mostVisible = null;
  let maxVisible = 0;
  commentBtns.forEach(btn => {
    const rect = btn.getBoundingClientRect();
    const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
    if (visible > maxVisible) {
      maxVisible = visible;
      mostVisible = btn;
    }
  });
  const span = mostVisible?.querySelector('span.html-span');
  return span?.textContent.trim() || '0';
}
getCurrentComments();
";
                        var commentCount = await webView.ExecuteScriptAsync(commentScript);
                        logTextBox.AppendText($"[COMMENTS] {commentCount}\r\n");

                        // Parse comments pour check >300
                        int comments = 0;
                        if (int.TryParse(commentCount.Trim().Trim('"'), out int c)) comments = c;
                        if (comments > 300 && creatorName != "NO_CREATOR" && creatorName != "ERR" && creatorName != "NO_VIDEO" && creatorName != "NO_PARENT3")
                        {
                            // Check si déjà dans le fichier
                            bool alreadyExists = false;
                            if (File.Exists(targetFile))
                            {
                                var lines = File.ReadAllLines(targetFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                                alreadyExists = lines.Contains(creatorName.Trim());
                            }
                            if (!alreadyExists)
                            {
                                File.AppendAllText(targetFile, creatorName.Trim() + "\r\n");
                                logTextBox.AppendText($"[TARGET] Added {creatorName} to FutureTargets.txt (comments: {comments})\r\n");
                            }
                            else
                            {
                                logTextBox.AppendText($"[TARGET] {creatorName} already in FutureTargets.txt (comments: {comments})\r\n");
                            }
                        }

                        // Human-like watch time: 5-15s
                        int watchTime = rand.Next(5000, 15001);
                        await Task.Delay(watchTime, token);

                        // Like seulement 9% des reels
                        bool shouldLike = rand.NextDouble() < 0.09;
                        if (shouldLike)
                        {
                            // LIKE on visible Reel
                            var likeTry = await webView.ExecuteScriptAsync(@"
(function(){
  const likeSVGs = document.querySelectorAll('svg[aria-label*=""aime""]');
  let visibleBtn = null;
  likeSVGs.forEach(svg => {
    const btn = svg.closest('div[role=""button""]');
    const rect = btn ? btn.getBoundingClientRect() : null;
    const isVisible = rect && rect.top < window.innerHeight && rect.bottom > 0;
    if (isVisible) {
      visibleBtn = btn;
    }
  });
  if (!visibleBtn) return 'NO_VISIBLE_LIKE';

  const svg = visibleBtn.querySelector('svg');
  const aria = svg.getAttribute('aria-label') || '';
  if (/n’aime plus/i.test(aria)) return 'ALREADY_LIKED';

  // Simule click humain (comme le working JS)
  visibleBtn.click();

  // Vérif simple post-click (sync, approx)
  const newAria = svg.getAttribute('aria-label') || '';
  return /n’aime plus/i.test(newAria) ? 'OK:LIKED' : 'CLICKED';
})();
");
                            logTextBox.AppendText($"[LIKE] {likeTry}\r\n");

                            // Post-like pause
                            await Task.Delay(rand.Next(1500, 3500), token);
                        }

                        // Random "distraction" pause
                        if (rand.NextDouble() < 0.2)
                        {
                            await Task.Delay(rand.Next(1000, 3000), token);
                        }

                        // Scroll to next reel if not last
                        if (reelNum < maxReels)
                        {
                            await webView.ExecuteScriptAsync(@"
(function() {
  const scroller = Array.from(document.querySelectorAll('div')).find(div => {
    const style = window.getComputedStyle(div);
    return style.overflowY === 'scroll' || style.overflowY === 'auto';
  }) || window;
  const startY = scroller.pageYOffset || scroller.scrollTop;
  const targetY = startY + window.innerHeight;
  const duration = 800;
  const startTime = performance.now();

  function scrollStep(currentTime) {
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3);

    if (scroller.scrollTo) scroller.scrollTo(0, startY + (targetY - startY) * easeOut);

    if (progress < 1) {
      requestAnimationFrame(scrollStep);
    }
  }

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_TO_NEXT';
})();
");
                            await Task.Delay(rand.Next(2000, 4001), token); // Stabilize for next
                        }
                    }

                    logTextBox.AppendText("[FLOW] Tous les reels traités.\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("Script annulé par l'utilisateur.\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                    Logger.LogError($"ScrollService.RunAsync/inner: {ex}");
                }
                finally
                {
                    form.ScriptCompleted();
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                Logger.LogError($"ScrollService.RunAsync: {ex}");
            }
        }
    }
}