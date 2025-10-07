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
        private string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public DownloadInstagramService(WebView2 webView, TextBox logTextBox, Profile profile, Form parentForm)
        {
            this.webView = webView;
            this.logTextBox = logTextBox;
            this.profile = profile;
            this.parentForm = parentForm;
            this.httpClient = new HttpClient();
            this.httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            this.httpClient.DefaultRequestHeaders.Add("x-ig-app-id", "936619743392459");
            this.httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            this.httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            this.httpClient.Timeout = TimeSpan.FromMinutes(10);
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
                            SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) // Default Desktop
                        };
                        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                        {
                            Logger.LogWarning("Téléchargement annulé: aucun dossier sélectionné.");
                            return;
                        }
                        destinationFolder = dialog.SelectedPath;
                    }
                    catch (System.Runtime.InteropServices.SEHException ex) // Catch Win32 bug
                    {
                        Logger.LogError("Erreur dialog dossier (SEH): " + ex.Message + ". Utilise default: Desktop.");
                        destinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
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

                await NavigateToUrlAsync($"https://www.instagram.com/{username}/");
                await Task.Delay(3000, token);

                var posts = await ExtractAllShortcodesFromProfileAsync(token);

                if (posts.Count == 0)
                {
                    Log("  Aucun post ou profil privé");
                    return;
                }

                Log($"  {posts.Count} posts détectés");

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
            const int maxNoNew = 5;
            int scrollAttempts = 0;
            const int maxScrolls = 100;

            try
            {
                while (scrollAttempts < maxScrolls)
                {
                    var script = @"
                        JSON.stringify(
                            Array.from(document.querySelectorAll('a[href*=""/p/""], a[href*=""/reel/""]'))
                                .map(a => a.href)
                        )
                    ";

                    var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    var urls = JArray.Parse(result.Trim('"').Replace("\\\"", "\""));

                    foreach (var url in urls)
                    {
                        var match = Regex.Match(url.ToString(), @"/(p|reel)/([A-Za-z0-9_-]+)");
                        if (match.Success)
                        {
                            string type = match.Groups[1].Value;
                            string shortcode = match.Groups[2].Value;
                            posts.Add((shortcode, type));
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

                    await webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, window.innerHeight * 1.5);");
                    await Task.Delay(1500, token);

                    var endScript = @"
                        (function() {
                            var hasEnd = document.body.innerHTML.includes('Fin des publications') || 
                                         document.body.innerHTML.includes('No more posts');
                            var hasProgress = document.querySelector('[role=""progressbar""]') !== null;
                            var atBottom = (window.innerHeight + window.scrollY) >= document.body.scrollHeight - 100;
                            return hasEnd || (!hasProgress && atBottom);
                        })()
                    ";

                    var isEnd = await webView.CoreWebView2.ExecuteScriptAsync(endScript);
                    if (bool.TryParse(isEnd.Trim('"'), out bool endReached) && endReached)
                    {
                        break;
                    }

                    scrollAttempts++;
                }
            }
            catch { }

            return posts.ToList();
        }

        private async Task<bool> DownloadPostByShortcodeAsync(string shortcode, string postType, string userFolder, CancellationToken token)
        {
            try
            {
                JObject postData = null;
                string successMethod = null;

                // STRATÉGIE 1: GraphQL direct (plus rapide, pas de navigation)
                try
                {
                    postData = await TryGraphQLFetchAsync(shortcode, token);
                    if (postData != null)
                    {
                        successMethod = "GraphQL Direct";
                        Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"  [{shortcode}] GraphQL Direct échoué: {ex.Message}");
                }

                // STRATÉGIE 2: Alternative GraphQL (query_hash)
                if (postData == null)
                {
                    try
                    {
                        postData = await TryAlternativeGraphQLAsync(shortcode, token);
                        if (postData != null)
                        {
                            successMethod = "Alternative GraphQL";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] Alternative GraphQL échoué: {ex.Message}");
                    }
                }
                // STRATÉGIE 2.5: Raw HTML Extraction (direct regex on HTML)
                if (postData == null)
                {
                    try
                    {
                        postData = await TryRawHTMLExtractionAsync(shortcode, postType, token);
                        if (postData != null)
                        {
                            successMethod = "Raw HTML Extraction";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] Raw HTML Extraction échoué: {ex.Message}");
                    }
                }
                // STRATÉGIE 3: Extraction via WebView (navigation sur le post)
                if (postData == null)
                {
                    try
                    {
                        postData = await TryDirectWebViewExtractionAsync(shortcode, postType, token);
                        if (postData != null)
                        {
                            successMethod = "WebView Extraction";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                        else
                        {
                            Log($"  [{shortcode}] WebView Extraction: aucune donnée trouvée");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] WebView Extraction échoué: {ex.Message}");
                    }
                }

                // STRATÉGIE 4: HTML scraping via HttpClient
                if (postData == null)
                {
                    try
                    {
                        string url = $"https://www.instagram.com/{postType}/{shortcode}/";
                        postData = await TryHTMLFetchAsync(url, token);
                        if (postData != null)
                        {
                            successMethod = "HTML Fetch";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                        else
                        {
                            Log($"  [{shortcode}] HTML Fetch: aucune donnée trouvée");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] HTML Fetch échoué: {ex.Message}");
                    }
                }

                // STRATÉGIE 5: Embedded endpoint (undocumented)
                if (postData == null)
                {
                    try
                    {
                        postData = await TryEmbedEndpointAsync(shortcode, token);
                        if (postData != null)
                        {
                            successMethod = "Embed Endpoint";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                        else
                        {
                            Log($"  [{shortcode}] Embed Endpoint: aucune donnée trouvée");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] Embed Endpoint échoué: {ex.Message}");
                    }
                }

                // STRATÉGIE 6: Last resort - essayer de capturer via Network interception
                if (postData == null)
                {
                    try
                    {
                        postData = await TryNetworkInterceptionAsync(shortcode, postType, token);
                        if (postData != null)
                        {
                            successMethod = "Network Interception";
                            Log($"  [{shortcode}] ✓ Méthode: {successMethod}");
                        }
                        else
                        {
                            Log($"  [{shortcode}] Network Interception: aucune donnée trouvée");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] Network Interception échoué: {ex.Message}");
                    }
                }

                // NEW STRATÉGIE 7: Try direct URL access to check if post exists
                if (postData == null)
                {
                    try
                    {
                        string url = $"https://www.instagram.com/{postType}/{shortcode}/";
                        var response = await httpClient.GetAsync(url, token);

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            Log($"  [{shortcode}] ✗ Post supprimé ou introuvable (404)");
                            return false;
                        }
                        else if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            Log($"  [{shortcode}] ✗ Accès refusé (403) - Post privé ou restreint");
                            return false;
                        }
                        else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            Log($"  [{shortcode}] ✗ Rate limit atteint (429) - Trop de requêtes");
                            return false;
                        }
                        else
                        {
                            Log($"  [{shortcode}] ⚠ Post existe (HTTP {(int)response.StatusCode}) mais données non extraites");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  [{shortcode}] Vérification URL échouée: {ex.Message}");
                    }
                }

                if (postData == null)
                {
                    Log($"  [{shortcode}] ✗ Échec final - Toutes les méthodes ont échoué");
                    return false;
                }

                return await DownloadMediaFromPostDataAsync(shortcode, postType, postData, userFolder, token);

            }
            catch (Exception ex)
            {
                Log($"  [{shortcode}] Erreur globale: {ex.Message}");
                return false;
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

                var headers = new Dictionary<string, string>
                {
                    { "x-fb-lsd", lsd },
                    { "x-asbd-id", "129477" },
                    { "sec-fetch-site", "same-origin" },
                    { "sec-fetch-mode", "cors" },
                    { "sec-fetch-dest", "empty" },
                    { "referer", $"https://www.instagram.com/p/{shortcode}/" }
                };

                foreach (var header in headers)
                {
                    if (!httpClient.DefaultRequestHeaders.Contains(header.Key))
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }

                var response = await httpClient.SendAsync(request, token);

                // Log HTTP error details
                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Log($"  [{shortcode}] GraphQL HTTP {(int)response.StatusCode}: {errorBody.Substring(0, Math.Min(100, errorBody.Length))}");
                    return null;
                }

                string responseString = await response.Content.ReadAsStringAsync();

                // Check for empty or error responses
                if (string.IsNullOrWhiteSpace(responseString))
                {
                    Log($"  [{shortcode}] GraphQL: réponse vide");
                    return null;
                }

                JObject data = JObject.Parse(responseString);

                // Check for GraphQL errors
                if (data["errors"] != null)
                {
                    Log($"  [{shortcode}] GraphQL errors: {data["errors"].ToString()}");
                    return null;
                }

                // ENHANCED: Try multiple response structures
                JObject media = null;

                // Structure 1: data.xdt_shortcode_media (most common)
                media = data["data"]?["xdt_shortcode_media"] as JObject;

                // Structure 2: data.items[0] (carousel/collection posts)
                if (media == null)
                {
                    var items = data["data"]?["items"] as JArray;
                    if (items != null && items.Count > 0)
                    {
                        media = items[0] as JObject;
                        if (media != null)
                        {
                            Log($"  [{shortcode}] GraphQL: données trouvées dans items[0]");
                        }
                    }
                }

                // Structure 3: data.xdt_api__v1__media__shortcode__web_info.items[0]
                if (media == null)
                {
                    var webInfo = data["data"]?["xdt_api__v1__media__shortcode__web_info"];
                    if (webInfo != null)
                    {
                        var webInfoItems = webInfo["items"] as JArray;
                        if (webInfoItems != null && webInfoItems.Count > 0)
                        {
                            media = webInfoItems[0] as JObject;
                            if (media != null)
                            {
                                Log($"  [{shortcode}] GraphQL: données trouvées dans xdt_api__v1__media__shortcode__web_info.items[0]");
                            }
                        }
                    }
                }

                // Structure 4: Recursively search entire response for media objects
                if (media == null)
                {
                    media = FindPostDataRecursive(data);
                    if (media != null)
                    {
                        Log($"  [{shortcode}] GraphQL: données trouvées via recherche récursive");
                    }
                }

                if (media == null)
                {
                    // Save raw response for debugging
                    string debugFile = Path.Combine(Path.GetTempPath(), $"ig_graphql_debug_{shortcode}.json");
                    File.WriteAllText(debugFile, data.ToString(Formatting.Indented));
                    Log($"  [{shortcode}] GraphQL: xdt_shortcode_media null - réponse sauvegardée: {debugFile}");
                }

                return media;
            }
            catch (Exception ex)
            {
                Log($"  [{shortcode}] GraphQL exception: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        private async Task<JObject> TryAlternativeGraphQLAsync(string shortcode, CancellationToken token)
        {
            try
            {
                const string queryHash = "9f8827793ef34641b2fb195d4d41151c";

                var variables = new { shortcode = shortcode };
                string variablesJson = JsonConvert.SerializeObject(variables);

                string requestUrl = $"https://www.instagram.com/graphql/query/?query_hash={queryHash}&variables={Uri.EscapeDataString(variablesJson)}";

                var response = await httpClient.GetAsync(requestUrl, token);

                if (!response.IsSuccessStatusCode)
                    return null;

                string responseString = await response.Content.ReadAsStringAsync();
                JObject data = JObject.Parse(responseString);

                return data["data"]?["shortcode_media"] as JObject;
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
                if (!response.IsSuccessStatusCode)
                    return null;

                string html = await response.Content.ReadAsStringAsync();

                // Pattern 1: Look for video_url directly in HTML
                var videoMatch = Regex.Match(html, @"""video_url""\s*:\s*""([^""]+)""");
                if (videoMatch.Success)
                {
                    string videoUrl = videoMatch.Groups[1].Value.Replace(@"\u0026", "&");

                    // Try to get dimensions
                    var widthMatch = Regex.Match(html, @"""video_width""\s*:\s*(\d+)");
                    var heightMatch = Regex.Match(html, @"""video_height""\s*:\s*(\d+)");

                    int width = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0;
                    int height = heightMatch.Success ? int.Parse(heightMatch.Groups[1].Value) : 0;

                    return new JObject
                    {
                        ["__typename"] = "XDTGraphVideo",
                        ["video_url"] = videoUrl,
                        ["dimensions"] = new JObject { ["width"] = width, ["height"] = height }
                    };
                }

                // Pattern 2: Look for display_url (images)
                var imageMatch = Regex.Match(html, @"""display_url""\s*:\s*""([^""]+)""");
                if (imageMatch.Success)
                {
                    string imageUrl = imageMatch.Groups[1].Value.Replace(@"\u0026", "&");

                    var widthMatch = Regex.Match(html, @"""dimensions""\s*:\s*\{\s*""height""\s*:\s*(\d+)\s*,\s*""width""\s*:\s*(\d+)");
                    int width = widthMatch.Success ? int.Parse(widthMatch.Groups[2].Value) : 0;
                    int height = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0;

                    return new JObject
                    {
                        ["__typename"] = "XDTGraphImage",
                        ["display_url"] = imageUrl,
                        ["dimensions"] = new JObject { ["width"] = width, ["height"] = height }
                    };
                }

                // Pattern 3: Check for carousel (multiple images/videos)
                if (html.Contains("edge_sidecar_to_children"))
                {
                    var carouselMatches = Regex.Matches(html, @"""(video_url|display_url)""\s*:\s*""([^""]+)""");
                    if (carouselMatches.Count > 0)
                    {
                        var edges = new JArray();

                        foreach (Match match in carouselMatches)
                        {
                            bool isVideo = match.Groups[1].Value == "video_url";
                            string mediaUrl = match.Groups[2].Value.Replace(@"\u0026", "&");

                            edges.Add(new JObject
                            {
                                ["node"] = new JObject
                                {
                                    ["__typename"] = isVideo ? "XDTGraphVideo" : "XDTGraphImage",
                                    [match.Groups[1].Value] = mediaUrl,
                                    ["dimensions"] = new JObject { ["width"] = 1080, ["height"] = 1080 }
                                }
                            });
                        }

                        if (edges.Count > 0)
                        {
                            return new JObject
                            {
                                ["__typename"] = "XDTGraphSidecar",
                                ["edge_sidecar_to_children"] = new JObject { ["edges"] = edges }
                            };
                        }
                    }
                }

                // Pattern 4: Try to find og:image meta tag as last resort
                var ogImageMatch = Regex.Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
                if (ogImageMatch.Success)
                {
                    string imageUrl = ogImageMatch.Groups[1].Value;
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

        private async Task<JObject> TryDirectWebViewExtractionAsync(string shortcode, string postType, CancellationToken token)
        {
            string originalUrl = null;
            try
            {
                originalUrl = webView.CoreWebView2.Source;
                string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";

                await NavigateToUrlAsync(postUrl);

                // Attendre que la page charge complètement
                await Task.Delay(4000, token);

                // Forcer le rendu en scrollant légèrement
                await webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, 100);");
                await Task.Delay(500, token);

                var extractScript = @"
                    (function() {
                        try
                            {
                                // Méthode 1: Chercher dans les scripts application/json
                                var scripts = document.querySelectorAll('script[type=""application/json""]');
                                for (var i = 0; i < scripts.length; i++) {
                                    try
                                        {
                                            var data = JSON.parse(scripts[i].textContent);
                                            
                                            function deepSearch(obj, depth)
                                            {
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
                                
                                // Méthode 2: window.__additionalDataLoaded
                                if (window.__additionalDataLoaded) {
                                    for (var key in window) {
                                        if (key.startsWith('__additionalDataLoaded')) {
                                            try {
                                                var result = deepSearch(window[key], 0);
                                                if (result) return JSON.stringify(result);
                                            } catch (e) {}
                                        }
                                    }
                                }
                                
                                // Méthode 3: Analyser tous les scripts pour trouver des patterns JSON
                                var allScripts = document.getElementsByTagName('script');
                                for (var i = 0; i < allScripts.length; i++) {
                                    var text = allScripts[i].textContent;
                                    
                                    // Chercher shortcode_media dans le texte
                                    if (text.includes('shortcode_media') || text.includes('xdt_shortcode_media')) {
                                        // Essayer d'extraire le JSON autour
                                        var patterns = [
                                            /shortcode_media[""']?\s*:\s*({[^}]+?display_url[^}]+?})/,
                                            /xdt_shortcode_media[""']?\s*:\s*({[^}]+?display_url[^}]+?})/,
                                            /""__typename"":""(XDTGraph|Graph)(Image|Video|Sidecar)""[^{]*?({[^{}]+?})/
                                        ];
                                        
                                        for (var p = 0; p < patterns.length; p++) {
                                            var match = text.match(patterns[p]);
                                            if (match) {
                                                try {
                                                    var extracted = match[match.length - 1];
                                                    // Valider que c'est du JSON valide
                                                    JSON.parse(extracted);
                                                    return extracted;
                                                } catch (e) {}
                                            }
                                        }
                                    }
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
                // Retour à la page d'origine
                if (!string.IsNullOrEmpty(originalUrl))
                {
                    try
                    {
                        await NavigateToUrlAsync(originalUrl);
                        await Task.Delay(500, token);
                    }
                    catch { }
                }
            }
        }

        private async Task<JObject> TryEmbedEndpointAsync(string shortcode, CancellationToken token)
        {
            try
            {
                // Instagram a un endpoint embed qui retourne parfois des données
                string embedUrl = $"https://www.instagram.com/p/{shortcode}/embed/captioned/";

                var response = await httpClient.GetAsync(embedUrl, token);

                if (!response.IsSuccessStatusCode)
                    return null;

                string html = await response.Content.ReadAsStringAsync();

                // Chercher les données JSON dans le HTML de l'embed
                var patterns = new[]
                {
                    @"window\.__additionalDataLoaded\([^,]+,\s*({.+?})\);",
                    @"<script type=""application/json"">(.+?)</script>",
                    @"window\._sharedData\s*=\s*({.+?});</script>"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.Singleline);
                    if (match.Success)
                    {
                        try
                        {
                            var jsonStr = match.Groups[1].Value;
                            var data = JObject.Parse(jsonStr);
                            var found = FindPostDataRecursive(data);
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

        private async Task<JObject> TryNetworkInterceptionAsync(string shortcode, string postType, CancellationToken token)
        {
            try
            {
                JObject capturedData = null;
                bool requestCompleted = false;

                void NetworkResponseReceived(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
                {
                    try
                    {
                        var responseJson = JObject.Parse(e.ParameterObjectAsJson);
                        var responseUrl = responseJson["response"]?["url"]?.ToString();

                        if (!string.IsNullOrEmpty(responseUrl) &&
                            (responseUrl.Contains("/graphql") || responseUrl.Contains("shortcode_media")))
                        {
                            // On a trouvé une requête GraphQL
                            requestCompleted = true;
                        }
                    }
                    catch { }
                }

                webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += NetworkResponseReceived;
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

                string postUrl = $"https://www.instagram.com/{postType}/{shortcode}/";
                await NavigateToUrlAsync(postUrl);
                await Task.Delay(3000, token);

                webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived -= NetworkResponseReceived;
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.disable", "{}");

                // Si on a capturé quelque chose, essayer de l'extraire via le script
                if (requestCompleted)
                {
                    return await TryDirectWebViewExtractionAsync(shortcode, postType, token);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

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

        private JObject FindPostDataRecursive(JToken token, int depth = 0)
        {
            if (token == null || token.Type == JTokenType.Null || depth > 15)
                return null;

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

                if (obj["xdt_shortcode_media"] != null)
                    return obj["xdt_shortcode_media"] as JObject;
                if (obj["shortcode_media"] != null)
                    return obj["shortcode_media"] as JObject;

                foreach (var prop in obj.Properties())
                {
                    var result = FindPostDataRecursive(prop.Value, depth + 1);
                    if (result != null)
                        return result;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    var result = FindPostDataRecursive(item, depth + 1);
                    if (result != null)
                        return result;
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
                JArray resources = postData["display_resources"] as JArray;
                string url = (resources != null && resources.Count > 0) ? resources.Last()["src"]?.ToString() : postData["display_url"]?.ToString();
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
                string url = postData["video_url"]?.ToString();
                if (!string.IsNullOrEmpty(url))
                {
                    int width = postData["dimensions"]?["width"]?.ToObject<int>() ?? 0;
                    int height = postData["dimensions"]?["height"]?.ToObject<int>() ?? 0;
                    mediaList.Add(new MediaInfo { Url = url, Type = "video", Width = width, Height = height });
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
                        string url;
                        if (childType == "XDTGraphVideo" || childType == "GraphVideo")
                        {
                            url = node["video_url"]?.ToString();
                        }
                        else
                        {
                            JArray childResources = node["display_resources"] as JArray;
                            url = (childResources != null && childResources.Count > 0) ? childResources.Last()["src"]?.ToString() : node["display_url"]?.ToString();
                        }
                        if (!string.IsNullOrEmpty(url))
                        {
                            int width = node["dimensions"]?["width"]?.ToObject<int>() ?? 0;
                            int height = node["dimensions"]?["height"]?.ToObject<int>() ?? 0;
                            string mType = (childType == "XDTGraphVideo" || childType == "GraphVideo") ? "video" : "image";
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

                    // IMPROVED: More robust retry logic with exponential backoff
                    byte[] contentBytes = null;
                    int maxRetries = 5; // Increased from 3

                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            // Add longer timeout for individual requests
                            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout per request

                                var mediaResponse = await httpClient.GetAsync(media.Url, cts.Token);

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
                                    // Crucial for Instagram CDN
                                    try { req.Headers.Referrer = new Uri(refererUrl); } catch { }

                                    // Light, permissive headers
                                    req.Headers.TryAddWithoutValidation("Accept", "*/*");
                                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
                                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
                                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", media.Type == "video" ? "video" : "image");

                                    if (mediaResponse.IsSuccessStatusCode)
                                    {
                                        contentBytes = await mediaResponse.Content.ReadAsByteArrayAsync();
                                        break;
                                    }
                                    else if (mediaResponse.StatusCode == HttpStatusCode.TooManyRequests)
                                    {
                                        Log($"    Rate limited, waiting...");
                                        await Task.Delay(10000 * (retry + 1), token);
                                        continue;
                                    }
                                    else
                                    {
                                        Log($"    ⚠ HTTP {(int)mediaResponse.StatusCode} (tentative {retry + 1}/{maxRetries})");
                                    }
                                }
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            Log($"    ⚠ Timeout (tentative {retry + 1}/{maxRetries})");
                        }
                        catch (Exception ex)
                        {
                            Log($"    ⚠ Erreur: {ex.Message} (tentative {retry + 1}/{maxRetries})");
                        }

                        if (retry < maxRetries - 1)
                        {
                            // Exponential backoff: 2s, 4s, 8s, 16s
                            int delayMs = (int)Math.Pow(2, retry + 1) * 1000;
                            await Task.Delay(delayMs, token);
                        }
                    }

                    // IMPROVED: More lenient validation
                    if (contentBytes == null)
                    {
                        Log($"    ✗ Fichier {i + 1}: échec du téléchargement après {maxRetries} tentatives");
                        continue;
                    }

                    int minSize = media.Type == "video" ? 10 * 1024 : 5 * 1024; // 10KB for video, 5KB for image
                    if (contentBytes.Length < minSize)
                    {
                        Log($"    ✗ Fichier {i + 1} trop petit ({contentBytes.Length / 1024}KB < {minSize / 1024}KB)");
                        continue;
                    }

                    string ext = media.Type == "video" ? "mp4" : "jpg";
                    string fileName = mediaList.Count == 1
                        ? $"{shortcode}.{ext}"
                        : $"{shortcode}_{i + 1:D2}.{ext}";

                    string filePath = Path.Combine(userFolder, fileName);

                    // Check if file already exists and has similar size
                    if (File.Exists(filePath))
                    {
                        var existingInfo = new FileInfo(filePath);
                        if (Math.Abs(existingInfo.Length - contentBytes.Length) < 1024) // Within 1KB
                        {
                            Log($"    ⊚ {fileName} existe déjà");
                            downloaded++;
                            continue;
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

        private async Task NavigateToUrlAsync(string url)
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                webView.CoreWebView2.NavigationCompleted -= Handler;
                tcs.TrySetResult(e.IsSuccess);
            }
            webView.CoreWebView2.NavigationCompleted += Handler;
            webView.CoreWebView2.Navigate(url);
            await tcs.Task;
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