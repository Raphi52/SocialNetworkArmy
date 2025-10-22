using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Forms
{
    public partial class TiktokBotForm : Form
    {
        private TargetService targetService;
        private ScrollService scrollService;
        private PublishService publishService;
        private DirectMessageService dmService;
        
        private readonly Profile profile;
        private readonly ProxyService proxyService;
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
        private Label lblProxyStatus;

        private bool isScriptRunning = false;
        private Font yaheiBold12 = new Font("Microsoft YaHei", 10f, FontStyle.Bold);
        private System.Windows.Forms.Timer closeTimer;
        private CancellationTokenSource _cts;

        private Panel toolbarPanel;
        private Button backButton;
        private Button forwardButton;
        private Button refreshButton;
        private TextBox urlTextBox;

        public TiktokBotForm(Profile profile)
        {
            this.profile = profile;
            cleanupService = new CleanupService();
            monitoringService = new MonitoringService();
            proxyService = new ProxyService();

            InitializeComponent();
            this.Icon = new System.Drawing.Icon("Data\\Icons\\TikTok.ico");

            targetButton.Enabled = false;
            closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            closeTimer.Tick += (s, e) => { closeTimer.Stop(); this.Close(); };
            this.FormClosing += OnFormClosing;

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
            this.ClientSize = new Size(1200, 800);
            this.MinimumSize = new Size(1000, 700);
            this.Text = profile.Name + " - TikTok";
            this.StartPosition = FormStartPosition.CenterScreen;

            // Toolbar Panel
            toolbarPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.ClientSize.Width, 39),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(45, 45, 45)
            };

            // Back Button
            backButton = new Button
            {
                Text = "Back",
                Location = new Point(10, 2),
                Size = new Size(80, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12
            };
            backButton.FlatAppearance.BorderSize = 2;
            backButton.FlatAppearance.BorderColor = Color.FromArgb(254, 44, 85); // TikTok red
            backButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            backButton.Click += BackButton_Click;

            // Forward Button
            forwardButton = new Button
            {
                Text = "Forward",
                Location = new Point(100, 2),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12
            };
            forwardButton.FlatAppearance.BorderSize = 2;
            forwardButton.FlatAppearance.BorderColor = Color.FromArgb(254, 44, 85);
            forwardButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            forwardButton.Click += ForwardButton_Click;

            // Refresh Button
            refreshButton = new Button
            {
                Text = "Refresh",
                Location = new Point(210, 2),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold12
            };
            refreshButton.FlatAppearance.BorderSize = 2;
            refreshButton.FlatAppearance.BorderColor = Color.FromArgb(254, 44, 85);
            refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            refreshButton.Click += RefreshButton_Click;

            // URL TextBox
            urlTextBox = new TextBox
            {
                Location = new Point(320, 7),
                Size = new Size(this.ClientSize.Width - 330, 30),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12,
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
            webView.Location = new Point(0, 40);
            webView.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 40 - 220);
            webView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(webView);

            // Bottom Panel
            var bottomPanel = new Panel
            {
                Location = new Point(0, this.ClientSize.Height - 220),
                Size = new Size(this.ClientSize.Width, 220),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(0)
            };

            // Buttons Panel
            var buttonsPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(bottomPanel.Width - 20, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            Size btnSize = new Size(135, 36);
            Padding btnMargin = new Padding(0, 0, 8, 0);

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
            scrollButton.FlatAppearance.BorderColor = Color.FromArgb(254, 44, 85);
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
                Text = "Send Messages",
                Size = new Size(155, 36),
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

            // Proxy Status Label
            lblProxyStatus = new Label
            {
                AutoSize = true,
                Text = "Checking Proxy...",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 10f, FontStyle.Regular),
                Margin = new Padding(15, 7, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            buttonsPanel.Controls.Add(lblProxyStatus);

            // Logs
            logTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = yaheiBold12,
                Location = new Point(10, 60),
                Size = new Size(bottomPanel.Width - 20, bottomPanel.Height - 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            bottomPanel.Controls.Add(buttonsPanel);
            bottomPanel.Controls.Add(logTextBox);
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
                    profile.Name + "_TikTok"
                );
                Directory.CreateDirectory(userDataDir);

                var options = new CoreWebView2EnvironmentOptions();

                // User-Agent réaliste pour TikTok
                string[] realUserAgents = new[]
                {
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
                    "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                };
                var userAgent = realUserAgents[new Random().Next(realUserAgents.Length)];

                options.AdditionalBrowserArguments =
                    $"--user-agent=\"{userAgent}\" " +
                    "--disable-blink-features=AutomationControlled " +
                    "--disable-dev-tools " +
                    "--disable-features=IsolateOrigins,site-per-process " +
                    "--disable-site-isolation-trials " +
                    "--disable-web-security " +
                    "--disable-features=CrossSiteDocumentBlockingIfIsolating " +
                    "--allow-running-insecure-content " +
                    "--disable-client-side-phishing-detection " +
                    "--disable-popup-blocking " +
                    "--disable-notifications " +
                    "--disable-save-password-bubble " +
                    "--disable-translate " +
                    "--disable-sync " +
                    "--disable-background-networking " +
                    "--disable-default-apps " +
                    "--no-first-run " +
                    "--no-default-browser-check " +
                    "--disable-dev-shm-usage " +
                    "--cipher-suite-blacklist=0x0001,0x0002 " +
                    "--no-sandbox " +
                    "--disable-gpu " +
                    "--autoplay-policy=no-user-gesture-required";

                string proxyUsed = profile.Proxy;
                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    logTextBox.AppendText($"[INFO] Applying proxy: {proxyUsed}\r\n");
                    proxyService.ApplyProxy(options, proxyUsed);
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDir,
                    options: options
                );

                await webView.EnsureCoreWebView2Async(env);

                // TikTok specific device ID
                string ttDeviceId = Guid.NewGuid().ToString("N");
                string ttWebId = Guid.NewGuid().ToString("N");

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($@"
localStorage.setItem('tt_webid', '{ttWebId}');
localStorage.setItem('tt_webid_v2', '{ttWebId}');
localStorage.setItem('device_id', '{ttDeviceId}');
localStorage.setItem('tt_csrf_token', '{Guid.NewGuid().ToString("N")}');
");

                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Network.setExtraHTTPHeaders",
                    "{\"headers\": {\"Accept-Language\": \"en-US,en;q=0.9\", \"Accept\": \"text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8\"}}"
                );

                // Script stealth AVANT tout chargement
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetTikTokStealthScript());

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                // Proxy auth
                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    proxyService.SetupProxyAuthentication(webView.CoreWebView2, proxyUsed);
                }

                // Naviguer vers TikTok
                webView.CoreWebView2.Navigate("https://www.tiktok.com/");
                webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;

                await Task.Delay(2000);
                webView.Focus();

                logTextBox.AppendText("[INFO] ✓ TikTok stealth mode active (enhanced)\r\n");

                // Vérifier le proxy après chargement
                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    _ = UpdateProxyStatusAsync(proxyUsed);
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ERROR] Browser init failed: {ex.Message}\r\n");
                MessageBox.Show($"Failed to load browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetTikTokStealthScript()
        {
            // Générer résolution aléatoire
            var resolutions = new[] {
                new { w = 1920, h = 1080 },
                new { w = 1366, h = 768 },
                new { w = 1536, h = 864 },
                new { w = 1440, h = 900 },
                new { w = 2560, h = 1440 }
            };
            var res = resolutions[new Random().Next(resolutions.Length)];

            return $@"
(function() {{
    'use strict';
    
    // ========== 1. WEBDRIVER PROTECTION (RENFORCÉ) ==========
    Object.defineProperty(navigator, 'webdriver', {{
        get: () => undefined,
        configurable: true
    }});
    delete navigator.__proto__.webdriver;
    
    // Protection contre les détections par propriétés
    const originalNavigator = navigator;
    delete Object.getPrototypeOf(navigator).webdriver;
    
    // ========== 2. CHROME OBJECT (ULTRA-COMPLET) ==========
    window.chrome = {{
        runtime: {{
            connect: function() {{ 
                return {{ 
                    onMessage: {{ addListener: () => {{}}, removeListener: () => {{}} }}, 
                    postMessage: () => {{}},
                    disconnect: () => {{}}
                }}; 
            }},
            sendMessage: function(extensionId, message, options, callback) {{
                if (callback) callback();
            }},
            onMessage: {{
                addListener: function() {{}},
                removeListener: function() {{}},
                hasListener: function() {{ return false; }}
            }},
            id: undefined,
            onConnect: {{
                addListener: function() {{}},
                removeListener: function() {{}}
            }},
            getManifest: function() {{ return undefined; }},
            getURL: function(path) {{ return 'chrome-extension://invalid/' + path; }}
        }},
        loadTimes: function() {{
            return {{
                commitLoadTime: performance.timing.domContentLoadedEventStart / 1000,
                connectionInfo: 'http/1.1',
                finishDocumentLoadTime: performance.timing.domContentLoadedEventEnd / 1000,
                finishLoadTime: performance.timing.loadEventEnd / 1000,
                firstPaintAfterLoadTime: 0,
                firstPaintTime: performance.timing.domLoading / 1000,
                navigationType: 'Other',
                npnNegotiatedProtocol: 'h2',
                requestTime: performance.timing.fetchStart / 1000,
                startLoadTime: performance.timing.fetchStart / 1000,
                wasAlternateProtocolAvailable: false,
                wasFetchedViaSpdy: true,
                wasNpnNegotiated: true
            }};
        }},
        csi: function() {{
            return {{
                onloadT: Date.now(),
                pageT: performance.now(),
                startE: performance.timing.navigationStart,
                tran: 15
            }};
        }},
        app: {{
            isInstalled: false,
            InstallState: {{
                DISABLED: 'disabled',
                INSTALLED: 'installed',
                NOT_INSTALLED: 'not_installed'
            }},
            RunningState: {{
                CANNOT_RUN: 'cannot_run',
                READY_TO_RUN: 'ready_to_run',
                RUNNING: 'running'
            }}
        }}
    }};

    Object.defineProperty(window, 'chrome', {{
        value: window.chrome,
        writable: true,
        configurable: true
    }});
    
    // ========== 3. PLUGINS (DÉTAILLÉS) ==========
    Object.defineProperty(navigator, 'plugins', {{
        get: () => [
            {{
                0: {{type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format'}},
                description: 'Portable Document Format',
                filename: 'internal-pdf-viewer',
                length: 1,
                name: 'Chrome PDF Plugin'
            }},
            {{
                0: {{type: 'application/pdf', suffixes: 'pdf', description: ''}},
                description: '',
                filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai',
                length: 1,
                name: 'Chrome PDF Viewer'
            }},
            {{
                0: {{type: 'application/x-nacl', suffixes: '', description: 'Native Client Executable'}},
                1: {{type: 'application/x-pnacl', suffixes: '', description: 'Portable Native Client Executable'}},
                description: '',
                filename: 'internal-nacl-plugin',
                length: 2,
                name: 'Native Client'
            }}
        ],
        configurable: true
    }});
    
    // ========== 4. LANGUAGES ==========
    Object.defineProperty(navigator, 'languages', {{
        get: () => ['en-US', 'en'],
        configurable: true
    }});
    
    Object.defineProperty(navigator, 'language', {{
        get: () => 'en-US',
        configurable: true
    }});
    
    // ========== 5. PERMISSIONS (TIKTOK OPTIMISÉ) ==========
    const originalQuery = window.navigator.permissions.query;
    window.navigator.permissions.query = (parameters) => {{
        const fakePermissions = {{
            'notifications': 'prompt',
            'geolocation': 'prompt',
            'camera': 'prompt',
            'microphone': 'prompt',
            'midi': 'prompt',
            'clipboard-read': 'prompt',
            'clipboard-write': 'prompt',
            'persistent-storage': 'prompt'
        }};
        return Promise.resolve({{
            state: fakePermissions[parameters.name] || 'prompt',
            onchange: null
        }});
    }};

    Object.defineProperty(Notification, 'permission', {{
        get: () => 'default',
        configurable: true
    }});
    
    // ========== 6. CONNECTION (4G RAPIDE) ==========
    Object.defineProperty(navigator, 'connection', {{
        get: () => ({{
            effectiveType: '4g',
            rtt: 50,
            downlink: 10,
            saveData: false,
            onchange: null,
            downlinkMax: Infinity,
            type: 'wifi'
        }}),
        configurable: true
    }});
    
    // ========== 7. HARDWARE ==========
    Object.defineProperty(navigator, 'hardwareConcurrency', {{
        get: () => 8,
        configurable: true
    }});
    
    Object.defineProperty(navigator, 'deviceMemory', {{
        get: () => 8,
        configurable: true
    }});
    
    Object.defineProperty(navigator, 'platform', {{
        get: () => 'Win32',
        configurable: true
    }});
    
    // ========== 8. DO NOT TRACK ==========
    Object.defineProperty(navigator, 'doNotTrack', {{
        get: () => null,
        configurable: true
    }});
    
    // ========== 9. CANVAS FINGERPRINT NOISE (AMÉLIORÉ) ==========
    const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
    const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
    
    const noisify = function(imageData) {{
        const data = imageData.data;
        for (let i = 0; i < data.length; i += 4) {{
            const noise = Math.random() > 0.5 ? 1 : -1;
            data[i] = data[i] + noise * Math.floor(Math.random() * 3);
            data[i + 1] = data[i + 1] + noise * Math.floor(Math.random() * 3);
            data[i + 2] = data[i + 2] + noise * Math.floor(Math.random() * 3);
        }}
        return imageData;
    }};
    
    CanvasRenderingContext2D.prototype.getImageData = function() {{
        const imageData = originalGetImageData.apply(this, arguments);
        return noisify(imageData);
    }};
    
    HTMLCanvasElement.prototype.toDataURL = function() {{
        const context = this.getContext('2d');
        if (context) {{
            const imageData = context.getImageData(0, 0, this.width, this.height);
            noisify(imageData);
            context.putImageData(imageData, 0, 0);
        }}
        return originalToDataURL.apply(this, arguments);
    }};
    
    // ========== 10. WEBGL FINGERPRINT (ULTRA-RANDOMISÉ) ==========
    const getParameterProxyHandler = {{
        apply: function(target, thisArg, args) {{
            const param = args[0];
            const result = Reflect.apply(target, thisArg, args);
            
            if (param === 37445) {{ // UNMASKED_VENDOR_WEBGL
                const vendors = ['Intel Inc.', 'NVIDIA Corporation', 'ATI Technologies Inc.', 'Qualcomm'];
                return vendors[Math.floor(Math.random() * vendors.length)];
            }}
            if (param === 37446) {{ // UNMASKED_RENDERER_WEBGL
                const renderers = [
                    'Intel(R) UHD Graphics 630',
                    'NVIDIA GeForce GTX 1660 Ti',
                    'AMD Radeon RX 580 Series',
                    'ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)',
                    'ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0)',
                    'ANGLE (AMD, AMD Radeon(TM) Graphics Direct3D11 vs_5_0 ps_5_0)'
                ];
                return renderers[Math.floor(Math.random() * renderers.length)];
            }}
            
            return result;
        }}
    }};
    
    const contextProxyHandler = {{
        get: function(target, prop) {{
            if (prop === 'getParameter') {{
                return new Proxy(target[prop], getParameterProxyHandler);
            }}
            return target[prop];
        }}
    }};
    
    const originalGetContext = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function() {{
        const context = originalGetContext.apply(this, arguments);
        if (arguments[0] === 'webgl' || arguments[0] === 'webgl2' || arguments[0] === 'experimental-webgl') {{
            return new Proxy(context, contextProxyHandler);
        }}
        return context;
    }};
    
    // ========== 11. BATTERY STATUS ==========
    if (navigator.getBattery) {{
        const originalGetBattery = navigator.getBattery;
        navigator.getBattery = function() {{
            return originalGetBattery().then(battery => {{
                Object.defineProperties(battery, {{
                    charging: {{ get: () => true }},
                    chargingTime: {{ get: () => 0 }},
                    dischargingTime: {{ get: () => Infinity }},
                    level: {{ get: () => 1 }}
                }});
                return battery;
            }});
        }};
    }}
    
    // ========== 12. SCREEN (RANDOMISÉ) ==========
    Object.defineProperties(screen, {{
        availWidth: {{ get: () => {res.w} }},
        availHeight: {{ get: () => {res.h - 40} }},
        width: {{ get: () => {res.w} }},
        height: {{ get: () => {res.h} }},
        colorDepth: {{ get: () => 24 }},
        pixelDepth: {{ get: () => 24 }},
        orientation: {{
            get: () => ({{
                type: 'landscape-primary',
                angle: 0
            }})
        }}
    }});
    
    // ========== 13. TIMEZONE (NATIF) ==========
    // Garder le timezone natif du système
    
    // ========== 14. MEDIA DEVICES (TIKTOK SPÉCIFIQUE) ==========
    if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {{
        const originalEnumerateDevices = navigator.mediaDevices.enumerateDevices;
        navigator.mediaDevices.enumerateDevices = function() {{
            return originalEnumerateDevices().then(devices => {{
                return [
                    {{
                        deviceId: 'default',
                        kind: 'audioinput',
                        label: 'Default - Microphone (Realtek High Definition Audio)',
                        groupId: 'default'
                    }},
                    {{
                        deviceId: 'communications',
                        kind: 'audioinput',
                        label: 'Communications - Microphone (Realtek High Definition Audio)',
                        groupId: 'communications'
                    }},
                    {{
                        deviceId: 'default',
                        kind: 'audiooutput',
                        label: 'Default - Speakers (Realtek High Definition Audio)',
                        groupId: 'default'
                    }},
                    {{
                        deviceId: 'communications',
                        kind: 'audiooutput',
                        label: 'Communications - Speakers (Realtek High Definition Audio)',
                        groupId: 'communications'
                    }},
                    {{
                        deviceId: '{Guid.NewGuid().ToString()}',
                        kind: 'videoinput',
                        label: 'Integrated Camera (04f2:b6dd)',
                        groupId: '{Guid.NewGuid().ToString()}'
                    }}
                ];
            }});
        }};
    }}
    
    // ========== 15. POINTER EVENTS (SOURIS NATURELLE) ==========
    Object.defineProperty(navigator, 'maxTouchPoints', {{
        get: () => 0,
        configurable: true
    }});
    
    // ========== 16. VENDOR & PRODUCT ==========
    Object.defineProperty(navigator, 'vendor', {{
        get: () => 'Google Inc.',
        configurable: true
    }});
    
    Object.defineProperty(navigator, 'productSub', {{
        get: () => '20030107',
        configurable: true
    }});
    
    Object.defineProperty(navigator, 'vendorSub', {{
        get: () => '',
        configurable: true
    }});
    
    // ========== 17. AUDIO CONTEXT (FINGERPRINT PROTECTION) ==========
    const audioContext = window.AudioContext || window.webkitAudioContext;
    if (audioContext) {{
        const originalCreateOscillator = audioContext.prototype.createOscillator;
        audioContext.prototype.createOscillator = function() {{
            const oscillator = originalCreateOscillator.apply(this, arguments);
            const originalStart = oscillator.start;
            oscillator.start = function() {{
                // Ajouter du bruit minimal
                const noise = Math.random() * 0.0001;
                if (oscillator.frequency) {{
                    oscillator.frequency.value = oscillator.frequency.value + noise;
                }}
                return originalStart.apply(this, arguments);
            }};
            return oscillator;
        }};
    }}
    
    // ========== 18. SPEECH SYNTHESIS ==========
    if (window.speechSynthesis) {{
        Object.defineProperty(window.speechSynthesis, 'getVoices', {{
            value: function() {{
                return [
                    {{ name: 'Microsoft David Desktop - English (United States)', lang: 'en-US', default: true, localService: true, voiceURI: 'Microsoft David Desktop - English (United States)' }},
                    {{ name: 'Microsoft Zira Desktop - English (United States)', lang: 'en-US', default: false, localService: true, voiceURI: 'Microsoft Zira Desktop - English (United States)' }}
                ];
            }}
        }});
    }}
    
    // ========== 19. HEADLESS DETECTION BYPASS ==========
    Object.defineProperty(navigator, 'headless', {{
        get: () => false
    }});
    
    // ========== 20. AUTOMATION FLAGS CLEANUP ==========
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_JSON;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Object;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Proxy;
    
    // ========== 21. TIKTOK SPECIFIC - RANDOMIZE DEVICE INFO ==========
    const ttDeviceId = localStorage.getItem('device_id') || '{Guid.NewGuid().ToString("N")}';
    const ttWebId = localStorage.getItem('tt_webid') || '{Guid.NewGuid().ToString("N")}';
    
    // Override Object.getOwnPropertyDescriptor to hide automation
    const originalGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
    Object.getOwnPropertyDescriptor = function(obj, prop) {{
        if (prop === 'webdriver') {{
            return undefined;
        }}
        return originalGetOwnPropertyDescriptor(obj, prop);
    }};
    
    // ========== 22. FUNCTION toString PROTECTION ==========
    const originalFunctionToString = Function.prototype.toString;
    Function.prototype.toString = function() {{
        if (this === originalGetImageData || 
            this === originalToDataURL || 
            this === originalGetContext ||
            this === originalQuery) {{
            return originalFunctionToString.call(Function.prototype.toString);
        }}
        return originalFunctionToString.call(this);
    }};
    
    // ========== 23. IFRAME DETECTION BYPASS ==========
    Object.defineProperty(window, 'top', {{
        get: function() {{
            return window;
        }}
    }});
    
    Object.defineProperty(window, 'frameElement', {{
        get: function() {{
            return null;
        }}
    }});
    
    // ========== 24. TIKTOK TOUCH EVENTS (MOBILE-LIKE BEHAVIOR) ==========
    const isTouchDevice = 'ontouchstart' in window;
    if (!isTouchDevice) {{
        window.ontouchstart = null;
        document.ontouchstart = null;
    }}
    
    // ========== 25. PERFORMANCE TIMING (REALISTIC) ==========
    if (window.performance && window.performance.timing) {{
        const originalTiming = window.performance.timing;
        Object.defineProperty(window.performance, 'timing', {{
            get: function() {{
                const timing = {{}};
                for (let key in originalTiming) {{
                    if (typeof originalTiming[key] === 'number') {{
                        timing[key] = originalTiming[key] + Math.floor(Math.random() * 10);
                    }} else {{
                        timing[key] = originalTiming[key];
                    }}
                }}
                return timing;
            }}
        }});
    }}
    
    // ========== 26. USER ACTIVATION (SIMULATE REAL USER) ==========
    if (navigator.userActivation) {{
        Object.defineProperty(navigator.userActivation, 'hasBeenActive', {{
            get: () => true
        }});
        Object.defineProperty(navigator.userActivation, 'isActive', {{
            get: () => true
        }});
    }}
    
    // ========== 27. MOUSE MOVEMENT SIMULATION ==========
    let lastMouseMove = Date.now();
    document.addEventListener('mousemove', () => {{
        lastMouseMove = Date.now();
    }}, true);
    
    // ========== 28. CLIPBOARD API ==========
    if (navigator.clipboard) {{
        const originalReadText = navigator.clipboard.readText;
        navigator.clipboard.readText = function() {{
            return Promise.reject(new DOMException('Access denied', 'NotAllowedError'));
        }};
    }}
    
    // ========== 29. WEB WORKERS ==========
    const originalWorker = window.Worker;
    window.Worker = class extends originalWorker {{
        constructor(...args) {{
            super(...args);
        }}
    }};
    
    // ========== 30. CONSOLE LOG PROTECTION ==========
    const originalConsoleLog = console.log;
    console.log = function(...args) {{
        // Filter out automation detection logs
        const message = args.join(' ');
        if (message.includes('webdriver') || 
            message.includes('automation') || 
            message.includes('headless')) {{
            return;
        }}
        return originalConsoleLog.apply(console, args);
    }};
    
    console.log('🛡️ TikTok ultra-stealth mode active (30 layers)');
}})();
";
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

        private async Task UpdateProxyStatusAsync(string proxyAddress)
        {
            try
            {
                logTextBox.AppendText("[Proxy] Verification starting...\r\n");
                lblProxyStatus.Text = "Checking Proxy...";
                lblProxyStatus.ForeColor = Color.Yellow;

                await Task.Delay(10000);

                logTextBox.AppendText("[Proxy] Trying WebView2 verification...\r\n");
                var proxyIp = await proxyService.GetWebView2ProxyIpAsync(webView.CoreWebView2);

                if (!string.IsNullOrEmpty(proxyIp))
                {
                    logTextBox.AppendText($"[Proxy] ✓ WebView2 detected IP: {proxyIp}\r\n");
                    logTextBox.AppendText("[Proxy] Getting location...\r\n");
                    var (city, country) = await GetLocationAsync(proxyIp);
                    string locationText = FormatLocationShort(city, country);
                    lblProxyStatus.Text = $"Proxy Active ✓ - IP: {proxyIp}{locationText}";
                    lblProxyStatus.ForeColor = Color.Green;
                    logTextBox.AppendText($"[Proxy] ✓ Verified: {proxyIp} - {city}, {country}\r\n");
                    return;
                }

                logTextBox.AppendText("[Proxy] WebView2 check failed, trying HttpClient fallback...\r\n");
                var fallbackIp = await proxyService.GetCurrentProxyIpAsync(proxyAddress);

                if (!string.IsNullOrEmpty(fallbackIp))
                {
                    logTextBox.AppendText($"[Proxy] ✓ HttpClient detected IP: {fallbackIp}\r\n");
                    logTextBox.AppendText("[Proxy] Getting location...\r\n");
                    var (city, country) = await GetLocationAsync(fallbackIp);
                    string locationText = FormatLocationShort(city, country);
                    lblProxyStatus.Text = $"Proxy Active ✓ - IP: {fallbackIp}{locationText}";
                    lblProxyStatus.ForeColor = Color.Green;
                    logTextBox.AppendText($"[Proxy] ✓ Verified (Fallback): {fallbackIp} - {city}, {country}\r\n");
                    return;
                }

                lblProxyStatus.Text = "Proxy Failed ✗ - Check credentials/format";
                lblProxyStatus.ForeColor = Color.Red;
                logTextBox.AppendText("[Proxy] ✗ Verification FAILED\r\n");
                logTextBox.AppendText("[Proxy] Check: format, credentials, and proxy is online\r\n");
            }
            catch (Exception ex)
            {
                lblProxyStatus.Text = $"Proxy Error: {ex.Message}";
                lblProxyStatus.ForeColor = Color.Orange;
                logTextBox.AppendText($"[Proxy] Exception: {ex.Message}\r\n");
            }
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
            publishButton.Enabled = false;
            dmButton.Enabled = false;
            downloadButton.Enabled = false;

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
            publishButton.Enabled = true;
            dmButton.Enabled = true;
            downloadButton.Enabled = true;

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