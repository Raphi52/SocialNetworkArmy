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
        private WebView2 webView;
        private Button targetButton;
        private Button scrollButton;
        private Button publishButton;
        private Button stopButton;
        private TextBox logTextBox;
        private bool isScriptRunning = false;

        public TikTokBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            automationService = new AutomationService(new FingerprintService(), new ProxyService(), limitsService, null, null, profile);
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
            this.ClientSize = new Size(900, 650);
            this.Text = $"TikTok Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
            targetButton = new Button { Text = "Target", Location = new Point(10, 10), Size = new Size(100, 30) };
            targetButton.Click += TargetButton_Click;
            scrollButton = new Button { Text = "Scroll", Location = new Point(120, 10), Size = new Size(100, 30) };
            scrollButton.Click += ScrollButton_Click;
            publishButton = new Button { Text = "Publish", Location = new Point(230, 10), Size = new Size(100, 30) };
            publishButton.Click += PublishButton_Click;

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

            logTextBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(10, 50), Size = new Size(880, 90), ReadOnly = true };

            panel.Controls.Add(targetButton);
            panel.Controls.Add(scrollButton);
            panel.Controls.Add(publishButton);
            panel.Controls.Add(stopButton);
            panel.Controls.Add(logTextBox);
            this.Controls.Add(panel);

            webView = new WebView2 { Dock = DockStyle.Fill };
            this.Controls.Add(webView);

            LoadBrowserAsync();
        }

        private async void LoadBrowserAsync()
        {
            var userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialNetworkArmy", profile.Name);
            webView = await automationService.InitializeBrowserAsync(profile, userDataDir);
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);
            webView.CoreWebView2.Navigate("https://www.tiktok.com/");

            await Task.Delay(2000);
            webView.Focus();
            webView.BringToFront();
            webView.DefaultBackgroundColor = System.Drawing.Color.White;

            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
            }

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
    }
}