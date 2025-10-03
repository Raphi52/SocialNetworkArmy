(() => {
    if (window.SNA_openFirstReelModal) return;

    const sleep = (ms) => new Promise(r => setTimeout(r, ms));
    const now = () => performance.now();

    function isVisible(el) {
        if (!el) return false;
        const r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0 && r.bottom > 0 && r.right > 0 &&
            r.left < (innerWidth || 1e9) && r.top < (innerHeight || 1e9);
    }

    function findFirstReelTile() {
        // Priorité au grid <article> pour viser la vignette (modal)
        const links = Array.from(document.querySelectorAll('article a[href*="/reel/"]'));
        return links.find(isVisible) || links[0] ||
            document.querySelector('a[href*="/reel/"]') || null;
    }

    function isModalOpen() {
        return !!document.querySelector('div[role="dialog"]');
    }

    async function openWithEnter(el, timeout = 3500) {
        try {
            el.focus();
            el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true }));
            el.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', code: 'Enter', bubbles: true }));
            const t0 = now();
            while (now() - t0 < timeout) {
                if (isModalOpen()) return 'MODAL_ENTER';
                await sleep(120);
            }
        } catch { }
        return null;
    }

    async function openWithClick(el, timeout = 3500) {
        try {
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            el.click();
            const t0 = now();
            while (now() - t0 < timeout) {
                if (isModalOpen()) return 'MODAL_CLICK';
                await sleep(120);
            }
        } catch { }
        return null;
    }

    async function openWithMouse(el, timeout = 3500) {
        try {
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            await sleep(200);
            const r = el.getBoundingClientRect();
            const x = r.left + r.width / 2 + (Math.random() - 0.5) * 6;
            const y = r.top + r.height / 2 + (Math.random() - 0.5) * 6;
            const opts = { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 };
            // volontairement sans 'click' pour favoriser le MODAL
            el.dispatchEvent(new MouseEvent('mousedown', opts));
            el.dispatchEvent(new MouseEvent('mouseup', opts));
            const t0 = now();
            while (now() - t0 < timeout) {
                if (isModalOpen()) return 'MODAL_MOUSE';
                await sleep(120);
            }
        } catch { }
        return null;
    }

    async function fallbackFocus(el) {
        try {
            const href = el.href || el.getAttribute('href');
            if (href) {
                location.assign(href);
                return 'FOCUS';
            }
        } catch { }
        return null;
    }

    window.SNA_openFirstReelModal = async function () {
        const tile = findFirstReelTile();
        if (!tile) return 'NO_EL';

        const viaEnter = await openWithEnter(tile);
        if (viaEnter) return viaEnter;

        const viaClick = await openWithClick(tile); // typo corrigée (plus de 'a')
        if (viaClick) return viaClick;

        const viaMouse = await openWithMouse(tile);
        if (viaMouse) return viaMouse;

        const viaFocus = await fallbackFocus(tile);
        if (viaFocus) return viaFocus;

        return 'OPEN_FAILED';
    };
})();
