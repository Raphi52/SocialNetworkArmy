# 🛡️ Guide d'intégration - Services de Robustesse

Ce guide explique comment intégrer les 3 nouveaux services dans vos classes existantes.

---

## 📦 Services disponibles

### 1. **ErrorHandler** - Gestion d'erreurs avec retry automatique
### 2. **HealthCheckService** - Vérification de santé système
### 3. **SafeModeDetector** - Protection contre les abus

---

## 🔄 ErrorHandler - Comment l'utiliser

### Installation

```csharp
// Dans votre service (PublishService, TargetService, etc.)
private readonly ErrorHandler errorHandler;

public YourService(WebView2 webView, TextBox logTextBox)
{
    this.errorHandler = new ErrorHandler(logTextBox);
}
```

### Utilisation basique - Retry automatique

```csharp
// Avant (sans retry):
var result = await SomeRiskyOperationAsync();

// Après (avec retry automatique):
var result = await errorHandler.ExecuteWithRetryAsync(
    async () => await SomeRiskyOperationAsync(),
    maxRetries: 3,              // 3 tentatives max
    baseDelayMs: 2000,          // Délai de base 2s
    operationName: "Upload Media"
);
// ✅ Auto-retry avec exponential backoff: 2s → 4s → 8s
```

### Exemple complet - Publier une photo

```csharp
try
{
    await errorHandler.ExecuteWithRetryAsync(async () =>
    {
        // 1. Click sur le bouton "New Post"
        await ClickNewPostButtonAsync();

        // 2. Upload de l'image
        await UploadImageAsync(imagePath);

        // 3. Ajouter la caption
        await AddCaptionAsync(caption);

        // 4. Publier
        await ClickPublishAsync();

        return true;
    },
    maxRetries: 3,
    operationName: "Publish Instagram Post");

    logTextBox.AppendText("[SUCCESS] Post published!\r\n");
}
catch (Exception ex)
{
    logTextBox.AppendText($"[FAILED] Could not publish after retries: {ex.Message}\r\n");

    // Vérifier si on doit activer le safe mode
    if (errorHandler.ShouldEnterSafeMode())
    {
        // Activer le safe mode (voir section SafeModeDetector)
    }
}
```

### Détection d'erreurs spécifiques

```csharp
try
{
    await SomeOperation();
}
catch (Exception ex)
{
    var errorType = errorHandler.DetectErrorType(ex);

    switch (errorType)
    {
        case ErrorHandler.ErrorType.InstagramRateLimit:
            // Pause de 15 minutes
            await Task.Delay(TimeSpan.FromMinutes(15));
            break;

        case ErrorHandler.ErrorType.ShadowBan:
            // Arrêter tout et alerter
            StopAllAutomation();
            break;

        case ErrorHandler.ErrorType.ElementNotFound:
            // Retry rapide
            await Task.Delay(1000);
            break;
    }
}
```

---

## ❤️ HealthCheckService - Vérifier que tout fonctionne

### Installation

```csharp
private readonly HealthCheckService healthCheck;

public YourService(WebView2 webView, TextBox logTextBox)
{
    this.healthCheck = new HealthCheckService(webView, logTextBox);
}
```

### Vérification complète avant de démarrer

```csharp
public async Task StartAutomationAsync()
{
    // ✅ ALWAYS check health before starting
    var health = await healthCheck.PerformHealthCheckAsync();

    if (!health.IsHealthy)
    {
        MessageBox.Show(
            $"Cannot start automation:\n{health.ErrorMessage}",
            "Health Check Failed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return;
    }

    if (!health.IsLoggedIn)
    {
        MessageBox.Show(
            "Please login to Instagram first",
            "Not Logged In",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return;
    }

    // Tout est bon, on peut démarrer
    logTextBox.AppendText($"[HEALTH] All systems operational ({health.ResponseTimeMs}ms)\r\n");
    await StartActualAutomationAsync();
}
```

### Quick check pendant l'exécution

```csharp
// Vérification rapide toutes les 5 minutes
while (isRunning)
{
    await DoSomeWork();

    if (++actionCount % 50 == 0)  // Tous les 50 actions
    {
        var isHealthy = await healthCheck.QuickHealthCheckAsync();
        if (!isHealthy)
        {
            logTextBox.AppendText("[HEALTH] Connection lost, pausing...\r\n");
            await Task.Delay(TimeSpan.FromMinutes(2));
        }
    }
}
```

---

## 🛡️ SafeModeDetector - Protection automatique

### Installation

```csharp
private readonly SafeModeDetector safeMode;

public YourService(WebView2 webView, TextBox logTextBox)
{
    this.safeMode = new SafeModeDetector(logTextBox);
}
```

### Intégration complète dans une boucle d'automation

```csharp
public async Task RunTargetAutomationAsync()
{
    foreach (var target in targets)
    {
        // ✅ 1. Vérifier si on peut faire une action
        if (!safeMode.CanPerformAction(out string reason))
        {
            logTextBox.AppendText($"[RATE_LIMIT] {reason}\r\n");
            await Task.Delay(TimeSpan.FromMinutes(1));
            continue;
        }

        // ✅ 2. Vérifier si safe mode doit être activé
        if (safeMode.ShouldActivateSafeMode(out string safeModeReason))
        {
            safeMode.ActivateSafeMode(safeModeReason);
            await Task.Delay(TimeSpan.FromMinutes(10));
            safeMode.DeactivateSafeMode();
            continue;
        }

        try
        {
            // Faire l'action
            await LikePostAsync(target);

            // ✅ 3. Enregistrer l'action
            safeMode.RecordAction();

            // ✅ 4. Afficher le rapport de santé
            logTextBox.AppendText($"[STATS] {safeMode.GetHealthReport()}\r\n");
        }
        catch (Exception ex)
        {
            // ✅ 5. Enregistrer l'erreur
            var errorType = errorHandler.DetectErrorType(ex);
            safeMode.RecordError(errorType.ToString());
        }

        // Pause humaine
        await Task.Delay(Random.Next(2000, 5000));
    }
}
```

### Exemple simple - Like avec protection

```csharp
public async Task LikePostWithProtectionAsync(string postUrl)
{
    // Vérifier les limites
    if (!safeMode.CanPerformAction(out string reason))
    {
        logTextBox.AppendText($"[BLOCKED] {reason}\r\n");
        throw new InvalidOperationException(reason);
    }

    try
    {
        // Faire le like
        await NavigateToPostAsync(postUrl);
        await ClickLikeButtonAsync();

        // Enregistrer le succès
        safeMode.RecordAction();

        logTextBox.AppendText($"[LIKE] ✓ Post liked ({safeMode.GetActionsPerHour()} actions today)\r\n");
    }
    catch (Exception ex)
    {
        safeMode.RecordError("LikeError");
        throw;
    }
}
```

---

## 🎯 Exemple complet - PublishService avec tous les services

```csharp
public class PublishService
{
    private readonly ErrorHandler errorHandler;
    private readonly HealthCheckService healthCheck;
    private readonly SafeModeDetector safeMode;

    public PublishService(WebView2 webView, TextBox logTextBox)
    {
        this.errorHandler = new ErrorHandler(logTextBox);
        this.healthCheck = new HealthCheckService(webView, logTextBox);
        this.safeMode = new SafeModeDetector(logTextBox);
    }

    public async Task PublishPostAsync(string imagePath, string caption)
    {
        // ✅ 1. Health check
        var health = await healthCheck.PerformHealthCheckAsync();
        if (!health.IsHealthy || !health.IsLoggedIn)
        {
            throw new Exception("System not ready");
        }

        // ✅ 2. Rate limiting
        if (!safeMode.CanPerformAction(out string reason))
        {
            logTextBox.AppendText($"[RATE_LIMIT] {reason}\r\n");
            await Task.Delay(TimeSpan.FromMinutes(1));
        }

        // ✅ 3. Publish with retry
        await errorHandler.ExecuteWithRetryAsync(async () =>
        {
            await UploadImageAsync(imagePath);
            await SetCaptionAsync(caption);
            await ClickPublishButtonAsync();

            // ✅ 4. Enregistrer l'action
            safeMode.RecordAction();

            return true;
        },
        maxRetries: 3,
        operationName: "Publish Post");

        // ✅ 5. Check if safe mode needed
        if (safeMode.ShouldActivateSafeMode(out string safeModeReason))
        {
            safeMode.ActivateSafeMode(safeModeReason);
        }

        logTextBox.AppendText($"[SUCCESS] Post published\r\n");
        logTextBox.AppendText($"[STATS] {safeMode.GetHealthReport()}\r\n");
    }
}
```

---

## 📊 Statistiques et Monitoring

### Afficher les stats en temps réel

```csharp
// Dans votre Form, ajouter un Timer
private System.Windows.Forms.Timer statsTimer;

private void InitializeStatsMonitoring()
{
    statsTimer = new System.Windows.Forms.Timer { Interval = 5000 }; // Toutes les 5s
    statsTimer.Tick += (s, e) =>
    {
        var report = safeMode.GetHealthReport();
        statusLabel.Text = report;

        // Changer la couleur selon l'état
        if (safeMode.IsSafeModeActive())
        {
            statusLabel.ForeColor = Color.Red;
        }
        else if (safeMode.GetActionsPerMinute() > 20)
        {
            statusLabel.ForeColor = Color.Orange;
        }
        else
        {
            statusLabel.ForeColor = Color.Green;
        }
    };
    statsTimer.Start();
}
```

---

## 🚨 Alertes et Notifications

### Configurer les alertes importantes

```csharp
// Vérifier périodiquement si tout va bien
private async Task MonitorHealthAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), ct);

        var health = await healthCheck.PerformHealthCheckAsync();

        if (!health.IsHealthy)
        {
            // Envoyer une notification (email, Discord, etc.)
            SendAlert($"Health check failed: {health.ErrorMessage}");
        }

        if (safeMode.GetRecentErrorCount() > 5)
        {
            SendAlert("Too many errors detected!");
        }
    }
}
```

---

## ✅ Checklist d'intégration

- [ ] Ajouter ErrorHandler à tous les services qui font des appels réseau
- [ ] Faire un HealthCheck au démarrage de chaque automation
- [ ] Intégrer SafeModeDetector dans les boucles d'actions
- [ ] Enregistrer chaque action avec `RecordAction()`
- [ ] Enregistrer chaque erreur avec `RecordError()`
- [ ] Afficher les stats avec `GetHealthReport()`
- [ ] Tester le safe mode en forçant des erreurs
- [ ] Vérifier que les alertes s'affichent correctement

---

## 🎓 Best Practices

1. **Toujours faire un health check avant de démarrer**
2. **Utiliser ExecuteWithRetryAsync pour toutes les opérations critiques**
3. **Enregistrer CHAQUE action dans SafeModeDetector**
4. **Vérifier CanPerformAction() avant chaque action**
5. **Ne jamais ignorer les alertes de safe mode**
6. **Logger les statistiques régulièrement pour monitoring**

---

## 🤝 Support

Ces services sont prêts à l'emploi. Pour toute question:
- Lire les commentaires XML dans le code
- Consulter les exemples ci-dessus
- Tester dans un environnement de dev d'abord

🚀 **Bonne intégration!**
