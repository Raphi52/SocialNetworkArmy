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
            try
            {
                // Test simple avec Canvas (simule CreepJS) – Audio optionnel pour éviter exceptions
                var testScript = @"
                    let canvasData = '';
                    let audioHash = 'N/A';
                    try {
                        const canvas = document.createElement('canvas');
                        const ctx = canvas.getContext('2d');
                        ctx.textBaseline = 'top';
                        ctx.font = '14px Arial';
                        ctx.fillText('Test fingerprint', 2, 2);
                        canvasData = canvas.toDataURL(); // Avec noise si spoof OK
                    } catch (e) {
                        canvasData = 'Error: ' + e.message;
                    }

                    try {
                        if (window.AudioContext || window.webkitAudioContext) {
                            const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                            const oscillator = audioCtx.createOscillator();
                            oscillator.type = 'sine';
                            oscillator.frequency.setValueAtTime(440 + Math.random() * 10, audioCtx.currentTime);
                            const analyser = audioCtx.createAnalyser();
                            oscillator.connect(analyser);
                            analyser.connect(audioCtx.destination); // Connect to destination
                            oscillator.start();
                            const buffer = new Uint8Array(analyser.frequencyBinCount);
                            analyser.getByteFrequencyData(buffer);
                            oscillator.stop();
                            audioHash = btoa(String.fromCharCode(...buffer)).substring(0, 20);
                        }
                    } catch (e) {
                        audioHash = 'Error: ' + e.message;
                    }

                    const score = canvasData.length > 100 && audioHash !== 'N/A' ? 'Unique (Low Risk)' : 'Detectable (High Risk)';
                    'Score: ' + score + ' | Canvas: ' + canvasData.substring(0, 20) + ' | Audio: ' + audioHash;
                ";
                var result = await webView.ExecuteScriptAsync(testScript);
                Logger.LogInfo($"Test Fingerprint: {result}");
                return result ?? "Error executing script";
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception in TestFingerprintAsync: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public async Task<bool> DetectCaptchaAsync(WebView2 webView)
        {
            try
            {
                var checkScript = "!!document.querySelector('.captcha, [data-recaptcha], iframe[src*=\"recaptcha\"]') ? 'true' : 'false';";
                var hasCaptcha = await webView.ExecuteScriptAsync(checkScript);
                bool captchaDetected = hasCaptcha == "true";
                if (captchaDetected)
                {
                    Logger.LogWarning("CAPTCHA détecté ! Pause et manuel requis.");
                }
                return captchaDetected;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception in DetectCaptchaAsync: {ex.Message}");
                return false;
            }
        }
    }
}