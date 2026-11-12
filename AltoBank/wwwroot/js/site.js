// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ===== Privacy mode (online banking) =====
// The default comes from Profile → Preferences (body[data-privacy-default]); the eye button in the top bar
// flips it for this browser tab only (sessionStorage). Tapping a blurred amount reveals just that amount.
(function () {
    'use strict';
    const toggle = document.querySelector('[data-privacy-toggle]');
    if (!toggle) return;
    const body = document.body;
    let saved = null;
    try { saved = sessionStorage.getItem('alto-privacy'); } catch { /* storage blocked: use the default */ }
    const apply = on => {
        body.classList.toggle('privacy-on', on);
        toggle.setAttribute('aria-pressed', on ? 'true' : 'false');
        const label = on ? 'Show balances' : 'Hide balances';
        toggle.title = label; toggle.setAttribute('aria-label', label);
        toggle.querySelector('i').className = on ? 'bi bi-eye-slash' : 'bi bi-eye';
    };
    apply((saved ?? body.dataset.privacyDefault) === 'on');
    toggle.addEventListener('click', () => {
        const on = !body.classList.contains('privacy-on');
        apply(on);
        try { sessionStorage.setItem('alto-privacy', on ? 'on' : 'off'); } catch { /* ignore */ }
    });
    document.addEventListener('click', e => {
        const amount = body.classList.contains('privacy-on') && e.target.closest && e.target.closest('.app-content .money');
        if (amount) amount.classList.toggle('is-revealed');
    });
})();

// ===== Sideways-scrolling tab bars ([data-tab-scroller]) =====
// Shows the right-edge fade only while more tabs are off-screen, and keeps the active tab in view.
(function () {
    'use strict';
    document.querySelectorAll('[data-tab-scroller]').forEach(wrap => {
        const bar = wrap.querySelector('[role="tablist"]');
        if (!bar) return;
        const update = () => wrap.classList.toggle('has-more', bar.scrollLeft + bar.clientWidth < bar.scrollWidth - 4);
        const reveal = () => {
            const active = bar.querySelector('.active');
            if (active) bar.scrollTo({ left: active.offsetLeft - (bar.clientWidth - active.offsetWidth) / 2, behavior: 'smooth' });
        };
        bar.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update);
        bar.addEventListener('shown.bs.tab', () => { reveal(); setTimeout(update, 350); });
        reveal(); update();
    });
})();
