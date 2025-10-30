using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SocialNetworkArmy.Services
{
    /// <summary>
    /// Service pour synchroniser les cookies entre InstagramBotForm (Main) et StoryPosterForm
    /// Utilise un dossier commun "Shared" pour garantir la persistence
    /// </summary>
    public class SharedCookiesService
    {
        private readonly string profileName;
        private readonly string sharedCookiesDir;
        private readonly string mainUserDataDir;
        private readonly string storyUserDataDir;

        public SharedCookiesService(string profileName)
        {
            this.profileName = profileName;

            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SocialNetworkArmy", "Profiles", profileName
            );

            // ✅ DOSSIER COMMUN pour les cookies partagés
            sharedCookiesDir = Path.Combine(baseDir, "Shared", "Default");

            // Dossiers spécifiques à chaque form
            mainUserDataDir = Path.Combine(baseDir, "Main", "Default");
            storyUserDataDir = Path.Combine(baseDir, "Story", "Default");

            // Créer le dossier partagé
            Directory.CreateDirectory(sharedCookiesDir);
        }

        public async Task<bool> LoadSharedCookiesAsync(string targetDir, Action<string> log = null)
        {
            const int maxRetries = 5;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    log?.Invoke($"[Cookies] Loading shared cookies (attempt {retryCount + 1}/{maxRetries})...");
                    Directory.CreateDirectory(targetDir);

                    // ✅ ATTENDRE QUE LE DOSSIER SOIT ACCESSIBLE
                    await Task.Delay(500);

                    var filesToCopy = new[]
                    {
                "Cookies",
                "Cookies-journal",
                "Network Persistent State",
                "Preferences",
                "Local State"
            };

                    bool anyCopied = false;
                    var failedFiles = new List<string>();

                    foreach (var fileName in filesToCopy)
                    {
                        var sourceFile = Path.Combine(sharedCookiesDir, fileName);
                        var targetFile = Path.Combine(targetDir, fileName);

                        if (File.Exists(sourceFile))
                        {
                            // ✅ VÉRIFIER QUE LE FICHIER N'EST PAS VIDE
                            var fileInfo = new FileInfo(sourceFile);
                            if (fileInfo.Length == 0)
                            {
                                log?.Invoke($"[Cookies] Skipping empty file: {fileName}");
                                continue;
                            }

                            bool copied = false;
                            for (int i = 0; i < 3; i++)
                            {
                                try
                                {
                                    // ✅ COPIE AVEC PARTAGE DE LECTURE
                                    using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    using (var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        await sourceStream.CopyToAsync(targetStream);
                                    }

                                    // ✅ VÉRIFIER QUE LA COPIE EST VALIDE
                                    var targetInfo = new FileInfo(targetFile);
                                    if (targetInfo.Length > 0)
                                    {
                                        anyCopied = true;
                                        copied = true;
                                        log?.Invoke($"[Cookies] ✓ Copied {fileName} ({targetInfo.Length / 1024}KB)");
                                        break;
                                    }
                                    else
                                    {
                                        File.Delete(targetFile);
                                        throw new IOException("Target file is empty after copy");
                                    }
                                }
                                catch (IOException ex) when (i < 2)
                                {
                                    log?.Invoke($"[Cookies] Retry {fileName} (locked): {ex.Message}");
                                    await Task.Delay(1000 * (i + 1)); // Backoff progressif
                                }
                                catch (Exception ex)
                                {
                                    if (i == 2)
                                    {
                                        failedFiles.Add($"{fileName}: {ex.Message}");
                                        log?.Invoke($"[Cookies] ✗ Failed {fileName}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }

                    // Copier les dossiers avec la même logique
                    await CopyDirectorySafeAsync(
                        Path.Combine(sharedCookiesDir, "Local Storage"),
                        Path.Combine(targetDir, "Local Storage"),
                        log
                    );

                    if (anyCopied)
                    {
                        log?.Invoke("[Cookies] ✓ Shared cookies loaded successfully");
                        if (failedFiles.Any())
                            log?.Invoke($"[Cookies] ⚠️ Some files failed: {string.Join(", ", failedFiles)}");
                        return true;
                    }
                    else if (retryCount == 0)
                    {
                        log?.Invoke("[Cookies] ℹ️ No shared cookies found (first login)");
                        return false;
                    }

                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        log?.Invoke($"[Cookies] Retrying in {retryCount}s...");
                        await Task.Delay(retryCount * 1000);
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[Cookies] ⚠️ Load error: {ex.Message}");
                    retryCount++;
                    if (retryCount < maxRetries)
                        await Task.Delay(retryCount * 1000);
                }
            }

            log?.Invoke("[Cookies] ⚠️ Max retries reached");
            return false;
        }
        private async Task CopyDirectorySafeAsync(string sourceDir, string targetDir, Action<string> log = null)
        {
            if (!Directory.Exists(sourceDir))
                return;

            try
            {
                Directory.CreateDirectory(targetDir);

                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var fileName = Path.GetFileName(file);
                    var targetFile = Path.Combine(targetDir, fileName);

                    try
                    {
                        // ✅ COPIE AVEC FILESTREAM POUR GÉRER LES FICHIERS VERROUILLÉS
                        using (var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await sourceStream.CopyToAsync(targetStream);
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"[Cookies] Skip locked file {fileName}: {ex.Message}");
                    }
                }

                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    var dirName = Path.GetFileName(dir);
                    await CopyDirectorySafeAsync(dir, Path.Combine(targetDir, dirName), log);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Cookies] Directory copy error: {ex.Message}");
            }
        }

        public async Task SaveSharedCookiesAsync(string sourceDir, Action<string> log = null)
        {
            try
            {
                log?.Invoke($"[Cookies] Saving cookies from {Path.GetFileName(Path.GetDirectoryName(sourceDir))} to Shared...");

                // Attendre que WebView2 écrive les cookies
                await Task.Delay(1500);
                Directory.CreateDirectory(sharedCookiesDir);

                var filesToSave = new[]
                {
            "Cookies",
            "Cookies-journal",
            "Network Persistent State",
            "Preferences",
            "Local State"
        };

                bool anySaved = false;
                List<string> savedFiles = new List<string>();

                foreach (var fileName in filesToSave)
                {
                    var sourceFile = Path.Combine(sourceDir, fileName);
                    var targetFile = Path.Combine(sharedCookiesDir, fileName);

                    if (File.Exists(sourceFile))
                    {
                        // Vérifier que le fichier n'est pas vide
                        var fileInfo = new FileInfo(sourceFile);
                        if (fileInfo.Length == 0)
                        {
                            log?.Invoke($"[Cookies] ⚠️ Skipping empty file: {fileName}");
                            continue;
                        }

                        bool saved = false;
                        for (int i = 0; i < 5; i++)
                        {
                            try
                            {
                                // Créer une copie temporaire d'abord
                                var tempFile = targetFile + ".tmp";
                                File.Copy(sourceFile, tempFile, true);

                                // Vérifier que la copie est valide
                                var tempInfo = new FileInfo(tempFile);
                                if (tempInfo.Length > 0)
                                {
                                    // Remplacer l'ancien fichier
                                    File.Delete(targetFile);
                                    File.Move(tempFile, targetFile);

                                    anySaved = true;
                                    saved = true;
                                    savedFiles.Add(fileName);
                                    break;
                                }
                                else
                                {
                                    File.Delete(tempFile);
                                    throw new IOException("Temp file is empty");
                                }
                            }
                            catch (IOException) when (i < 4)
                            {
                                await Task.Delay(500 * (i + 1));
                            }
                            catch (Exception ex)
                            {
                                if (i == 4)
                                    log?.Invoke($"[Cookies] ⚠️ Failed to save {fileName}: {ex.Message}");
                            }
                        }
                    }
                }

                // Sauvegarder Local Storage et Session Storage
                await CopyDirectoryAsync(
                    Path.Combine(sourceDir, "Local Storage"),
                    Path.Combine(sharedCookiesDir, "Local Storage"),
                    log
                );

                await CopyDirectoryAsync(
                    Path.Combine(sourceDir, "Session Storage"),
                    Path.Combine(sharedCookiesDir, "Session Storage"),
                    log
                );

                if (anySaved)
                {
                    log?.Invoke($"[Cookies] ✓ Saved: {string.Join(", ", savedFiles)}");
                }
                else
                {
                    log?.Invoke("[Cookies] ℹ️ No cookies to save");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Cookies] ⚠️ Save error: {ex.Message}");
                log?.Invoke($"[Cookies] Stack: {ex.StackTrace}");
            }
        }
        private async Task CopyDirectoryAsync(string sourceDir, string targetDir, Action<string> log = null)
        {
            if (!Directory.Exists(sourceDir))
                return;

            try
            {
                Directory.CreateDirectory(targetDir);

                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var fileName = Path.GetFileName(file);
                    var targetFile = Path.Combine(targetDir, fileName);

                    try
                    {
                        File.Copy(file, targetFile, true);
                    }
                    catch
                    {
                        // Ignorer les fichiers verrouillés
                    }
                }

                foreach (var dir in Directory.GetDirectories(sourceDir))
                {
                    var dirName = Path.GetFileName(dir);
                    await CopyDirectoryAsync(dir, Path.Combine(targetDir, dirName), log);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Cookies] Directory copy error: {ex.Message}");
            }
        }

        /// <summary>
        /// Nettoie les anciens cookies (optionnel, pour maintenance)
        /// </summary>
        public void CleanOldCookies(Action<string> log = null)
        {
            try
            {
                var dirs = new[] { mainUserDataDir, storyUserDataDir };

                foreach (var dir in dirs)
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }

                log?.Invoke("[Cookies] ✓ Old session data cleaned");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Cookies] Clean error: {ex.Message}");
            }
        }
        public bool AreSharedCookiesValid()
        {
            try
            {
                var cookiesFile = Path.Combine(sharedCookiesDir, "Cookies");

                if (!File.Exists(cookiesFile))
                    return false;

                var fileInfo = new FileInfo(cookiesFile);

                // ✅ Vérifier que le fichier existe, n'est pas vide, et n'est pas trop vieux
                if (fileInfo.Length == 0)
                    return false;

                // ✅ Cookies de plus de 7 jours = probablement expirés
                var ageInDays = (DateTime.Now - fileInfo.LastWriteTime).TotalDays;
                if (ageInDays > 7)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Vérifie si des cookies partagés existent (utile pour détecter première connexion)
        /// </summary>
        public bool HasSharedCookies()
        {
            var cookiesFile = Path.Combine(sharedCookiesDir, "Cookies");
            return File.Exists(cookiesFile) && new FileInfo(cookiesFile).Length > 0;
        }
    }
}