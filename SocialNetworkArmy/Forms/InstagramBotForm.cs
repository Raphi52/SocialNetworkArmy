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
        private ScrollService scrollService;
        private PublishService publishService;

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

            // Fenêtre plus grande et cohérente
            this.ClientSize = new Size(1200, 800);
            this.MinimumSize = new Size(1000, 700);
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            // ===== WebView en plein écran (ajouté AVANT le panneau bas pour la bonne z-order) =====
            webView = new WebView2 { Dock = DockStyle.Fill };
            webView.DefaultBackgroundColor = Color.Black;
            this.Controls.Add(webView);

            // ===== Panneau bas (plus haut) =====
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 240, // <-- plus grand qu'avant (150)
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(8)
            };

            // Bandeau des boutons en haut du panneau
            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0),
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
                Enabled = false // activé après init WebView
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
            buttonsPanel.Controls.Add(stopButton);

            // Zone de logs qui prend tout le reste du panneau
            logTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,           // <-- remplace le positionnement absolu
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12
            };

            bottomPanel.Controls.Add(logTextBox);
            bottomPanel.Controls.Add(buttonsPanel);

            // IMPORTANT : ajouter le panneau bas APRÈS le webView pour qu'il reste visible
            this.Controls.Add(bottomPanel);
        }


        private async void LoadBrowserAsync()
        {
            try
            {
                // One folder per profile = one persistent session per profile
                var userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SocialNetworkArmy",
                    "Profiles",            // (optional subfolder, keeps things tidy)
                    profile.Name           // <-- key: different folder for each profile
                );
                Directory.CreateDirectory(userDataDir);

                // Create environment bound to that folder
                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDir,
                    options: null
                );

                // Initialize WebView2 with this environment
                await webView.EnsureCoreWebView2Async(env);

                // Settings + first navigation
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Navigate("https://www.instagram.com/");

                await Task.Delay(800);
                webView.Focus();
                webView.BringToFront();

                // Initialize services after CoreWebView2 is ready
                targetButton.Enabled = true;
                targetService = new TargetService(webView, logTextBox, profile, this);
                scrollService = new ScrollService(webView, logTextBox, profile, this);
                publishService = new PublishService(webView, logTextBox, this);

                try { await monitoringService.TestFingerprintAsync(webView); } catch { /* ignore */ }

                Logger.LogInfo("WebView2 prêt. TargetService et ScrollService initialisés.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erreur d'initialisation WebView2 : " + ex);
                logTextBox.AppendText("[INIT] Erreur WebView2 : " + ex.Message + "\r\n");
            }
        }

        public async Task StartScriptAsync(string actionName)
        {
            _cts = new CancellationTokenSource();
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

            logTextBox.AppendText("Arrêt du script en cours...\r\n");

            try
            {
                _cts?.Cancel();
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"Erreur Stop (ignorée) : {ex.Message}\r\n");
                Logger.LogError($"Erreur StopScript : {ex}");
            }
        }
        private CancellationTokenSource _cts;

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
            logTextBox.AppendText("Script arrêté avec succès.\r\n");
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
            if (scrollService == null)
            {
                logTextBox.AppendText("[INIT] Navigateur en cours d'initialisation… réessaie dans 1–2s.\r\n");
                return;
            }
            await scrollService.RunAsync();
        }

        // ========================= PUBLISH =========================
        private async void PublishButton_Click(object sender, EventArgs e)
        {
            try
            {
                logTextBox.AppendText("[Publish] Vérification du planning (data/schedule.csv)...\r\n");

                // Pas de sélection de fichier ici — on laisse le PublishService
                // charger automatiquement le fichier "data/schedule.csv"
                // et décider quoi publier aujourd’hui selon le profil courant.

                string caption = null; // la description sera lue depuis le CSV si applicable
                bool autoPublish = true; // on peut l’activer par défaut

                // On lance directement la publication
                await publishService.RunAsync(
                    Array.Empty<string>(),  // on passe un tableau vide : la classe se charge de charger depuis le CSV
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