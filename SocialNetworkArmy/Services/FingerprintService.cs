// Services/FingerprintService.cs - Mise à jour avec audio et plugins spoof améliorés
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class FingerprintService
    {
        private static readonly Random rand = new Random();

        public Fingerprint GenerateDesktopFingerprint()
        {
            // ✅ AMÉLIORATION: Plus de User-Agents (versions récentes Chrome 120-131)
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            };

            // ✅ AMÉLIORATION: Plus de résolutions courantes
            var resolutions = new[]
            {
                "1920x1080", // 23%
                "1366x768",  // 14%
                "1536x864",  // 9%
                "1440x900",  // 8%
                "2560x1440", // 7%
                "1600x900",  // 5%
                "1280x1024", // 4%
                "2560x1080", // Ultrawide
                "3440x1440", // Ultrawide
                "1680x1050",
                "1280x720"
            };

            // ✅ AMÉLIORATION: Plus de timezones réalistes
            var timezones = new[]
            {
                "Europe/Paris",
                "Europe/London",
                "Europe/Berlin",
                "Europe/Madrid",
                "Europe/Rome",
                "America/New_York",
                "America/Chicago",
                "America/Los_Angeles",
                "America/Toronto",
                "Asia/Tokyo",
                "Asia/Shanghai",
                "Australia/Sydney"
            };

            var baseFonts = new[] { "Arial", "Times New Roman", "Helvetica", "Courier New", "Verdana", "Georgia", "Calibri", "Segoe UI" };
            var fonts = baseFonts
                .Concat(Enumerable.Range(0, rand.Next(8, 15)).Select(_ => baseFonts[rand.Next(baseFonts.Length)]))
                .Distinct()
                .ToList();

            // ✅ AMÉLIORATION: Plus de GPUs réalistes
            var gpuConfigs = new[]
            {
                new { vendor = "NVIDIA Corporation", renderer = "NVIDIA GeForce RTX 3060" },
                new { vendor = "NVIDIA Corporation", renderer = "NVIDIA GeForce RTX 3070" },
                new { vendor = "NVIDIA Corporation", renderer = "NVIDIA GeForce RTX 4060" },
                new { vendor = "NVIDIA Corporation", renderer = "NVIDIA GeForce RTX 4070" },
                new { vendor = "NVIDIA Corporation", renderer = "NVIDIA GeForce GTX 1660" },
                new { vendor = "Intel Inc.", renderer = "Intel UHD Graphics 630" },
                new { vendor = "Intel Inc.", renderer = "Intel Iris Xe Graphics" },
                new { vendor = "Google Inc.", renderer = "ANGLE (Intel, Intel(R) UHD Graphics 630, D3D11)" },
                new { vendor = "Google Inc.", renderer = "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060, D3D11)" },
                new { vendor = "Apple", renderer = "Apple M1" },
                new { vendor = "Apple", renderer = "Apple M2" },
                new { vendor = "AMD", renderer = "AMD Radeon RX 6700 XT" },
            };

            var gpu = gpuConfigs[rand.Next(gpuConfigs.Length)];

            var platforms = new[] { "Win32", "MacIntel" };
            var vendorsUA = new[] { "Google Inc.", "Apple Computer, Inc.", "" };

            var languages = new List<string> { "en-US", "en" };

            // ✅ Calculer viewport basé sur résolution (réaliste)
            var selectedRes = resolutions[rand.Next(resolutions.Length)];
            var resParts = selectedRes.Split('x');
            int width = int.Parse(resParts[0]);
            int height = int.Parse(resParts[1]);
            string viewport = $"{width}x{height - rand.Next(60, 120)}"; // Soustraire barre d'outils/favoris

            return new Fingerprint
            {
                UserAgent = userAgents[rand.Next(userAgents.Length)],
                Timezone = timezones[rand.Next(timezones.Length)],
                Languages = languages,
                ScreenResolution = selectedRes,
                Viewport = viewport,
                WebGLVendor = gpu.vendor,
                WebGLRenderer = gpu.renderer,
                AudioContext = $"noise_{rand.Next(10000, 99999)}",
                Fonts = fonts,
                HardwareConcurrency = rand.Next(4, 17), // 4-16 cores
                Platform = platforms[rand.Next(platforms.Length)],
                Vendor = vendorsUA[rand.Next(vendorsUA.Length)],
                ScreenDepth = rand.NextDouble() < 0.85 ? 24 : 32, // 85% ont 24-bit
                MaxTouchPoints = 0,
                Plugins = GeneratePlugins()
            };
        }

        // ✅ NOUVEAU: Fingerprint pour appareils mobiles (Story posting)
        public Fingerprint GenerateMobileFingerprint()
        {
            // ✅ User-Agents mobiles réalistes (Android Chrome récents)
            var userAgents = new[]
            {
                "Mozilla/5.0 (Linux; Android 14; SM-S911B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 13; Pixel 7 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 14; SM-G998B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 13; OnePlus 11) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (iPhone; CPU iPhone OS 17_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/130.0.6723.37 Mobile/15E148 Safari/604.1",
                "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0.6778.73 Mobile/15E148 Safari/604.1",
                "Mozilla/5.0 (Linux; Android 14; SM-A546B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36",
                "Mozilla/5.0 (Linux; Android 13; Redmi Note 12 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
            };

            // ✅ Résolutions mobiles courantes
            var resolutions = new[]
            {
                "360x800",   // Samsung Galaxy A series
                "375x812",   // iPhone X/XS/11 Pro
                "390x844",   // iPhone 12/13/14
                "393x852",   // Pixel 7
                "412x915",   // Samsung Galaxy S21/S22
                "414x896",   // iPhone 11/XR
                "428x926",   // iPhone 12/13/14 Pro Max
                "360x780",   // Various Android
                "384x854",   // Various Android
                "411x823",   // Pixel 6
            };

            // ✅ Timezones
            var timezones = new[]
            {
                "Europe/Paris", "Europe/London", "Europe/Berlin", "Europe/Madrid", "Europe/Rome",
                "America/New_York", "America/Chicago", "America/Los_Angeles", "America/Toronto",
                "Asia/Tokyo", "Asia/Shanghai", "Australia/Sydney"
            };

            // ✅ GPUs mobiles réalistes
            var gpuConfigs = new[]
            {
                new { vendor = "Qualcomm", renderer = "Adreno (TM) 730" },
                new { vendor = "Qualcomm", renderer = "Adreno (TM) 740" },
                new { vendor = "ARM", renderer = "Mali-G78" },
                new { vendor = "ARM", renderer = "Mali-G710" },
                new { vendor = "Apple", renderer = "Apple A16 GPU" },
                new { vendor = "Apple", renderer = "Apple A17 Pro GPU" },
                new { vendor = "Qualcomm", renderer = "Adreno (TM) 650" },
                new { vendor = "ARM", renderer = "Mali-G77" },
            };

            var gpu = gpuConfigs[rand.Next(gpuConfigs.Length)];

            var platforms = new[] { "Linux armv81", "iPhone" };
            var vendorsUA = new[] { "Google Inc.", "Apple Computer, Inc." };

            var languages = new List<string> { "en-US", "en" };

            // ✅ Calculer viewport basé sur résolution mobile
            var selectedRes = resolutions[rand.Next(resolutions.Length)];
            var resParts = selectedRes.Split('x');
            int width = int.Parse(resParts[0]);
            int height = int.Parse(resParts[1]);
            string viewport = $"{width}x{height - rand.Next(40, 80)}"; // Soustraire barre URL

            // ✅ Fonts mobiles (moins que desktop)
            var baseFonts = new[] { "Arial", "Helvetica", "sans-serif", "Roboto", "San Francisco" };
            var fonts = baseFonts.Take(rand.Next(3, 6)).ToList();

            return new Fingerprint
            {
                UserAgent = userAgents[rand.Next(userAgents.Length)],
                Timezone = timezones[rand.Next(timezones.Length)],
                Languages = languages,
                ScreenResolution = selectedRes,
                Viewport = viewport,
                WebGLVendor = gpu.vendor,
                WebGLRenderer = gpu.renderer,
                AudioContext = $"noise_{rand.Next(10000, 99999)}",
                Fonts = fonts,
                HardwareConcurrency = rand.Next(4, 9), // Mobile: 4-8 cores
                Platform = platforms[rand.Next(platforms.Length)],
                Vendor = vendorsUA[rand.Next(vendorsUA.Length)],
                ScreenDepth = 24, // Mobile toujours 24-bit
                MaxTouchPoints = rand.Next(5, 11), // Mobile: 5-10 touch points
                Plugins = GenerateMobilePlugins()
            };
        }

        private List<object> GenerateMobilePlugins()
        {
            // Plugins mobiles (moins que desktop)
            return new List<object>
            {
                new { name = "Chrome PDF Viewer", filename = "internal-pdf-viewer", description = "Portable Document Format" },
                new { name = "PDF Viewer", filename = "mhjfbmdgcfjbbpaeojofohoefgiehjai", description = "PDF Viewer" }
            };
        }

        private List<object> GeneratePlugins()
        {
            // Liste simple mais valable ; on renvoie des objets pour sérialiser proprement en JS
            return new List<object>
            {
                new { name = "Chrome PDF Plugin", filename = "internal-pdf-viewer", description = "Portable Document Format" },
                new { name = "Chrome PDF Viewer", filename = "mhjfbmdgcfjbbpaeojofohoefgiehjai", description = "PDF Viewer" },
                new { name = "Native Client", filename = "internal-nacl-plugin", description = "Native Client Executable" }
            };
        }

        public string GenerateJSSpoof(Fingerprint fingerprint)
        {
            // Sérialisation sûre pour insertion dans un bloc JS
            string uaJs = JsonConvert.SerializeObject(fingerprint.UserAgent ?? "");
            string platformJs = JsonConvert.SerializeObject(fingerprint.Platform ?? "");
            string vendorJs = JsonConvert.SerializeObject(fingerprint.Vendor ?? "");
            string languagesJs = JsonConvert.SerializeObject(fingerprint.Languages ?? new List<string>());
            string fontsJs = JsonConvert.SerializeObject(fingerprint.Fonts ?? new List<string>());
            string pluginsJs = JsonConvert.SerializeObject(fingerprint.Plugins ?? new List<object>());
            string timezoneJs = JsonConvert.SerializeObject(fingerprint.Timezone ?? "");
            int hwConcurrency = fingerprint.HardwareConcurrency;
            int maxTouch = fingerprint.MaxTouchPoints;
            int screenDepth = fingerprint.ScreenDepth;
            string screenRes = JsonConvert.SerializeObject(fingerprint.ScreenResolution ?? "1920x1080");
            string webglVendor = JsonConvert.SerializeObject(fingerprint.WebGLVendor ?? "");
            string webglRenderer = JsonConvert.SerializeObject(fingerprint.WebGLRenderer ?? "");
            string audioLabel = JsonConvert.SerializeObject(fingerprint.AudioContext ?? $"noise_{rand.Next(1000, 9999)}");

            // JS: crée un PluginArray-like, ajoute de petits bruits et override audio/canvas/webgl props
            var js = $@"
(function() {{
    // ---------- Basic navigator overrides ----------
    Object.defineProperty(navigator, 'userAgent', {{ get: () => {uaJs} }});
    Object.defineProperty(navigator, 'platform', {{ get: () => {platformJs} }});
    Object.defineProperty(navigator, 'vendor', {{ get: () => {vendorJs} }});
    Object.defineProperty(navigator, 'languages', {{ get: () => {languagesJs} }});
    Object.defineProperty(navigator, 'hardwareConcurrency', {{ get: () => {hwConcurrency} }});
    Object.defineProperty(navigator, 'maxTouchPoints', {{ get: () => {maxTouch} }});
    Object.defineProperty(screen, 'depth', {{ get: () => {screenDepth} }});

    // Timezone via Intl override
    try {{
        const _Intl = Intl.DateTimeFormat;
        Intl.DateTimeFormat = function() {{
            const inst = new _Intl(...arguments);
            const ro = inst.resolvedOptions();
            const patched = function() {{ return Object.assign(ro, {{ timeZone: {timezoneJs} }}); }};
            inst.resolvedOptions = patched;
            return inst;
        }};
    }} catch(e){{ console.warn('tz spoof failed', e); }}

    // ---------- WebGL vendor/renderer spoof ----------
    (function() {{
        try {{
            const proto = WebGLRenderingContext && WebGLRenderingContext.prototype;
            if (proto) {{
                const origGetParameter = proto.getParameter;
                proto.getParameter = function(param) {{
                    if (param === 37445) return {webglVendor}; // UNMASKED_VENDOR_WEBGL
                    if (param === 37446) return {webglRenderer}; // UNMASKED_RENDERER_WEBGL
                    return origGetParameter.call(this, param);
                }};
            }}
        }} catch(e){{ console.warn('webgl spoof failed', e); }}
    }})();

    // ---------- Canvas fingerprint mitigation (deterministic tiny noise) ----------
    (function() {{
        const origToDataURL = HTMLCanvasElement.prototype.toDataURL;
        const seed = {audioLabel}.toString().split('').reduce((s,c)=>s + c.charCodeAt(0), 0) % 997;
        HTMLCanvasElement.prototype.toDataURL = function() {{
            try {{
                const ctx = this.getContext('2d');
                if (ctx && this.width && this.height) {{
                    const img = ctx.getImageData(0,0,Math.min(64,this.width), Math.min(64,this.height));
                    for (let i=0;i<img.data.length;i+=4) {{
                        // deterministic tiny change derived from seed (keeps jitter small)
                        img.data[i] = (img.data[i] + ((seed + i) % 3)) & 255;
                    }}
                    ctx.putImageData(img, 0, 0);
                }}
            }} catch(e){{/*ignore*/}}
            return origToDataURL.apply(this, arguments);
        }};
    }})();

    // ---------- AudioContext spoof: add tiny deterministic noise on analyser data ----------
    (function() {{
        try {{
            const OrigAudioCtx = window.AudioContext || window.webkitAudioContext;
            if (OrigAudioCtx) {{
                const OrigPrototype = OrigAudioCtx.prototype;
                const origGetChannelData = Float32Array.prototype.slice; // fallback not used but safe
                const seed = {audioLabel}.toString().split('').reduce((s,c)=>s + c.charCodeAt(0), 0) % 997;
                const ProxyAudioContext = function() {{
                    const ctx = new OrigAudioCtx(...arguments);
                    const origCreateAnalyser = ctx.createAnalyser;
                    ctx.createAnalyser = function() {{
                        const analyser = origCreateAnalyser.apply(this, arguments);
                        const origGetFloat = analyser.getFloatTimeDomainData.bind(analyser);
                        analyser.getFloatTimeDomainData = function(array) {{
                            try {{
                                origGetFloat(array);
                                for (let i = 0; i < array.length; i++) {{
                                    // very small deterministic noise
                                    array[i] += ((seed + i) % 5) * 1e-6;
                                }}
                            }} catch(e){{/*ignore*/}}
                            return array;
                        }};
                        return analyser;
                    }};
                    return ctx;
                }};
                ProxyAudioContext.prototype = OrigAudioCtx.prototype;
                window.AudioContext = window.webkitAudioContext = ProxyAudioContext;
            }}
        }} catch(e){{ console.warn('audio spoof failed', e); }}
    }})();

    // ---------- Plugins spoof: construct PluginArray-like object ----------
    (function() {{
        try {{
            const pluginsSource = {pluginsJs};
            const arr = Array.isArray(pluginsSource) ? pluginsSource.slice(0) : [];
            // Create plugin objects and PluginArray-like wrapper
            const pluginArray = {{
                length: arr.length,
                namedItem: function(name) {{
                    for (var i=0;i<arr.length;i++) if (arr[i] && arr[i].name === name) return arr[i];
                    return null;
                }},
                item: function(index) {{
                    return arr[index] || null;
                }}
            }};
            for (var i=0;i<arr.length;i++) pluginArray[i] = arr[i];

            // mimic toString and properties
            pluginArray.toString = function() {{ return '[object PluginArray]'; }};
            arr.forEach(function(p) {{
                if (p && !p.toString) p.toString = function() {{ return '[object Plugin]'; }};
            }});

            Object.defineProperty(navigator, 'plugins', {{
                get: function() {{ return pluginArray; }}
            }});

            // common mimeTypes spoof (minimal)
            const mimeTypes = [{{ type: 'application/pdf', suffixes: 'pdf', description: 'Portable Document Format' }}];
            const mimeArr = {{
                length: mimeTypes.length,
                item: function(i) {{ return mimeTypes[i] || null; }},
                namedItem: function(name) {{ return mimeTypes.find(m=>m.type===name) || null; }}
            }};
            Object.defineProperty(navigator, 'mimeTypes', {{
                get: function() {{ return mimeArr; }}
            }});
        }} catch(e){{ console.warn('plugins spoof failed', e); }}
    }})();

    // ---------- Misc & WebView2 Detection Masking ----------
    // ✅ Mask navigator.webdriver
    try {{ Object.defineProperty(navigator, 'webdriver', {{ get: () => undefined }}); }} catch(e){{}}

    // ✅ Mask window.chrome.webview (WebView2 specific)
    try {{
        if (window.chrome && window.chrome.webview) {{
            delete window.chrome.webview;
        }}
    }} catch(e){{}}

    // ✅ Mask automation detection properties
    try {{ delete window.__playwright; }} catch(e){{}}
    try {{ delete window.__puppeteer; }} catch(e){{}}
    try {{ delete window.__selenium; }} catch(e){{}}
    try {{ delete window.callPhantom; }} catch(e){{}}
    try {{ delete window._phantom; }} catch(e){{}}
    try {{ delete navigator.__proto__.webdriver; }} catch(e){{}}

    // ✅ Override chrome runtime (make it look real)
    try {{
        if (!window.chrome) {{
            window.chrome = {{}};
        }}
        if (!window.chrome.runtime) {{
            window.chrome.runtime = {{}};
        }}
    }} catch(e){{}}

    try {{ window.RTCPeerConnection = undefined; }} catch(e){{}}

    try {{
        const [w,h] = {screenRes}.replace(/['""]/g,'').split('x').map(Number);
        if (!isNaN(w)) Object.defineProperty(screen, 'width', {{ get: () => w }});
        if (!isNaN(h)) Object.defineProperty(screen, 'height', {{ get: () => h }});
    }} catch(e){{}}

    // Block direct devtools/cdp websocket attempts (non-intrusive)
    (function() {{
        const RealWS = window.WebSocket;
        window.WebSocket = function(url, protocols) {{
            try {{
                if (typeof url === 'string' && (url.indexOf('devtools') !== -1 || url.indexOf('cdp') !== -1)) {{
                    console.warn('Blocked suspicious websocket', url);
                    return {{ close: function(){{}} }};
                }}
            }} catch(e){{/*ignore*/}}
            return new RealWS(url, protocols);
        }};
        try {{ window.WebSocket.prototype = RealWS.prototype; }} catch(e){{/*ignore*/}}
    }})();

}})();
";

            return js;
        }
    }
}
