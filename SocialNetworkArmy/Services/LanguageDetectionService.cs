using System;
using System.Collections.Generic;
using System.Linq;
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
                if (string.IsNullOrWhiteSpace(text))
                {
                    return "Unknown";
                }

                // ✅ STEP 1: Détection locale ultra-rapide (0ms) pour langues courantes
                string localDetection = DetectLanguageLocally(text);
                if (localDetection != null)
                {
                    Log($"[LangDetect] ✓ LOCAL pattern: {localDetection} (0ms, no API)");
                    return localDetection;
                }

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

                // ✅ AMÉLIORATION: Optimiser la taille du texte (plus court = plus rapide)
                string truncatedText = text.Length > 200 ? text.Substring(0, 200) : text;

                // ✅ STEP 2: Check cache (30min duration)
                string cacheKey = truncatedText.GetHashCode().ToString();
                if (languageCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.Now - cached.timestamp < CACHE_DURATION)
                    {
                        Log($"[LangDetect] ✓ CACHE hit: {cached.language} (no API)");
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

                        // ✅ Store in cache for 30min
                        languageCache[cacheKey] = (languageName, DateTime.Now);

                        Log($"[LangDetect] ✓ API call: {languageName} ({score:P0} confidence)");
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

        // ✅ IMPROVED: Enhanced local detection with more keywords and scoring
        private string DetectLanguageLocally(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
                return null;

            string lowerText = text.ToLower();
            int totalWords = lowerText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            if (totalWords < 3)
                return null;

            // ✅ ENHANCED: More keywords + character-based detection + scoring system
            var languageScores = new Dictionary<string, int>();

            // English detection (common words + no accents)
            var englishKeywords = new[] { " the ", " and ", " is ", " are ", " you ", " for ", " with ", " this ", " that ", " have ", " was ", " were ", " from ", " your ", " can ", " will ", " not ", " but ", " all ", " out ", " get ", " like ", " love ", " just ", " more ", " what ", " when ", " good ", " new ", " day ", " time ", " life " };
            languageScores["English"] = CountMatches(lowerText, englishKeywords);

            // French detection (avec accents!)
            var frenchKeywords = new[] { " le ", " la ", " les ", " de ", " je ", " tu ", " il ", " elle ", " nous ", " vous ", " est ", " sont ", " pour ", " avec ", " dans ", " sur ", " par ", " un ", " une ", " des ", " mon ", " ma ", " mes ", " ton ", " ta ", " son ", " sa ", " ce ", " cette ", " qui ", " que ", " pas ", " plus ", " tout ", " très ", " bien ", " faire ", " été ", " merci ", " j'ai ", " c'est ", " n'est ", " aujourd'hui ", "être", "avoir", "français", "française" };
            languageScores["French"] = CountMatches(lowerText, frenchKeywords);
            if (lowerText.Contains("é") || lowerText.Contains("à") || lowerText.Contains("è") || lowerText.Contains("ç") || lowerText.Contains("ê"))
                languageScores["French"] += 3; // Bonus pour accents français

            // Spanish detection (con acentos!)
            var spanishKeywords = new[] { " el ", " la ", " los ", " las ", " de ", " que ", " es ", " son ", " para ", " con ", " por ", " en ", " un ", " una ", " del ", " al ", " mi ", " tu ", " su ", " este ", " esta ", " como ", " más ", " muy ", " todo ", " todos ", " bien ", " estar ", " hacer ", " hoy ", " día ", " vida ", " gracias ", " español ", " española ", " qué ", " cómo ", " dónde ", " cuándo ", " está ", " están " };
            languageScores["Spanish"] = CountMatches(lowerText, spanishKeywords);
            if (lowerText.Contains("ñ") || lowerText.Contains("á") || lowerText.Contains("é") || lowerText.Contains("í") || lowerText.Contains("ó") || lowerText.Contains("ú") || lowerText.Contains("¿") || lowerText.Contains("¡"))
                languageScores["Spanish"] += 3; // Bonus pour caractères espagnols

            // Portuguese detection (com acentos!)
            var portugueseKeywords = new[] { " o ", " a ", " os ", " as ", " de ", " que ", " é ", " são ", " para ", " com ", " não ", " em ", " um ", " uma ", " do ", " da ", " dos ", " das ", " por ", " no ", " na ", " meu ", " minha ", " seu ", " sua ", " este ", " esta ", " como ", " mais ", " muito ", " tudo ", " bem ", " estar ", " fazer ", " hoje ", " dia ", " vida ", " obrigado ", " obrigada ", " português ", " portuguesa ", " você ", " está ", " estão ", " português", " até ", " também " };
            languageScores["Portuguese"] = CountMatches(lowerText, portugueseKeywords);
            if (lowerText.Contains("ã") || lowerText.Contains("õ") || lowerText.Contains("ç"))
                languageScores["Portuguese"] += 3; // Bonus pour accents portugais

            // German detection (mit Umlauten!)
            var germanKeywords = new[] { " der ", " die ", " das ", " und ", " ist ", " sind ", " für ", " mit ", " auf ", " den ", " dem ", " des ", " ein ", " eine ", " ich ", " du ", " er ", " sie ", " wir ", " ihr ", " nicht ", " auch ", " aus ", " bei ", " nach ", " von ", " zu ", " im ", " am ", " vom ", " zum ", " wie ", " was ", " wann ", " gut ", " sehr ", " haben ", " sein ", " werden ", " deutsch ", " deutsche " };
            languageScores["German"] = CountMatches(lowerText, germanKeywords);
            if (lowerText.Contains("ä") || lowerText.Contains("ö") || lowerText.Contains("ü") || lowerText.Contains("ß"))
                languageScores["German"] += 3; // Bonus pour umlauts

            // Find highest score
            var bestMatch = languageScores.OrderByDescending(kvp => kvp.Value).FirstOrDefault();

            // Need at least 3 matches to be confident
            if (bestMatch.Value >= 3)
            {
                return bestMatch.Key;
            }

            return null; // Fallback to API
        }

        private int CountMatches(string text, string[] keywords)
        {
            int count = 0;
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword))
                    count++;
            }
            return count;
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
