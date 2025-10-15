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

                foreach (var username in targets)
                {
                    if (token.IsCancellationRequested) break;
                    await DownloadUserContentAsync(username, destinationFolder, token);
                    await Task.Delay(2000, token);
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

        private async Task DownloadUserContentAsync(string username, string baseFolder, CancellationToken token)
        {
            try
            {
                Log($"@{username}");
                string userFolder = Path.Combine(baseFolder, username);
                Directory.CreateDirectory(userFolder);
                string failedLogPath = Path.Combine(userFolder, "failed.txt");

                await NavigateToUrlAsync($"https://www.instagram.com/{username}/");
                await Task.Delay(3000, token);

                var posts = await ExtractAllShortcodesFromProfileAsync(token);

                if (posts.Count == 0)
                {
                    Log("  Aucun post ou profil privé");
                    return;
                }

                Log($"  {posts.Count} posts détectés");

                // AJOUT : Attendre un peu avant de commencer les téléchargements
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

                Log($"✓ {success}/{posts.Count} téléchargés");
            }
            catch (Exception ex)
            {
                Log($"Erreur @{username}: {ex.Message}");
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

      

        private async Task<bool> DownloadPostByShortcodeAsync(string shortcode, string postType, string userFolder, CancellationToken token)
        {
            string failedLogPath = Path.Combine(userFolder, "failed.txt");

            void LogFailure(string reason)
            {
                try
                {
                    File.AppendAllText(failedLogPath, $"{shortcode} | {postType} | {reason}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    Log($"  [FAILED_LOG_ERROR] Impossible d'écrire dans failed.txt : {ex.Message}");
                }
            }
            try
            {
                JObject postData = null;
                string successMethod = null;
                bool isReel = postType == "reel";

                // ============================================
                // STRATÉGIE 1: Performance API améliorée
                // ============================================
                if (isReel || postType == "p")
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(15));
                        try
                        {
                            string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";
                            await NavigateToUrlAsync(postUrl);

                            // Attendre le chargement
                            await Task.Delay(3000, cts.Token);

                            // Forcer le chargement de la vidéo
                            await ExecJsAsync("document.querySelector('video')?.play();", cts.Token);
                            await Task.Delay(3000, cts.Token);

                            // --- Nouveau script : renvoie toutes les URLs MP4 trouvées ---
                            string rawVideoListJson = await ExecJsAsync(@"
(function() {
  try {
    const vids = performance.getEntriesByType('resource')
      .filter(r => r.name.includes('.mp4') && (r.name.includes('cdninstagram') || r.name.includes('fbcdn')))
      .map(r => r.name);
    return JSON.stringify(vids);
  } catch(e) {
    return '[]';
  }
})();
", cts.Token);

                            string videoUrl = null;

                            // --- Parsing JSON et sélection de la meilleure URL ---
                            if (!string.IsNullOrEmpty(rawVideoListJson))
                            {
                                try
                                {
                                    var list = JsonConvert.DeserializeObject<List<string>>(rawVideoListJson);
                                    if (list != null && list.Count > 0)
                                    {
                                        // Prioriser la plus longue URL (souvent la vraie vidéo principale)
                                        string bestUrl = list.OrderByDescending(u => u.Length).FirstOrDefault();

                                        if (IsValidVideoUrl(bestUrl))
                                        {
                                            videoUrl = CleanVideoUrl(bestUrl);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"[PERF_PARSE] Erreur parsing vidéo: {ex.Message}");
                                }
                            }

                            // --- Vérification et téléchargement ---
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
                                return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);
                            }
                        }
                        catch
                        {
                            // Ignorer les erreurs silencieuses sur cette stratégie
                        }
                    }
                }


                // ============================================
                // STRATÉGIE 2: GraphQL Direct (pas de navigation supplémentaire)
                // ============================================
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        postData = await TryGraphQLFetchAsync(shortcode, cts.Token);
                        if (postData != null && ValidatePostData(postData, isReel))
                        {
                            successMethod = "GraphQL";
                            Log($"  [{shortcode}] ✓ {successMethod}");
                            return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);
                        }
                    }
                    catch { }
                }

                // ============================================
                // STRATÉGIE 3: Raw HTML Extraction (pas de navigation supplémentaire)
                // ============================================
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(4));
                    try
                    {
                        postData = await TryRawHTMLExtractionAsync(shortcode, postType, cts.Token);
                        if (postData != null && ValidatePostData(postData, isReel))
                        {
                            successMethod = "Raw HTML";
                            Log($"  [{shortcode}] ✓ {successMethod}");
                            return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);
                        }
                    }
                    catch { }
                }
                // STRATÉGIE 4: WebView Extraction (ancien code)
                if (postData == null && !isReel)
                {
                    try
                    {
                        postData = await TryDirectWebViewExtractionAsync(shortcode, postType, token);
                        if (postData != null && ValidatePostData(postData, isReel))
                        {
                            successMethod = "WebView Extraction";
                            Log($"  [{shortcode}] ✓ {successMethod}");
                            return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);
                        }
                    }
                    catch { }
                }

                // ============================================
                // STRATÉGIE 5: HTML Fetch via HttpClient
                // ============================================
                if (postData == null)
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(5));
                        try
                        {
                            postData = await TryHTMLFetchAsync($"https://www.instagram.com/{postType}/{shortcode}/", cts.Token);
                            if (postData != null && ValidatePostData(postData, isReel))
                            {
                                Log($"  [{shortcode}] ✓ HTML Fetch");
                                return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);
                            }
                        }
                        catch { }
                    }
                }
                // Échec
               LogFailure($"  [{shortcode}] Toutes stratégies échouées");
                return false;
            }
            catch (Exception ex)
            {
                LogFailure(ex.Message);
                return false;
            }
        }
        private string CleanVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            try
            {
                // Nettoyer les caractères encodés
                url = url
                    .Replace("\\u0026", "&")
                    .Replace(@"\/", "/")
                    .Replace("&amp;", "&")
                    .Trim();

                // IMPORTANT: NE PAS supprimer tous les params !
                // On garde les params d'authentification Instagram (efg, _nc_ht, _nc_cat, etc.)
                // On supprime SEULEMENT les byte ranges qui causent des téléchargements partiels
                var uri = new Uri(url);
                var query = HttpUtility.ParseQueryString(uri.Query);

                // Supprimer UNIQUEMENT les params de range
                query.Remove("bytestart");
                query.Remove("byteend");

                // Reconstruire l'URL avec les params nécessaires
                url = new UriBuilder(uri) { Query = query.ToString() }.Uri.ToString();

                return url;
            }
            catch
            {
                return url;
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
            finally
            {
                if (!string.IsNullOrEmpty(originalUrl))
                {
                    try
                    {
                        await NavigateToUrlAsync(originalUrl);
                    }
                    catch { }
                }
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

                // Chercher les données
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
                {
                    videoUrl = FindVideoUrlRecursive(postData);
                }

                if (!string.IsNullOrEmpty(videoUrl))
                {
                    videoUrl = videoUrl
                        .Replace(@"\u0026", "&")
                        .Replace(@"\/", "/")
                        .Replace("&amp;", "&");

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

            Log($"  [{shortcode}] {logType} - {mediaList.Count} média(s)");

            if (mediaList.Count == 0)
            {
                Log($"  [{shortcode}] Aucun média trouvé");
                return false;
            }

            int downloaded = 0;
            var refererUrl = $"https://www.instagram.com/{postType}/{shortcode}/";

            for (int i = 0; i < mediaList.Count; i++)
            {
                try
                {
                    var media = mediaList[i];
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

                                        // Validation: vérifier que c'est le bon type de fichier
                                        string contentType = mediaResponse.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                                        if (media.Type == "video")
                                        {
                                            if (contentType.StartsWith("image/"))
                                            {
                                                Log($"    ✗ Média {i + 1}: Image reçue au lieu de vidéo");
                                                contentBytes = null;
                                                break;
                                            }

                                            if (contentBytes.Length >= 12)
                                            {
                                                if (IsImageFile(contentBytes))
                                                {
                                                    Log($"    ✗ Média {i + 1}: Fichier image détecté (magic bytes)");
                                                    contentBytes = null;
                                                    break;
                                                }
                                            }
                                        }

                                        break;
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
                            {
                                await Task.Delay(1000, token);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (retry < maxRetries - 1)
                            {
                                await Task.Delay(1000, token);
                            }
                        }
                    }

                    if (contentBytes == null || contentBytes.Length == 0)
                    {
                        Log($"    ✗ Fichier {i + 1}: échec du téléchargement");
                        File.AppendAllText(Path.Combine(userFolder, "failed.txt"), $"{shortcode} | Media {i + 1} | échec du téléchargement{Environment.NewLine}");

                        continue;
                    }

                    int minSize = media.Type == "video" ? 10 * 1024 : 5 * 1024;
                    if (contentBytes.Length < minSize)
                    {
                        Log($"    ✗ Fichier {i + 1} trop petit ({contentBytes.Length / 1024}KB)");
                        continue;
                    }

                    string ext = media.Type == "video" ? "mp4" : "jpg";
                    string fileName = mediaList.Count == 1
                        ? $"{shortcode}.{ext}"
                        : $"{shortcode}_{i + 1:D2}.{ext}";

                    string filePath = Path.Combine(userFolder, fileName);

                    if (File.Exists(filePath))
                    {
                        var existingInfo = new FileInfo(filePath);

                        if (existingInfo.Length > 0)
                        {
                            if (media.Type == "video")
                            {
                                byte[] existingBytes = File.ReadAllBytes(filePath);

                                if (IsImageFile(existingBytes))
                                {
                                    Log($"    ⚠ {fileName} existe mais c'est une image, re-téléchargement...");
                                    string backupPath = filePath.Replace(".mp4", "_old.jpg");
                                    try { File.Move(filePath, backupPath); } catch { }
                                }
                                else if (existingBytes.Length >= contentBytes.Length * 0.9)
                                {
                                    Log($"    ⊚ {fileName} existe déjà (vidéo valide)");
                                    downloaded++;
                                    continue;
                                }
                            }
                            else
                            {
                                if (Math.Abs(existingInfo.Length - contentBytes.Length) < 1024)
                                {
                                    Log($"    ⊚ {fileName} existe déjà");
                                    downloaded++;
                                    continue;
                                }
                            }
                        }
                    }

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

            if (downloaded > 0)
            {
                Log($"  [{shortcode}] ✓ {downloaded}/{mediaList.Count}");
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