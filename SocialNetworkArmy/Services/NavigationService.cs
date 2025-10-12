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

        
        public async Task<bool> NavigateToProfileViaSearchAsync(string profileUsername, CancellationToken token = default)
        {
            try
            {
                logTextBox.AppendText($"[NAV] Navigating to profile '{profileUsername}' via search...\r\n");

                // Délai pré-action aléatoire
                int preDelay = rand.Next(800, 2000);
                logTextBox.AppendText($"[NAV] Waiting {preDelay}ms before search...\r\n");
                await Task.Delay(preDelay, token);

                // ========== ÉTAPE 1: CLIQUER SUR LA LOUPE (CORRIGÉ) ==========
                if (string.IsNullOrEmpty(searchLabel))
                {
                    await DetectLanguageAsync(token);
                }

                logTextBox.AppendText($"[NAV] Clicking Search button...\r\n");

                var searchScript = $@"
(function(){{
  try {{
    function isVisible(el) {{
      return el && el.offsetWidth > 0 && el.offsetHeight > 0;
    }}
    function isInSidebar(el) {{
      while (el && el !== document.body) {{
        if (el.getAttribute('role') === 'dialog' || el.closest('[aria-label*=""Saisie de la recherche""]')) return false;
        el = el.parentElement;
      }}
      return true;
    }}

    // 🔍 Sélectionne UNIQUEMENT la loupe de la barre latérale principale
    const candidates = Array.from(document.querySelectorAll('svg[aria-label=""Recherche""], svg[aria-label=""Search""]'));
    let searchEl = candidates
      .map(svg => svg.closest('a[role=""link""], div[role=""button""]'))
      .find(el => el && isVisible(el) && isInSidebar(el));

    if (!searchEl) return 'NO_SIDEBAR_SEARCH_FOUND';

    // Clique naturel
    const rect = searchEl.getBoundingClientRect();
    const marginX = rect.width * 0.2;
    const marginY = rect.height * 0.2;
    const offsetX = marginX + Math.random() * (rect.width - 2 * marginX);
    const offsetY = marginY + Math.random() * (rect.height - 2 * marginY);
    const clientX = rect.left + offsetX;
    const clientY = rect.top + offsetY;
    const opts = {{ bubbles:true, cancelable:true, clientX, clientY, button:0 }};

    searchEl.scrollIntoView({{behavior:'smooth', block:'center'}});
    searchEl.dispatchEvent(new MouseEvent('mousedown', opts));
    searchEl.dispatchEvent(new MouseEvent('mouseup', opts));
    searchEl.dispatchEvent(new MouseEvent('click', opts));

    return 'SEARCH_CLICKED:' + Math.round(clientX) + ',' + Math.round(clientY);
  }} catch(e) {{
    return 'ERR:' + (e.message || String(e));
  }}
}})()";



                var searchTry = await webView.ExecuteScriptAsync(searchScript);
                logTextBox.AppendText($"[NAV] Search click: {searchTry}\r\n");
                // 🔎 Attente que la vraie barre de recherche de profils soit visible (et non celle des DMs)
                logTextBox.AppendText("[NAV] Waiting for profile search input to become visible...\r\n");

                var waitVisible = await webView.ExecuteScriptAsync(@"
(function(){
  return new Promise(resolve=>{
    let tries = 0;
    const check = () => {
      const input = document.querySelector('nav input[placeholder*=""echercher"" i], nav input[aria-label*=""Saisie de la recherche"" i], div[role=""dialog""] input[placeholder*=""echercher"" i]');
      if (input) {
        const r = input.getBoundingClientRect();
        const cs = getComputedStyle(input);
        if (r.width > 0 && r.height > 0 && cs.display !== 'none' && cs.visibility !== 'hidden') {
          input.setAttribute('data-correct-profile-search', 'true');
          resolve('VISIBLE');
          return;
        }
      }
      if (++tries > 30) {
        resolve('TIMEOUT');
        return;
      }
      setTimeout(check, 150);
    };
    check();
  });
})();");

                logTextBox.AppendText($"[NAV] Input visibility check: {waitVisible}\r\n");

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
  // Vérifie si on est soit dans /explore/, soit dans la vue DM avec input recherche
  const url = window.location.href;
  const hasExplore = url.includes('/explore');
  const dmInput = document.querySelector('input[aria-label*=""Saisie de la recherche"" i], input[placeholder*=""echercher"" i]');
  const sidebarActive = !!document.activeElement && document.activeElement.tagName.toLowerCase() === 'input';
  return (hasExplore || dmInput || sidebarActive) ? 'true' : 'false';
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
(async function() {{
  try {{
    const sleep = (ms) => new Promise(r => setTimeout(r, ms));
    function randomDelay(min, max) {{
      return Math.floor(min + Math.random() * (max - min + 1));
    }}

    // 1️⃣ Trouver le bon champ de saisie (logique TestService)
    let active = document.activeElement;
    if (!active || active.tagName.toLowerCase() !== 'input') {{
      const allInputs = Array.from(document.querySelectorAll('input[placeholder*=""echercher"" i], input[type=""text""]'));
      // Barre de recherche du menu latéral = visible et dans la partie haute
      active = allInputs.find(i => 
        i.offsetTop < window.innerHeight * 0.5 && 
        i.offsetWidth > 0 && i.offsetHeight > 0 && 
        getComputedStyle(i).visibility !== 'hidden' && 
        getComputedStyle(i).display !== 'none'
      );
    }}

    if (!active) return 'NO_VISIBLE_INPUT';
    active.focus();
    active.scrollIntoView({{behavior:'smooth', block:'center'}});

    const text = '{escapedSearch}';
    const chars = Array.from(text);

    // 2️⃣ Frappe humanisée (delay, erreurs, corrections)
    for (let i = 0; i < chars.length; i++) {{
      const c = chars[i];
      const val = active.value;
      const proto = HTMLInputElement.prototype;
      const desc = Object.getOwnPropertyDescriptor(proto, 'value');
      desc.set.call(active, val + c);
      active.dispatchEvent(new Event('input', {{bubbles:true}}));
      active.dispatchEvent(new Event('change', {{bubbles:true}}));

      // délai variable selon le caractère
      let delay;
      if (c === ',' || c === ';') delay = randomDelay(300, 500);
      else if ('.!?'.includes(c)) delay = randomDelay(400, 600);
      else if (c === ' ') delay = randomDelay(150, 250);
      else delay = randomDelay(90, 180);
      await sleep(delay);

      // 5% de chances de taper une faute + corriger
      if (Math.random() < 0.05 && i < chars.length - 1) {{
        const wrongChars = 'azertyuiopqsdfghjklmwxcvbn';
        const wrong = wrongChars[Math.floor(Math.random() * wrongChars.length)];
        const val2 = active.value;
        desc.set.call(active, val2 + wrong);
        active.dispatchEvent(new Event('input', {{bubbles:true}}));
        await sleep(randomDelay(200, 300));
        // backspace (supprime le caractère erroné)
        desc.set.call(active, active.value.slice(0, -1));
        active.dispatchEvent(new Event('input', {{bubbles:true}}));
        await sleep(randomDelay(150, 250));
      }}

      // Petites pauses naturelles
      if (Math.random() < 0.03) await sleep(randomDelay(400, 800));
    }}

    await sleep(randomDelay(400, 700));
    return 'TYPED_SUCCESSFULLY';
  }} catch(e) {{
    return 'ERR_TYPING:' + (e.message || e);
  }}
}})();";


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