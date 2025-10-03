using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Forms
{
    public partial class InstagramBotForm : Form
    {
        private readonly Profile profile;
        private readonly AutomationService automationService;
        private readonly LimitsService limitsService;
        private readonly CleanupService cleanupService;
        private readonly MonitoringService monitoringService;
        private WebView2 webView;
        private Button targetButton;
        private Button scrollButton;
        private Button publishButton;
        private Button stopButton;
        private TextBox logTextBox;
        private bool isScriptRunning = false;

        private Font yaheiBold12 = new Font("Microsoft YaHei", 12f, FontStyle.Bold); // Police globale YaHei 12 gras

        private System.Windows.Forms.Timer closeTimer; // Timer pour delay safe sans re-entrancy

        public InstagramBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            automationService = new AutomationService(new FingerprintService(), new ProxyService(), limitsService, cleanupService, monitoringService, profile);
            InitializeComponent();

            // Timer pour retry close safe
            closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                this.Close(); // Safe après delay
            };

            this.FormClosing += OnFormClosing; // Handler nommé pour éviter re-entrancy
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (isScriptRunning)
            {
                e.Cancel = true;
                StopScript(); // Sync stop

                // Delay via timer au lieu de Task (évite cancel op)
                closeTimer.Interval = 1000;
                closeTimer.Start();
                return;
            }
        }

        private void InitializeComponent()
        {
            // Dark mode : Fond sombre
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Font = yaheiBold12; // Police globale sur la form (propagé aux enfants)

            this.ClientSize = new Size(900, 650);
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Color.FromArgb(30, 30, 30) };
            targetButton = new Button { Text = "Target", Location = new Point(10, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            targetButton.FlatAppearance.BorderSize = 2;
            targetButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80); // Vert
            targetButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            targetButton.Click += TargetButton_Click;

            scrollButton = new Button { Text = "Scroll", Location = new Point(130, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            scrollButton.FlatAppearance.BorderSize = 2;
            scrollButton.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243); // Bleu
            scrollButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            scrollButton.Click += ScrollButton_Click;

            publishButton = new Button { Text = "Publish", Location = new Point(250, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            publishButton.FlatAppearance.BorderSize = 2;
            publishButton.FlatAppearance.BorderColor = Color.FromArgb(255, 152, 0); // Orange
            publishButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            publishButton.Click += PublishButton_Click;

            stopButton = new Button
            {
                Text = "Stop",
                Location = new Point(370, 10),
                Size = new Size(110, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Enabled = false,
                Font = yaheiBold12
            };
            stopButton.FlatAppearance.BorderSize = 2;
            stopButton.FlatAppearance.BorderColor = Color.FromArgb(244, 67, 54); // Rouge
            stopButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            stopButton.Click += StopButton_Click;

            logTextBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 50), Size = new Size(880, 90), ReadOnly = true, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = yaheiBold12 };

            panel.Controls.Add(targetButton);
            panel.Controls.Add(scrollButton);
            panel.Controls.Add(publishButton);
            panel.Controls.Add(stopButton);
            panel.Controls.Add(logTextBox);
            this.Controls.Add(panel);

            webView = new WebView2 { Dock = DockStyle.Fill };
            webView.DefaultBackgroundColor = Color.Black;
            this.Controls.Add(webView);

            LoadBrowserAsync();
        }

        private async void LoadBrowserAsync()
        {
            var userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialNetworkArmy", profile.Name);
            webView = await automationService.InitializeBrowserAsync(profile, userDataDir);
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);
            webView.CoreWebView2.Navigate("https://www.instagram.com/");

            await Task.Delay(2000);
            webView.Focus();
            webView.BringToFront();
            webView.DefaultBackgroundColor = Color.Black;

            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
            }

            await monitoringService.TestFingerprintAsync(webView);
            Logger.LogInfo("WebView Instagram focused and interactions enabled.");
        }

        private async Task StartScriptAsync(string actionName)
        {
            if (isScriptRunning)
            {
                logTextBox.AppendText($"Script {actionName} déjà en cours ! Arrêtez d'abord.\r\n");
                return;
            }

            isScriptRunning = true;
            stopButton.Enabled = true;
            targetButton.Enabled = false;
            scrollButton.Enabled = false;
            publishButton.Enabled = false;
            logTextBox.AppendText($"Démarrage {actionName}...\r\n");

            if (webView?.CoreWebView2 != null)
                await webView.ExecuteScriptAsync("window.isRunning = true; console.log('Script démarré');");
        }

        private void StopScript()
        {
            if (!isScriptRunning) return;

            isScriptRunning = false;
            stopButton.Enabled = false;
            targetButton.Enabled = true;
            scrollButton.Enabled = true;
            publishButton.Enabled = true;
            logTextBox.AppendText("Arrêt du script en cours...\r\n");

            try
            {
                if (webView?.CoreWebView2 != null)
                    webView.ExecuteScriptAsync("window.isRunning = false; console.log('Script arrêté');");
                logTextBox.AppendText("Flag JS envoyé – Script arrêté avec succès.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"Erreur Stop (ignorée) : {ex.Message}\r\n");
                Logger.LogError($"Erreur StopScript : {ex}");
            }
        }

        // InstagramBotForm.cs
        // InstagramBotForm.cs
        // InstagramBotForm.cs
        private async void TargetButton_Click(object sender, EventArgs e)
        {
            // 0) Démarrage « Target » (UI + flag JS)
            await StartScriptAsync("Target");

            try
            {
                // 1) Charger la liste des cibles
                var targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "targets.txt");
                var targets = Helpers.LoadTargets(targetsPath);
                if (!targets.Any())
                {
                    logTextBox.AppendText("Aucun target trouvé dans targets.txt !\r\n");
                    StopScript();
                    return;
                }

                // 2) (Optionnel) ouvrir DevTools pour voir console.log/erreurs
                try { webView.CoreWebView2?.OpenDevToolsWindow(); } catch { /* ignore */ }

                // 3) Aller sur la page Reels du 1er target
                var firstTarget = targets.First().Trim();
                var targetUrl = $"https://www.instagram.com/{firstTarget}/reels/";
                logTextBox.AppendText($"[NAV] Vers {targetUrl}\r\n");
                webView.CoreWebView2.Navigate(targetUrl);

                // 4) Attendre un peu que la navigation se stabilise
                await Task.Delay(3000);

                // 5) Lire l’URL et le titre (diagnostic)
                var url = await webView.ExecuteScriptAsync("window.location.href");
                var title = await webView.ExecuteScriptAsync("document.title");
                logTextBox.AppendText($"[NAV] url={url}, title={title}\r\n");

                // 6) Vérifier login (mur de connexion)
                var loginWall = await webView.ExecuteScriptAsync(@"
(function(){
  return !!document.querySelector('[href*=""/accounts/login/""], .login-button');
})()");
                if (string.Equals(loginWall, "true", StringComparison.OrdinalIgnoreCase))
                {
                    logTextBox.AppendText("[CHECK] Login requis : connecte-toi dans la WebView puis relance.\r\n");
                    StopScript();
                    return;
                }

                // 7) Sélecteur 1er Reel (priorité au grid <article>)
                var findReelScript = @"
(function(){
  const a = document.querySelector('article a[href*=""/reel/""]')
        || document.querySelector('a[href*=""/reel/""]');
  return a ? a.href : null;
})()";

                var reelHref = await webView.ExecuteScriptAsync(findReelScript);
                logTextBox.AppendText($"[SELECTOR] 1er reel href (avant scroll) = {reelHref}\r\n");

                // 8) Lazy-load par scroll si rien
                if (reelHref == "null")
                {
                    logTextBox.AppendText("[SCROLL] Lazy-load…\r\n");
                    await webView.ExecuteScriptAsync(@"
(async function(){
  for(let i=0;i<6;i++){
    window.scrollBy(0, window.innerHeight);
    await new Promise(r => setTimeout(r, 800));
  }
  return true;
})()");
                    await Task.Delay(1000);

                    reelHref = await webView.ExecuteScriptAsync(findReelScript);
                    logTextBox.AppendText($"[SELECTOR] 1er reel href (après scroll) = {reelHref}\r\n");
                }

                // 9) Si toujours rien → abandon propre
                if (reelHref == "null")
                {
                    logTextBox.AppendText("[ERREUR] Aucun Reel détecté sur la page du profil.\r\n");
                    StopScript();
                    return;
                }

                // 10) Tentative 1 : click simple
                var clickSimple = await webView.ExecuteScriptAsync(@"
(function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  el.click();
  return 'CLICKED';
})()");
                logTextBox.AppendText($"[CLICK_SIMPLE] résultat={clickSimple}\r\n");

                // 11) Check ouverture (URL / modal / vidéo)
                var openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                logTextBox.AppendText($"[CHECK_OPENED] après click simple => {openedCheck}\r\n");

                // 12) Si pas ouvert, MouseEvents SANS 'click' pour privilégier le MODAL
                if (!string.Equals(openedCheck, "true", StringComparison.OrdinalIgnoreCase))
                {
                    var clickMouseEvents = await webView.ExecuteScriptAsync(@"
(async function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  await new Promise(r=>setTimeout(r,500));
  const r = el.getBoundingClientRect();
  const x = r.left + r.width/2, y = r.top + r.height/2;
  el.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,clientX:x,clientY:y}));
  el.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,clientX:x,clientY:y}));
  return 'MOUSE_EVENTS_SENT';
})()");
                    logTextBox.AppendText($"[CLICK_MOUSE] résultat={clickMouseEvents}\r\n");

                    // 🔁 Re-vérifier ouverture (recalcul !)
                    openedCheck = await webView.ExecuteScriptAsync(@"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()");
                    logTextBox.AppendText($"[CHECK_OPENED] après mouse events => {openedCheck}\r\n");
                }

                // 13) Fin : log de succès / échec
                if (string.Equals(openedCheck, "true", StringComparison.OrdinalIgnoreCase))
                {
                    logTextBox.AppendText("[OK] Premier Reel ouvert avec succès ✅\r\n");
                }
                else
                {
                    logTextBox.AppendText("[KO] Impossible d’ouvrir le 1er Reel (sélecteur/clic).❌\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[EXCEPTION] {ex.Message}\r\n");
                Logger.LogError($"TargetButton_Click: {ex}");
            }
            finally
            {
                // Remet l’UI proprement (conforme à ton flux actuel)
                StopScript();
            }
        }


        private async void ScrollButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Scroll");
            webView.CoreWebView2.Navigate("https://www.instagram.com/reels/");

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "instagram", "scroll.js");

            await automationService.ExecuteActionWithLimitsAsync(webView, "scroll", "likes", async () =>
            {
                await automationService.ExecuteScriptAsync(webView, scriptPath);
                await automationService.SimulateHumanScrollAsync(webView, Config.GetConfig().ScrollDurationMin * 60);
            });
        }

        private async void PublishButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Publish");

            var today = DateTime.Today;
            var schedulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "schedule.csv");
            var allSchedule = Helpers.LoadSchedule(schedulePath);
            var filteredSchedule = allSchedule.Where(s => s.Date.Date == today &&
                                                          s.Account == profile.Name &&
                                                          s.Platform == "Instagram").ToList();

            if (!filteredSchedule.Any())
            {
                logTextBox.AppendText("Aucune publication prévue aujourd'hui pour ce profil.\r\n");
                StopScript();
                return;
            }

            var jsonData = JsonConvert.SerializeObject(filteredSchedule);
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "instagram", "publish.js");

            if (File.Exists(scriptPath))
            {
                var scriptContent = File.ReadAllText(scriptPath);
                var fullScript = $"var data = {jsonData}; {scriptContent}";

                await automationService.ExecuteActionWithLimitsAsync(webView, "publish", "posts", async () =>
                {
                    await webView.ExecuteScriptAsync(fullScript);
                });

                logTextBox.AppendText($"Publish lancé sur {filteredSchedule.Count} entrées.\r\n");
            }
            else
            {
                logTextBox.AppendText("Script publish.js introuvable !\r\n");
                StopScript();
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopScript();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                yaheiBold12.Dispose(); // Clean font
                closeTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}