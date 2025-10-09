using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class NavigationService
    {
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Random rand;
        private string detectedLang;
        private string searchLabel;

        public NavigationService(WebView2 webView, TextBox logTextBox)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.rand = new Random();
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
        /// Détecte la langue d'Instagram et initialise les labels
        /// </summary>
        public async Task<string> DetectLanguageAsync(CancellationToken token = default)
        {
            var langResult = await webView.ExecuteScriptAsync("document.documentElement.lang;");
            detectedLang = langResult?.Trim('"') ?? "en";
            searchLabel = detectedLang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "Recherche" : "Search";

            logTextBox.AppendText($"[NAV] Detected language: {detectedLang}\r\n");
            return detectedLang;
        }

        /// <summary>
        /// Navigue vers la page d'accueil Instagram
        /// </summary>
        public async Task NavigateToHomeAsync(CancellationToken token = default)
        {
            var currentUrl = await webView.ExecuteScriptAsync("window.location.href;");
            if (!currentUrl.Contains("instagram.com"))
            {
                logTextBox.AppendText("[NAV] Navigating to Instagram home...\r\n");
                webView.CoreWebView2.Navigate("https://www.instagram.com/");
                await Task.Delay(rand.Next(3000, 5000), token);
            }
        }

        /// <summary>
        /// Clique sur le bouton Home pour revenir à l'accueil
        /// </summary>
        public async Task<bool> ClickHomeButtonAsync(CancellationToken token = default)
        {
            logTextBox.AppendText("[NAV] Clicking Home button...\r\n");

            var homeScript = @"
(function(){
  try{
    var homeEl = null;
    
    var svgs = Array.from(document.querySelectorAll('svg[aria-label]'));
    var homeSvg = svgs.find(function(svg){ return /accueil|home/i.test(svg.getAttribute('aria-label')); });
    if (homeSvg) {
      homeEl = homeSvg.closest('a, span[role=""link""]');
    }
    
    if (!homeEl) {
      homeEl = document.querySelector('a[href=""/""]');
    }
    
    if (!homeEl) return 'NO_HOME_ELEMENT';
    
    var rect = homeEl.getBoundingClientRect();
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    var clientX = rect.left + offsetX;
    var clientY = rect.top + offsetY;
    
    // Simulate mouse approach: 3-5 move events towards the target
    var startX = clientX + (Math.random() * 100 - 50);  // Start offset
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      homeEl.dispatchEvent(new MouseEvent('mousemove', {bubbles: true, clientX: moveX, clientY: moveY}));
    }
    
    var opts = {bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0};
    
    homeEl.dispatchEvent(new MouseEvent('mousedown', opts));
    homeEl.dispatchEvent(new MouseEvent('mouseup', opts));
    homeEl.dispatchEvent(new MouseEvent('click', opts));
    
    return 'HOME_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY);
  } catch(e){
    return 'ERR:' + (e.message || String(e));
  }
})()";

            var homeResult = await webView.ExecuteScriptAsync(homeScript);
            logTextBox.AppendText($"[NAV] Home click result: {homeResult}\r\n");

            await Task.Delay(rand.Next(1500, 2500), token);
            return homeResult.Contains("HOME_CLICKED");
        }

        /// <summary>
        /// Navigue vers un profil en utilisant la recherche (méthode humaine)
        /// </summary>
        public async Task<bool> NavigateToProfileViaSearchAsync(string profileUsername, CancellationToken token = default)
        {
            try
            {
                logTextBox.AppendText($"[NAV] Navigating to profile '{profileUsername}' via search...\r\n");

                // Délai pré-action aléatoire
                int preDelay = rand.Next(800, 2000);
                logTextBox.AppendText($"[NAV] Waiting {preDelay}ms before search...\r\n");
                await Task.Delay(preDelay, token);

                // ========== ÉTAPE 1: CLIQUER SUR LA LOUPE ==========
                if (string.IsNullOrEmpty(searchLabel))
                {
                    await DetectLanguageAsync(token);
                }

                logTextBox.AppendText($"[NAV] Clicking Search button...\r\n");

                var searchScript = $@"
(function(){{
  try{{
    var searchEl = null;
    
    // Méthode 1: Par aria-label du SVG
    var svgs = Array.from(document.querySelectorAll('svg[aria-label]'));
    var searchSvg = svgs.find(function(svg){{ return /{searchLabel}/i.test(svg.getAttribute('aria-label')); }});
    if (searchSvg) {{
      searchEl = searchSvg.closest('a, span[role=""link""]');
    }}
    
    // Méthode 2: Par href=""/explore/""
    if (!searchEl) {{
      searchEl = document.querySelector('a[href=""/explore/""], a[href^=""/explore""]');
    }}
    
    // Méthode 3: Par texte ""Recherche"" ou ""Search""
    if (!searchEl) {{
      var allNavLinks = Array.from(document.querySelectorAll('a, span[role=""link""]'));
      searchEl = allNavLinks.find(function(el){{ return /{searchLabel}/i.test(el.innerText || el.textContent); }});
    }}
    
    if (!searchEl) return 'NO_SEARCH_ELEMENT';
    
    if (searchEl.offsetWidth === 0 || searchEl.offsetHeight === 0) {{
      return 'SEARCH_NOT_VISIBLE';
    }}
    
    var rect = searchEl.getBoundingClientRect();
    
    // Cliquer sur un pixel aléatoire dans le bouton (éviter les bords de 20%)
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    var clientX = rect.left + offsetX;
    var clientY = rect.top + offsetY;
    
    // Simulate mouse approach: 3-5 move events towards the target
    var startX = clientX + (Math.random() * 100 - 50);  // Start offset
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      searchEl.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    
    searchEl.dispatchEvent(new MouseEvent('mouseenter', opts));
    searchEl.dispatchEvent(new MouseEvent('mouseover', opts));
    searchEl.dispatchEvent(new MouseEvent('mousedown', opts));
    searchEl.dispatchEvent(new MouseEvent('mouseup', opts));
    searchEl.dispatchEvent(new MouseEvent('click', opts));
    searchEl.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    return 'SEARCH_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY);
  }} catch(e){{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";

                var searchTry = await webView.ExecuteScriptAsync(searchScript);
                logTextBox.AppendText($"[NAV] Search click: {searchTry}\r\n");

                if (!searchTry.Contains("SEARCH_CLICKED"))
                {
                    logTextBox.AppendText("[NAV] ✗ Failed to click search button\r\n");
                    return false;
                }

                // Attendre que la page de recherche se charge
                int loadDelay = rand.Next(2000, 4000);
                await Task.Delay(loadDelay, token);

                // Vérifier qu'on est sur la page de recherche
                var checkSearch = await webView.ExecuteScriptAsync(@"
(function(){
  var url = window.location.href;
  var hasExplore = url.includes('/explore');
  var hasSearchBox = !!document.querySelector('input[placeholder*=""herch""], input[placeholder*=""earch""]');
  return (hasExplore || hasSearchBox) ? 'true' : 'false';
})()");

                if (!JsBoolIsTrue(checkSearch))
                {
                    logTextBox.AppendText("[NAV] ✗ Search page did not load\r\n");
                    return false;
                }

                logTextBox.AppendText("[NAV] ✓ Search page loaded\r\n");

                // ========== ÉTAPE 2: TYPING HUMAIN ==========
                logTextBox.AppendText($"[NAV] Typing: '{profileUsername}'\r\n");

                var escapedSearch = profileUsername
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
  
  const dlg = document.querySelector('div[role=""dialog""]');
  const root = dlg || document;
  
  const text = '{escapedSearch}';
  const chars = Array.from(text);
  
  let ta = root.querySelector('input[placeholder*=""herch""], input[placeholder*=""earch""]');
  let ce = null;
  
  if (!ta) {{
    ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
    if (!ce) return 'NO_SEARCH_INPUT_INITIAL';
  }}
  
  const initialTarget = ta || ce;
  
  initialTarget.scrollIntoView({{behavior:'smooth', block:'center'}});
  await sleep(randomDelay(200, 400));
  
  const rect = initialTarget.getBoundingClientRect();
  const x = rect.left + rect.width / 2;
  const y = rect.top + rect.height / 2;
  const opts = {{bubbles:true, cancelable:true, clientX:x, clientY:y, button:0}};
  
  initialTarget.dispatchEvent(new MouseEvent('mousedown', opts));
  initialTarget.dispatchEvent(new MouseEvent('mouseup', opts));
  initialTarget.dispatchEvent(new MouseEvent('click', opts));
  
  await sleep(randomDelay(100, 250));
  initialTarget.focus();
  
  for (let i = 0; i < chars.length; i++) {{
    const char = chars[i];
    
    ta = root.querySelector('input[placeholder*=""herch""], input[placeholder*=""earch""]');
    ce = null;
    
    if (!ta) {{
      ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
      if (!ce) return 'NO_SEARCH_INPUT_AT_' + i;
    }}
    
    const currentTarget = ta || ce;
    
    try {{
      if (ta) {{
        const currentValue = ta.value;
        const proto = HTMLInputElement.prototype;
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
      delay = randomDelay(300, 500);
    }} else if (char === '.' || char === '!' || char === '?') {{
      delay = randomDelay(400, 600);
    }} else if (char === ' ') {{
      delay = randomDelay(150, 250);
    }} else {{
      delay = randomDelay(100, 300);
    }}
    
    if (Math.random() < 0.05 && i < chars.length - 1) {{
      await sleep(delay);
      
      const wrongChars = 'qwertyuiopasdfghjklzxcvbnm';
      const wrongChar = wrongChars[Math.floor(Math.random() * wrongChars.length)];
      
      ta = root.querySelector('input[placeholder*=""herch""], input[placeholder*=""earch""]');
      ce = null;
      
      if (!ta) {{
        ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
        if (!ce) return 'NO_SEARCH_INPUT_ERROR_AT_' + i;
      }}
      
      const currentTargetError = ta || ce;
      
      try {{
        if (ta) {{
          const currentValue = currentTargetError.value;
          const proto = HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(currentTargetError, currentValue + wrongChar);
          currentTargetError.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('insertText', false, wrongChar);
        }}
      }} catch(e) {{
        return 'ERROR_TYPE_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(200, 400));
      
      ta = root.querySelector('input[placeholder*=""herch""], input[placeholder*=""earch""]');
      ce = null;
      
      if (!ta) {{
        ce = root.querySelector('div[role=""textbox""][contenteditable=""true""]');
        if (!ce) return 'NO_SEARCH_INPUT_DELETE_AT_' + i;
      }}
      
      const currentTargetDelete = ta || ce;
      
      try {{
        if (ta) {{
          const currentValue = currentTargetDelete.value;
          const proto = HTMLInputElement.prototype;
          const desc = Object.getOwnPropertyDescriptor(proto, 'value');
          desc.set.call(currentTargetDelete, currentValue.slice(0, -1));
          currentTargetDelete.dispatchEvent(new Event('input', {{bubbles: true}}));
        }} else {{
          document.execCommand('delete', false);
        }}
      }} catch(e) {{
        return 'DELETE_ERROR_AT_' + i + ': ' + (e.message || String(e));
      }}
      
      await sleep(randomDelay(100, 200));
    }}
    
    if (Math.random() < 0.03) {{
      await sleep(randomDelay(500, 1000));
    }}
    
    await sleep(delay);
  }}
  
  await sleep(randomDelay(500, 800));
  
  return 'TYPED_SUCCESSFULLY';
}})()";

                int charCount = profileUsername.Length;
                int baseTime = charCount * 200;
                int punctuationCount = profileUsername.Count(c => ".!?,;".Contains(c));
                int punctuationDelay = punctuationCount * 400;
                int errorDelay = (int)(charCount * 0.05 * 600);
                int totalTime = baseTime + punctuationDelay + errorDelay + 4000;

                var typingTask = webView.ExecuteScriptAsync(typingScript);
                await Task.Delay(totalTime, token);

                var typingResult = await typingTask;
                logTextBox.AppendText($"[NAV] Typing result: {typingResult}\r\n");

                if (typingResult != "{}")
                {
                    logTextBox.AppendText("[NAV] ✗ Typing failed\r\n");
                    return false;
                }

                // Attendre les résultats
                await Task.Delay(rand.Next(1500, 2500), token);

                // ========== ÉTAPE 3: CLIQUER SUR LE PREMIER RÉSULTAT ==========
                logTextBox.AppendText($"[NAV] Clicking first result...\r\n");

                var lowerSearch = profileUsername.ToLowerInvariant();
                var clickFirstResultScript = $@"
(function(){{
  try{{
    var lowerSearch = '{lowerSearch}';
    var firstResult = null;
    
    var allLinks = document.querySelectorAll('a[role=""link""]');
    
    for (var i = 0; i < allLinks.length; i++) {{
      var link = allLinks[i];
      var href = link.getAttribute('href');
      
      if (href && href.match(/^\/[a-zA-Z0-9._]+\/?$/)) {{
        var hasImg = !!link.querySelector('img');
        var hasText = (link.innerText || '').trim().length > 0;
        
        if (hasImg && hasText) {{
          firstResult = link;
          break;
        }}
      }}
    }}
    
    if (!firstResult) {{
      var clickables = document.querySelectorAll('[role=""button""], span[role=""link""]');
      for (var i = 0; i < clickables.length; i++) {{
        var el = clickables[i];
        var text = (el.innerText || '').toLowerCase();
        if (text.includes(lowerSearch)) {{
          firstResult = el.closest('a') || el;
          break;
        }}
      }}
    }}
    
    if (!firstResult) return 'NO_RESULT_FOUND';
    
    if (firstResult.offsetWidth === 0 || firstResult.offsetHeight === 0) {{
      return 'RESULT_NOT_VISIBLE';
    }}
    
    firstResult.scrollIntoView({{behavior:'smooth', block:'center'}});
    
    var rect = firstResult.getBoundingClientRect();
    
    var marginX = rect.width * 0.2;
    var marginY = rect.height * 0.2;
    var offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    var offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    var clientX = rect.left + offsetX;
    var clientY = rect.top + offsetY;
    
    // Simulate mouse approach: 3-5 move events towards the target
    var startX = clientX + (Math.random() * 100 - 50);  // Start offset
    var startY = clientY + (Math.random() * 100 - 50);
    for (let i = 1; i <= 5; i++) {{
      var moveX = startX + (clientX - startX) * (i / 5);
      var moveY = startY + (clientY - startY) * (i / 5);
      firstResult.dispatchEvent(new MouseEvent('mousemove', {{bubbles: true, clientX: moveX, clientY: moveY}}));
    }}
    
    var opts = {{bubbles: true, cancelable: true, clientX: clientX, clientY: clientY, button: 0}};
    
    firstResult.dispatchEvent(new MouseEvent('mouseenter', opts));
    firstResult.dispatchEvent(new MouseEvent('mouseover', opts));
    firstResult.dispatchEvent(new MouseEvent('mousedown', opts));
    firstResult.dispatchEvent(new MouseEvent('mouseup', opts));
    firstResult.dispatchEvent(new MouseEvent('click', opts));
    firstResult.dispatchEvent(new MouseEvent('mouseleave', opts));
    
    return 'RESULT_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY) +
           '|HREF:' + (firstResult.href || 'NO_HREF');
  }} catch(e){{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";

                var clickResultTry = await webView.ExecuteScriptAsync(clickFirstResultScript);
                logTextBox.AppendText($"[NAV] First result click: {clickResultTry}\r\n");

                if (!clickResultTry.Contains("RESULT_CLICKED"))
                {
                    logTextBox.AppendText("[NAV] ✗ Failed to click result\r\n");
                    return false;
                }

                // Attendre le chargement du profil
                await Task.Delay(rand.Next(2000, 4000), token);

                // Vérifier qu'on est sur le profil
                var checkProfile = await webView.ExecuteScriptAsync(@"
(function(){
  var url = window.location.href;
  var isProfile = url.match(/instagram\.com\/[^\/]+\/?$/);
  return isProfile ? 'true' : 'false';
})()");

                if (JsBoolIsTrue(checkProfile))
                {
                    logTextBox.AppendText($"[NAV] ✓ Successfully navigated to profile '{profileUsername}'\r\n");
                    return true;
                }
                else
                {
                    logTextBox.AppendText("[NAV] ⚠ May not be on profile page\r\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[NAV] ✗ Navigation error: {ex.Message}\r\n");
                return false;
            }
        }

        /// <summary>
        /// Navigue directement vers l'URL d'un profil (méthode rapide mais moins humaine)
        /// </summary>
        public async Task NavigateToProfileDirectAsync(string profileUsername, CancellationToken token = default)
        {
            var profileUrl = $"https://www.instagram.com/{profileUsername}/";
            logTextBox.AppendText($"[NAV] Direct navigation to: {profileUrl}\r\n");

            webView.CoreWebView2.Navigate(profileUrl);
            await Task.Delay(rand.Next(3000, 5000), token);
        }
    }
}