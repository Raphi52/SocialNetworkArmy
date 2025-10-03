// scroll.js - Instagram Scroll Action
(async function () {
    const config = {
        sessionDurationMin: 20 * 60 * 1000,  // 20 min en ms
        sessionDurationMax: 40 * 60 * 1000,
        likePercent: 30, commentPercent: 30,
        viewDurationMin: 5, viewDurationMax: 10  // sec
    };

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function generateRandomComment() {
        const comments = [
            'Wow ! 🔥', 'Trop bien !', 'Super vidéo !', 'J\'aime ! 😍',
            'Intéressant !', 'Haha !', 'Bravo ! 👏'
        ];
        return comments[Math.floor(Math.random() * comments.length)];
    }

    function simulateHumanClick(element) {
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 10;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 10;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    function likeReel() {
        const likeBtn = document.querySelector('svg[aria-label="Like"]')?.closest('button');
        if (likeBtn && Math.random() < config.likePercent / 100) {
            simulateHumanClick(likeBtn);
            return true;
        }
        return false;
    }

    async function addComment(comment) {
        const commentInput = document.querySelector('textarea[placeholder*="Add a comment"]');
        if (commentInput && Math.random() < config.commentPercent / 100) {
            commentInput.focus();
            for (let char of comment) {
                commentInput.value += char;
                commentInput.dispatchEvent(new Event('input', { bubbles: true }));
                await new Promise(resolve => setTimeout(resolve, randomDelay(100, 300)));
            }
            const postBtn = document.querySelector('button[type="submit"]');
            if (postBtn) simulateHumanClick(postBtn);
        }
    }

    function smoothScroll() {
        // Scroll fluide avec pauses aléatoires
        let scrollAmount = 0;
        const scrollInterval = setInterval(() => {
            if (scrollAmount > window.innerHeight * 2) {  // Scroll modéré
                clearInterval(scrollInterval);
                return;
            }
            window.scrollBy(0, Math.random() * 100 + 50);  // Vitesse variable
            scrollAmount += 50;
        }, randomDelay(200, 800));
    }

    // Navigue vers Reels si pas déjà
    if (!window.location.href.includes('/reels/')) {
        window.location.href = 'https://www.instagram.com/reels/';
        await new Promise(resolve => setTimeout(resolve, 3000));
    }

    const startTime = Date.now();
    const endTime = startTime + randomDelay(config.sessionDurationMin, config.sessionDurationMax);

    while (Date.now() < endTime) {
        // Visionnage Reel actuel
        await new Promise(resolve => setTimeout(resolve, randomDelay(config.viewDurationMin * 1000, config.viewDurationMax * 1000)));

        likeReel();
        const comment = generateRandomComment();
        await addComment(comment);

        // Scroll fluide
        smoothScroll();
        await new Promise(resolve => setTimeout(resolve, randomDelay(2000, 5000)));  // Pause

        // Random next Reel si possible
        const nextReel = document.querySelector('button[aria-label="Next"]');
        if (nextReel && Math.random() > 0.5) {
            simulateHumanClick(nextReel);
            await new Promise(resolve => setTimeout(resolve, 1000));
        }
    }

    console.log('Scroll action terminée.');
})();