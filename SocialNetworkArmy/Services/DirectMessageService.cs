using SocialNetworkArmy.Forms;
using SocialNetworkArmy.Models;
using SocialNetworkArmy.Utils;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using System.Collections.Generic;

namespace SocialNetworkArmy.Services
{
    public class DirectMessageService
    {
        private readonly WebView2 _webView;
        private readonly System.Windows.Forms.TextBox _log;
        private readonly Profile _profile;
        private readonly InstagramBotForm _form;
        private readonly NavigationService _navigationService;
        private readonly MessageService _messageService;
        private readonly Random _rng;

        // Timings (humanized - no longer constants)
        private int WaitAfterDirectMs => 2500 + _rng.Next(500, 1500);
        private int WaitAfterKItemMs => 4000 + _rng.Next(800, 2000);
        private int InterProfileMs => 4000 + _rng.Next(1000, 3000);

        public DirectMessageService(WebView2 webView, System.Windows.Forms.TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            _webView = webView;
            _log = logTextBox;
            _profile = profile;
            _form = form;
            _rng = new Random();
            _navigationService = new NavigationService(webView, logTextBox);
            _messageService = new MessageService(webView, Log);
        }

        private void Log(string m) => _log?.AppendText("[DM] " + m + Environment.NewLine);

        /// <summary>
        /// ✅ NOUVEAU: Comportement humain - Micro-pause aléatoire
        /// </summary>
        private async Task HumanMicroPauseAsync(CancellationToken token)
        {
            try
            {
                // 60% chance de faire une micro-pause (0.3s-1.2s)
                if (_rng.NextDouble() < 0.60)
                {
                    int pauseDuration = _rng.Next(300, 1200);
                    await Task.Delay(pauseDuration, token);
                }
            }
            catch (Exception ex)
            {
                Log($"[HUMAN] Micro-pause error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NOUVEAU: Comportement humain - Mouvement de souris aléatoire
        /// </summary>
        private async Task HumanMouseMovementAsync(CancellationToken token)
        {
            try
            {
                // 40% chance de faire un mouvement de souris
                if (_rng.NextDouble() < 0.40)
                {
                    var mouseMoveScript = @"
(function() {
    try {
        const elements = document.querySelectorAll('a, button, div[role=""button""], article');
        if (elements.length === 0) return 'NO_ELEMENTS';

        const randomEl = elements[Math.floor(Math.random() * Math.min(elements.length, 15))];
        const rect = randomEl.getBoundingClientRect();

        if (rect.top >= 0 && rect.bottom <= window.innerHeight) {
            const x = rect.left + rect.width / 2;
            const y = rect.top + rect.height / 2;

            randomEl.dispatchEvent(new MouseEvent('mouseover', {bubbles: true, clientX: x, clientY: y}));

            setTimeout(() => {
                randomEl.dispatchEvent(new MouseEvent('mouseleave', {bubbles: true, clientX: x, clientY: y}));
            }, Math.random() * 800 + 300);
        }

        return 'MOUSE_MOVED';
    } catch(e) {
        return 'ERROR: ' + e.message;
    }
})();";

                    await _webView.ExecuteScriptAsync(mouseMoveScript);
                    await Task.Delay(_rng.Next(300, 800), token);
                }
            }
            catch (Exception ex)
            {
                Log($"[HUMAN] Mouse movement error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NOUVEAU: Comportement humain - Hésitation (pause avant action importante)
        /// </summary>
        private async Task HumanHesitationAsync(CancellationToken token)
        {
            try
            {
                // 30% chance d'hésiter (0.5s-2s)
                if (_rng.NextDouble() < 0.30)
                {
                    int hesitationDuration = _rng.Next(500, 2000);
                    Log($"[HUMAN] Hesitating ({hesitationDuration}ms)...");
                    await Task.Delay(hesitationDuration, token);
                }
            }
            catch (Exception ex)
            {
                Log($"[HUMAN] Hesitation error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NOUVEAU: Comportement humain - Scroll léger aléatoire
        /// </summary>
        private async Task HumanRandomScrollAsync(CancellationToken token)
        {
            try
            {
                // 25% chance de faire un petit scroll
                if (_rng.NextDouble() < 0.25)
                {
                    var scrollScript = @"
(function() {
    try {
        const scrollAmount = (Math.random() > 0.5 ? 1 : -1) * (Math.random() * 150 + 50);
        window.scrollBy({
            top: scrollAmount,
            behavior: 'smooth'
        });
        return 'SCROLLED';
    } catch(e) {
        return 'ERROR: ' + e.message;
    }
})();";

                    await _webView.ExecuteScriptAsync(scrollScript);
                    await Task.Delay(_rng.Next(500, 1200), token);
                }
            }
            catch (Exception ex)
            {
                Log($"[HUMAN] Random scroll error: {ex.Message}");
            }
        }

        public async Task RunAsync(CancellationToken token = default)
        {
            await _form.StartScriptAsync("Direct Messages");
            var runToken = token.CanBeCanceled ? token : _form.GetCancellationToken();
            var rng = new Random();

            try
            {
                var dataDir = "data";
                var messagesPath = Path.Combine(dataDir, "dm_messages.txt");
                var targetsPath = Path.Combine(dataDir, "dm_targets.txt");
                var sentPath = Path.Combine(dataDir, "dm_sent.txt");

                if (!File.Exists(messagesPath)) { Log("Fichier dm_messages.txt manquant."); return; }
                var messages = File.ReadAllLines(messagesPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (messages.Count == 0) { Log("Aucun message valide."); return; }

                if (!File.Exists(targetsPath)) { Log("Fichier dm_targets.txt manquant."); return; }
                var allTargets = File.ReadAllLines(targetsPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (allTargets.Count == 0) { Log("Aucune cible valide."); return; }

                // Charger les comptes déjà traités
                var sentAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(sentPath))
                {
                    var sentLines = File.ReadAllLines(sentPath);
                    foreach (var line in sentLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            sentAccounts.Add(line.Trim());
                        }
                    }
                    Log($"Comptes déjà traités : {sentAccounts.Count}");
                }

                // Filtrer les cibles non traitées
                var targets = allTargets.Where(t => !sentAccounts.Contains(t.Trim())).ToList();

                if (targets.Count == 0)
                {
                    Log("Tous les comptes ont déjà été traités !");
                    return;
                }

                Log($"Comptes à traiter : {targets.Count}/{allTargets.Count}");

                foreach (var target in targets)
                {
                    runToken.ThrowIfCancellationRequested();
                    Log($"Profil : {target}");

                    // ✅ Comportement humain : hésitation avant navigation
                    await HumanHesitationAsync(runToken);

                    bool navSuccess = await _navigationService.NavigateToProfileViaSearchAsync(target, runToken);
                    if (!navSuccess)
                    {
                        Log("Navigation échouée, passage au suivant.");
                        await Task.Delay(InterProfileMs, runToken);
                        continue;
                    }

                    // ✅ Comportement humain : micro-pause après navigation
                    await HumanMicroPauseAsync(runToken);

                    // ✅ Comportement humain : mouvement de souris aléatoire
                    await HumanMouseMovementAsync(runToken);

                    bool messageSent = false;
                    var msg = messages[rng.Next(messages.Count)];

                    // ✅ Comportement humain : hésitation avant clic sur bouton message
                    await HumanHesitationAsync(runToken);

                    var buttonResult = await _messageService.TryClickMessageButtonAsync(runToken);
                    Log($"[BUTTON] Résultat: {buttonResult}");

                    // ✅ Comportement humain : micro-pause après clic bouton
                    await HumanMicroPauseAsync(runToken);

                    // ========== CAS 1: MODAL PRO AVEC INPUT ==========
                    if (buttonResult == "pro_modal_opened")
                    {
                        Log("Modal compte pro détecté → envoi direct dans la modal");
                        messageSent = await HandleOverlayAsync(msg, runToken);
                    }
                    // ========== CAS 2: MODAL SANS INPUT (dialog d'avertissement) ==========
                    else if (buttonResult == "modal_no_input" || buttonResult == "modal_with_continue_button")
                    {
                        Log("Modal détecté sans input → tentative d'avancement");

                        if (await _messageService.AdvanceDialogIfNeededAsync(runToken))
                        {
                            Log("Dialog avancé, attente page DM...");
                            await Task.Delay(1500, runToken);

                            var st = await _messageService.EnsureOnDmPageAsync(runToken, 12000, "[READY-AFTER-DIALOG] ");
                            if (st == "editor" || st == "url")
                            {
                                messageSent = await SendOnDmPageAsync(msg, runToken);
                            }
                            else
                            {
                                Log("Page DM non prête après dialog → échec");
                            }
                        }
                        else
                        {
                            Log("Impossible d'avancer le dialog → échec");
                        }
                    }
                    // ========== CAS 3: REDIRECTION VERS /direct/ ==========
                    else if (buttonResult == "redirected_to_direct" || buttonResult == "clicked_no_modal")
                    {
                        Log("Redirection vers page DM détectée");
                        await Task.Delay(WaitAfterDirectMs, runToken);

                        var st = await _messageService.EnsureOnDmPageAsync(runToken, 12000, "[READY-REDIRECT] ");

                        if (st == "dialog")
                        {
                            Log("Dialog détecté sur page DM, avancement...");
                            if (await _messageService.AdvanceDialogIfNeededAsync(runToken))
                            {
                                await Task.Delay(900, runToken);
                                st = await _messageService.EnsureOnDmPageAsync(runToken, 12000, "[READY-AFTER-ADV] ");
                            }
                        }

                        if (st == "editor" || st == "url")
                        {
                            messageSent = await SendOnDmPageAsync(msg, runToken);
                        }
                        else
                        {
                            Log("Page DM non prête → échec");
                        }
                    }
                    // ========== CAS 4: AUCUN BOUTON TROUVÉ → KEBAB ==========
                    else if (buttonResult == "no_button_found")
                    {
                        Log("Aucun bouton message trouvé → tentative kebab menu");

                        var opened = await _messageService.TryOpenKebabMenuAsync(runToken);
                        Log(opened ? "[KEBAB] Menu ouvert" : "[KEBAB] Menu introuvable");

                        if (!opened)
                        {
                            Log("[KEBAB] Échec ouverture menu → passage au suivant");
                        }
                        else
                        {
                            var itemClicked = await _messageService.ClickSendItemInMenuAsync(runToken);
                            Log(itemClicked ? "[K-ITEM] Item cliqué" : "[K-ITEM] Item introuvable");

                            if (itemClicked)
                            {
                                Log("[KEBAB] Clic effectué → on suppose la page DM ouverte");
                                await Task.Delay(WaitAfterKItemMs, runToken);
                                messageSent = await SendOnDmPageAsync(msg, runToken);
                            }
                            else
                            {
                                Log("[K-ITEM] Aucun item message cliqué → échec");
                            }
                        }
                    }
                    // ========== CAS 5: ERREUR ==========
                    else if (buttonResult.StartsWith("error"))
                    {
                        Log($"Erreur détectée: {buttonResult} → passage au suivant");
                    }
                    else
                    {
                        Log($"Résultat inattendu ({buttonResult}) → échec");
                    }

                    // ========== SAUVEGARDE SI SUCCÈS ==========
                    if (messageSent)
                    {
                        Log("✓ Message envoyé avec succès");

                        // Ajouter le compte au fichier dm_sent.txt
                        try
                        {
                            File.AppendAllText(sentPath, target.Trim() + Environment.NewLine);
                            Log($"✓ Compte '{target}' enregistré dans dm_sent.txt");
                        }
                        catch (Exception ex)
                        {
                            Log($"⚠ Erreur sauvegarde : {ex.Message}");
                        }

                        // ✅ Comportement humain : micro-pause après succès
                        await HumanMicroPauseAsync(runToken);
                    }
                    else
                    {
                        Log("✗ Échec total pour ce profil");
                    }

                    // ✅ Comportement humain : scroll aléatoire avant le prochain profil
                    await HumanRandomScrollAsync(runToken);

                    // ✅ Comportement humain : mouvement de souris aléatoire
                    await HumanMouseMovementAsync(runToken);

                    await Task.Delay(InterProfileMs, runToken);
                }

                Log("Tous les profils ont été traités.");
            }
            catch (OperationCanceledException)
            {
                try { _webView?.CoreWebView2?.Stop(); } catch { }
                Log("Script annulé par l'utilisateur.");
            }
            catch (Exception ex)
            {
                Log("Erreur : " + ex.Message);
                Logger.LogError("Erreur dans DirectMessageService : " + ex);
            }
            finally
            {
                _form.ScriptCompleted();
            }
        }

        private async Task<bool> HandleOverlayAsync(string msg, CancellationToken token)
        {
            Log("[OVERLAY] Traitement modal compte pro");
            int waitMs = 1000 + new Random().Next(500, 900);
            var typed = await _messageService.TypeInProModalAsync(msg, token);
            Log(typed ? "[OVERLAY TYPE] ✓ Texte tapé" : "[OVERLAY TYPE] ✗ Échec frappe");

            if (typed)
            {
                await Task.Delay(300 + new Random().Next(400), token);
                var sent = await _messageService.SendInProModalAsync(token);
                Log(sent ? "[OVERLAY SEND] ✓ Message envoyé" : "[OVERLAY SEND] ✗ Échec envoi");
                return sent;
            }

            return false;
        }

        private async Task<bool> SendOnDmPageAsync(string msg, CancellationToken token)
        {
            var threadOk = await _messageService.EnsureThreadOpenAsync(token);
            Log(threadOk ? "[THREAD] ✓ Thread ouvert" : "[THREAD] ⚠ Fallback");

            var hydrated = await _messageService.WaitEditorHydratedAsync(token);
            Log(hydrated ? "[EDITOR] ✓ Éditeur hydraté" : "[EDITOR] ⚠ Sans Lexical");

            var typed = await _messageService.TypeMessageImprovedAsync(msg, token);
            Log(typed ? "[TYPE] ✓ Texte tapé" : "[TYPE] ✗ Échec frappe");

            if (!typed)
            {
                Log("Échec frappe → abandon");
                return false;
            }

            await Task.Delay(300 + new Random().Next(400), token);

            var sent = await _messageService.ClickSendAsync(token);
            Log(sent ? "[SEND] ✓ Message envoyé" : "[SEND] ✗ Échec envoi");

            return sent;
        }
    }
}