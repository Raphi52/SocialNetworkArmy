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

        // Timings
        private const int WaitAfterDirectMs = 3000; // Réduit car TryClickMessageButtonAsync attend déjà
        private const int WaitAfterKItemMs = 5000;
        private const int InterProfileMs = 5000;

        public DirectMessageService(WebView2 webView, System.Windows.Forms.TextBox logTextBox, Profile profile, InstagramBotForm form)
        {
            _webView = webView;
            _log = logTextBox;
            _profile = profile;
            _form = form;
            _navigationService = new NavigationService(webView, logTextBox);
            _messageService = new MessageService(webView, Log);
        }

        private void Log(string m) => _log?.AppendText("[DM] " + m + Environment.NewLine);

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

                if (!File.Exists(messagesPath)) { Log("Fichier dm_messages.txt manquant."); return; }
                var messages = File.ReadAllLines(messagesPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (messages.Count == 0) { Log("Aucun message valide."); return; }

                if (!File.Exists(targetsPath)) { Log("Fichier dm_targets.txt manquant."); return; }
                var targets = File.ReadAllLines(targetsPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (targets.Count == 0) { Log("Aucune cible valide."); return; }

                foreach (var target in targets)
                {
                    runToken.ThrowIfCancellationRequested();
                    Log($"Profil : {target}");

                    bool navSuccess = await _navigationService.NavigateToProfileViaSearchAsync(target, runToken);
                    if (!navSuccess)
                    {
                        Log("Navigation échouée, passage au suivant.");
                        await Task.Delay(InterProfileMs, runToken);
                        continue;
                    }
                    await Task.Delay(400, runToken);

                    bool messageSent = false;
                    var msg = messages[rng.Next(messages.Count)];

                    var buttonResult = await _messageService.TryClickMessageButtonAsync(runToken);
                    Log($"[BUTTON] Résultat: {buttonResult}");

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
                                // ✅ Le clic est passé → on fait confiance, pas besoin de EnsureOnDmPageAsync
                                Log("[KEBAB] Clic effectué → on suppose la page DM ouverte");
                                await Task.Delay(WaitAfterKItemMs, runToken);

                                // 🔥 On transpose directement le scénario stable qui marche
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

                    Log(messageSent ? "✓ Message envoyé avec succès" : "✗ Échec total pour ce profil");

                    await Task.Delay(800, runToken);
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