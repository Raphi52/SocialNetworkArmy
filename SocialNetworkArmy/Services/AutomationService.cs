// Services/AutomationService.cs - Correction constructeur (ajoute Profile) et cleanup data kinds
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using static SocialNetworkArmy.Services.ProxyService;

namespace SocialNetworkArmy.Services
{
    public class AutomationService
    {
        private readonly FingerprintService fingerprintService;
        private readonly ProxyService proxyService;
        private readonly LimitsService limitsService;
        private readonly CleanupService cleanupService;
        private readonly MonitoringService monitoringService;
        private readonly Profile profile; // Ajouté pour constructeur

        // Constructeur corrigé : Ajoute Profile en paramètre
        public AutomationService(FingerprintService fingerprintService, ProxyService proxyService, LimitsService limitsService, CleanupService cleanupService, MonitoringService monitoringService, Profile profile)
        {
            this.fingerprintService = fingerprintService;
            this.proxyService = proxyService;
            this.limitsService = limitsService;
            this.cleanupService = cleanupService;
            this.monitoringService = monitoringService;
            this.profile = profile; // Stocké pour cleanup
        }

        public async Task<WebView2> InitializeBrowserAsync(Profile profile, string userDataDir)
        {
            var webView = new WebView2();
            var envOptions = new CoreWebView2EnvironmentOptions();

            // Proxy fixe du profil
            if (!string.IsNullOrEmpty(profile.Proxy))
            {
                proxyService.ApplyProxy(envOptions, profile.Proxy);
            }

            // Anti-CDP et autres args stealth
            envOptions.AdditionalBrowserArguments = "--disable-dev-shm-usage --no-sandbox --disable-gpu-sandbox --disable-web-security";

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataDir, envOptions);
            await webView.EnsureCoreWebView2Async(environment);

            // Fingerprint spoof
            var fingerprint = JsonConvert.DeserializeObject<Fingerprint>(profile.Fingerprint);
            var spoofScript = fingerprintService.GenerateJSSpoof(fingerprint);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(spoofScript);

            // Test initial fingerprint
            await monitoringService.TestFingerprintAsync(webView);

            Logger.LogInfo($"Browser initialized for '{profile.Name}' with full anti-detection.");
            return webView;
        }
        // ... (code existant pour lancer WebView2)

        private async Task<CoreWebView2> InitializeWebViewAsync(ProxyInfo proxyInfo)
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = $"--proxy-server=http://{proxyInfo.Host}:{proxyInfo.Port}"
                // Autres args existants, e.g., pour fingerprints : "--user-agent=..." 
            };

            var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
            var webView = new WebView2(); // Ou votre instance existante
            await webView.EnsureCoreWebView2Async(environment);

            // Nouveau : Handler pour l'auth proxy
            webView.CoreWebView2.BasicAuthenticationRequested += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(proxyInfo.Username))
                {
                    e.Response.UserName = proxyInfo.Username;
                    e.Response.Password = proxyInfo.Password;
                }
                else
                {
                    // Si pas d'auth, annuler ou log erreur
                    e.Cancel = true;
                }
            };

            return webView.CoreWebView2;
        }

        public async Task<bool> ExecuteActionWithLimitsAsync(WebView2 webView, string actionType, string subAction, Func<Task> actionFunc)
        {
            if (!limitsService.CanPerformAction(actionType, subAction))
            {
                Logger.LogWarning($"Limite atteinte pour {subAction}. Skip.");
                return false;
            }

            // Check CAPTCHA
            if (await monitoringService.DetectCaptchaAsync(webView))
            {
                Logger.LogWarning("CAPTCHA détecté - Pause manuelle.");
                return false;
            }

            await actionFunc();
            limitsService.IncrementAction(actionType, subAction);
            return true;
        }

        public async Task ExecuteScriptAsync(WebView2 webView, string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                var script = File.ReadAllText(scriptPath);
                await webView.ExecuteScriptAsync(script);
                Logger.LogInfo($"Executed: {Path.GetFileName(scriptPath)}");
            }
            else
            {
                Logger.LogError($"Script not found: {scriptPath}");
            }
        }

        public async Task SimulateHumanScrollAsync(WebView2 webView, int durationSeconds)
        {
            var scrollScript = $@"
                let startTime = Date.now();
                let endTime = startTime + {durationSeconds * 1000};
                function smoothScroll() {{
                    if (Date.now() > endTime || !window.isRunning) return;
                    window.scrollBy(0, Math.random() * 50 + 20);
                    setTimeout(smoothScroll, Math.random() * 500 + 200);
                }}
                smoothScroll();
            ";
            await webView.ExecuteScriptAsync(scrollScript);
        }

        public async Task CleanupAsync(WebView2 webView, Profile profile)
        {
            await cleanupService.CleanupBrowserAsync(webView);
            cleanupService.PruneMemory(profile);
        }
    }
}