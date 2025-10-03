Cahier des charges – Automatisation Instagram / TikTok (C# / WinForms)
1. Objectif
Développer un outil Windows en C# (.NET 6/8, WinForms) permettant :
De gérer plusieurs profils (Instagram et TikTok).


De lancer un navigateur configuré avec des empreintes anti-détection (fingerprints + proxy).


D’exécuter des scripts d’automatisation réalistes (Target, Scroll, Publish).


D’assurer la gestion multi-profils, la planification d’actions et le suivi statistique.


De conserver une interface simple et des logs clairs.



2. Fonctionnalités principales
2.1 Gestion des profils (MainForm)
Inputs :


Plateforme : Instagram ou TikTok.


Nom de profil (identifiant unique).


Proxy associé (http://ip:port ou socks5://ip:port).


Actions :


Créer un profil → génère fingerprints + sauvegarde (JSON ou SQLite).


Supprimer un profil existant.


Sélectionner un profil dans la liste (ListBox ou DataGridView).


Lancer le navigateur avec les paramètres du profil.


Persistance :


Profils stockés dans profiles.json ou SQLite.


Cookies/session isolés par profil.


Multi-profils simultanés :


Support du threading / async pour gérer plusieurs profils en parallèle.



2.2 Anti-détection / Fingerprinting
Chaque profil doit simuler un utilisateur réel via :
User-Agent dynamique (desktop/mobile).


Fuseau horaire, langue, résolution écran, WebGL, Canvas, plugins.


Spoofing hardware (CPU/GPU, fonts, concurrency).


Masquage automatisation (navigator.webdriver, WebRTC, etc.).


Proxy dédié (résidentiel/mobile de préférence).


Sessions/cookies persistants restaurés automatiquement.


👉 Implémentation avec PuppeteerSharp ou PlaywrightSharp en C# et scripts JS injectés.

2.3 Actions (Forms spécifiques)
Chaque plateforme dispose de son Form (InstagramBotForm / TikTokBotForm) avec trois boutons : Target, Scroll, Publish.
a) Target
Charge targets.txt (UTF-8, un profil par ligne).


Pour chaque profil :


Ouvre le dernier Reel/post → like + commentaire (aléatoire).


Visionne 5–10 Reels suivants (5–10 sec chacun) → like environ 20%.


Gestion d’erreurs : profil privé, inexistant ou sans contenu → log + skip.


Fermeture du navigateur en fin de traitement.


b) Scroll
Accède à la page des Reels (/reels/ ou /foryou).


Pendant 20–40 min :


Visionnage aléatoire avec scroll fluide et pauses variables.


Like ~30%, commentaire ~30% (contenu aléatoire).


Fermeture automatique à la fin.


c) Publish
Lit schedule.csv ou Excel (Date / Account / Plateforme / Media Path / Description).


Filtre par date du jour + compte sélectionné + plateforme.


Pour chaque ligne correspondante :


Ouvre la page de publication.


Upload du média (photo/vidéo) et ajout de la description.


Vérification erreurs (fichier manquant, format non supporté).


Fermeture automatique après les publications.



3. Gestion des fichiers
targets.txt : liste des cibles (UTF-8).


schedule.csv : planification des publications.


profiles.json : stockage des profils et fingerprints.


Logs/ : fichiers .log avec rotation quotidienne et export JSON.



4. Interface utilisateur
MainForm.cs : CRUD profils + lancement navigateur.


InstagramBotForm.cs / TikTokBotForm.cs : boutons Target / Scroll / Publish.


Tableau de bord temps réel : progression, logs, statut navigateur.


Dashboard statistique : graphiques sur engagement (likes, commentaires, publications réussies).


Scheduler intégré : possibilité de planifier l’exécution automatique des scripts.



5. Logs & suivi
Logger centralisé (Logger.cs).


Niveaux : INFO / WARNING / ERROR.


Exemple :
 [2025-10-02 12:35:20][INFO] Profil "insta_demo" → Like effectué sur Reel #id123


Logs affichés en temps réel dans l’interface + sauvegarde persistante.


Analyse post-exécution (nb likes, nb commentaires, nb publications).



6. Paramétrage
Fichier config.json pour ajuster :


Durée visionnage min/max (ex: 5–10s).


% like/comment (avec plage aléatoire).


Nombre max de Reels par profil.


Délais entre actions.


Paramètres modifiables directement depuis l’interface.



7. Stack technique
Langage : C# .NET 6 ou .NET 8


UI : WinForms


Automation : PuppeteerSharp ou PlaywrightSharp


Parsing CSV/Excel : CsvHelper + ClosedXML


Persistance : JSON (Newtonsoft.Json) ou SQLite


Logging : Serilog ou Logger custom



8. Sécurité & limitations
Automatisation = violation CGU Instagram/TikTok → risque de bannissement.


Actions doivent intégrer de l’aléatoire (temps, taux d’interaction, déplacements souris).


Proxy obligatoire (HTTP/SOCKS5).


Nettoyage mémoire + fermeture navigateur après chaque run.


Limites de sécurité intégrées (ex: max 50 likes/jour/profil).



9. Structure projet
SocialNetworkArmy/
│
├── Data/
│   ├── profiles.json
│   ├── targets.txt
│   ├── schedule.csv
│   └── Logs/
│
├── Scripts/
│   ├── instagram/ (target.js, scroll.js, publish.js)
│   └── tiktok/ (target.js, scroll.js, publish.js)
│
├── Services/
│   ├── ProfileService.cs
│   ├── AutomationService.cs
│   ├── ProxyService.cs
│   └── FingerprintService.cs
│
├── Forms/
│   ├── MainForm.cs
│   ├── InstagramBotForm.cs
│   └── TikTokBotForm.cs
│
├── Models/
│   ├── Profile.cs
│   ├── Fingerprint.cs
│   └── ScheduleEntry.cs
│
└── Utils/
├── Logger.cs
├── Config.cs
└── Helpers.cs















LES COMPTES DOIVENT SURVIVRE :
 
Anti-détection / Fingerprinting 


Pour assurer que Instagram et TikTok ne détectent jamais l'automatisation, il faut implémenter une stratégie complète d'évasion couvrant les empreintes digitales (fingerprints), les comportements humains simulés, les protocoles de communication, et les limites d'activité. L'objectif est de rendre l'outil indistinguable d'un utilisateur réel en évitant tous les signaux de détection connus (comme les flags d'automatisation, les patterns comportementaux anormaux, ou les artefacts techniques). Voici une liste exhaustive de tous les éléments à considérer et implémenter, basée sur les meilleures pratiques pour PuppeteerSharp ou PlaywrightSharp. Cela inclut des techniques basiques (déjà mentionnées) et avancées pour contourner les défenses modernes comme les VMs obfuscées de TikTok ou les fingerprintings basés sur l'IA d'Instagram.
Empreintes digitales (Fingerprints) à spoofer

User-Agent dynamique: Générer aléatoirement des User-Agents réalistes (basés sur des listes de navigateurs réels comme Chrome, Firefox sur desktop/mobile). Éviter les User-Agents par défaut de Puppeteer/Playwright qui incluent "HeadlessChrome". Rotation par session ou par action pour matcher des dispositifs variés (ex: iOS pour TikTok mobile-like).
Fuseau horaire et langue: Spoofer le timezone (ex: via Intl.DateTimeFormat) et la langue du navigateur (navigator.languages) pour correspondre à des utilisateurs réels (ex: aléatoire par proxy géolocalisé). Utiliser des valeurs cohérentes avec le proxy (ex: US pour un proxy américain).
Résolution d'écran et viewport: Randomiser la résolution (ex: 1920x1080, 1280x720) et le viewport pour simuler différents appareils. Éviter les résolutions par défaut headless qui sont détectables.
WebGL et Canvas fingerprinting: Spoofer les rendus WebGL (vendor/renderer) et Canvas (via injection JS pour modifier toDataURL ou ajouter du bruit aléatoire aux pixels). TikTok utilise spécifiquement cela dans sa VM obfuscée ; ajouter du bruit unique par session pour éviter les matches exacts.
Audio fingerprinting: Modifier les propriétés audio (ex: AudioContext, oscillatorNode) pour ajouter du bruit aléatoire et éviter les fingerprints statiques.
Fonts et plugins: Spoofer la liste des fonts installées (via injection pour simuler des sets communs comme Arial, Times New Roman). Ajouter des plugins manquants (ex: PDF viewer, Flash-like stubs) pour matcher un navigateur réel ; Puppeteer headless manque souvent ces éléments.
Hardware spoofing: Randomiser le hardware concurrency (navigator.hardwareConcurrency, ex: 4-16 cœurs), CPU/GPU info (via WebGL), et mémoire disponible. Utiliser des valeurs plausibles basées sur des stats réelles d'utilisateurs.
WebRTC masking: Désactiver ou spoofer WebRTC (ex: navigator.mediaDevices) pour cacher l'IP réelle ; configurer pour matcher le proxy et éviter les leaks.
Autres APIs navigateur: Spoofer navigator.platform, navigator.vendor, screen.depth, navigator.maxTouchPoints (pour simuler touch sur mobile). Utiliser des proxies JS pour intercepter et modifier ces appels.

Masquage des indicateurs d'automatisation

Désactiver navigator.webdriver: Définir navigator.webdriver à undefined via page.addInitScript (Playwright) ou page.evaluateOnNewDocument (Puppeteer). C'est un flag clé détecté par Instagram et TikTok.
Cacher les artefacts CDP (Chrome DevTools Protocol): Minimiser l'usage de CDP pour éviter les détections protocol-level (ex: WebSocket communications, object serialization). Utiliser des frameworks avancés comme Nodriver ou Selenium Driverless pour réimplémenter les primitives d'automatisation sans CDP/WebDriver.
Mode headful vs headless: Préférer le mode headful (visible) pour certaines actions, car headless laisse des traces (ex: codebase unifié de Chrome depuis 2022 rend headless plus détectable). Basculer dynamiquement en fonction du risque.
Éviter les VMs obfuscées (spécifique TikTok): Pour TikTok, qui compile son JS en bytecode exécuté par une VM custom, extraire/reimplémenter l'interpréteur ou émuler le bytecode manuellement. Utiliser un navigateur complet pour générer des signaux valides au lieu de bots HTTP purs.
Intégration avec anti-detect browsers: Utiliser des patches comme Rebrowser pour Puppeteer/Playwright, ou intégrer avec des browsers anti-detect commerciaux (ex: pour spoofing avancé et rotation automatique).

Gestion des proxies et réseau

Proxies dédiés et rotation: Utiliser exclusivement des proxies résidentiels ou mobiles (pas datacenter, car détectables). Rotation automatique d'IP par session ou après N actions (ex: toutes les 10-20 min). Support HTTP/SOCKS5 avec authentification. Géolocaliser les proxies pour matcher le fingerprint (ex: proxy US pour un User-Agent américain).
Rotation des headers HTTP: Randomiser les headers comme Referer, Accept-Language, Accept-Encoding, et Connection. Éviter les patterns statiques ; utiliser des listes réalistes.
Gestion des CAPTCHAs: Éviter de les déclencher en imitant les humains ; si déclenchés, intégrer un solver externe (ex: via API). Surveiller les patterns qui les activent (ex: trop de requêtes rapides).

Simulation de comportements humains

Déplacements de souris et clics: Simuler des mouvements courbes avec accélération/décélération (pas linéaires). Utiliser des libs comme puppeteer-mouse pour des paths aléatoires. Pour les likes/comments, ajouter des hovers aléatoires avant clic.
Vitesse de frappe et saisie: Typer les commentaires avec des délais variables par caractère (ex: 100-300ms), inclure des erreurs/backspaces aléatoires pour humaniser.
Scroll fluide et pauses: Implémenter un scroll non-linéaire avec vitesse variable (ex: accélérer puis ralentir). Ajouter des pauses aléatoires (ex: 2-10s) pendant le visionnage de Reels.
Taux d'interaction aléatoires: Like ~20-30% avec variance (ex: Poisson distribution), commentaires ~10-30% avec contenu généré aléatoirement (ex: emojis variés, phrases courtes). Configurable via config.json avec plages min/max.
Durées de session variables: Sessions de 20-40 min avec fin aléatoire ; éviter les durées fixes. Inclure des "pauses inactives" simulées.
Ordre des actions randomisé: Ne pas suivre un ordre fixe (ex: like puis comment) ; randomiser la séquence pour éviter les patterns.
Gestion des erreurs humaine: Sur profil privé/inexistant, simuler un "regard" bref puis back ; loguer sans paniquer.

Limites de sécurité et anti-patterns

Limites quotidiennes: Intégrer des caps hardcodés (ex: max 50 likes/jour/profil, 20 follows, 10 posts) pour éviter les flags d'activité anormale. Configurable mais avec warnings si dépassé.
Randomisation globale: Utiliser des distributions statistiques (ex: normale pour délais) pour tous les timings/interactions. Éviter les boucles prédictibles.
Nettoyage post-session: Fermer le navigateur, effacer la mémoire/cache temporaire, tuer les processus résiduels pour éviter les leaks.
Multi-threading prudent: Pour multi-profils, utiliser async avec délais entre lancements pour éviter les bursts d'activité détectables.
Éviter les signaux d'automatisation avancée: Pas de navigation trop rapide, pas d'accès direct à des URLs internes sans simulation de navigation.

Implémentation technique

Libs et plugins: Utiliser puppeteer-extra-plugin-stealth ou playwright-extra pour des patches automatiques. Injecter des scripts JS custom pour spoofing (ex: via page.evaluate).
Monitoring et adaptation: Logger les détections potentielles (ex: CAPTCHAs, bans) et ajuster dynamiquement (ex: ralentir si warning). Mettre à jour les fingerprints périodiquement via config.
Test et validation: Tester contre des outils comme CreepJS ou FingerprintJS pour vérifier l'unicité des fingerprints. Simuler des runs sur des comptes tests pour mesurer les taux de ban.
Évolution des défenses: Surveiller les updates (ex: unification Chrome headless/headful, VMs TikTok) et patcher en conséquence. Prévoir une modularité pour switcher vers des frameworks comme Nodriver si Puppeteer devient trop détectable.

PACKAGES NUGGETS

Microsoft.Web.WebView2 Pour l'automatisation navigateur + anti-détection
CsvHelper --version 30.0.1  # Parsing CSV pour schedule.csv
ClosedXML --version 0.102.1  # Parsing Excel si tu ajoutes du support
Newtonsoft.Json --version 13.0.3  # JSON pour profiles.json et fingerprints
Serilog --version 3.1.1  # Logging avancé (remplace Logger custom)
Serilog.Sinks.File --version 6.0.0  # Pour rotation logs quotidiens
Serilog.Sinks.Console --version 5.0.1  # Logs en console pour debug




