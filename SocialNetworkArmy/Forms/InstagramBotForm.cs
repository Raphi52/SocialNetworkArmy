using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
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
        private TargetService targetService;   // défini après init
        private ScrollService scrollService;
        private PublishService publishService;
        private DirectMessageService dmService;
        private DownloadInstagramService downloadService;
        private readonly Profile profile;
        private readonly AutomationService automationService;
        private readonly ProxyService proxyService; // Added for direct access
        private readonly LimitsService limitsService;
        private readonly CleanupService cleanupService;
        private readonly MonitoringService monitoringService;
        private WebView2 webView;
        private Button targetButton;
        private Button scrollButton;
        private Button publishButton;
        private Button dmButton;
        private Button downloadButton;
        private Button stopButton;
        private TextBox logTextBox;
        private Label lblProxyStatus; // Added for proxy indicator
        private bool isScriptRunning = false;
        private Font yaheiBold12 = new Font("Microsoft YaHei", 12f, FontStyle.Bold);
        private System.Windows.Forms.Timer closeTimer;
        private CancellationTokenSource _cts;
        public InstagramBotForm(Profile profile)
        {
            this.profile = profile;
            limitsService = new LimitsService(profile.Name);
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            proxyService = new ProxyService(); // Initialized separately for access
            automationService = new AutomationService(new FingerprintService(), proxyService, limitsService, cleanupService, monitoringService, profile);
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
            // Fenêtre
            this.ClientSize = new Size(1200, 800);
            this.MinimumSize = new Size(1000, 700);
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;
            // WebView
            webView = new WebView2();
            webView.DefaultBackgroundColor = Color.Black;
            webView.Location = new Point(0, 0);
            webView.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 240);
            webView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(webView);
            // Panneau bas
            var bottomPanel = new Panel
            {
                Location = new Point(0, this.ClientSize.Height - 240),
                Size = new Size(this.ClientSize.Width, 240),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(0)
            };
            // Bandeau boutons
            var buttonsPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(bottomPanel.Width - 20, 48),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            // Styles communs
            Size btnSize = new Size(120, 36);
            Padding btnMargin = new Padding(0, 0, 10, 0);
            targetButton = new Button
            {
                Text = "Target",
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12,
                Margin = btnMargin,
                Enabled = false
            };
            targetButton.FlatAppearance.BorderSize = 2;
            targetButton.FlatAppearance.BorderColor = Color.FromArgb(76, 175, 80);
            targetButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            targetButton.Click += TargetButton_Click;
            scrollButton = new Button
            {
                Text = "Scroll",
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12,
                Margin = btnMargin
            };
            scrollButton.FlatAppearance.BorderSize = 2;
            scrollButton.FlatAppearance.BorderColor = Color.FromArgb(33, 150, 243);
            scrollButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            scrollButton.Click += ScrollButton_Click;
            publishButton = new Button
            {
                Text = "Publish",
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12,
                Margin = btnMargin
            };
            publishButton.FlatAppearance.BorderSize = 2;
            publishButton.FlatAppearance.BorderColor = Color.FromArgb(255, 152, 0);
            publishButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            publishButton.Click += PublishButton_Click;
            dmButton = new Button
            {
                Text = "Direct Messages",
                Size = new Size(160, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12,
                Margin = btnMargin
            };
            dmButton.FlatAppearance.BorderSize = 2;
            dmButton.FlatAppearance.BorderColor = Color.FromArgb(156, 39, 176);
            dmButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            dmButton.Click += DmButton_Click;
            downloadButton = new Button
            {
                Text = "Download",
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12,
                Margin = btnMargin
            };
            downloadButton.FlatAppearance.BorderSize = 2;
            downloadButton.FlatAppearance.BorderColor = Color.FromArgb(0, 188, 212);
            downloadButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            downloadButton.Click += DownloadButton_Click;
            stopButton = new Button
            {
                Text = "Stop",
                Size = btnSize,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Enabled = false,
                Font = yaheiBold12,
                Margin = btnMargin
            };
            stopButton.FlatAppearance.BorderSize = 2;
            stopButton.FlatAppearance.BorderColor = Color.FromArgb(244, 67, 54);
            stopButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            stopButton.Click += StopButton_Click;
            buttonsPanel.Controls.Add(targetButton);
            buttonsPanel.Controls.Add(scrollButton);
            buttonsPanel.Controls.Add(publishButton);
            buttonsPanel.Controls.Add(dmButton);
            buttonsPanel.Controls.Add(downloadButton);
            buttonsPanel.Controls.Add(stopButton);
            // Proxy Status Label (Added)
            lblProxyStatus = new Label
            {
                AutoSize = true,
                Location = new Point(10, 60),
                Text = "Checking Proxy...",
                ForeColor = Color.White,
                Font = yaheiBold12,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            // Logs (Adjusted location to make space for proxy label)
            logTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12,
                Location = new Point(10, 90),
                Size = new Size(bottomPanel.Width - 20, bottomPanel.Height - 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            bottomPanel.Controls.Add(buttonsPanel);
            bottomPanel.Controls.Add(lblProxyStatus);
            bottomPanel.Controls.Add(logTextBox);
            // ajouter le panneau bas APRÈS le webView pour qu'il reste visible
            this.Controls.Add(bottomPanel);
        }
        private async void LoadBrowserAsync()
        {
            try
            {
                var userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SocialNetworkArmy",
                    "Profiles",
                    profile.Name
                );
                Directory.CreateDirectory(userDataDir);

                // Options pour proxy et stealth
                var options = new CoreWebView2EnvironmentOptions();
                options.AdditionalBrowserArguments = "--disable-dev-shm-usage --no-sandbox --disable-gpu-sandbox --disable-web-security";

                // Applique proxy: priorise profile.Proxy, sinon rotation fichier
                if (!string.IsNullOrEmpty(profile.Proxy))
                {
                    proxyService.ApplyProxy(options, profile.Proxy); // Utilise le proxy du profil
                    Logger.LogInfo($"Proxy du profil appliqué: {profile.Proxy}");
                }
                else
                {
                    proxyService.ApplyProxy(options); // Rotation depuis fichier
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDir,
                    options: options
                );

                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Navigate("https://www.instagram.com/");

                await Task.Delay(800);
                webView.Focus();
                webView.BringToFront();

                // Services...
                targetButton.Enabled = true;
                targetService = new TargetService(webView, logTextBox, profile, this);
                scrollService = new ScrollService(webView, logTextBox, profile, this);
                publishService = new PublishService(webView, logTextBox, this);
                dmService = new DirectMessageService(webView, logTextBox, profile, this);
                downloadService = new DownloadInstagramService(webView, logTextBox, profile, this);

                try { await monitoringService.TestFingerprintAsync(webView); } catch { /* ignore */ }

                // Vérif proxy après init
                await UpdateProxyStatusAsync();

                Logger.LogInfo("WebView2 prêt. Services initialisés. Arguments: " + options.AdditionalBrowserArguments);
            }
            catch (Exception ex)
            {
                Logger.LogError("Erreur d'initialisation WebView2 : " + ex);
                logTextBox.AppendText("[INIT] Erreur WebView2 : " + ex.Message + "\r\n");
            }
        }

        // Méthode inchangée, mais avec logs
        private async Task UpdateProxyStatusAsync()
        {
            try
            {
                logTextBox.AppendText("[Proxy] Vérification en cours...\r\n");
                var proxyIp = await proxyService.GetCurrentProxyIpAsync();
                if (!string.IsNullOrEmpty(proxyIp))
                {
                    lblProxyStatus.Text = $"Proxy Active (IP: {proxyIp}) - Protected";
                    lblProxyStatus.ForeColor = Color.Green;
                    logTextBox.AppendText($"[Proxy] OK: {proxyIp}\r\n");
                }
                else
                {
                    lblProxyStatus.Text = "Proxy Inactive - Not Protected";
                    lblProxyStatus.ForeColor = Color.Red;
                    logTextBox.AppendText("[Proxy] Échec: Vérifie proxies.txt ou le proxy du profil.\r\n");
                }
            }
            catch (Exception ex)
            {
                lblProxyStatus.Text = "Proxy Error: " + ex.Message;
                lblProxyStatus.ForeColor = Color.Orange;
                logTextBox.AppendText($"[Proxy] Erreur: {ex.Message}\r\n");
                Logger.LogError("Erreur lors de la vérification du proxy : " + ex);
            }
        }
        // Added method to update proxy status
       
        public async Task StartScriptAsync(string actionName)
        {
            if (isScriptRunning)
            {
                logTextBox.AppendText($"Script {actionName} déjà en cours ! Arrêtez d'abord.\r\n");
                return;
            }
            _cts = new CancellationTokenSource();
            isScriptRunning = true;
            stopButton.Enabled = true;
            targetButton.Enabled = false;
            scrollButton.Enabled = false;
            publishButton.Enabled = false;
            dmButton.Enabled = false;
            logTextBox.AppendText($"Démarrage {actionName}...\r\n");
            if (webView?.CoreWebView2 != null)
                await webView.ExecuteScriptAsync("window.isRunning = true; console.log('Script démarré');");
        }
        public void StopScript()
        {
            if (!isScriptRunning) return;
            logTextBox.AppendText("Arrêt du script en cours...\r\n");
            try
            {
                _cts?.Cancel();
                try { webView?.CoreWebView2?.Stop(); } catch { /* ignore */ }
                ScriptCompleted();
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"Erreur Stop (ignorée) : {ex.Message}\r\n");
                Logger.LogError($"Erreur StopScript : {ex}");
            }
        }
        public CancellationToken GetCancellationToken()
        {
            return _cts?.Token ?? CancellationToken.None;
        }
        public void ScriptCompleted()
        {
            isScriptRunning = false;
            stopButton.Enabled = false;
            targetButton.Enabled = true;
            scrollButton.Enabled = true;
            publishButton.Enabled = true;
            dmButton.Enabled = true;
            logTextBox.AppendText("Script arrêté avec succès.\r\n");
        }
        // ====== HANDLERS ======
        private async void TargetButton_Click(object sender, EventArgs e)
        {
            if (targetService == null)
            {
                logTextBox.AppendText("[INIT] Navigateur en cours d'initialisation… réessaie dans 1–2s.\r\n");
                return;
            }
            await targetService.RunAsync();
        }
        private async void ScrollButton_Click(object sender, EventArgs e)
        {
            if (scrollService == null)
            {
                logTextBox.AppendText("[INIT] Navigateur en cours d'initialisation… réessaie dans 1–2s.\r\n");
                return;
            }
            await scrollService.RunAsync();
        }
        private async void PublishButton_Click(object sender, EventArgs e)
        {
            try
            {
                logTextBox.AppendText("[Publish] Vérification du planning (data/schedule.csv)...\r\n");
                string caption = null;
                bool autoPublish = true;
                await publishService.RunAsync(
                Array.Empty<string>(),
                caption: caption,
                autoPublish: autoPublish,
                token: GetCancellationToken());
                logTextBox.AppendText("[Publish] Script terminé.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText("[Publish] ERREUR: " + ex.Message + "\r\n");
            }
        }
        private async void DmButton_Click(object sender, EventArgs e)
        {
            try
            {
                logTextBox.AppendText("[DM] Vérification des messages et cibles (data/dm_messages.txt et data/dm_targets.txt)...\r\n");
                await dmService.RunAsync();
                logTextBox.AppendText("[DM] Script terminé.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText("[DM] ERREUR: " + ex.Message + "\r\n");
            }
        }
        private async void DownloadButton_Click(object sender, EventArgs e)
        {
            if (downloadService == null)
            {
                logTextBox.AppendText("[INIT] Navigateur en cours d'initialisation… réessaie dans 1–2s.\r\n");
                return;
            }
            try
            {
                logTextBox.AppendText("[Download] Démarrage du téléchargement...\r\n");
                await downloadService.RunAsync();
                logTextBox.AppendText("[Download] Script terminé.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText("[Download] ERREUR: " + ex.Message + "\r\n");
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
                yaheiBold12.Dispose();
                closeTimer?.Dispose();
                _cts?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

