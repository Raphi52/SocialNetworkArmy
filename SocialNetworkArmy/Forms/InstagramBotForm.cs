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
    public partial class InstagramBotForm : Form
    {
        private TargetService targetService;
        private ScrollService scrollService;
        private PublishService publishService;
        private DirectMessageService dmService;
        private DownloadInstagramService downloadService;
        private readonly Profile profile;
        private readonly AutomationService automationService;
        private readonly ProxyService proxyService;
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
        private Label lblProxyStatus;
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
            proxyService = new ProxyService();
            automationService = new AutomationService(new FingerprintService(), proxyService, limitsService, cleanupService, monitoringService, profile);
            InitializeComponent();
            this.Icon = new System.Drawing.Icon("Data\\Icons\\Insta.ico");
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
            this.Text = $"Instagram Bot - {profile.Name}";
            this.StartPosition = FormStartPosition.CenterScreen;

            // WebView
            webView = new WebView2();
            webView.DefaultBackgroundColor = Color.Black;
            webView.Location = new Point(0, 0);
            webView.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 220);
            webView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(webView);

            // Bottom Panel (reduced height from 240 to 220)
            var bottomPanel = new Panel
            {
                Location = new Point(0, this.ClientSize.Height - 220),
                Size = new Size(this.ClientSize.Width, 220),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(0)
            };

            // Buttons Panel (moved up to Y=10, height 50)
            var buttonsPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(bottomPanel.Width - 20, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(30, 30, 30)
            };

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

            // Proxy Status Label (added to buttonsPanel, to the right)
            lblProxyStatus = new Label
            {
                AutoSize = true,
                Text = "Checking Proxy...",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 10f, FontStyle.Regular),
                Margin = new Padding(30, 7, 0, 0), // 30px left spacing from Stop, 7px top for vertical centering
                TextAlign = ContentAlignment.MiddleLeft
            };
            buttonsPanel.Controls.Add(lblProxyStatus);

            // Logs (moved up to Y=60, increased height to Height - 70 = 150)
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
                    profile.Name
                );
                Directory.CreateDirectory(userDataDir);

                var options = new CoreWebView2EnvironmentOptions();

                options.AdditionalBrowserArguments =
                    "--disable-blink-features=AutomationControlled " +
                    "--disable-dev-shm-usage " +
                    "--no-sandbox";

                string proxyUsed = profile.Proxy;

                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    logTextBox.AppendText($"[INFO] Applying proxy from profile: {proxyUsed}\r\n");
                    proxyService.ApplyProxy(options, proxyUsed);
                }
                else
                {
                    logTextBox.AppendText($"[INFO] No proxy configured in profile. Using direct connection.\r\n");
                    lblProxyStatus.Text = "No Proxy - Direct Connection";
                    lblProxyStatus.ForeColor = Color.Orange;

                    string localIp = await GetPublicIpAsync();
                    if (!string.IsNullOrEmpty(localIp))
                    {
                        var (city, country) = await GetLocationAsync(localIp);
                        string locationText = FormatLocationShort(city, country);
                        lblProxyStatus.Text = $"No Proxy - IP: {localIp}{locationText}";
                    }
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDir,
                    options: options
                );

                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    proxyService.SetupProxyAuthentication(webView.CoreWebView2, proxyUsed);
                }
                else
                {
                    webView.CoreWebView2.BasicAuthenticationRequested += (sender, e) =>
                    {
                        e.Cancel = true;
                    };
                }

                webView.CoreWebView2.NavigationStarting += async (sender, args) =>
                {
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                Object.defineProperty(navigator, 'webdriver', {
                    get: () => false
                });

                Object.defineProperty(navigator, 'plugins', {
                    get: () => [1, 2, 3, 4, 5]
                });

                Object.defineProperty(navigator, 'languages', {
                    get: () => ['en-US', 'en']
                });

                window.chrome = {
                    runtime: {},
                    loadTimes: function() {},
                    csi: function() {},
                    app: {}
                };

                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                    parameters.name === 'notifications' ?
                        Promise.resolve({ state: Notification.permission }) :
                        originalQuery(parameters)
                );

                Object.defineProperty(navigator, 'connection', {
                    get: () => ({
                        effectiveType: '4g',
                        rtt: 50,
                        downlink: 10,
                        saveData: false
                    })
                });

                delete navigator.__proto__.webdriver;
            ");
                };

                webView.CoreWebView2.Navigate("https://www.instagram.com/");

                await Task.Delay(800);
                webView.Focus();
                webView.BringToFront();

                targetButton.Enabled = true;
                targetService = new TargetService(webView, logTextBox, profile, this);
                scrollService = new ScrollService(webView, logTextBox, profile, this);
                publishService = new PublishService(webView, logTextBox, this);
                dmService = new DirectMessageService(webView, logTextBox, profile, this);
                downloadService = new DownloadInstagramService(webView, logTextBox, profile, this);

                try { await monitoringService.TestFingerprintAsync(webView); } catch { /* ignore */ }

                if (!string.IsNullOrEmpty(proxyUsed))
                {
                    await UpdateProxyStatusAsync(proxyUsed);
                }

                logTextBox.AppendText("[INFO] WebView2 ready. Stealth mode active.\r\n");
                logTextBox.AppendText($"[INFO] Browser arguments: {options.AdditionalBrowserArguments}\r\n");
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[ERROR] WebView2 initialization error: {ex.Message}\r\n");
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

                await Task.Delay(6000);

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
                logTextBox.AppendText("[Proxy] Check: format[](http://user:pass@host:port), credentials, and proxy is online\r\n");
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