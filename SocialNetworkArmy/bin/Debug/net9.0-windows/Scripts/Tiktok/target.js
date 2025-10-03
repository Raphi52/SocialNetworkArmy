// target.js - TikTok Target Action
// Paramètre: data = array de targets (usernames)
(async function () {
    const config = {
        viewDurationMin: 5, viewDurationMax: 10,
        likePercent: 20, commentPercent: 30,
        maxReels: 10,
        delayMin: 2000, delayMax: 5000
    };

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function generateRandomComment() {
        const comments = [
            'Amazing! 🔥', 'Love this! ❤️', 'Great video!', 'Keep going!',
            'So cool! 😎', 'Haha funny!', 'Inspiring!'
        ];
        return comments[Math.floor(Math.random() * comments.length)];
    }

    function simulateHumanClick(element) {
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 20;  // Plus de variance pour mobile
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 20;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    function likeVideo() {
        const likeBtn = document.querySelector('svg[data-e2e="like-icon"]')?.closest('div') ||
            document.querySelector('[data-e2e="like-icon"]');
        if (likeBtn && Math.random() < config.likePercent / 100) {
            simulateHumanClick(likeBtn);
            console.log('Like effectué !');
            return true;
        }
        return false;
    }

    async function addComment(comment) {
        const commentBtn = document.querySelector('[data-e2e="comment-icon"]');
        if (commentBtn && Math.random() < config.commentPercent / 100) {
            simulateHumanClick(commentBtn);
            await new Promise(resolve => setTimeout(resolve, 1000));
            const input = document.querySelector('div[contenteditable="true"]');
            if (input) {
                input.focus();
                for (let char of comment) {
                    input.textContent += char;
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    await new Promise(resolve => setTimeout(resolve, randomDelay(100, 300)));
                }
                const sendBtn = document.querySelector('button[data-e2e="send-comment"]');
                if (sendBtn) simulateHumanClick(sendBtn);
                console.log('Commentaire ajouté: ' + comment);
            }
        }
    }

    async function processTarget(username) {
        try {
            window.location.href = `https://www.tiktok.com/@${username}`;
            await new Promise(resolve => setTimeout(resolve, randomDelay(3000, 5000)));

            // Ouvre premier vidéo
            const firstVideo = document.querySelector('a[href*="/video/"]');
            if (!firstVideo) throw new Error('Pas de vidéo trouvée');
            simulateHumanClick(firstVideo);
            await new Promise(resolve => setTimeout(resolve, randomDelay(2000, 4000)));

            likeVideo();
            const comment = generateRandomComment();
            await addComment(comment);

            // Visionne suivants
            let viewed = 0;
            while (viewed < config.maxReels) {
                // Swipe sim (scroll vertical pour mobile)
                window.scrollBy(0, window.innerHeight * (Math.random() > 0.5 ? 1 : -1));
                await new Promise(resolve => setTimeout(resolve, randomDelay(config.viewDurationMin * 1000, config.viewDurationMax * 1000)));
                likeVideo();
                viewed++;
            }

            console.log(`Target ${username} traité.`);
        } catch (error) {
            console.error(`Erreur ${username}: ${error.message} - Skip.`);
        }
    }

    if (!data || !Array.isArray(data)) {
        console.error('Targets manquants !');
        return;
    }

    for (let target of data) {
        await processTarget(target);
        await new Promise(resolve => setTimeout(resolve, randomDelay(config.delayMax, config.delayMax * 2)));
    }

    console.log('Target terminé.');
})();