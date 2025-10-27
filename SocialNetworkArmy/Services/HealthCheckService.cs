using Microsoft.Web.WebView2.WinForms;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Service de vérification de santé pour s'assurer que tout fonctionne correctement.
    /// Vérifie: Internet, WebView, Instagram accessible, session active.
    /// </summary>
    public class HealthCheckService
    {
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public HealthCheckService(WebView2 webView, TextBox logTextBox)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
        }

        /// <summary>
        /// Résultat complet du health check.
        /// </summary>
        public class HealthCheckResult
        {
            public bool IsHealthy => InternetConnected && WebViewReady && InstagramAccessible;
            public bool InternetConnected { get; set; }
            public bool WebViewReady { get; set; }
            public bool InstagramAccessible { get; set; }
            public bool IsLoggedIn { get; set; }
            public string ErrorMessage { get; set; }
            public int ResponseTimeMs { get; set; }
        }

        /// <summary>
        /// Effectue un health check complet du système.
        /// </summary>
        public async Task<HealthCheckResult> PerformHealthCheckAsync()
        {
            var result = new HealthCheckResult();
            var startTime = DateTime.Now;

            logTextBox.AppendText("\r\n");
            logTextBox.AppendText("╔════════════════════════════════════╗\r\n");
            logTextBox.AppendText("║     HEALTH CHECK STARTING...       ║\r\n");
            logTextBox.AppendText("╚════════════════════════════════════╝\r\n");

            try
            {
                // 1. Vérifier la connexion Internet
                logTextBox.AppendText("[HEALTH] Checking internet connection...\r\n");
                result.InternetConnected = await CheckInternetConnectionAsync();
                LogStatus("Internet", result.InternetConnected);

                if (!result.InternetConnected)
                {
                    result.ErrorMessage = "No internet connection";
                    LogSummary(result);
                    return result;
                }

                // 2. Vérifier que WebView2 est prêt
                logTextBox.AppendText("[HEALTH] Checking WebView2 status...\r\n");
                result.WebViewReady = CheckWebViewReady();
                LogStatus("WebView2", result.WebViewReady);

                if (!result.WebViewReady)
                {
                    result.ErrorMessage = "WebView2 not ready or crashed";
                    LogSummary(result);
                    return result;
                }

                // 3. Vérifier qu'Instagram est accessible
                logTextBox.AppendText("[HEALTH] Checking Instagram accessibility...\r\n");
                result.InstagramAccessible = await CheckInstagramAccessibleAsync();
                LogStatus("Instagram", result.InstagramAccessible);

                if (!result.InstagramAccessible)
                {
                    result.ErrorMessage = "Instagram is not accessible (blocked or down)";
                    LogSummary(result);
                    return result;
                }

                // 4. Vérifier si l'utilisateur est connecté (optionnel, peut échouer)
                logTextBox.AppendText("[HEALTH] Checking login status...\r\n");
                result.IsLoggedIn = await CheckInstagramLoginAsync();
                LogStatus("Login Status", result.IsLoggedIn);

                // Calculer le temps de réponse
                result.ResponseTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
                logTextBox.AppendText($"[HEALTH] Response time: {result.ResponseTimeMs}ms\r\n");

                LogSummary(result);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Health check failed: {ex.Message}";
                logTextBox.AppendText($"[HEALTH] ✗ EXCEPTION: {ex.Message}\r\n");
                LogSummary(result);
                return result;
            }
        }

        /// <summary>
        /// Vérifie si Internet est accessible en pingant Google DNS.
        /// </summary>
        private async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                var response = await httpClient.GetAsync("https://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Essayer un backup (Cloudflare DNS)
                try
                {
                    var response = await httpClient.GetAsync("https://1.1.1.1");
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Vérifie que WebView2 est prêt et fonctionnel.
        /// </summary>
        private bool CheckWebViewReady()
        {
            try
            {
                return webView != null &&
                       !webView.IsDisposed &&
                       webView.CoreWebView2 != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Vérifie qu'Instagram.com est accessible.
        /// </summary>
        private async Task<bool> CheckInstagramAccessibleAsync()
        {
            try
            {
                var response = await httpClient.GetAsync("https://www.instagram.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Vérifie si l'utilisateur est connecté à Instagram.
        /// </summary>
        private async Task<bool> CheckInstagramLoginAsync()
        {
            try
            {
                if (!CheckWebViewReady())
                    return false;

                var checkScript = @"
(function(){
    try {
        // Vérifier plusieurs indicateurs de connexion
        const hasProfileLink = !!document.querySelector('a[href*=""/accounts/edit/""]');
        const hasUserMenu = !!document.querySelector('[aria-label*=""Profile""]') ||
                           !!document.querySelector('a[href=""#""]');
        const noLoginButton = !document.querySelector('a[href*=""/accounts/login/""]');

        return (hasProfileLink || hasUserMenu || noLoginButton).toString();
    } catch(e) {
        return 'false';
    }
})()";

                var result = await webView.CoreWebView2.ExecuteScriptAsync(checkScript);
                return result?.Trim('"').ToLower() == "true";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Log le statut d'un check individuel.
        /// </summary>
        private void LogStatus(string checkName, bool passed)
        {
            string emoji = passed ? "✓" : "✗";
            string status = passed ? "PASSED" : "FAILED";
            logTextBox.AppendText($"[HEALTH] {emoji} {checkName}: {status}\r\n");
        }

        /// <summary>
        /// Log le résumé final du health check.
        /// </summary>
        private void LogSummary(HealthCheckResult result)
        {
            logTextBox.AppendText("\r\n");
            logTextBox.AppendText("╔════════════════════════════════════╗\r\n");
            if (result.IsHealthy)
            {
                logTextBox.AppendText("║     ✓ ALL SYSTEMS OPERATIONAL     ║\r\n");
                logTextBox.AppendText("╠════════════════════════════════════╣\r\n");
                logTextBox.AppendText($"║ Response Time: {result.ResponseTimeMs,4}ms            ║\r\n");
                if (result.IsLoggedIn)
                    logTextBox.AppendText("║ Login Status: ACTIVE ✓             ║\r\n");
            }
            else
            {
                logTextBox.AppendText("║     ✗ HEALTH CHECK FAILED          ║\r\n");
                logTextBox.AppendText("╠════════════════════════════════════╣\r\n");
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    // Wrapper le message d'erreur sur plusieurs lignes si nécessaire
                    var errorLines = WrapText(result.ErrorMessage, 34);
                    foreach (var line in errorLines)
                    {
                        logTextBox.AppendText($"║ {line,-34} ║\r\n");
                    }
                }
            }
            logTextBox.AppendText("╚════════════════════════════════════╝\r\n");
            logTextBox.AppendText("\r\n");
        }

        /// <summary>
        /// Wrapper le texte pour l'affichage dans les boxes.
        /// </summary>
        private string[] WrapText(string text, int maxWidth)
        {
            var lines = new System.Collections.Generic.List<string>();
            var words = text.Split(' ');
            var currentLine = "";

            foreach (var word in words)
            {
                if ((currentLine + " " + word).Length > maxWidth)
                {
                    if (!string.IsNullOrEmpty(currentLine))
                        lines.Add(currentLine.Trim());
                    currentLine = word;
                }
                else
                {
                    currentLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine.Trim());

            return lines.ToArray();
        }

        /// <summary>
        /// Effectue un health check rapide (juste Internet + WebView).
        /// </summary>
        public async Task<bool> QuickHealthCheckAsync()
        {
            try
            {
                var internetOk = await CheckInternetConnectionAsync();
                var webViewOk = CheckWebViewReady();
                return internetOk && webViewOk;
            }
            catch
            {
                return false;
            }
        }
    }
}
