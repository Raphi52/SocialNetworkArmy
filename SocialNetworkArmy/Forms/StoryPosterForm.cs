using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Forms
{
    public partial class StoryPosterForm : Form
    {
        private WebView2 webView;
        private readonly Profile profile;
        private TextBox logTextBox;
        private readonly ProxyService proxyService;
        private readonly Random random = new Random();
        private readonly string userDataFolder;
        private Button postStoryButton;
        private bool isWebViewReady = false;

        // Dimensions Samsung Galaxy S23 (mode portrait)
        private const int DEVICE_WIDTH = 360;
        private const int DEVICE_HEIGHT = 780;
        private const double DEVICE_PIXEL_RATIO = 3.0;

        // ✅ SCRIPT STEALTH FINAL - TOUTES LES CORRECTIONS
        // REMPLACER LA CONSTANTE STEALTH_SCRIPT dans StoryPosterForm.cs

        private const string STEALTH_SCRIPT = @"
(function() {
    'use strict';
    
    // ========== 1. WEBDRIVER (RENFORCÉ) ==========
    Object.defineProperty(navigator, 'webdriver', {
        get: () => undefined,
        configurable: true
    });
    delete navigator.__proto__.webdriver;
    
    const originalNavigator = navigator;
    delete Object.getPrototypeOf(navigator).webdriver;

    // ========== 2. AUTOMATION FLAGS CLEANUP (ÉTENDU) ==========
    const automationProps = [
        '__webdriver_evaluate', '__selenium_evaluate', '__webdriver_script_function',
        '__driver_evaluate', '__webdriver_unwrapped', '__fxdriver_unwrapped',
        '__webdriver_script_fn', '__selenium_unwrapped', '__driver_unwrapped',
        'cdc_adoQpoasnfa76pfcZLmcfl_Array', 'cdc_adoQpoasnfa76pfcZLmcfl_Promise',
        'cdc_adoQpoasnfa76pfcZLmcfl_Symbol', '$cdc_asdjflasutopfhvcZLmcfl_',
        '$chrome_asyncScriptInfo', '__$webdriverAsyncExecutor', '_Selenium_IDE_Recorder',
        'callSelenium', '_selenium', '__nightmare', 'domAutomation', 
        'domAutomationController', '__fxdriver_unwrapped', '__webdriver_script_func'
    ];

    automationProps.forEach(prop => {
        try { delete window[prop]; delete document[prop]; delete navigator[prop]; } catch(e) {}
    });

    // ========== 3. CHROME RUNTIME (MASQUÉ) ==========
    if (window.chrome && window.chrome.runtime) {
        delete window.chrome.runtime;
        Object.defineProperty(window.chrome, 'runtime', {
            get: () => undefined,
            configurable: true
        });
    }

    // ========== 4. PERMISSIONS API ==========
    const originalQuery = window.navigator.permissions.query;
    window.navigator.permissions.query = (parameters) => {
        const fakePermissions = {
            'notifications': 'prompt',
            'geolocation': 'prompt',
            'camera': 'prompt',
            'microphone': 'prompt',
            'persistent-storage': 'prompt'
        };
        return Promise.resolve({
            state: fakePermissions[parameters.name] || 'prompt',
            onchange: null
        });
    };

    Object.defineProperty(Notification, 'permission', {
        get: () => 'default',
        configurable: true
    });

    // ========== 5. LANGUAGES ==========
    Object.defineProperty(navigator, 'languages', {
        get: () => ['en-US', 'en', 'fr-FR', 'fr'],
        configurable: true
    });

    Object.defineProperty(navigator, 'language', {
        get: () => 'en-US',
        configurable: true
    });

    // ========== 6. PLUGINS (MOBILE RÉALISTE) ==========
    Object.defineProperty(navigator, 'plugins', {
        get: () => {
            const plugins = [
                { name: 'PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' },
                { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' },
                { name: 'Chromium PDF Viewer', filename: 'internal-pdf-viewer', description: 'Portable Document Format' }
            ];
            plugins.item = (index) => plugins[index] || null;
            plugins.namedItem = (name) => plugins.find(p => p.name === name) || null;
            plugins.refresh = () => {};
            plugins[Symbol.iterator] = function*() { for (let p of plugins) yield p; };
            return Object.setPrototypeOf(plugins, PluginArray.prototype);
        },
        configurable: true
    });

    // ========== 7. SCREEN DIMENSIONS MOBILE ==========
    const w = __WIDTH__;
    const h = __HEIGHT__;
    const dpr = __DPR__;

    Object.defineProperty(window, 'devicePixelRatio', {
        get: () => dpr,
        configurable: true
    });

    Object.defineProperty(window.screen, 'width', {
        get: () => w,
        configurable: true
    });

    Object.defineProperty(window.screen, 'height', {
        get: () => h,
        configurable: true
    });

    Object.defineProperty(window.screen, 'availWidth', {
        get: () => w,
        configurable: true
    });

    Object.defineProperty(window.screen, 'availHeight', {
        get: () => h - 24,
        configurable: true
    });
    
    Object.defineProperty(screen, 'colorDepth', {
        get: () => 24,
        configurable: true
    });
    
    Object.defineProperty(screen, 'pixelDepth', {
        get: () => 24,
        configurable: true
    });

    // ========== 8. TOUCH SUPPORT ==========
    Object.defineProperty(navigator, 'maxTouchPoints', {
        get: () => 5,
        configurable: true
    });
    
    // Ajouter touch events
    if (!('ontouchstart' in window)) {
        window.ontouchstart = null;
        document.ontouchstart = null;
    }

    // ========== 9. SCREEN ORIENTATION (MOBILE) ==========
    Object.defineProperty(window.screen, 'orientation', {
        get: () => ({
            type: 'portrait-primary',
            angle: 0,
            lock: () => Promise.resolve(),
            unlock: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => true,
            onchange: null
        }),
        configurable: true
    });

    // ========== 10. VIEWPORT META ==========
    const setupViewport = () => {
        if (document.head) {
            let meta = document.querySelector('meta[name=viewport]');
            if (!meta) {
                meta = document.createElement('meta');
                meta.name = 'viewport';
                document.head.appendChild(meta);
            }
            meta.content = 'width=device-width, initial-scale=1.0, maximum-scale=5.0, user-scalable=yes';
            return true;
        }
        return false;
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', setupViewport);
    } else {
        setupViewport();
    }

    // ========== 11. CONNECTION API (4G MOBILE) ==========
    Object.defineProperty(navigator, 'connection', {
        get: () => ({
            effectiveType: '4g',
            downlink: 10,
            rtt: 50,
            saveData: false,
            type: 'cellular',
            downlinkMax: Infinity,
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => true,
            onchange: null
        }),
        configurable: true
    });

    // ========== 12. USER AGENT DATA (FIXÉ POUR MOBILE) ==========
    if (navigator.userAgentData) {
        Object.defineProperty(navigator.userAgentData, 'brands', {
            get: () => [
                { brand: 'Not A(Brand', version: '8' },
                { brand: 'Chromium', version: '130' },
                { brand: 'Google Chrome', version: '130' }
            ],
            configurable: true
        });
        
        Object.defineProperty(navigator.userAgentData, 'mobile', {
            get: () => true,
            configurable: true
        });
        
        Object.defineProperty(navigator.userAgentData, 'platform', {
            get: () => 'Android',
            configurable: true
        });
    }
    
    // ========== 13. PLATFORM ==========
    Object.defineProperty(navigator, 'platform', {
        get: () => 'Linux armv81',
        configurable: true
    });
    
    Object.defineProperty(navigator, 'vendor', {
        get: () => 'Google Inc.',
        configurable: true
    });
    
    Object.defineProperty(navigator, 'productSub', {
        get: () => '20030107',
        configurable: true
    });
    
    Object.defineProperty(navigator, 'vendorSub', {
        get: () => '',
        configurable: true
    });

    // ========== 14. CANVAS FINGERPRINT NOISE (AMÉLIORÉ) ==========
    const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
    const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
    
    const noisify = function(imageData) {
        const data = imageData.data;
        for (let i = 0; i < data.length; i += 4) {
            const noise = Math.random() > 0.5 ? 1 : -1;
            data[i] = data[i] + noise * Math.floor(Math.random() * 3);
            data[i + 1] = data[i + 1] + noise * Math.floor(Math.random() * 3);
            data[i + 2] = data[i + 2] + noise * Math.floor(Math.random() * 3);
        }
        return imageData;
    };
    
    CanvasRenderingContext2D.prototype.getImageData = function() {
        const imageData = originalGetImageData.apply(this, arguments);
        return noisify(imageData);
    };
    
    HTMLCanvasElement.prototype.toDataURL = function(type) {
        if (this.width === 0 || this.height === 0) return originalToDataURL.apply(this, arguments);
        
        const context = this.getContext('2d');
        if (context) {
            const imageData = context.getImageData(0, 0, this.width, this.height);
            noisify(imageData);
            context.putImageData(imageData, 0, 0);
        }
        return originalToDataURL.apply(this, arguments);
    };

    // ========== 15. WEBGL FINGERPRINT (MOBILE GPU) ==========
    const getParameterProxyHandler = {
        apply: function(target, thisArg, args) {
            const param = args[0];
            const result = Reflect.apply(target, thisArg, args);
            
            if (param === 37445) { // UNMASKED_VENDOR_WEBGL
                return 'Qualcomm';
            }
            if (param === 37446) { // UNMASKED_RENDERER_WEBGL
                return 'Adreno (TM) 740';
            }
            
            return result;
        }
    };
    
    const contextProxyHandler = {
        get: function(target, prop) {
            if (prop === 'getParameter') {
                return new Proxy(target[prop], getParameterProxyHandler);
            }
            return target[prop];
        }
    };
    
    const originalGetContext = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function() {
        const context = originalGetContext.apply(this, arguments);
        if (arguments[0] === 'webgl' || arguments[0] === 'webgl2' || arguments[0] === 'experimental-webgl') {
            return new Proxy(context, contextProxyHandler);
        }
        return context;
    };

    // ========== 16. BATTERY STATUS (MOBILE) ==========
    if (navigator.getBattery) {
        const originalGetBattery = navigator.getBattery;
        navigator.getBattery = function() {
            return originalGetBattery().then(battery => {
                Object.defineProperties(battery, {
                    charging: { get: () => false },
                    chargingTime: { get: () => Infinity },
                    dischargingTime: { get: () => 7200 + Math.random() * 3600 },
                    level: { get: () => 0.65 + Math.random() * 0.3 }
                });
                return battery;
            });
        };
    }
    
    // ========== 17. MEDIA DEVICES (MOBILE) ==========
    if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
        const originalEnumerateDevices = navigator.mediaDevices.enumerateDevices;
        navigator.mediaDevices.enumerateDevices = function() {
            return Promise.resolve([
                {
                    deviceId: 'default',
                    kind: 'audioinput',
                    label: 'Microphone',
                    groupId: 'default'
                },
                {
                    deviceId: 'default',
                    kind: 'audiooutput',
                    label: 'Speaker',
                    groupId: 'default'
                },
                {
                    deviceId: 'front_camera',
                    kind: 'videoinput',
                    label: 'Front Camera',
                    groupId: 'camera_group'
                },
                {
                    deviceId: 'back_camera',
                    kind: 'videoinput',
                    label: 'Back Camera',
                    groupId: 'camera_group'
                }
            ]);
        };
    }
    
    // ========== 18. HEADLESS DETECTION ==========
    Object.defineProperty(navigator, 'headless', {
        get: () => false
    });
    
    // ========== 19. OBJECT.GETOWNPROPERTYDESCRIPTOR OVERRIDE ==========
    const originalGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
    Object.getOwnPropertyDescriptor = function(obj, prop) {
        if (prop === 'webdriver') {
            return undefined;
        }
        return originalGetOwnPropertyDescriptor(obj, prop);
    };
    
    // ========== 20. FUNCTION TOSTRING PROTECTION ==========
    const originalFunctionToString = Function.prototype.toString;
    Function.prototype.toString = function() {
        if (this === originalGetImageData || 
            this === originalToDataURL || 
            this === originalGetContext ||
            this === originalQuery) {
            return originalFunctionToString.call(Function.prototype.toString);
        }
        return originalFunctionToString.call(this);
    };
    
    // ========== 21. IFRAME DETECTION BYPASS ==========
    Object.defineProperty(window, 'top', {
        get: function() {
            return window;
        }
    });
    
    Object.defineProperty(window, 'frameElement', {
        get: function() {
            return null;
        }
    });
    
    // ========== 22. PERFORMANCE TIMING ==========
    if (window.performance && window.performance.timing) {
        const originalTiming = window.performance.timing;
        Object.defineProperty(window.performance, 'timing', {
            get: function() {
                const timing = {};
                for (let key in originalTiming) {
                    if (typeof originalTiming[key] === 'number') {
                        timing[key] = originalTiming[key] + Math.floor(Math.random() * 10);
                    } else {
                        timing[key] = originalTiming[key];
                    }
                }
                return timing;
            }
        });
    }
    
    // ========== 23. USER ACTIVATION ==========
    if (navigator.userActivation) {
        Object.defineProperty(navigator.userActivation, 'hasBeenActive', {
            get: () => true
        });
        Object.defineProperty(navigator.userActivation, 'isActive', {
            get: () => true
        });
    }
    
    // ========== 24. HARDWARE CONCURRENCY (MOBILE) ==========
    Object.defineProperty(navigator, 'hardwareConcurrency', {
        get: () => 8,
        configurable: true
    });
    
    Object.defineProperty(navigator, 'deviceMemory', {
        get: () => 8,
        configurable: true
    });
    
    // ========== 25. DO NOT TRACK ==========
    Object.defineProperty(navigator, 'doNotTrack', {
        get: () => null,
        configurable: true
    });
    
    // ========== 26. CONSOLE LOG PROTECTION ==========
    const originalConsoleLog = console.log;
    console.log = function(...args) {
        const message = args.join(' ');
        if (message.includes('webdriver') || 
            message.includes('automation') || 
            message.includes('headless')) {
            return;
        }
        return originalConsoleLog.apply(console, args);
    };

    console.log('🛡️ Instagram Story ultra-stealth mode (26 layers - MOBILE)');
})();
";

        public StoryPosterForm(Profile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SocialNetworkArmy", "Profiles", profile.Name);

            proxyService = new ProxyService();
            InitializeComponent();
            this.Icon = new Icon("Data\\Icons\\Insta.ico");

            _ = LoadBrowserAsync();
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.ClientSize = new Size(DEVICE_WIDTH + 20, DEVICE_HEIGHT + 120);
            this.Text = $"Story - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(DEVICE_WIDTH + 20, 700);

            webView = new WebView2
            {
                Location = new Point(10, 10),
                Size = new Size(DEVICE_WIDTH, DEVICE_HEIGHT),
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            logTextBox = new TextBox
            {
                Location = new Point(10, DEVICE_HEIGHT + 20),
                Size = new Size(DEVICE_WIDTH - 130, 100),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            postStoryButton = new Button
            {
                Text = "Post Story",
                Location = new Point(DEVICE_WIDTH - 110, DEVICE_HEIGHT + 20),
                Size = new Size(120, 100),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold),
                Enabled = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            postStoryButton.FlatAppearance.BorderSize = 2;
            postStoryButton.FlatAppearance.BorderColor = Color.FromArgb(156, 39, 176);
            postStoryButton.Click += PostStoryButton_Click;

            this.Controls.Add(webView);
            this.Controls.Add(logTextBox);
            this.Controls.Add(postStoryButton);
        }

        private async Task LoadBrowserAsync()
        {
            try
            {
                logTextBox.AppendText("[INFO] Initializing browser...\r\n");
                Directory.CreateDirectory(userDataFolder);

                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--disable-blink-features=AutomationControlled " +
                        "--disable-features=IsolateOrigins,site-per-process " +
                        "--disable-site-isolation-trials " +
                        "--disable-dev-shm-usage " +
                        "--no-first-run " +
                        "--no-default-browser-check " +
                        "--autoplay-policy=no-user-gesture-required " +
                        $"--window-size={DEVICE_WIDTH},{DEVICE_HEIGHT} " +
                        // ✅ User-Agent Chrome Mobile STANDARD (pas Edge)
                        "--user-agent=\"Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36\""
                };

                if (!string.IsNullOrWhiteSpace(profile.Proxy))
                {
                    logTextBox.AppendText($"[Proxy] {profile.Proxy}\r\n");
                    proxyService.ApplyProxy(options, profile.Proxy);
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder,
                    options: options
                );

                await webView.EnsureCoreWebView2Async(env);
                logTextBox.AppendText("[OK] WebView2 ready\r\n");

                // ✅ BLOQUER LE FILE DIALOG AU NIVEAU DU BROWSER (AVANT TOUTE PAGE)
                var blockDialogScript = @"
(function() {
    'use strict';
    
    // Override au niveau du prototype AVANT que la page charge
    const originalClick = HTMLInputElement.prototype.click;
    HTMLInputElement.prototype.click = function() {
        if (this.type === 'file') {
            console.log('⚠️ File dialog blocked by prototype override');
            return;
        }
        return originalClick.call(this);
    };
    
    // Bloquer showOpenFilePicker
    if (window.showOpenFilePicker) {
        window.showOpenFilePicker = () => Promise.reject(new Error('Blocked'));
    }
    
    console.log('✅ File dialog blocker active');
})();";

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(blockDialogScript);
                logTextBox.AppendText("[OK] File dialog blocker injected\r\n");

                var script = STEALTH_SCRIPT
                    .Replace("__WIDTH__", DEVICE_WIDTH.ToString(CultureInfo.InvariantCulture))
                    .Replace("__HEIGHT__", DEVICE_HEIGHT.ToString(CultureInfo.InvariantCulture))
                    .Replace("__DPR__", DEVICE_PIXEL_RATIO.ToString(CultureInfo.InvariantCulture));

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                logTextBox.AppendText("[OK] Stealth script injected\r\n");

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                if (!string.IsNullOrEmpty(profile.Proxy))
                {
                    proxyService.SetupProxyAuthentication(webView.CoreWebView2, profile.Proxy);
                }
                else
                {
                    webView.CoreWebView2.BasicAuthenticationRequested += (s, e) => e.Cancel = true;
                }

                webView.CoreWebView2.NavigationCompleted += async (sender, args) =>
                {
                    logTextBox.AppendText("[OK] Page loaded\r\n");
                    await LogEnvironmentAsync();

                    // ✅ CRITIQUE : Warm-up avant d'activer le bouton
                    logTextBox.AppendText("[Warmup] Simulating human activity...\r\n");
                    await Task.Delay(random.Next(5000, 10000)); // 5-10 secondes de "lecture"

                    // Simuler un scroll léger
                    await webView.CoreWebView2.ExecuteScriptAsync(@"
                        window.scrollBy({
                            top: Math.random() * 300,
                            behavior: 'smooth'
                        });
                    ");

                    await Task.Delay(random.Next(2000, 4000));
                    logTextBox.AppendText("[Warmup] Ready\r\n");

                    isWebViewReady = true;
                    if (postStoryButton.InvokeRequired)
                        postStoryButton.Invoke(new Action(() => postStoryButton.Enabled = true));
                    else
                        postStoryButton.Enabled = true;
                };

                webView.CoreWebView2.Navigate("https://www.instagram.com/");
                logTextBox.AppendText("[INFO] Loading Instagram...\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ERROR] {ex.Message}\r\n");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LogEnvironmentAsync()
        {
            try
            {
                string envLog = await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            return JSON.stringify({
                                ua: navigator.userAgent,
                                platform: navigator.platform,
                                vendor: navigator.vendor,
                                webdriver: navigator.webdriver,
                                chromeRuntime: typeof window.chrome?.runtime,
                                maxTouch: navigator.maxTouchPoints,
                                dpr: window.devicePixelRatio,
                                screen: window.screen.width + 'x' + window.screen.height,
                                inner: window.innerWidth + 'x' + window.innerHeight
                            });
                        } catch(e) {
                            return JSON.stringify({ error: e.message });
                        }
                    })();
                ");
                if (!string.IsNullOrWhiteSpace(envLog))
                    logTextBox.AppendText($"[ENV] {envLog.Trim('\"')}\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ENV] Error: {ex.Message}\r\n");
            }
        }

        private async void PostStoryButton_Click(object sender, EventArgs e)
        {
            await PostTodayStoryAsync();
        }

        public async Task<bool> PostTodayStoryAsync()
        {
            if (!isWebViewReady)
            {
                logTextBox.AppendText("[Story] ✗ Not ready\r\n");
                return false;
            }

            // ✅ CRITIQUE : Vérifier qu'on n'a pas posté récemment
            string lastPostFile = Path.Combine(userDataFolder, "last_story_post.txt");
            if (File.Exists(lastPostFile))
            {
                string lastPostStr = File.ReadAllText(lastPostFile);
                if (DateTime.TryParse(lastPostStr, out DateTime lastPost))
                {
                    TimeSpan elapsed = DateTime.Now - lastPost;
                    if (elapsed.TotalMinutes < 15) // Minimum 15 minutes entre les posts
                    {
                        logTextBox.AppendText($"[Story] ✗ Too soon (last post: {elapsed.TotalMinutes:F1}min ago)\r\n");
                        return false;
                    }
                }
            }

            logTextBox.AppendText("[Story] Checking schedule...\r\n");
            string mediaPath = GetTodayStoryMediaPath();

            if (string.IsNullOrEmpty(mediaPath))
            {
                logTextBox.AppendText("[Story] ✗ No story for today\r\n");
                return false;
            }

            logTextBox.AppendText($"[Story] Media: {Path.GetFileName(mediaPath)}\r\n");
            bool success = await UploadStoryAsync(mediaPath);

            if (success)
            {
                logTextBox.AppendText("[Story] ✓ Success!\r\n");
                // Enregistrer l'heure du post
                File.WriteAllText(lastPostFile, DateTime.Now.ToString("o"));
            }
            else
            {
                logTextBox.AppendText("[Story] ✗ Failed\r\n");
            }

            return success;
        }

        private string GetTodayStoryMediaPath()
        {
            string csvPath = Path.Combine("Data", "schedule.csv");
            if (!File.Exists(csvPath))
            {
                logTextBox.AppendText($"[Schedule] ✗ CSV not found\r\n");
                return null;
            }

            try
            {
                DateTime now = DateTime.Now;
                string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                string[] lines = File.ReadAllLines(csvPath);

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = line.Split(',').Select(c => c.Trim()).ToArray();
                    if (columns.Length < 5) continue;

                    string dateTime = columns[0];
                    string platform = columns[1];
                    string account = columns[2];
                    string activity = columns[3];
                    string media = columns[4];

                    string dateOnly = dateTime.Split(' ')[0];

                    if (dateOnly.Equals(today, StringComparison.OrdinalIgnoreCase) &&
                        platform.Equals(profile.Platform, StringComparison.OrdinalIgnoreCase) &&
                        account.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) &&
                        activity.Equals("story", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(media) &&
                        File.Exists(media))
                    {
                        logTextBox.AppendText($"[Schedule] ✓ Match: {Path.GetFileName(media)}\r\n");
                        return media;
                    }
                }

                logTextBox.AppendText("[Schedule] ✗ No match\r\n");
                return null;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Schedule] ✗ Error: {ex.Message}\r\n");
                return null;
            }
        }

        public async Task<bool> UploadStoryAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                logTextBox.AppendText($"[Upload] ✗ File not found\r\n");
                return false;
            }

            try
            {
                string fileName = Path.GetFileName(filePath);
                logTextBox.AppendText($"[Upload] Processing: {fileName}...\r\n");

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                if (fileBytes.Length > 50 * 1024 * 1024)
                {
                    logTextBox.AppendText("[Upload] ✗ File too large (max 50MB)\r\n");
                    return false;
                }

                string base64 = Convert.ToBase64String(fileBytes);
                string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
                string mimeType = (extension == ".mp4" || extension == ".mov") ? "video/mp4"
                                : (extension == ".png") ? "image/png"
                                : "image/jpeg";

                logTextBox.AppendText($"[Upload] Type: {mimeType}, Size: {fileBytes.Length / 1024}KB\r\n");

                // ✅ Délai humain VARIABLE (pas toujours pareil)
                int initialDelay = random.Next(2000, 6000); // 2-6 secondes
                logTextBox.AppendText($"[Upload] Waiting {initialDelay}ms...\r\n");
                await Task.Delay(initialDelay);

                // ✅ Simuler une activité aléatoire (50% de chance)
                if (random.Next(0, 2) == 0)
                {
                    logTextBox.AppendText("[Upload] Simulating scroll...\r\n");
                    await webView.CoreWebView2.ExecuteScriptAsync(@"
                        window.scrollBy({
                            top: Math.random() * 100 - 50,
                            behavior: 'smooth'
                        });
                    ");
                    await Task.Delay(random.Next(500, 1500));
                }

                logTextBox.AppendText("[Upload] Clicking Create...\r\n");

                var createResult = await webView.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    try {
        const allSvgs = Array.from(document.querySelectorAll('svg'));
        const plusSvg = allSvgs.find(svg => {
            const path = svg.querySelector('path[d*=""M2 12v3.45""]');
            const line1 = svg.querySelector('line[x1=""6.545""]');
            const line2 = svg.querySelector('line[x1=""12.003""]');
            return path && line1 && line2;
        });
        
        if (plusSvg) {
            const link = plusSvg.closest('a');
            if (link) {
                link.click();
                return 'CLICKED_VIA_PLUS_SVG';
            }
        }

        const btn = document.querySelector('a[href*=""/create""]') ||
                   document.querySelector('svg[aria-label*=""Create""]')?.closest('a') ||
                   document.querySelector('svg[aria-label*=""Créer""]')?.closest('a');
        
        if (btn) {
            btn.click();
            return 'CLICKED_FALLBACK';
        }
        
        return 'NOT_FOUND';
    } catch(e) {
        return 'ERROR:' + e.message;
    }
})();");

                logTextBox.AppendText($"[Upload] Create: {createResult?.Trim('\"')}\r\n");

                if (!createResult?.Contains("CLICKED") == true)
                {
                    logTextBox.AppendText("[Upload] ✗ Create button not found\r\n");
                    return false;
                }

                await Task.Delay(random.Next(2000, 3500));

                logTextBox.AppendText("[Upload] Selecting Story tab...\r\n");

                var storyTabResult = await webView.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    try {
        const storySvg = document.querySelector('svg[aria-label=""Story""]');
        if (storySvg) {
            const button = storySvg.closest('div[role=""button""]');
            if (button) {
                button.click();
                return 'STORY_CLICKED_VIA_SVG';
            }
        }

        const tabs = Array.from(document.querySelectorAll('div[role=""tab""], button'));
        const storyTab = tabs.find(el =>
            /story/i.test(el.textContent) || /story/i.test(el.getAttribute('aria-label') || '')
        );
        if (storyTab) {
            storyTab.click();
            return 'STORY_CLICKED_FALLBACK';
        }

        return 'STORY_NOT_FOUND';
    } catch(e) {
        return 'ERROR:' + e.message;
    }
})();");

                logTextBox.AppendText($"[Upload] Story tab: {storyTabResult?.Trim('\"')}\r\n");

                await Task.Delay(random.Next(2000, 3500));

                logTextBox.AppendText("[Upload] Uploading file...\r\n");

                string uploadScript = $@"
(function() {{
    try {{
        const base64 = '{base64}';
        const mimeType = '{mimeType}';
        const fileName = '{fileName.Replace("'", "_")}';

        // Décoder le base64
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        
        const blob = new Blob([bytes], {{ type: mimeType }});
        const file = new File([blob], fileName, {{ type: mimeType, lastModified: Date.now() }});

        // Trouver l'input
        const input = document.querySelector('input[type=""file""]');
        if (!input) return 'NO_INPUT';

        // ✅ Assigner directement les fichiers SANS déclencher click()
        const dt = new DataTransfer();
        dt.items.add(file);
        
        // Forcer la valeur
        try {{
            Object.defineProperty(input, 'files', {{
                value: dt.files,
                writable: false,
                configurable: true
            }});
        }} catch(e) {{
            input.files = dt.files;
        }}
        
        // Déclencher les événements
        const changeEvent = new Event('change', {{ bubbles: true, cancelable: false }});
        const inputEvent = new Event('input', {{ bubbles: true, cancelable: false }});
        
        input.dispatchEvent(changeEvent);
        input.dispatchEvent(inputEvent);
        
        // Vérifier que les fichiers sont bien assignés
        if (input.files.length === 0) return 'FILES_NOT_ASSIGNED';
        
        return 'SUCCESS';
    }} catch(e) {{
        return 'ERROR:' + e.message;
    }}
}})();";

                string uploadResult = await webView.CoreWebView2.ExecuteScriptAsync(uploadScript);
                logTextBox.AppendText($"[Upload] Upload: {uploadResult?.Trim('\"')}\r\n");

                if (!uploadResult?.Contains("SUCCESS") == true)
                {
                    logTextBox.AppendText("[Upload] ✗ Upload failed\r\n");
                    return false;
                }

                await Task.Delay(random.Next(4000, 6000));

                logTextBox.AppendText("[Upload] Publishing story...\r\n");

                string publishScript = @"
(async function() {
    'use strict';

    const delay = (min, max) => new Promise(r => 
        setTimeout(r, Math.floor(Math.random() * (max - min + 1)) + min)
    );

    const simulateMouseMove = async (element) => {
        const rect = element.getBoundingClientRect();
        const steps = 2 + Math.floor(Math.random() * 2);
        
        for (let i = 0; i < steps; i++) {
            await delay(50, 150);
            const progress = (i + 1) / steps;
            const x = rect.left + rect.width * (0.3 + Math.random() * 0.4) * progress;
            const y = rect.top + rect.height * (0.3 + Math.random() * 0.4) * progress;
            
            element.dispatchEvent(new MouseEvent('mousemove', {
                view: window,
                bubbles: true,
                cancelable: true,
                clientX: x,
                clientY: y
            }));
        }
    };

    const humanClick = async (element) => {
        if (!element) return false;

        element.scrollIntoView({ behavior: 'smooth', block: 'center' });
        await delay(400, 800);

        await simulateMouseMove(element);
        await delay(150, 300);

        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width * (0.45 + Math.random() * 0.1);
        const y = rect.top + rect.height * (0.45 + Math.random() * 0.1);

        const opts = {
            view: window,
            bubbles: true,
            cancelable: true,
            clientX: x,
            clientY: y,
            button: 0,
            buttons: 1
        };

        element.dispatchEvent(new MouseEvent('mouseover', opts));
        element.dispatchEvent(new MouseEvent('mouseenter', opts));
        await delay(80, 200);

        element.dispatchEvent(new MouseEvent('mousedown', opts));
        await delay(80, 160);
        
        if (element.focus) element.focus();
        
        element.dispatchEvent(new MouseEvent('mouseup', opts));
        await delay(30, 80);
        element.dispatchEvent(new MouseEvent('click', opts));
        
        return true;
    };

    try {
        await delay(500, 1200);

        const addSpan = Array.from(document.querySelectorAll('span')).find(el => 
            el.textContent.trim() === 'Ajouter à votre story' ||
            el.textContent.trim() === 'Add to your story' ||
            el.textContent.trim() === 'Share to story'
        );

        if (addSpan) {
            const button = addSpan.closest('button, div[role=""button""], a[role=""button""]');
            if (button) {
                const rect = button.getBoundingClientRect();
                if (rect.width > 0 && rect.height > 0) {
                    await humanClick(button);
                    await delay(200, 500);
                    return 'PUBLISHED_VIA_SPAN';
                }
            }
        }

        const shareBtn = Array.from(document.querySelectorAll('button, div[role=""button""]'))
            .find(el => {
                const text = (el.textContent || '').toLowerCase();
                return text.includes('ajouter à votre story') || 
                       text.includes('add to your story') ||
                       text.includes('share to story');
            });

        if (shareBtn) {
            const rect = shareBtn.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                await humanClick(shareBtn);
                await delay(200, 500);
                return 'PUBLISHED_VIA_TEXT';
            }
        }

        const fallbackBtn = Array.from(document.querySelectorAll('button, div[role=""button""]'))
            .find(el => {
                const text = (el.textContent || '').toLowerCase();
                const label = (el.getAttribute('aria-label') || '').toLowerCase();
                return text.includes('share') || text.includes('partager') || 
                       text.includes('add to') || text.includes('ajouter') ||
                       label.includes('share') || label.includes('add');
            });

        if (fallbackBtn) {
            const rect = fallbackBtn.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                await humanClick(fallbackBtn);
                await delay(200, 500);
                return 'PUBLISHED_FALLBACK';
            }
        }

        return 'BUTTON_NOT_FOUND';
    } catch(e) {
        return 'ERROR:' + e.message;
    }
})();";

                string publishResult = await webView.CoreWebView2.ExecuteScriptAsync(publishScript);
                logTextBox.AppendText($"[Upload] Publish: {publishResult?.Trim('\"')}\r\n");

                await Task.Delay(random.Next(5000, 8000));

                return publishResult?.Contains("PUBLISHED") == true;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Upload] ✗ Exception: {ex.Message}\r\n");
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}