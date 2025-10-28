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
        private readonly LanguageDetectionService languageDetector;
        private readonly Models.AccountConfig config;

        // ✅ Cache for recent detection results (avoid re-detecting on scrollBack)
        private class ReelDetectionCache
        {
            public bool IsFemale { get; set; }
            public string Language { get; set; }
            public bool PassedFilters { get; set; }
            public DateTime Timestamp { get; set; }
        }
        private readonly System.Collections.Generic.Dictionary<string, ReelDetectionCache> recentDetections
            = new System.Collections.Generic.Dictionary<string, ReelDetectionCache>();
        private static readonly TimeSpan DETECTION_CACHE_DURATION = TimeSpan.FromMinutes(5);

        public ScrollReelsService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.contentFilter = new ContentFilterService(webView, logTextBox);
            this.languageDetector = new LanguageDetectionService(logTextBox);
            this.config = ConfigService.LoadConfig(profile.Name);
        }

        /// <summary>
        /// Safe logging that checks if logTextBox is disposed before writing
        /// Fixes: "cannot access disposed object" exception on form close
        /// </summary>
        private void SafeLog(string message)
        {
            try
            {
                if (logTextBox == null || logTextBox.IsDisposed || logTextBox.Disposing)
                    return;

                if (logTextBox.InvokeRequired)
                {
                    logTextBox.BeginInvoke(new Action(() =>
                    {
                        if (!logTextBox.IsDisposed && !logTextBox.Disposing)
                        {
                            logTextBox.AppendText(message + "\r\n");
                            logTextBox.SelectionStart = logTextBox.TextLength;
                            logTextBox.ScrollToCaret();
                        }
                    }));
                }
                else
                {
                    logTextBox.AppendText(message + "\r\n");
                    logTextBox.SelectionStart = logTextBox.TextLength;
                    logTextBox.ScrollToCaret();
                }
            }
            catch (ObjectDisposedException)
            {
                // Form closed while logging - ignore silently
            }
            catch { }
        }

        private static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> ExtractReelCaptionAsync(CancellationToken token)
        {
            var captionScript = @"
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

    if (!mostVisibleVideo) return '';

    // Navigate up to find the container with caption
    let container = mostVisibleVideo.parentElement;
    for (let i = 0; i < 5 && container; i++) {
      container = container.parentElement;
    }

    if (!container) return '';

    // Try to find caption text
    // Strategy 1: Look for h1
    const h1 = container.querySelector('h1');
    if (h1 && h1.textContent && h1.textContent.trim().length > 0) {
      return h1.textContent.trim();
    }

    // Strategy 2: Look for spans with caption-like content
    const spans = container.querySelectorAll('span');
    for (let span of spans) {
      const text = span.textContent || '';
      if (text.length > 20 && !text.includes('ago') && !text.includes('il y a') && !text.includes('comment')) {
        return text.trim();
      }
    }

    return '';
  } catch(e) {
    return '';
  }
})();";

            try
            {
                var result = await webView.ExecuteScriptAsync(captionScript);
                return result?.Trim('"').Trim() ?? "";
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[CAPTION ERROR] {ex.Message}\r\n");
                return "";
            }
        }

        private async Task<int> GetHumanWatchTime(Random rand)
        {
            // ✅ OPTIMIZED FOR SPEED: Shorter watch times, bonus will extend for perfect matches
            // 60% du temps : 5-8 secondes (engagement rapide)
            // 25% du temps : 8-12 secondes (bon engagement)
            // 10% du temps : 12-18 secondes (très bon engagement)
            // 5% du temps : 18-25 secondes (excellent engagement)
            double dice = rand.NextDouble();

            if (dice < 0.60)
            {
                return rand.Next(5000, 8001); // 5-8s (fast scroll, algo still learns)
            }
            else if (dice < 0.85)
            {
                return rand.Next(8000, 12001); // 8-12s (good engagement)
            }
            else if (dice < 0.95)
            {
                return rand.Next(12000, 18001); // 12-18s (very good)
            }
            else
            {
                return rand.Next(18000, 25001); // 18-25s (excellent)
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

            // ✅ AMÉLIORATION: Légère dérive horizontale pour scroll plus humain
            int horizontalDrift = rand.Next(-8, 9);

            var scrollBackScript = $@"
(function() {{
  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {{
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }}) || document.body;

  if (!scroller) return 'NO_SCROLLER_FOUND';

  const startY = scroller.scrollTop;
  const startX = scroller.scrollLeft || 0;
  const targetY = Math.max(0, startY - window.innerHeight);
  const duration = 800 + Math.random() * 400;
  const horizontalDrift = {horizontalDrift};
  const startTime = performance.now();

  function scrollStep(currentTime) {{
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3);

    // ✅ Calcul Y avec easing + X avec légère oscillation humaine
    const y = startY + (targetY - startY) * easeOut;
    const x = startX + horizontalDrift * Math.sin(progress * Math.PI);

    scroller.scrollTo(x, y);

    if (progress < 1) {{
      requestAnimationFrame(scrollStep);
    }}
  }}

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_BACK';
}})();
";

            var result = await webView.ExecuteScriptAsync(scrollBackScript);
            logTextBox.AppendText($"[SCROLL_BACK] {result}\r\n");

            // Attente pour stabilisation
            await Task.Delay(rand.Next(2000, 3500), token);

            return result.Contains("SCROLLED_BACK");
        }

        private async Task<bool> ScrollToNextReelAsync(Random rand, CancellationToken token)
        {
            // ✅ AMÉLIORATION: Variations de vitesse de scroll (humain)
            double speedProfile = rand.NextDouble();
            int duration;

            if (speedProfile < 0.15) // 15% : Scroll rapide (impatient)
            {
                duration = 400 + rand.Next(0, 200);
                logTextBox.AppendText("[SCROLL] Fast scroll (impatient)\r\n");
            }
            else if (speedProfile < 0.75) // 60% : Scroll normal
            {
                duration = 800 + rand.Next(0, 400);
            }
            else // 25% : Scroll lent (hésitant)
            {
                duration = 1200 + rand.Next(0, 600);
                logTextBox.AppendText("[SCROLL] Slow scroll (hesitant)\r\n");
            }

            // ✅ AMÉLIORATION: Légère dérive horizontale pour scroll plus humain
            int horizontalDrift = rand.Next(-8, 9); // ±8px de drift horizontal

            var scrollScript = $@"
(function() {{
  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {{
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }}) || document.body;

  if (!scroller) return 'NO_SCROLLER_FOUND';

  const startY = scroller.scrollTop;
  const startX = scroller.scrollLeft || 0;
  const targetY = startY + window.innerHeight;
  const duration = {duration};
  const horizontalDrift = {horizontalDrift};
  const startTime = performance.now();

  function scrollStep(currentTime) {{
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeOut = 1 - Math.pow(1 - progress, 3);

    // ✅ Calcul Y avec easing + X avec légère oscillation humaine
    const y = startY + (targetY - startY) * easeOut;
    const x = startX + horizontalDrift * Math.sin(progress * Math.PI); // Oscillation sinusoïdale

    scroller.scrollTo(x, y);

    if (progress < 1) {{
      requestAnimationFrame(scrollStep);
    }}
  }}

  requestAnimationFrame(scrollStep);
  return 'SCROLLED_TO_NEXT';
}})();
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
                // ✅ OPTIMIZED: Faster reel change detection (200-400ms vs 500-1000ms)
                await Task.Delay(rand.Next(200, 400), token);

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

        // ✅ AMÉLIORATION: Réécoute partielle du reel
        private async Task ReplayReelAsync(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[REPLAY] Replaying reel from start...\r\n");

                await webView.ExecuteScriptAsync(@"
(function() {
  const videos = document.querySelectorAll('video');
  for(let v of videos) {
    const rect = v.getBoundingClientRect();
    if (rect.top < window.innerHeight && rect.bottom > 0) {
      v.currentTime = 0;
      return 'REPLAYED';
    }
  }
  return 'NO_VIDEO';
})();");

                int replayDuration = rand.Next(3000, 8000);
                logTextBox.AppendText($"[REPLAY] Re-watching for {replayDuration / 1000}s...\r\n");
                await Task.Delay(replayDuration, token);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[REPLAY ERROR] {ex.Message}\r\n");
            }
        }

        // ✅ AMÉLIORATION: Consultation du profil créateur
        private async Task VisitCreatorProfileAsync(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[PROFILE] Visiting creator profile...\r\n");

                var clickProfileScript = @"
(function(){
  try {
    const videos = document.querySelectorAll('video');
    let mostVisible = null;
    let maxVisible = 0;

    videos.forEach(video => {
      const rect = video.getBoundingClientRect();
      const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visible > maxVisible) {
        maxVisible = visible;
        mostVisible = video;
      }
    });

    if (!mostVisible) return 'NO_VIDEO';

    const parent = mostVisible.parentElement?.parentElement?.parentElement;
    if (!parent) return 'NO_PARENT';

    const creatorLink = parent.querySelector('a[role=""link""]');

    if (creatorLink) {
      creatorLink.click();
      return 'CLICKED';
    }
    return 'NO_LINK';
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})();";

                var result = await webView.ExecuteScriptAsync(clickProfileScript);

                if (result.Contains("CLICKED"))
                {
                    await Task.Delay(rand.Next(3000, 7000), token); // Browse profile

                    // Scroll profile
                    await webView.ExecuteScriptAsync("window.scrollBy(0, " + rand.Next(200, 500) + ");");
                    await Task.Delay(rand.Next(2000, 4000), token);

                    // Back button (safe)
                    logTextBox.AppendText("[PROFILE] Returning to reels...\r\n");
                    await webView.ExecuteScriptAsync(@"
(function() {
  try {
    if (window.history.length > 1) {
      window.history.back();
      return 'BACK';
    }
    return 'NO_HISTORY';
  } catch(e) {
    return 'ERROR';
  }
})();");
                    await Task.Delay(rand.Next(2000, 3500), token);
                }
                else
                {
                    logTextBox.AppendText("[PROFILE] Could not find creator link\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[PROFILE ERROR] {ex.Message}\r\n");
            }
        }

        // ✅ AMÉLIORATION: Expand caption
        private async Task ExpandCaptionAsync(Random rand, CancellationToken token)
        {
            try
            {
                var expandScript = @"
(function(){
  try {
    const moreButtons = document.querySelectorAll('[role=""button""]');
    for (let btn of moreButtons) {
      const text = btn.textContent || '';
      if (text.includes('more') || text.includes('plus')) {
        const rect = btn.getBoundingClientRect();
        if (rect.top < window.innerHeight && rect.bottom > 0) {
          btn.click();
          return 'EXPANDED';
        }
      }
    }
    return 'NO_MORE_BUTTON';
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})();";

                var result = await webView.ExecuteScriptAsync(expandScript);
                if (result.Contains("EXPANDED"))
                {
                    logTextBox.AppendText("[CAPTION] Expanded caption to read more\r\n");
                    await Task.Delay(rand.Next(1500, 3500), token); // Read full caption
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[CAPTION ERROR] {ex.Message}\r\n");
            }
        }

        // ✅ AMÉLIORATION: Double-tap like
        private async Task DoubleTapLikeAsync(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[LIKE] Double-tap like...\r\n");

                var doubleTapScript = @"
(async function(){
  try {
    const videos = document.querySelectorAll('video');
    let mostVisible = null;
    let maxVisible = 0;

    videos.forEach(video => {
      const rect = video.getBoundingClientRect();
      const visible = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visible > maxVisible) {
        maxVisible = visible;
        mostVisible = video;
      }
    });

    if (!mostVisible) return 'NO_VIDEO';

    const rect = mostVisible.getBoundingClientRect();
    const x = rect.left + rect.width / 2;
    const y = rect.top + rect.height / 2;

    const opts = {bubbles: true, cancelable: true, clientX: x, clientY: y};
    mostVisible.dispatchEvent(new MouseEvent('click', opts));

    await new Promise(r => setTimeout(r, 150));

    mostVisible.dispatchEvent(new MouseEvent('click', opts));

    return 'DOUBLE_TAPPED';
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})();";

                await webView.ExecuteScriptAsync(doubleTapScript);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[DOUBLE_TAP ERROR] {ex.Message}\r\n");
            }
        }

        // ✅ AMÉLIORATION: Mini-pauses pendant le visionnage
        private async Task WatchWithMicroPausesAsync(int totalWatchTime, Random rand, CancellationToken token)
        {
            try
            {
                int elapsed = 0;

                while (elapsed < totalWatchTime && !token.IsCancellationRequested)
                {
                    int segment = rand.Next(2000, 5000);
                    await Task.Delay(Math.Min(segment, totalWatchTime - elapsed), token);
                    elapsed += segment;

                    // 30% de chance de micro-pause (regard ailleurs)
                    if (elapsed < totalWatchTime && rand.NextDouble() < 0.30)
                    {
                        int microPause = rand.Next(400, 1200);
                        logTextBox.AppendText($"[MICRO_PAUSE] {microPause}ms (looking away)\r\n");
                        await Task.Delay(microPause, token);
                        elapsed += microPause;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, just return
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[WATCH ERROR] {ex.Message}\r\n");
            }
        }

        // ✅ AMÉLIORATION: Adaptive behavior based on engagement
        private double GetLikeProbability(int watchTime, int comments)
        {
            double baseProbability = 0.04;

            // Si on a regardé longtemps (>20s), plus de chance de like
            if (watchTime > 20000) baseProbability *= 2.5;

            // Si beaucoup d'engagement, plus de chance de like
            if (comments > 5000) baseProbability *= 1.5;

            return Math.Min(baseProbability, 0.25); // Cap à 25%
        }

        // ✅ AMÉLIORATION: Mouvement de souris réaliste
        private async Task RandomMouseMovementAsync(Random rand, CancellationToken token)
        {
            try
            {
                if (rand.NextDouble() < 0.20)
                {
                    var mouseScript = @"
(function(){
  try {
    const container = document.querySelector('video')?.parentElement || document.body;
    const rect = container.getBoundingClientRect();

    const x = rect.left + Math.random() * rect.width;
    const y = rect.top + Math.random() * rect.height;

    container.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true,
      clientX: x,
      clientY: y
    }));

    return 'MOVED';
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})();";

                    await webView.ExecuteScriptAsync(mouseScript);
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[MOUSE ERROR] {ex.Message}\r\n");
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

                            // ✅ COMBINED FILTER: Niche (if enabled) && Language
                            bool passedNicheFilter = true;
                            bool passedLanguageFilter = true;

                            // 1️⃣ NICHE FILTER (if enabled in config)
                            if (config.ShouldApplyNicheFilter())
                            {
                                passedNicheFilter = await contentFilter.IsCurrentContentFemaleAsync();
                                if (!passedNicheFilter)
                                {
                                    logTextBox.AppendText($"[FILTER] ✗ Niche filter failed (not female) - SKIPPING\r\n");
                                }
                            }

                            // 2️⃣ LANGUAGE FILTER
                            string detectedLanguage = "Unknown";
                            string reelCaption = await ExtractReelCaptionAsync(token);

                            if (!string.IsNullOrWhiteSpace(reelCaption))
                            {
                                detectedLanguage = await languageDetector.DetectLanguageAsync(reelCaption);
                                passedLanguageFilter = config.IsLanguageTargeted(detectedLanguage);

                                if (!passedLanguageFilter)
                                {
                                    logTextBox.AppendText($"[FILTER] ✗ Language filter failed (detected: {detectedLanguage}) - SKIPPING\r\n");
                                }
                            }
                            else
                            {
                                // If no caption, assume language passes (can't determine)
                                logTextBox.AppendText($"[FILTER] ℹ️ No caption found, language filter skipped\r\n");
                            }

                            // 3️⃣ COMBINED DECISION: Both must pass
                            if (!passedNicheFilter || !passedLanguageFilter)
                            {
                                // ⚡ Skip IMMEDIATELY (algo signal: not interested at all)
                                bool scrollSuccess = await ScrollToNextReelAsync(rand, token);

                                if (scrollSuccess)
                                {
                                    string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = newCreator;
                                }

                                continue;
                            }

                            logTextBox.AppendText($"[FILTER] ✓ Content passed all filters (niche: {(config.ShouldApplyNicheFilter() ? "female" : "any")}, lang: {detectedLanguage})\r\n");

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

                            // ✅ FutureTargets: ONLY if perfect match (Female + target language)
                            bool isPerfectMatch = passedNicheFilter && passedLanguageFilter &&
                                                  config.ShouldApplyNicheFilter() && !config.IsLanguageTargeted("Any");

                            if (isPerfectMatch && comments >= config.MinCommentsToAddToFutureTargets &&
                                creatorName != "NO_CREATOR" && creatorName != "ERR" && creatorName != "NO_VISIBLE_VIDEO" && creatorName != "NO_PARENT3")
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
                                    logTextBox.AppendText($"[TARGET] ✓ Added {creatorName} to FutureTargets.txt (perfect match!)\r\n");
                                }
                                else
                                {
                                    logTextBox.AppendText($"[TARGET] {creatorName} already in FutureTargets.txt\r\n");
                                }
                            }

                            // ✅ SPEED OPTIMIZED: Simple watch time logic
                            int watchTime;
                            if (isPerfectMatch)
                            {
                                // 🎯 Perfect match (Female + target language): 10-25s watch
                                watchTime = rand.Next(10000, 25001);
                                logTextBox.AppendText($"[WATCH] 🎯 Perfect match → {watchTime / 1000}s\r\n");

                                // Expand caption (20% chance)
                                if (!string.IsNullOrWhiteSpace(reelCaption) && reelCaption.Length > 100 && rand.NextDouble() < 0.20)
                                {
                                    await ExpandCaptionAsync(rand, token);
                                }

                                await WatchWithMicroPausesAsync(watchTime, rand, token);
                                await RandomMouseMovementAsync(rand, token);

                                // Long pause (5% chance)
                                if (await ShouldTakeLongPause(rand))
                                {
                                    await TakeLongPauseWithVideo(rand, token);
                                }

                                // Replay (15% chance if watch > 15s)
                                if (watchTime > 15000 && rand.NextDouble() < 0.15)
                                {
                                    await ReplayReelAsync(rand, token);
                                }

                                // Profile visit (5-8% chance if high engagement)
                                if (comments >= config.MinCommentsToAddToFutureTargets && rand.NextDouble() < 0.08)
                                {
                                    await VisitCreatorProfileAsync(rand, token);
                                }
                            }
                            else
                            {
                                // ⚡ NOT a match: INSTANT SKIP (0ms)
                                watchTime = 0;
                                logTextBox.AppendText($"[SKIP] ⚡ Instant skip\r\n");
                                // No delay at all - skip immediately
                            }

                            // ✅ Like logic (only for perfect matches)
                            bool shouldLike = isPerfectMatch && rand.NextDouble() < GetLikeProbability(watchTime, comments);

                            if (shouldLike)
                            {
                                // 30% de chance de double-tap au lieu du bouton
                                if (rand.NextDouble() < 0.30)
                                {
                                    await DoubleTapLikeAsync(rand, token);
                                }
                                else
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
                                }
                                await Task.Delay(rand.Next(1500, 3500), token);
                            }

                            // Random "distraction" pause
                            if (rand.NextDouble() < 0.2)
                            {
                                await Task.Delay(rand.Next(1000, 3000), token);
                            }
                            await RandomHumanScrollNoiseAsync(rand, token);

                            // ✅ AMÉLIORATION: Hésitation avant de scroller (35% de chance)
                            if (rand.NextDouble() < 0.35)
                            {
                                int hesitationTime = rand.Next(800, 2500);
                                logTextBox.AppendText($"[HESITATION] Pausing {hesitationTime}ms before scrolling...\r\n");
                                await Task.Delay(hesitationTime, token);
                            }

                            // ✅ AMÉLIORATION: Comportement "oops" - scroll rapide + retour immédiat (8% de chance)
                            if (reelNum > 2 && rand.NextDouble() < 0.08)
                            {
                                logTextBox.AppendText("[OOPS] Fast scroll then immediate return...\r\n");

                                // Scroll rapide
                                await ScrollToNextReelAsync(rand, token);
                                string tempCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);

                                await Task.Delay(rand.Next(300, 800), token); // Court délai

                                // Retour immédiat
                                await ScrollBackToPreviousReelAsync(rand, token);
                                previousCreator = await WaitForNewReelAsync(tempCreator, creatorScript, rand, token);

                                await Task.Delay(rand.Next(2000, 4000), token); // Re-watch
                                continue; // Continue la boucle sur ce reel
                            }

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