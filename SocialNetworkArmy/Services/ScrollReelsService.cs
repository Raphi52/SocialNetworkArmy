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
    public class ScrollReelsService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly ContentFilterService contentFilter;

        public ScrollReelsService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.contentFilter = new ContentFilterService(webView, logTextBox);
        }

        private static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<int> GetHumanWatchTime(Random rand)
        {
            // ✅ OPTIMIZED: Minimum 10s pour éduquer l'algo sur la niche "influenceuses"
            // 50% du temps : 10-15 secondes (bon engagement)
            // 30% du temps : 15-22 secondes (très bon engagement)
            // 15% du temps : 22-30 secondes (excellent engagement)
            // 5% du temps : 30-40 secondes (super engagé) + pause longue
            double dice = rand.NextDouble();

            if (dice < 0.50)
            {
                return rand.Next(10000, 15001); // 10-15s
            }
            else if (dice < 0.80)
            {
                return rand.Next(15000, 22001); // 15-22s
            }
            else if (dice < 0.95)
            {
                return rand.Next(22000, 30001); // 22-30s
            }
            else
            {
                return rand.Next(30000, 40001); // 30-40s
            }
        }

        private async Task<bool> ShouldTakeLongPause(Random rand)
        {
            // De temps en temps (5% de chance), on fait une longue pause
            return rand.NextDouble() < 0.05;
        }

        private async Task TakeLongPauseWithVideo(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[PAUSE] Pausing video for extended watch...\r\n");

                // Mettre la vidéo en pause
                await webView.ExecuteScriptAsync(@"
(function() {
  const videos = document.querySelectorAll('video');
  for(let v of videos) {
    const rect = v.getBoundingClientRect();
    const isVisible = rect.top < window.innerHeight && rect.bottom > 0;
    if (isVisible && !v.paused) {
      v.pause();
      return 'PAUSED';
    }
  }
  return 'NO_VIDEO_PAUSED';
})();");

                // Pause de 10 à 30 secondes
                int pauseDuration = rand.Next(10000, 30001);
                logTextBox.AppendText($"[PAUSE] Taking {pauseDuration / 1000}s break...\r\n");
                await Task.Delay(pauseDuration, token);

                // Reprendre la lecture
                await webView.ExecuteScriptAsync(@"
(function() {
  const videos = document.querySelectorAll('video');
  for(let v of videos) {
    const rect = v.getBoundingClientRect();
    const isVisible = rect.top < window.innerHeight && rect.bottom > 0;
    if (isVisible && v.paused) {
      v.play();
      return 'RESUMED';
    }
  }
  return 'NO_VIDEO_RESUMED';
})();");

                logTextBox.AppendText("[PAUSE] Resuming playback...\r\n");

                // Petit délai après reprise
                await Task.Delay(rand.Next(500, 1500), token);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[PAUSE ERROR] {ex.Message}\r\n");
                Logger.LogError($"TakeLongPauseWithVideo: {ex}");
            }
        }

        // ✅ NOUVELLE MÉTHODE : Scroll back
        private async Task<bool> ScrollBackToPreviousReelAsync(Random rand, CancellationToken token)
        {
            logTextBox.AppendText("[SCROLL_BACK] Going back to previous reel...\r\n");

            var scrollBackScript = @"
(function() {
  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }) || document.body;

  if (!scroller) return 'NO_SCROLLER_FOUND';

  const startY = scroller.scrollTop;
  const targetY = Math.max(0, startY - window.innerHeight);
  const duration = 800 + Math.random() * 400;
  const startTime = performance.now();

  function scrollStep(currentTime) {
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3);
    scroller.scrollTo(0, startY + (targetY - startY) * easeOut);
    if (progress < 1) {
      requestAnimationFrame(scrollStep);
    }
  }

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_BACK';
})();
";

            var result = await webView.ExecuteScriptAsync(scrollBackScript);
            logTextBox.AppendText($"[SCROLL_BACK] {result}\r\n");

            // Attente pour stabilisation
            await Task.Delay(rand.Next(2000, 3500), token);

            return result.Contains("SCROLLED_BACK");
        }

        private async Task<bool> ScrollToNextReelAsync(Random rand, CancellationToken token)
        {
            // Scroll progressif avec easing (ancienne méthode qui fonctionnait)
            var scrollScript = @"
(function() {
  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }) || document.body;

  if (!scroller) return 'NO_SCROLLER_FOUND';

  const startY = scroller.scrollTop;
  const targetY = startY + window.innerHeight;
  const duration = 800 + Math.random() * 400;
  const startTime = performance.now();

  function scrollStep(currentTime) {
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3);
    scroller.scrollTo(0, startY + (targetY - startY) * easeOut);
    if (progress < 1) {
      requestAnimationFrame(scrollStep);
    }
  }

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_TO_NEXT';
})();
";

            var result = await webView.ExecuteScriptAsync(scrollScript);
            logTextBox.AppendText($"[SCROLL] {result}\r\n");

            // Attente pour stabilisation
            await Task.Delay(rand.Next(2000, 4000), token);

            return true;
        }

        private async Task<string> WaitForNewReelAsync(string previousCreator, string creatorScript, Random rand, CancellationToken token, int maxAttempts = 5)
        {
            string newCreator = null;
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                await Task.Delay(rand.Next(500, 1000), token);

                // Vérifier le nouveau créateur
                newCreator = await webView.ExecuteScriptAsync(creatorScript);
                newCreator = newCreator?.Trim('"').Trim();

                // Vérifier aussi que la vidéo est en lecture
                var isPlayingScript = @"
(function() {
  const videos = document.querySelectorAll('video');
  for(let v of videos) {
    const rect = v.getBoundingClientRect();
    const isVisible = rect.top < window.innerHeight && rect.bottom > 0;
    if (isVisible && !v.paused) return 'PLAYING';
  }
  return 'NOT_PLAYING';
})();";

                var playStatus = await webView.ExecuteScriptAsync(isPlayingScript);

                // Succès si nouveau créateur ET vidéo en lecture
                if (!string.IsNullOrEmpty(newCreator) &&
                    newCreator != previousCreator &&
                    newCreator != "NO_VISIBLE_VIDEO" &&
                    newCreator != "NO_CREATOR" &&
                    playStatus?.Contains("PLAYING") == true)
                {
                    logTextBox.AppendText($"[REEL CHANGE] {previousCreator} → {newCreator}\r\n");
                    return newCreator;
                }

                attempts++;

                if (attempts < maxAttempts)
                {
                    logTextBox.AppendText($"[RETRY {attempts}] Waiting for reel change...\r\n");

                    // Stratégies alternatives: encore quelques coups de molette
                    if (attempts == 2 || attempts == 4)
                    {
                        // Quelques coups de molette supplémentaires
                        await webView.ExecuteScriptAsync($@"
(function() {{
  const scroller = document.querySelector('div[role=""main""]')?.querySelector('div[style*=""scroll""]') ||
                   document.querySelector('div[data-testid=""reels-tab""]') ||
                   document.body;
  for(let i=0; i<{rand.Next(2, 4)}; i++) {{
    scroller.scrollBy(0, {rand.Next(60, 120)});
  }}
  return 'EXTRA_SCROLLS';
}})();");
                    }
                    else if (attempts == 3)
                    {
                        // Essayer les touches clavier
                        await webView.ExecuteScriptAsync(@"
                          const event = new KeyboardEvent('keydown', { key: 'ArrowDown' });
                          document.dispatchEvent(event);
                        ");
                        await Task.Delay(100, token);
                        await webView.ExecuteScriptAsync(@"
                          document.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowDown' }));
                        ");
                    }
                }
            }

            logTextBox.AppendText($"[WARNING] Could not confirm reel change after {maxAttempts} attempts\r\n");
            return newCreator ?? previousCreator;
        }
        // ✅ NOUVELLE MÉTHODE : Petits scrolls erratiques pour humaniser
        private async Task RandomHumanScrollNoiseAsync(Random rand, CancellationToken token)
        {
            if (rand.NextDouble() < 0.25)
            {
                logTextBox.AppendText("[HUMAN NOISE] Adding random scroll movement...\r\n");

                var noiseScript = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }) || document.body;

  // 40% de chance de faire un petit scroll up
  if (Math.random() < 0.40) {
    const scrollUpAmount = -(Math.random() * 80 + 40); // -40 à -120px
    scroller.scrollBy({
      top: scrollUpAmount,
      behavior: 'smooth'
    });
    await sleep(600 + Math.random() * 400);
  } else {
    // Sinon petit scroll down/up aléatoire
    const randomScroll = Math.random() * 100 - 50; // -50 à +50px
    scroller.scrollBy({
      top: randomScroll,
      behavior: 'smooth'
    });
    await sleep(400);
  }

  return 'NOISE_ADDED';
})()";

                var noiseResult = await webView.ExecuteScriptAsync(noiseScript);
                logTextBox.AppendText($"[NOISE] {noiseResult}\r\n");
            }
        }
        public async Task RunAsync(CancellationToken token = default)
        {
            // Sécurité : s'assurer que CoreWebView2 est prêt même si on est appelé tôt
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Scroll");
                var localToken = form.GetCancellationToken();
                token = localToken;

                try
                {
                    Random rand = new Random();
                    logTextBox.AppendText($"[SCROLL] Starting continuous reels processing (until stopped by user).\r\n");

                    // Aller sur la page Reels
                    var targetUrl = $"https://www.instagram.com/reels/";
                    webView.CoreWebView2.Navigate(targetUrl);

                    // Attendre que la navigation se stabilise
                    await Task.Delay(rand.Next(4000, 7001), token);

                    // Initial scroll avec coups de molette pour charger le premier reel
                    var numInitialScrolls = rand.Next(3, 6);
                    await webView.ExecuteScriptAsync($@"
(async function(){{
  for(let i=0; i<{numInitialScrolls}; i++){{
    window.scrollBy(0, {rand.Next(100, 200)});
    await new Promise(r => setTimeout(r, {rand.Next(80, 180)}));
  }}
  return true;
}})()");

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
                    var lang = langResult?.Trim('"') ?? "en";
                    logTextBox.AppendText($"[LANG] Detected language: {lang}\r\n");

                    string likeContains;
                    string unlikeRegex;
                    string commentLabel;

                    if (lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                    {
                        likeContains = "aime";
                        unlikeRegex = "n'aime plus";
                        commentLabel = "Commenter";
                    }
                    else
                    {
                        likeContains = "ike";
                        unlikeRegex = "unlike";
                        commentLabel = "Comment";
                    }

                    // Prépare le fichier FutureTargets.txt
                    string dataDir = Path.Combine(Application.StartupPath, "data");
                    Directory.CreateDirectory(dataDir);
                    string targetFile = Path.Combine(dataDir, "FutureTargets.txt");

                    // BOUCLE INFINIE POUR REELS
                    string previousCreator = null;
                    int reelNum = 0;
                    int reelsSinceLastScrollBack = 0; // ✅ Compteur pour scroll back

                    while (!token.IsCancellationRequested)
                    {
                        reelNum++;
                        reelsSinceLastScrollBack++; // ✅ Incrémenter à chaque reel

                        try
                        {
                            logTextBox.AppendText($"[REEL {reelNum}] Début interaction...\r\n");

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
                            creatorName = creatorName?.Trim('"').Trim();
                            logTextBox.AppendText($"[CREATOR] {creatorName}\r\n");

                            // ✅ CONTENT FILTER: Check if content is female
                            bool isFemale = await contentFilter.IsCurrentContentFemaleAsync();
                            if (!isFemale)
                            {
                                logTextBox.AppendText($"[FILTER] ✗ Content filtered (not female) - SKIPPING\r\n");

                                // ⚡ Skip IMMEDIATELY (algo signal: not interested at all)
                                bool scrollSuccess = await ScrollToNextReelAsync(rand, token);

                                if (scrollSuccess)
                                {
                                    string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = newCreator;
                                }

                                continue; // Skip this reel, go to next iteration
                            }

                            logTextBox.AppendText($"[FILTER] ✓ Content passed filter (female detected)\r\n");

                            // Extract comment count
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

                            // Parse comments
                            string cleanCount = commentCount.Trim().Trim('"').ToLower();
                            int comments = 0;

                            if (cleanCount.EndsWith("k"))
                            {
                                string numericPart = new string(cleanCount.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
                                if (double.TryParse(numericPart, out double number))
                                {
                                    comments = (int)(number * 1000);
                                }
                            }
                            else
                            {
                                string digitsOnly = new string(cleanCount.Where(char.IsDigit).ToArray());
                                int.TryParse(digitsOnly, out comments);
                            }

                            if (comments > 300 && creatorName != "NO_CREATOR" && creatorName != "ERR" && creatorName != "NO_VISIBLE_VIDEO" && creatorName != "NO_PARENT3")
                            {
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

                            // Temps de visionnage humain: 80% -> 1-5s, 20% -> 10-15s
                            int watchTime = await GetHumanWatchTime(rand);
                            logTextBox.AppendText($"[WATCH] Watching for {watchTime / 1000}s...\r\n");
                            await Task.Delay(watchTime, token);

                            // Pause longue aléatoire (5% de chance)
                            if (await ShouldTakeLongPause(rand))
                            {
                                await TakeLongPauseWithVideo(rand, token);
                            }

                            // Like ~4% des reels
                            bool shouldLike = rand.NextDouble() < 0.04;
                            if (shouldLike)
                            {
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

  visibleBtn.click();

  const newAria = svg.getAttribute('aria-label') || '';
  return /{unlikeRegex}/i.test(newAria) ? 'OK:LIKED' : 'CLICKED';
}})();
";
                                var likeTry = await webView.ExecuteScriptAsync(likeScript);
                                logTextBox.AppendText($"[LIKE] {likeTry}\r\n");
                                await Task.Delay(rand.Next(1500, 3500), token);
                            }

                            // Random "distraction" pause
                            if (rand.NextDouble() < 0.2)
                            {
                                await Task.Delay(rand.Next(1000, 3000), token);
                            }
                            await RandomHumanScrollNoiseAsync(rand, token);

                            // ✅ DÉCISION : Scroll back ou scroll next
                            bool shouldScrollBack = false;

                            // Scroll back seulement si :
                            // 1. On a scrollé au moins 8 reels depuis le dernier scroll back
                            // 2. 8% de chance aléatoire
                            // 3. On n'est pas sur les 3 premiers reels
                            if (reelNum > 3 && reelsSinceLastScrollBack >= 8 && rand.NextDouble() < 0.08)
                            {
                                shouldScrollBack = true;
                                reelsSinceLastScrollBack = 0; // ✅ Reset le compteur
                            }

                            if (shouldScrollBack)
                            {
                                // ✅ SCROLL BACK
                                bool scrollBackSuccess = await ScrollBackToPreviousReelAsync(rand, token);

                                if (scrollBackSuccess)
                                {
                                    // Attendre et vérifier le changement de reel
                                    string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = newCreator;

                                    // Re-regarder ce reel brièvement (comportement humain)
                                    int reWatchTime = rand.Next(2000, 5000);
                                    logTextBox.AppendText($"[SCROLL_BACK] Re-watching previous reel for {reWatchTime / 1000}s...\r\n");
                                    await Task.Delay(reWatchTime, token);

                                    // Puis rescroller vers le bas (retour à la normale)
                                    logTextBox.AppendText("[SCROLL_BACK] Continuing forward...\r\n");
                                    await ScrollToNextReelAsync(rand, token);
                                    string forwardCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = forwardCreator;
                                }
                                else
                                {
                                    logTextBox.AppendText("[SCROLL_BACK] Failed, continuing normally\r\n");
                                }
                            }
                            else
                            {
                                // ✅ SCROLL NORMAL (vers le bas)
                                bool scrollSuccess = await ScrollToNextReelAsync(rand, token);

                                if (scrollSuccess)
                                {
                                    // Attendre et vérifier le changement de reel
                                    string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = newCreator;
                                }
                                else
                                {
                                    logTextBox.AppendText("[SCROLL] Failed, will retry next iteration\r\n");
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            logTextBox.AppendText("Script cancelled during reel loop.\r\n");
                            break;
                        }
                        catch (Exception ex)
                        {
                            logTextBox.AppendText($"[REEL EXCEPTION] {ex.Message}\r\n");
                            Logger.LogError($"ScrollService.RunAsync reel loop: {ex}");
                            await Task.Delay(rand.Next(2000, 5000), token);
                        }
                    }

                    logTextBox.AppendText("[FLOW] Continuous reels processing stopped (token triggered or exit).\r\n");
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