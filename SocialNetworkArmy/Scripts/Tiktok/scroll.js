// scroll.js - TikTok Scroll Action
(async function () {
    const config = {
        sessionDurationMin: 20 * 60 * 1000, sessionDurationMax: 40 * 60 * 1000,
        likePercent: 30, commentPercent: 30,
        viewDurationMin: 5, viewDurationMax: 10
    };

    function randomDelay(min, max) {
        return Math.random() * (max - min) + min;
    }

    function generateRandomComment() {
        const comments = ['Wow! 🔥', 'Love it! ❤️', 'Great! 😊', 'Funny!', 'Inspire! 👏'];
        return comments[Math.floor(Math.random() * comments.length)];
    }

    function simulateHumanClick(element) {
        const rect = element.getBoundingClientRect();
        const x = rect.left + rect.width / 2 + (Math.random() - 0.5) * 20;
        const y = rect.top + rect.height / 2 + (Math.random() - 0.5) * 20;
        const event = new MouseEvent('click', { bubbles: true, clientX: x, clientY: y });
        element.dispatchEvent(event);
    }

    function likeVideo() {
        const likeBtn = document.querySelector('[data-e2e="like-icon"]');
        if (likeBtn && Math.random() < config.likePercent / 100) {
            simulateHumanClick(likeBtn);
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
                input.textContent = comment;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                const sendBtn = document.querySelector('button[data-e2e="send-comment"]');
                if (sendBtn) simulateHumanClick(sendBtn);
            }
        }
    }

    // Navigue vers For You
    if (!window.location.href.includes('/foryou')) {
        window.location.href = 'https://www.tiktok.com/foryou';
        await new Promise(resolve => setTimeout(resolve, 3000));
    }

    const start = Date.now();
    const end = start + randomDelay(config.sessionDurationMin, config.sessionDurationMax);

    while (Date.now() < end) {
        await new Promise(resolve => setTimeout(resolve, randomDelay(config.viewDurationMin * 1000, config.viewDurationMax * 1000)));

        likeVideo();
        const comment = generateRandomComment();
        await addComment(comment);

        // Swipe/Scroll fluide
        const scrollDir = Math.random() > 0.5 ? window.innerHeight : -window.innerHeight;
        window.scrollBy(0, scrollDir * (Math.random() * 0.5 + 0.5));  // Vitesse variable
        await new Promise(resolve => setTimeout(resolve, randomDelay(1000, 3000)));
    }

    console.log('Scroll terminé.');
})();