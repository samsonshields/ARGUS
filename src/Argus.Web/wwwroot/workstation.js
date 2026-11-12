// Keeps the context-bar Zulu clock current. Minute precision; no network access.
(() => {
    const pad = value => String(value).padStart(2, "0");
    const tick = () => {
        const now = new Date();
        const text = `${now.getUTCFullYear()}-${pad(now.getUTCMonth() + 1)}-${pad(now.getUTCDate())} ${pad(now.getUTCHours())}:${pad(now.getUTCMinutes())}Z`;
        for (const clock of document.querySelectorAll("[data-utc-clock]")) {
            if (clock.textContent !== text) { clock.textContent = text; clock.setAttribute("datetime", now.toISOString()); }
        }
    };
    tick();
    setInterval(tick, 15000);
})();
