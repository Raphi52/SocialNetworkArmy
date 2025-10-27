using System;
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

        // ✅ Configuration HuggingFace (GRATUIT)
        private const string HUGGINGFACE_API_URL = "https://api-inference.huggingface.co/models/rizvandwiki/gender-classification";
        private static string HUGGINGFACE_TOKEN = null;

        public ContentFilterService(WebView2 webView, TextBox logTextBox)
        {
            this.webView = webView;
            this.logTextBox = logTextBox;
            this.httpClient = new HttpClient();
            // Token will be loaded lazily on first use (faster startup)
        }

        private void LoadHuggingFaceToken()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "huggingface_token.txt");

                if (File.Exists(configPath))
                {
                    HUGGINGFACE_TOKEN = File.ReadAllText(configPath).Trim();
                    Log("[CONFIG] HuggingFace token loaded successfully");
                }
                else
                {
                    Log("[CONFIG] ⚠️ HuggingFace token not found. Create Data/huggingface_token.txt with your token.");
                    Log("[CONFIG] Get free token at: https://huggingface.co/settings/tokens");
                    HUGGINGFACE_TOKEN = ""; // Empty string to avoid null exceptions
                }
            }
            catch (Exception ex)
            {
                Log($"[CONFIG ERROR] Failed to load token: {ex.Message}");
                HUGGINGFACE_TOKEN = "";
            }
        }

        /// <summary>
        /// Analyse une image depuis une URL et détermine si c'est une fille
        /// </summary>
        public async Task<bool> IsImageFemaleAsync(string imageUrl)
        {
            try
            {
                var result = await AnalyzeWithHuggingFaceAsync(imageUrl);

                if (result.HasFaces)
                {
                    if (result.IsFemale)
                    {
                        Log($"[Filter] ✓ Female");
                        return true;
                    }
                    else
                    {
                        Log($"[Filter] ✗ Male");
                        return false;
                    }
                }
                else
                {
                    Log($"[Filter] ✗ No face");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Analyse avec HuggingFace (GRATUIT, illimité)
        /// </summary>
        private async Task<AnalysisResult> AnalyzeWithHuggingFaceAsync(string imageUrl)
        {
            try
            {
                // Lazy load token on first use
                if (HUGGINGFACE_TOKEN == null)
                {
                    LoadHuggingFaceToken();
                }

                // Vérifier que le token est chargé
                if (string.IsNullOrEmpty(HUGGINGFACE_TOKEN))
                {
                    Log($"[Filter] ⚠️ Token missing");
                    return new AnalysisResult { HasFaces = false };
                }

                // 1) Télécharger l'image
                var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

                // 2) Envoyer à HuggingFace
                var request = new HttpRequestMessage(HttpMethod.Post, HUGGINGFACE_API_URL);
                request.Headers.Add("Authorization", $"Bearer {HUGGINGFACE_TOKEN}");
                request.Content = new ByteArrayContent(imageBytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var response = await httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[Filter] ⚠️ API error: {response.StatusCode}");
                    return new AnalysisResult { HasFaces = false };
                }

                // 3) Parser la réponse
                var data = JsonDocument.Parse(json);
                var predictions = data.RootElement.EnumerateArray();

                var result = new AnalysisResult { HasFaces = false, FaceCount = 0, IsFemale = false };

                foreach (var pred in predictions)
                {
                    var label = pred.GetProperty("label").GetString();
                    var score = pred.GetProperty("score").GetDouble();

                    // Check for female
                    if (label.Contains("female", StringComparison.OrdinalIgnoreCase) ||
                        label.Contains("woman", StringComparison.OrdinalIgnoreCase))
                    {
                        result.HasFaces = true;
                        result.FaceCount = 1;
                        result.IsFemale = score > 0.6; // Threshold
                        break;
                    }

                    // Check for male
                    if (label.Contains("male", StringComparison.OrdinalIgnoreCase) ||
                        label.Contains("man", StringComparison.OrdinalIgnoreCase))
                    {
                        result.HasFaces = true;
                        result.FaceCount = 1;
                        result.IsFemale = false;
                        break;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Error: {ex.Message}");
                return new AnalysisResult { HasFaces = false };
            }
        }

        /// <summary>
        /// Alternative: Analyse locale avec TensorFlow (sans API externe)
        /// Nécessite: PM> Install-Package TensorFlow.NET
        /// </summary>
        public async Task<bool> IsImageFemaleLocalAsync(string imageUrl)
        {
            // ⚠️ Nécessite un modèle pré-entraîné (ex: gender_net.caffemodel)
            // Plus complexe mais gratuit et sans limite

            Log($"[Filter] Local analysis not implemented yet");
            return true;
        }

        /// <summary>
        /// Détecte le genre depuis le screenshot actuel dans Instagram
        /// </summary>
        public async Task<bool> IsCurrentContentFemaleAsync()
        {
            try
            {
                // 1) Récupérer l'URL de l'image actuellement affichée via JavaScript
                string imageUrl = await webView.ExecuteScriptAsync(@"
                    (function() {
                        // Strategy 1: Video poster
                        const videos = document.querySelectorAll('video');
                        for (let video of videos) {
                            const rect = video.getBoundingClientRect();
                            if (rect.top < window.innerHeight && rect.bottom > 0 && video.poster) {
                                return video.poster;
                            }
                        }

                        // Strategy 2: Visible images
                        const allImages = Array.from(document.querySelectorAll('img'));
                        const visibleImages = allImages.filter(img => {
                            const rect = img.getBoundingClientRect();
                            return rect.top < window.innerHeight && rect.bottom > 0;
                        });

                        if (visibleImages.length > 0) {
                            visibleImages.sort((a, b) => {
                                return (b.width * b.height) - (a.width * a.height);
                            });

                            const largest = visibleImages[0];
                            if (largest.width > 50 && largest.height > 50) {
                                return largest.src || largest.currentSrc;
                            }
                        }

                        return null;
                    })()
                ");

                imageUrl = imageUrl?.Trim('"');

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    Log($"[Filter] ✗ No image");
                    return false;
                }

                // 2) Analyser l'image
                return await IsImageFemaleAsync(imageUrl);
            }
            catch (Exception ex)
            {
                Log($"[Filter] ⚠️ Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ALTERNATIVE SIMPLE: Filtrage par mots-clés dans la bio/caption
        /// Moins précis mais gratuit et immédiat
        /// </summary>
        public async Task<bool> IsProfileFemaleByKeywordsAsync()
        {
            try
            {
                string bioText = await webView.ExecuteScriptAsync(@"
                    (function() {
                        // Récupérer la bio du profil
                        let bio = document.querySelector('header section div span')?.innerText || '';
                        
                        // Récupérer la caption du post
                        let caption = document.querySelector('article span[dir]')?.innerText || '';
                        
                        return (bio + ' ' + caption).toLowerCase();
                    })()
                ");

                bioText = bioText?.Trim('"').ToLower() ?? "";

                // Mots-clés féminins
                string[] femaleKeywords = { "girl", "woman", "she", "her", "female", "model", "beauty",
                                           "fille", "femme", "elle", "belle", "beauté", "💋", "👗", "💅" };

                // Mots-clés masculins (à exclure)
                string[] maleKeywords = { "guy", "man", "he", "him", "male", "bro", "dude",
                                         "homme", "mec", "gars", "il", "lui" };

                int femaleScore = 0;
                int maleScore = 0;

                foreach (var kw in femaleKeywords)
                {
                    if (bioText.Contains(kw)) femaleScore++;
                }

                foreach (var kw in maleKeywords)
                {
                    if (bioText.Contains(kw)) maleScore++;
                }

                if (maleScore > 0)
                {
                    Log($"[Filter] ✗ Male keywords detected - SKIPPING");
                    return false;
                }

                if (femaleScore > 0)
                {
                    Log($"[Filter] ✓ Female keywords detected - KEEPING");
                    return true;
                }

                // Si aucun mot-clé, on garde (neutre)
                Log($"[Filter] ℹ️ No gender keywords - KEEPING (neutral)");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[Filter] Keyword filter error: {ex.Message}");
                return true;
            }
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
            public int FaceCount { get; set; }
            public bool IsFemale { get; set; }
        }
    }
}