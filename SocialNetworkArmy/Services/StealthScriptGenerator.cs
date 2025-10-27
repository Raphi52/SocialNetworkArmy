using System;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Génère des scripts JavaScript anti-détection pour contourner les protections des sites web.
    /// 28 couches de protection incluant fingerprinting, canvas, WebGL, audio, etc.
    /// </summary>
    public static class StealthScriptGenerator
    {
        public static string GenerateScript()
        {
            var rand = new Random();

            // ✅ EXPANDED: Plus de résolutions réalistes
            var resolutions = new[] {
                new { w = 1920, h = 1080 },  // Full HD (most common)
                new { w = 1366, h = 768 },   // Laptop standard
                new { w = 1536, h = 864 },   // Laptop HD
                new { w = 1440, h = 900 },   // 16:10
                new { w = 2560, h = 1440 },  // 2K
                new { w = 1600, h = 900 },   // HD+
                new { w = 1280, h = 720 },   // HD
                new { w = 1280, h = 1024 },  // 5:4
                new { w = 2048, h = 1152 },  // QWXGA
                new { w = 3840, h = 2160 }   // 4K (rare but realistic)
            };
            var res = resolutions[rand.Next(resolutions.Length)];

            // ✅ Randomiser le hardware de manière réaliste
            var cores = new[] { 4, 6, 8, 12, 16 };
            var ram = new[] { 4, 8, 16, 32 };
            var hardwareConcurrency = cores[rand.Next(cores.Length)];
            var deviceMemory = ram[rand.Next(ram.Length)];

            // ✅ COHERENT WebGL: GPU doit matcher le CPU
            string webglVendor, webglRenderer;
            var gpuType = rand.Next(3); // 0=Intel, 1=NVIDIA, 2=AMD

            if (gpuType == 0) // Intel (most common for laptops)
            {
                webglVendor = "Intel Inc.";
                var intelGPUs = new[] {
                    "Intel(R) UHD Graphics 620",
                    "Intel(R) UHD Graphics 630",
                    "Intel(R) Iris(R) Xe Graphics",
                    "ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)"
                };
                webglRenderer = intelGPUs[rand.Next(intelGPUs.Length)];
            }
            else if (gpuType == 1) // NVIDIA (gaming/workstation)
            {
                webglVendor = "NVIDIA Corporation";
                var nvidiaGPUs = new[] {
                    "NVIDIA GeForce GTX 1650",
                    "NVIDIA GeForce GTX 1660 Ti",
                    "NVIDIA GeForce RTX 3060",
                    "NVIDIA GeForce RTX 3070",
                    "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0)"
                };
                webglRenderer = nvidiaGPUs[rand.Next(nvidiaGPUs.Length)];
            }
            else // AMD
            {
                webglVendor = "ATI Technologies Inc.";
                var amdGPUs = new[] {
                    "AMD Radeon RX 580 Series",
                    "AMD Radeon RX 6600",
                    "AMD Radeon(TM) Graphics",
                    "ANGLE (AMD, AMD Radeon(TM) Graphics Direct3D11 vs_5_0 ps_5_0)"
                };
                webglRenderer = amdGPUs[rand.Next(amdGPUs.Length)];
            }

            // ✅ VARIED Plugins: 3-5 plugins avec variation
            var hasFlashPlugin = rand.NextDouble() < 0.1; // 10% ont encore Flash
            var hasSilverlightPlugin = rand.NextDouble() < 0.05; // 5% ont Silverlight
            var pluginsList = new System.Collections.Generic.List<string>
            {
                @"{ 0: {type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format'}, description: 'Portable Document Format', filename: 'internal-pdf-viewer', length: 1, name: 'Chrome PDF Plugin' }",
                @"{ 0: {type: 'application/pdf', suffixes: 'pdf', description: ''}, description: '', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai', length: 1, name: 'Chrome PDF Viewer' }",
                @"{ 0: {type: 'application/x-nacl', suffixes: '', description: 'Native Client Executable'}, 1: {type: 'application/x-pnacl', suffixes: '', description: 'Portable Native Client Executable'}, description: '', filename: 'internal-nacl-plugin', length: 2, name: 'Native Client' }"
            };

            if (hasFlashPlugin)
                pluginsList.Add(@"{ 0: {type: 'application/x-shockwave-flash', suffixes: 'swf', description: 'Shockwave Flash'}, description: 'Shockwave Flash', filename: 'pepflashplayer.dll', length: 1, name: 'Shockwave Flash' }");

            if (hasSilverlightPlugin)
                pluginsList.Add(@"{ 0: {type: 'application/x-silverlight', suffixes: 'xap', description: 'Silverlight'}, description: 'Silverlight Plug-In', filename: 'npctrl.dll', length: 1, name: 'Silverlight Plug-In' }");

            var pluginsJs = string.Join(",\n            ", pluginsList);

            // ✅ LOCALIZED Languages: Adapter selon géolocalisation (basic implementation)
            var languageProfiles = new[] {
                new { langs = new[] { "en-US", "en" }, primary = "en-US" },                    // USA
                new { langs = new[] { "en-GB", "en" }, primary = "en-GB" },                    // UK
                new { langs = new[] { "fr-FR", "fr", "en-US" }, primary = "fr-FR" },          // France
                new { langs = new[] { "de-DE", "de", "en" }, primary = "de-DE" },             // Germany
                new { langs = new[] { "es-ES", "es", "en" }, primary = "es-ES" },             // Spain
                new { langs = new[] { "pt-BR", "pt", "en" }, primary = "pt-BR" },             // Brazil
                new { langs = new[] { "ja-JP", "ja", "en" }, primary = "ja-JP" },             // Japan
                new { langs = new[] { "zh-CN", "zh", "en" }, primary = "zh-CN" },             // China
                new { langs = new[] { "en-CA", "en", "fr-CA" }, primary = "en-CA" },          // Canada
                new { langs = new[] { "it-IT", "it", "en" }, primary = "it-IT" }              // Italy
            };
            var langProfile = languageProfiles[rand.Next(languageProfiles.Length)];
            var languagesJs = string.Join(", ", langProfile.langs.Select(l => $"'{l}'"));
            var primaryLanguage = langProfile.primary;

            // ✅ Randomiser la connection (avec variation réaliste)
            var rtt = rand.Next(20, 100);           // 20-100ms latency
            var downlink = rand.Next(5, 50);        // 5-50 Mbps

            // ✅ Batterie réaliste
            var batteryCharging = rand.NextDouble() > 0.3;  // 70% branché, 30% sur batterie
            var batteryLevel = batteryCharging
                ? 0.8 + rand.NextDouble() * 0.2    // Si branché: 80-100%
                : 0.2 + rand.NextDouble() * 0.7;   // Si sur batterie: 20-90%

            // ✅ DeviceId stable (basé sur un hash, pas un GUID aléatoire)
            var deviceIdSeed = Environment.MachineName + Environment.UserName;
            var stableDeviceId = Math.Abs(deviceIdSeed.GetHashCode()).ToString("x");

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

    // ========== 3. PLUGINS (VARIÉS) ==========
    Object.defineProperty(navigator, 'plugins', {{
        get: () => [
            {pluginsJs}
        ],
        configurable: true
    }});

    // ========== 4. LANGUAGES (LOCALISÉES) ==========
    Object.defineProperty(navigator, 'languages', {{
        get: () => [{languagesJs}],
        configurable: true
    }});

    Object.defineProperty(navigator, 'language', {{
        get: () => '{primaryLanguage}',
        configurable: true
    }});

    // ========== 5. PERMISSIONS (FIXÉ) ==========
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

    // ========== 6. CONNECTION (RANDOMISÉ) ==========
    Object.defineProperty(navigator, 'connection', {{
        get: () => ({{
            effectiveType: '4g',
            rtt: {rtt},
            downlink: {downlink},
            saveData: false,
            onchange: null,
            downlinkMax: Infinity,
            type: 'wifi'
        }}),
        configurable: true
    }});

    // ========== 7. HARDWARE (RANDOMISÉ) ==========
    Object.defineProperty(navigator, 'hardwareConcurrency', {{
        get: () => {hardwareConcurrency},
        configurable: true
    }});

    Object.defineProperty(navigator, 'deviceMemory', {{
        get: () => {deviceMemory},
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

    // ========== 10. WEBGL FINGERPRINT (COHÉRENT AVEC CPU/GPU) ==========
    const getParameterProxyHandler = {{
        apply: function(target, thisArg, args) {{
            const param = args[0];
            const result = Reflect.apply(target, thisArg, args);

            if (param === 37445) {{ // UNMASKED_VENDOR_WEBGL
                return '{webglVendor}';
            }}
            if (param === 37446) {{ // UNMASKED_RENDERER_WEBGL
                return '{webglRenderer}';
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

    // ========== 11. BATTERY STATUS (RANDOMISÉ) ==========
    if (navigator.getBattery) {{
        const originalGetBattery = navigator.getBattery;
        navigator.getBattery = function() {{
            return originalGetBattery().then(battery => {{
                Object.defineProperties(battery, {{
                    charging: {{ get: () => {batteryCharging.ToString().ToLower()} }},
                    chargingTime: {{ get: () => {(batteryCharging ? "0" : "Infinity")} }},
                    dischargingTime: {{ get: () => {(batteryCharging ? "Infinity" : "7200")} }},
                    level: {{ get: () => {batteryLevel:F2} }}
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

    // ========== 14. MEDIA DEVICES (NOUVEAU) ✅ ==========
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
                        deviceId: '{stableDeviceId}',
                        kind: 'videoinput',
                        label: 'Integrated Camera (04f2:b6dd)',
                        groupId: '{stableDeviceId}'
                    }}
                ];
            }});
        }};
    }}

    // ========== 15. POINTER EVENTS (NOUVEAU) ✅ ==========
    Object.defineProperty(navigator, 'maxTouchPoints', {{
        get: () => 0,
        configurable: true
    }});

    // ========== 16. VENDOR & PRODUCT (NOUVEAU) ✅ ==========
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

    // ========== 17. AUDIO CONTEXT FINGERPRINT (NOUVEAU) ✅ ==========
    const audioContext = window.AudioContext || window.webkitAudioContext;
    if (audioContext) {{
        const originalCreateOscillator = audioContext.prototype.createOscillator;
        audioContext.prototype.createOscillator = function() {{
            const oscillator = originalCreateOscillator.apply(this, arguments);
            const originalStart = oscillator.start;
            oscillator.start = function() {{
                const noise = Math.random() * 0.0001;
                if (oscillator.frequency) {{
                    oscillator.frequency.value = oscillator.frequency.value + noise;
                }}
                return originalStart.apply(this, arguments);
            }};
            return oscillator;
        }};
    }}

    // ========== 18. SPEECH SYNTHESIS (NOUVEAU) ✅ ==========
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

    // ========== 19. HEADLESS DETECTION BYPASS (NOUVEAU) ✅ ==========
    Object.defineProperty(navigator, 'headless', {{
        get: () => false
    }});

    // ========== 20. AUTOMATION FLAGS CLEANUP (NOUVEAU) ✅ ==========
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_JSON;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Object;
    delete window.cdc_adoQpoasnfa76pfcZLmcfl_Proxy;

    // ========== 21. OBJECT.GETOWNPROPERTYDESCRIPTOR OVERRIDE (NOUVEAU) ✅ ==========
    const originalGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
    Object.getOwnPropertyDescriptor = function(obj, prop) {{
        if (prop === 'webdriver') {{
            return undefined;
        }}
        return originalGetOwnPropertyDescriptor(obj, prop);
    }};

    // ========== 22. FUNCTION TOSTRING PROTECTION (NOUVEAU) ✅ ==========
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

    // ========== 23. IFRAME DETECTION BYPASS (NOUVEAU) ✅ ==========
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

    // ========== 24. PERFORMANCE TIMING RANDOMIZATION (NOUVEAU) ✅ ==========
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

    // ========== 25. USER ACTIVATION (NOUVEAU) ✅ ==========
    if (navigator.userActivation) {{
        Object.defineProperty(navigator.userActivation, 'hasBeenActive', {{
            get: () => true
        }});
        Object.defineProperty(navigator.userActivation, 'isActive', {{
            get: () => true
        }});
    }}

    // ========== 26. CLIPBOARD API (NOUVEAU) ✅ ==========
    if (navigator.clipboard) {{
        const originalReadText = navigator.clipboard.readText;
        navigator.clipboard.readText = function() {{
            return Promise.reject(new DOMException('Access denied', 'NotAllowedError'));
        }};
    }}

    // ========== 27. WEB WORKERS PROTECTION (NOUVEAU) ✅ ==========
    const originalWorker = window.Worker;
    window.Worker = class extends originalWorker {{
        constructor(...args) {{
            super(...args);
        }}
    }};

    // ========== 28. CONSOLE LOG PROTECTION (NOUVEAU) ✅ ==========
    const originalConsoleLog = console.log;
    console.log = function(...args) {{
        const message = args.join(' ');
        if (message.includes('webdriver') ||
            message.includes('automation') ||
            message.includes('headless') ||
            message.includes('stealth')) {{
            return;
        }}
        return originalConsoleLog.apply(console, args);
    }};

    // ✅ REMOVED: Ne JAMAIS logger que le stealth est actif!
    // console.log('🛡️ Instagram ultra-stealth mode active (28 layers)');
}})();
";
        }
    }
}
