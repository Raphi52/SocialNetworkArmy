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
    public partial class TikTokBotForm : Form
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

        public TikTokBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            automationService = new AutomationService(new FingerprintService(), new ProxyService(), limitsService, cleanupService, monitoringService, profile);
            InitializeComponent();
            this.FormClosing += (s, e) => {
                if (isScriptRunning)
                {
                    StopScript();
                    e.Cancel = true;
                    Task.Delay(1000).ContinueWith(t => this.Close());
                }
            };
        }

        private void InitializeComponent()
        {
            // Dark mode : Fond sombre
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Font = yaheiBold12; // Police globale sur la form (propagé aux enfants)

            this.ClientSize = new Size(900, 650);
            this.Text = $"TikTok Bot - {profile.Name}";
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

            if (webView.CoreWebView2 != null)
            {
                // User-Agent mobile iPhone pour éviter "page not available" sur TikTok
                webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1";
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
            }

            webView.CoreWebView2.Navigate("https://www.tiktok.com/foryou"); // Page feed stable au lieu de home

            await Task.Delay(3000); // Wait plus long pour load
            webView.Focus();
            webView.BringToFront();
            webView.DefaultBackgroundColor = Color.Black;

            // Check post-load si page OK
            try
            {
                var title = await webView.ExecuteScriptAsync("document.title");
                if (title.Contains("TikTok") || title.Contains("For You"))
                {
                    logTextBox.AppendText("TikTok chargé avec succès.\r\n");
                }
                else
                {
                    logTextBox.AppendText("Attention : Page TikTok peut ne pas être chargée correctement.\r\n");
                }
            }
            catch
            {
                logTextBox.AppendText("Erreur check TikTok load.\r\n");
            }

            await monitoringService.TestFingerprintAsync(webView);
            Logger.LogInfo("WebView TikTok focused and interactions enabled.");
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

        private async void TargetButton_Click(object sender, EventArgs e)
        {
            await StartScriptAsync("Target");

            var targetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "targets.txt");
            var targets = Helpers.LoadTargets(targetsPath);
            if (!targets.Any())
            {
                logTextBox.AppendText("Aucun target trouvé dans targets.txt !\r\n");
                StopScript();
                return;
            }

            var jsonData = JsonConvert.SerializeObject(targets);
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "tiktok", "target.js");

            if (File.Exists(scriptPath))
            {
                var scriptContent = File.ReadAllText(scriptPath);
                var fullScript = $"var data = {jsonData}; {scriptContent}";

                await automationService.ExecuteActionWithLimitsAsync(webView, "target", "likes", async () => {
                    try
                    {
                        await webView.ExecuteScriptAsync(fullScript);
                    }
                    catch (Exception scriptEx)
                    {
                        logTextBox.AppendText($"Erreur script JS : {scriptEx.Message}\r\n");
                        Logger.LogError($"JS Error in Target : {scriptEx}");
                    }
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
            webView.CoreWebView2.Navigate("https://www.tiktok.com/foryou");

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "tiktok", "scroll.js");

            await automationService.ExecuteActionWithLimitsAsync(webView, "scroll", "views", async () => {
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
                                                          s.Platform == "TikTok").ToList();

            if (!filteredSchedule.Any())
            {
                logTextBox.AppendText("Aucune publication prévue aujourd'hui pour ce profil.\r\n");
                StopScript();
                return;
            }

            var jsonData = JsonConvert.SerializeObject(filteredSchedule);
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "tiktok", "publish.js");

            if (File.Exists(scriptPath))
            {
                var scriptContent = File.ReadAllText(scriptPath);
                var fullScript = $"var data = {jsonData}; {scriptContent}";

                await automationService.ExecuteActionWithLimitsAsync(webView, "publish", "videos", async () => {
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
            }
            base.Dispose(disposing);
        }
    }
}