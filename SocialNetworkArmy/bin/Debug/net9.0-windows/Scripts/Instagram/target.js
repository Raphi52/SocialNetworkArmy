// target.js - Instagram Target Action (version finale pour Reel clic)
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

    // Wait poll renforcé (1s intervals, max 15s)
    async function waitForElement(selector, maxWait = 15000) {
        console.log('Attente élément : ' + selector);
        const start = Date.now();
        while (Date.now() - start < maxWait) {
            const element = document.querySelector(selector);
            if (element) {
                console.log('Élément trouvé ! Selector : ' + selector);
                return element;
            }
            await new Promise(resolve => setTimeout(resolve, 1000)); // Poll 1s
        }
        console.error('Timeout pour ' + selector + ' après ' + maxWait + 'ms');
        return null;
    }

    async function processTarget(username) {
        if (shouldStop()) return 'STOPPED';

        try {
            console.log('Navigation vers ' + username + '/reels/');
            window.location.href = `https://www.instagram.com/${username}/reels/`;
            await new Promise(resolve => setTimeout(resolve, randomDelay(5000, 7000))); // Wait load page

            if (shouldStop()) return 'STOPPED';

            // Force scroll initial pour load Reels (IG lazy-load)
            console.log('Scroll initial pour charger Reels...');
            window.scrollBy(0, window.innerHeight * 3); // Scroll 3x hauteur
            await new Promise(resolve => setTimeout(resolve, 3000));

            if (shouldStop()) return 'STOPPED';

            // Wait pour premier Reel (sélecteurs prioritaires)
            let firstReel = await waitForElement('div[role="feed"] a[href^="/reel/"]:first-of-type', 8000) ||
                await waitForElement('article a[href^="/reel/"]:first-of-type', 5000) ||
                await waitForElement('div[role="article"] a[href^="/reel/"]:first-of-type', 5000) ||
                await waitForElement('a[href^="/reel/"]:first-of-type', 3000); // Global fallback

            if (!firstReel) {
                console.error('Premier Reel non trouvé – retry scroll + wait');
                // Retry scroll + wait
                window.scrollBy(0, window.innerHeight * 2);
                await new Promise(resolve => setTimeout(resolve, 4000));
                firstReel = document.querySelector('div[role="feed"] a[href^="/reel/"]:first-of-type') ||
                    document.querySelector('a[href^="/reel/"]:first-of-type');

                if (!firstReel) {
                    throw new Error('Pas de Reel après retry – profil sans contenu ?');
                }
            }

            console.log('Premier Reel trouvé : ' + firstReel.href);
            simulateHumanClick(firstReel);
            await new Promise(resolve => setTimeout(resolve, randomDelay(2000, 4000)));

            if (shouldStop()) return 'STOPPED';

            // Like + Comment
            likePost();
            const comment = generateRandomComment();
            await addComment(comment);

            // Visionne suivants
            let reelsViewed = 1; // Premier vu
            const numReelsToView = Math.floor(Math.random() * 6) + 5;
            while (reelsViewed < config.maxReels && reelsViewed < numReelsToView) {
                if (shouldStop()) return 'STOPPED';
                const nextBtn = document.querySelector('button[aria-label="Next"]') ||
                    document.querySelector('div[role="button"][tabindex="0"]') ||
                    document.querySelector('svg[aria-label="Next"]')?.closest('button');
                if (nextBtn) {
                    simulateHumanClick(nextBtn);
                    console.log('Next Reel #' + reelsViewed);
                    await new Promise(resolve => setTimeout(resolve, randomDelay(config.viewDurationMin * 1000, config.viewDurationMax * 1000)));
                    likePost();
                } else {
                    window.scrollBy(0, window.innerHeight);
                    await new Promise(resolve => setTimeout(resolve, 5000));
                }
                reelsViewed++;
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