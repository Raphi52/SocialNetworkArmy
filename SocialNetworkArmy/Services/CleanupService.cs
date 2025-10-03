// Services/CleanupService.cs - Correction pour DateTime (pas DateTimeOffset)
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    public class CleanupService
    {
        public async Task CleanupBrowserAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null) return;

            try
            {
                // Clear browsing data: cookies, cache, history (avec enums corrects)
                var dataKinds = CoreWebView2BrowsingDataKinds.Cookies |
                                CoreWebView2BrowsingDataKinds.CacheStorage |
                                CoreWebView2BrowsingDataKinds.DownloadHistory |
                                CoreWebView2BrowsingDataKinds.BrowsingHistory |
                                CoreWebView2BrowsingDataKinds.LocalStorage |
                                CoreWebView2BrowsingDataKinds.ServiceWorkers;

                // Corrigé : Utilise DateTime au lieu de DateTimeOffset
                await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(dataKinds, DateTime.MinValue, DateTime.Now);

                // Close and dispose
                webView.Stop(); // Arrête navigation
                webView.Dispose();
                Logger.LogInfo("Nettoyage navigateur : cache/cookies effacés et fermé.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Erreur nettoyage : {ex.Message}");
            }
        }

        public void PruneMemory(Profile profile)
        {
            profile.StorageState = ""; // Reset cookies/session
            Logger.LogInfo("Mémoire profil purgée.");
        }
    }
}