using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class ScrollHomeService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly Random rand = new Random();
        private System.Collections.Generic.HashSet<string> donePostIds;
        private string donePostsPath;
        private readonly ContentFilterService contentFilter;
        private readonly LanguageDetectionService languageDetector;
        private readonly Models.AccountConfig config;

        public ScrollHomeService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.contentFilter = new ContentFilterService(webView, logTextBox);
            this.languageDetector = new LanguageDetectionService(logTextBox);
            this.config = ConfigService.LoadConfig(profile.Name);
        }

        private static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get humanized watch time based on content quality and type
        /// Consistent with ScrollReelsService
        /// </summary>
        private int GetWatchTime(Random rand, bool isPerfectMatch, bool isReel)
        {
            if (isPerfectMatch)
            {
                if (isReel)
                {
                    // Perfect match reel: 10-18s (same as ScrollReels)
                    return rand.Next(10000, 18001);
                }
                else
                {
                    // Perfect match static: 2-5s (images are quicker than videos)
                    return rand.Next(2000, 5001);
                }
            }
            else
            {
                // Not a match: instant skip
                return 0;
            }
        }

        /// <summary>
        /// Calculate like probability based on watch time and engagement
        /// Consistent with ScrollReelsService
        /// </summary>
        private double GetLikeProbability(int watchTime, int comments)
        {
            double baseProbability = 0.15; // Base 15% for perfect matches

            // Bonus for long watch time (watched >12s)
            if (watchTime > 12000)
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
        /// Consistent with ScrollReelsService
        /// </summary>
        private double GetProfileVisitProbability(int watchTime, int comments)
        {
            double baseProbability = 0.03; // Base 3% for perfect matches

            // Bonus for long watch time (watched >15s = really interested)
            if (watchTime > 15000)
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

        /// <summary>
        /// Visit creator's profile with realistic browsing behavior
        /// Consistent with ScrollReelsService
        /// </summary>
        private async Task VisitCreatorProfileAsync(Random rand, CancellationToken token)
        {
            try
            {
                logTextBox.AppendText("[PROFILE] Visiting creator profile...\r\n");

                var clickProfileScript = @"
(function(){
  try {
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {
      const rect = article.getBoundingClientRect();
      const visibleHeight = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      if (visibleHeight > bestScore) {
        bestScore = visibleHeight;
        targetArticle = article;
      }
    }

    if (!targetArticle) return 'NO_ARTICLE';

    const creatorLink = targetArticle.querySelector('a[href*=""/""][role=""link""]');
    if (!creatorLink) return 'NO_LINK';

    creatorLink.click();
    return 'CLICKED';
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

                    // Return to feed
                    logTextBox.AppendText("[PROFILE] Returning to feed...\r\n");
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

        private async Task RandomHumanNoiseAsync(CancellationToken token)
        {
            if (rand.NextDouble() < 0.25)
            {
                var noiseScript = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  if (Math.random() < 0.40) {
    const scrollUpAmount = -(Math.random() * 80 + 40);
    window.scrollBy({
      top: scrollUpAmount,
      behavior: 'smooth'
    });
    await sleep(600 + Math.random() * 400);
  } else {
    const randomScroll = Math.random() * 100 - 50;
    window.scrollBy({
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

        private async Task<string> ExtractPostIdAsync(CancellationToken token)
        {
            var postIdScript = @"
(function(){
  try {
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;
    
    for (let article of articles) {
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
      const visiblePercent = visibleHeight / rect.height;
      
      if (visiblePercent > 0.3 && visibleHeight > bestScore) {
        bestScore = visibleHeight;
        targetArticle = article;
      }
    }
    
    if (!targetArticle) return 'NO_ARTICLE';
    
    const postLink = targetArticle.querySelector('a[href*=""/p/""], a[href*=""/reel/""]');
    if (!postLink) return 'NO_LINK';
    
    const href = postLink.getAttribute('href');
    const match = href.match(/\/(p|reel)\/([^\/\?]+)/);
    
    return match ? match[2] : 'NO_ID';
  } catch(e) {
    return 'ERROR: ' + e.message;
  }
})();";

            try
            {
                var result = await webView.ExecuteScriptAsync(postIdScript);
                return result?.Trim('"').Trim() ?? "NO_ID";
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[POST_ID ERROR] {ex.Message}\r\n");
                return "ERROR";
            }
        }

        private async Task<string> ExtractPostCaptionAsync(CancellationToken token)
        {
            var captionScript = @"
(function(){
  try {
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
      const visiblePercent = visibleHeight / rect.height;

      if (visiblePercent > 0.3 && visibleHeight > bestScore) {
        bestScore = visibleHeight;
        targetArticle = article;
      }
    }

    if (!targetArticle) return '';

    const h1 = targetArticle.querySelector('h1');
    if (h1 && h1.textContent && h1.textContent.trim().length > 0) {
      return h1.textContent.trim();
    }

    const spans = targetArticle.querySelectorAll('span');
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

        private async Task<(bool found, string datetime, string text, double ageHours, bool isReel)> ExtractPostInfoAsync(CancellationToken token)
        {
            var extractScript = @"
(function(){
  try {
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
      const visiblePercent = visibleHeight / rect.height;

      if (visiblePercent > 0.3 && visibleHeight > bestScore) {
        bestScore = visibleHeight;
        targetArticle = article;
      }
    }

    if (!targetArticle) return {found: false};

    const video = targetArticle.querySelector('video');
    const hasVideo = !!video;

    const timeEl = targetArticle.querySelector('time[datetime]') || targetArticle.querySelector('time');
    if (!timeEl) return {found: false, isReel: hasVideo};

    const datetime = timeEl.getAttribute('datetime') || '';
    const text = timeEl.textContent || '';

    return {
      found: true,
      isReel: hasVideo,
      datetime: datetime,
      text: text
    };
  } catch(e) {
    return {found: false, error: e.message};
  }
})();";

            var result = await webView.ExecuteScriptAsync(extractScript);
            result = result?.Trim('"');

            if (string.IsNullOrWhiteSpace(result) || !result.StartsWith("{"))
            {
                return (false, null, null, -1, false);
            }

            try
            {
                var cleanedJson = result
                    .Replace("\\\"", "\"")
                    .Replace("\"{", "{")
                    .Replace("}\"", "}");

                var data = JsonSerializer.Deserialize<JsonElement>(cleanedJson);

                if (!data.GetProperty("found").GetBoolean())
                {
                    return (false, null, null, -1, false);
                }

                bool isReel = data.GetProperty("isReel").GetBoolean();
                string datetime = data.GetProperty("datetime").GetString();
                string text = data.GetProperty("text").GetString();

                double ageHours = -1;
                if (!string.IsNullOrEmpty(datetime) && DateTimeOffset.TryParse(datetime, out var postTime))
                {
                    var age = DateTimeOffset.UtcNow - postTime;
                    ageHours = age.TotalHours;
                }

                return (true, datetime, text, ageHours, isReel);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXTRACT ERROR] {ex.Message}\r\n");
                return (false, null, null, -1, false);
            }
        }

        private bool IsPostAlreadyCommented(string postId)
        {
            if (string.IsNullOrWhiteSpace(postId) ||
                postId == "NO_ARTICLE" ||
                postId == "NO_LINK" ||
                postId == "NO_ID" ||
                postId.StartsWith("ERROR"))
            {
                return false;
            }

            return donePostIds.Contains(postId);
        }

        private async Task MarkPostAsDoneThreadSafe(string postId, CancellationToken token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(postId) ||
                    postId == "NO_ARTICLE" ||
                    postId == "NO_LINK" ||
                    postId == "NO_ID" ||
                    postId.StartsWith("ERROR"))
                {
                    return;
                }

                string groupName = !string.IsNullOrWhiteSpace(profile.GroupName)
                    ? profile.GroupName
                    : profile.Name;

                var groupLock = ScheduleService.GetGroupLock(groupName);
                await groupLock.WaitAsync(token);

                try
                {
                    var currentDone = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (File.Exists(donePostsPath))
                    {
                        currentDone = new System.Collections.Generic.HashSet<string>(
                            File.ReadAllLines(donePostsPath)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Select(line => line.Trim()),
                            StringComparer.OrdinalIgnoreCase
                        );
                    }

                    if (currentDone.Contains(postId))
                    {
                        logTextBox.AppendText($"[DONE_POSTS] ℹ️ Already marked by another account: {postId}\r\n");
                        donePostIds.Add(postId);
                        return;
                    }

                    File.AppendAllText(donePostsPath, postId + Environment.NewLine);
                    donePostIds.Add(postId);

                    string fileName = Path.GetFileName(donePostsPath);
                    logTextBox.AppendText($"[DONE_POSTS] ✓ Added to {fileName}: {postId}\r\n");
                }
                finally
                {
                    groupLock.Release();
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[DONE_POSTS ERROR] {ex.Message}\r\n");
                Logger.LogError($"MarkPostAsDoneThreadSafe: {ex}");
            }
        }

        private async Task<(string creator, int comments)> ExtractCreatorAndCommentsAsync(CancellationToken token)
        {
            var extractScript = @"
(function(){
  try {
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
      const visiblePercent = visibleHeight / rect.height;
  
      if (visiblePercent > 0.3 && visibleHeight > bestScore) {
        bestScore = visibleHeight;
        targetArticle = article;
      }
    }

    if (!targetArticle) return {creator: 'NO_ARTICLE', comments: 0};

    const creatorLink = targetArticle.querySelector('a[href*=""/""][role=""link""]');
    let creator = 'NO_CREATOR';
    if (creatorLink) {
      const href = creatorLink.getAttribute('href');
      const match = href.match(/^\/([^\/\?]+)/);
      if (match && match[1] && !match[1].includes('/')) {
        creator = match[1];
      }
    }

    let comments = 0;
    
    const commentBtns = targetArticle.querySelectorAll('[role=""button""], a');
    for (let btn of commentBtns) {
      const ariaLabel = btn.getAttribute('aria-label') || '';
      const text = btn.textContent || '';
      
      let match = ariaLabel.match(/(\d+[\s,]*\d*)\s*(k)?\s*commentaire/i);
      if (!match) {
        match = text.match(/(\d+[\s,]*\d*)\s*(k)?\s*commentaire/i);
      }

      if (!match) {
        match = ariaLabel.match(/(\d+[\s,]*\d*)\s*(k)?\s*comment/i);
      }
      if (!match) {
        match = text.match(/(\d+[\s,]*\d*)\s*(k)?\s*comment/i);
      }

      if (match) {
        let numStr = match[1].replace(/[\s,]/g, '');
        let num = parseFloat(numStr);

        if (match[2] && match[2].toLowerCase() === 'k') {
          num = num * 1000;
        }

        comments = Math.floor(num);
        break;
      }
    }

    return {
      creator: creator,
      comments: comments
    };
  } catch(e) {
    return {creator: 'ERROR', comments: 0};
  }
})();";

            try
            {
                var result = await webView.ExecuteScriptAsync(extractScript);
                result = result?.Trim('"');

                if (string.IsNullOrWhiteSpace(result) || !result.StartsWith("{"))
                {
                    return ("NO_DATA", 0);
                }

                var cleanedJson = result
                    .Replace("\\\"", "\"")
                    .Replace("\"{", "{")
                    .Replace("}\"", "}");

                var data = JsonSerializer.Deserialize<JsonElement>(cleanedJson);

                string creator = data.GetProperty("creator").GetString() ?? "NO_CREATOR";
                int comments = data.GetProperty("comments").GetInt32();

                return (creator, comments);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXTRACT CREATOR ERROR] {ex.Message}\r\n");
                return ("ERROR", 0);
            }
        }

        private void AddToFutureTargets(string creator, int comments, string targetFile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(creator) ||
                    creator == "NO_ARTICLE" ||
                    creator == "NO_CREATOR" ||
                    creator == "ERROR" ||
                    creator == "NO_DATA" ||
                    creator.Contains("explore") ||
                    creator.Contains("reel") ||
                    creator.Contains("p/"))
                {
                    return;
                }

                if (comments < config.MinCommentsToAddToFutureTargets)
                {
                    return;
                }

                bool alreadyExists = false;
                if (File.Exists(targetFile))
                {
                    var lines = File.ReadAllLines(targetFile)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => l.Trim())
                        .ToList();

                    alreadyExists = lines.Any(l =>
                        l.Equals(creator, StringComparison.OrdinalIgnoreCase));
                }

                if (!alreadyExists)
                {
                    File.AppendAllText(targetFile, creator.Trim() + Environment.NewLine);
                    logTextBox.AppendText($"[TARGET] ✓ Added '{creator}' to FutureTargets.txt ({comments} comments)\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[TARGET ERROR] {ex.Message}\r\n");
                Logger.LogError($"AddToFutureTargets: {ex}");
            }
        }

        private async Task<string> LikeCurrentPostAsync(string likeSelectors, string unlikeTest, CancellationToken token)
        {
            var likeScript = $@"
(function(){{
  try {{
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {{
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
  
      if (visibleHeight > bestScore) {{
        bestScore = visibleHeight;
        targetArticle = article;
      }}
    }}

    if (!targetArticle) return 'NO_ARTICLE';

    const svg = targetArticle.querySelector('{likeSelectors}');
    if (!svg) return 'NO_SVG';

    const svgAria = svg.getAttribute('aria-label') || '';
    const isAlreadyLiked = /{unlikeTest}/i.test(svgAria);

    if (isAlreadyLiked) return 'ALREADY_LIKED';

    const btn = svg.closest('button, [role=""button""]');
    if (!btn) return 'NO_BUTTON';

    btn.scrollIntoView({{behavior:'smooth', block:'center'}});
    btn.click();

    return 'LIKED';
  }} catch(e) {{
    return 'ERROR: ' + e.message;
  }}
}})();";

            return await webView.ExecuteScriptAsync(likeScript);
        }

        private async Task CommentOnCurrentPostAsync(string comment, string publishPattern, CancellationToken token)
        {
            logTextBox.AppendText($"[COMMENT] Selected: '{comment}'\r\n");

            var escapedComment = comment
                .Replace("\\", "\\\\")
                .Replace("\u2018", "'")
                .Replace("\u2019", "'")
                .Replace("'", "\\'");

            var typingScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  function randomDelay(min, max) {{
    return Math.floor(min + Math.random() * (max - min + 1));
  }}

  try {{
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;

    for (let article of articles) {{
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
  
      if (visibleHeight > bestScore) {{
        bestScore = visibleHeight;
        targetArticle = article;
      }}
    }}

    if (!targetArticle) return 'NO_ARTICLE';

    const text = '{escapedComment}';
    const chars = Array.from(text);

    let ta = targetArticle.querySelector('textarea');
    let ce = null;

    if (!ta) {{
      ce = targetArticle.querySelector('div[role=""textbox""][contenteditable=""true""]');
      if (!ce) return 'NO_COMPOSER_INITIAL';
    }}

    const initialTarget = ta || ce;

    initialTarget.scrollIntoView({{behavior:'smooth', block:'center'}});
    await sleep(randomDelay(200, 400));

    const rect = initialTarget.getBoundingClientRect();
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
      initialTarget.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}

    const opts = {{bubbles:true, cancelable:true, clientX:clientX, clientY:clientY, button:0}};
    initialTarget.dispatchEvent(new MouseEvent('mousedown', opts));
    initialTarget.dispatchEvent(new MouseEvent('mouseup', opts));
    initialTarget.dispatchEvent(new MouseEvent('click', opts));

    await sleep(randomDelay(100, 250));
    initialTarget.focus();

    for (let i = 0; i < chars.length; i++) {{
      const char = chars[i];
  
      ta = targetArticle.querySelector('textarea');
      ce = null;
  
      if (!ta) {{
        ce = targetArticle.querySelector('div[role=""textbox""][contenteditable=""true""]');
        if (!ce) return 'NO_COMPOSER_AT_' + i;
      }}
  
      const currentTarget = ta || ce;
  
      try {{
        if (ta) {{
          const currentValue = ta.value;
          const proto = HTMLTextAreaElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(ta, currentValue + char);
      
          ta.dispatchEvent(new Event('input', {{bubbles: true}}));
          ta.dispatchEvent(new Event('change', {{bubbles: true}}));
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
    
        const wrongChars = 'azerty';
        const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
    
        ta = targetArticle.querySelector('textarea');
        ce = null;
        if (!ta) {{
          ce = targetArticle.querySelector('div[role=""textbox""][contenteditable=""true""]');
          if (!ce) return 'NO_COMPOSER_ERROR_AT_' + i;
        }}
    
        const currentTargetError = ta || ce;
    
        try {{
          if (ta) {{
            const currentValue = currentTargetError.value;
            const proto = HTMLTextAreaElement.prototype;
            const desc = Object.getOwnPropertyDescriptor(proto, 'value');
            desc.set.call(ta, currentValue + wrongChar);
            ta.dispatchEvent(new Event('input', {{bubbles: true}}));
          }} else {{
            document.execCommand('insertText', false, wrongChar);
          }}
        }} catch(e) {{
          return 'ERROR_TYPE_AT_' + i + ': ' + (e.message || String(e));
        }}
    
        await sleep(randomDelay(100, 250));
    
        ta = targetArticle.querySelector('textarea');
        ce = null;
        if (!ta) {{
          ce = targetArticle.querySelector('div[role=""textbox""][contenteditable=""true""]');
          if (!ce) return 'NO_COMPOSER_DELETE_AT_' + i;
        }}
    
        const currentTargetDelete = ta || ce;
    
        try {{
          if (ta) {{
            const currentValue = currentTargetDelete.value;
            const proto = HTMLTextAreaElement.prototype;
            const desc = Object.getOwnPropertyDescriptor(proto, 'value');
            desc.set.call(ta, currentValue.slice(0, -1));
            ta.dispatchEvent(new Event('input', {{bubbles: true}}));
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
  }} catch(e) {{
    return 'ERROR: ' + (e.message || String(e));
  }}
}})()";

            int charCount = comment.Length;
            int baseTime = charCount * 100;
            int punctuationCount = comment.Count(c => ".!?,;".Contains(c));
            int punctuationDelay = punctuationCount * 300;
            int errorDelay = (int)(charCount * 0.05 * 500);
            int totalTime = baseTime + punctuationDelay + errorDelay + 3000;

            logTextBox.AppendText($"[TYPING] Starting... (estimated {totalTime}ms)\r\n");

            var typingTask = webView.ExecuteScriptAsync(typingScript);
            await Task.Delay(totalTime, token);
            var typingResult = await typingTask;
            logTextBox.AppendText($"[TYPING] {typingResult}\r\n");

            await Task.Delay(rand.Next(1000, 2000), token);

            var publishScript = $@"
(async function(){{
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  
  function btnEnabled(b){{
    if (!b) return false;
    if (b.disabled) return false;
    const ad = b.getAttribute('aria-disabled');
    if (ad && ad.toString().toLowerCase() === 'true') return false;
    const st = getComputedStyle(b);
    return !(st.pointerEvents === 'none' || st.display === 'none' || st.visibility === 'hidden');
  }}

  function findPublishControl(form){{
    if (!form) return null;
    let btn = form.querySelector('button[type=""submit""]');
    if (btn) return btn;
    const candidates = [...form.querySelectorAll('button,[role=""button""]')];
    const match = candidates.find(el => /{publishPattern}/i.test((el.textContent||'').trim()));
    if (match) return match;
    return null;
  }}

  async function waitEnabled(el, timeout=10000){{
    const t0 = performance.now();
    while (performance.now() - t0 < timeout){{
      if (btnEnabled(el)) return true;
      await sleep(150);
    }}
    return false;
  }}

  try {{
    const articles = document.querySelectorAll('article');
    let targetArticle = null;
    let bestScore = -1;
    
    for (let article of articles) {{
      const rect = article.getBoundingClientRect();
      const visibleTop = Math.max(rect.top, 0);
      const visibleBottom = Math.min(rect.bottom, window.innerHeight);
      const visibleHeight = Math.max(0, visibleBottom - visibleTop);
      
      if (visibleHeight > bestScore) {{
        bestScore = visibleHeight;
        targetArticle = article;
      }}
    }}
    
    if (!targetArticle) return 'NO_ARTICLE';
    
    const form = targetArticle.querySelector('form');
    if (!form) return 'NO_FORM';
    
    const ctrl = findPublishControl(form);
    if (!ctrl) return 'NO_CTRL';
    
    const ok = await waitEnabled(ctrl, 10000);
    if (!ok) return 'CTRL_DISABLED_TIMEOUT';
    
    const btnRect = ctrl.getBoundingClientRect();
    var marginX = btnRect.width * 0.2;
    var marginY = btnRect.height * 0.2;
    var offsetX = marginX + Math.random() * (btnRect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (btnRect.height - 2 * marginY);
    const btnX = btnRect.left + offsetX;
    const btnY = btnRect.top + offsetY;
    
    var startX = btnX + (Math.random() * 100 - 50);
    var startY = btnY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (btnX - startX) * (i / 5);
      var moveY = startY + (btnY - startY) * (i / 5);
      ctrl.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    const btnOpts = {{bubbles:true, cancelable:true, clientX:btnX, clientY:btnY, button:0}};
    ctrl.dispatchEvent(new MouseEvent('mousedown', btnOpts));
    ctrl.dispatchEvent(new MouseEvent('mouseup', btnOpts));
    ctrl.dispatchEvent(new MouseEvent('click', btnOpts));
    
    const t0 = performance.now();
    while (performance.now() - t0 < 12000) {{
      const ta2 = targetArticle.querySelector('textarea');
      const ce2 = targetArticle.querySelector('div[role=""textbox""][contenteditable=""true""]');
      const target = ta2 || ce2;
      
      if (!target) break;
      
      const content = ta2 ? ta2.value : (ce2 ? ce2.textContent : '');
      if (content.trim().length === 0) break;
      
      await sleep(220);
    }}
    
    return 'PUBLISHED';
  }} catch(e) {{
    return 'ERROR: ' + (e.message || String(e));
  }}
}})()";

            var publishResult = await webView.ExecuteScriptAsync(publishScript);
            logTextBox.AppendText($"[PUBLISH] {publishResult}\r\n");
            await Task.Delay(rand.Next(1500, 2500), token);
        }

        private async Task<bool> ScrollToNextPostAsync(Random rand, CancellationToken token)
        {
            double scrollChoice = rand.NextDouble();
            int scrollAmount;

            if (scrollChoice < 0.4)
            {
                scrollAmount = (int)(400 + rand.Next(0, 200));
            }
            else if (scrollChoice < 0.8)
            {
                scrollAmount = (int)(600 + rand.Next(0, 300));
            }
            else
            {
                scrollAmount = (int)(900 + rand.Next(0, 400));
            }

            var scrollScript = $@"
(function() {{
  const startY = window.scrollY || window.pageYOffset;
  const targetY = startY + {scrollAmount};
  const duration = 600 + Math.random() * 500;
  const startTime = performance.now();

  function scrollStep(currentTime) {{
    const elapsed = currentTime - startTime;
    const progress = Math.min(elapsed / duration, 1);
    const easeInOut = progress < 0.5 
      ? 2 * progress * progress 
      : 1 - Math.pow(-2 * progress + 2, 2) / 2;

    window.scrollTo(0, startY + (targetY - startY) * easeInOut);

    if (progress < 1) {{
      requestAnimationFrame(scrollStep);
    }}
  }}

  requestAnimationFrame(scrollStep);
  return 'SCROLLED';
}})();
";

            var result = await webView.ExecuteScriptAsync(scrollScript);
            await Task.Delay(rand.Next(1500, 3500), token);

            return true;
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            await webView.EnsureCoreWebView2Async(null);

            try
            {
                await form.StartScriptAsync("ScrollHome");
                var localToken = form.GetCancellationToken();
                token = localToken;

                try
                {
                    logTextBox.AppendText($"[SCROLL HOME] Starting feed scroll...\r\n");

                    var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                    Directory.CreateDirectory(dataDir);

                    string groupName = !string.IsNullOrWhiteSpace(profile.GroupName)
                        ? profile.GroupName
                        : profile.Name;

                    string donePostsFileName = $"Done_Posts_{groupName}.txt";
                    donePostsPath = Path.Combine(dataDir, donePostsFileName);
                    donePostIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (File.Exists(donePostsPath))
                    {
                        var lines = File.ReadAllLines(donePostsPath)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(line => line.Trim());

                        donePostIds = new System.Collections.Generic.HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
                        logTextBox.AppendText($"[DONE_POSTS] Loaded {donePostIds.Count} posts from {donePostsFileName}\r\n");
                    }
                    else
                    {
                        File.Create(donePostsPath).Close();
                        logTextBox.AppendText($"[DONE_POSTS] Created {donePostsFileName}\r\n");
                    }

                    logTextBox.AppendText($"[GROUP] Using group: '{groupName}'\r\n");

                    webView.CoreWebView2.Navigate("https://www.instagram.com/");
                    await Task.Delay(rand.Next(3000, 6000), token);

                    bool isLoaded = false;
                    int loadRetries = 0;
                    while (!isLoaded && loadRetries < 5)
                    {
                        var loadCheck = await webView.ExecuteScriptAsync(@"
document.querySelector('article, main') ? 'true' : 'false';");
                        isLoaded = JsBoolIsTrue(loadCheck);
                        if (!isLoaded)
                        {
                            await Task.Delay(2000, token);
                            loadRetries++;
                        }
                    }

                    if (!isLoaded)
                    {
                        throw new Exception("Home feed failed to load.");
                    }

                    var langResult = await webView.ExecuteScriptAsync("document.documentElement.lang;");
                    var lang = langResult?.Trim('"') ?? "en";
                    logTextBox.AppendText($"[LANG] Detected language: {lang}\r\n");

                    string likeSelectors = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase)
                        ? @"svg[aria-label=""J\u2019aime""], svg[aria-label=""Je n\u2019aime plus""]"
                        : @"svg[aria-label=""Like""], svg[aria-label=""Unlike""]";
                    string unlikeTest = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase)
                        ? @"n\u2019aime plus"
                        : "unlike";
                    string publishPattern = lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase)
                        ? "publier|envoyer"
                        : "post|send";

                    var commentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "comments.txt");
                    var comments = new System.Collections.Generic.List<string>();
                    if (File.Exists(commentsPath))
                    {
                        comments = File.ReadAllLines(commentsPath)
                                           .Where(line => !string.IsNullOrWhiteSpace(line))
                                           .Select(line => line.Trim())
                                           .ToList();
                    }

                    if (!comments.Any())
                    {
                        logTextBox.AppendText("[COMMENTS] Using default comments\r\n");
                        comments = new string[] {
                            "Amazing! 🔥", "Love this! ❤️", "So cool! ✨",
                            "Impressive!", "Well done! 👍", "Perfect! 🎯"
                        }.ToList();
                    }

                    int postNum = 0;
                    int reelsInteracted = 0;
                    int postsScrolled = 0;
                    int postsSkippedAlreadyCommented = 0;

                    while (!token.IsCancellationRequested)
                    {
                        if (postNum > 0 && postNum % rand.Next(15, 26) == 0)
                        {
                            int microBreak = rand.Next(120000, 300000);
                            logTextBox.AppendText($"[BREAK] Taking {microBreak / 60000}min break...\r\n");
                            await Task.Delay(microBreak, token);
                        }

                        postNum++;

                        try
                        {
                            logTextBox.AppendText($"\r\n[POST {postNum}] Analyzing...\r\n");

                            var postId = await ExtractPostIdAsync(token);
                            logTextBox.AppendText($"[POST {postNum}] ID: {postId}\r\n");

                            if (IsPostAlreadyCommented(postId))
                            {
                                postsSkippedAlreadyCommented++;
                                logTextBox.AppendText($"[POST {postNum}] ⚠️ Already commented by group '{groupName}', skipping...\r\n");

                                int skipDelay = rand.Next(800, 2000);
                                await Task.Delay(skipDelay, token);
                                await RandomHumanNoiseAsync(token);
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

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
                            string postCaption = await ExtractPostCaptionAsync(token);

                            if (!string.IsNullOrWhiteSpace(postCaption))
                            {
                                detectedLanguage = await languageDetector.DetectLanguageAsync(postCaption);
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
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

                            logTextBox.AppendText($"[FILTER] ✓ Perfect match (niche + language: {detectedLanguage})\r\n");

                            var (found, datetime, text, ageHours, isReel) = await ExtractPostInfoAsync(token);

                            if (!found)
                            {
                                logTextBox.AppendText($"[POST {postNum}] Could not extract info, skipping...\r\n");
                                await Task.Delay(rand.Next(1000, 2000), token);
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

                            var (creator, commentCount) = await ExtractCreatorAndCommentsAsync(token);
                            if (!string.IsNullOrWhiteSpace(creator) &&
                                creator != "NO_ARTICLE" &&
                                creator != "NO_CREATOR" &&
                                creator != "ERROR")
                            {
                                var futureTargetsPath = Path.Combine(dataDir, "FutureTargets.txt");
                                AddToFutureTargets(creator, commentCount, futureTargetsPath);
                            }

                            string postType = isReel ? "Reel" : "Static";
                            logTextBox.AppendText($"[POST {postNum}] Type: {postType}, Comments: {commentCount}\r\n");

                            bool shouldComment = false;
                            bool shouldSkip = false;

                            if (ageHours >= 0)
                            {
                                logTextBox.AppendText($"[AGE] {ageHours:F1}h\r\n");

                                if (ageHours <= config.MaxPostAgeHours)
                                {
                                    shouldComment = true;
                                    logTextBox.AppendText($"[DECISION] ≤ {config.MaxPostAgeHours}h → WILL COMMENT\r\n");
                                }
                                else
                                {
                                    shouldComment = false;
                                    shouldSkip = (rand.NextDouble() < 0.80);
                                    logTextBox.AppendText($"[DECISION] > {config.MaxPostAgeHours}h → {(shouldSkip ? "SKIP (80%)" : "NO COMMENT")}\r\n");
                                }
                            }
                            else
                            {
                                logTextBox.AppendText("[AGE] Unknown age\r\n");
                            }

                            if (shouldSkip)
                            {
                                int skipDelay = rand.Next(800, 2000);
                                await Task.Delay(skipDelay, token);
                                await RandomHumanNoiseAsync(token);
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

                            // Get watch time using consistent logic
                            int watchTime = GetWatchTime(rand, isPerfectMatch, isReel);
                            logTextBox.AppendText($"[WATCH] {(isPerfectMatch ? "🎯 Perfect match" : "⚡ Skip")} {postType} → {watchTime / 1000}s\r\n");
                            await Task.Delay(watchTime, token);

                            // Long pause for reels (5% chance)
                            if (isPerfectMatch && isReel && await ShouldTakeLongPause(rand))
                            {
                                await TakeLongPauseWithVideo(rand, token);
                            }

                            // Calculate like probability using GetLikeProbability
                            double likeProbability = GetLikeProbability(watchTime, commentCount);
                            bool shouldLike = isPerfectMatch && rand.NextDouble() < likeProbability;

                            logTextBox.AppendText($"[LIKE] Probability: {likeProbability:P0} → {(shouldLike ? "WILL LIKE" : "SKIP")}\r\n");

                            if (shouldLike)
                            {
                                var likeTry = await LikeCurrentPostAsync(likeSelectors, unlikeTest, token);
                                logTextBox.AppendText($"[LIKE] ❤️ {likeTry}\r\n");
                                await Task.Delay(rand.Next(1500, 3000), token);
                            }

                            // Calculate profile visit probability
                            double profileVisitProbability = GetProfileVisitProbability(watchTime, commentCount);
                            bool shouldVisitProfile = isPerfectMatch && rand.NextDouble() < profileVisitProbability;

                            logTextBox.AppendText($"[PROFILE] Probability: {profileVisitProbability:P0} → {(shouldVisitProfile ? "WILL VISIT" : "SKIP")}\r\n");

                            if (shouldVisitProfile)
                            {
                                await VisitCreatorProfileAsync(rand, token);
                            }

                            if (shouldComment)
                            {
                                if (isReel) reelsInteracted++;

                                string randomComment = comments[rand.Next(comments.Count)];
                                await CommentOnCurrentPostAsync(randomComment, publishPattern, token);

                                await MarkPostAsDoneThreadSafe(postId, token);
                            }
                            else
                            {
                                logTextBox.AppendText($"[COMMENT] Skipped: {(ageHours >= 0 ? $"{ageHours:F1}h old" : "unknown age")}\r\n");
                            }

                            await RandomHumanNoiseAsync(token);
                            await ScrollToNextPostAsync(rand, token);
                            postsScrolled++;

                        }
                        catch (OperationCanceledException)
                        {
                            logTextBox.AppendText("[SCROLL HOME] Cancelled by user.\r\n");
                            break;
                        }
                        catch (Exception ex)
                        {
                            logTextBox.AppendText($"[POST EXCEPTION] {ex.Message}\r\n");
                            Logger.LogError($"ScrollHomeService.RunAsync post loop: {ex}");
                            await Task.Delay(rand.Next(2000, 5000), token);
                        }
                    }

                    logTextBox.AppendText($"\r\n[SCROLL HOME] ✓ Session completed:\r\n");
                    logTextBox.AppendText($"  - Posts scrolled: {postsScrolled}\r\n");
                    logTextBox.AppendText($"  - Reels commented: {reelsInteracted}\r\n");
                    logTextBox.AppendText($"  - Posts skipped (already commented): {postsSkippedAlreadyCommented}\r\n");
                }
                catch (OperationCanceledException)
                {
                    logTextBox.AppendText("[SCROLL HOME] Script cancelled.\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                    Logger.LogError($"ScrollHomeService.RunAsync/inner: {ex}");
                }
                finally
                {
                    form.ScriptCompleted();
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                Logger.LogError($"ScrollHomeService.RunAsync: {ex}");
            }
        }
    }
}