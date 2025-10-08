using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class ProxyService
    {
        private List<string> proxies = new List<string>();
        private int currentIndex = 0;
        private readonly string proxiesFilePath = Path.Combine("Data", "proxies.txt");
        private string realIp = null;
        private string currentProxyAddress = null;

        public ProxyService()
        {
            LoadProxies();
            _ = GetRealIpAsync(); // Cache real IP asynchronously
        }

        private void LoadProxies()
        {
            if (!File.Exists(proxiesFilePath))
            {
                Logger.LogWarning($"Proxies file not found: {proxiesFilePath}. Crée-le avec un proxy valide (ex: http://ip:port).");
                return;
            }

            var lines = File.ReadAllLines(proxiesFilePath)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(line => line.Trim())
                            .Where(IsValidProxyFormat)
                            .ToList();

            if (lines.Count == 0)
            {
                Logger.LogWarning("No valid proxies found in file. Vérifie le format dans proxies.txt.");
                return;
            }

            // Normalize: Add protocol if missing
            proxies = lines.Select(NormalizeProxyAddress).ToList();
            Logger.LogInfo($"Loaded {proxies.Count} proxies from {proxiesFilePath}. Premier: {proxies[0]}");
        }

        private bool IsValidProxyFormat(string line)
        {
            // Regex for proxy: [protocol://][user:pass@]host:port
            var regex = new Regex(@"^((http|https|socks4|socks5)://)?(?:([^:]+):([^@]+)@)?(?:[\w\.-]+|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})$", RegexOptions.IgnoreCase);
            return regex.IsMatch(line);
        }

        private string NormalizeProxyAddress(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;

            // If no protocol, assume http:// (change to socks5:// si besoin)
            if (!line.Contains("://"))
            {
                line = "http://" + line;
            }

            return line;
        }

        public void ApplyProxy(CoreWebView2EnvironmentOptions options, string proxy = null)
        {
            string proxyAddress = proxy ?? GetNextProxyAddress();
            if (string.IsNullOrEmpty(proxyAddress))
            {
                Logger.LogWarning("No proxy available to apply.");
                return;
            }

            // Ensure normalized
            proxyAddress = NormalizeProxyAddress(proxyAddress);

            if (IsValidProxyFormat(proxyAddress))
            {
                string args = options.AdditionalBrowserArguments ?? "";
                options.AdditionalBrowserArguments = string.IsNullOrEmpty(args) ? $"--proxy-server={proxyAddress}" : $"{args} --proxy-server={proxyAddress}";
                currentProxyAddress = proxyAddress;
                Logger.LogInfo($"Proxy appliqué au WebView2: {proxyAddress} (via --proxy-server)");
            }
            else
            {
                Logger.LogWarning($"Invalid proxy format: {proxyAddress}");
            }
        }

        private string GetNextProxyAddress()
        {
            if (proxies.Count == 0) return null;
            var address = proxies[currentIndex % proxies.Count];
            currentIndex++;
            return address;
        }

        public async Task<string> GetCurrentProxyIpAsync()
        {
            string proxyAddress = currentProxyAddress ?? GetNextProxyAddress();
            if (string.IsNullOrEmpty(proxyAddress))
            {
                Logger.LogDebug("No current proxy address available for verification.");
                return null;
            }

            Logger.LogInfo($"Vérification proxy: {proxyAddress}");

            try
            {
                // Parse proxy pour auth (manual pour special chars)
                var (host, port, user, pass) = ParseProxy(proxyAddress);
                var webProxy = new WebProxy(host, int.Parse(port));
                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                {
                    webProxy.Credentials = new NetworkCredential(user, pass);
                    Logger.LogDebug($"Auth ajoutée pour proxy: {user}:***");
                }

                var handler = new HttpClientHandler { Proxy = webProxy, UseProxy = true };
                handler.UseDefaultCredentials = false; // Important: Don't use default, use proxy creds
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(30); // Increased for slow proxies

                var response = await client.GetStringAsync("http://ipv4.icanhazip.com");
                var proxyIp = response.Trim();

                // Compare with real IP
                if (string.IsNullOrEmpty(realIp))
                {
                    realIp = await GetRealIpAsync();
                }

                if (string.IsNullOrEmpty(proxyIp) || proxyIp == realIp)
                {
                    Logger.LogWarning($"Proxy {proxyAddress} non protecteur: IP {proxyIp} == real IP {realIp}");
                    return null;
                }

                Logger.LogInfo($"Proxy vérifié OK: {proxyAddress} -> IP {proxyIp} (différent de real: {realIp})");
                return proxyIp;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Échec vérification proxy {proxyAddress}: {ex.Message}. Teste ce proxy manuellement (ex: curl --proxy {proxyAddress} https://api.ipify.org).");
                return null;
            }
        }

        private (string host, string port, string user, string pass) ParseProxy(string proxyAddress)
        {
            // Manual parsing to handle special chars in pass
            if (proxyAddress.StartsWith("http://") || proxyAddress.StartsWith("https://"))
            {
                proxyAddress = proxyAddress.Substring(proxyAddress.IndexOf("://") + 3);
            }

            var atIndex = proxyAddress.LastIndexOf('@');
            string authPart = "";
            string hostPortPart = proxyAddress;
            if (atIndex > 0)
            {
                authPart = proxyAddress.Substring(0, atIndex);
                hostPortPart = proxyAddress.Substring(atIndex + 1);
            }

            string user = "";
            string pass = "";
            if (!string.IsNullOrEmpty(authPart))
            {
                var colonIndex = authPart.IndexOf(':');
                if (colonIndex > 0)
                {
                    user = authPart.Substring(0, colonIndex);
                    pass = authPart.Substring(colonIndex + 1);
                }
                else
                {
                    user = authPart;
                }
            }

            var colonPortIndex = hostPortPart.LastIndexOf(':');
            string host = hostPortPart;
            string port = "80"; // default
            if (colonPortIndex > 0)
            {
                host = hostPortPart.Substring(0, colonPortIndex);
                port = hostPortPart.Substring(colonPortIndex + 1);
            }

            return (host, port, user, pass);
        }

        private async Task<string> GetRealIpAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetStringAsync("http://ipv4.icanhazip.com");
                var ip = response.Trim();
                Logger.LogInfo($"IP réelle détectée: {ip}");
                return ip;
            }
            catch (Exception ex)
            {
                Logger.LogError("Échec détection IP réelle: " + ex.Message);
                return null;
            }
        }

        public ProxyInfo GetNextProxy()
        {
            string proxyString = GetNextProxyAddress();
            if (string.IsNullOrEmpty(proxyString))
            {
                throw new InvalidOperationException("No proxies available");
            }

            // Utilisez votre regex existante pour parser (adaptée de MainForm)
            var regex = new Regex(@"^(http://)?(([^:]+):([^@]+)@)?([\w\.-]+|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})$", RegexOptions.IgnoreCase);
            var match = regex.Match(proxyString);

            if (!match.Success)
            {
                throw new ArgumentException("Format proxy invalide");
            }

            return new ProxyInfo
            {
                Username = match.Groups[3].Value, // Peut être vide si pas d'auth
                Password = match.Groups[4].Value,
                Host = match.Groups[5].Value,
                Port = match.Groups[6].Value
            };
        }

        public struct ProxyInfo
        {
            public string Host { get; set; }
            public string Port { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }
    }
}