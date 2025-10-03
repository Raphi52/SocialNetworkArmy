// publish.js - TikTok Publish Action
// Paramètre: data = array d'entries
(async function () {
    const config = { delayMin: 3000, delayMax: 6000 };

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function simulateHumanClick(element) {
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 20;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 20;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    // Navigue vers upload
    const uploadBtn = document.querySelector('[data-e2e="upload-icon"]') || document.querySelector('[aria-label="Upload"]');
    if (uploadBtn) {
        simulateHumanClick(uploadBtn);
        await new Promise(resolve => setTimeout(resolve, 2000));
    } else {
        window.location.href = 'https://www.tiktok.com/upload?lang=en';
    }

    if (!data || !Array.isArray(data)) {
        console.error('Schedule manquant !');
        return;
    }

    for (let entry of data) {
        try {
            // Upload (simulé ; utilise C# pour vrai file)
            const fileInput = document.querySelector('input[type="file"]');
            if (fileInput) {
                fileInput.click();
                console.log(`Upload: ${entry.mediaPath}`);
                await new Promise(resolve => setTimeout(resolve, 3000));
            }

            // Description
            const descInput = document.querySelector('textarea[placeholder*="Describe"]');
            if (descInput) {
                descInput.value = entry.description;
                descInput.dispatchEvent(new Event('input', { bubbles: true }));
            }

            // Poste
            const postBtn = document.querySelector('button[data-e2e="post-button"]');
            if (postBtn) {
                simulateHumanClick(postBtn);
                console.log(`Publié: ${entry.description.substring(0, 20)}...`);
            } else {
                throw new Error('Bouton Post non trouvé');
            }

            await new Promise(resolve => setTimeout(resolve, randomDelay(config.delayMin, config.delayMax)));
        } catch (error) {
            console.error(`Erreur ${entry.mediaPath}: ${error.message}`);
        }
    }

    console.log('Publish terminé.');
})();