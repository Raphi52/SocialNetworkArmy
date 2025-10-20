using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class DownloadInstagramService
    {
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly Form parentForm;
        private readonly HttpClient httpClient;
        private string instagramCookies;
        private string csrfToken;
        private string userId;
        private string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
        private readonly HttpClientHandler httpHandler;
        private readonly SemaphoreSlim _navigationLock = new SemaphoreSlim(1, 1);

        public DownloadInstagramService(WebView2 webView, TextBox logTextBox, Profile profile, Form parentForm)
        {
            this.webView = webView;
            this.logTextBox = logTextBox;
            this.profile = profile;
            this.parentForm = parentForm;

            this.httpHandler = new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            this.httpClient = new HttpClient(httpHandler);
            this.httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            this.httpClient.DefaultRequestHeaders.Add("x-ig-app-id", "936619743392459");
            this.httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            this.httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            this.httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public void Dispose()
        {
            httpClient?.Dispose();
            httpHandler?.Dispose();
            _navigationLock?.Dispose();
        }

        public async Task RunAsync()
        {
            var instagramBotForm = parentForm as dynamic;
            if (instagramBotForm == null)
            {
                Log("Erreur: Impossible d'accéder au formulaire parent.");
                return;
            }

            try
            {
                await instagramBotForm.StartScriptAsync("Download Instagram");

                string destinationFolder = null;
                parentForm.Invoke(new Action(() =>
                {
                    try
                    {
                        using var dialog = new FolderBrowserDialog
                        {
                            Description = "Choisir dossier de destination",
                            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                        };
                        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                        {
                            Logger.LogWarning("Téléchargement annulé: aucun dossier sélectionné.");
                            return;
                        }
                        destinationFolder = dialog.SelectedPath;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Erreur sélection dossier: " + ex.Message);
                        return;
                    }
                }));

                if (string.IsNullOrEmpty(destinationFolder))
                {
                    Log("Téléchargement annulé.");
                    return;
                }

                string targetsFile = Path.Combine("data", "downloadinstatargets.txt");
                if (!File.Exists(targetsFile))
                {
                    Log($"Erreur: {targetsFile} n'existe pas.");
                    return;
                }

                var targets = File.ReadAllLines(targetsFile)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                    .ToList();

                if (targets.Count == 0)
                {
                    Log("Aucune cible.");
                    return;
                }

                Log($"{targets.Count} compte(s) | Dossier: {destinationFolder}");

                await InitializeSessionAsync();

                var token = instagramBotForm.GetCancellationToken();
                await webView.CoreWebView2.ExecuteScriptAsync("location.reload()");
                await Task.Delay(2000);
                await InitializeSessionAsync();

                var failedTargets = new List<string>();

                foreach (var username in targets)
                {
                    if (token.IsCancellationRequested) break;

                    bool success = await DownloadUserContentAsync(username, destinationFolder, token);

                    if (!success)
                    {
                        failedTargets.Add(username);
                    }

                    await Task.Delay(2000, token);
                }

                // ✅ SIMPLEMENT LOGGER LES ÉCHECS - PAS DE RETRY AUTOMATIQUE
                if (failedTargets.Count > 0)
                {
                    Log($"\n⚠ {failedTargets.Count} compte(s) échoué(s):");
                    foreach (var username in failedTargets)
                    {
                        Log($"  - @{username}");
                    }
                    Log("\nConsultez les fichiers 'failed.txt' dans chaque dossier pour les détails.");
                }

                Log("✓ Terminé!");
            }
            catch (OperationCanceledException)
            {
                Log("Annulé.");
            }
            catch (Exception ex)
            {
                Log($"Erreur: {ex.Message}");
            }
            finally
            {
                instagramBotForm.ScriptCompleted();
            }
        }

        private async Task InitializeSessionAsync()
        {
            try
            {
                var cookieScript = "document.cookie";
                var cookieResult = await webView.CoreWebView2.ExecuteScriptAsync(cookieScript);
                instagramCookies = cookieResult.Trim('"').Replace("\\\"", "\"");

                var csrfMatch = Regex.Match(instagramCookies, @"csrftoken=([^;]+)");
                if (csrfMatch.Success)
                {
                    csrfToken = csrfMatch.Groups[1].Value;
                }

                var dsUserMatch = Regex.Match(instagramCookies, @"ds_user_id=([^;]+)");
                if (dsUserMatch.Success)
                {
                    userId = dsUserMatch.Groups[1].Value;
                }

                httpClient.DefaultRequestHeaders.Remove("Cookie");
                httpClient.DefaultRequestHeaders.Add("Cookie", instagramCookies);

                if (!string.IsNullOrEmpty(csrfToken))
                {
                    httpClient.DefaultRequestHeaders.Remove("x-csrftoken");
                    httpClient.DefaultRequestHeaders.Add("x-csrftoken", csrfToken);
                }

                Log($"Session initialisée (CSRF: {!string.IsNullOrEmpty(csrfToken)}, UserID: {!string.IsNullOrEmpty(userId)})");
            }
            catch (Exception ex)
            {
                Log($"Avertissement session: {ex.Message}");
            }
        }

        private async Task<bool> DownloadUserContentAsync(string username, string baseFolder, CancellationToken token)
        {
            try
            {
                Log($"@{username}");
                string userFolder = Path.Combine(baseFolder, username);
                Directory.CreateDirectory(userFolder);

                await NavigateToUrlAsync($"https://www.instagram.com/{username}/");
                await Task.Delay(3000, token);

                var posts = await ExtractAllShortcodesFromProfileAsync(token);

                if (posts.Count == 0)
                {
                    Log("  Aucun post ou profil privé");
                    return false;
                }

                Log($"  {posts.Count} posts détectés");
                await Task.Delay(2000, token);

                int success = 0;
                for (int i = 0; i < posts.Count; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var (shortcode, postType) = posts[i];
                    try
                    {
                        if (await DownloadPostByShortcodeAsync(shortcode, postType, userFolder, token))
                            success++;

                        int delay = 3000 + (i % 5 == 0 ? 2000 : 0);
                        await Task.Delay(delay, token);
                    }
                    catch (Exception ex)
                    {
                        Log($"  Erreur [{shortcode}]: {ex.Message}");
                        await Task.Delay(2000, token);
                    }
                }

                double successRate = (double)success / posts.Count * 100;
                Log($"✓ {success}/{posts.Count} téléchargés ({successRate:F1}%)");

                // Considérer comme succès si au moins 80% téléchargés
                return successRate >= 80;
            }
            catch (Exception ex)
            {
                Log($"Erreur @{username}: {ex.Message}");
                return false;
            }
        }

        private async Task<List<(string Shortcode, string Type)>> ExtractAllShortcodesFromProfileAsync(CancellationToken token)
        {
            var posts = new HashSet<(string, string)>();
            int previousCount = 0;
            int noNewCount = 0;
            const int maxNoNew = 9;
            int scrollAttempts = 0;
            const int maxScrolls = 100;

            try
            {
                while (scrollAttempts < maxScrolls)
                {
                    var script = @"
                JSON.stringify(
                    Array.from(document.querySelectorAll('a[href*=""/p/""], a[href*=""/reel/""]'))
                        .map(a => ({
                            href: a.href,
                            hasVideoIcon: a.querySelector('svg[aria-label*=""Clip""], svg[aria-label*=""Video""], svg[aria-label*=""Reel""]') !== null,
                            hasReelClass: a.className.includes('reel') || a.href.includes('/reel/')
                        }))
                )
            ";

                    var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    var items = JArray.Parse(result.Trim('"').Replace("\\\"", "\""));

                    foreach (var item in items)
                    {
                        string href = item["href"]?.ToString();
                        bool hasVideoIcon = item["hasVideoIcon"]?.ToObject<bool>() ?? false;
                        bool hasReelClass = item["hasReelClass"]?.ToObject<bool>() ?? false;

                        var match = Regex.Match(href, @"/(p|reel)/([A-Za-z0-9_-]+)");
                        if (match.Success)
                        {
                            string urlType = match.Groups[1].Value;
                            string shortcode = match.Groups[2].Value;
                            string finalType = urlType;

                            if (urlType == "p" && (hasVideoIcon || hasReelClass))
                            {
                                finalType = "reel";
                            }

                            posts.Add((shortcode, finalType));
                        }
                    }

                    if (posts.Count == previousCount)
                    {
                        noNewCount++;
                        if (noNewCount >= maxNoNew)
                        {
                            Log("  Fin du profil détectée");
                            break;
                        }
                    }
                    else
                    {
                        noNewCount = 0;
                    }

                    previousCount = posts.Count;
                    await ExecJsAsync("performance.clearResourceTimings();", token);
                    await webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, window.innerHeight * 1.5);");
                    await Task.Delay(1500, token);

                    scrollAttempts++;
                }
            }
            catch (Exception ex)
            {
                Log($"Erreur extraction shortcodes: {ex.Message}");
            }

            return posts.ToList();
        }

        private async Task<string> TryPerformanceAPIExtraction(string shortcode, string postType, CancellationToken token)
        {
            try
            {
                string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";

                // ✅ Vérifier si déjà sur la bonne page
                string currentUrl = webView.CoreWebView2.Source;
                if (!currentUrl.Contains(shortcode))
                {
                    await NavigateToUrlAsync(postUrl);
                    await Task.Delay(4000, token);
                }
                else
                {
                    Log($"  [{shortcode}] ⚡ Déjà sur la page");
                    await Task.Delay(1000, token); // Attente réduite
                }

                // Forcer le chargement vidéo
                await ExecJsAsync(@"
            const video = document.querySelector('video');
            if (video) {
                video.play();
                video.load();
            }
            window.scrollBy(0, 200);
        ", token);

                await Task.Delay(3000, token);

                // Extraction Performance API (code existant inchangé)
                string rawVideoListJson = await ExecJsAsync(@"
            (function() {
                try {
                    const entries = performance.getEntriesByType('resource');
                    
                    const videoUrls = entries
                        .filter(r => {
                            const url = r.name.toLowerCase();
                            const isInstagram = url.includes('cdninstagram') || url.includes('fbcdn');
                            const isVideo = url.includes('.mp4') || url.includes('video');
                            const notThumbnail = !url.includes('/t51.2885-15/') && 
                                                !url.includes('150x150') &&
                                                !url.includes('240x240');
                            
                            return isInstagram && isVideo && notThumbnail;
                        })
                        .map(r => ({
                            url: r.name,
                            size: r.transferSize || 0,
                            duration: r.duration || 0,
                            type: r.initiatorType
                        }))
                        .filter(v => {
                            const isShortReel = v.url.includes('reel') || v.duration < 15000;
                            const minSize = isShortReel ? 30000 : 100000;
                            return v.size > minSize;
                        })
                        .filter(v => !v.url.includes('bytestart') && !v.url.includes('byteend'))
                        .sort((a, b) => b.size - a.size);
                    
                    return JSON.stringify(videoUrls);
                } catch(e) {
                    return '[]';
                }
            })();
        ", token);

                if (!string.IsNullOrEmpty(rawVideoListJson))
                {
                    var videoList = JsonConvert.DeserializeObject<List<dynamic>>(rawVideoListJson);

                    if (videoList != null && videoList.Count > 0)
                    {
                        var bestVideo = videoList.FirstOrDefault();

                        if (bestVideo != null)
                        {
                            return bestVideo.url.ToString();
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<JObject> TryEmbedAPIAsync(string shortcode, CancellationToken token)
        {
            try
            {
                string embedUrl = $"https://www.instagram.com/p/{shortcode}/embed/captioned/";

                var request = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                var response = await httpClient.SendAsync(request, token);
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync();

                var videoMatch = Regex.Match(html, @"""video_url""\s*:\s*""([^""]+)""");
                if (videoMatch.Success)
                {
                    string videoUrl = videoMatch.Groups[1].Value
                        .Replace(@"\u0026", "&")
                        .Replace(@"\/", "/");

                    if (IsValidVideoUrl(videoUrl))
                    {
                        return new JObject
                        {
                            ["__typename"] = "XDTGraphVideo",
                            ["video_url"] = videoUrl,
                            ["dimensions"] = new JObject { ["width"] = 720, ["height"] = 1280 }
                        };
                    }
                }

                var imageMatch = Regex.Match(html, @"""display_url""\s*:\s*""([^""]+)""");
                if (imageMatch.Success)
                {
                    string imageUrl = imageMatch.Groups[1].Value
                        .Replace(@"\u0026", "&")
                        .Replace(@"\/", "/");

                    return new JObject
                    {
                        ["__typename"] = "XDTGraphImage",
                        ["display_url"] = imageUrl,
                        ["dimensions"] = new JObject { ["width"] = 1080, ["height"] = 1080 }
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        private bool IsValidMP4File(byte[] data)
        {
            if (data == null || data.Length < 12) return false;

            // Vérifier magic bytes MP4
            bool hasFtyp = (data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70);
            bool hasMoov = (data[4] == 0x6D && data[5] == 0x6F && data[6] == 0x6F && data[7] == 0x76);
            bool hasMdat = (data[4] == 0x6D && data[5] == 0x64 && data[6] == 0x61 && data[7] == 0x74);

            if (!hasFtyp && !hasMoov && !hasMdat)
            {
                if (IsImageFile(data)) return false; // C'est une image
            }

            // Taille minimale: 500KB
            if (data.Length < 500 * 1024) return false;

            return hasFtyp || hasMoov || hasMdat;
        }

        private string CleanVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            try
            {
                url = url
                    .Replace("\\u0026", "&")
                    .Replace(@"\/", "/")
                    .Replace("&amp;", "&")
                    .Trim();

                // NE SUPPRIMER QUE bytestart/byteend - GARDER TOUT LE RESTE
                var uri = new Uri(url);
                var query = HttpUtility.ParseQueryString(uri.Query);

                // ❌ NE PAS FAIRE : query.Remove("bytestart"); query.Remove("byteend");
                // ✅ À LA PLACE, reconstruire l'URL COMPLÈTE sans modifications

                return url; // Retourner l'URL originale sans modifications
            }
            catch
            {
                return url;
            }
        }

        // 2. Améliorer l'extraction Performance API
        private async Task<bool> DownloadPostByShortcodeAsync(string shortcode, string postType, string userFolder, CancellationToken token)
        {
            string failedLogPath = Path.Combine(userFolder, "failed.txt");

            try
            {
                // ✅ VÉRIFICATION IMMÉDIATE - AVANT TOUTE NAVIGATION
                if (PostAlreadyDownloaded(shortcode, userFolder, out int existingFilesCount))
                {
                    Log($"  [{shortcode}] ⊚ Déjà téléchargé ({existingFilesCount} fichier(s))");
                    return true; // SORTIE IMMÉDIATE - Aucune navigation
                }

                JObject postData = null;
                string successMethod = null;
                bool isReel = postType == "reel";

                // === STRATÉGIE 1: GraphQL SANS NAVIGATION ===
                postData = await TryGraphQLFetchAsync(shortcode, token);
                if (postData != null && ValidatePostData(postData, isReel))
                {
                    successMethod = "GraphQL";
                    Log($"  [{shortcode}] ✓ {successMethod}");

                    if (await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token))
                        return true;
                }

                // === STRATÉGIE 2: Embed API SANS NAVIGATION ===
                if (postData == null)
                {
                    postData = await TryEmbedAPIAsync(shortcode, token);
                    if (postData != null && ValidatePostData(postData, isReel))
                    {
                        successMethod = "Embed API";
                        Log($"  [{shortcode}] ✓ {successMethod}");

                        if (await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token))
                            return true;
                    }
                }

                // === STRATÉGIE 3: Performance API (AVEC navigation uniquement si nécessaire) ===
                if (postData == null && (isReel || postType == "p"))
                {
                    string videoUrl = await TryPerformanceAPIExtraction(shortcode, postType, token);
                    if (!string.IsNullOrEmpty(videoUrl))
                    {
                        postData = new JObject
                        {
                            ["__typename"] = "XDTGraphVideo",
                            ["video_url"] = videoUrl,
                            ["dimensions"] = new JObject { ["width"] = 720, ["height"] = 1280 }
                        };
                        successMethod = "Performance API";
                        Log($"  [{shortcode}] ✓ {successMethod}");

                        if (await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token))
                            return true;
                    }
                }

                // === STRATÉGIE 4: WebView (dernier recours) ===
                if (postData == null)
                {
                    postData = await TryDirectWebViewExtractionAsync(shortcode, postType, token);
                    if (postData != null && ValidatePostData(postData, isReel))
                    {
                        successMethod = "WebView";
                        Log($"  [{shortcode}] ✓ {successMethod}");

                        if (await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token))
                            return true;
                    }
                }

                // Échec final
                string reason = $"Toutes stratégies échouées";
                Log($"  [{shortcode}] ✗ {reason}");
                File.AppendAllText(failedLogPath, $"{shortcode} | {postType} | {reason}{Environment.NewLine}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"  [{shortcode}] ✗ Exception: {ex.Message}");
                File.AppendAllText(failedLogPath, $"{shortcode} | {postType} | {ex.Message}{Environment.NewLine}");
                return false;
            }
        }

        // ✅ NOUVELLE MÉTHODE: Vérifier si le post est déjà téléchargé
        private bool PostAlreadyDownloaded(string shortcode, string userFolder, out int fileCount)
        {
            fileCount = 0;

            try
            {
                // Chercher tous les fichiers correspondant au shortcode
                var patterns = new[] {
            $"{shortcode}.mp4",
            $"{shortcode}.jpg",
            $"{shortcode}_*.mp4",
            $"{shortcode}_*.jpg"
        };

                var files = new List<string>();
                foreach (var pattern in patterns)
                {
                    files.AddRange(Directory.GetFiles(userFolder, pattern));
                }

                if (files.Count == 0)
                    return false;

                // Vérifier que les fichiers ne sont pas corrompus ou vides
                var validFiles = files.Where(f =>
                {
                    var info = new FileInfo(f);
                    // Taille minimale: 5KB pour images, 100KB pour vidéos
                    bool isVideo = f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                    long minSize = isVideo ? 100 * 1024 : 5 * 1024;

                    return info.Length >= minSize;
                }).ToList();

                fileCount = validFiles.Count;
                return fileCount > 0;
            }
            catch
            {
                fileCount = 0;
                return false;
            }
        }
        private async Task<JObject> TryDirectWebViewExtractionAsync(string shortcode, string postType, CancellationToken token)
        {
            string originalUrl = null;
            try
            {
                originalUrl = webView.CoreWebView2.Source;
                string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";

                await NavigateToUrlAsync(postUrl);
                await Task.Delay(4000, token);

                var extractScript = @"
            (function() {
                try {
                    var scripts = document.querySelectorAll('script[type=""application/json""]');
                    for (var i = 0; i < scripts.length; i++) {
                        try {
                            var data = JSON.parse(scripts[i].textContent);
                            
                            function deepSearch(obj, depth) {
                                if (!obj || typeof obj !== 'object' || depth > 15) return null;
                                
                                if (obj.__typename) {
                                    var tn = obj.__typename;
                                    if ((tn.includes('Graph') || tn.includes('XDT')) && 
                                        (obj.display_url || obj.video_url || obj.edge_sidecar_to_children)) {
                                        return obj;
                                    }
                                }
                                
                                if (obj.shortcode_media) return obj.shortcode_media;
                                if (obj.xdt_shortcode_media) return obj.xdt_shortcode_media;
                                
                                for (var key in obj) {
                                    if (obj.hasOwnProperty(key)) {
                                        var found = deepSearch(obj[key], depth + 1);
                                        if (found) return found;
                                    }
                                }
                                return null;
                            }
                            
                            var result = deepSearch(data, 0);
                            if (result) return JSON.stringify(result);
                        } catch (e) {}
                    }
                    return null;
                } catch (e) {
                    return null;
                }
            })()
        ";

                var result = await webView.CoreWebView2.ExecuteScriptAsync(extractScript);

                if (!string.IsNullOrEmpty(result) && result != "null" && result != "\"\"")
                {
                    var cleaned = result.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
                    var parsed = JObject.Parse(cleaned);
                    return parsed;
                }

                return null;
            }
            catch
            {
                return null;
            }
           
        }

        // Ajoutez cette méthode manquante
        private async Task<JObject> TryHTMLFetchAsync(string url, CancellationToken token)
        {
            try
            {
                var response = await httpClient.GetAsync(url, token);
                if (!response.IsSuccessStatusCode)
                    return null;

                string html = await response.Content.ReadAsStringAsync();

                var patterns = new[]
                {
            (@"<script type=""application/json""[^>]*>(.+?)</script>", RegexOptions.Singleline),
            (@"window\._sharedData\s*=\s*({.+?});</script>", RegexOptions.Singleline),
            (@"window\.__additionalDataLoaded\([^,]+,\s*({.+?})\);", RegexOptions.Singleline)
        };

                foreach (var (pattern, options) in patterns)
                {
                    var matches = Regex.Matches(html, pattern, options);
                    foreach (Match match in matches)
                    {
                        try
                        {
                            var json = JObject.Parse(match.Groups[1].Value);
                            var found = FindPostDataRecursive(json);
                            if (found != null)
                                return found;
                        }
                        catch { }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        // Helper pour valider les données avant téléchargement
        private bool ValidatePostData(JObject postData, bool isReel)
        {
            if (postData == null) return false;

            string typename = postData["__typename"]?.ToString();

            if (isReel)
            {
                // Pour un reel, on DOIT avoir une video_url
                bool hasVideoUrl = postData["video_url"] != null || FindVideoUrlRecursive(postData) != null;
                if (!hasVideoUrl) return false;

                // Rejeter si c'est marqué comme image
                if (typename == "XDTGraphImage" || typename == "GraphImage") return false;
            }

            return true;
        }

        private async Task<string> ExecJsAsync(string script, CancellationToken token)
        {
            try
            {
                var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                if (string.IsNullOrEmpty(result) || result == "null" || result == "\"\"")
                    return null;

                return result.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            catch
            {
                return null;
            }
        }

        private async Task<JObject> TryRawHTMLExtractionAsync(string shortcode, string postType, CancellationToken token)
        {
            try
            {
                string url = $"https://www.instagram.com/{postType}/{shortcode}/";
                var response = await httpClient.GetAsync(url, token);
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync();

                // Pour vidéos/reels: chercher video_url
                var videoMatch = Regex.Match(html, @"""video_url""\s*:\s*""([^""]+)""");
                if (videoMatch.Success)
                {
                    string videoUrl = videoMatch.Groups[1].Value
                        .Replace(@"\u0026", "&")
                        .Replace(@"\/", "/");

                    if (IsValidVideoUrl(videoUrl))
                    {
                        return new JObject
                        {
                            ["__typename"] = "XDTGraphVideo",
                            ["video_url"] = videoUrl,
                            ["dimensions"] = new JObject { ["width"] = 640, ["height"] = 1280 }
                        };
                    }
                }

                // Pour images: chercher display_url
                var imageMatch = Regex.Match(html, @"""display_url""\s*:\s*""([^""]+)""");
                if (imageMatch.Success)
                {
                    string imageUrl = imageMatch.Groups[1].Value
                        .Replace(@"\u0026", "&")
                        .Replace(@"\/", "/");

                    return new JObject
                    {
                        ["__typename"] = "XDTGraphImage",
                        ["display_url"] = imageUrl,
                        ["dimensions"] = new JObject { ["width"] = 1080, ["height"] = 1080 }
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<JObject> TryGraphQLFetchAsync(string shortcode, CancellationToken token)
        {
            try
            {
                const string docId = "10015901848480474";
                string lsd = !string.IsNullOrEmpty(csrfToken) ? csrfToken : "AVqbxe3J_YA";

                var variables = new { shortcode = shortcode };
                string variablesJson = JsonConvert.SerializeObject(variables);

                var formData = new Dictionary<string, string>
        {
            { "variables", variablesJson },
            { "doc_id", docId }
        };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.instagram.com/api/graphql");
                request.Content = new FormUrlEncodedContent(formData);

                request.Headers.TryAddWithoutValidation("x-fb-lsd", lsd);
                request.Headers.TryAddWithoutValidation("x-asbd-id", "129477");
                request.Headers.TryAddWithoutValidation("referer", $"https://www.instagram.com/p/{shortcode}/");

                var response = await httpClient.SendAsync(request, token);
                if (!response.IsSuccessStatusCode) return null;

                string responseString = await response.Content.ReadAsStringAsync();
                JObject data = JObject.Parse(responseString);

                if (data["errors"] != null) return null;

                JObject media = data["data"]?["xdt_shortcode_media"] as JObject;

                if (media == null)
                {
                    var items = data["data"]?["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        media = items[0] as JObject;
                    }
                }

                if (media == null)
                {
                    media = FindPostDataRecursive(data);
                }

                return media;
            }
            catch
            {
                return null;
            }
        }

        private async Task<JObject> TryNetworkInterceptionAsync(string shortcode, string postType, CancellationToken token)
        {
            string capturedVideoUrl = null;

            try
            {
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

                void OnResponseReceived(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
                {
                    try
                    {
                        var response = JObject.Parse(e.ParameterObjectAsJson);
                        var url = response["response"]?["url"]?.ToString();
                        var mimeType = response["response"]?["mimeType"]?.ToString();

                        if (!string.IsNullOrEmpty(url) &&
                            (url.Contains("cdninstagram.com") || url.Contains("fbcdn.net")) &&
                            (mimeType?.StartsWith("video/") == true || url.Contains(".mp4")))
                        {
                            if (IsValidVideoUrl(url))
                            {
                                capturedVideoUrl = url;
                            }
                        }
                    }
                    catch { }
                }

                var receiver = webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");
                receiver.DevToolsProtocolEventReceived += OnResponseReceived;

                try
                {
                    string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";
                    await NavigateToUrlAsync(postUrl);
                    await Task.Delay(3000, token);
                    await ExecJsAsync("performance.clearResourceTimings();", token);
                    await webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, 500);");
                    await Task.Delay(1000, token);

                    if (!string.IsNullOrEmpty(capturedVideoUrl))
                    {
                        return new JObject
                        {
                            ["__typename"] = "XDTGraphVideo",
                            ["video_url"] = capturedVideoUrl,
                            ["dimensions"] = new JObject { ["width"] = 720, ["height"] = 1280 }
                        };
                    }
                }
                finally
                {
                    receiver.DevToolsProtocolEventReceived -= OnResponseReceived;
                    await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool IsValidVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!url.Contains("cdninstagram.com") && !url.Contains("fbcdn.net")) return false;
            if (url.Contains("/t51.") || url.Contains("/v/t") || url.Contains("video")) return true;
            if (url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            return false;
        }

        private string FindVideoUrlRecursive(JToken token, int depth = 0)
        {
            if (token == null || depth > 10) return null;

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var videoUrl = obj["video_url"]?.ToString();
                if (!string.IsNullOrEmpty(videoUrl)) return videoUrl;

                foreach (var prop in obj.Properties())
                {
                    var result = FindVideoUrlRecursive(prop.Value, depth + 1);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    var result = FindVideoUrlRecursive(item, depth + 1);
                    if (!string.IsNullOrEmpty(result)) return result;
                }
            }

            return null;
        }

        private JObject FindPostDataRecursive(JToken token, int depth = 0)
        {
            if (token == null || token.Type == JTokenType.Null || depth > 15) return null;

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                string typename = obj["__typename"]?.ToString();

                if (!string.IsNullOrEmpty(typename) &&
                    (typename.StartsWith("XDTGraph") || typename.StartsWith("Graph")))
                {
                    if (obj["display_url"] != null || obj["video_url"] != null || obj["edge_sidecar_to_children"] != null)
                        return obj;
                }

                foreach (var prop in obj.Properties())
                {
                    var result = FindPostDataRecursive(prop.Value, depth + 1);
                    if (result != null) return result;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    var result = FindPostDataRecursive(item, depth + 1);
                    if (result != null) return result;
                }
            }

            return null;
        }

        private async Task<bool> DownloadMediaFromPostDataAsync(string shortcode, string postType, JObject postData, string userFolder, CancellationToken token)
        {
            List<MediaInfo> mediaList = new List<MediaInfo>();
            string logType = "";
            string typeName = postData["__typename"]?.ToString();

            // ... [Garde ton code d'extraction des médias existant] ...

            if (typeName == "XDTGraphImage" || typeName == "GraphImage")
            {
                logType = "Photo";
                string url = postData["display_url"]?.ToString();
                if (!string.IsNullOrEmpty(url))
                {
                    int width = postData["dimensions"]?["width"]?.ToObject<int>() ?? 0;
                    int height = postData["dimensions"]?["height"]?.ToObject<int>() ?? 0;
                    mediaList.Add(new MediaInfo { Url = url, Type = "image", Width = width, Height = height });
                }
            }
            else if (typeName == "XDTGraphVideo" || typeName == "GraphVideo")
            {
                logType = "Vidéo/Reel";
                string videoUrl = postData["video_url"]?.ToString();

                if (string.IsNullOrEmpty(videoUrl))
                    videoUrl = FindVideoUrlRecursive(postData);

                if (!string.IsNullOrEmpty(videoUrl))
                {
                    videoUrl = videoUrl.Replace(@"\u0026", "&").Replace(@"\/", "/").Replace("&amp;", "&");

                    if (IsValidVideoUrl(videoUrl))
                    {
                        int width = postData["dimensions"]?["width"]?.ToObject<int>() ?? 640;
                        int height = postData["dimensions"]?["height"]?.ToObject<int>() ?? 1280;
                        mediaList.Add(new MediaInfo { Url = videoUrl, Type = "video", Width = width, Height = height });
                    }
                    else
                    {
                        Log($"  [{shortcode}] ⚠ URL vidéo invalide");
                        return false;
                    }
                }
                else
                {
                    Log($"  [{shortcode}] ⚠ Pas de video_url trouvée");
                    return false;
                }
            }
            else if (typeName == "XDTGraphSidecar" || typeName == "GraphSidecar")
            {
                logType = "Carrousel";
                var edges = postData["edge_sidecar_to_children"]?["edges"] as JArray;
                if (edges != null)
                {
                    foreach (var edge in edges)
                    {
                        var node = edge["node"];
                        string childType = node["__typename"]?.ToString();
                        string url = null;
                        string mType = "image";

                        if (childType == "XDTGraphVideo" || childType == "GraphVideo")
                        {
                            url = node["video_url"]?.ToString();
                            if (string.IsNullOrEmpty(url)) url = FindVideoUrlRecursive(node);

                            if (!string.IsNullOrEmpty(url))
                            {
                                url = url.Replace(@"\u0026", "&").Replace(@"\/", "/");
                                if (!IsValidVideoUrl(url)) continue;
                            }
                            mType = "video";
                        }
                        else
                        {
                            url = node["display_url"]?.ToString();
                            mType = "image";
                        }

                        if (!string.IsNullOrEmpty(url))
                        {
                            int width = node["dimensions"]?["width"]?.ToObject<int>() ?? 0;
                            int height = node["dimensions"]?["height"]?.ToObject<int>() ?? 0;
                            mediaList.Add(new MediaInfo { Url = url, Type = mType, Width = width, Height = height });
                        }
                    }
                }
            }
            else
            {
                Log($"  [{shortcode}] Type inconnu: {typeName}");
                return false;
            }

            if (mediaList.Count == 0)
            {
                Log($"  [{shortcode}] Aucun média trouvé");
                return false;
            }

            Log($"  [{shortcode}] {logType} - {mediaList.Count} média(s)");

            int downloaded = 0;
            int skipped = 0;
            var refererUrl = $"https://www.instagram.com/{postType}/{shortcode}/";

            for (int i = 0; i < mediaList.Count; i++)
            {
                try
                {
                    var media = mediaList[i];

                    // ✅ VÉRIFICATION 2: Vérifier si ce média spécifique existe déjà
                    string ext = media.Type == "video" ? "mp4" : "jpg";
                    string fileName = mediaList.Count == 1
                        ? $"{shortcode}.{ext}"
                        : $"{shortcode}_{i + 1:D2}.{ext}";

                    string filePath = Path.Combine(userFolder, fileName);

                    if (File.Exists(filePath))
                    {
                        var existingInfo = new FileInfo(filePath);

                        // Vérifier que le fichier existant est valide
                        bool isValid = false;

                        if (media.Type == "video")
                        {
                            // Pour vidéo: vérifier taille > 500KB ET magic bytes MP4
                            if (existingInfo.Length >= 500 * 1024)
                            {
                                byte[] header = new byte[12];
                                using (var fs = File.OpenRead(filePath))
                                {
                                    fs.Read(header, 0, 12);
                                }

                                bool hasFtyp = (header[4] == 0x66 && header[5] == 0x74 &&
                                               header[6] == 0x79 && header[7] == 0x70);
                                bool hasMoov = (header[4] == 0x6D && header[5] == 0x6F &&
                                               header[6] == 0x6F && header[7] == 0x76);

                                isValid = hasFtyp || hasMoov;
                            }
                        }
                        else
                        {
                            // Pour image: vérifier taille > 5KB
                            isValid = existingInfo.Length >= 5 * 1024;
                        }

                        if (isValid)
                        {
                            Log($"    ⊚ {fileName} existe déjà et est valide");
                            downloaded++; // Compter comme téléchargé
                            skipped++;
                            continue;
                        }
                        else
                        {
                            Log($"    ⚠ {fileName} existe mais est invalide, re-téléchargement...");
                            File.Delete(filePath); // Supprimer le fichier corrompu
                        }
                    }

                    // ✅ Télécharger uniquement si nécessaire
                    byte[] contentBytes = null;
                    int maxRetries = 3;

                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                cts.CancelAfter(TimeSpan.FromSeconds(30));

                                var decodedUrl = HttpUtility.HtmlDecode(media.Url)
                                    ?.Replace("\\u0026", "&")
                                    ?.Replace("&amp;", "&");

                                if (string.IsNullOrWhiteSpace(decodedUrl))
                                {
                                    Log($"    ✗ URL invalide pour le média {i + 1}");
                                    break;
                                }

                                using (var req = new HttpRequestMessage(HttpMethod.Get, decodedUrl))
                                {
                                    try { req.Headers.Referrer = new Uri(refererUrl); } catch { }
                                    req.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                                    string acceptType = media.Type == "video" ? "video/*" : "image/*";
                                    req.Headers.TryAddWithoutValidation("Accept", acceptType);

                                    if (!string.IsNullOrEmpty(instagramCookies))
                                    {
                                        req.Headers.TryAddWithoutValidation("Cookie", instagramCookies);
                                    }

                                    var mediaResponse = await httpClient.SendAsync(req, cts.Token);

                                    if (mediaResponse.IsSuccessStatusCode)
                                    {
                                        contentBytes = await mediaResponse.Content.ReadAsByteArrayAsync();

                                        // Validation stricte
                                        if (media.Type == "video")
                                        {
                                            if (!IsValidMP4File(contentBytes))
                                            {
                                                Log($"    ✗ Média {i + 1}: Fichier MP4 invalide");
                                                contentBytes = null;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            if (contentBytes.Length < 5 * 1024)
                                            {
                                                Log($"    ✗ Image {i + 1} trop petite");
                                                contentBytes = null;
                                                break;
                                            }
                                        }

                                        break; // Succès
                                    }
                                    else if (mediaResponse.StatusCode == HttpStatusCode.Forbidden)
                                    {
                                        if (retry < maxRetries - 1)
                                        {
                                            await Task.Delay(2000 * (retry + 1), token);
                                            continue;
                                        }
                                    }
                                    else if (mediaResponse.StatusCode == HttpStatusCode.NotFound)
                                    {
                                        Log($"    ✗ Média supprimé (404)");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            if (retry < maxRetries - 1)
                                await Task.Delay(1000, token);
                        }
                        catch (Exception ex)
                        {
                            if (retry < maxRetries - 1)
                                await Task.Delay(1000, token);
                        }
                    }

                    if (contentBytes == null || contentBytes.Length == 0)
                    {
                        Log($"    ✗ Fichier {i + 1}: échec du téléchargement");
                        File.AppendAllText(Path.Combine(userFolder, "failed.txt"),
                            $"{shortcode} | Media {i + 1} | échec du téléchargement{Environment.NewLine}");
                        continue;
                    }

                    // Sauvegarder
                    await File.WriteAllBytesAsync(filePath, contentBytes, token);

                    double mb = contentBytes.Length / 1024.0 / 1024.0;
                    Log($"    ✓ {fileName} ({mb:F1}MB) [{media.Width}x{media.Height}]");
                    downloaded++;
                }
                catch (Exception ex)
                {
                    Log($"    ✗ Média {i + 1}: {ex.Message}");
                }
            }

            if (downloaded > 0 || skipped > 0)
            {
                Log($"  [{shortcode}] ✓ {downloaded}/{mediaList.Count} téléchargés{(skipped > 0 ? $" ({skipped} déjà présents)" : "")}");
                return true;
            }

            return false;
        }


        private bool IsImageFile(byte[] data)
        {
            if (data == null || data.Length < 4) return false;

            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return true;

            // PNG: 89 50 4E 47
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return true;

            // GIF: 47 49 46 38
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
                return true;

            // WebP
            if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 &&
                data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                return true;

            return false;
        }

        private async Task NavigateToUrlAsync(string url)
        {
            await _navigationLock.WaitAsync();
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                void Handler(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    webView.CoreWebView2.NavigationCompleted -= Handler;
                    tcs.TrySetResult(e.IsSuccess);
                }

                cts.Token.Register(() =>
                {
                    webView.CoreWebView2.NavigationCompleted -= Handler;
                    tcs.TrySetCanceled();
                });

                webView.CoreWebView2.NavigationCompleted += Handler;

                try
                {
                    webView.CoreWebView2.Navigate(url);
                    await tcs.Task;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Navigation timeout vers {url}");
                }
                finally
                {
                    cts.Dispose();
                }
            }
            finally
            {
                _navigationLock.Release();
            }
        }

        private void Log(string message)
        {
            try
            {
                if (logTextBox.InvokeRequired)
                {
                    logTextBox.Invoke(new Action(() =>
                    {
                        logTextBox.AppendText($"[DL] {message}\r\n");
                        logTextBox.SelectionStart = logTextBox.Text.Length;
                        logTextBox.ScrollToCaret();
                    }));
                }
                else
                {
                    logTextBox.AppendText($"[DL] {message}\r\n");
                    logTextBox.SelectionStart = logTextBox.Text.Length;
                    logTextBox.ScrollToCaret();
                }
            }
            catch { }
        }

        private class MediaInfo
        {
            public string Url { get; set; }
            public string Type { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
    }
}