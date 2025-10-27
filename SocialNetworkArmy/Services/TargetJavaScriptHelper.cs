namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Scripts JavaScript pour les interactions avec les reels Instagram.
    /// Contient tous les scripts pour la navigation, détection de boutons, etc.
    /// </summary>
    public static class TargetJavaScriptHelper
    {
        /// <summary>
        /// Script pour trouver et cliquer sur le bouton "Suivant" dans une modale.
        /// Sélectionne le bouton le plus à droite (stratégie simple et efficace).
        /// </summary>
        public static string GetNextButtonScript()
        {
            return @"
(function(){
  try {
    console.log('[TEST] Script started');

    var dialog = document.querySelector('div[role=""dialog""]');
    if (!dialog) {
      console.log('[TEST] No dialog');
      return 'NO_DIALOG';
    }

    console.log('[TEST] Dialog found');

    // Stratégie simple : dernier bouton visible
    var allBtns = Array.from(dialog.querySelectorAll('button')).filter(b => {
      var rect = b.getBoundingClientRect();
      return b.offsetWidth > 0 && b.offsetHeight > 0 && rect.width > 0;
    });

    console.log('[TEST] Found ' + allBtns.length + ' buttons');

    if (allBtns.length < 2) {
      console.log('[TEST] Not enough buttons');
      return 'NO_NEXT_BUTTON';
    }

    // Trier par position horizontale, prendre le dernier (à droite)
    allBtns.sort(function(a, b) {
      return a.getBoundingClientRect().left - b.getBoundingClientRect().left;
    });

    var nextBtn = allBtns[allBtns.length - 1];
    console.log('[TEST] Selected rightmost button');

    var rect = nextBtn.getBoundingClientRect();
    var clientX = Math.floor(rect.left + rect.width / 2);
    var clientY = Math.floor(rect.top + rect.height / 2);

    console.log('[TEST] Click at ' + clientX + ',' + clientY);

    nextBtn.click();

    var result = 'NEXT_CLICKED:' + clientX + ',' + clientY;
    console.log('[TEST] Returning: ' + result);
    return result;

  } catch(e) {
    console.error('[TEST] Error: ' + e.message);
    return 'EXCEPTION: ' + e.message;
  }
})()";
        }

        /// <summary>
        /// Script pour vérifier si un reel est ouvert (URL, dialog ou overlay vidéo).
        /// </summary>
        public static string GetReelOpenedCheckScript()
        {
            return @"
(function(){
  const urlHasReel = window.location.href.includes(""/reel/"");
  const hasDialog  = !!document.querySelector('div[role=""dialog""]');
  const hasOverlay = !!document.querySelector('[data-visualcompletion=""ignore""] video, video');
  return (urlHasReel || hasDialog || hasOverlay).toString();
})()";
        }

        /// <summary>
        /// Script pour cliquer sur un lien de reel (première tentative).
        /// </summary>
        public static string GetClickReelLinkScript()
        {
            return @"
(function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_REEL_LINK';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  el.click();
  return 'CLICKED';
})()";
        }

        /// <summary>
        /// Script pour cliquer avec événements souris complets (fallback robuste).
        /// </summary>
        public static string GetClickReelWithMouseEventsScript()
        {
            return @"
(async function(){
  const el = document.querySelector('article a[href*=""/reel/""]')
          || document.querySelector('a[href*=""/reel/""]');
  if(!el) return 'NO_EL';
  el.scrollIntoView({behavior:'smooth', block:'center'});
  await new Promise(r=>setTimeout(r,500));
  const r = el.getBoundingClientRect();
  const x = r.left + r.width/2, y = r.top + r.height/2;
  el.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,clientX:x,clientY:y}));
  el.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,clientX:x,clientY:y}));
  return 'MOUSE_EVENTS_SENT';
})()";
        }

        /// <summary>
        /// Script pour extraire l'ID du reel depuis l'URL.
        /// </summary>
        public static string GetReelIdScript()
        {
            return @"
(function(){
  const match = window.location.href.match(/\/reel\/([^\/]+)/);
  return match ? match[1] : 'NO_ID';
})()";
        }

        /// <summary>
        /// Script pour extraire la date de publication du reel.
        /// </summary>
        public static string GetReelDateScript()
        {
            return @"
(function(){
  const timeEl = document.querySelector('time.x1p4m5qa');
  if (timeEl) {
    const datetime = timeEl.getAttribute('datetime') || 'NO_DATETIME';
    const text = timeEl.textContent || 'NO_TEXT';
    return JSON.stringify({datetime: datetime, text: text});
  } else {
    return 'NO_DATE_FOUND';
  }
})()";
        }

        /// <summary>
        /// Script pour naviguer au reel suivant avec la touche ArrowRight.
        /// </summary>
        public static string GetArrowRightScript()
        {
            return @"
(function(){
  try{
    document.body.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    document.body.dispatchEvent(new KeyboardEvent('keyup', {key:'ArrowRight', code:'ArrowRight', keyCode:39, bubbles:true}));
    return 'ARROW_KEY_USED';
  }catch(e){ return 'JSERR: ' + String(e); }
})()";
        }

        /// <summary>
        /// Script pour vérifier si on est synchronisé sur le bon reel après navigation.
        /// </summary>
        public static string GetCheckReelIdScript()
        {
            return @"
(function(){
  const match = window.location.href.match(/\/reel\/([^\/]+)/);
  return match ? match[1] : 'NO_ID';
})()";
        }

        /// <summary>
        /// Vérifie si un résultat JavaScript est "true".
        /// </summary>
        public static bool JsBoolIsTrue(string jsResult)
        {
            if (string.IsNullOrWhiteSpace(jsResult)) return false;
            var s = jsResult.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
                s = s.Substring(1, s.Length - 2);
            return s.Equals("true", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
