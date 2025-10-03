// publish.js - Instagram Publish Action
// Paramètre: data = array d'entries schedule filtrées (ex: [{mediaPath: 'path/to/media.jpg', description: 'Mon post !'}])
(async function () {
    const config = { delayMin: 3000, delayMax: 6000 };

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function simulateHumanClick(element) {
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 10;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 10;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    // Navigue vers création post
    if (!window.location.href.includes('/p/') && !window.location.href.includes('create')) {
        // Clique sur + pour créer (assume sur home/feed)
        const createBtn = document.querySelector('svg[aria-label="New post"]')?.closest('button') ||
            document.querySelector('[aria-label="New post"]');
        if (createBtn) {
            simulateHumanClick(createBtn);
            await new Promise(resolve => setTimeout(resolve, 2000));
        } else {
            window.location.href = 'https://www.instagram.com/';
            await new Promise(resolve => setTimeout(resolve, 3000));
        }
    }

    if (typeof data === 'undefined' || !Array.isArray(data)) {
        console.error('Données schedule manquantes !');
        return;
    }

    for (let entry of data) {
        try {
            // Upload média (simulé ; en réalité, utilise File API ou CDP depuis C# pour vrai upload)
            const uploadInput = document.querySelector('input[type="file"]');
            if (uploadInput) {
                // Note: Pour vrai upload, passe le fichier via C# (WebView2.FileUpload) ; ici, simule
                uploadInput.click();
                console.log(`Upload simulé: ${entry.mediaPath}`);
                await new Promise(resolve => setTimeout(resolve, 2000));  // Délai pour "upload"
            }

            // Ajoute description
            const captionInput = document.querySelector('textarea[placeholder*="What\'s on your mind"]');
            if (captionInput) {
                captionInput.focus();
                captionInput.value = entry.description;
                captionInput.dispatchEvent(new Event('input', { bubbles: true }));
            }

            // Poste
            const shareBtn = document.querySelector('button[type="button"]:has-text("Share")') ||
                document.querySelector('[aria-label="Share"]');
            if (shareBtn) {
                simulateHumanClick(shareBtn);
                console.log(`Publication réussie: ${entry.description.substring(0, 20)}...`);
            } else {
                throw new Error('Bouton Share non trouvé');
            }

            await new Promise(resolve => setTimeout(resolve, randomDelay(config.delayMin, config.delayMax)));
        } catch (error) {
            console.error(`Erreur publication ${entry.mediaPath}: ${error.message}`);
        }
    }

    console.log('Publish action terminée.');
})();