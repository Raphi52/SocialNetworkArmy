using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class ContentFilterService
    {
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly HttpClient httpClient;

        // ✅ Modèle multi-attributs (visage + corps entier)
        private const string HUGGINGFACE_API_URL = "https://api-inference.huggingface.co/models/dima806/facial_emotions_image_detection";

        // ✅ NOUVEAU: Modèle corps entier (fallback si visage pas détecté)
        private const string FULL_BODY_API_URL = "https://api-inference.huggingface.co/models/rizvandwiki/gender-classification";

        private static string HUGGINGFACE_TOKEN = null;

        // Cache par créateur (24h)
        private static readonly Dictionary<string, (bool isFemale, DateTime timestamp)> creatorGenderCache
            = new Dictionary<string, (bool, DateTime)>();
        private static readonly TimeSpan CREATOR_CACHE_DURATION = TimeSpan.FromHours(24);

        public ContentFilterService(WebView2 webView, TextBox logTextBox)
        {
            this.webView = webView;
            this.logTextBox = logTextBox;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(30); // ✅ Timeout pour éviter blocages
        }

        private void LoadHuggingFaceToken()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "huggingface_token.txt");

                if (File.Exists(configPath))
                {
                    HUGGINGFACE_TOKEN = File.ReadAllText(configPath).Trim();
                }
                else
                {
                    Log("[CONFIG] ⚠️ HuggingFace token not found. Create Data/huggingface_token.txt");
                    Log("[CONFIG] Get free token at: https://huggingface.co/settings/tokens");
                    HUGGINGFACE_TOKEN = "";
                }
            }
            catch (Exception ex)
            {
                Log($"[CONFIG ERROR] {ex.Message}");
                HUGGINGFACE_TOKEN = "";
            }
        }

        /// <summary>
        /// ✅ NOUVELLE VERSION: Analyse multi-niveaux (visage + corps)
        /// </summary>
        public async Task<bool> IsImageFemaleAsync(string imageUrl)
        {
            try
            {
                // ✅ Niveau 1: Essayer détection visage d'abord (plus précis)
                // ✅ Niveau 1: Essayer détection visage d'abord (plus précis)
                var faceResult = await AnalyzeWithHuggingFaceAsync(imageUrl, useFaceModel: true);

                if (faceResult.HasFaces && faceResult.Confidence > 0.60)  // Lower threshold
                {
                    // ✅ Visage détecté avec bonne confiance
                    if (faceResult.IsFemale && faceResult.Confidence > 0.65)  // Female threshold
                    {
                        Log($"[Filter] ✓ Female FACE ({faceResult.Confidence:P0}) - KEEP");
                        return true;
                    }
                    else if (!faceResult.IsFemale && faceResult.Confidence > 0.65)  // Male threshold (symmetric)
                    {
                        Log($"[Filter] ✗ Male FACE ({faceResult.Confidence:P0}) - SKIP");
                        return false;
                    }
                }

                // ✅ Niveau 2: Fallback sur analyse corps entier
                Log($"[Filter] → Trying full-body analysis...");
                var bodyResult = await AnalyzeWithHuggingFaceAsync(imageUrl, useFaceModel: false);

                if (bodyResult.HasPerson)
                {
                    if (bodyResult.IsFemale && bodyResult.Confidence > 0.70)
                    {
                        Log($"[Filter] ✓ Female BODY ({bodyResult.Confidence:P0}) - KEEP");
                        return true;
                    }
                    else if (!bodyResult.IsFemale && bodyResult.Confidence > 0.50)
                    {
                        Log($"[Filter] ✗ Male BODY ({bodyResult.Confidence:P0}) - SKIP");
                        return false;
                    }
                }

                // ❌ Aucune détection fiable
                Log($"[Filter] ? Uncertain - SKIP by default");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ Analyse unifiée: visage OU corps selon le paramètre
        /// </summary>
        private async Task<AnalysisResult> AnalyzeWithHuggingFaceAsync(string imageUrl, bool useFaceModel)
        {
            try
            {
                // Lazy load token
                if (HUGGINGFACE_TOKEN == null)
                {
                    LoadHuggingFaceToken();
                }

                if (string.IsNullOrEmpty(HUGGINGFACE_TOKEN))
                {
                    return new AnalysisResult { HasFaces = false, HasPerson = false };
                }

                // Sélectionner le modèle
                string apiUrl = useFaceModel ? HUGGINGFACE_API_URL : FULL_BODY_API_URL;
                string modelType = useFaceModel ? "FACE" : "BODY";

                // Télécharger l'image
                var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

                // Appel API
                var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Add("Authorization", $"Bearer {HUGGINGFACE_TOKEN}");
                request.Content = new ByteArrayContent(imageBytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var response = await httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[Filter] ⚠️ {modelType} API error: {response.StatusCode}");
                    return new AnalysisResult { HasFaces = false, HasPerson = false };
                }

                // Parser la réponse
                return ParseGenderResponse(json, modelType);
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Analysis error: {ex.Message}");
                return new AnalysisResult { HasFaces = false, HasPerson = false };
            }
        }

        /// <summary>
        /// ✅ Parser universel pour les réponses de genre
        /// </summary>
        private AnalysisResult ParseGenderResponse(string json, string modelType)
        {
            try
            {
                var data = JsonDocument.Parse(json);
                var result = new AnalysisResult();

                double femaleScore = 0;
                double maleScore = 0;
                bool detectionFound = false;

                // Deux formats possibles selon le modèle
                if (data.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Format: [{"label":"female","score":0.95}, ...]
                    foreach (var pred in data.RootElement.EnumerateArray())
                    {
                        if (pred.TryGetProperty("label", out var labelProp) &&
                            pred.TryGetProperty("score", out var scoreProp))
                        {
                            var label = labelProp.GetString()?.ToLower() ?? "";
                            var score = scoreProp.GetDouble();

                            // ✅ ORDRE IMPORTANT: female AVANT male (sinon "female" match "male")
                            if (label == "female" || label == "woman" || label == "girl")
                            {
                                femaleScore = score;
                                detectionFound = true;
                            }
                            else if (label == "male" || label == "man" || label == "boy")
                            {
                                maleScore = score;
                                detectionFound = true;
                            }
                        }
                    }
                }
                else if (data.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Format: {"female":0.95, "male":0.05}
                    if (data.RootElement.TryGetProperty("female", out var femProp))
                    {
                        femaleScore = femProp.GetDouble();
                        detectionFound = true;
                    }
                    if (data.RootElement.TryGetProperty("male", out var malProp))
                    {
                        maleScore = malProp.GetDouble();
                        detectionFound = true;
                    }
                }

                // ✅ Log des scores
                if (detectionFound)
                {
                    Log($"[Filter] {modelType} RAW: Female={femaleScore:F2}, Male={maleScore:F2}");
                    Log($"[Filter] {modelType} scores: Female={femaleScore:P0}, Male={maleScore:P0}");
                }

                // ✅ Déterminer le résultat
                result.HasFaces = detectionFound;
                result.HasPerson = detectionFound;
                result.IsFemale = femaleScore > maleScore;
                result.Confidence = Math.Max(femaleScore, maleScore);

                return result;
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Parse error: {ex.Message}");
                return new AnalysisResult { HasFaces = false, HasPerson = false };
            }
        }

        /// <summary>
        /// ✅ Détection intelligente du contenu actuel
        /// </summary>
        public async Task<bool> IsCurrentContentFemaleAsync()
        {
            try
            {
                // ✅ Extraction avec filtres stricts
                string scriptResult = await webView.ExecuteScriptAsync(@"
    (function() {
        let creator = '';
        const creatorLink = document.querySelector('article a[href*=""/""]');
        if (creatorLink) {
            const href = creatorLink.getAttribute('href');
            const match = href.match(/^\/([^\/]+)\/?$/);
            if (match) creator = match[1];
        }

        const allImages = Array.from(document.querySelectorAll('img'));
        const debugInfo = {
            totalImages: allImages.length,
            largeImages: 0,
            validRatios: 0
        };

        const imageUrls = [];
        const validImages = allImages.filter(img => {
            const rect = img.getBoundingClientRect();
            const width = img.naturalWidth || img.width;
            const height = img.naturalHeight || img.height;
            const ratio = width / height;
            
            const isVisible = rect.top < window.innerHeight && rect.bottom > 0;
            const isLargeEnough = width > 200 && height > 200;
            if (isLargeEnough) debugInfo.largeImages++;
            
            const isPortraitOrSquare = ratio > 0.5 && ratio < 2.0;
            if (isPortraitOrSquare) debugInfo.validRatios++;
            
            const notIcon = !img.src.includes('emoji') && 
                          (!img.alt || !img.alt.toLowerCase().includes('icon')) &&
                          (!img.alt || !img.alt.toLowerCase().includes('emoji'));
            
            const notButton = !img.closest('button');
            
            return isVisible && isLargeEnough && isPortraitOrSquare && notIcon && notButton;
        });

        validImages.sort((a, b) => {
            const aSize = (b.naturalWidth || b.width) * (b.naturalHeight || b.height);
            const bSize = (a.naturalWidth || a.width) * (a.naturalHeight || a.height);
            return aSize - bSize;
        });

        for (let img of validImages.slice(0, 3)) {
            const url = img.src || img.currentSrc;
            if (url && !imageUrls.includes(url)) {
                imageUrls.push(url);
            }
        }

        if (imageUrls.length === 0) {
            const videos = document.querySelectorAll('video');
            for (let video of videos) {
                const rect = video.getBoundingClientRect();
                if (rect.top < window.innerHeight && rect.bottom > 0 && video.poster) {
                    imageUrls.push(video.poster);
                    break;
                }
            }
        }

        return JSON.stringify({
            creator: creator,
            images: imageUrls,
            debug: debugInfo
        });
    })()
");

                scriptResult = scriptResult?.Trim('"')?.Replace("\\\"", "\"");

                if (string.IsNullOrWhiteSpace(scriptResult))
                {
                    Log($"[Filter] ✗ No data extracted");
                    return false;
                }

                // Parse résultat
                var data = JsonDocument.Parse(scriptResult);
                string creator = data.RootElement.GetProperty("creator").GetString();
                var images = data.RootElement.GetProperty("images");

                var imageUrls = new List<string>();
                foreach (var img in images.EnumerateArray())
                {
                    string url = img.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                        imageUrls.Add(url);
                }

                if (imageUrls.Count == 0)
                {
                    Log($"[Filter] ✗ No valid images found");
                    return false;
                }

                // ✅ Vérifier cache créateur (24h)
                if (!string.IsNullOrWhiteSpace(creator) && creatorGenderCache.TryGetValue(creator, out var cached))
                {
                    if (DateTime.Now - cached.timestamp < CREATOR_CACHE_DURATION)
                    {
                        Log($"[Filter] ✓ CACHE: @{creator} → {(cached.isFemale ? "Female" : "Male")} (no API call)");
                        return cached.isFemale;
                    }
                    else
                    {
                        creatorGenderCache.Remove(creator); // Expiré
                    }
                }

                // ✅ Analyser l'image (multi-niveaux: visage puis corps)
                Log($"[Filter] Analyzing @{creator}...");
                bool finalResult = await IsImageFemaleAsync(imageUrls[0]);

                // ✅ Cacher le résultat
                if (!string.IsNullOrWhiteSpace(creator))
                {
                    creatorGenderCache[creator] = (finalResult, DateTime.Now);
                    Log($"[Filter] ✓ Cached @{creator} → {(finalResult ? "Female" : "Male")}");
                }

                return finalResult;
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ Vider le cache (utile pour debug)
        /// </summary>
        public void ClearCache()
        {
            creatorGenderCache.Clear();
            Log($"[Filter] Cache cleared");
        }

        /// <summary>
        /// ✅ Statistiques du cache
        /// </summary>
        public string GetCacheStats()
        {
            int total = creatorGenderCache.Count;
            int females = 0;
            int males = 0;

            foreach (var entry in creatorGenderCache.Values)
            {
                if (entry.isFemale) females++;
                else males++;
            }

            return $"Cache: {total} creators ({females} female, {males} male)";
        }

        private void Log(string message)
        {
            if (logTextBox == null || logTextBox.IsDisposed) return;

            if (logTextBox.InvokeRequired)
            {
                logTextBox.BeginInvoke(new Action(() =>
                {
                    logTextBox.AppendText(message + Environment.NewLine);
                    logTextBox.SelectionStart = logTextBox.TextLength;
                    logTextBox.ScrollToCaret();
                }));
            }
            else
            {
                logTextBox.AppendText(message + Environment.NewLine);
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.ScrollToCaret();
            }
        }

        private class AnalysisResult
        {
            public bool HasFaces { get; set; }
            public bool HasPerson { get; set; } // ✅ NOUVEAU: détection corps
            public int FaceCount { get; set; }
            public bool IsFemale { get; set; }
            public double Confidence { get; set; }
        }
    }
}
