using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.Linq;
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
        private readonly Random _rng = new Random();

        public TestService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
        }

        private void Log(string message)
        {
            logTextBox?.AppendText($"[TEST] {message}{Environment.NewLine}");
        }

        private async Task<string> ExecJsAsync(string js, CancellationToken token)
        {
            var res = await webView.ExecuteScriptAsync(js);

            if (res != null && res.StartsWith("\"") && res.EndsWith("\""))
            {
                res = res.Substring(1, res.Length - 2);
                res = res.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "");
            }

            return res ?? "ERROR_NO_RESPONSE";
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            Log("Test de saisie dans la vraie barre de recherche DM...");

            string js = @"
(async function() {
  try {
    const sleep = (ms) => new Promise(r => setTimeout(r, ms));

    // 1️⃣ Trouver la loupe (celle du menu gauche)
    const searchBtn = Array.from(document.querySelectorAll('svg[aria-label=""Recherche""], svg[aria-label=""Search""]'))
      .map(svg => svg.closest('a[role=""link""], div[role=""button""]'))
      .find(el => el && el.offsetWidth > 0 && el.offsetHeight > 0 && el.querySelector('svg'));

    if (!searchBtn) return 'LOUPE_NON_TROUVÉE';

    searchBtn.scrollIntoView({behavior:'smooth', block:'center'});
    searchBtn.dispatchEvent(new MouseEvent('mousedown', {bubbles:true, cancelable:true, button:0}));
    searchBtn.dispatchEvent(new MouseEvent('mouseup', {bubbles:true, cancelable:true, button:0}));
    searchBtn.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true, button:0}));

    await sleep(600);

    // 2️⃣ Vérifier le focus actif (souvent le champ de recherche DM)
    let active = document.activeElement;
    if (!active || active.tagName.toLowerCase() !== 'input') {
      // fallback : chercher un input dans la zone visible du haut
      const allInputs = Array.from(document.querySelectorAll('input[placeholder*=""echercher"" i], input[type=""text""]'));
      active = allInputs.find(i => i.offsetTop < window.innerHeight * 0.5 && i.offsetWidth > 0 && i.offsetHeight > 0);
    }

    if (!active) return 'AUCUN_INPUT_VISIBLE';

    active.style.outline = '3px solid red';
    await sleep(300);

    // 3️⃣ Simulation de frappe réaliste
    const message = 'reza428553';
    for (const c of message) {
      const val = active.value;
      const proto = HTMLInputElement.prototype;
      const desc = Object.getOwnPropertyDescriptor(proto, 'value');
      desc.set.call(active, val + c);
      active.dispatchEvent(new Event('input', {bubbles:true}));
      active.dispatchEvent(new Event('change', {bubbles:true}));
      await sleep(90 + Math.random() * 60);
    }

    await sleep(500);
    active.style.outline = '';
    return 'TYPING_OK';
  } catch(e) {
    return 'ERREUR_JS: ' + (e.message || String(e));
  }
})();";

            var result = await ExecJsAsync(js, token);
            Log($"Résultat du test : {result}");
        }



    }
}