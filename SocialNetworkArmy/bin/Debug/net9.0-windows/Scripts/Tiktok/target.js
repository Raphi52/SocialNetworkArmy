// target.js - Instagram Target Action (version simplifiée : clic direct premier ._aajz)
(async function () {
    const config = {
        viewDurationMin: 5, viewDurationMax: 10,
        likePercent: 20, commentPercent: 30,
        maxReels: 10,
        delayMin: 2000, delayMax: 5000
    };

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
        if (!element) return false;
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 10;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 10;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
        console.log('Clic simulé sur Reel : ', element.href || element.className.substring(0, 20));
        return true;
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
            commentInput.focus();
            for (let char of comment) {
                if (shouldStop()) break;
                commentInput.value += char;
                commentInput.dispatchEvent(new Event('input', { bubbles: true }));
                await new Promise(resolve => setTimeout(resolve, randomDelay(100, 300)));
            }
            if (shouldStop()) return;
            const postBtn = document.querySelector('button[type="submit"]');
            if (postBtn) simulateHumanClick(postBtn);
            console.log('Commentaire ajouté: ' + comment);
        }
    }

    // Wait simple pour premier ._aajz (poll 2s, max 10s)
    async function waitForFirstReel(maxWait = 10000) {
        console.log('Attente premier Reel via a ._aajz...');
        const start = Date.now();
        while (Date.now() - start < maxWait) {
            const firstReel = document.querySelector('a ._aajz');
            if (firstReel) {
                console.log('Premier Reel trouvé ! Href: ' + (firstReel.href || 'N/A'));
                return firstReel;
            }
            console.log('Pas encore trouvé – wait 2s...');
            await new Promise(resolve => setTimeout(resolve, 2000));
        }
        console.error('Timeout – Pas de Reel après ' + maxWait + 'ms');
        return null;
    }

    async function processTarget(username) {
        if (shouldStop()) return 'STOPPED';

        try {
            console.log('Navigation vers ' + username + '/reels/');
            window.location.href = `https://www.instagram.com/${username}/reels/`;
            await new Promise(resolve => setTimeout(resolve, randomDelay(5000, 7000)));

            if (shouldStop()) return 'STOPPED';

            // Scroll forcé pour charger (5x hauteur)
            console.log('Scroll forcé pour charger Reels...');
            for (let i = 0; i < 5; i++) {
                window.scrollBy(0, window.innerHeight);
                await new Promise(resolve => setTimeout(resolve, 1000));
            }

            if (shouldStop()) return 'STOPPED';

            // Wait et clic
            const firstReel = await waitForFirstReel(10000);
            if (!firstReel) {
                console.error('Premier Reel non trouvé – skip profil.');
                throw new Error('No Reel found');
            }

            simulateHumanClick(firstReel);
            await new Promise(resolve => setTimeout(resolve, randomDelay(2000, 4000)));

            if (shouldStop()) return 'STOPPED';

            // Like + Comment
            likePost();
            const comment = generateRandomComment();
            await addComment(comment);

            // Visionne 1-2 suivants (pour test)
            let reelsViewed = 1;
            for (let i = 0; i < 1; i++) {  // Seulement 1 next pour test
                if (shouldStop()) return 'STOPPED';
                const nextBtn = document.querySelector('button[aria-label="Next"]') || document.querySelector('div[role="button"][tabindex="0"]');
                if (nextBtn) {
                    simulateHumanClick(nextBtn);
                    console.log('Next Reel vu.');
                    await new Promise(resolve => setTimeout(resolve, randomDelay(3000, 5000)));
                    likePost();
                    reelsViewed++;
                } else {
                    break;
                }
            }

            console.log(`Target ${username} OK (${reelsViewed} Reels vus).`);
            return 'SUCCESS';
        } catch (error) {
            if (shouldStop()) return 'STOPPED';
            console.error(`Erreur ${username}: ${error.message} – Skip.`);
            return 'ERROR: ' + error.message;
        }
    }

    // Boucle targets
    if (typeof data === 'undefined' || !Array.isArray(data)) {
        console.error('Données targets manquantes !');
        return JSON.stringify({ status: 'ERROR', message: 'No data', reelsClicked: 0 });
    }

    let results = [];
    let reelsClicked = 0;
    for (let i = 0; i < data.length; i++) {
        if (shouldStop()) {
            results.push('STOPPED');
            break;
        }
        const result = await processTarget(data[i]);
        results.push(result);
        if (result === 'SUCCESS') reelsClicked++;
        if (shouldStop()) break;
        if (i < data.length - 1) {
            await new Promise(resolve => setTimeout(resolve, randomDelay(config.delayMax, config.delayMax * 2)));
        }
    }

    const status = shouldStop() ? 'STOPPED' : 'SUCCESS';
    console.log(`Target terminé – Status: ${status}, Targets traités: ${results.length}, Reels cliqués: ${reelsClicked}.`);
    return JSON.stringify({ status, results, reelsClicked });
})();