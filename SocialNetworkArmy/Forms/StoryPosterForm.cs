using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
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

        // Samsung Galaxy S23 dimensions (logical pixels)
        private const int DEVICE_WIDTH = 360;
        private const int DEVICE_HEIGHT = 780;
        private const double DEVICE_PIXEL_RATIO = 3.0;

        // Script JS d'émulation Android — avec tokens à remplacer
        private const string ANDROID_EMU_SCRIPT = @"
(function() {
    'use strict';
    const DEVICE_WIDTH = __WIDTH__;
    const DEVICE_HEIGHT = __HEIGHT__;
    const DPR = __DPR__;

    console.log('📱 Samsung Galaxy S23 Emulation Active');

    // ===== SUPPRIMER TRACES AUTOMATION =====
    Object.defineProperty(navigator, 'webdriver', { get: () => undefined, configurable: true, enumerable: false });
    try { delete window.cdc_adoQpoasnfa76pfcZLmcfl_; } catch(e) {}
    try { if (window.chrome && window.chrome.runtime) delete window.chrome.runtime; } catch(e) {}

    // ===== ANDROID PLATFORM =====
    Object.defineProperty(navigator, 'platform', { get: () => 'Linux armv8l', configurable: true, enumerable: true });
    Object.defineProperty(navigator, 'vendor', { get: () => 'Google Inc.', configurable: true, enumerable: true });

    // ===== DEVICE SPECS (Galaxy S23) =====
    Object.defineProperty(navigator, 'maxTouchPoints', { get: () => 5, configurable: true, enumerable: true });
    Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8, configurable: true, enumerable: true });
    Object.defineProperty(navigator, 'deviceMemory', { get: () => 8, configurable: true, enumerable: true });
    Object.defineProperty(window, 'devicePixelRatio', { get: () => DPR, configurable: true, enumerable: true });

    // ===== SCREEN DIMENSIONS =====
    Object.defineProperty(window.screen, 'width', { get: () => DEVICE_WIDTH, configurable: true });
    Object.defineProperty(window.screen, 'height', { get: () => DEVICE_HEIGHT, configurable: true });
    Object.defineProperty(window.screen, 'availWidth', { get: () => DEVICE_WIDTH, configurable: true });
    Object.defineProperty(window.screen, 'availHeight', { get: () => DEVICE_HEIGHT - 48, configurable: true });

    // ===== SCREEN ORIENTATION (Android) =====
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
        configurable: true,
        enumerable: true
    });

    // ===== WINDOW DIMENSIONS =====
    Object.defineProperty(window, 'innerWidth', { get: () => DEVICE_WIDTH, configurable: true });
    Object.defineProperty(window, 'innerHeight', { get: () => DEVICE_HEIGHT, configurable: true });
    Object.defineProperty(window, 'outerWidth', { get: () => DEVICE_WIDTH, configurable: true });
    Object.defineProperty(window, 'outerHeight', { get: () => DEVICE_HEIGHT, configurable: true });

    // ===== VIEWPORT META =====
    const setupViewport = () => {
        if (document.head) {
            let meta = document.querySelector('meta[name=viewport]');
            if (!meta) {
                meta = document.createElement('meta');
                meta.name = 'viewport';
                document.head.appendChild(meta);
            }
            meta.content = 'width=device-width, initial-scale=1.0, maximum-scale=5.0, user-scalable=yes, viewport-fit=cover';
            return true;
        }
        return false;
    };

    if (!setupViewport()) {
        const observer = new MutationObserver(() => {
            if (setupViewport()) observer.disconnect();
        });
        if (document.documentElement) {
            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
    }

    // ===== TOUCH SUPPORT =====
    if (!window.TouchEvent) {
        window.TouchEvent = class TouchEvent extends UIEvent {
            constructor(type, eventInitDict) {
                super(type, eventInitDict);
                this.touches = (eventInitDict && eventInitDict.touches) || [];
                this.targetTouches = (eventInitDict && eventInitDict.targetTouches) || [];
                this.changedTouches = (eventInitDict && eventInitDict.changedTouches) || [];
            }
        };
    }

    if (!window.Touch) {
        window.Touch = class Touch {
            constructor(touchInit) { Object.assign(this, touchInit || {}); }
        };
    }

    // ===== NETWORK INFO (Android) =====
    Object.defineProperty(navigator, 'connection', {
        get: () => ({
            effectiveType: '4g',
            downlink: 10,
            rtt: 50,
            saveData: false,
            type: 'wifi',
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => true,
            onchange: null
        }),
        configurable: true,
        enumerable: true
    });

    // ===== PERMISSIONS API =====
    if (navigator.permissions && navigator.permissions.query) {
        const originalQuery = navigator.permissions.query.bind(navigator.permissions);
        navigator.permissions.query = async (desc) => {
            try {
                if (desc && (desc.name === 'camera' || desc.name === 'microphone' || desc.name === 'geolocation' || desc.name === 'storage')) {
                    return { state: 'granted', onchange: null };
                }
            } catch(e) {}
            return originalQuery(desc);
        };
    }

    // ===== MEDIA DEVICES =====
    if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
        const originalEnumerate = navigator.mediaDevices.enumerateDevices.bind(navigator.mediaDevices);
        navigator.mediaDevices.enumerateDevices = async () => {
            try {
                const list = await originalEnumerate();
                if (Array.isArray(list) && list.length > 0) return list;
            } catch(e) {}
            return [
                { deviceId: 'default', kind: 'audioinput', label: 'Microphone', groupId: 'group1' },
                { deviceId: 'camera-back', kind: 'videoinput', label: 'Back Camera', groupId: 'group2' },
                { deviceId: 'camera-front', kind: 'videoinput', label: 'Front Camera', groupId: 'group2' },
                { deviceId: 'speaker', kind: 'audiooutput', label: 'Speaker', groupId: 'group3' }
            ];
        };
    }

    // ===== CLEANUP =====
    const toDelete = [
        '__webdriver_script_fn','__webdriver_script_func','__webdriver_script_arguments',
        '__selenium_unwrapped','__webdriver_unwrapped','__driver_evaluate','__webdriver_evaluate',
        '__fxdriver_evaluate','__driver_unwrapped','__fxdriver_unwrapped','__webdriver_evaluate'
    ];
    try {
        toDelete.forEach(k => { try { delete window[k]; } catch(e) {} });
        toDelete.forEach(k => { try { delete document[k]; } catch(e) {} });
    } catch(e) {}

    console.log('✅ Android emulation ready');
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

            // Charge le navigateur (mais NE poste pas tout seul)
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
                Text = "Post Today's Story",
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
                logTextBox.AppendText("[INFO] Initializing Samsung Galaxy S23 emulation...\r\n");
                Directory.CreateDirectory(userDataFolder);

                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--disable-blink-features=AutomationControlled " +
                        "--disable-features=IsolateOrigins,site-per-process " +
                        "--disable-site-isolation-trials " +
                        "--disable-web-security " +
                        "--disable-dev-shm-usage " +
                        "--no-sandbox " +
                        $"--window-size={DEVICE_WIDTH},{DEVICE_HEIGHT} " +
                        "--user-agent=\"Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36\""
                };

                if (!string.IsNullOrWhiteSpace(profile.Proxy))
                {
                    logTextBox.AppendText($"[Proxy] Applying proxy: {profile.Proxy}\r\n");
                    proxyService.ApplyProxy(options, profile.Proxy);
                }
                else
                {
                    logTextBox.AppendText("[Proxy] No proxy - Direct connection\r\n");
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder,
                    options: options
                );

                await webView.EnsureCoreWebView2Async(env);
                logTextBox.AppendText("[INFO] WebView2 environment initialized\r\n");

                // Injecte le script d’emulation sans casser C#
                var script = ANDROID_EMU_SCRIPT
                    .Replace("__WIDTH__", DEVICE_WIDTH.ToString(CultureInfo.InvariantCulture))
                    .Replace("__HEIGHT__", DEVICE_HEIGHT.ToString(CultureInfo.InvariantCulture))
                    .Replace("__DPR__", DEVICE_PIXEL_RATIO.ToString(CultureInfo.InvariantCulture));

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                logTextBox.AppendText("[INFO] Android emulation script injected\r\n");

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                

                if (!string.IsNullOrEmpty(profile.Proxy))
                {
                    proxyService.SetupProxyAuthentication(webView.CoreWebView2, profile.Proxy);
                    logTextBox.AppendText("[INFO] Proxy authentication set up\r\n");
                }
                else
                {
                    webView.CoreWebView2.BasicAuthenticationRequested += (s, e) => e.Cancel = true;
                }

                webView.CoreWebView2.NavigationCompleted += async (sender, args) =>
                {
                    logTextBox.AppendText("[INFO] Navigation completed\r\n");
                    await LogEnvironmentAsync();

                    isWebViewReady = true;
                    if (postStoryButton.InvokeRequired)
                        postStoryButton.Invoke(new Action(() => postStoryButton.Enabled = true));
                    else
                        postStoryButton.Enabled = true;
                };

                webView.CoreWebView2.Navigate("https://www.instagram.com/");
                logTextBox.AppendText("[INFO] Navigating to Instagram...\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ERROR] {ex.Message}\r\n");
                MessageBox.Show($"Browser error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LogEnvironmentAsync()
        {
            try
            {
                logTextBox.AppendText("[INFO] Logging effective environment...\r\n");
                string envLog = await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            return JSON.stringify({
                                userAgent: navigator.userAgent,
                                platform: navigator.platform,
                                vendor: navigator.vendor,
                                maxTouchPoints: navigator.maxTouchPoints,
                                hardwareConcurrency: navigator.hardwareConcurrency,
                                deviceMemory: navigator.deviceMemory,
                                devicePixelRatio: window.devicePixelRatio,
                                screen: {
                                    width: window.screen.width,
                                    height: window.screen.height,
                                    availWidth: window.screen.availWidth,
                                    availHeight: window.screen.availHeight
                                },
                                innerWidth: window.innerWidth,
                                innerHeight: window.innerHeight,
                                orientation: (window.screen.orientation && window.screen.orientation.type) || 'unknown',
                                connection: (navigator.connection && navigator.connection.effectiveType) || 'unknown'
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
                logTextBox.AppendText("[Story] ✗ WebView not ready yet. Please wait...\r\n");
                return false;
            }

            logTextBox.AppendText("[Story] Looking for today's story in schedule.csv...\r\n");
            string mediaPath = GetTodayStoryMediaPath();

            if (string.IsNullOrEmpty(mediaPath))
            {
                logTextBox.AppendText("[Story] ✗ No story found for today or invalid media path\r\n");
                return false;
            }

            logTextBox.AppendText($"[Story] Found media: {mediaPath}\r\n");
            bool success = await UploadStoryAsync(mediaPath);

            if (success) logTextBox.AppendText("[Story] ✓ Story posted successfully!\r\n");
            else logTextBox.AppendText("[Story] ✗ Failed to post story\r\n");

            return success;
        }

        private string GetTodayStoryMediaPath()
        {
            string csvPath = Path.Combine("Data", "schedule.csv");
            if (!File.Exists(csvPath))
            {
                logTextBox.AppendText($"[Story] ✗ schedule.csv not found at {csvPath}\r\n");
                return null;
            }

            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string[] lines = File.ReadAllLines(csvPath);
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var columns = line.Split(',');
                    if (columns.Length < 5) continue;

                    string dateTime = columns[0].Trim();
                    string platform = columns[1].Trim();
                    string account = columns[2].Trim();
                    string activity = columns[3].Trim();
                    string media = columns[4].Trim();

                    if (dateTime.StartsWith(today, StringComparison.Ordinal) &&
                        platform.Equals(profile.Platform, StringComparison.OrdinalIgnoreCase) &&
                        account.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) &&
                        activity.Equals("story", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(media) &&
                        File.Exists(media))
                    {
                        return media;
                    }
                }

                logTextBox.AppendText($"[Story] No valid story entry found for {today}, profile {profile.Name}, platform {profile.Platform}\r\n");
                return null;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Story] ✗ Error reading schedule.csv: {ex.Message}\r\n");
                return null;
            }
        }

        public async Task<bool> UploadStoryAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                logTextBox.AppendText($"[Upload] ✗ File not found: {filePath}\r\n");
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

                logTextBox.AppendText($"[Upload] Type: {((extension == ".mp4" || extension == ".mov") ? "Video" : "Image")}\r\n");
                logTextBox.AppendText($"[Upload] Size: {fileBytes.Length / 1024} KB\r\n");

                await Task.Delay(random.Next(2500, 4500));
                logTextBox.AppendText("[Upload] Clicking 'Create' button...\r\n");

                var createResult = await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            // Recherche multi-méthodes du bouton Create
                            
                            // Méthode 1: Via l'aria-label du SVG (le plus fiable)
                            const createSvg = document.querySelector('svg[aria-label=""Accueil""]')?.closest('[role]')?.parentElement;
                            if (createSvg) {
                                createSvg.click();
                                return 'CREATE_BUTTON_CLICKED_VIA_SVG';
                            }
                            
                            // Méthode 2: Chercher le SVG avec le path spécifique (icône +)
                            const allSvgs = Array.from(document.querySelectorAll('svg'));
                            const plusSvg = allSvgs.find(svg => {
                                const path = svg.querySelector('path[d*=""M2 12v3.45""]');
                                const line1 = svg.querySelector('line[x1=""6.545""]');
                                const line2 = svg.querySelector('line[x1=""12.003""]');
                                return path && line1 && line2;
                            });
                            
                            if (plusSvg) {
                                let parent = plusSvg.closest('a, div[role=""button""], [tabindex]');
                                if (parent) {
                                    parent.click();
                                    return 'CREATE_BUTTON_CLICKED_VIA_PLUS_ICON';
                                }
                            }
                            
                            // Méthode 3: Chercher via aria-label ""Accueil"" (dans le document fourni)
                            const accueilBtn = document.querySelector('[aria-label=""Accueil""]')?.closest('a, div[role], [tabindex]');
                            if (accueilBtn) {
                                accueilBtn.click();
                                return 'CREATE_BUTTON_CLICKED_VIA_ACCUEIL';
                            }
                            
                            // Méthode 4: Fallback classique
                            const btn = document.querySelector('a[href*=""/create""]')
                                   || document.querySelector('svg[aria-label*=""New post""]')?.closest('a')
                                   || document.querySelector('[aria-label*=""Create""]')?.closest('a')
                                   || document.querySelector('[aria-label*=""Créer""]')?.closest('a');
                            if (btn) { 
                                btn.click(); 
                                return 'CREATE_BUTTON_CLICKED_FALLBACK'; 
                            }
                            
                            return 'CREATE_BUTTON_NOT_FOUND';
                        } catch(e) { return 'ERROR:' + e.message; }
                    })();
                ");

                logTextBox.AppendText($"[Upload] Create result: {createResult?.Trim('\"') ?? "No response"}\r\n");
                if (createResult == null || !createResult.Contains("CREATE_BUTTON_CLICKED"))
                {
                    logTextBox.AppendText("[Upload] ⚠ Create button not found\r\n");
                    return false;
                }

                await Task.Delay(random.Next(1500, 3000));

                logTextBox.AppendText("[Upload] Selecting 'Story' tab...\r\n");
                await webView.CoreWebView2.ExecuteScriptAsync(@"
                    (function() {
                        try {
                            const tabs = Array.from(document.querySelectorAll('div[role=""tab""], button'));
                            const storyTab = tabs.find(el =>
                                (el.textContent && /story/i.test(el.textContent)) ||
                                (el.getAttribute('aria-label') && /story/i.test(el.getAttribute('aria-label')))
                            );
                            if (storyTab) storyTab.click();
                            return 'OK';
                        } catch(e) { return 'ERROR:' + e.message; }
                    })();
                ");

                await Task.Delay(random.Next(1500, 3000));

                logTextBox.AppendText("[Upload] Uploading file...\r\n");
                string injectScript = $@"
(function() {{
    try {{
        const base64 = '{base64}';
        const mimeType = '{mimeType}';
        const fileName = '{fileName.Replace("'", "_")}';

        const byteCharacters = atob(base64);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) byteNumbers[i] = byteCharacters.charCodeAt(i);
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], {{ type: mimeType }});
        const file = new File([blob], fileName, {{ type: mimeType, lastModified: Date.now() }});

        let input = document.querySelector('input[type=""file""]');
        if (input) {{
            const dt = new DataTransfer();
            dt.items.add(file);
            input.files = dt.files;
            input.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return 'SUCCESS';
        }}
        return 'INPUT_NOT_FOUND';
    }} catch(error) {{
        return 'ERROR: ' + error.message;
    }}
}})();";

                string result = await webView.CoreWebView2.ExecuteScriptAsync(injectScript);
                logTextBox.AppendText($"[Upload] Result: {result?.Trim('\"') ?? "No response"}\r\n");

                if (result == null || !result.Contains("SUCCESS"))
                {
                    logTextBox.AppendText($"[Upload] ✗ Upload failed: {result}\r\n");
                    return false;
                }

                await Task.Delay(random.Next(2500, 4000));

                // Optionnel: charger stickers
                await ForceLoadStickersAsync();
                await Task.Delay(random.Next(1500, 2500));

                string publishScript = @"
(async function() {
    try {
        // Attente pour que le DOM soit prêt
        await new Promise(r => setTimeout(r, 500));

        // Méthode 1 : Recherche directe du span (la plus fiable)
        const span = Array.from(document.querySelectorAll('span')).find(el => 
            el.textContent.trim() === 'Ajouter à votre story' || 
            el.textContent.trim() === 'Add to your story' ||
            el.textContent.trim() === 'Share to story'
        );

        if (span) {
            // Scroll vers l'élément
            span.scrollIntoView({behavior: 'smooth', block: 'center'});
            await new Promise(r => setTimeout(r, 300));

            // Vérifie visibilité
            const rect = span.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) {
                return 'STORY_SPAN_INVISIBLE';
            }

            // Triple clic : span + 3 niveaux de parents
            span.click();
            await new Promise(r => setTimeout(r, 50));
            
            if (span.parentElement) {
                span.parentElement.click();
                await new Promise(r => setTimeout(r, 50));
            }
            
            if (span.parentElement?.parentElement) {
                span.parentElement.parentElement.click();
                await new Promise(r => setTimeout(r, 50));
            }
            
            if (span.parentElement?.parentElement?.parentElement) {
                span.parentElement.parentElement.parentElement.click();
            }

            return 'STORY_SPAN_CLICKED_SUCCESS';
        }

        // Méthode 2 : Fallback via SVG Story
        const svg = document.querySelector('svg[aria-label=""Story""], svg[aria-label=""story"" i]');
        if (svg) {
            let parent = svg;
            for (let i = 0; i < 5; i++) {
                parent = parent.parentElement;
                if (!parent || parent === document.body) break;
                
                const style = window.getComputedStyle(parent);
                if (parent.onclick || style.cursor === 'pointer' || parent.getAttribute('role') === 'button') {
                    parent.click();
                    return 'STORY_SVG_PARENT_CLICKED';
                }
            }
            
            // Clic sur le 3e parent par défaut
            if (svg.parentElement?.parentElement?.parentElement) {
                svg.parentElement.parentElement.parentElement.click();
                return 'STORY_SVG_FALLBACK_CLICKED';
            }
        }

        // Méthode 3 : Recherche agressive par texte
        const allElements = Array.from(document.querySelectorAll('div, button, span'));
        const textMatch = allElements.find(el => {
            const text = (el.textContent || '').toLowerCase();
            return (text.includes('ajouter à votre story') || 
                    text.includes('add to your story') ||
                    text.includes('share to story')) && 
                   text.length < 100;
        });

        if (textMatch) {
            textMatch.click();
            return 'STORY_TEXT_ELEMENT_CLICKED';
        }

        return 'STORY_BUTTON_NOT_FOUND';
    } catch (e) {
        return 'ERROR:' + e.message;
    }
})();
";
                string publishResult = await webView.CoreWebView2.ExecuteScriptAsync(publishScript);
                logTextBox.AppendText($"[Upload] Publish result: {publishResult?.Trim('\"') ?? "No response"}\r\n");
                await Task.Delay(10000);
                return true;


            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Upload] ✗ Exception: {ex.Message}\r\n");
                return false;
            }

        }
        
        private async Task ForceLoadStickersAsync()
        {
            try
            {
                logTextBox.AppendText("[Stickers] Loading stickers (Android mode)...\r\n");

                string stickerScript = @"
(function() {
    try {
        const selectors = [
            '[aria-label*=""sticker"" i]',
            '[aria-label*=""Sticker"" i]',
            '[aria-label*=""GIF"" i]',
            '[aria-label*=""Draw"" i]',
            '[aria-label*=""Text"" i]',
            '[aria-label*=""Aa""]',
            'svg[aria-label]',
            'button[type=""button""]'
        ];

        let foundButtons = [];

        for (const selector of selectors) {
            const elements = document.querySelectorAll(selector);
            elements.forEach(el => {
                const label = (el.getAttribute('aria-label') || el.textContent || '').toLowerCase();
                if (label.includes('sticker') || label.includes('gif') || label.includes('text') || label.includes('draw')) {
                    foundButtons.push({
                        element: el,
                        label: label,
                        type: label.includes('sticker') ? 'sticker' :
                              label.includes('gif') ? 'gif' :
                              label.includes('text') ? 'text' : 'other'
                    });
                }
            });
        }

        const stickerBtn = foundButtons.find(b => b.type === 'sticker');
        if (stickerBtn) {
            stickerBtn.element.click();
            return 'STICKERS_LOADED:' + foundButtons.length + '_buttons';
        }

        return foundButtons.length > 0 ?
            'BUTTONS_FOUND:' + foundButtons.map(b => b.type).join(',') :
            'NO_STICKER_BUTTONS';
    } catch(e) {
        return 'ERROR: ' + e.message;
    }
})();";

                string result = await webView.CoreWebView2.ExecuteScriptAsync(stickerScript);
                logTextBox.AppendText($"[Stickers] Result: {result?.Trim('\"') ?? "Unknown"}\r\n");

                if (result != null && (result.Contains("STICKERS_LOADED") || result.Contains("BUTTONS_FOUND")))
                {
                    logTextBox.AppendText("[Stickers] ✓ Sticker UI likely available\r\n");
                    await Task.Delay(random.Next(1000, 2000));

                    await webView.CoreWebView2.ExecuteScriptAsync(@"
                        setTimeout(() => {
                            const panels = document.querySelectorAll('[role=""dialog""], div[style*=""overflow""]');
                            panels.forEach(panel => {
                                if (panel.scrollHeight > panel.clientHeight) {
                                    panel.scrollBy(0, 100);
                                    setTimeout(() => panel.scrollBy(0, -100), 400);
                                }
                            });
                        }, 800);
                    ");
                }
                else
                {
                    logTextBox.AppendText("[Stickers] ⚠ Not immediately available\r\n");
                }
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Stickers] Error: {ex.Message}\r\n");
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
