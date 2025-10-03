// Services/MonitoringService.cs - Monitoring et tests
using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Utils;

namespace SocialNetworkArmy.Services
{
    public class MonitoringService
    {
        public async Task<string> TestFingerprintAsync(WebView2 webView)
        {
            // Test simple avec Canvas + Audio (simule CreepJS)
            var testScript = @"
                const canvas = document.createElement('canvas');
                const ctx = canvas.getContext('2d');
                ctx.textBaseline = 'top';
                ctx.font = '14px Arial';
                ctx.fillText('Test fingerprint', 2, 2);
                const canvasData = canvas.toDataURL(); // Avec noise si spoof OK

                const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                const oscillator = audioCtx.createOscillator();
                oscillator.type = 'sine';
                oscillator.frequency.setValueAtTime(440 + Math.random() * 10, audioCtx.currentTime); // Bruit
                const analyser = audioCtx.createAnalyser();
                oscillator.connect(analyser);
                const buffer = new Uint8Array(analyser.frequencyBinCount);
                analyser.getByteFrequencyData(buffer);
                const audioHash = btoa(String.fromCharCode(...buffer)).substring(0, 20);

                const score = canvasData.length > 100 && audioHash.length > 0 ? 'Unique (Low Risk)' : 'Detectable (High Risk)';
                'Score: ' + score + ' | Canvas: ' + canvasData.substring(0, 20) + ' | Audio: ' + audioHash;
            ";
            var result = await webView.ExecuteScriptAsync(testScript);
            Logger.LogInfo($"Test Fingerprint: {result}");
            return result;
        }

        public async Task<bool> DetectCaptchaAsync(WebView2 webView)
        {
            var checkScript = "!!document.querySelector('.captcha, [data-recaptcha], iframe[src*=\"recaptcha\"]') ? 'true' : 'false';";
            var hasCaptcha = await webView.ExecuteScriptAsync(checkScript);
            if (hasCaptcha == "true")
            {
                Logger.LogWarning("CAPTCHA détecté ! Pause et manuel requis.");
                return true;
            }
            return false;
        }
    }
}