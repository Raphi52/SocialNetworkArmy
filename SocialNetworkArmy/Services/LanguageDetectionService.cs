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
        private const string LIBRETRANSLATE_URL = "https://libretranslate.com/detect";

        // Cache pour éviter les appels API redondants
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

                // Optimiser la taille du texte (plus court = plus rapide)
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

                // ✅ STEP 3: LibreTranslate API (free, no key required)
                string libretranslateResult = await DetectLanguageLibreTranslate(truncatedText);
                if (libretranslateResult != "Unknown")
                {
                    languageCache[cacheKey] = (libretranslateResult, DateTime.Now);
                    Log($"[LangDetect] ✓ LibreTranslate: {libretranslateResult}");
                    return libretranslateResult;
                }

                Log($"[LangDetect] ⚠️ All methods failed");
                return "Unknown";
            }
            catch (Exception ex)
            {
                Log($"[LangDetect] ⚠️ Error: {ex.Message}");
                return "Unknown";
            }
        }

        private async Task<string> DetectLanguageLibreTranslate(string text)
        {
            try
            {
                var payload = new { q = text };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(LIBRETRANSLATE_URL, content);

                if (!response.IsSuccessStatusCode)
                {
                    return "Unknown";
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonDocument.Parse(jsonResponse);

                // Format: [{"confidence":0.99,"language":"fr"}]
                if (result.RootElement.ValueKind == JsonValueKind.Array && result.RootElement.GetArrayLength() > 0)
                {
                    string langCode = result.RootElement[0].GetProperty("language").GetString();
                    double confidence = result.RootElement[0].GetProperty("confidence").GetDouble();

                    string languageName = MapLanguageCode(langCode);
                    Log($"[LangDetect] LibreTranslate: {languageName} ({confidence:P0} confidence)");
                    return languageName;
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                Log($"[LangDetect] LibreTranslate error: {ex.Message}");
                return "Unknown";
            }
        }

        // ✅ ENHANCED: Vocabulaire enrichi x5 pour Français et Anglais
        private string DetectLanguageLocally(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
                return null;

            string lowerText = text.ToLower();
            int totalWords = lowerText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            if (totalWords < 3)
                return null;

            var languageScores = new Dictionary<string, int>();

            // ✅ ENGLISH - Vocabulaire enrichi x5 (150+ mots-clés)
            var englishKeywords = new[] { 
                // Articles & prépositions
                " the ", " a ", " an ", " and ", " or ", " but ", " if ", " of ", " to ", " in ", " on ", " at ", " by ", " for ", " with ", " from ", " about ", " into ", " through ", " during ", " before ", " after ", " above ", " below ", " between ", " among ", " under ", " over ",
                // Verbes courants
                " is ", " are ", " was ", " were ", " be ", " been ", " being ", " have ", " has ", " had ", " do ", " does ", " did ", " will ", " would ", " should ", " could ", " can ", " may ", " might ", " must ", " shall ", " get ", " go ", " going ", " come ", " came ", " make ", " making ", " take ", " give ", " see ", " know ", " think ", " look ", " want ", " use ", " find ", " tell ", " ask ", " work ", " feel ", " try ", " leave ", " call ",
                // Pronoms
                " i ", " you ", " he ", " she ", " it ", " we ", " they ", " me ", " him ", " her ", " us ", " them ", " my ", " your ", " his ", " her ", " its ", " our ", " their ", " mine ", " yours ", " this ", " that ", " these ", " those ", " who ", " what ", " which ", " where ", " when ", " why ", " how ",
                // Mots fréquents
                " not ", " no ", " yes ", " all ", " any ", " some ", " many ", " much ", " more ", " most ", " other ", " such ", " only ", " own ", " same ", " so ", " than ", " too ", " very ", " just ", " now ", " then ", " here ", " there ", " where ", " way ", " well ", " also ", " back ", " even ", " still ", " just ", " like ", " love ", " need ", " right ", " good ", " great ", " new ", " first ", " last ", " long ", " little ", " own ", " old ", " different ", " small ", " large ", " next ", " early ", " young ", " important ", " few ", " public ", " bad ", " able ", " free ",
                // Expressions
                " i'm ", " you're ", " he's ", " she's ", " it's ", " we're ", " they're ", " i've ", " you've ", " we've ", " they've ", " don't ", " doesn't ", " didn't ", " won't ", " wouldn't ", " can't ", " couldn't ", " shouldn't ", " isn't ", " aren't ", " wasn't ", " weren't ", " haven't ", " hasn't ", " hadn't "
            };
            languageScores["English"] = CountMatches(lowerText, englishKeywords);

            // ✅ FRENCH - Vocabulaire enrichi x5 (150+ mots-clés)
            var frenchKeywords = new[] { 
                // Articles & prépositions
                " le ", " la ", " les ", " un ", " une ", " des ", " de ", " du ", " d'", " au ", " aux ", " à ", " en ", " dans ", " sur ", " sous ", " avec ", " sans ", " pour ", " par ", " chez ", " vers ", " depuis ", " pendant ", " avant ", " après ", " devant ", " derrière ", " entre ", " parmi ",
                // Verbes courants
                " est ", " sont ", " était ", " étaient ", " été ", " être ", " avoir ", " a ", " ai ", " as ", " ont ", " avait ", " avaient ", " eu ", " sera ", " seront ", " serait ", " seraient ", " faire ", " fait ", " fais ", " font ", " faisait ", " aller ", " va ", " vais ", " allait ", " allé ", " venir ", " vient ", " venu ", " pouvoir ", " peut ", " peuvent ", " pouvait ", " pu ", " vouloir ", " veut ", " veux ", " voulait ", " voulu ", " devoir ", " doit ", " doivent ", " devait ", " dû ", " savoir ", " sait ", " savait ", " su ", " voir ", " voit ", " voyait ", " vu ", " dire ", " dit ", " disait ", " prendre ", " prend ", " prenait ", " pris ", " donner ", " donne ", " donnait ", " donné ", " mettre ", " met ", " mettait ", " mis ",
                // Pronoms
                " je ", " tu ", " il ", " elle ", " on ", " nous ", " vous ", " ils ", " elles ", " me ", " te ", " se ", " le ", " la ", " lui ", " leur ", " moi ", " toi ", " mon ", " ma ", " mes ", " ton ", " ta ", " tes ", " son ", " sa ", " ses ", " notre ", " nos ", " votre ", " vos ", " leur ", " leurs ", " ce ", " cet ", " cette ", " ces ", " qui ", " que ", " quoi ", " dont ", " où ", " quand ", " comment ", " pourquoi ", " quel ", " quelle ", " quels ", " quelles ",
                // Mots fréquents
                " pas ", " plus ", " tout ", " tous ", " toute ", " toutes ", " très ", " bien ", " bon ", " bonne ", " mal ", " mauvais ", " petit ", " grand ", " grande ", " autre ", " même ", " tel ", " telle ", " aussi ", " encore ", " déjà ", " jamais ", " toujours ", " souvent ", " parfois ", " maintenant ", " alors ", " donc ", " car ", " mais ", " ou ", " et ", " si ", " comme ", " beaucoup ", " peu ", " assez ", " trop ", " plusieurs ", " chaque ", " quelque ", " certain ", " divers ", " différent ", " nouveau ", " nouvelle ", " vieux ", " vieille ", " jeune ", " premier ", " première ", " dernier ", " dernière ", " seul ", " seule ", " ici ", " là ", " partout ", " nulle ", " quelque ",
                // Expressions typiques
                " c'est ", " c'était ", " n'est ", " n'était ", " j'ai ", " j'avais ", " j'étais ", " t'es ", " qu'il ", " qu'elle ", " qu'on ", " d'un ", " d'une ", " l'on ", " s'il ", " aujourd'hui ", " parce que ", " puisque ", " jusqu'à ", " merci ", " bonjour ", " salut ", " oui ", " non ", " peut-être ", " voilà ", " voici "
            };
            languageScores["French"] = CountMatches(lowerText, frenchKeywords);

            // Bonus pour accents français
            if (lowerText.Contains("é") || lowerText.Contains("è") || lowerText.Contains("ê") ||
                lowerText.Contains("à") || lowerText.Contains("â") || lowerText.Contains("ù") ||
                lowerText.Contains("û") || lowerText.Contains("î") || lowerText.Contains("ô") ||
                lowerText.Contains("ç") || lowerText.Contains("ï") || lowerText.Contains("ë"))
                languageScores["French"] += 5; // Bonus renforcé

            // Spanish detection (vocabulaire original)
            var spanishKeywords = new[] { " el ", " la ", " los ", " las ", " de ", " que ", " es ", " son ", " para ", " con ", " por ", " en ", " un ", " una ", " del ", " al ", " mi ", " tu ", " su ", " este ", " esta ", " como ", " más ", " muy ", " todo ", " todos ", " bien ", " estar ", " hacer ", " hoy ", " día ", " vida ", " gracias ", " español ", " española ", " qué ", " cómo ", " dónde ", " cuándo ", " está ", " están " };
            languageScores["Spanish"] = CountMatches(lowerText, spanishKeywords);
            if (lowerText.Contains("ñ") || lowerText.Contains("á") || lowerText.Contains("é") || lowerText.Contains("í") || lowerText.Contains("ó") || lowerText.Contains("ú") || lowerText.Contains("¿") || lowerText.Contains("¡"))
                languageScores["Spanish"] += 3;

            // Portuguese detection (vocabulaire original)
            var portugueseKeywords = new[] { " o ", " a ", " os ", " as ", " de ", " que ", " é ", " são ", " para ", " com ", " não ", " em ", " um ", " uma ", " do ", " da ", " dos ", " das ", " por ", " no ", " na ", " meu ", " minha ", " seu ", " sua ", " este ", " esta ", " como ", " mais ", " muito ", " tudo ", " bem ", " estar ", " fazer ", " hoje ", " dia ", " vida ", " obrigado ", " obrigada ", " português ", " portuguesa ", " você ", " está ", " estão ", " português", " até ", " também " };
            languageScores["Portuguese"] = CountMatches(lowerText, portugueseKeywords);
            if (lowerText.Contains("ã") || lowerText.Contains("õ") || lowerText.Contains("ç"))
                languageScores["Portuguese"] += 3;

            // German detection (vocabulaire original)
            var germanKeywords = new[] { " der ", " die ", " das ", " und ", " ist ", " sind ", " für ", " mit ", " auf ", " den ", " dem ", " des ", " ein ", " eine ", " ich ", " du ", " er ", " sie ", " wir ", " ihr ", " nicht ", " auch ", " aus ", " bei ", " nach ", " von ", " zu ", " im ", " am ", " vom ", " zum ", " wie ", " was ", " wann ", " gut ", " sehr ", " haben ", " sein ", " werden ", " deutsch ", " deutsche " };
            languageScores["German"] = CountMatches(lowerText, germanKeywords);
            if (lowerText.Contains("ä") || lowerText.Contains("ö") || lowerText.Contains("ü") || lowerText.Contains("ß"))
                languageScores["German"] += 3;

            // ✅ Chinese detection (caractères chinois)
            int chineseCharCount = 0;
            foreach (char c in text)
            {
                // Plages Unicode pour caractères chinois (CJK Unified Ideographs)
                if ((c >= 0x4E00 && c <= 0x9FFF) ||   // CJK Unified Ideographs (commun)
                    (c >= 0x3400 && c <= 0x4DBF) ||   // CJK Unified Ideographs Extension A
                    (c >= 0x20000 && c <= 0x2A6DF) || // CJK Unified Ideographs Extension B
                    (c >= 0xF900 && c <= 0xFAFF))     // CJK Compatibility Ideographs
                {
                    chineseCharCount++;
                }
            }

            // Si au moins 2 caractères chinois détectés, c'est du chinois
            if (chineseCharCount >= 2)
            {
                languageScores["Chinese"] = chineseCharCount * 2; // Score élevé pour privilégier le chinois
            }

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
            switch (code.ToLower())
            {
                // Langues principales
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

                default: return code.ToUpper();
            }
        }
    }
}