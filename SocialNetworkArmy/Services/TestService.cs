using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    public class TestService
    {
        private readonly InstagramBotForm form;
        private readonly WebView2 webView;
        private readonly TextBox logTextBox;
        private readonly Profile profile;
        private readonly Random _rng = new Random();
        private readonly HttpClient httpClient = new HttpClient();

        public TestService(WebView2 webView, TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.logTextBox = logTextBox ?? throw new ArgumentNullException(nameof(logTextBox));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.form = form ?? throw new ArgumentNullException(nameof(form));

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        private void Log(string message)
        {
            logTextBox?.AppendText($"[TEST] {message}{Environment.NewLine}");
        }

        private async Task<string> ExecJsAsync(string js, CancellationToken token)
        {
            var res = await webView.ExecuteScriptAsync(js);

            if (res != null && res.StartsWith("\"") && res.EndsWith("\""))
            {
                res = res.Substring(1, res.Length - 2);
                res = res.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "");
            }

            return res ?? "ERROR_NO_RESPONSE";
        }

        private async Task AutomateLogin(string username, string password, CancellationToken token)
        {
            try
            {
                Log("Automating login...");
                webView.CoreWebView2.Navigate("https://www.instagram.com/accounts/login/");
                await Task.Delay(3000, token);

                string fillScript = $@"
                    (function() {{
                        let userInput = document.querySelector('input[name=""username""]');
                        if (userInput) userInput.value = '{username.Replace("'", "\\'")}';
                        let passInput = document.querySelector('input[name=""password""]');
                        if (passInput) passInput.value = '{password.Replace("'", "\\'")}';
                        let submitBtn = document.querySelector('button[type=""submit""]');
                        if (submitBtn) submitBtn.click();
                        return 'Submitted';
                    }})();
                ";
                await ExecJsAsync(fillScript, token);
                await Task.Delay(5000, token);  // Wait for login

                // Handle 2FA if needed (manual for now)
                Log("Login submitted. Check for 2FA/CAPTCHA manually if prompted.");
            }
            catch (Exception ex)
            {
                Log($"Login error: {ex.Message}");
            }
        }

        private async Task SyncCookiesAndHeaders()
        {
            Log("Syncing cookies and headers...");
            var cookieManager = webView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://www.instagram.com");
            string cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.instagram.com/");
            httpClient.DefaultRequestHeaders.Add("Accept", "video/webm,video/mp4,video/x-matroska,video/3gpp; q=1.0, */*; q=0.5");
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            try
            {
                Log("Starting reel download test with 20 strategies...");

                string reelUrl = "https://www.instagram.com/reel/DPuCOUcAINK/";
                string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "reel_DPuCOUcAINK.mp4");

                // Navigate to the reel page
                Log($"Navigating to {reelUrl}");
                webView.CoreWebView2.Navigate(reelUrl);
                await Task.Delay(5000, token);  // Wait for page load

                Log("Triggering video playback to load resources...");
                string playScript = "document.querySelector('video')?.play();";
                await ExecJsAsync(playScript, token);
                await Task.Delay(3000, token);  // Wait for network request to complete

                await SyncCookiesAndHeaders();

                // Check if login is required
                string loginCheck = @"document.querySelector('input[name=""username""]') !== null || document.body.innerText.includes('Log in');";
                string isLoginNeeded = await ExecJsAsync(loginCheck, token);
                if (bool.TryParse(isLoginNeeded, out bool needed) && needed)
                {
                    var username = Microsoft.VisualBasic.Interaction.InputBox("Enter IG username:", "Login Required", "");
                    var password = Microsoft.VisualBasic.Interaction.InputBox("Enter IG password:", "Login Required", "");
                    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    {
                        Log("Login canceled.");
                        return;
                    }
                    await AutomateLogin(username, password, token);
                    await Task.Delay(5000, token);  // Wait for post-login redirect
                    await SyncCookiesAndHeaders();  // Resync after login
                }

                string videoUrl = null;
                int strategyCount = 1;

                // Strategy 1: Direct <video> src
                Log($"Trying strategy {strategyCount}: Direct video tag src");
                videoUrl = await ExecJsAsync("document.querySelector('video')?.src || null;", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 2: First video tag with .mp4
                Log($"Trying strategy {strategyCount}: First video with .mp4 src");
                videoUrl = await ExecJsAsync("Array.from(document.querySelectorAll('video[src]')).find(v => v.src.includes('.mp4'))?.src || null;", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 3: Video in reel container
                Log($"Trying strategy {strategyCount}: Video in reel container");
                videoUrl = await ExecJsAsync("document.querySelector('div[data-reel] video')?.src || null;", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 4: Video with playback controls
                Log($"Trying strategy {strategyCount}: Video with playback controls");
                videoUrl = await ExecJsAsync("document.querySelector('video[playsinline]')?.src || null;", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 5: All videos and pick longest src
                Log($"Trying strategy {strategyCount}: Longest video src");
                videoUrl = await ExecJsAsync(@"
                    Array.from(document.querySelectorAll('video[src]'))
                    .map(v => v.src)
                    .filter(src => src.includes('.mp4'))
                    .sort((a,b) => b.length - a.length)[0] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 6: JSON in first script
                Log($"Trying strategy {strategyCount}: video_url in first JSON script");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        let script = document.querySelector('script[type=""application/json""]');
                        if (script) {
                            let data = JSON.parse(script.textContent);
                            let url = JSON.stringify(data).match(/""video_url"":""([^""]+)""/)?.[1];
                            return url ? decodeURIComponent(url.replace(/\\u0026/g, '&')) : null;
                        }
                        return null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 7: Deep search for video_url in all scripts
                Log($"Trying strategy {strategyCount}: Deep video_url in all JSON");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        let scripts = document.querySelectorAll('script[type=""application/json""]');
                        for (let s of scripts) {
                            try {
                                let data = JSON.parse(s.textContent);
                                function deepFind(obj) {
                                    if (obj && obj.video_url) return obj.video_url;
                                    for (let k in obj) {
                                        let found = deepFind(obj[k]);
                                        if (found) return found;
                                    }
                                }
                                let url = deepFind(data);
                                if (url) return decodeURIComponent(url.replace(/\\u0026/g, '&'));
                            } catch {}
                        }
                        return null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 8: Search for playback_url
                Log($"Trying strategy {strategyCount}: playback_url in JSON");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        let scripts = document.querySelectorAll('script[type=""application/json""]');
                        for (let s of scripts) {
                            let text = s.textContent;
                            let match = text.match(/""playback_url"":""([^""]+)""/);
                            if (match) return decodeURIComponent(match[1].replace(/\\u0026/g, '&'));
                        }
                        return null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 9: video_versions array
                Log($"Trying strategy {strategyCount}: video_versions URL");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        let scripts = document.querySelectorAll('script[type=""application/json""]');
                        for (let s of scripts) {
                            let text = s.textContent;
                            let match = text.match(/""video_versions""\s*:\s*\[\s*{\s*""url"":""([^""]+)""/);
                            if (match) return decodeURIComponent(match[1].replace(/\\u0026/g, '&'));
                        }
                        return null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 10: og:video meta tag (fixed)
                Log($"Trying strategy {strategyCount}: og:video meta tag");
                videoUrl = await ExecJsAsync("document.querySelector('meta[property=\\'og:video\\']')?.content || null;", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 11: Performance API - latest .mp4
                Log($"Trying strategy {strategyCount}: Performance API latest .mp4");
                videoUrl = await ExecJsAsync(@"
                    performance.getEntriesByType('resource')
                    .filter(r => r.name.includes('.mp4') && (r.name.includes('cdninstagram') || r.name.includes('fbcdn')))
                    .map(r => r.name)
                    .pop() || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 12: Performance API - largest file
                Log($"Trying strategy {strategyCount}: Performance API largest .mp4");
                videoUrl = await ExecJsAsync(@"
                    performance.getEntriesByType('resource')
                    .filter(r => r.name.includes('.mp4') && r.transferSize > 100000)
                    .sort((a,b) => b.transferSize - a.transferSize)
                    .map(r => r.name)[0] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 13: Performance API - video initiator
                Log($"Trying strategy {strategyCount}: Performance API video initiator");
                videoUrl = await ExecJsAsync(@"
                    performance.getEntriesByType('resource')
                    .filter(r => r.initiatorType === 'video' && r.name.includes('.mp4'))
                    .map(r => r.name)[0] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 14: Performance API - cdn filter
                Log($"Trying strategy {strategyCount}: Performance API cdn filter");
                videoUrl = await ExecJsAsync(@"
                    performance.getEntriesByType('resource')
                    .filter(r => (r.name.includes('cdninstagram') || r.name.includes('fbcdn')) && r.name.endsWith('.mp4'))
                    .map(r => r.name)[0] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 15: Performance API - all mp4, pick first
                Log($"Trying strategy {strategyCount}: Performance API first mp4");
                videoUrl = await ExecJsAsync(@"
                    performance.getEntriesByType('resource')
                    .find(r => r.name.endsWith('.mp4'))?.name || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 16: React internals deep search
                Log($"Trying strategy {strategyCount}: React internals video_url");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        function findReact(node) {
                            for (let key in node) if (key.startsWith('__react')) return node[key];
                            return null;
                        }
                        let root = document.body;
                        let internals = findReact(root);
                        function deepSearch(obj) {
                            if (obj && obj.video_url) return obj.video_url;
                            for (let k in obj) {
                                let found = deepSearch(obj[k]);
                                if (found) return found;
                            }
                            return null;
                        }
                        return deepSearch(internals) || null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 17: window._sharedData
                Log($"Trying strategy {strategyCount}: window._sharedData video_url");
                videoUrl = await ExecJsAsync("JSON.stringify(window._sharedData || {})", token);
                if (!string.IsNullOrEmpty(videoUrl) && videoUrl != "{}")
                {
                    var data = JObject.Parse(videoUrl);
                    videoUrl = data.SelectToken("$..video_url")?.ToString();
                    if (await TryDownload(videoUrl, savePath, token))
                    {
                        Log($"Strategy {strategyCount}: Success");
                        return;
                    }
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 18: window.__initialState
                Log($"Trying strategy {strategyCount}: window.__initialState video_url");
                videoUrl = await ExecJsAsync("JSON.stringify(window.__initialState || {})", token);
                if (!string.IsNullOrEmpty(videoUrl) && videoUrl != "{}")
                {
                    var data = JObject.Parse(videoUrl);
                    videoUrl = data.SelectToken("$..video_url")?.ToString();
                    if (await TryDownload(videoUrl, savePath, token))
                    {
                        Log($"Strategy {strategyCount}: Success");
                        return;
                    }
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 19: Regex on entire HTML for video_url
                Log($"Trying strategy {strategyCount}: Regex HTML video_url");
                videoUrl = await ExecJsAsync(@"
                    document.documentElement.outerHTML.match(/""video_url"":""([^""]+)""/)?.[1] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 20: Regex on HTML for .mp4 URLs
                Log($"Trying strategy {strategyCount}: Regex HTML all .mp4");
                videoUrl = await ExecJsAsync(@"
                    document.documentElement.outerHTML.match(/https?:\/\/[^""]+\.mp4[^""]*/g)?.[0] || null;
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                // Strategy 21: window.__additionalData video_url
                Log($"Trying strategy {strategyCount}: window.__additionalData video_url");
                videoUrl = await ExecJsAsync(@"
                    (function() {
                        if (window.__additionalData) {
                            let data = window.__additionalData;
                            function deepFind(obj, key) {
                                if (obj && obj[key]) return obj[key];
                                for (let k in obj) {
                                    let found = deepFind(obj[k], key);
                                    if (found) return found;
                                }
                            }
                            let url = deepFind(data, 'video_url') || deepFind(data, 'src');
                            if (url) return decodeURIComponent(url.replace(/\\u0026/g, '&'));
                        }
                        return null;
                    })();
                ", token);
                if (await TryDownload(videoUrl, savePath, token))
                {
                    Log($"Strategy {strategyCount}: Success");
                    return;
                }
                Log($"Strategy {strategyCount}: Failed - No valid URL");
                strategyCount++;

                Log("All strategies tried, no valid URL found.");
            }
            catch (TaskCanceledException)
            {
                Log("Download canceled.");
            }
            catch (Exception ex)
            {
                Log($"Error during download: {ex.Message}");
            }
        }

        private async Task<bool> TryDownload(string videoUrl, string savePath, CancellationToken token, int retries = 2)
        {
            if (string.IsNullOrEmpty(videoUrl) || videoUrl == "null" || !videoUrl.Contains(".mp4")) return false;

            // Minimal cleaning: Only remove byte ranges if present
            var uri = new Uri(videoUrl);
            var query = HttpUtility.ParseQueryString(uri.Query);
            query.Remove("bytestart");
            query.Remove("byteend");
            videoUrl = new UriBuilder(uri) { Query = query.ToString() }.Uri.ToString();

            for (int attempt = 1; attempt <= retries; attempt++)
            {
                try
                {
                    using var response = await httpClient.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    Log($"Attempt {attempt}: HTTP {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        long? contentLength = response.Content.Headers.ContentLength;
                        if (contentLength < 1024 * 50)
                        {
                            Log("Skipped: File too small (thumbnail?)");
                            return false;
                        }

                        using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await response.Content.CopyToAsync(fileStream, token);
                        Log($"Reel downloaded successfully to {savePath}");
                        Log($"File size: {new FileInfo(savePath).Length / 1024} KB");
                        return true;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Log("403 Forbidden - Check params, cookies, or if login required.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Download error on attempt {attempt}: {ex.Message}");
                }
                await Task.Delay(2000, token);  // Backoff
            }
            return false;
        }
    }
}