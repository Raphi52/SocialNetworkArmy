using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SocialNetworkArmy.Models;

namespace SocialNetworkArmy.Forms
{
    public static class FormManager
    {
        private static InstagramBotForm _activeBotForm;
        private static StoryPosterForm _activeStoryForm;
        private static TiktokBotForm _activeTikTokForm;

        // ✅ ANTI-SPAM: Empêcher les ouvertures trop rapides
        private static DateTime _lastOpenTime = DateTime.MinValue;
        private static readonly object _openLock = new object();
        private const int MIN_DELAY_MS = 800; // Minimum 800ms entre chaque ouverture

        public static async void OpenInstagramBotForm(Profile profile)
        {
            if (!CanOpen()) return;

            CloseStoryForm();
            CloseTikTokForm();
            CloseInstagramBotForm();

            // ✅ Attendre que les forms se ferment complètement
            await Task.Delay(300);

            _activeBotForm = new InstagramBotForm(profile);
            _activeBotForm.FormClosed += (s, e) => _activeBotForm = null;
            _activeBotForm.Show();
        }

        public static async void OpenStoryPosterForm(Profile profile)
        {
            if (!CanOpen()) return;

            CloseInstagramBotForm();
            CloseTikTokForm();
            CloseStoryForm();

            // ✅ Attendre que les forms se ferment complètement
            await Task.Delay(300);

            _activeStoryForm = new StoryPosterForm(profile);
            _activeStoryForm.FormClosed += (s, e) => _activeStoryForm = null;
            _activeStoryForm.Show();
        }

        public static async void OpenTikTokBotForm(Profile profile)
        {
            if (!CanOpen()) return;

            CloseStoryForm();
            CloseInstagramBotForm();
            CloseTikTokForm();

            // ✅ Attendre que les forms se ferment complètement
            await Task.Delay(300);

            _activeTikTokForm = new TiktokBotForm(profile);
            _activeTikTokForm.FormClosed += (s, e) => _activeTikTokForm = null;
            _activeTikTokForm.Show();
        }

        // ✅ NOUVELLE MÉTHODE: Vérifier si on peut ouvrir (anti-spam)
        private static bool CanOpen()
        {
            lock (_openLock)
            {
                var now = DateTime.Now;
                var elapsed = (now - _lastOpenTime).TotalMilliseconds;

                if (elapsed < MIN_DELAY_MS)
                {
                    MessageBox.Show(
                        $"Please wait {(MIN_DELAY_MS - elapsed) / 1000:F1}s before opening another window.",
                        "Too Fast",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                _lastOpenTime = now;
                return true;
            }
        }

        private static void CloseInstagramBotForm()
        {
            var form = _activeBotForm;
            if (form != null && !form.IsDisposed)
            {
                _activeBotForm = null;
                try
                {
                    form.Close();
                    form.Dispose();
                }
                catch
                {
                    // Ignorer les erreurs
                }
            }
        }

        private static void CloseStoryForm()
        {
            var form = _activeStoryForm;
            if (form != null && !form.IsDisposed)
            {
                _activeStoryForm = null;
                try
                {
                    form.Close();
                    form.Dispose();
                }
                catch
                {
                    // Ignorer les erreurs
                }
            }
        }

        private static void CloseTikTokForm()
        {
            var form = _activeTikTokForm;
            if (form != null && !form.IsDisposed)
            {
                _activeTikTokForm = null;
                try
                {
                    form.Close();
                    form.Dispose();
                }
                catch
                {
                    // Ignorer les erreurs
                }
            }
        }

        public static void CloseAll()
        {
            CloseInstagramBotForm();
            CloseStoryForm();
            CloseTikTokForm();
        }
    }
}