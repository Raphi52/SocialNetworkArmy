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

    // Try to find caption text
    // Strategy 1: Look for h1 (usually contains the caption)
    const h1 = targetArticle.querySelector('h1');
    if (h1 && h1.textContent && h1.textContent.trim().length > 0) {
      return h1.textContent.trim();
    }

    // Strategy 2: Look for spans with caption-like content
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

                // ⭐ Filtre minimum commentaires (config)
                if (comments < config.MinCommentsToAddToFutureTargets)
                {
                    logTextBox.AppendText($"[TARGET] ⊘ Skipped '{creator}' ({comments} comments < {config.MinCommentsToAddToFutureTargets})\r\n");
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
                else
                {
                    logTextBox.AppendText($"[TARGET] ℹ️ '{creator}' already in FutureTargets.txt ({comments} comments)\r\n");
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
            return rand.NextDouble() < 0.08;
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

        private async Task RandomHumanNoiseAsync(CancellationToken token)
        {
            if (rand.NextDouble() < 0.35)
            {
                logTextBox.AppendText("[HUMAN NOISE] Adding idle scroll or hover...\r\n");

                var noiseScript = @"
(async function(){
  const sleep = (ms) => new Promise(r => setTimeout(r, ms));

  if (Math.random() < 0.30) {
    const scrollUpAmount = -(Math.random() * 200 + 100);
    window.scrollBy({
      top: scrollUpAmount,
      behavior: 'smooth'
    });
    await sleep(800 + Math.random() * 700);
  } else {
    window.scrollBy({
      top: Math.random() * 150 - 75,
      behavior: 'smooth'
    });
    await sleep(500);
  }

  var elements = document.querySelectorAll('a, button, div[role=""button""], article');
  if (elements.length > 0) {
    var randomEl = elements[Math.floor(Math.random() * Math.min(elements.length, 20))];
    var rect = randomEl.getBoundingClientRect();
    if (rect.top >= 0 && rect.bottom <= window.innerHeight) {
      var x = rect.left + rect.width / 2;
      var y = rect.top + rect.height / 2;
      randomEl.dispatchEvent(new MouseEvent('mouseover', {bubbles: true, clientX: x, clientY: y}));
      await sleep(Math.random() * 1500 + 500);
      randomEl.dispatchEvent(new MouseEvent('mouseleave', {bubbles: true, clientX: x, clientY: y}));
    }
  }
  return 'NOISE_ADDED';
})()";
                var noiseResult = await webView.ExecuteScriptAsync(noiseScript);
                logTextBox.AppendText($"[NOISE] {noiseResult}\r\n");
            }
        }

        private async Task RandomScrollUpAsync(CancellationToken token)
        {
            if (rand.NextDouble() < 0.12)
            {
                int scrollUpAmount = rand.Next(300, 800);
                logTextBox.AppendText($"[SCROLL UP] Scrolling up {scrollUpAmount}px (re-checking content)...\r\n");

                var scrollUpScript = $@"
(function() {{
  const startY = window.scrollY || window.pageYOffset;
  const targetY = Math.max(0, startY - {scrollUpAmount});
  const duration = 800 + Math.random() * 600;
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
  return 'SCROLLED_UP';
}})();";

                var result = await webView.ExecuteScriptAsync(scrollUpScript);
                logTextBox.AppendText($"[SCROLL UP] {result}\r\n");

                int pauseAfterScrollUp = rand.Next(2000, 5000);
                await Task.Delay(pauseAfterScrollUp, token);

                logTextBox.AppendText("[SCROLL DOWN] Resuming feed...\r\n");
                await ScrollToNextPostAsync(rand, token);
            }
        }

        private async Task<bool> ScrollToNextPostAsync(Random rand, CancellationToken token)
        {
            logTextBox.AppendText("[SCROLL] Scrolling to next post...\r\n");

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
            logTextBox.AppendText($"[SCROLL] {result} ({scrollAmount}px)\r\n");

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
                    var startTime = DateTime.Now;
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
                            logTextBox.AppendText($"[POST {postNum}] Analyzing...\r\n");

                            var postId = await ExtractPostIdAsync(token);
                            logTextBox.AppendText($"[POST {postNum}] ID: {postId}\r\n");

                            if (IsPostAlreadyCommented(postId))
                            {
                                postsSkippedAlreadyCommented++;
                                logTextBox.AppendText($"[POST {postNum}] ⚠️ Already commented by group '{groupName}', skipping...\r\n");

                                int skipDelay = rand.Next(800, 2000);
                                await Task.Delay(skipDelay, token);
                                await RandomHumanNoiseAsync(token);
                                await RandomScrollUpAsync(token);
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

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
                            string postCaption = await ExtractPostCaptionAsync(token);

                            if (!string.IsNullOrWhiteSpace(postCaption))
                            {
                                detectedLanguage = await languageDetector.DetectLanguageAsync(postCaption);
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
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

                            logTextBox.AppendText($"[FILTER] ✓ Content passed all filters (niche: {(config.ShouldApplyNicheFilter() ? "female" : "any")}, lang: {detectedLanguage})\r\n");

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
                            if (!string.IsNullOrWhiteSpace(creator) && creator != "NO_ARTICLE" && creator != "NO_CREATOR" && creator != "ERROR")
                            {
                                var futureTargetsPath = Path.Combine(dataDir, "FutureTargets.txt");
                                AddToFutureTargets(creator, commentCount, futureTargetsPath);
                            }
                            string postType = isReel ? "Reel" : "Static";
                            logTextBox.AppendText($"[POST {postNum}] Type: {postType}\r\n");
                            logTextBox.AppendText($"[DATE] datetime={datetime}, text={text}\r\n");

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
                                logTextBox.AppendText($"[SKIP] Waiting {skipDelay}ms...\r\n");
                                await Task.Delay(skipDelay, token);
                                await RandomHumanNoiseAsync(token);
                                await ScrollToNextPostAsync(rand, token);
                                postsScrolled++;
                                continue;
                            }

                            int watchTime;
                            if (isReel)
                            {
                                watchTime = await GetHumanWatchTime(rand);

                                // 🎯 BONUS: +30-50% temps si femme + langue correspondent (éduquer l'algo)
                                if (passedNicheFilter && passedLanguageFilter &&
                                    config.ShouldApplyNicheFilter() && !config.IsLanguageTargeted("Any"))
                                {
                                    int bonus = (int)(watchTime * (0.30 + rand.NextDouble() * 0.20)); // +30-50%
                                    watchTime += bonus;
                                    logTextBox.AppendText($"[WATCH] 🎯 Perfect match (Female + {detectedLanguage}) → +{bonus / 1000}s bonus!\r\n");
                                }

                                logTextBox.AppendText($"[WATCH] Reel - {watchTime / 1000}s...\r\n");
                                await Task.Delay(watchTime, token);

                                if (await ShouldTakeLongPause(rand))
                                {
                                    await TakeLongPauseWithVideo(rand, token);
                                }
                            }
                            else
                            {
                                watchTime = rand.Next(1500, 4000);

                                // 🎯 BONUS: +30-50% temps si femme + langue correspondent (éduquer l'algo)
                                if (passedNicheFilter && passedLanguageFilter &&
                                    config.ShouldApplyNicheFilter() && !config.IsLanguageTargeted("Any"))
                                {
                                    int bonus = (int)(watchTime * (0.30 + rand.NextDouble() * 0.20)); // +30-50%
                                    watchTime += bonus;
                                    logTextBox.AppendText($"[WATCH] 🎯 Perfect match (Female + {detectedLanguage}) → +{bonus}ms bonus!\r\n");
                                }

                                logTextBox.AppendText($"[WATCH] Static - {watchTime}ms...\r\n");
                                await Task.Delay(watchTime, token);
                            }

                            double likeChance = isReel ? 0.08 : 0.03;
                            if (rand.NextDouble() < likeChance)
                            {
                                var likeTry = await LikeCurrentPostAsync(likeSelectors, unlikeTest, token);
                                logTextBox.AppendText($"[LIKE] {likeTry}\r\n");
                                await Task.Delay(rand.Next(1500, 3000), token);
                            }
                            else
                            {
                                logTextBox.AppendText($"[LIKE] Skipped (random {(likeChance * 100):F0}%)\r\n");
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

                    logTextBox.AppendText($"[SCROLL HOME] ✓ Session completed:\r\n");
                    logTextBox.AppendText($"  - Posts scrolled: {postsScrolled}\r\n");
                    logTextBox.AppendText($"  - Reels commented: {reelsInteracted}\r\n");
                    logTextBox.AppendText($"  - Posts skipped (already commented by group): {postsSkippedAlreadyCommented}\r\n");
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