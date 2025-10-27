using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Détecte les situations à risque et active automatiquement le mode sécurisé.
    /// Surveille: taux d'erreurs, patterns suspects, réponses Instagram anormales.
    /// </summary>
    public class SafeModeDetector
    {
        private readonly TextBox logTextBox;
        private readonly Queue<DateTime> recentActions = new Queue<DateTime>();
        private readonly Queue<ErrorEvent> recentErrors = new Queue<ErrorEvent>();
        private const int MaxActionsPerMinute = 30;
        private const int MaxActionsPerHour = 300;
        private bool safeModeActive = false;

        private class ErrorEvent
        {
            public DateTime Timestamp { get; set; }
            public string ErrorType { get; set; }
        }

        public SafeModeDetector(TextBox logTextBox)
        {
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
        }

        /// <summary>
        /// Enregistre une action effectuée (like, comment, follow, etc.).
        /// </summary>
        public void RecordAction()
        {
            recentActions.Enqueue(DateTime.Now);

            // Garder seulement les actions de la dernière heure
            while (recentActions.Any() && (DateTime.Now - recentActions.Peek()).TotalHours > 1)
            {
                recentActions.Dequeue();
            }
        }

        /// <summary>
        /// Enregistre une erreur survenue.
        /// </summary>
        public void RecordError(string errorType)
        {
            recentErrors.Enqueue(new ErrorEvent
            {
                Timestamp = DateTime.Now,
                ErrorType = errorType
            });

            // Garder seulement les erreurs des 10 dernières minutes
            while (recentErrors.Any() && (DateTime.Now - recentErrors.Peek().Timestamp).TotalMinutes > 10)
            {
                recentErrors.Dequeue();
            }
        }

        /// <summary>
        /// Vérifie si le mode sécurisé doit être activé.
        /// </summary>
        public bool ShouldActivateSafeMode(out string reason)
        {
            reason = null;

            // 1. Vérifier le taux d'actions par minute
            var actionsLastMinute = recentActions.Count(a => (DateTime.Now - a).TotalMinutes <= 1);
            if (actionsLastMinute > MaxActionsPerMinute)
            {
                reason = $"Too many actions per minute ({actionsLastMinute}/{MaxActionsPerMinute})";
                return true;
            }

            // 2. Vérifier le taux d'actions par heure
            var actionsLastHour = recentActions.Count;
            if (actionsLastHour > MaxActionsPerHour)
            {
                reason = $"Too many actions per hour ({actionsLastHour}/{MaxActionsPerHour})";
                return true;
            }

            // 3. Vérifier le taux d'erreurs
            var errorsLast5Min = recentErrors.Count(e => (DateTime.Now - e.Timestamp).TotalMinutes <= 5);
            if (errorsLast5Min >= 10)
            {
                reason = $"Too many errors in 5 minutes ({errorsLast5Min})";
                return true;
            }

            // 4. Vérifier les erreurs consécutives du même type
            var last5Errors = recentErrors
                .Where(e => (DateTime.Now - e.Timestamp).TotalMinutes <= 5)
                .Select(e => e.ErrorType)
                .ToList();

            if (last5Errors.Count >= 5 && last5Errors.Distinct().Count() == 1)
            {
                reason = $"Repeated error pattern detected: {last5Errors.First()}";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Active le mode sécurisé avec notification.
        /// </summary>
        public void ActivateSafeMode(string reason)
        {
            if (safeModeActive)
                return;

            safeModeActive = true;

            logTextBox.AppendText("\r\n");
            logTextBox.AppendText("╔══════════════════════════════════════════════════╗\r\n");
            logTextBox.AppendText("║                                                  ║\r\n");
            logTextBox.AppendText("║          ⚠️  SAFE MODE ACTIVATED  ⚠️            ║\r\n");
            logTextBox.AppendText("║                                                  ║\r\n");
            logTextBox.AppendText("╠══════════════════════════════════════════════════╣\r\n");
            logTextBox.AppendText($"║ Reason: {reason,-42} ║\r\n");
            logTextBox.AppendText("╠══════════════════════════════════════════════════╣\r\n");
            logTextBox.AppendText("║ ACTIONS TAKEN:                                   ║\r\n");
            logTextBox.AppendText("║ • All automation paused                          ║\r\n");
            logTextBox.AppendText("║ • Waiting 10 minutes before resuming             ║\r\n");
            logTextBox.AppendText("║ • Reduced action rate when resumed               ║\r\n");
            logTextBox.AppendText("╠══════════════════════════════════════════════════╣\r\n");
            logTextBox.AppendText("║ RECOMMENDATIONS:                                 ║\r\n");
            logTextBox.AppendText("║ • Check your actions/hour limit                  ║\r\n");
            logTextBox.AppendText("║ • Verify proxy is working correctly              ║\r\n");
            logTextBox.AppendText("║ • Consider using manual mode for today           ║\r\n");
            logTextBox.AppendText("╚══════════════════════════════════════════════════╝\r\n");
            logTextBox.AppendText("\r\n");

            MessageBox.Show(
                $"⚠️ SAFE MODE ACTIVATED\n\n" +
                $"Reason: {reason}\n\n" +
                $"All automation has been paused for safety.\n" +
                $"The system will resume in 10 minutes with reduced rate.",
                "Safe Mode",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Désactive le mode sécurisé après résolution du problème.
        /// </summary>
        public void DeactivateSafeMode()
        {
            if (!safeModeActive)
                return;

            safeModeActive = false;

            // Nettoyer les historiques
            recentActions.Clear();
            recentErrors.Clear();

            logTextBox.AppendText("\r\n");
            logTextBox.AppendText("╔══════════════════════════════════════════════════╗\r\n");
            logTextBox.AppendText("║         ✓ SAFE MODE DEACTIVATED ✓               ║\r\n");
            logTextBox.AppendText("╠══════════════════════════════════════════════════╣\r\n");
            logTextBox.AppendText("║ Normal operation resumed                         ║\r\n");
            logTextBox.AppendText("║ Error counters reset                             ║\r\n");
            logTextBox.AppendText("╚══════════════════════════════════════════════════╝\r\n");
            logTextBox.AppendText("\r\n");
        }

        /// <summary>
        /// Retourne true si le mode sécurisé est actuellement actif.
        /// </summary>
        public bool IsSafeModeActive() => safeModeActive;

        /// <summary>
        /// Obtient le nombre d'actions dans la dernière minute.
        /// </summary>
        public int GetActionsPerMinute()
        {
            return recentActions.Count(a => (DateTime.Now - a).TotalMinutes <= 1);
        }

        /// <summary>
        /// Obtient le nombre d'actions dans la dernière heure.
        /// </summary>
        public int GetActionsPerHour()
        {
            return recentActions.Count;
        }

        /// <summary>
        /// Obtient le nombre d'erreurs dans les 5 dernières minutes.
        /// </summary>
        public int GetRecentErrorCount()
        {
            return recentErrors.Count(e => (DateTime.Now - e.Timestamp).TotalMinutes <= 5);
        }

        /// <summary>
        /// Retourne un rapport de santé actuel.
        /// </summary>
        public string GetHealthReport()
        {
            var actionsPerMin = GetActionsPerMinute();
            var actionsPerHour = GetActionsPerHour();
            var recentErrorCount = GetRecentErrorCount();

            return $"Actions/min: {actionsPerMin}/{MaxActionsPerMinute} | " +
                   $"Actions/hour: {actionsPerHour}/{MaxActionsPerHour} | " +
                   $"Errors (5min): {recentErrorCount} | " +
                   $"Safe Mode: {(safeModeActive ? "ACTIVE" : "OFF")}";
        }

        /// <summary>
        /// Vérifie si on peut effectuer une nouvelle action sans dépasser les limites.
        /// </summary>
        public bool CanPerformAction(out string waitReason)
        {
            waitReason = null;

            if (safeModeActive)
            {
                waitReason = "Safe mode is active";
                return false;
            }

            var actionsPerMin = GetActionsPerMinute();
            if (actionsPerMin >= MaxActionsPerMinute)
            {
                waitReason = $"Rate limit: {actionsPerMin}/{MaxActionsPerMinute} actions per minute";
                return false;
            }

            var actionsPerHour = GetActionsPerHour();
            if (actionsPerHour >= MaxActionsPerHour)
            {
                waitReason = $"Rate limit: {actionsPerHour}/{MaxActionsPerHour} actions per hour";
                return false;
            }

            return true;
        }
    }
}
