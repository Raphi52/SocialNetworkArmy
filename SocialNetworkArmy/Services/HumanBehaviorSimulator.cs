using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Simule des comportements humains naturels pour éviter la détection de bot.
    /// Inclut des pauses aléatoires, mouvements de souris, etc.
    /// </summary>
    public class HumanBehaviorSimulator
    {
        private readonly Random rand;
        private readonly WebView2 webView;

        public HumanBehaviorSimulator(WebView2 webView)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.rand = new Random();
        }

        /// <summary>
        /// Pause aléatoire simulant un comportement humain.
        /// 96% du temps: pause courte (0.5-2s)
        /// 4% du temps: pause longue (10-60s) comme si l'utilisateur lisait/réfléchissait
        /// </summary>
        public async Task RandomHumanPauseAsync(
            CancellationToken token,
            int minShort = 500,
            int maxShort = 2000,
            double longPauseChance = 0.04,
            int minLong = 10000,
            int maxLong = 60000)
        {
            if (rand.NextDouble() < longPauseChance)
            {
                // Pause longue (rare)
                int longDelay = rand.Next(minLong, maxLong);
                await Task.Delay(longDelay, token);
            }
            else
            {
                // Pause courte (fréquent)
                int shortDelay = rand.Next(minShort, maxShort);
                await Task.Delay(shortDelay, token);
            }
        }

        /// <summary>
        /// Simule des micro-mouvements de souris aléatoires.
        /// Ajoute du "bruit" humain naturel pour éviter la détection.
        /// </summary>
        public async Task RandomHumanNoiseAsync(CancellationToken token)
        {
            try
            {
                // 40% de chance d'ajouter du bruit
                if (rand.NextDouble() < 0.4)
                {
                    // Nombre de micro-mouvements (1 à 3)
                    int movements = rand.Next(1, 4);

                    for (int i = 0; i < movements; i++)
                    {
                        // Position aléatoire sur l'écran
                        int x = rand.Next(100, 800);
                        int y = rand.Next(100, 600);

                        // Simuler un mouvement de souris via CDP
                        string moveScript = $@"
(function(){{
  const event = new MouseEvent('mousemove', {{
    clientX: {x},
    clientY: {y},
    bubbles: true
  }});
  document.dispatchEvent(event);
  return 'MOVED';
}})()";

                        await webView.CoreWebView2.ExecuteScriptAsync(moveScript);

                        // Petite pause entre les mouvements (30-100ms)
                        await Task.Delay(rand.Next(30, 100), token);
                    }
                }
            }
            catch (Exception)
            {
                // Ignorer les erreurs de simulation de bruit
                // (pas critique pour le fonctionnement)
            }
        }

        /// <summary>
        /// Pause courte typique d'un utilisateur regardant quelque chose (0.8-2s).
        /// </summary>
        public async Task ShortViewPauseAsync(CancellationToken token)
        {
            int delay = rand.Next(800, 2000);
            await Task.Delay(delay, token);
        }

        /// <summary>
        /// Pause moyenne pour une lecture/interaction (2-5s).
        /// </summary>
        public async Task MediumInteractionPauseAsync(CancellationToken token)
        {
            int delay = rand.Next(2000, 5000);
            await Task.Delay(delay, token);
        }

        /// <summary>
        /// Retourne un délai aléatoire dans une plage donnée (en millisecondes).
        /// </summary>
        public int GetRandomDelay(int min, int max)
        {
            return rand.Next(min, max);
        }

        /// <summary>
        /// Décide aléatoirement si une action doit être effectuée (basé sur une probabilité).
        /// </summary>
        /// <param name="probability">Probabilité entre 0.0 et 1.0 (ex: 0.8 = 80%)</param>
        public bool ShouldPerformAction(double probability)
        {
            return rand.NextDouble() < probability;
        }
    }
}
