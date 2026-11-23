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

// On narrow screens the investigation tabs scroll sideways; keep the active tab visible inside the bar
// (adjusting only the bar's own scroll position, never the page) after loads and Blazor re-renders.
(() => {
    let shown = null;
    const reveal = () => {
        const active = document.querySelector(".investigation-nav a.active");
        if (!active || active === shown) return;
        shown = active;
        const nav = active.parentElement;
        if (nav.scrollWidth <= nav.clientWidth) return;
        const left = active.offsetLeft - nav.offsetLeft;
        if (left < nav.scrollLeft || left + active.offsetWidth > nav.scrollLeft + nav.clientWidth - 40)
            nav.scrollLeft = Math.max(0, left - 16);
    };
    let queued = false;
    const schedule = () => { if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; reveal(); }); } };
    new MutationObserver(schedule).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ["class"] });
    schedule();
})();
