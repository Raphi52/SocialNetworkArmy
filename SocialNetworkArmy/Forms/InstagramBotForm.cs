using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SocialNetworkArmy.Forms
{
    public partial class InstagramBotForm : Form
    {
        private TargetService targetService;
        private ScrollReelsService scrollService;
        private ScrollHomeService scrollHomeService;
        private PublishService publishService;
        private DirectMessageService dmService;
        private DownloadInstagramService downloadService;
        private TestService testService;
        private readonly Profile profile;
        private readonly ProxyService proxyService;
        public string CurrentAccountName => profile?.Name;
        public bool AreServicesReady { get; private set; } = false;
        private readonly CleanupService cleanupService;
        private readonly MonitoringService monitoringService;
        // ✅ NOUVEAU: Fingerprinting pour stealth 10/10 (DM + toutes features)
        private readonly FingerprintService fingerprintService;
        private readonly Models.Fingerprint fingerprint;
        private WebView2 webView;
        private Button targetButton;
        private Button scrollButton;
        private Button scrollHomeButton; 
        private Button publishButton;
        private Button dmButton;
        private Button downloadButton;
        private Button scheduleButton;
        private Button testButton;
        private Button stopButton;
        private TextBox logTextBox;
        private Label lblProxyStatus;
        private bool isScriptRunning = false;
        private Font Sergoe = new Font("Segoe UI", 10f, FontStyle.Bold);
        private Panel bottomPanel;
        private System.Windows.Forms.Timer closeTimer;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _initCts; // ✅ For initialization cancellation
        private Panel toolbarPanel;
        private Button backButton;
        private Button forwardButton;
        private Button refreshButton;
        private TextBox urlTextBox;
        private bool isWebViewReady = false;
        private bool isDisposing = false;
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public InstagramBotForm(Profile profile)
        {
            this.profile = profile;

            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            proxyService = new ProxyService();
            _initCts = new CancellationTokenSource(); // ✅ Initialize cancellation for async init

            // ✅ NOUVEAU: Générer fingerprint desktop unique pour stealth 10/10
            fingerprintService = new FingerprintService();
            fingerprint = fingerprintService.GenerateDesktopFingerprint();

            InitializeComponent();
            if (Environment.OSVersion.Version.Major >= 10)
            {
                int useImmersiveDarkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            }
            this.Icon = new System.Drawing.Icon("Data\\Icons\\Insta.ico");
            targetButton.Enabled = false;
            closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            closeTimer.Tick += (s, e) => { closeTimer.Stop(); this.Close(); };
            this.FormClosing += OnFormClosing;
            LoadBrowserAsync();
        }
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // ✅ Cancel initialization if still running
            try
            {
                _initCts?.Cancel();
            }
            catch { }

            // ✅ Autoriser fermeture système
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                try
                {
                    _cts?.Cancel();
                    isScriptRunning = false;
                }
                catch { }
                return;
            }

            // ✅ Si script en cours ET fermeture manuelle (pas programmée)
            if (isScriptRunning && e.CloseReason == CloseReason.UserClosing)
            {
                StopScript();
                e.Cancel = true; // Annuler la fermeture immédiate
                closeTimer.Interval = 1000; // Attendre 1s
                closeTimer.Start(); // Puis fermer
                return;
            }

            // ✅ Sinon laisser fermer (cas du scheduler avec "close")
        }
        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(15, 15, 15);
            this.ForeColor = Color.White;
            this.Font = Sergoe;
            this.ClientSize = new Size(1200, 800);
            this.MinimumSize = new Size(1000, 700);
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            // Toolbar Panel at top
            toolbarPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.ClientSize.Width, 45),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(28, 28, 28)
            };

            // Back Button
            backButton = new Button
            {
                Text = "←",
                Location = new Point(10, 5),
                Size = new Size(50, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 16f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            backButton.FlatAppearance.BorderSize = 0;
            backButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            backButton.Click += BackButton_Click;

            // Forward Button
            forwardButton = new Button
            {
                Text = "→",
                Location = new Point(65, 5),
                Size = new Size(50, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 16f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            forwardButton.FlatAppearance.BorderSize = 0;
            forwardButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            forwardButton.Click += ForwardButton_Click;

            // Refresh Button
            refreshButton = new Button
            {
                Text = "⟳",
                Location = new Point(120, 5),
                Size = new Size(50, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 18f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, -2, 0, 0)
            };
            refreshButton.FlatAppearance.BorderSize = 0;
            refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            refreshButton.Click += RefreshButton_Click;

            // URL TextBox
            urlTextBox = new TextBox
            {
                Location = new Point(180, 10),
                Size = new Size(this.ClientSize.Width - 190, 28),
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Sergoe,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            urlTextBox.KeyDown += UrlTextBox_KeyDown;

            toolbarPanel.Controls.Add(backButton);
            toolbarPanel.Controls.Add(forwardButton);
            toolbarPanel.Controls.Add(refreshButton);
            toolbarPanel.Controls.Add(urlTextBox);
            this.Controls.Add(toolbarPanel);

            // WebView
            webView = new WebView2();
            webView.DefaultBackgroundColor = Color.Black;
            webView.Location = new Point(0, 45);
            webView.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 45 - 240);
            webView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(webView);

            // Bottom Panel
            bottomPanel = new Panel
            {
                Location = new Point(0, this.ClientSize.Height - 240),
                Size = new Size(this.ClientSize.Width, 240),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent,
                Padding = new Padding(12)
            };

            // Buttons Panel
            var buttonsPanel = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(bottomPanel.Width - 24, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            Size btnSize = new Size(120, 38);
            Padding btnMargin = new Padding(0, 0, 10, 0);

            // Target Button
            targetButton = CreateStyledButton("⬤ Target", btnSize, btnMargin, Color.FromArgb(76, 175, 80));
            targetButton.Click += TargetButton_Click;
            targetButton.Enabled = false;

            // Scroll Button - Symbole film plus net
            scrollButton = CreateStyledButton("🎬 Scroll Reels", new Size(150, 38), btnMargin, Color.FromArgb(33, 150, 243));
            scrollButton.Click += ScrollButton_Click;
            scrollButton.Enabled = false;

            // Scroll Home Button - Symbole maison
            scrollHomeButton = CreateStyledButton("🏠 Scroll Home", new Size(150, 38), btnMargin, Color.FromArgb(233, 30, 99));
            scrollHomeButton.Click += ScrollHomeButton_Click;
            scrollHomeButton.Enabled = false;

            // Publish Button - Flèche vers le haut
            publishButton = CreateStyledButton("📤 Publish", btnSize, btnMargin, Color.FromArgb(255, 152, 0));
            publishButton.Click += PublishButton_Click;
            publishButton.Enabled = false;

            // DM Button - Symbole bulle
            dmButton = CreateStyledButton("✉ Messages", new Size(140, 38), btnMargin, Color.FromArgb(156, 39, 176));
            dmButton.Click += DmButton_Click;
            dmButton.Enabled = false;

            // Download Button - Flèche vers le bas
            downloadButton = CreateStyledButton("📥 Download", new Size(150, 38), btnMargin, Color.FromArgb(0, 188, 212));
            downloadButton.Click += DownloadButton_Click;
            downloadButton.Enabled = false;

            // Test Button
            testButton = CreateStyledButton("◉ Test", btnSize, btnMargin, Color.FromArgb(63, 81, 181));
            testButton.Click += TestButton_Click;
            testButton.Enabled = false;

            // Stop Button - Carré plein
            stopButton = CreateStyledButton("⏹️ Stop", btnSize, btnMargin, Color.FromArgb(244, 67, 54));
            stopButton.Click += StopButton_Click;
            stopButton.Enabled = false;


            buttonsPanel.Controls.Add(targetButton);
            buttonsPanel.Controls.Add(scrollButton);
            buttonsPanel.Controls.Add(scrollHomeButton);
            buttonsPanel.Controls.Add(publishButton);
            buttonsPanel.Controls.Add(dmButton);
            buttonsPanel.Controls.Add(downloadButton);
           // buttonsPanel.Controls.Add(testButton);
            buttonsPanel.Controls.Add(stopButton);

            // Proxy Status Label
            lblProxyStatus = new Label
            {
                AutoSize = true,
                Text = "Checking Proxy...",
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Microsoft YaHei", 8.5f, FontStyle.Regular),
                Location = new Point(12, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                BackColor = Color.Transparent
            };

            // Log Panel with shadow effect
            var logPanel = new Panel
            {
                Location = new Point(12, 95),
                Size = new Size(bottomPanel.Width - 24, bottomPanel.Height - 107),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent
            };

            // Logs TextBox
            logTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                Location = new Point(1, 1),
                Size = new Size(logPanel.Width - 2, logPanel.Height - 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            logPanel.Controls.Add(logTextBox);
            bottomPanel.Controls.Add(buttonsPanel);
            bottomPanel.Controls.Add(lblProxyStatus);
            bottomPanel.Controls.Add(logPanel);
            this.Controls.Add(bottomPanel);
        }

        private Button CreateStyledButton(string text, Size size, Padding margin, Color color)
        {
            var button = new Button
            {
                Text = text,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Margin = margin,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = DarkenColor(color, 20);

            // ✅ Changer la couleur quand désactivé/activé
            button.EnabledChanged += (s, e) => {
                if (!button.Enabled)
                {
                    button.BackColor = Color.FromArgb(40, 40, 40); // Gris foncé
                    button.ForeColor = Color.FromArgb(120, 120, 120); // Texte gris
                }
                else
                {
                    button.BackColor = color; // Couleur d'origine
                    button.ForeColor = Color.White; // Texte blanc
                }
            };

            return button;
        }
        private Color DarkenColor(Color color, int amount)
        {
            return Color.FromArgb(
                color.A,
                Math.Max(0, color.R - amount),
                Math.Max(0, color.G - amount),
                Math.Max(0, color.B - amount)
            );
        }

        private void InstagramBotForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                return;

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Gradient background for entire form
            float centerX = this.ClientRectangle.Width / 2f;
            float centerY = this.ClientRectangle.Height / 2f;
            float angle = 80f;
            float distance = 600f;

            double radians = angle * Math.PI / 180.0;
            PointF point1 = new PointF(
                centerX - (float)(Math.Cos(radians) * distance),
                centerY - (float)(Math.Sin(radians) * distance)
            );
            PointF point2 = new PointF(
                centerX + (float)(Math.Cos(radians) * distance),
                centerY + (float)(Math.Sin(radians) * distance)
            );

            using (LinearGradientBrush brush = new LinearGradientBrush(
                point1,
                point2,
                Color.FromArgb(15, 15, 15),
                Color.FromArgb(15, 15, 15)
            ))
            {
                ColorBlend colorBlend = new ColorBlend();
                colorBlend.Colors = new Color[]
                {
                    Color.FromArgb(0, 0, 0),
                    Color.FromArgb(15, 15, 15),
                    Color.FromArgb(50, 50, 50),
                    Color.FromArgb(15, 15, 15),
                    Color.FromArgb(0, 0, 0)
                };
                colorBlend.Positions = new float[] { 0.0f, 0.30f, 0.5f, 0.70f, 1.0f };
                brush.InterpolationColors = colorBlend;
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            // Draw shadows under panels
            if (toolbarPanel != null)
                DrawControlShadow(e.Graphics, toolbarPanel);

            base.OnPaint(e);
        }

        private void DrawControlShadow(Graphics g, Control control)
        {
            if (control == null) return;

            Rectangle shadowRect = new Rectangle(
                control.Left + 2,
                control.Top + 2,
                control.Width,
                control.Height
            );

            using (GraphicsPath shadowPath = GetRoundedRect(shadowRect, 3))
            {
                using (PathGradientBrush shadowBrush = new PathGradientBrush(shadowPath))
                {
                    shadowBrush.CenterColor = Color.FromArgb(80, 0, 0, 0);
                    shadowBrush.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    shadowBrush.FocusScales = new PointF(0.85f, 0.85f);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(shadowBrush, shadowPath);
                }
            }
        }

        private GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private async void LoadBrowserAsync()
        {
            var token = _initCts.Token;
            try
            {
                // ✅ Check if already cancelled
                if (token.IsCancellationRequested || IsDisposed || isDisposing)
                    return;

                // ✅ 1. CHECK WEBVIEW2 RUNTIME
                string webView2Version = null;
                try
                {
                    webView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[ERROR] Cannot detect WebView2: {ex.Message}\r\n");
                }

                if (string.IsNullOrEmpty(webView2Version))
                {
                    logTextBox.AppendText("[ERROR] WebView2 Runtime not found!\r\n");
                    MessageBox.Show("WebView2 Runtime not detected!\n\nDownload from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }

                if (token.IsCancellationRequested) return;
                logTextBox.AppendText($"[INFO] WebView2 Runtime version: {webView2Version}\r\n");

                // ✅ 2. SETUP DIRECTORIES
                string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                string userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SocialNetworkArmy", "Profiles", profile.Name, "Main"
                );

                logTextBox.AppendText($"[INFO] Creating session folder: {sessionId}\r\n");
                Directory.CreateDirectory(userDataDir);

                // ✅ 3. COPY COOKIES IF AVAILABLE
                var persistentCookiesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SocialNetworkArmy", "Profiles", profile.Name, "Cookies"
                );

                if (Directory.Exists(persistentCookiesDir))
                {
                    try
                    {
                        var defaultDir = Path.Combine(userDataDir, "Default");
                        Directory.CreateDirectory(defaultDir);

                        var cookiesFile = Path.Combine(persistentCookiesDir, "Cookies");
                        if (File.Exists(cookiesFile))
                        {
                            File.Copy(cookiesFile, Path.Combine(defaultDir, "Cookies"), true);
                            logTextBox.AppendText("[INFO] ✓ Cookies loaded\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        logTextBox.AppendText($"[WARN] Could not copy cookies: {ex.Message}\r\n");
                    }
                }

                // ✅ 4. CREATE ENVIRONMENT OPTIONS WITH FINGERPRINT
                var options = new CoreWebView2EnvironmentOptions();

                // ✅ AMÉLIORATION: Utiliser l'UA du fingerprint généré (10/10 stealth)
                var userAgent = fingerprint.UserAgent;
                logTextBox.AppendText($"[INFO] Using fingerprint UA: {userAgent.Substring(0, Math.Min(80, userAgent.Length))}...\r\n");

                // SIMPLIFIED ARGUMENTS (removed aggressive flags that can break loading)
                options.AdditionalBrowserArguments =
                    $"--user-agent=\"{userAgent}\" " +
                    "--disable-blink-features=AutomationControlled " +
                    "--disable-features=IsolateOrigins,site-per-process " + // ✅ FIX: Allow cross-origin
                    "--disable-web-security " + // ✅ FIX: Prevent CORS issues
                    "--no-sandbox " +
                    "--disable-dev-shm-usage";

                // ✅ 5. APPLY PROXY AFTER ENVIRONMENT CREATION (NOT BEFORE)
                string proxyUsed = profile.Proxy;
                bool hasProxy = !string.IsNullOrEmpty(proxyUsed);

                if (hasProxy)
                {
                    logTextBox.AppendText($"[INFO] Proxy configured: {proxyUsed}\r\n");
                    // Don't apply yet - wait for environment creation
                }

                // ✅ 6. CREATE ENVIRONMENT
                CoreWebView2Environment env = null;

                try
                {
                    logTextBox.AppendText("[INFO] Creating WebView2 environment...\r\n");

                    env = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: userDataDir,
                        options: options
                    );

                    logTextBox.AppendText("[INFO] ✓ Environment created\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[ERROR] Environment creation failed: {ex.Message}\r\n");

                    // Try without user data folder (incognito mode)
                    logTextBox.AppendText("[INFO] Trying incognito mode...\r\n");
                    try
                    {
                        env = await CoreWebView2Environment.CreateAsync(
                            browserExecutableFolder: null,
                            userDataFolder: null,
                            options: options
                        );
                        logTextBox.AppendText("[INFO] ✓ Environment created (incognito)\r\n");
                    }
                    catch (Exception ex2)
                    {
                        logTextBox.AppendText($"[FATAL] All attempts failed: {ex2.Message}\r\n");
                        MessageBox.Show($"Failed to create WebView2 environment:\n{ex2.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;
                    }
                }

                if (env == null)
                {
                    logTextBox.AppendText("[ERROR] Environment is null\r\n");
                    this.Close();
                    return;
                }

                // ✅ 7. INITIALIZE WEBVIEW2 CONTROL
                logTextBox.AppendText("[INFO] Initializing WebView2 control...\r\n");

                try
                {
                    await webView.EnsureCoreWebView2Async(env);
                    logTextBox.AppendText("[INFO] ✓ WebView2 control initialized\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[ERROR] Control init failed: {ex.Message}\r\n");
                    MessageBox.Show($"Failed to initialize WebView2:\n{ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }

                // ✅ 8. NOW APPLY PROXY (AFTER CORE WEBVIEW READY)
                if (hasProxy)
                {
                    try
                    {
                        logTextBox.AppendText("[INFO] Applying proxy settings...\r\n");
                        proxyService.ApplyProxy(options, proxyUsed);
                        proxyService.SetupProxyAuthentication(webView.CoreWebView2, proxyUsed);
                        logTextBox.AppendText("[INFO] ✓ Proxy applied\r\n");
                    }
                    catch (Exception ex)
                    {
                        logTextBox.AppendText($"[WARN] Proxy setup failed: {ex.Message}\r\n");
                        var result = MessageBox.Show(
                            "Proxy configuration failed.\n\nContinue without proxy?",
                            "Proxy Error",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                        if (result == DialogResult.No)
                        {
                            this.Close();
                            return;
                        }
                    }
                }

                // ✅ 9. CONFIGURE SETTINGS (BEFORE NAVIGATION)
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.UserAgent = userAgent;

                // ✅ 10. INJECT ADVANCED FINGERPRINT STEALTH (10/10)
                var stealthScript = fingerprintService.GenerateJSSpoof(fingerprint);
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(stealthScript);
                logTextBox.AppendText("[INFO] ✓ Advanced fingerprint stealth injected (10/10)\r\n");

                // ✅ 11. SET EXTRA HEADERS
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Network.setExtraHTTPHeaders",
                    "{\"headers\": {\"Accept-Language\": \"en-US,en;q=0.9\"}}"
                );

                // ✅ 12. SETUP NAVIGATION HANDLERS (BEFORE NAVIGATE)
                TaskCompletionSource<bool> navigationTcs = new TaskCompletionSource<bool>();
                bool navigationHandled = false;

                void NavigationCompletedHandler(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (!navigationHandled)
                    {
                        navigationHandled = true;
                        logTextBox.AppendText($"[NAV] Completed: Success={e.IsSuccess}, WebErrorStatus={e.WebErrorStatus}\r\n");
                        navigationTcs.TrySetResult(e.IsSuccess);
                    }
                }

                webView.CoreWebView2.NavigationCompleted += NavigationCompletedHandler;
                webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted; // For URL bar

                // ✅ 13. START NAVIGATION
                logTextBox.AppendText("[INFO] Navigating to Instagram...\r\n");

                try
                {
                    webView.CoreWebView2.Navigate("https://www.instagram.com/");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[ERROR] Navigate() threw: {ex.Message}\r\n");
                    webView.CoreWebView2.NavigationCompleted -= NavigationCompletedHandler;
                    MessageBox.Show($"Navigation failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.Close();
                    return;
                }

                // ✅ 14. WAIT FOR NAVIGATION (WITH TIMEOUT)
                var timeoutTask = Task.Delay(60000); // 60 seconds (increased from 30)
                var completedTask = await Task.WhenAny(navigationTcs.Task, timeoutTask);

                

                if (completedTask == timeoutTask)
                {
                    logTextBox.AppendText("[ERROR] Navigation timeout after 60s\r\n");

                    var result = MessageBox.Show(
                        "Instagram loading timeout.\n\nPossible causes:\n" +
                        "1. Slow internet connection\n" +
                        "2. Proxy issues\n" +
                        "3. Instagram blocked/down\n\n" +
                        "Continue anyway?",
                        "Timeout",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                    {
                        this.Close();
                        return;
                    }
                }
                else if (!await navigationTcs.Task)
                {
                    logTextBox.AppendText("[ERROR] Navigation failed (IsSuccess=false)\r\n");

                    var result = MessageBox.Show(
                        "Failed to load Instagram.\n\n" +
                        "Possible causes:\n" +
                        "1. Proxy authentication failed\n" +
                        "2. Network blocked Instagram\n" +
                        "3. Instagram rate limit\n\n" +
                        "Continue anyway?",
                        "Navigation Failed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.No)
                    {
                        this.Close();
                        return;
                    }
                }
                else
                {
                    logTextBox.AppendText("[INFO] ✓ Instagram loaded successfully\r\n");
                }

                // ✅ 15. VERIFY PROXY (IF USED)
                if (hasProxy)
                {
                    try
                    {
                        await UpdateProxyStatusAsync(proxyUsed);
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            logTextBox.AppendText($"[Proxy] Verification error: {ex.Message}\r\n");
                            lblProxyStatus.Text = "Proxy Check Failed";
                            lblProxyStatus.ForeColor = Color.Orange;
                        }));
                    }
                }
                else
                {
                    this.Invoke(new Action(() =>
                    {
                        lblProxyStatus.Text = "No Proxy";
                        lblProxyStatus.ForeColor = Color.Gray;
                    }));
                }

                // ✅ 16. SAVE COOKIES ON CLOSE
                this.FormClosing += async (s, e) =>
                {
                    if (!isScriptRunning && webView?.CoreWebView2 != null)
                    {
                        try
                        {
                            var cookiesSource = Path.Combine(userDataDir, "Default", "Cookies");
                            if (File.Exists(cookiesSource))
                            {
                                Directory.CreateDirectory(persistentCookiesDir);
                                File.Copy(cookiesSource, Path.Combine(persistentCookiesDir, "Cookies"), true);
                                logTextBox.AppendText("[INFO] ✓ Cookies saved\r\n");
                            }
                        }
                        catch { }
                    }
                };

                // ✅ 17. INITIALIZE SERVICES
                logTextBox.AppendText("[INFO] Browser ready - initializing services...\r\n");
                await Task.Delay(200, token); // Minimal delay for Instagram DOM to be ready

                if (token.IsCancellationRequested) return;
                InitializeServices();

                webView.Focus();
                logTextBox.AppendText("[INFO] ✓ Setup complete - Bot ready\r\n");
            }
            catch (OperationCanceledException)
            {
                // ✅ Form closed during initialization - this is normal, exit silently
                logTextBox.AppendText("[INFO] Initialization cancelled (form closed)\r\n");
                return;
            }
            catch (TaskCanceledException)
            {
                // ✅ Form closed during initialization - this is normal, exit silently
                logTextBox.AppendText("[INFO] Initialization cancelled (form closed)\r\n");
                return;
            }
            catch (Exception ex) when (IsDisposed || isDisposing)
            {
                // ✅ Form disposed during init - ignore errors
                return;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[STACK] {ex.StackTrace}\r\n");

                MessageBox.Show(
                    $"Critical error:\n{ex.Message}\n\n" +
                    "Try:\n" +
                    "1. Disable proxy temporarily\n" +
                    "2. Check internet connection\n" +
                    "3. Run as Administrator\n" +
                    "4. Disable antivirus",
                    "Fatal Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                this.Close();
            }
        }

        // ✅ MÉTHODE HELPER POUR NETTOYER LE DOSSIER
        private void TryCleanUserDataFolder(string userDataDir)
        {
            try
            {
                logTextBox.AppendText("[INFO] Attempting to clean user data folder...\r\n");

                var lockFiles = new[] {
            "Singleton Lock",
            "lockfile",
            "Singleton Cookie",
            "Singleton Socket",
            "GPUCache"
        };

                foreach (var lockFile in lockFiles)
                {
                    var lockPath = Path.Combine(userDataDir, lockFile);
                    if (File.Exists(lockPath))
                    {
                        try
                        {
                            File.Delete(lockPath);
                            logTextBox.AppendText($"[INFO] Deleted: {lockFile}\r\n");
                        }
                        catch { }
                    }

                    if (Directory.Exists(lockPath))
                    {
                        try
                        {
                            Directory.Delete(lockPath, true);
                            logTextBox.AppendText($"[INFO] Deleted folder: {lockFile}\r\n");
                        }
                        catch { }
                    }
                }

                logTextBox.AppendText("[INFO] Cleanup completed\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[WARN] Cleanup failed: {ex.Message}\r\n");
            }
        }

        // ✅ MÉTHODE HELPER POUR AFFICHER LES ERREURS
        private void ShowWebView2ErrorDialog(string userDataDir, string errorDetail)
        {
            logTextBox.AppendText("[ERROR] Failed to initialize WebView2\r\n");
            logTextBox.AppendText("[FIX] Solutions:\r\n");
            logTextBox.AppendText("1. Reinstall WebView2 Runtime\r\n");
            logTextBox.AppendText("2. Delete profile folder manually\r\n");
            logTextBox.AppendText("3. Run as Administrator\r\n");
            logTextBox.AppendText($"4. Profile path: {userDataDir}\r\n");

            var result = MessageBox.Show(
                $"Failed to initialize WebView2\n\n" +
                $"Error: {errorDetail}\n\n" +
                "Solutions:\n" +
                "1. Click 'Yes' to open WebView2 download page\n" +
                "2. Click 'No' to open profile folder (delete it manually)\n" +
                "3. Click 'Cancel' to close\n\n" +
                $"Profile path:\n{userDataDir}",
                "WebView2 Initialization Error",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes)
            {
                // Ouvrir page de téléchargement WebView2
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    UseShellExecute = true
                });
            }
            else if (result == DialogResult.No)
            {
                // Ouvrir le dossier du profil
                if (Directory.Exists(userDataDir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = userDataDir,
                        UseShellExecute = true
                    });
                }
            }
        }
        private bool IsProfileInUse(string profileDir)
        {
            try
            {
                var lockFile = Path.Combine(profileDir, "Singleton Lock");
                if (!File.Exists(lockFile))
                    return false;

                // Essayer d'ouvrir en mode exclusif
                using (var fs = File.Open(lockFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false; // Fichier accessible = pas en cours d'utilisation
                }
            }
            catch (IOException)
            {
                return true; // Impossible d'accéder = en cours d'utilisation
            }
            catch
            {
                return false; // Autres erreurs = considérer comme disponible
            }
        }
        private async Task CopyCookiesFromMainProfile(string mainDir, string targetDir)
        {
            try
            {
                await Task.Delay(500); // Attendre que le dossier Main soit prêt

                var cookiesFile = Path.Combine(mainDir, "Default", "Cookies");
                var targetCookiesDir = Path.Combine(targetDir, "Default");
                var targetCookiesFile = Path.Combine(targetCookiesDir, "Cookies");

                if (File.Exists(cookiesFile))
                {
                    Directory.CreateDirectory(targetCookiesDir);

                    // Copier avec retry (le fichier peut être verrouillé)
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            File.Copy(cookiesFile, targetCookiesFile, true);
                            logTextBox.AppendText("[INFO] ✓ Cookies copied from Main session\r\n");
                            break;
                        }
                        catch
                        {
                            if (i == 2)
                            {
                                logTextBox.AppendText("[WARN] Could not copy cookies (file locked)\r\n");
                                break;
                            }
                            await Task.Delay(1000);
                        }
                    }

                    // Copier aussi Local Storage
                    var localStorageDir = Path.Combine(mainDir, "Default", "Local Storage");
                    var targetLocalStorageDir = Path.Combine(targetDir, "Default", "Local Storage");

                    if (Directory.Exists(localStorageDir))
                    {
                        CopyDirectory(localStorageDir, targetLocalStorageDir);
                        logTextBox.AppendText("[INFO] ✓ LocalStorage copied from Main session\r\n");
                    }
                }
                else
                {
                    logTextBox.AppendText("[INFO] No cookies to copy (Main session not logged in yet)\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[WARN] Could not copy cookies: {ex.Message}\r\n");
            }
        }

        // ✅ AJOUTER helper pour copier dossier récursivement
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var targetFile = Path.Combine(targetDir, fileName);
                try
                {
                    File.Copy(file, targetFile, true);
                }
                catch { /* Skip locked files */ }
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(targetDir, dirName));
            }
        }

        // ✅ MODIFIER AUSSI UpdateProxyStatusAsync pour utiliser Invoke
        private async Task UpdateProxyStatusAsync(string proxyAddress)
        {
            try
            {
                // ✅ Mise à jour UI avec Invoke
                this.Invoke(new Action(() =>
                {
                    logTextBox.AppendText("[Proxy] Verification starting...\r\n");
                    lblProxyStatus.Text = "Checking Proxy...";
                    lblProxyStatus.ForeColor = Color.Yellow;
                }));

                // Essayer directement HttpClient (plus rapide et fiable)
                var proxyIp = await proxyService.GetCurrentProxyIpAsync(proxyAddress);

                if (!string.IsNullOrEmpty(proxyIp))
                {
                    var (city, country) = await GetLocationAsync(proxyIp);
                    string locationText = FormatLocationShort(city, country);

                    this.Invoke(new Action(() =>
                    {
                        lblProxyStatus.Text = $"Proxy Active ✓ - IP: {proxyIp}{locationText}";
                        lblProxyStatus.ForeColor = Color.Green;
                        logTextBox.AppendText($"[Proxy] ✓ Verified: {proxyIp} - {city}, {country}\r\n");
                    }));
                    return;
                }

                this.Invoke(new Action(() =>
                {
                    lblProxyStatus.Text = "Proxy Failed ✗ - Check credentials/format";
                    lblProxyStatus.ForeColor = Color.Red;
                    logTextBox.AppendText("[Proxy] ✗ Verification FAILED\r\n");
                    logTextBox.AppendText("[Proxy] Check: format, credentials, and proxy is online\r\n");
                }));
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    lblProxyStatus.Text = $"Proxy Error: {ex.Message}";
                    lblProxyStatus.ForeColor = Color.Orange;
                    logTextBox.AppendText($"[Proxy] Exception: {ex.Message}\r\n");
                }));
            }
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (webView != null && webView.Source != null)
            {
                urlTextBox.Text = webView.Source.ToString();
            }
        }
        private void BackButton_Click(object sender, EventArgs e)
        {
            if (webView.CoreWebView2.CanGoBack)
            {
                webView.CoreWebView2.GoBack();
            }
        }
        private void ForwardButton_Click(object sender, EventArgs e)
        {
            if (webView.CoreWebView2.CanGoForward)
            {
                webView.CoreWebView2.GoForward();
            }
        }
        private void RefreshButton_Click(object sender, EventArgs e)
        {
            webView.CoreWebView2.Reload();
        }
        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string url = urlTextBox.Text;
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }
                webView.CoreWebView2.Navigate(url);
                e.SuppressKeyPress = true;
            }
        }
        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                return (await httpClient.GetStringAsync("https://api.ipify.org")).Trim();
            }
            catch
            {
                return null;
            }
        }
        private async Task<(string city, string country)> GetLocationAsync(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                logTextBox.AppendText("[Location] Invalid IP provided\r\n");
                return ("Unknown", "Unknown");
            }
            var services = new[]
            {
new { Url = $"https://ipapi.co/{ip}/json/", Name = "ipapi.co", Parser = (Func<string, (string, string)>)ParseIpApiCo },
new { Url = $"https://ipwhois.app/json/{ip}", Name = "ipwhois.app", Parser = (Func<string, (string, string)>)ParseIpWhois },
new { Url = $"https://freeipapi.com/api/json/{ip}", Name = "freeipapi.com", Parser = (Func<string, (string, string)>)ParseFreeIpApi },
new { Url = $"http://ip-api.com/json/{ip}", Name = "ip-api.com", Parser = (Func<string, (string, string)>)ParseIpApiCom }
};
            foreach (var service in services)
            {
                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    logTextBox.AppendText($"[Location] Trying {service.Name}...\r\n");
                    var response = await httpClient.GetStringAsync(service.Url);
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        logTextBox.AppendText($"[Location] Empty response from {service.Name}\r\n");
                        continue;
                    }
                    var (city, country) = service.Parser(response);
                    if (city != "Unknown" || country != "Unknown")
                    {
                        logTextBox.AppendText($"[Location] ✓ Found via {service.Name}: {city}, {country}\r\n");
                        return (city, country);
                    }
                }
                catch (HttpRequestException ex)
                {
                    logTextBox.AppendText($"[Location] {service.Name} HTTP error: {ex.Message}\r\n");
                }
                catch (TaskCanceledException)
                {
                    logTextBox.AppendText($"[Location] {service.Name} timeout\r\n");
                }
                catch (Exception ex)
                {
                    logTextBox.AppendText($"[Location] {service.Name} error: {ex.Message}\r\n");
                }
            }
            logTextBox.AppendText("[Location] All services failed\r\n");
            return ("Unknown", "Unknown");
        }
        private async void ScrollHomeButton_Click(object sender, EventArgs e)
        {
            if (scrollHomeService == null)
            {
                logTextBox.AppendText("[INIT] Browser initializing... retry in 1-2s.\r\n");
                return;
            }


            await scrollHomeService.RunAsync(GetCancellationToken());
        }
        private (string city, string country) ParseIpApiCo(string json)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                if (data?.error != null && (bool)data.error == true)
                {
                    logTextBox.AppendText($"[Location] ipapi.co error: {data.reason}\r\n");
                    return ("Unknown", "Unknown");
                }
                string city = data?.city?.ToString() ?? "Unknown";
                string country = data?.country_name?.ToString() ?? "Unknown";
                return (city, country);
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Location] Parse error (ipapi.co): {ex.Message}\r\n");
                return ("Unknown", "Unknown");
            }
        }
        private (string city, string country) ParseIpWhois(string json)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                if (data?.success != null && (bool)data.success == false)
                {
                    return ("Unknown", "Unknown");
                }
                string city = data?.city?.ToString() ?? "Unknown";
                string country = data?.country?.ToString() ?? "Unknown";
                return (city, country);
            }
            catch
            {
                return ("Unknown", "Unknown");
            }
        }
        private (string city, string country) ParseFreeIpApi(string json)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                string city = data?.cityName?.ToString() ?? "Unknown";
                string country = data?.countryName?.ToString() ?? "Unknown";
                return (city, country);
            }
            catch
            {
                return ("Unknown", "Unknown");
            }
        }
        private (string city, string country) ParseIpApiCom(string json)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                if (data?.status != null && data.status.ToString() == "fail")
                {
                    return ("Unknown", "Unknown");
                }
                string city = data?.city?.ToString() ?? "Unknown";
                string country = data?.country?.ToString() ?? "Unknown";
                return (city, country);
            }
            catch
            {
                return ("Unknown", "Unknown");
            }
        }
        private string FormatLocationShort(string city, string country)
        {
            if (city != "Unknown" && country != "Unknown")
            {
                return $" ({city}, {country})";
            }
            else if (city != "Unknown")
            {
                return $" ({city})";
            }
            else if (country != "Unknown")
            {
                return $" ({country})";
            }
            return "";
        }
       
        public async Task StartScriptAsync(string actionName)
        {
            if (isScriptRunning)
            {
                logTextBox.AppendText($"Script {actionName} already running! Stop it first.\r\n");
                return;
            }
            _cts = new CancellationTokenSource();
            isScriptRunning = true;
            stopButton.Enabled = true;
            targetButton.Enabled = false;
            scrollButton.Enabled = false;
            scrollHomeButton.Enabled = false;
            publishButton.Enabled = false;
            dmButton.Enabled = false;
            downloadButton.Enabled = false;
            //testButton.Enabled = false;
            logTextBox.AppendText($"Starting {actionName}...\r\n");
            if (webView?.CoreWebView2 != null)
                await webView.ExecuteScriptAsync("window.isRunning = true; console.log('Script started');");
        }
        public void StopScript()
        {
            if (!isScriptRunning) return;
            logTextBox.AppendText("Stopping script...\r\n");
            try
            {
                _cts?.Cancel();
                try { webView?.CoreWebView2?.Stop(); } catch { /* ignore */ }
                ScriptCompleted();
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"Stop error (ignored): {ex.Message}\r\n");
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
            scrollHomeButton.Enabled = true;
            publishButton.Enabled = true;
            dmButton.Enabled = true;
            downloadButton.Enabled = true;
            //testButton.Enabled = true;
            logTextBox.AppendText("Script stopped successfully.\r\n");
        }
        private async void TargetButton_Click(object sender, EventArgs e)
        {
            if (targetService == null)
            {
                logTextBox.AppendText("[INIT] Browser initializing... retry in 1-2s.\r\n");
                return;
            }
            await targetService.RunAsync();
        }
        private async void ScrollButton_Click(object sender, EventArgs e)
        {
            if (scrollService == null)
            {
                logTextBox.AppendText("[INIT] Browser initializing... retry in 1-2s.\r\n");
                return;
            }
            await scrollService.RunAsync();
        }
        private async void PublishButton_Click(object sender, EventArgs e)
        {
            try
            {
                logTextBox.AppendText("[Publish] Checking schedule (data/schedule.csv)...\r\n");
                string caption = null;
                bool autoPublish = true;
                await publishService.RunAsync(
                Array.Empty<string>(),
                caption: caption,
                autoPublish: autoPublish,
                token: GetCancellationToken());
                logTextBox.AppendText("[Publish] Script completed.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Publish] ERROR: {ex.Message}\r\n");
            }
        }
        private async void DmButton_Click(object sender, EventArgs e)
        {
            try
            {
                logTextBox.AppendText("[DM] Checking messages and targets (data/dm_messages.txt and data/dm_targets.txt)...\r\n");
                await dmService.RunAsync();
                logTextBox.AppendText("[DM] Script completed.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[DM] ERROR: {ex.Message}\r\n");
            }
        }
        private void InitializeServices()
        {
            try
            {
                logTextBox.AppendText("[INFO] Initializing services...\r\n");

                // ✅ VÉRIFIER QUE WEBVIEW2 EST VRAIMENT PRÊT
                if (webView == null || webView.IsDisposed || webView.CoreWebView2 == null)
                {
                    logTextBox.AppendText("[ERROR] WebView2 not ready - cannot initialize services\r\n");
                    AreServicesReady = false;
                    return;
                }

                // ✅ PROTÉGER contre l'initialisation multiple
                if (AreServicesReady)
                {
                    logTextBox.AppendText("[WARN] Services already initialized\r\n");
                    return;
                }

                // Créer les services
                targetService = new TargetService(webView, logTextBox, profile, this);
                scrollService = new ScrollReelsService(webView, logTextBox, profile, this);
                scrollHomeService = new ScrollHomeService(webView, logTextBox, profile, this);
                publishService = new PublishService(webView, logTextBox, this);
                dmService = new DirectMessageService(webView, logTextBox, profile, this);
                downloadService = new DownloadInstagramService(webView, logTextBox, profile, this);
                testService = new TestService(webView, logTextBox, profile, this);

                // ✅ MARQUER COMME PRÊT SEULEMENT SI TOUT EST OK
                AreServicesReady = true;

                // Activer les boutons sur le thread UI
                Action enableButtons = () =>
                {
                    targetButton.Enabled = true;
                    scrollButton.Enabled = true;
                    scrollHomeButton.Enabled = true;
                    publishButton.Enabled = true;
                    dmButton.Enabled = true;
                    downloadButton.Enabled = true;
                    // testButton.Enabled = true; // ✅ Décommenter si vous gardez testButton
                };

                if (this.InvokeRequired)
                    this.Invoke(enableButtons);
                else
                    enableButtons();

                logTextBox.AppendText("[INFO] ✓ All services initialized and ready\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ERROR] Services initialization failed: {ex.Message}\r\n");
                logTextBox.AppendText($"[ERROR] Stack: {ex.StackTrace}\r\n");
                AreServicesReady = false;
            }
        }
       
        private async void DownloadButton_Click(object sender, EventArgs e)
        {
            if (downloadService == null)
            {
                logTextBox.AppendText("[INIT] Browser initializing... retry in 1-2s.\r\n");
                return;
            }
            try
            {
                logTextBox.AppendText("[Download] Starting download...\r\n");
                await downloadService.RunAsync();
                logTextBox.AppendText("[Download] Script completed.\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Download] ERROR: {ex.Message}\r\n");
            }
        }

        private async void TestButton_Click(object sender, EventArgs e)
        {
            if (testService == null)
            {
                logTextBox.AppendText("[INIT] Browser initializing... retry in 1-2s.\r\n");
                return;
            }
            await testService.RunAsync();
        }
        private void StopButton_Click(object sender, EventArgs e)
        {
            StopScript();
        }
        public void ForceClose()
        {
            try
            {
                // Arrêter le script sans attendre
                if (isScriptRunning)
                {
                    _cts?.Cancel();
                    isScriptRunning = false;
                }

                // Fermer immédiatement
                this.FormClosing -= OnFormClosing; // Désactiver temporairement l'event
                this.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForceClose error: {ex.Message}");
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (isDisposing) return;
            isDisposing = true;

            if (disposing)
            {
                try
                {
                    isWebViewReady = false;

                    if (webView != null && !webView.IsDisposed)
                    {
                      

                        try
                        {
                            webView.Dispose();
                        }
                        catch { }
                        webView = null;
                    }

                    // Cleanup services
                    targetService = null;
                    scrollService = null;
                    scrollHomeService = null;
                    publishService = null;
                    dmService = null;
                    downloadService = null;
                    testService = null;

                    // ✅ Dispose cancellation tokens
                    _initCts?.Dispose();
                    _cts?.Dispose();

                    Sergoe?.Dispose();
                    closeTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
                }
            }

            base.Dispose(disposing);
        }
    }
}