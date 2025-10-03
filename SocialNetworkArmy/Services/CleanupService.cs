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
        private bool _isDisposed = false; // Flag pour double-dispose

        public async Task CleanupBrowserAsync(WebView2 webView)
        {
            if (webView == null || _isDisposed)
            {
                Logger.LogWarning("WebView2 null ou déjà nettoyé – skip.");
                return;
            }

            try
            {
                // Check CoreWebView2 valide avant clear
                if (webView.CoreWebView2 == null || webView.IsDisposed)
                {
                    Logger.LogWarning("CoreWebView2 invalide – skip data clear.");
                }
                else
                {
                    var dataKinds = CoreWebView2BrowsingDataKinds.Cookies |
                                    CoreWebView2BrowsingDataKinds.CacheStorage |
                                    CoreWebView2BrowsingDataKinds.DownloadHistory |
                                    CoreWebView2BrowsingDataKinds.BrowsingHistory |
                                    CoreWebView2BrowsingDataKinds.LocalStorage |
                                    CoreWebView2BrowsingDataKinds.ServiceWorkers;

                    await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        dataKinds,
                        DateTime.MinValue,
                        DateTime.Now
                    );
                    Logger.LogInfo("Données browsing effacées.");
                }

                // Stop si nav active
                if (webView.CoreWebView2 != null && !string.IsNullOrEmpty(webView.CoreWebView2.Source))
                {
                    try
                    {
                        webView.CoreWebView2.Stop();
                        Logger.LogInfo("Navigation arrêtée.");
                        await Task.Delay(200); // Stabilise
                    }
                    catch (InvalidOperationException)
                    {
                        Logger.LogWarning("Stop ignoré – déjà arrêté.");
                    }
                }

                // Dispose final (seulement une fois)
                if (!webView.IsDisposed)
                {
                    webView.Dispose();
                    _isDisposed = true;
                    Logger.LogInfo("WebView2 disposé – cleanup complet.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Erreur cleanup : {ex.Message}");
            }
        }

        public void PruneMemory(Profile profile)
        {
            if (profile == null) return;
            profile.StorageState = "";
            Logger.LogInfo($"Mémoire profil '{profile.Name}' purgée.");
        }
    }
}