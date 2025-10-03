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
        private TargetService targetService;   // <-- sera défini après init
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

        private Font yaheiBold12 = new Font("Microsoft YaHei", 12f, FontStyle.Bold);
        private System.Windows.Forms.Timer closeTimer;

        public InstagramBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            automationService = new AutomationService(new FingerprintService(), new ProxyService(), limitsService, cleanupService, monitoringService, profile);

            InitializeComponent();

            // désactiver Target tant que WebView2 n'est pas prêt
            targetButton.Enabled = false;

            // Timer pour retry close safe
            closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            closeTimer.Tick += (s, e) => { closeTimer.Stop(); this.Close(); };

            this.FormClosing += OnFormClosing;

            // lance l'init async
            LoadBrowserAsync();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (isScriptRunning)
            {
                e.Cancel = true;
                StopScript();
                closeTimer.Interval = 1000;
                closeTimer.Start();
                return;
            }
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Font = yaheiBold12;

            this.ClientSize = new Size(900, 650);
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Color.FromArgb(30, 30, 30) };

            targetButton = new Button { Text = "Target", Location = new Point(10, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            targetButton.FlatAppearance.BorderSize = 2;
            targetButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80);
            targetButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            targetButton.Click += TargetButton_Click;

            scrollButton = new Button { Text = "Scroll", Location = new Point(130, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            scrollButton.FlatAppearance.BorderSize = 2;
            scrollButton.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243);
            scrollButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            scrollButton.Click += ScrollButton_Click;

            publishButton = new Button { Text = "Publish", Location = new Point(250, 10), Size = new Size(110, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, UseVisualStyleBackColor = false, Font = yaheiBold12 };
            publishButton.FlatAppearance.BorderSize = 2;
            publishButton.FlatAppearance.BorderColor = Color.FromArgb(255, 152, 0);
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
            stopButton.FlatAppearance.BorderColor = Color.FromArgb(244, 67, 54);
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
        }

        private async void LoadBrowserAsync()
        {
            try
            {
                var userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SocialNetworkArmy",
                    profile.Name
                );

                // webView est déjà ajouté au form dans InitializeComponent
                // On initialise le runtime/instance CoreWebView2
                await webView.EnsureCoreWebView2Async(null);

                // (si tu gardes InitializeBrowserAsync, tu peux remplacer Ensure... par ton appel)
                // webView = await automationService.InitializeBrowserAsync(profile, userDataDir);

                // Réglages
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.IsScriptEnabled = true;
                    webView.CoreWebView2.Navigate("https://www.instagram.com/");
                }

                await Task.Delay(800);
                webView.Focus();
                webView.BringToFront();

                // Maintenant que CoreWebView2 existe -> on peut créer TargetService
                targetButton.Enabled = true;
                targetService = new TargetService(webView, logTextBox, profile, this);

                // (optionnel) test fp
                try { await monitoringService.TestFingerprintAsync(webView); } catch { /* ignore */ }

                Logger.LogInfo("WebView2 prêt. TargetService initialisé.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erreur d'initialisation WebView2 : " + ex);
                logTextBox.AppendText("[INIT] Erreur WebView2 : " + ex.Message + "\r\n");
            }
        }

        public async Task StartScriptAsync(string actionName)
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

        public void StopScript()
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

        // ========================= TARGET =========================
        private async void TargetButton_Click(object sender, EventArgs e)
        {
            if (targetService == null)
            {
                logTextBox.AppendText("[INIT] Navigateur en cours d'initialisation… réessaie dans 1–2s.\r\n");
                return;
            }
            await targetService.RunAsync();
        }

        // ========================= SCROLL =========================
        private async void ScrollButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Scroll");
            if (webView?.CoreWebView2 == null)
            {
                logTextBox.AppendText("[SCROLL] WebView non prêt.\r\n");
                StopScript();
                return;
            }

            webView.CoreWebView2.Navigate("https://www.instagram.com/reels/");

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "instagram", "scroll.js");

            await automationService.ExecuteActionWithLimitsAsync(webView, "scroll", "likes", async () =>
            {
                await automationService.ExecuteScriptAsync(webView, scriptPath);
                await automationService.SimulateHumanScrollAsync(webView, Config.GetConfig().ScrollDurationMin * 60);
            });

            StopScript();
        }

        // ========================= PUBLISH =========================
        private async void PublishButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Publish");

            var today = DateTime.Today;
            var schedulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "schedule.csv");
            var allSchedule = Helpers.LoadSchedule(schedulePath);
            var filteredSchedule = allSchedule
                .Where(s => s.Date.Date == today && s.Account == profile.Name && s.Platform == "Instagram")
                .ToList();

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
            }

            StopScript();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopScript();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                yaheiBold12.Dispose();
                closeTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
