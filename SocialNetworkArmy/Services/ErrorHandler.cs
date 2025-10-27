using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Système avancé de gestion d'erreurs avec retry logic et exponential backoff.
    /// Détecte les erreurs critiques Instagram et propose des actions de récupération.
    /// </summary>
    public class ErrorHandler
    {
        private readonly TextBox logTextBox;
        private int consecutiveErrors = 0;
        private DateTime? lastErrorTime = null;

        public ErrorHandler(TextBox logTextBox)
        {
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
        }

        /// <summary>
        /// Types d'erreurs détectables avec actions appropriées.
        /// </summary>
        public enum ErrorType
        {
            NetworkTimeout,          // Timeout réseau (retry avec backoff)
            InstagramRateLimit,      // Limite Instagram atteinte (pause longue)
            ElementNotFound,         // Élément DOM introuvable (retry court)
            AuthenticationFailed,    // Session expirée (alerte utilisateur)
            ShadowBan,              // Shadowban détecté (arrêt total)
            WebViewCrash,           // WebView crashé (restart)
            UnknownError            // Erreur inconnue (log + continue)
        }

        /// <summary>
        /// Détecte le type d'erreur à partir du message.
        /// </summary>
        public ErrorType DetectErrorType(Exception ex)
        {
            var message = ex.Message.ToLower();

            // Timeout réseau
            if (message.Contains("timeout") || message.Contains("timed out"))
                return ErrorType.NetworkTimeout;

            // Rate limit Instagram
            if (message.Contains("rate limit") || message.Contains("too many requests") ||
                message.Contains("429") || message.Contains("wait a few minutes"))
                return ErrorType.InstagramRateLimit;

            // Session expirée
            if (message.Contains("login") || message.Contains("session") ||
                message.Contains("unauthorized") || message.Contains("401"))
                return ErrorType.AuthenticationFailed;

            // Élément DOM
            if (message.Contains("element") || message.Contains("selector") ||
                message.Contains("not found") || message.Contains("null"))
                return ErrorType.ElementNotFound;

            // WebView crash
            if (message.Contains("webview") || message.Contains("disposed") ||
                message.Contains("corewebview2"))
                return ErrorType.WebViewCrash;

            // Shadowban (détection basique)
            if (message.Contains("shadowban") || message.Contains("restricted") ||
                message.Contains("blocked"))
                return ErrorType.ShadowBan;

            return ErrorType.UnknownError;
        }

        /// <summary>
        /// Exécute une action avec retry automatique et exponential backoff.
        /// </summary>
        public async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> action,
            int maxRetries = 3,
            int baseDelayMs = 2000,
            string operationName = "Operation")
        {
            int attempt = 0;

            while (attempt < maxRetries)
            {
                try
                {
                    attempt++;
                    logTextBox.AppendText($"[RETRY] {operationName} - Attempt {attempt}/{maxRetries}\r\n");

                    var result = await action();

                    // Succès - réinitialiser le compteur d'erreurs
                    if (attempt > 1)
                    {
                        logTextBox.AppendText($"[RETRY] ✓ {operationName} succeeded after {attempt} attempts\r\n");
                    }

                    consecutiveErrors = 0;
                    return result;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    lastErrorTime = DateTime.Now;

                    var errorType = DetectErrorType(ex);
                    logTextBox.AppendText($"[ERROR] {operationName} failed (Attempt {attempt}/{maxRetries})\r\n");
                    logTextBox.AppendText($"[ERROR] Type: {errorType}\r\n");
                    logTextBox.AppendText($"[ERROR] Message: {ex.Message}\r\n");

                    // Dernière tentative - lever l'exception
                    if (attempt >= maxRetries)
                    {
                        logTextBox.AppendText($"[ERROR] ✗ {operationName} failed after {maxRetries} attempts\r\n");
                        HandleCriticalError(errorType, ex);
                        throw;
                    }

                    // Calculer le délai avec exponential backoff
                    int delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);

                    // Ajuster le délai selon le type d'erreur
                    delay = errorType switch
                    {
                        ErrorType.InstagramRateLimit => delay * 10,  // Rate limit = attendre beaucoup plus longtemps
                        ErrorType.NetworkTimeout => delay * 2,        // Timeout = attendre un peu plus
                        ErrorType.ElementNotFound => delay / 2,       // Element not found = retry plus vite
                        _ => delay
                    };

                    logTextBox.AppendText($"[RETRY] Waiting {delay}ms before retry {attempt + 1}...\r\n");
                    await Task.Delay(delay);
                }
            }

            throw new InvalidOperationException($"{operationName} failed after {maxRetries} retries");
        }

        /// <summary>
        /// Gère les erreurs critiques qui nécessitent une intervention.
        /// </summary>
        private void HandleCriticalError(ErrorType errorType, Exception ex)
        {
            switch (errorType)
            {
                case ErrorType.ShadowBan:
                    logTextBox.AppendText($"\r\n");
                    logTextBox.AppendText($"╔═══════════════════════════════════════╗\r\n");
                    logTextBox.AppendText($"║  ⚠️  SHADOWBAN DETECTED  ⚠️          ║\r\n");
                    logTextBox.AppendText($"╠═══════════════════════════════════════╣\r\n");
                    logTextBox.AppendText($"║ This account may be shadowbanned.     ║\r\n");
                    logTextBox.AppendText($"║ RECOMMENDED ACTION:                   ║\r\n");
                    logTextBox.AppendText($"║ - Stop all automation for 24-48h      ║\r\n");
                    logTextBox.AppendText($"║ - Use account manually                ║\r\n");
                    logTextBox.AppendText($"║ - Check if posts are visible          ║\r\n");
                    logTextBox.AppendText($"╚═══════════════════════════════════════╝\r\n");
                    MessageBox.Show(
                        "⚠️ SHADOWBAN DETECTED\n\n" +
                        "This account may be restricted.\n" +
                        "Stop automation for 24-48 hours and use manually.",
                        "Critical Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;

                case ErrorType.AuthenticationFailed:
                    logTextBox.AppendText($"\r\n[AUTH] ⚠️ Session expired - Please login again\r\n");
                    MessageBox.Show(
                        "Your Instagram session has expired.\n\n" +
                        "Please login again in the browser.",
                        "Authentication Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    break;

                case ErrorType.InstagramRateLimit:
                    logTextBox.AppendText($"\r\n[RATE_LIMIT] ⚠️ Instagram rate limit reached\r\n");
                    logTextBox.AppendText($"[RATE_LIMIT] Waiting 15 minutes before resuming...\r\n");
                    break;

                case ErrorType.WebViewCrash:
                    logTextBox.AppendText($"\r\n[WEBVIEW] ⚠️ WebView crashed - Restart required\r\n");
                    MessageBox.Show(
                        "The browser component crashed.\n\n" +
                        "Please restart the application.",
                        "Browser Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    break;
            }
        }

        /// <summary>
        /// Retourne true si trop d'erreurs consécutives (mode safe recommandé).
        /// </summary>
        public bool ShouldEnterSafeMode()
        {
            // Plus de 5 erreurs en 5 minutes = problème sérieux
            if (consecutiveErrors >= 5 && lastErrorTime.HasValue)
            {
                var timeSinceLastError = DateTime.Now - lastErrorTime.Value;
                if (timeSinceLastError.TotalMinutes < 5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Réinitialise le compteur d'erreurs (après succès ou intervention manuelle).
        /// </summary>
        public void ResetErrorCount()
        {
            consecutiveErrors = 0;
            lastErrorTime = null;
            logTextBox.AppendText("[ERROR_HANDLER] Error counter reset\r\n");
        }

        /// <summary>
        /// Obtient le nombre d'erreurs consécutives actuelles.
        /// </summary>
        public int GetConsecutiveErrors() => consecutiveErrors;
    }
}
