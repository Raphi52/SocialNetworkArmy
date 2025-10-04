SocialNetworkArmy

Description Générale

SocialNetworkArmy est un outil Windows en C# (.NET 8 avec WinForms) pour automatiser les interactions sur Instagram et TikTok de manière réaliste et anti-détection. Il gère plusieurs profils, utilise des proxies et un spoofing de fingerprints (User-Agent, WebGL, etc.), et simule un comportement humain pour éviter les bans (délais aléatoires, interactions partielles, limites quotidiennes).



Plateformes supportées : Instagram et TikTok.

Fonctionnalités clés :



Gestion multi-profils (login/logout sécurisé).

Automatisation via WebView2 (navigation, clics, saisie, scrolls natifs).

Actions : Target (ciblage), Scroll (visionnage passif), Post (publication).

Anti-détection : Proxies rotatifs, randomisation des timings/mouvements, limites par jour.

UI : Forms séparées pour Instagram/TikTok, logs en temps réel, stats exportables.





Dépendances :



Microsoft.Web.WebView2 (pour l'embed de navigateur).

Serilog (logging).

CsvHelper (exports stats et lecture CSV pour publications).

Newtonsoft.Json (config).

Pas de Playwright/Puppeteer : tout via WebView2 natif.







Architecture



Services :



ProfileService : Gestion des profils (login, sessions, multi-comptes).

AutomationService : Orchestre les actions globales (lancement WebView2, injection de fingerprints, proxies).

ProxyService : Rotation de proxies (fichier Data/proxies.txt).

FingerprintService : Spoofing (User-Agent aléatoire, Canvas/WebGL via args WebView2).

TargetService : Spécifique au ciblage (détails ci-dessous).

LoggerService : Logs structurés (console + fichier).





Forms :



MainForm : Sélection plateforme/profil, boutons d'actions.

InstagramBotForm / TikTokBotForm : UI dédiée avec WebView2 embarqué, logs live.





Données :



Data/profiles.json : Liste profils (username, password hashé).

Data/targets.txt : Cibles (un username par ligne, UTF-8).

Data/comments.txt : Commentaires aléatoires (un par ligne).

Data/publish\_schedule.csv : Planning publications (format CSV : Date,Account,Plateforme,Media Path,Description).

Data/commented\_creators.txt : Liste des créateurs commentés (append par Scrolling, un par ligne).

config.json : Params (taux interactions, délais min/max, limites likes/jour).





Build : .NET 8, target x64. Installer WebView2 Runtime via Evergreen Bootstrapper.



Détails des Actions

Target (Ciblage)



Objectif : Pour chaque cible (de Data/targets.txt), ouvrir son profil, puis scroller 5-10 Reels/posts suivants pour simuler un "engagement naturel". Liker aléatoirement 9% des Reels scrollés, et commenter uniquement ceux publiés il y a moins de 24h (détection via timestamp relatif ou scraping date de post).

Flux :



Charger liste cibles.

Pour chaque : Naviguer vers https://www.instagram.com/\[target]/ (ou TikTok equiv.).

Attendre chargement (via CoreWebView2.NavigationCompleted).

Si profil privé/inexistant : Log erreur, skip (simuler un bref scroll puis next).

Ouvrir dernier Reel/post : Clique via ExecuteScriptAsync("document.querySelector('selector').click()") (injection JS minimale si besoin, mais prioriser événements natifs WebView2).

Scroller 5-10 Reels : Scroll fluide (via ExecuteScriptAsync pour smooth scroll), durée 5-10s par Reel.

Likes : Pour chaque Reel scrollé, si random < 9% : Simuler clic souris (aléatoire position ±10px pour humaniser).

Commentaires : Pour chaque Reel avec <24h : Saisir texte aléatoire de comments.txt (délai frappe 100-300ms/char, avec 5% backspace/erreurs).

Délai global : 30-90s par cible + pause 1-5min entre cibles.





Sécurité : Max 50 interactions/jour/profil. Randomiser User-Agent/fingerprint par session.

Intégration : Bouton "Target" sur Form → Appel TargetService.ProcessTargetsAsync(profile, platform).



Scroll (Visionnage Passif)



Objectif : Naviguer feed principal, scroll infini 10-20min (vitesse variable, pauses aléatoires). Liker aléatoirement 9% des posts/Reels, commenter uniquement ceux avec déjà ≥100 commentaires (détection via count de replies), et logger les noms des créateurs commentés dans Data/commented\_creators.txt (append, un username par ligne, avec timestamp).

Flux :



Naviguer feed principal (https://www.instagram.com/ ou TikTok equiv., post-login).

Attendre chargement.

Scroll infini : Boucle de scrolls fluides (via ExecuteScriptAsync("window.scrollBy(0, window.innerHeight \* Math.random());")), avec pauses aléatoires 2-5s.

Pour chaque post/Reel visible :



Like : Si random < 9% : Clic simulé.

Commentaire : Vérif nombre commentaires (inject ExecuteScriptAsync("return document.querySelector('.comment-count')?.textContent || 0;") → Si ≥100 : Saisir commentaire aléatoire + submit ; append username créateur à commented\_creators.txt).





Durée totale : 10-20min, avec ~10% likes/vues partielles.

Humanisation : Mouvements souris aléatoires, délais frappe.





Sécurité : Limites quotidiennes (ex. max 100 likes/session). Logs stats (likes, commentaires effectués).

Intégration : Bouton "Scrolling" sur Form → Appel AutomationService.StartScrollingAsync(profile, platform).



Post (Publication)



Objectif : Lire le fichier Data/publish\_schedule.csv (format : Date,Account,Plateforme,Media Path,Description ; exemple : "2025-10-03,monprofil,Instagram,C:\\test\\image.jpg,Test post !"). Filtrer les lignes où Date = date du jour (format YYYY-MM-DD), et où Account = nom du profil actif (défini dans Data/profiles.json). Pour chaque ligne matchante : Publier le média avec la description fournie.

Flux :



Charger CSV via CsvHelper (parser en objets {Date, Account, Platforme, MediaPath, Description}).

Filtrer : Date == DateTime.Today.ToString("yyyy-MM-dd") ET Account == profile.Username.

Pour chaque entrée filtrée :



Naviguer vers page publication (https://www.instagram.com/p/ ou + pour nouveau post ; TikTok equiv.).

Attendre formulaire upload.

Uploader média : Via WebView2, simuler drag-drop ou file input (ExecuteScriptAsync pour trigger input file avec chemin absolu).

Saisir description : Texte de CSV + hashtags aléatoires si config ; délai frappe humanisé.

Submit : Clic bouton post + attente confirmation.





Gestion erreurs : Si média introuvable/échec upload → Log et skip ; retry max 3x.

Post-upload : Délai 1-3min avant next (si multiple).





Sécurité : Max 5 posts/jour/profil. Vérif format média (jpg/png/mp4 via Path.GetExtension).

Intégration : Bouton "Publish" sur Form → Appel AutomationService.PublishScheduledAsync(profile, platform) (scan CSV et exécute).



Classe TargetService (Détails Implémentation)

Voici ce que fait TargetService.cs maintenant (structure proposée ; on peut coder ça ensemble si tu veux) :



Rôle : Gère le ciblage end-to-end pour une plateforme/profil, en utilisant un WebView2 partagé via AutomationService. Pas de JS externes : tout en C# avec événements WebView2 (NavigationCompleted, DOMContentLoaded) et injections inline minimales pour interactions précises. Focus sur scroll 5-10 Reels, likes 9%, commentaires <24h.

Méthodes clés :



async Task ProcessTargetsAsync(Profile profile, Platform platform) :



Lit targets.txt → Pour chaque cible : await TargetSingleAsync(target, webView).

Logs progression/stats (ex. "Cible @userX : 7 Reels scrollés, 1 like, 2 commentaires <24h").

Gère exceptions (timeout, ban detect) → Retry ou skip.





async Task TargetSingleAsync(string target, CoreWebView2 webView) :



Navigue : webView.Navigate(new Uri($"https://www.{platform.Domain}/{target}/"));.

Attend : await webView.WaitForNavigationAsync(); (extension helper pour polling).

Vérif profil : Inject await webView.ExecuteScriptAsync("return document.querySelector('.error') ? true : false;") → Si erreur, log et return.

Scroll Reels : Boucle 5-10x : await webView.ExecuteScriptAsync("window.scrollBy(0, window.innerHeight \* Math.random());"); + durée 5-10s.

Pour chaque Reel :



Like : Si Random.Shared.NextDouble() < 0.09 : await webView.ExecuteScriptAsync("document.querySelector('svg\[aria-label=\\"Like\\"]')?.click();"); + délai.

Commentaire <24h : Inject pour check timestamp (ExecuteScriptAsync("return new Date(document.querySelector('.post-date')?.textContent).getTime() > Date.now() - 86400000;")) → Si true : Saisir via host object + append log.





Humanisation : Ajouter MouseSimulator.MoveRandom(webView) (classe helper pour mouvements souris via Windows API).









Dépendances : Inject ILogger<TargetService>, IProxyService, IFingerprintService.

Exemple snippet (dans TargetSingleAsync) :

csharp// Scroll et interactions

int reelsCount = Random.Shared.Next(5, 11);

for (int i = 0; i < reelsCount; i++)

{

&nbsp;   await webView.ExecuteScriptAsync("window.scrollBy(0, window.innerHeight);");

&nbsp;   await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 11)));



&nbsp;   // Like 9%

&nbsp;   if (Random.Shared.NextDouble() < 0.09)

&nbsp;   {

&nbsp;       await webView.ExecuteScriptAsync("document.querySelector('svg\[aria-label=\\"Like\\"]')?.click();");

&nbsp;       await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 5)));

&nbsp;   }



&nbsp;   // Check <24h et commentaire

&nbsp;   var isRecent = await webView.ExecuteScriptAsync("return (Date.now() - new Date(document.querySelector('.post-date')?.getAttribute('datetime') || 0)) < 86400000;");

&nbsp;   if (Convert.ToBoolean(await webView.CoreWebView2.EvaluateScriptAsync(isRecent)))

&nbsp;   {

&nbsp;       // Saisir commentaire...

&nbsp;   }

}



Avantages WebView2 : Léger, pas de headless externe, intégration native WinForms. Limites : Moins flexible que Playwright pour scraping avancé, mais parfait pour automation basique.



Installation \& Lancement



Clone repo.

dotnet restore.

Installer WebView2 Runtime (auto via NuGet ou manual).

Configurer config.json et fichiers Data (incl. CSV pour publish).

dotnet run → Sélectionner profil/plateforme → Lancer actions.



TODO



Implémenter helpers pour WebView2 (WaitForElement, SimulateKeyPress, CheckPostAge, CountComments).

Ajouter stats dashboard (Graphiques via WinForms charts).

Tests unitaires (xUnit) pour services.

Version portable (self-contained).

