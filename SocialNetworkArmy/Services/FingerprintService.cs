// Services/FingerprintService.cs
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
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };

            var resolutions = new[] { "1920x1080", "1366x768" };
            var timezones = new[] { "America/New_York", "Europe/Paris" };

            var baseFonts = new[] { "Arial", "Times New Roman" };
            var fonts = baseFonts.Concat(Enumerable.Range(0, rand.Next(5, 8)).Select(_ => baseFonts[rand.Next(baseFonts.Length)])).ToList();

            var vendors = new[] { "NVIDIA Corporation", "Intel Inc." };
            var renderers = new[] { "NVIDIA GeForce RTX 3060", "Intel UHD Graphics 630" };

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
                HardwareConcurrency = rand.Next(4, 17),
                Platform = platforms[rand.Next(platforms.Length)],
                Vendor = vendorsUA[rand.Next(vendorsUA.Length)],
                ScreenDepth = rand.Next(2) == 0 ? 24 : 32,
                MaxTouchPoints = 0
            };
        }

        public string GenerateJSSpoof(Fingerprint fingerprint)
        {
            var languagesJson = JsonConvert.SerializeObject(fingerprint.Languages ?? new List<string>());
            var fontsJson = JsonConvert.SerializeObject(fingerprint.Fonts ?? new List<string>());

            return $@"
                Object.defineProperty(navigator, 'userAgent', {{ get: () => '{fingerprint.UserAgent}' }});
                Object.defineProperty(navigator, 'platform', {{ get: () => '{fingerprint.Platform}' }});
                Object.defineProperty(navigator, 'vendor', {{ get: () => '{fingerprint.Vendor ?? ""}' }});
                Object.defineProperty(navigator, 'languages', {{ get: () => {languagesJson} }});
                Object.defineProperty(navigator, 'hardwareConcurrency', {{ get: () => {fingerprint.HardwareConcurrency} }});
                Object.defineProperty(navigator, 'maxTouchPoints', {{ get: () => {fingerprint.MaxTouchPoints} }});
                Object.defineProperty(screen, 'depth', {{ get: () => {fingerprint.ScreenDepth} }});

                const originalDateTimeFormat = Intl.DateTimeFormat;
                Intl.DateTimeFormat = function() {{
                    const dtf = new originalDateTimeFormat(...arguments);
                    dtf.resolvedOptions = () => ({{
                        ...dtf.resolvedOptions(),
                        timeZone: '{fingerprint.Timezone}'
                    }});
                    return dtf;
                }};

                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {{
                    if (parameter === 37445) return '{fingerprint.WebGLVendor}';
                    if (parameter === 37446) return '{fingerprint.WebGLRenderer}';
                    return getParameter.call(this, parameter);
                }};

                const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
                HTMLCanvasElement.prototype.toDataURL = function(...args) {{
                    const ctx = this.getContext('2d');
                    if (ctx) {{
                        const imageData = ctx.getImageData(0, 0, this.width, this.height);
                        for (let i = 0; i < imageData.data.length; i += 4) {{
                            imageData.data[i] += Math.random() * 0.0001 * 255;
                        }}
                        ctx.putImageData(imageData, 0, 0);
                    }}
                    return originalToDataURL.apply(this, args);
                }};

                Object.defineProperty(navigator, 'fonts', {{
                    get: () => ({{
                        size: {fingerprint.Fonts.Count},
                        families: {fontsJson}
                    }})
                }});

                Object.defineProperty(navigator, 'webdriver', {{ get: () => undefined }});
                window.RTCPeerConnection = undefined;

                const [width, height] = '{fingerprint.ScreenResolution}'.split('x').map(Number);
                Object.defineProperty(screen, 'width', {{ get: () => width }});
                Object.defineProperty(screen, 'height', {{ get: () => height }});
            ";
        }
    }
}