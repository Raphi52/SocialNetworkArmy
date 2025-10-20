using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Service pour gérer les cookies TikTok dans WebView2
    /// </summary>
    public class TikTokCookieManagerWebView
    {
        private readonly string cookiesFilePath;

        public TikTokCookieManagerWebView(string cookiesFilePath = null)
        {
            this.cookiesFilePath = cookiesFilePath ??
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies.txt");
        }

        /// <summary>
        /// Vérifie si le fichier cookies.txt existe
        /// </summary>
        public bool CookiesFileExists()
        {
            return File.Exists(cookiesFilePath);
        }

        /// <summary>
        /// Charge et injecte les cookies dans WebView2
        /// IMPORTANT : Appelez cette méthode APRÈS l'initialisation de CoreWebView2
        /// </summary>
        public async Task<bool> LoadCookiesIntoWebView(CoreWebView2 coreWebView2, Action<string> logAction = null)
        {
            try
            {
                if (coreWebView2 == null)
                {
                    logAction?.Invoke("❌ CoreWebView2 n'est pas initialisé");
                    return false;
                }

                if (!CookiesFileExists())
                {
                    logAction?.Invoke($"❌ Fichier cookies.txt introuvable : {cookiesFilePath}");
                    return false;
                }

                // Charger les cookies depuis le fichier
                var cookies = ParseNetscapeCookiesFile(cookiesFilePath);

                if (cookies.Count == 0)
                {
                    logAction?.Invoke("⚠ Aucun cookie valide trouvé dans le fichier");
                    return false;
                }

                // Injecter les cookies dans WebView2
                int successCount = 0;
                var cookieManager = coreWebView2.CookieManager;

                foreach (var cookieData in cookies)
                {
                    try
                    {
                        var cookie = cookieManager.CreateCookie(
                            cookieData.Name,
                            cookieData.Value,
                            cookieData.Domain,
                            cookieData.Path
                        );

                        // Configurer les propriétés du cookie
                        cookie.IsSecure = cookieData.IsSecure;
                        cookie.IsHttpOnly = cookieData.IsHttpOnly;


                        if (cookieData.Expires.HasValue)
                        {
                            cookie.Expires = cookieData.Expires.Value;  // ✓ Directement le DateTime
                        }

                        // Ajouter le cookie
                        cookieManager.AddOrUpdateCookie(cookie);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        logAction?.Invoke($"⚠ Cookie ignoré ({cookieData.Name}): {ex.Message}");
                    }
                }

                logAction?.Invoke($"✓ {successCount}/{cookies.Count} cookies chargés avec succès");

                // Attendre un peu pour que les cookies soient appliqués
                await Task.Delay(500);

                return successCount > 0;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"❌ Erreur lors du chargement des cookies : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parse un fichier cookies.txt au format Netscape
        /// </summary>
        private List<CookieData> ParseNetscapeCookiesFile(string filePath)
        {
            var cookies = new List<CookieData>();

            var lines = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToList();

            foreach (var line in lines)
            {
                try
                {
                    var parts = line.Split('\t');

                    if (parts.Length < 7)
                        continue;

                    string domain = parts[0];
                    string flag = parts[1]; // TRUE/FALSE
                    string path = parts[2];
                    string secure = parts[3]; // TRUE/FALSE
                    string expiration = parts[4];
                    string name = parts[5];
                    string value = parts[6];

                    // Nettoyer le domaine (enlever le point initial si présent)
                    if (domain.StartsWith("."))
                        domain = domain.Substring(1);

                    cookies.Add(new CookieData
                    {
                        Name = name,
                        Value = value,
                        Domain = domain,
                        Path = path,
                        IsSecure = secure.ToUpper() == "TRUE",
                        IsHttpOnly = false,
                        Expires = ParseExpiration(expiration)
                    });
                }
                catch
                {
                    continue;
                }
            }

            return cookies;
        }

        /// <summary>
        /// Convertit le timestamp Unix en DateTime nullable
        /// </summary>
        private DateTime? ParseExpiration(string expirationStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expirationStr) || expirationStr == "0")
                    return null;

                long timestamp = long.Parse(expirationStr);

                if (timestamp == 0)
                    return null;

                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(timestamp);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Vérifie si l'utilisateur est connecté
        /// </summary>
        public async Task<bool> IsUserLoggedInAsync(CoreWebView2 coreWebView2)
        {
            try
            {
                var cookies = await coreWebView2.CookieManager.GetCookiesAsync("https://www.tiktok.com");

                // Vérifier si on a les cookies de session importants
                bool hasSessionId = cookies.Any(c => c.Name == "sessionid" || c.Name == "sid_tt");
                bool hasUidTt = cookies.Any(c => c.Name == "uid_tt");

                return hasSessionId && hasUidTt;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Affiche des informations sur les cookies
        /// </summary>
        public void PrintCookieInfo(Action<string> logAction = null)
        {
            if (!CookiesFileExists())
            {
                logAction?.Invoke("❌ Aucun fichier cookies.txt trouvé");
                return;
            }

            var cookies = ParseNetscapeCookiesFile(cookiesFilePath);

            logAction?.Invoke($"📊 Informations sur les cookies :");
            logAction?.Invoke($"   • Fichier : {cookiesFilePath}");
            logAction?.Invoke($"   • Nombre total : {cookies.Count}");

            var importantCookies = new[] { "sessionid", "sid_tt", "uid_tt", "sid_guard", "tt_chain_token" };
            var found = cookies.Where(c => importantCookies.Contains(c.Name)).ToList();

            logAction?.Invoke($"   • Cookies importants trouvés : {found.Count}/{importantCookies.Length}");
            foreach (var cookie in found)
            {
                string status = cookie.Expires.HasValue && cookie.Expires.Value > DateTime.Now ? "✓ Valide" : "⚠ Expiré";
                logAction?.Invoke($"     - {cookie.Name}: {status}");
            }
        }

        // Classe interne pour stocker les données des cookies
        private class CookieData
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public bool IsSecure { get; set; }
            public bool IsHttpOnly { get; set; }
            public DateTime? Expires { get; set; }
        }
    }
}