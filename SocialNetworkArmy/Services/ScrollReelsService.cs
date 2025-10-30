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

    let container = mostVisibleVideo.parentElement;
    for (let i = 0; i < 5 && container; i++) {
      container = container.parentElement;
    }

    if (!container) return '';

    const h1 = container.querySelector('h1');
    if (h1 && h1.textContent && h1.textContent.trim().length > 0) {
      return h1.textContent.trim();
    }

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

        /// <summary>
        /// Get humanized watch time based on content quality
        /// </summary>
        private int GetWatchTime(Random rand, bool isPerfectMatch)
        {
            if (isPerfectMatch)
            {
                // Perfect match: 15-30s (strong engagement signal)
                return rand.Next(15000, 30001);
            }
            else
            {
                // Not a match: instant skip
                return 0;
            }
        }

        /// <summary>
        /// Calculate like probability based on watch time and engagement
        /// </summary>
        private double GetLikeProbability(int watchTime, int comments)
        {
            double baseProbability = 0.15; // Base 15% for perfect matches

            // Bonus for long watch time (watched >20s)
            if (watchTime > 20000)
            {
                baseProbability += 0.08; // +8%
            }

            // Bonus for high engagement content (>5k comments)
            if (comments > 5000)
            {
                baseProbability += 0.05; // +5%
            }

            // Cap at 30% max
            return Math.Min(baseProbability, 0.30);
        }

        /// <summary>
        /// Calculate profile visit probability based on watch time and engagement
        /// Higher engagement = more likely to check creator's profile
        /// </summary>
        private double GetProfileVisitProbability(int watchTime, int comments)
        {
            double baseProbability = 0.03; // Base 3% for perfect matches

            // Bonus for very long watch time (watched >25s = really interested)
            if (watchTime > 25000)
            {
                baseProbability += 0.05; // +5%
            }

            // Bonus for high engagement (>5k comments)
            if (comments > 5000)
            {
                baseProbability += 0.04; // +4%
            }

            // Big bonus for viral content (>15k comments)
            if (comments > 15000)
            {
                baseProbability += 0.08; // +8%
            }

            // Cap at 20% max (profile visits are rarer than likes)
            return Math.Min(baseProbability, 0.20);
        }

        private async Task<bool> ShouldTakeLongPause(Random rand)
        {
            return rand.NextDouble() < 0.05; // 5% chance
        }

        private async Task TakeLongPauseWithVideo(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[PAUSE] Pausing video for extended watch...\r\n");

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

                int pauseDuration = rand.Next(10000, 30001);
                logTextBox.AppendText($"[PAUSE] Taking {pauseDuration / 1000}s break...\r\n");
                await Task.Delay(pauseDuration, token);

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
                await Task.Delay(rand.Next(500, 1500), token);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[PAUSE ERROR] {ex.Message}\r\n");
                Logger.LogError($"TakeLongPauseWithVideo: {ex}");
            }
        }

        private async Task<bool> ScrollToNextReelAsync(Random rand, CancellationToken token)
        {
            // Varied scroll speeds (human-like)
            double speedProfile = rand.NextDouble();
            int duration;

            if (speedProfile < 0.15) // 15%: Fast scroll
            {
                duration = 400 + rand.Next(0, 200);
            }
            else if (speedProfile < 0.75) // 60%: Normal scroll
            {
                duration = 800 + rand.Next(0, 400);
            }
            else // 25%: Slow scroll
            {
                duration = 1200 + rand.Next(0, 600);
            }

            int horizontalDrift = rand.Next(-8, 9); // Slight horizontal drift

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

    const y = startY + (targetY - startY) * easeOut;
    const x = startX + horizontalDrift * Math.sin(progress * Math.PI);

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
            await Task.Delay(rand.Next(2000, 4000), token);

            return true;
        }

        private async Task<string> WaitForNewReelAsync(string previousCreator, string creatorScript, Random rand, CancellationToken token, int maxAttempts = 5)
        {
            string newCreator = null;
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                await Task.Delay(rand.Next(200, 400), token);

                newCreator = await webView.ExecuteScriptAsync(creatorScript);
                newCreator = newCreator?.Trim('"').Trim();

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
                    // Retry strategies
                    if (attempts == 2 || attempts == 4)
                    {
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
                }
            }

            logTextBox.AppendText($"[WARNING] Could not confirm reel change after {maxAttempts} attempts\r\n");
            return newCreator ?? previousCreator;
        }

        /// <summary>
        /// Add random scroll noise to simulate human behavior (25% chance)
        /// </summary>
        private async Task RandomScrollNoiseAsync(Random rand, CancellationToken token)
        {
            if (rand.NextDouble() < 0.25)
            {
                var noiseScript = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  let scroller = document.querySelector('div[role=""main""] > div') ||
                 document.querySelector('div[data-testid=""reels-tab""]') ||
                 Array.from(document.querySelectorAll('div')).find(div => {
                   const style = window.getComputedStyle(div);
                   return (style.overflowY === 'scroll' || style.overflowY === 'auto') && div.clientHeight >= window.innerHeight * 0.8;
                 }) || document.body;

  if (Math.random() < 0.40) {
    const scrollUpAmount = -(Math.random() * 80 + 40);
    scroller.scrollBy({
      top: scrollUpAmount,
      behavior: 'smooth'
    });
    await sleep(600 + Math.random() * 400);
  } else {
    const randomScroll = Math.random() * 100 - 50;
    scroller.scrollBy({
      top: randomScroll,
      behavior: 'smooth'
    });
    await sleep(400);
  }

  return 'NOISE_ADDED';
})()";

                await webView.ExecuteScriptAsync(noiseScript);
            }
        }

        /// <summary>
        /// Visit creator's profile with realistic browsing behavior
        /// </summary>
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
                    // Browse profile (3-8s)
                    int browseDuration = rand.Next(3000, 8000);
                    logTextBox.AppendText($"[PROFILE] Browsing for {browseDuration / 1000}s...\r\n");
                    await Task.Delay(browseDuration, token);

                    // Scroll profile (realistic browsing)
                    int scrollAmount = rand.Next(200, 600);
                    await webView.ExecuteScriptAsync($"window.scrollBy(0, {scrollAmount});");
                    await Task.Delay(rand.Next(2000, 4000), token);

                    // Sometimes scroll more (40% chance)
                    if (rand.NextDouble() < 0.40)
                    {
                        scrollAmount = rand.Next(300, 700);
                        await webView.ExecuteScriptAsync($"window.scrollBy(0, {scrollAmount});");
                        await Task.Delay(rand.Next(1500, 3000), token);
                    }

                    // Return to reels
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
                    logTextBox.AppendText($"[PROFILE] Could not click profile link: {result}\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[PROFILE ERROR] {ex.Message}\r\n");
                Logger.LogError($"VisitCreatorProfileAsync: {ex}");
            }
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("Scroll");
                var localToken = form.GetCancellationToken();
                token = localToken;

                try
                {
                    Random rand = new Random();
                    logTextBox.AppendText($"[SCROLL] Starting continuous reels processing.\r\n");

                    // Navigate to Reels page
                    var targetUrl = $"https://www.instagram.com/reels/";
                    webView.CoreWebView2.Navigate(targetUrl);
                    await Task.Delay(rand.Next(4000, 7001), token);

                    // Initial scroll to load first reel
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

                    // Wait for reels feed to load
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

                    // Prepare FutureTargets.txt
                    string dataDir = Path.Combine(Application.StartupPath, "data");
                    Directory.CreateDirectory(dataDir);
                    string targetFile = Path.Combine(dataDir, "FutureTargets.txt");

                    // MAIN REELS LOOP
                    string previousCreator = null;
                    int reelNum = 0;

                    while (!token.IsCancellationRequested)
                    {
                        reelNum++;

                        try
                        {
                            logTextBox.AppendText($"\r\n[REEL {reelNum}] Starting interaction...\r\n");

                            // Extract creator name
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

                            // FILTERS: Niche + Language
                            bool passedNicheFilter = true;
                            bool passedLanguageFilter = true;

                            // 1. Niche filter (if enabled)
                            if (config.ShouldApplyNicheFilter())
                            {
                                passedNicheFilter = await contentFilter.IsCurrentContentFemaleAsync();
                                if (!passedNicheFilter)
                                {
                                    logTextBox.AppendText($"[FILTER] ✗ Niche filter failed - SKIPPING\r\n");
                                }
                            }

                            // 2. Language filter
                            string detectedLanguage = "Unknown";
                            string reelCaption = await ExtractReelCaptionAsync(token);

                            if (!string.IsNullOrWhiteSpace(reelCaption))
                            {
                                detectedLanguage = await languageDetector.DetectLanguageAsync(reelCaption);
                                passedLanguageFilter = config.IsLanguageTargeted(detectedLanguage);

                                if (!passedLanguageFilter)
                                {
                                    logTextBox.AppendText($"[FILTER] ✗ Language ({detectedLanguage}) - SKIPPING\r\n");
                                }
                            }
                            else
                            {
                                logTextBox.AppendText($"[FILTER] ℹ️ No caption, language filter skipped\r\n");
                            }

                            // Combined decision
                            bool isPerfectMatch = passedNicheFilter && passedLanguageFilter;

                            if (!isPerfectMatch)
                            {
                                // Instant skip
                                logTextBox.AppendText($"[SKIP] ⚡ Instant skip (filters failed)\r\n");
                                bool scrollSuccess = await ScrollToNextReelAsync(rand, token);

                                if (scrollSuccess)
                                {
                                    string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                    previousCreator = newCreator;
                                }

                                continue;
                            }

                            logTextBox.AppendText($"[FILTER] ✓ Perfect match (niche + language: {detectedLanguage})\r\n");

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

                            logTextBox.AppendText($"[ENGAGEMENT] {comments} comments\r\n");

                            // Add to FutureTargets if high engagement
                            if (comments >= config.MinCommentsToAddToFutureTargets &&
                                creatorName != "NO_CREATOR" && creatorName != "ERR" &&
                                creatorName != "NO_VISIBLE_VIDEO" && creatorName != "NO_PARENT3")
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
                                    logTextBox.AppendText($"[TARGET] ✓ Added {creatorName} to FutureTargets.txt\r\n");
                                }
                            }

                            // Get watch time
                            int watchTime = GetWatchTime(rand, isPerfectMatch);
                            logTextBox.AppendText($"[WATCH] 🎯 Perfect match → {watchTime / 1000}s\r\n");

                            // Watch the reel
                            await Task.Delay(watchTime, token);

                            // Long pause (5% chance)
                            if (await ShouldTakeLongPause(rand))
                            {
                                await TakeLongPauseWithVideo(rand, token);
                            }

                            // Calculate like probability using GetLikeProbability
                            double likeProbability = GetLikeProbability(watchTime, comments);
                            bool shouldLike = rand.NextDouble() < likeProbability;

                            logTextBox.AppendText($"[LIKE] Probability: {likeProbability:P0} → {(shouldLike ? "WILL LIKE" : "SKIP")}\r\n");

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
                                logTextBox.AppendText($"[LIKE] ❤️ {likeTry}\r\n");
                                await Task.Delay(rand.Next(1500, 3500), token);
                            }

                            // Calculate profile visit probability
                            double profileVisitProbability = GetProfileVisitProbability(watchTime, comments);
                            bool shouldVisitProfile = rand.NextDouble() < profileVisitProbability;

                            logTextBox.AppendText($"[PROFILE] Probability: {profileVisitProbability:P0} → {(shouldVisitProfile ? "WILL VISIT" : "SKIP")}\r\n");

                            if (shouldVisitProfile)
                            {
                                await VisitCreatorProfileAsync(rand, token);
                            }

                            // Random noise and hesitation
                            if (rand.NextDouble() < 0.2)
                            {
                                await Task.Delay(rand.Next(1000, 3000), token);
                            }

                            await RandomScrollNoiseAsync(rand, token);

                            if (rand.NextDouble() < 0.35)
                            {
                                int hesitationTime = rand.Next(800, 2500);
                                logTextBox.AppendText($"[HESITATION] {hesitationTime}ms...\r\n");
                                await Task.Delay(hesitationTime, token);
                            }

                            // Scroll to next reel
                            bool scrollSuccess2 = await ScrollToNextReelAsync(rand, token);

                            if (scrollSuccess2)
                            {
                                string newCreator = await WaitForNewReelAsync(previousCreator, creatorScript, rand, token);
                                previousCreator = newCreator;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            logTextBox.AppendText("Script cancelled.\r\n");
                            break;
                        }
                        catch (Exception ex)
                        {
                            logTextBox.AppendText($"[REEL ERROR] {ex.Message}\r\n");
                            Logger.LogError($"ScrollService reel loop: {ex}");
                            await Task.Delay(rand.Next(2000, 5000), token);
                        }
                    }

                    logTextBox.AppendText("\r\n[COMPLETE] Reels processing stopped.\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("Script cancelled by user.\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                    Logger.LogError($"ScrollService.RunAsync: {ex}");
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