using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Services;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SocialNetworkArmy.Forms
{
    public partial class StoryPosterForm : Form
    {
        private WebView2 webView;
        private readonly Profile profile;
        private TextBox logTextBox;
        private readonly ProxyService proxyService;
        private SharedCookiesService sharedCookiesService;
        private readonly Random random = new Random();
        private readonly string userDataFolder;
        private Button postStoryButton;
        private bool isWebViewReady = false;
        private bool isDisposing = false;
        // ✅ NOUVEAU: Fingerprinting pour stealth 10/10
        private readonly FingerprintService fingerprintService;
        private readonly Models.Fingerprint fingerprint;
        // Device dimensions extracted from fingerprint
        private readonly int deviceWidth;
        private readonly int deviceHeight;
        private Font yaheiBold10 = new Font("Microsoft YaHei", 9f, FontStyle.Bold);
        private Panel bottomPanel;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
      


        public StoryPosterForm(Profile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.userDataFolder = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "SocialNetworkArmy", "Profiles", profile.Name, "Story");
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
         ControlStyles.UserPaint |
         ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
            proxyService = new ProxyService();
            sharedCookiesService = new SharedCookiesService(profile.Name);
            // ✅ NOUVEAU: Générer fingerprint mobile unique
            fingerprintService = new FingerprintService();
            fingerprint = fingerprintService.GenerateMobileFingerprint();

            // Extract dimensions from fingerprint resolution (e.g., "360x800")
            var resParts = fingerprint.ScreenResolution.Split('x');
            deviceWidth = int.Parse(resParts[0]);
            deviceHeight = int.Parse(resParts[1]);

            InitializeComponent();
            if (Environment.OSVersion.Version.Major >= 10)
            {
                int useImmersiveDarkMode = 1;
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            }
            this.Icon = new Icon("Data\\Icons\\Insta.ico");

            _ = LoadBrowserAsync();
        }

        private void InitializeComponent()
        {
            this.BackColor = Color.FromArgb(15, 15, 15);
            this.ForeColor = Color.White;
            this.ClientSize = new Size(deviceWidth + 20, deviceHeight + 140);
            this.Text = $"📱 Story - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(deviceWidth + 20, 700);

            // WebView avec ombre
            webView = new WebView2
            {
                Location = new Point(10, 10),
                Size = new Size(deviceWidth, deviceHeight),
                BackColor = Color.Black,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // Bottom Panel transparent
            bottomPanel = new Panel
            {
                Location = new Point(0, deviceHeight + 20),
                Size = new Size(this.ClientSize.Width, 120),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Transparent,
                Padding = new Padding(10)
            };

            // Log Panel avec effet
            var logPanel = new Panel
            {
                Location = new Point(10, 10),
                Size = new Size(deviceWidth - 130, 100),
                BackColor = Color.Transparent
            };

            // Log TextBox
            logTextBox = new TextBox
            {
                Location = new Point(1, 1),
                Size = new Size(logPanel.Width - 2, logPanel.Height - 2),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f, FontStyle.Regular),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // Post Story Button styled
            postStoryButton = new Button
            {
                Text = "📤 Post Story",
                Location = new Point(deviceWidth - 110, 10),
                Size = new Size(120, 100),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(156, 39, 176),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Font = yaheiBold10,
                Enabled = false,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            postStoryButton.FlatAppearance.BorderSize = 0;
            postStoryButton.FlatAppearance.MouseOverBackColor = DarkenColor(Color.FromArgb(156, 39, 176), 20);
            postStoryButton.Click += PostStoryButton_Click;

            logPanel.Controls.Add(logTextBox);
            bottomPanel.Controls.Add(logPanel);
            bottomPanel.Controls.Add(postStoryButton);

            this.Controls.Add(webView);
            this.Controls.Add(bottomPanel);
            this.FormClosing += StoryPosterForm_FormClosing;
        }

        private async Task LoadBrowserAsync()
        {
            try
            {
                logTextBox.AppendText("[INFO] Initializing browser...\r\n");
                Directory.CreateDirectory(userDataFolder);

                // ✅ PARTAGE DES COOKIES: Copier depuis Main vers Story
                var storyCookiesDir = Path.Combine(userDataFolder, "Default");

                // ✅ Vérifier la validité AVANT de charger
                if (!sharedCookiesService.AreSharedCookiesValid())
                {
                    logTextBox.AppendText("[Cookies] ⚠️ No valid cookies found - login required\r\n");
                }
                else
                {
                    await sharedCookiesService.LoadSharedCookiesAsync(
                        storyCookiesDir,
                        msg => logTextBox.AppendText(msg + "\r\n")
                    );
                }

                // ✅ AMÉLIORATION: Utiliser User-Agent du fingerprint (randomisé)
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
                        $"--window-size={deviceWidth},{deviceHeight} " +
                        $"--user-agent=\"{fingerprint.UserAgent}\""
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

                // ✅ USE FingerprintService full mode for stories (better stealth for media upload)
                // Full mode includes Canvas/Audio/WebGL spoofing for maximum stealth on story uploads
                var stealthScript = fingerprintService.GenerateJSSpoof(fingerprint);
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(stealthScript);
                logTextBox.AppendText("[OK] Mobile stealth script injected (Full mode with fingerprint)\r\n");

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
        private Color DarkenColor(Color color, int amount)
        {
            return Color.FromArgb(
                color.A,
                Math.Max(0, color.R - amount),
                Math.Max(0, color.G - amount),
                Math.Max(0, color.B - amount)
            );
        }

        private void StoryPosterForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                return;

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Gradient background identique à MainForm
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

            // Ombre sous WebView
            if (webView != null)
                DrawControlShadow(e.Graphics, webView);

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
       

        private async void PostStoryButton_Click(object sender, EventArgs e)
        {
            await PostTodayStoryAsync();
        }



        // Dans StoryPosterForm.cs, remplacer GetTodayStoryMediaPath() par ceci :

        private string GetTodayStoryMediaPath()
        {
            try
            {
                logTextBox.AppendText("[Schedule] ========================================\r\n");
                logTextBox.AppendText("[Schedule] Starting story search...\r\n");

                // ✅ PASSER LE TEXTBOX POUR LES LOGS DÉTAILLÉS
                var match = ScheduleHelper.GetTodayMediaForAccount(
                    profile.Name,
                    profile.Platform,
                    "story",
                    targetDate: null,
                    log: logTextBox  // ⬅️ AJOUT CRITIQUE!
                );

                if (match == null)
                {
                    logTextBox.AppendText("[Schedule] ✗ No story scheduled for today\r\n");
                    logTextBox.AppendText("[Schedule] ========================================\r\n");
                    return null;
                }

                logTextBox.AppendText($"[Schedule] ✓ Found {(match.IsGroup ? "group" : "account")} match: {match.AccountOrGroup}\r\n");
                logTextBox.AppendText($"[Schedule] ✓ Media: {Path.GetFileName(match.MediaPath)}\r\n");
                logTextBox.AppendText("[Schedule] ========================================\r\n");

                return match.MediaPath;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Schedule] ✗ Error: {ex.Message}\r\n");
                logTextBox.AppendText($"[Schedule] Stack: {ex.StackTrace}\r\n");
                logTextBox.AppendText("[Schedule] ========================================\r\n");
                return null;
            }
        }
        public async Task<bool> PostTodayStoryAsync()
        {
            if (!isWebViewReady)
            {
                logTextBox.AppendText("[Story] ✗ Not ready\r\n");
                return false;
            }

            // Vérifier qu'on n'a pas posté récemment (cooldown 15min)
            string lastPostFile = Path.Combine(userDataFolder, "last_story_post.txt");
            if (File.Exists(lastPostFile))
            {
                string lastPostStr = File.ReadAllText(lastPostFile);
                if (DateTime.TryParse(lastPostStr, out DateTime lastPost))
                {
                    TimeSpan elapsed = DateTime.Now - lastPost;
                    if (elapsed.TotalMinutes < 15)
                    {
                        logTextBox.AppendText($"[Story] ✗ Cooldown active ({elapsed.TotalMinutes:F1}min ago)\r\n");
                        return false;
                    }
                }
            }

            logTextBox.AppendText("[Story] Checking schedule...\r\n");

            // ✅ UTILISER LA MÉTHODE CORRIGÉE
            string mediaPath = GetTodayStoryMediaPath();

            if (string.IsNullOrEmpty(mediaPath))
            {
                logTextBox.AppendText("[Story] ✗ No story scheduled today\r\n");
                return false;
            }

            logTextBox.AppendText($"[Story] Uploading: {Path.GetFileName(mediaPath)}\r\n");
            await UploadStoryAsync(mediaPath);

            // Enregistrer le timestamp
            logTextBox.AppendText("[Story] ✓ Process completed!\r\n");
            File.WriteAllText(lastPostFile, DateTime.Now.ToString("o"));

            return true;
        }
        public async Task UploadStoryAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                logTextBox.AppendText($"[Upload] ✗ File not found\r\n");
                return;
            }

            try
            {
                string fileName = Path.GetFileName(filePath);
                logTextBox.AppendText($"[Upload] Processing: {fileName}...\r\n");

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                if (fileBytes.Length > 50 * 1024 * 1024)
                {
                    logTextBox.AppendText("[Upload] ✗ File too large (max 50MB)\r\n");
                    return;
                }

                string base64 = Convert.ToBase64String(fileBytes);
                string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
                string mimeType = (extension == ".mp4" || extension == ".mov") ? "video/mp4"
                                : (extension == ".png") ? "image/png"
                                : "image/jpeg";

                logTextBox.AppendText($"[Upload] Type: {mimeType}, Size: {fileBytes.Length / 1024}KB\r\n");

                // Délai humain VARIABLE
                int initialDelay = random.Next(2000, 6000);
                logTextBox.AppendText($"[Upload] Waiting {initialDelay}ms...\r\n");
                await Task.Delay(initialDelay);

                // Simuler une activité aléatoire (50% de chance)
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

                await webView.CoreWebView2.ExecuteScriptAsync(@"
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
                return;
            }
        }

        const btn = document.querySelector('a[href*=""/create""]') ||
                   document.querySelector('svg[aria-label*=""Create""]')?.closest('a') ||
                   document.querySelector('svg[aria-label*=""Créer""]')?.closest('a');
        
        if (btn) btn.click();
    } catch(e) {}
})();");

                await Task.Delay(random.Next(2000, 3500));

                logTextBox.AppendText("[Upload] Selecting Story tab...\r\n");

                await webView.CoreWebView2.ExecuteScriptAsync(@"
(function() {
    try {
        const storySvg = document.querySelector('svg[aria-label=""Story""]');
        if (storySvg) {
            const button = storySvg.closest('div[role=""button""]');
            if (button) {
                button.click();
                return;
            }
        }

        const tabs = Array.from(document.querySelectorAll('div[role=""tab""], button'));
        const storyTab = tabs.find(el =>
            /story/i.test(el.textContent) || /story/i.test(el.getAttribute('aria-label') || '')
        );
        if (storyTab) storyTab.click();
    } catch(e) {}
})();");

                await Task.Delay(random.Next(2000, 3500));

                logTextBox.AppendText("[Upload] Uploading file...\r\n");

                string uploadScript = $@"
(function() {{
    try {{
        const base64 = '{base64}';
        const mimeType = '{mimeType}';
        const fileName = '{fileName.Replace("'", "_")}';

        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        
        const blob = new Blob([bytes], {{ type: mimeType }});
        const file = new File([blob], fileName, {{ type: mimeType, lastModified: Date.now() }});

        const input = document.querySelector('input[type=""file""]');
        if (!input) return;

        const dt = new DataTransfer();
        dt.items.add(file);
        
        try {{
            Object.defineProperty(input, 'files', {{
                value: dt.files,
                writable: false,
                configurable: true
            }});
        }} catch(e) {{
            input.files = dt.files;
        }}
        
        const changeEvent = new Event('change', {{ bubbles: true, cancelable: false }});
        const inputEvent = new Event('input', {{ bubbles: true, cancelable: false }});
        
        input.dispatchEvent(changeEvent);
        input.dispatchEvent(inputEvent);
    }} catch(e) {{}}
}})();";

                await webView.CoreWebView2.ExecuteScriptAsync(uploadScript);

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
        if (!element) return;

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
                    return;
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
                return;
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
            }
        }
    } catch(e) {}
})();";

                await webView.CoreWebView2.ExecuteScriptAsync(publishScript);
                logTextBox.AppendText("[Upload] Publication initiated\r\n");

                await Task.Delay(random.Next(3000, 5000));
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Upload] ✗ Exception: {ex.Message}\r\n");
            }
        }
        private void StoryPosterForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.TaskManagerClosing &&
                webView?.CoreWebView2 != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500);
                        var storyCookiesDir = Path.Combine(userDataFolder, "Default");
                        await sharedCookiesService.SaveSharedCookiesAsync(
                            storyCookiesDir,
                            msg => { }
                        );
                    }
                    catch { }
                });
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
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"WebView2 dispose error: {ex.Message}");
                        }
                        webView = null;
                    }

                    try
                    {
                        logTextBox?.Dispose();
                        postStoryButton?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Controls dispose error: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"General dispose error: {ex.Message}");
                }
            }

            base.Dispose(disposing);
        }
    }
}