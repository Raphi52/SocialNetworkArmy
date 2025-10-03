// target.js - Instagram Target Action (log URL + wait renforcé ._aajz)
(async function () {
    const config = {
        viewDurationMin: 5, viewDurationMax: 10,
        likePercent: 20, commentPercent: 30,
        maxReels: 1,
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
        element.scrollIntoView({ behavior: 'smooth', block: 'center' });
        await new Promise(resolve => setTimeout(resolve, 1000));
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 10;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 10;
        const down = new MouseEvent('mousedown', { bubbles: true, clientX: x, clientY: y });
        const up = new MouseEvent('mouseup', { bubbles: true, clientX: x, clientY: y });
        const click = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(down);
        element.dispatchEvent(up);
        element.dispatchEvent(click);
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

    // Wait renforcé pour premier ._aajz (poll 500ms, scroll si 0, max 30s)
    async function waitForFirstReel(maxWait = 30000) {
        console.log('Wait renforcé pour a ._aajz...');
        const start = Date.now();
        let scrollCount = 0;
        while (Date.now() - start < maxWait) {
            const allReels = document.querySelectorAll('a ._aajz');
            console.log(`Éléments a ._aajz trouvés : ${allReels.length}`);
            if (allReels.length > 0) {
                const firstReel = allReels[0];
                const rect = firstReel.getBoundingClientRect();
                console.log('Premier Reel – Top: ' + rect.top + ', Href: ' + (firstReel.href || 'N/A'));
                if (rect.top >= -100) {
                    console.log('Premier Reel visible – prêt pour clic !');
                    return firstReel;
                } else {
                    console.log('Premier Reel hors viewport – scroll auto...');
                    firstReel.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    await new Promise(resolve => setTimeout(resolve, 1500));
                }
            } else {
                console.log('Pas d\'a ._aajz – scroll pour charger...');
                window.scrollBy(0, window.innerHeight);
                scrollCount++;
                if (scrollCount > 5) break; // Max 5 scrolls
                await new Promise(resolve => setTimeout(resolve, 2000));
            }
            await new Promise(resolve => setTimeout(resolve, 500)); // Poll 0.5s
        }
        console.error('Timeout – Pas de Reel après ' + maxWait + 'ms et ' + scrollCount + ' scrolls');
        return null;
    }

    async function processTarget(username) {
        if (shouldStop()) return 'STOPPED';

        try {
            console.log('Navigation vers ' + username + '/reels/');
            window.location.href = `https://www.instagram.com/${username}/reels/`;
            await new Promise(resolve => setTimeout(resolve, randomDelay(6000, 9000))); // Wait load étendu

            if (shouldStop()) return 'STOPPED';

            // Log URL pour check navigation
            console.log('URL après nav : ' + window.location.href);

            // Scroll forcé upfront (5x)
            console.log('Scroll forcé pour Reels...');
            for (let i = 0; i < 5; i++) {
                window.scrollBy(0, window.innerHeight);
                await new Promise(resolve => setTimeout(resolve, 1500));
            }

            if (shouldStop()) return 'STOPPED';

            // Wait et clic
            const firstReel = await waitForFirstReel(30000);
            if (!firstReel) {
                console.error('Premier Reel non trouvé – skip.');
                throw new Error('No Reel');
            }

            console.log('Premier Reel prêt – Clic !');
            simulateHumanClick(firstReel);
            await new Promise(resolve => setTimeout(resolve, randomDelay(3000, 5000)));

            if (shouldStop()) return 'STOPPED';

            // Like + Comment
            likePost();
            const comment = generateRandomComment();
            await addComment(comment);

            console.log(`Target ${username} OK.`);
            return 'SUCCESS';
        } catch (error) {
            if (shouldStop()) return 'STOPPED';
            console.error(`Erreur ${username}: ${error.message}`);
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