using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class LanguageDetectionService
    {
        private readonly HttpClient httpClient;
        private readonly TextBox logTextBox;
        private static string HUGGINGFACE_TOKEN = null;

        // Free HuggingFace model for language detection
        private const string HUGGINGFACE_API_URL = "https://api-inference.huggingface.co/models/papluca/xlm-roberta-base-language-detection";

        // ✅ AMÉLIORATION: Cache pour éviter les appels API redondants
        private static readonly Dictionary<string, (string language, DateTime timestamp)> languageCache = new Dictionary<string, (string, DateTime)>();
        private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(30);

        public LanguageDetectionService(TextBox logTextBox)
        {
            this.logTextBox = logTextBox;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        private void Log(string message)
        {
            try
            {
                if (logTextBox != null && logTextBox.InvokeRequired)
                    logTextBox.Invoke(new Action(() => logTextBox.AppendText(message + "\r\n")));
                else
                    logTextBox?.AppendText(message + "\r\n");
            }
            catch { }
        }

        private void LoadHuggingFaceToken()
        {
            try
            {
                string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "huggingface_token.txt");

                if (System.IO.File.Exists(configPath))
                {
                    HUGGINGFACE_TOKEN = System.IO.File.ReadAllText(configPath).Trim();
                }
                else
                {
                    HUGGINGFACE_TOKEN = "";
                }
            }
            catch
            {
                HUGGINGFACE_TOKEN = "";
            }
        }

        public async Task<string> DetectLanguageAsync(string text)
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
                    Log($"[LangDetect] ⚠️ Token missing");
                    return "Unknown";
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return "Unknown";
                }

                // ✅ AMÉLIORATION: Optimiser la taille du texte (plus court = plus rapide)
                string truncatedText = text.Length > 200 ? text.Substring(0, 200) : text;

                // ✅ AMÉLIORATION: Check cache first
                string cacheKey = truncatedText.GetHashCode().ToString();
                if (languageCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.Now - cached.timestamp < CACHE_DURATION)
                    {
                        Log($"[LangDetect] Cache hit: {cached.language}");
                        return cached.language;
                    }
                    else
                    {
                        languageCache.Remove(cacheKey); // Expired
                    }
                }

                // Create request payload
                var payload = new
                {
                    inputs = truncatedText
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, HUGGINGFACE_API_URL);
                request.Headers.Add("Authorization", $"Bearer {HUGGINGFACE_TOKEN}");
                request.Content = content;

                var response = await httpClient.SendAsync(request);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log($"[LangDetect] ⚠️ API error: {response.StatusCode}");
                    return "Unknown";
                }

                // Parse response
                // Expected format: [[{"label":"en","score":0.99},...]]
                var result = JsonDocument.Parse(jsonResponse);

                if (result.RootElement.ValueKind == JsonValueKind.Array && result.RootElement.GetArrayLength() > 0)
                {
                    var firstArray = result.RootElement[0];
                    if (firstArray.ValueKind == JsonValueKind.Array && firstArray.GetArrayLength() > 0)
                    {
                        var topPrediction = firstArray[0];
                        string label = topPrediction.GetProperty("label").GetString();
                        double score = topPrediction.GetProperty("score").GetDouble();

                        // Map language codes to full names
                        string languageName = MapLanguageCode(label);

                        // ✅ AMÉLIORATION: Store in cache
                        languageCache[cacheKey] = (languageName, DateTime.Now);

                        Log($"[LangDetect] Detected: {languageName} ({score:P0})");
                        return languageName;
                    }
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                Log($"[LangDetect] ⚠️ Error: {ex.Message}");
                return "Unknown";
            }
        }

        private string MapLanguageCode(string code)
        {
            // ✅ AMÉLIORATION: Plus de langues supportées
            switch (code.ToLower())
            {
                // Langues principales (ConfigForm)
                case "en": return "English";
                case "fr": return "French";
                case "es": return "Spanish";
                case "pt": return "Portuguese";
                case "de": return "German";

                // Langues additionnelles
                case "it": return "Italian";
                case "ru": return "Russian";
                case "ar": return "Arabic";
                case "zh": case "zh-cn": case "zh-tw": return "Chinese";
                case "ja": return "Japanese";
                case "ko": return "Korean";
                case "nl": return "Dutch";
                case "pl": return "Polish";
                case "tr": return "Turkish";
                case "hi": return "Hindi";
                case "sv": return "Swedish";
                case "no": case "nb": return "Norwegian";
                case "da": return "Danish";
                case "fi": return "Finnish";
                case "cs": return "Czech";
                case "ro": return "Romanian";
                case "uk": return "Ukrainian";
                case "vi": return "Vietnamese";
                case "th": return "Thai";
                case "id": return "Indonesian";
                case "ms": return "Malay";
                case "he": return "Hebrew";
                case "el": return "Greek";
                case "hu": return "Hungarian";
                case "bg": return "Bulgarian";
                case "hr": return "Croatian";
                case "sk": return "Slovak";
                case "sl": return "Slovenian";
                case "sr": return "Serbian";
                case "ca": return "Catalan";
                case "gl": return "Galician";
                case "eu": return "Basque";

                default: return code.ToUpper(); // Return code if unknown
            }
        }
    }
}
