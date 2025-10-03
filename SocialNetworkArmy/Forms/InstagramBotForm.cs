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

        public InstagramBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            automationService = new AutomationService(new FingerprintService(), new ProxyService(), limitsService, cleanupService, monitoringService, profile);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.ClientSize = new Size(900, 650);  // Augmenté en hauteur pour accommoder la textbox plus grande
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            // Crée le panel en premier, hauteur augmentée à 150 pour plus d'espace logs
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
            targetButton = new Button { Text = "Target", Location = new Point(10, 10), Size = new Size(100, 30) };
            targetButton.Click += TargetButton_Click;
            scrollButton = new Button { Text = "Scroll", Location = new Point(120, 10), Size = new Size(100, 30) };
            scrollButton.Click += ScrollButton_Click;
            publishButton = new Button { Text = "Publish", Location = new Point(230, 10), Size = new Size(100, 30) };
            publishButton.Click += PublishButton_Click;

            // Bouton Stop
            stopButton = new Button
            {
                Text = "Stop",
                Location = new Point(340, 10),
                Size = new Size(100, 30),
                BackColor = System.Drawing.Color.Red,
                ForeColor = System.Drawing.Color.White,
                Enabled = false
            };
            stopButton.Click += StopButton_Click;

            // TextBox logs agrandie : plus haute (90 au lieu de 40), même x mais y ajusté si besoin
            logTextBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 50), Size = new Size(880, 90), ReadOnly = true };

            panel.Controls.Add(targetButton);
            panel.Controls.Add(scrollButton);
            panel.Controls.Add(publishButton);
            panel.Controls.Add(stopButton);
            panel.Controls.Add(logTextBox);
            this.Controls.Add(panel);  // Panel en premier

            webView = new WebView2 { Dock = DockStyle.Fill };  // WebView2 après, s'ajuste automatiquement
            this.Controls.Add(webView);  // Ajouté en dernier

            LoadBrowserAsync();
        }

        private async void LoadBrowserAsync()
        {
            var userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialNetworkArmy", profile.Name);
            webView = await automationService.InitializeBrowserAsync(profile, userDataDir);
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);
            webView.CoreWebView2.Navigate("https://www.instagram.com/");

            // Ajouts pour activer les clics/focus
            await Task.Delay(2000);  // Délai pour que la page charge
            webView.Focus();  // Force le focus sur WebView
            webView.BringToFront();  // Met au premier plan
            webView.DefaultBackgroundColor = System.Drawing.Color.White;  // Force un redraw

            // Active menu contextuel et scripts
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
            }

            // Intégration : Test fingerprint après init
            await monitoringService.TestFingerprintAsync(webView);

            Logger.LogInfo("WebView Instagram focused and interactions enabled.");
        }

        // Méthodes pour démarrer un script (ajoute le flag isRunning)
        private async Task StartScriptAsync(string actionName)
        {
            if (isScriptRunning)
            {
                logTextBox.AppendText($"Script {actionName} déjà en cours ! Arrêtez d'abord.\r\n");
                return;
            }

            isScriptRunning = true;
            stopButton.Enabled = true;  // Active le bouton Stop
            targetButton.Enabled = false;  // Désactive les autres boutons pendant exécution
            scrollButton.Enabled = false;
            publishButton.Enabled = false;
            logTextBox.AppendText($"Démarrage {actionName}...\r\n");

            // Injecte le flag JS pour démarrer
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

            // Set le flag JS à false pour stopper les boucles
            _ = Task.Run(async () => {
                if (webView?.CoreWebView2 != null)
                {
                    await webView.ExecuteScriptAsync("window.isRunning = false; console.log('Script arrêté par l\'utilisateur');");
                    // Optionnel : Arrête toute navigation en cours
                    webView.CoreWebView2.Stop();
                }
                // Intégration : Cleanup après arrêt
                await automationService.CleanupAsync(webView, profile);
            });
        }

        private async void TargetButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Target");

            // Charge les targets depuis targets.txt
            var targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "targets.txt");
            var targets = Helpers.LoadTargets(targetsPath);
            if (!targets.Any())
            {
                logTextBox.AppendText("Aucun target trouvé dans targets.txt !\r\n");
                StopScript();  // Reset si erreur
                return;
            }

            var jsonData = JsonConvert.SerializeObject(targets);
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "instagram", "target.js");

            if (File.Exists(scriptPath))
            {
                var scriptContent = File.ReadAllText(scriptPath);
                var fullScript = $"var data = {jsonData}; {scriptContent}";

                // Intégration : Wrap avec limits et CAPTCHA check
                await automationService.ExecuteActionWithLimitsAsync(webView, "target", "likes", async () => {
                    await webView.ExecuteScriptAsync(fullScript);
                });

                logTextBox.AppendText($"Target lancé sur {targets.Count} profils.\r\n");
            }
            else
            {
                logTextBox.AppendText("Script target.js introuvable !\r\n");
                StopScript();
            }
        }

        private async void ScrollButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Scroll");
            webView.CoreWebView2.Navigate("https://www.instagram.com/reels/");

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "instagram", "scroll.js");

            // Intégration : Wrap avec limits
            await automationService.ExecuteActionWithLimitsAsync(webView, "scroll", "likes", async () => {
                await automationService.ExecuteScriptAsync(webView, scriptPath);
                await automationService.SimulateHumanScrollAsync(webView, Config.GetConfig().ScrollDurationMin * 60);
            });
        }

        private async void PublishButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Publish");

            // Charge et filtre le schedule (aujourd'hui + profil + plateforme)
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

                // Intégration : Wrap avec limits pour posts
                await automationService.ExecuteActionWithLimitsAsync(webView, "publish", "posts", async () => {
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
    }
}