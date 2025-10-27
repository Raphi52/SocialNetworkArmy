using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Service pour publier des Reels Instagram via WebView2
    /// </summary>
    public class ReelsService
    {
        private readonly WebView2 webView;
        private readonly TextBox log;
        private readonly InstagramBotForm form;
        private readonly Profile profile;
        private readonly Random rng = new Random();

        public ReelsService(WebView2 webView, TextBox log, InstagramBotForm form, Profile profile)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.form = form ?? throw new ArgumentNullException(nameof(form));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        /// <summary>
        /// Publie un reel basé sur le schedule du jour
        /// </summary>
        public async Task<bool> PublishScheduledReelAsync(CancellationToken token = default)
        {
            try
            {
                log.AppendText("[Reels] Checking schedule...\r\n");

                // ✅ UTILISER LA LOGIQUE CENTRALISÉE
                var match = ScheduleHelper.GetTodayMediaForAccount(
                    profile.Name,
                    profile.Platform,
                    "reels"
                );

                if (match == null)
                {
                    log.AppendText("[Reels] ✗ No reel scheduled today\r\n");
                    return false;
                }

                log.AppendText($"[Reels] ✓ Found {(match.IsGroup ? "group" : "account")} match: {match.AccountOrGroup}\r\n");
                log.AppendText($"[Reels] ✓ Media: {Path.GetFileName(match.MediaPath)}\r\n");

                if (!string.IsNullOrWhiteSpace(match.Description))
                {
                    log.AppendText($"[Reels] ✓ Caption: {match.Description.Substring(0, Math.Min(50, match.Description.Length))}...\r\n");
                }

                // Publier le reel
                return await PublishReelAsync(match.MediaPath, match.Description, token);
            }
            catch (Exception ex)
            {
                log.AppendText($"[Reels] ✗ Error: {ex.Message}\r\n");
                return false;
            }
        }

        /// <summary>
        /// Publie un reel avec le fichier spécifié
        /// </summary>
        private async Task<bool> PublishReelAsync(string videoPath, string caption, CancellationToken token)
        {
            if (!File.Exists(videoPath))
            {
                log.AppendText("[Reels] ✗ File not found\r\n");
                return false;
            }

            try
            {
                await webView.EnsureCoreWebView2Async();

                log.AppendText("[Reels] Starting publication...\r\n");

                // 1) Cliquer sur Créer
                if (!await ClickCreateAsync(token))
                {
                    log.AppendText("[Reels] ✗ Create button not found\r\n");
                    return false;
                }
                await Delay(2000, 3000, token);

                // 2) Sélectionner Reel
                if (!await SelectReelTabAsync(token))
                {
                    log.AppendText("[Reels] ✗ Reel tab not found\r\n");
                    return false;
                }
                await Delay(2000, 3000, token);

                // 3) Uploader le fichier
                if (!await UploadVideoAsync(videoPath, token))
                {
                    log.AppendText("[Reels] ✗ Upload failed\r\n");
                    return false;
                }
                await Delay(4000, 6000, token);

                // 4) Suivant (preview)
                if (!await ClickNextAsync(token))
                {
                    log.AppendText("[Reels] ✗ Next button not found\r\n");
                    return false;
                }
                await Delay(3000, 4000, token);

                // 5) Suivant (description)
                if (!await ClickNextAsync(token))
                {
                    log.AppendText("[Reels] ✗ Second Next not found\r\n");
                    return false;
                }
                await Delay(2000, 3000, token);

                // 6) Écrire la caption si fournie
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    if (!await WriteCaptionAsync(caption, token))
                    {
                        log.AppendText("[Reels] ⚠ Caption failed (continuing)\r\n");
                    }
                    await Delay(1000, 2000, token);
                }

                // 7) Publier
                if (!await ClickShareAsync(token))
                {
                    log.AppendText("[Reels] ✗ Share button not found\r\n");
                    return false;
                }

                log.AppendText("[Reels] ✓ Publication completed!\r\n");
                return true;
            }
            catch (Exception ex)
            {
                log.AppendText($"[Reels] ✗ Exception: {ex.Message}\r\n");
                return false;
            }
        }

        // ========== Helpers ==========

        private async Task<bool> ClickCreateAsync(CancellationToken token)
        {
            string js = @"
(() => {
    const rm = s => (s||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase();
    const wants = ['creer','nouvelle publication','create','new post'];
    const el = [...document.querySelectorAll('[aria-label]')].find(e => {
        const lab = rm((e.getAttribute('aria-label') || ''));
        return wants.some(k => lab.includes(k));
    });
    if (!el) return 'NOT_FOUND';
    el.scrollIntoView({block:'center'});
    const r = el.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    ['pointerdown','mousedown','mouseup','pointerup','click'].forEach(ev =>
        el.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'CLICKED';
})();";
            var res = Trim(await Exec(js));
            return res == "CLICKED";
        }

        private async Task<bool> SelectReelTabAsync(CancellationToken token)
        {
            string js = @"
(() => {
    const dlg = document.querySelector('[role=""dialog""]') || document;
    const btn = [...dlg.querySelectorAll('button,[role=""button""],div[role=""tab""]')]
        .find(e => /reel/i.test((e.textContent||'') + (e.getAttribute('aria-label')||'')));
    if (!btn) return 'NOT_FOUND';
    btn.scrollIntoView({block:'center'});
    const r = btn.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    ['pointerdown','mousedown','mouseup','pointerup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'CLICKED';
})();";
            var res = Trim(await Exec(js));
            return res == "CLICKED";
        }

        private async Task<bool> UploadVideoAsync(string videoPath, CancellationToken token)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(videoPath, token);
                var b64 = Convert.ToBase64String(bytes);
                var name = Path.GetFileName(videoPath).Replace("'", "_");

                string js = $@"
(() => {{
    const b64 = '{b64}';
    const name = '{name}';
    const bin = atob(b64);
    const u8 = new Uint8Array(bin.length);
    for (let i=0; i<bin.length; i++) u8[i] = bin.charCodeAt(i);
    const blob = new Blob([u8], {{type: 'video/mp4'}});
    const file = new File([blob], name, {{type: 'video/mp4', lastModified: Date.now()}});
    
    const dlg = document.querySelector('[role=""dialog""]') || document;
    const input = dlg.querySelector('input[type=""file""]');
    if (!input) return 'NO_INPUT';
    
    const dt = new DataTransfer();
    dt.items.add(file);
    
    try {{
        Object.defineProperty(input, 'files', {{configurable:true, value: dt.files}});
    }} catch(e) {{
        input.files = dt.files;
    }}
    
    input.dispatchEvent(new Event('input', {{bubbles:true}}));
    input.dispatchEvent(new Event('change', {{bubbles:true}}));
    return 'OK';
}})();";

                var res = Trim(await Exec(js));
                return res == "OK";
            }
            catch (Exception ex)
            {
                log.AppendText($"[Reels/Upload] {ex.Message}\r\n");
                return false;
            }
        }

        private async Task<bool> ClickNextAsync(CancellationToken token)
        {
            string js = @"
(() => {
    const dlg = document.querySelector('[role=""dialog""]') || document;
    const btn = [...dlg.querySelectorAll('button,[role=""button""]')]
        .find(e => /suivant|next/i.test((e.textContent||'') + (e.getAttribute('aria-label')||'')));
    if (!btn) return 'NOT_FOUND';
    btn.scrollIntoView({block:'center'});
    const r = btn.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    ['pointerdown','mousedown','mouseup','pointerup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'CLICKED';
})();";
            var res = Trim(await Exec(js));
            return res == "CLICKED";
        }

        private async Task<bool> WriteCaptionAsync(string caption, CancellationToken token)
        {
            string js = @"
(() => {
    const dlg = document.querySelector('[role=""dialog""]') || document;
    let box = dlg.querySelector('textarea[aria-label], textarea');
    if (!box) box = dlg.querySelector('[role=""textbox""][contenteditable=""true""]');
    if (!box) return 'NO_BOX';
    box.focus();
    document.execCommand('selectAll', false, null);
    document.execCommand('delete', false, null);
    document.execCommand('insertText', false, " + System.Text.Json.JsonSerializer.Serialize(caption) + @");
    box.dispatchEvent(new Event('input', {bubbles:true}));
    return 'OK';
})();";
            var res = Trim(await Exec(js));
            return res == "OK";
        }

        private async Task<bool> ClickShareAsync(CancellationToken token)
        {
            string js = @"
(() => {
    const dlg = document.querySelector('[role=""dialog""]') || document;
    const btn = [...dlg.querySelectorAll('button,[role=""button""]')]
        .find(e => /partager|share|publier|post/i.test((e.textContent||'') + (e.getAttribute('aria-label')||'')));
    if (!btn) return 'NOT_FOUND';
    btn.scrollIntoView({block:'center'});
    const r = btn.getBoundingClientRect();
    const x = r.left + r.width/2, y = r.top + r.height/2;
    ['pointerdown','mousedown','mouseup','pointerup','click'].forEach(ev =>
        btn.dispatchEvent(new MouseEvent(ev, {bubbles:true, clientX:x, clientY:y, button:0}))
    );
    return 'CLICKED';
})();";
            var res = Trim(await Exec(js));
            return res == "CLICKED";
        }

        private async Task<string> Exec(string js)
        {
            return await webView.CoreWebView2.ExecuteScriptAsync(js);
        }

        private string Trim(string s) => (s ?? "").Trim().Trim('"');

        private async Task Delay(int minMs, int maxMs, CancellationToken token)
        {
            await Task.Delay(rng.Next(minMs, maxMs), token);
        }
    }
}