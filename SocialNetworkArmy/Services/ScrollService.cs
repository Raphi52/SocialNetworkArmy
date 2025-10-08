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

                    // Check for reels feed to load before starting the loop
                    bool isLoaded = false;
                    int loadRetries = 0;
                    while (!isLoaded && loadRetries < 5)
                    {
                        var loadCheck = await webView.ExecuteScriptAsync("document.querySelectorAll('video').length > 0 ? 'true' : 'false';");
                        isLoaded = JsBoolIsTrue(loadCheck);
                        if (!isLoaded)
                        {
                            await Task.Delay(2000, token);
                            loadRetries++;
                        }
                    }
                    if (!isLoaded)
                    {
                        throw new Exception("Reels feed failed to load.");
                    }

                    // Detect language
                    var langResult = await webView.ExecuteScriptAsync("document.documentElement.lang;");
                    var lang = langResult?.Trim('"') ?? "en"; // Default to English if null
                    logTextBox.AppendText($"[LANG] Detected language: {lang}\r\n");

                    string likeContains;
                    string unlikeRegex;
                    string commentLabel;

                    if (lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                    {
                        likeContains = "aime";
                        unlikeRegex = "n’aime plus";
                        commentLabel = "Commenter";
                    }
                    else
                    {
                        likeContains = "ike"; // For "Like" or "Unlike"
                        unlikeRegex = "unlike";
                        commentLabel = "Comment";
                    }

                    // Prépare le fichier FutureTargets.txt
                    string dataDir = Path.Combine(Application.StartupPath, "data"); // Assure-toi que c'est le bon chemin
                    Directory.CreateDirectory(dataDir);
                    string targetFile = Path.Combine(dataDir, "FutureTargets.txt");

                    // ======================= BOUCLE POUR REELS (HUMAN SCROLL + WATCH + LIKE + LOG CREATOR/COMMENTS) =======================
                    string previousCreator = null;
                    for (int reelNum = 1; reelNum <= maxReels; reelNum++)
                    {
                        token.ThrowIfCancellationRequested();

                        logTextBox.AppendText($"[REEL {reelNum}/{maxReels}] Début interaction...\r\n");

                        // Extract creator name from visible Reel
                        var creatorScript = @"
(function(){
  try {
    const videos = document.querySelectorAll('video');
    let mostVisibleVideo = null;
    let maxVisible = 0;
    videos.forEach(video => {
      const rect = video.getBoundingClientRect();
      const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visible > maxVisible) {
        maxVisible = visible;
        mostVisibleVideo = video;
      }
    });
    if (!mostVisibleVideo) return 'NO_VISIBLE_VIDEO';

    var parent3 = mostVisibleVideo.parentElement.parentElement.parentElement;
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
                        creatorName = creatorName?.Trim('"').Trim(); // Clean up
                        logTextBox.AppendText($"[CREATOR] {creatorName}\r\n");

                        // Extract comment count from visible Reel using the working getCurrentComments
                        var commentScript = $@"
function getCurrentComments() {{
  const commentBtns = document.querySelectorAll('[role=""button""]:has(svg[aria-label=""{commentLabel}""])');
  let mostVisible = null;
  let maxVisible = 0;
  commentBtns.forEach(btn => {{
    const rect = btn.getBoundingClientRect();
    const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
    if (visible > maxVisible) {{
      maxVisible = visible;
      mostVisible = btn;
    }}
  }});
  const span = mostVisible?.querySelector('span.html-span');
  return span?.textContent.trim() || '0';
}}
getCurrentComments();
";
                        var commentCount = await webView.ExecuteScriptAsync(commentScript);
                        logTextBox.AppendText($"[COMMENTS] {commentCount}\r\n");

                        // Parse comments to handle K (thousands) and check >300
                        string cleanCount = commentCount.Trim().Trim('"').ToLower(); // Normalize input
                        int comments = 0;

                        if (cleanCount.EndsWith("k"))
                        {
                            // Remove "K" and parse the numeric part
                            string numericPart = new string(cleanCount.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
                            if (double.TryParse(numericPart, out double number))
                            {
                                comments = (int)(number * 1000); // Convert to thousands
                            }
                        }
                        else
                        {
                            // Original logic for plain numbers
                            string digitsOnly = new string(cleanCount.Where(char.IsDigit).ToArray());
                            int.TryParse(digitsOnly, out comments);
                        }

                        if (comments > 300 && creatorName != "NO_CREATOR" && creatorName != "ERR" && creatorName != "NO_VISIBLE_VIDEO" && creatorName != "NO_PARENT3")
                        {
                            // Check if already in file
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
                            var likeScript = $@"
(function(){{
  const likeSVGs = document.querySelectorAll('svg[aria-label*=""{likeContains}""]');
  let visibleBtn = null;
  likeSVGs.forEach(svg => {{
    const btn = svg.closest('div[role=""button""]');
    const rect = btn ? btn.getBoundingClientRect() : null;
    const isVisible = rect && rect.top < window.innerHeight && rect.bottom > 0;
    if (isVisible) {{
      visibleBtn = btn;
    }}
  }});
  if (!visibleBtn) return 'NO_VISIBLE_LIKE';

  const svg = visibleBtn.querySelector('svg');
  const aria = svg.getAttribute('aria-label') || '';
  if (/{unlikeRegex}/i.test(aria)) return 'ALREADY_LIKED';

  // Simule click humain (comme le working JS)
  visibleBtn.click();

  // Vérif simple post-click (sync, approx)
  const newAria = svg.getAttribute('aria-label') || '';
  return /{unlikeRegex}/i.test(newAria) ? 'OK:LIKED' : 'CLICKED';
}})();
";
                            var likeTry = await webView.ExecuteScriptAsync(likeScript);
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
                            var scrollResult = await webView.ExecuteScriptAsync(@"
(function() {
  // More precise scroller detection: look for the main reels container
  let scroller = document.querySelector('div[role=""main""] > div') || // Common reels wrapper
                 document.querySelector('div[data-testid=""reels-tab""]') || // Reels tab container
                 Array.from(document.querySelectorAll('div')).find(div => {
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8; // Filter for tall scrollers
                 }) || document.body; // Fallback to body instead of window for better control

  if (!scroller) return 'NO_SCROLLER_FOUND';

  const startY = scroller.scrollTop;
  const targetY = startY + window.innerHeight; // Assume full viewport snap
  const duration = 800 + Math.random() * 400; // Vary duration for human-like feel (800-1200ms)
  const startTime = performance.now();

  function scrollStep(currentTime) {
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3); // Cubic ease-out for smooth snap

    scroller.scrollTo(0, startY + (targetY - startY) * easeOut);

    if (progress < 1) {
      requestAnimationFrame(scrollStep);
    }
  }

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_TO_NEXT';
})();
");
                            logTextBox.AppendText($"[SCROLL] Result: {scrollResult}\r\n");
                            await Task.Delay(rand.Next(2000, 4001), token); // Stabilize

                            // Post-scroll verification
                            int retryCount = 0;
                            const int maxRetries = 3;
                            string newCreator = null;
                            while (retryCount < maxRetries)
                            {
                                await Task.Delay(rand.Next(1500, 3000), token); // Wait for load/snap
                                newCreator = await webView.ExecuteScriptAsync(creatorScript);
                                newCreator = newCreator?.Trim('"').Trim();

                                if (newCreator != previousCreator && newCreator != "NO_VISIBLE_VIDEO" && newCreator != "NO_CREATOR")
                                {
                                    break; // Successfully advanced
                                }

                                logTextBox.AppendText($"[SCROLL RETRY {retryCount + 1}] Stuck on {previousCreator}, retrying...\r\n");

                                // Retry scroll with slight variation (e.g., overscroll by 10-50px)
                                await webView.ExecuteScriptAsync($@"
(function() {{
  const scroller = document.querySelector('div[role=""main""] > div') || 
                 document.querySelector('div[data-testid=""reels-tab""]') || 
                 Array.from(document.querySelectorAll('div')).find(div => {{
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }}) || document.body;
  scroller.scrollBy(0, {rand.Next(10, 51)});
  return 'NUDGE_SCROLL';
}})();");
                                retryCount++;
                            }

                            if (retryCount >= maxRetries)
                            {
                                logTextBox.AppendText($"[SCROLL ERROR] Max retries reached, still stuck on {previousCreator}. Skipping.\r\n");
                                // Optional: Break loop or handle error
                            }

                            previousCreator = newCreator; // Update for next iteration
                        }
                        else
                        {
                            previousCreator = creatorName; // For first reel
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