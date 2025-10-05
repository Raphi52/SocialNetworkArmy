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
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
            };

            var resolutions = new[] { "1920x1080", "1366x768", "1536x864" };
            var timezones = new[] { "Europe/Paris", "America/New_York", "Europe/London" };

            var baseFonts = new[] { "Arial", "Times New Roman", "Helvetica", "Courier New", "Verdana" };
            var fonts = baseFonts
                .Concat(Enumerable.Range(0, rand.Next(5, 8)).Select(_ => baseFonts[rand.Next(baseFonts.Length)]))
                .ToList();

            var vendors = new[] { "NVIDIA Corporation", "Intel Inc.", "Google Inc." };
            var renderers = new[] { "NVIDIA GeForce RTX 3060", "Intel UHD Graphics 630", "ANGLE (Intel, Intel(R) UHD Graphics 630, D3D11)" };

            var platforms = new[] { "Win32", "MacIntel" };
            var vendorsUA = new[] { "Google Inc.", "Apple Computer, Inc." };

            var languages = new List<string> { "en-US", "en" };

            return new Fingerprint
            {
                UserAgent = userAgents[rand.Next(userAgents.Length)],
                Timezone = timezones[rand.Next(timezones.Length)],
                Languages = languages,
                ScreenResolution = resolutions[rand.Next(resolutions.Length)],
                Viewport = "1920x1017",
                WebGLVendor = vendors[rand.Next(vendors.Length)],
                WebGLRenderer = renderers[rand.Next(renderers.Length)],
                AudioContext = $"noise_{rand.Next(1000, 9999)}",
                Fonts = fonts,
                HardwareConcurrency = rand.Next(4, 12),
                Platform = platforms[rand.Next(platforms.Length)],
                Vendor = vendorsUA[rand.Next(vendorsUA.Length)],
                ScreenDepth = rand.Next(2) == 0 ? 24 : 32,
                MaxTouchPoints = 0,
                Plugins = GeneratePlugins()
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

    // ---------- Misc ----------
    try {{ Object.defineProperty(navigator, 'webdriver', {{ get: () => undefined }}); }} catch(e){{}}
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
