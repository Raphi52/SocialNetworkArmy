// target.js - Instagram Target Action (mise à jour avec support Stop)
// Paramètre: data = array de targets (usernames, ex: ['user1', 'user2'])
(async function () {
    const config = {
        viewDurationMin: 5, viewDurationMax: 10,  // secondes
        likePercent: 20, commentPercent: 30,
        maxReels: 10,
        delayMin: 2000, delayMax: 5000  // ms entre actions
    };

    // Fonction pour vérifier si le script doit s'arrêter
    function shouldStop() {
        return !window.isRunning;
    }

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function generateRandomComment() {
        const comments = [
            'Super ! 👍', 'J\'adore ! ❤️', 'Contenu génial !', 'Continue comme ça !',
            'Trop cool ! 😊', 'Impressionnant !', 'Haha, bien vu !'
        ];
        return comments[Math.floor(Math.random() * comments.length)];
    }

    function simulateHumanClick(element) {
        // Simulation mouse move + click pour anti-détection
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 10;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 10;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    function likePost() {
        if (shouldStop()) return false;
        const likeBtn = document.querySelector('svg[aria-label="Like"]')?.closest('button') ||
            document.querySelector('[aria-label="Like"]');
        if (likeBtn && Math.random() < config.likePercent / 100) {
            simulateHumanClick(likeBtn);
            console.log('Like effectué !');
            return true;
        }
        return false;
    }

    async function addComment(comment) {
        if (shouldStop()) return;
        const commentInput = document.querySelector('textarea[placeholder*="Comment"]');
        if (commentInput && Math.random() < config.commentPercent / 100) {
            // Typer avec délais humains
            commentInput.focus();
            for (let char of comment) {
                if (shouldStop()) break;
                commentInput.value += char;
                commentInput.dispatchEvent(new Event('input', { bubbles: true }));
                await new Promise(resolve => setTimeout(resolve, randomDelay(100, 300)));
            }
            if (shouldStop()) return;
            // Post comment
            const postBtn = document.querySelector('button[type="submit"]');
            if (postBtn) simulateHumanClick(postBtn);
            console.log('Commentaire ajouté: ' + comment);
        }
    }

    // Fonction pour traiter un target
    async function processTarget(username) {
        if (shouldStop()) {
            console.log('Target interrompu avant traitement de ' + username);
            return;
        }

        try {
            // Navigue vers le profil
            window.location.href = `https://www.instagram.com/${username}/reels/`;
            await new Promise(resolve => setTimeout(resolve, randomDelay(3000, 5000)));  // Attente chargement

            if (shouldStop()) return;

            // Ouvre le dernier Reel (premier dans la liste)
            const firstReel = document.querySelector('a[href*="/reel/"]');
            if (!firstReel) throw new Error('Pas de Reel trouvé');
            simulateHumanClick(firstReel);
            await new Promise(resolve => setTimeout(resolve, randomDelay(2000, 4000)));

            if (shouldStop()) return;

            // Like + Comment sur le Reel actuel
            likePost();
            const comment = generateRandomComment();
            await addComment(comment);

            // Visionne 5-10 Reels suivants
            let reelsViewed = 0;
            const numReelsToView = Math.floor(Math.random() * 6) + 5;
            while (reelsViewed < config.maxReels && reelsViewed < numReelsToView) {
                if (shouldStop()) {
                    console.log('Target interrompu pendant visionnage des Reels pour ' + username);
                    return;
                }
                // Scroll ou next Reel (simulé)
                const nextBtn = document.querySelector('button[aria-label="Next"]') ||
                    document.querySelector('div[role="button"][tabindex="0"]');  // Approximation
                if (nextBtn) {
                    simulateHumanClick(nextBtn);
                    await new Promise(resolve => setTimeout(resolve, randomDelay(config.viewDurationMin * 1000, config.viewDurationMax * 1000)));
                    likePost();  // ~20% chance
                } else {
                    // Fallback: scroll
                    window.scrollBy(0, window.innerHeight);
                    await new Promise(resolve => setTimeout(resolve, 5000));
                }
                reelsViewed++;
            }

            console.log(`Target ${username} traité avec succès.`);
        } catch (error) {
            if (shouldStop()) {
                console.log('Target interrompu pendant erreur pour ' + username);
                return;
            }
            console.error(`Erreur sur ${username}: ${error.message} - Skip.`);
        }
    }

    // Boucle sur les targets (passés via data)
    if (typeof data === 'undefined' || !Array.isArray(data)) {
        console.error('Données targets manquantes !');
        return;
    }

    for (let i = 0; i < data.length; i++) {
        if (shouldStop()) {
            console.log('Target interrompu avant traitement de ' + data[i]);
            break;
        }
        await processTarget(data[i]);
        if (shouldStop()) break;
        if (i < data.length - 1) {  // Pas de délai après le dernier
            await new Promise(resolve => setTimeout(resolve, randomDelay(config.delayMax, config.delayMax * 2)));  // Pause entre targets
        }
    }

    if (shouldStop()) {
        console.log('Target action interrompue par l\'utilisateur.');
    } else {
        console.log('Target action terminée.');
    }
})();