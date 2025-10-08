// Helpers/ProxyAuthHelper.cs
// VERSION COMPATIBLE avec WebView2 plus ancien
using Microsoft.Web.WebView2.Core;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Helpers
{
    public class ProxyCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool HasAuth { get; set; }
    }

    public static class ProxyAuthHelper
    {
        /// <summary>
        /// Parse une URL de proxy au format: http://user:pass@IP:port ou IP:port
        /// </summary>
        public static ProxyCredentials ParseProxy(string proxyUrl)
        {
            if (string.IsNullOrWhiteSpace(proxyUrl))
                return null;

            var regex = new Regex(
                @"^(http://)?(?:([^:]+):([^@]+)@)?([\w\.-]+|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})$",
                RegexOptions.IgnoreCase
            );

            var match = regex.Match(proxyUrl);

            if (!match.Success)
                return null;

            var credentials = new ProxyCredentials
            {
                Username = match.Groups[2].Success ? match.Groups[2].Value : null,
                Password = match.Groups[3].Success ? match.Groups[3].Value : null,
                Host = match.Groups[4].Value,
                Port = int.Parse(match.Groups[5].Value),
                HasAuth = match.Groups[2].Success && match.Groups[3].Success
            };

            return credentials;
        }

        /// <summary>
        /// Crée une string proxy au format compatible WebView2
        /// Inclut les credentials directement dans l'URL si présents
        /// </summary>
        public static string BuildProxyString(ProxyCredentials credentials)
        {
            if (credentials == null)
                return null;

            if (credentials.HasAuth)
            {
                // Format: http://user:pass@host:port
                return $"http://{credentials.Username}:{credentials.Password}@{credentials.Host}:{credentials.Port}";
            }
            else
            {
                // Format: http://host:port
                return $"http://{credentials.Host}:{credentials.Port}";
            }
        }

        /// <summary>
        /// Applique le proxy à CoreWebView2EnvironmentOptions
        /// COMPATIBLE avec toutes les versions de WebView2
        /// </summary>
        public static void ApplyProxyToOptions(CoreWebView2EnvironmentOptions options, ProxyCredentials credentials)
        {
            if (credentials == null)
                return;

            string proxyString = BuildProxyString(credentials);

            string existingArgs = options.AdditionalBrowserArguments ?? "";
            if (string.IsNullOrEmpty(existingArgs))
            {
                options.AdditionalBrowserArguments = $"--proxy-server={proxyString}";
            }
            else
            {
                options.AdditionalBrowserArguments = $"{existingArgs} --proxy-server={proxyString}";
            }
        }

        /// <summary>
        /// Validation de format proxy
        /// </summary>
        public static bool IsValidProxyFormat(string proxyUrl)
        {
            return ParseProxy(proxyUrl) != null;
        }

        /// <summary>
        /// Extrait l'adresse sans credentials (pour logs)
        /// </summary>
        public static string GetProxyAddressWithoutAuth(string proxyUrl)
        {
            var credentials = ParseProxy(proxyUrl);
            if (credentials == null)
                return proxyUrl;

            return $"{credentials.Host}:{credentials.Port}";
        }
    }
}