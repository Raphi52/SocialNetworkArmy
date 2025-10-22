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
        private string realIp = null;
        private string currentProxyAddress = null;

        public ProxyService()
        {
            _ = GetRealIpAsync();
        }

        private bool IsValidProxyFormat(string line)
        {
            var regex = new Regex(@"^((http|https|socks4|socks5)://)?(?:([^:@]+):([^@]+)@)?(?:[\w\.-]+|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d{1,5})$", RegexOptions.IgnoreCase);
            return regex.IsMatch(line);
        }

        private string NormalizeProxyAddress(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;

            if (!line.Contains("://"))
            {
                line = "http://" + line;
            }

            return line;
        }

        public void ApplyProxy(CoreWebView2EnvironmentOptions options, string proxy = null)
        {
            if (string.IsNullOrEmpty(proxy))
            {
                Logger.LogWarning("No proxy provided to apply.");
                return;
            }

            Logger.LogInfo($"[PROXY] Starting ApplyProxy with: {proxy}");

            string proxyAddress = NormalizeProxyAddress(proxy);
            Logger.LogInfo($"[PROXY] Normalized address: {proxyAddress}");

            if (!IsValidProxyFormat(proxyAddress))
            {
                Logger.LogWarning($"Invalid proxy format: {proxyAddress}");
                return;
            }

            var (protocol, host, port, user, pass) = ParseProxy(proxyAddress);
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
            {
                Logger.LogWarning($"Invalid parsed proxy: {proxyAddress}");
                return;
            }

            Logger.LogInfo($"[PROXY] Parsed values:");
            Logger.LogInfo($"  Protocol: {protocol}");
            Logger.LogInfo($"  Host: {host}");
            Logger.LogInfo($"  Port: {port}");
            Logger.LogInfo($"  User: {user}");
            Logger.LogInfo($"  Pass: {(string.IsNullOrEmpty(pass) ? "EMPTY" : $"***{pass.Length} chars***")}");
            Logger.LogInfo($"  Has Auth: {!string.IsNullOrEmpty(user)}");

            // Build browser arguments
            string args = options.AdditionalBrowserArguments ?? "";

            // CRITICAL: Chromium's --proxy-server does NOT support HTTPS proxies
            // Always use HTTP for the proxy connection scheme
            // The proxy server itself will handle HTTPS tunneling via CONNECT
            string proxyScheme = "http";

            // Log if original was HTTPS (for debugging)
            if (protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInfo($"[PROXY] Note: Original proxy used HTTPS, converting to HTTP for Chromium compatibility");
                Logger.LogInfo($"[PROXY] The proxy will still tunnel HTTPS traffic via HTTP CONNECT method");
            }

            // Build proxy URL - ALWAYS use http:// for Chromium, NO credentials
            string proxyServer = $"{proxyScheme}://{host}:{port}";
            Logger.LogInfo($"[PROXY] Built proxy server URL: {proxyServer}");

            // Use --proxy-server WITHOUT credentials (auth via BasicAuthenticationRequested)
            string proxyArg = $"--proxy-server=\"{proxyServer}\"";

            // Bypass local addresses
            string bypassArg = "--proxy-bypass-list=\"<local>\"";

            // Anti-detection flags - make browser look more legitimate
            // Remove flags that scream "automation"
            string stealthFlags =
                "--disable-blink-features=AutomationControlled " +  // Critical: hides automation
                "--disable-features=IsolateOrigins,site-per-process " + // Better compatibility
                "--disable-site-isolation-trials " +
                "--ignore-certificate-errors " +
                "--ignore-urlfetcher-cert-requests";

            // Combine arguments
            if (string.IsNullOrEmpty(args))
            {
                options.AdditionalBrowserArguments = $"{proxyArg} {bypassArg} {stealthFlags}";
            }
            else
            {
                options.AdditionalBrowserArguments = $"{args} {proxyArg} {bypassArg} {stealthFlags}";
            }

            currentProxyAddress = proxyAddress;
            Logger.LogInfo($"[PROXY] ✓ Final browser arguments:");
            Logger.LogInfo($"  {options.AdditionalBrowserArguments}");
        }

        // Primary authentication handler - this is the correct way for WebView2
        public void SetupProxyAuthentication(CoreWebView2 webView2, string proxyAddress)
        {
            if (string.IsNullOrEmpty(proxyAddress))
            {
                Logger.LogInfo("No proxy address provided for authentication setup");
                return;
            }

            var (protocol, host, port, user, pass) = ParseProxy(proxyAddress);

            if (string.IsNullOrEmpty(user))
            {
                Logger.LogInfo("No proxy authentication required (no credentials in proxy URL)");
                return;
            }

            int authAttempts = 0;

            webView2.BasicAuthenticationRequested += (sender, args) =>
            {
                try
                {
                    authAttempts++;
                    Logger.LogInfo($"[AUTH #{authAttempts}] Request for: {args.Uri}");
                    Logger.LogInfo($"[AUTH #{authAttempts}] Challenge: {args.Challenge}");

                    var deferral = args.GetDeferral();

                    // Provide RAW (unencoded) credentials to the auth handler
                    args.Response.UserName = user;
                    args.Response.Password = pass ?? "";

                    deferral.Complete();

                    Logger.LogInfo($"[AUTH #{authAttempts}] ✓ Credentials sent (user: {user})");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[AUTH] Error: {ex.Message}");
                }
            };

            Logger.LogInfo($"✓ Authentication handler registered for user: {user}");
        }

        public async Task<string> GetWebView2ProxyIpAsync(CoreWebView2 webView2)
        {
            if (webView2 == null)
            {
                Logger.LogWarning("WebView2 not initialized for proxy verification");
                return null;
            }

            try
            {
                Logger.LogInfo("Verifying proxy IP through WebView2...");

                // Wait longer for proxy to be fully initialized (especially with auth)
                await Task.Delay(4000);

                // Try multiple IP services
                string[] ipCheckUrls = new[]
                {
                    "https://api.ipify.org?format=json",
                    "https://api.myip.com",
                    "https://ipinfo.io/json",
                    "https://ifconfig.me/all.json"
                };

                foreach (var url in ipCheckUrls)
                {
                    try
                    {
                        Logger.LogDebug($"Checking IP via: {url}");

                        string script = $@"
                            (async () => {{
                                try {{
                                    const response = await fetch('{url}', {{
                                        method: 'GET',
                                        headers: {{ 'Accept': 'application/json' }},
                                        credentials: 'omit'
                                    }});
                                    if (!response.ok) return null;
                                    const data = await response.json();
                                    return data.ip || data.ip_addr || null;
                                }} catch (e) {{
                                    return null;
                                }}
                            }})()
                        ";

                        var result = await webView2.ExecuteScriptAsync(script);

                        if (!string.IsNullOrEmpty(result) && result != "null")
                        {
                            // Remove quotes from JSON string
                            string proxyIp = result.Trim('"', ' ', '\r', '\n');

                            // Validate IP format
                            if (System.Net.IPAddress.TryParse(proxyIp, out _))
                            {
                                Logger.LogInfo($"✓ WebView2 proxy IP detected: {proxyIp}");

                                // Compare with real IP
                                if (string.IsNullOrEmpty(realIp))
                                {
                                    realIp = await GetRealIpAsync();
                                }

                                if (!string.IsNullOrEmpty(realIp) && proxyIp == realIp)
                                {
                                    Logger.LogWarning($"⚠ Proxy not working: IP {proxyIp} == real IP {realIp}");
                                    return null;
                                }

                                return proxyIp;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Failed to check IP via {url}: {ex.Message}");
                        continue;
                    }
                }

                Logger.LogWarning("Could not verify proxy IP through WebView2");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error verifying WebView2 proxy IP: {ex.Message}");
                return null;
            }
        }

        public async Task<string> GetCurrentProxyIpAsync(string proxyAddress)
        {
            if (string.IsNullOrEmpty(proxyAddress))
            {
                Logger.LogDebug("No proxy address provided for verification.");
                return null;
            }

            Logger.LogInfo($"Verifying proxy via HttpClient: {proxyAddress}");

            try
            {
                var (protocol, host, port, user, pass) = ParseProxy(proxyAddress);

                Logger.LogInfo($"Parsed proxy - Host: {host}, Port: {port}, User: {user}");

                // Create proxy with proper credentials
                var webProxy = new WebProxy($"http://{host}:{port}", true);

                if (!string.IsNullOrEmpty(user))
                {
                    // Use NetworkCredential for proper authentication
                    webProxy.Credentials = new NetworkCredential(user, pass ?? "");
                    Logger.LogInfo($"Proxy credentials configured for user: {user}");
                }

                // Use SocketsHttpHandler for better proxy support
                var socketsHandler = new SocketsHttpHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    PreAuthenticate = true,
                    DefaultProxyCredentials = webProxy.Credentials,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    ConnectTimeout = TimeSpan.FromSeconds(20),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                using var client = new HttpClient(socketsHandler);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                string proxyIp = null;

                // Try HTTP endpoints (they work better with proxies)
                string[] ipCheckUrls = new[]
                {
                    "http://api.ipify.org",
                    "http://ifconfig.me/ip",
                    "http://icanhazip.com"
                };

                foreach (var url in ipCheckUrls)
                {
                    try
                    {
                        Logger.LogDebug($"Trying IP check service: {url}");
                        var response = await client.GetStringAsync(url);
                        proxyIp = response.Trim();

                        // Validate IP format
                        if (!string.IsNullOrWhiteSpace(proxyIp) &&
                            System.Net.IPAddress.TryParse(proxyIp, out _))
                        {
                            Logger.LogDebug($"Got valid IP from {url}: {proxyIp}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Failed {url}: {ex.Message}");
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(proxyIp))
                {
                    Logger.LogWarning($"Could not retrieve IP through proxy {proxyAddress}");
                    return null;
                }

                if (string.IsNullOrEmpty(realIp))
                {
                    realIp = await GetRealIpAsync();
                }

                if (proxyIp == realIp)
                {
                    Logger.LogWarning($"⚠ Proxy is not protecting: IP {proxyIp} == real IP {realIp}");
                    return null;
                }

                Logger.LogInfo($"✓ Proxy verified OK: {proxyAddress} -> IP {proxyIp} (real: {realIp})");
                return proxyIp;
            }
            catch (HttpRequestException ex)
            {
                Logger.LogError($"HTTP error verifying proxy: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.LogError($"  Inner exception: {ex.InnerException.Message}");
                }
                return null;
            }
            catch (TaskCanceledException)
            {
                Logger.LogError($"Timeout verifying proxy (30s exceeded)");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to verify proxy: {ex.Message}");
                return null;
            }
        }

        public (string protocol, string host, string port, string user, string pass) ParseProxy(string proxyAddress)
        {
            if (string.IsNullOrEmpty(proxyAddress)) return ("", "", "", "", "");

            try
            {
                // Extract protocol
                string protocol = "http";
                string remaining = proxyAddress;

                if (remaining.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "https";
                    remaining = remaining.Substring(8);
                }
                else if (remaining.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "http";
                    remaining = remaining.Substring(7);
                }
                else if (remaining.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "socks5";
                    remaining = remaining.Substring(9);
                }
                else if (remaining.StartsWith("socks4://", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "socks4";
                    remaining = remaining.Substring(9);
                }

                // Find the LAST @ to separate auth from host:port
                int atIndex = remaining.LastIndexOf('@');

                string user = "";
                string pass = "";
                string hostPort = remaining;

                if (atIndex > 0)
                {
                    // There's authentication
                    string authPart = remaining.Substring(0, atIndex);
                    hostPort = remaining.Substring(atIndex + 1);

                    // Find the FIRST : in auth part to separate user:pass
                    int colonIndex = authPart.IndexOf(':');
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

                // Parse host:port (find LAST : for port)
                int portColonIndex = hostPort.LastIndexOf(':');
                string host = hostPort;
                string port = "80";

                if (portColonIndex > 0)
                {
                    host = hostPort.Substring(0, portColonIndex);
                    port = hostPort.Substring(portColonIndex + 1);
                }

                Logger.LogDebug($"ParseProxy result: protocol={protocol}, host={host}, port={port}, user={user}, pass={(string.IsNullOrEmpty(pass) ? "none" : "***")}");

                return (protocol, host, port, user, pass);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error parsing proxy: {ex.Message}");
                return ("", "", "", "", "");
            }
        }

        private async Task<string> GetRealIpAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetStringAsync("http://api.ipify.org");
                var ip = response.Trim();
                Logger.LogInfo($"Real IP detected: {ip}");
                return ip;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to detect real IP: " + ex.Message);
                return null;
            }
        }

        public string GetCurrentProxyAddress()
        {
            return currentProxyAddress;
        }
        public bool ValidateProxyWhitelist(string proxyAddress, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(proxyAddress))
            {
                errorMessage = "Aucun proxy spécifié.";
                return false;
            }

            // Normaliser le proxy pour la comparaison
            string normalizedProxy = NormalizeProxyAddress(proxyAddress);

            // Valider le format
            if (!IsValidProxyFormat(normalizedProxy))
            {
                errorMessage = "Format de proxy invalide.";
                return false;
            }

            // Vérifier la whitelist
            if (!ProxyWhitelist.IsAuthorized(normalizedProxy))
            {
                errorMessage = "❌ Ce proxy n'est pas autorisé.\n\n" +
                              "Veuillez utiliser uniquement les proxies fournis par votre fournisseur.\n\n" +
                              "Contactez le support si vous pensez qu'il s'agit d'une erreur.";
                Logger.LogWarning($"Tentative d'utilisation d'un proxy non autorisé: {normalizedProxy}");
                return false;
            }

            Logger.LogInfo($"✓ Proxy autorisé validé: {normalizedProxy}");
            return true;
        }
        public ProxyInfo GetProxyInfo(string proxyAddress)
        {
            if (string.IsNullOrEmpty(proxyAddress))
            {
                return new ProxyInfo();
            }

            var (protocol, host, port, user, pass) = ParseProxy(proxyAddress);

            return new ProxyInfo
            {
                Username = user,
                Password = pass,
                Host = host,
                Port = port
            };
        }
        public static class ProxyWhitelist
        {
            // Votre liste de proxies autorisés
            private static readonly HashSet<string> AuthorizedProxies = new()
        {
            "http://spait7n9et:n4MpiP9Ize9iw+pk1E@isp.decodo.com:10001",
            "http://spait7n9et:n4MpiP9Ize9iw+pk1E@isp.decodo.com:10002",
            "http://spait7n9et:n4MpiP9Ize9iw+pk1E@isp.decodo.com:10003",
            // Ajoutez tous vos proxies ici
        };

            public static bool IsAuthorized(string proxy)
            {
                // Comparaison exacte (sensible à la casse)
                return AuthorizedProxies.Contains(proxy?.Trim());
            }

            public static IReadOnlyList<string> GetAllProxies()
            {
                return AuthorizedProxies.ToList().AsReadOnly();
            }
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