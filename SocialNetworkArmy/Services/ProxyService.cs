using System;
using Microsoft.Web.WebView2.Core;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class ProxyService
    {
        public void ApplyProxy(CoreWebView2EnvironmentOptions options, string proxy)
        {
            if (string.IsNullOrEmpty(proxy)) return;

            // WebView2 proxy support via AdditionalBrowserArguments
            if (proxy.StartsWith("http://") || proxy.StartsWith("socks5://"))
            {
                options.AdditionalBrowserArguments = $"--proxy-server={proxy}";
                Logger.LogInfo($"Proxy applied: {proxy}");
            }
            else
            {
                Logger.LogWarning($"Invalid proxy format: {proxy}");
            }
        }
    }
}