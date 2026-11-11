// =====================================================================
// Decision screen (Views/Accounts/Decision.cshtml)
//  • First visit: 3-second "Reviewing your application" animation, then "Approved!" + confetti
//  • Copy-to-clipboard for routing/account numbers, Show/Hide full account number
//  • Clears the saved application from sessionStorage (it's been submitted)
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('decision');
    if (!root) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const reviewing = root.querySelector('[data-reviewing]');
    const result = root.querySelector('[data-result]');
    const status = root.querySelector('[data-review-status]');

    // The application is done — forget the saved answers.
    try { sessionStorage.removeItem('alto.apply.' + root.dataset.userKey); } catch (e) { /* ignore */ }

    // ---------- Reviewing -> Approved ----------
    if (root.dataset.animate === 'true' && !reducedMotion) {
        result.hidden = true;
        reviewing.hidden = false;
        const messages = ['Checking your information…', 'Confirming your identity…', 'Setting up your account…'];
        messages.forEach((m, i) => setTimeout(() => { status.textContent = m; }, i * 1000));
        setTimeout(() => {
            reviewing.hidden = true;
            result.hidden = false;
            result.classList.add('reveal');
            confetti();
        }, 3000);
    } else if (root.dataset.animate === 'true') {
        confetti();
    }

    // ---------- Confetti (pure CSS animation on generated <i> elements) ----------
    function confetti() {
        if (reducedMotion) return;
        const colors = ['#F2B544', '#8FB8E8', '#4CC38A', '#DCE6F2', '#EF6B5B'];
        const layer = document.createElement('div');
        layer.className = 'confetti-layer';
        layer.setAttribute('aria-hidden', 'true');
        for (let i = 0; i < 140; i++) {
            const piece = document.createElement('i');
            const size = 6 + Math.random() * 7;
            piece.style.left = (50 + (Math.random() - 0.5) * 30) + 'vw';
            piece.style.width = size + 'px';
            piece.style.height = (size * (Math.random() < 0.5 ? 0.45 : 1)) + 'px';
            piece.style.background = colors[i % colors.length];
            piece.style.borderRadius = Math.random() < 0.3 ? '50%' : '2px';
            // Each piece bursts outward (--dx) and falls (--dy) while spinning (--rot).
            piece.style.setProperty('--dx', ((Math.random() - 0.5) * 110) + 'vw');
            piece.style.setProperty('--dy', (55 + Math.random() * 60) + 'vh');
            piece.style.setProperty('--rot', (Math.random() * 1080 - 540) + 'deg');
            piece.style.animationDuration = (1.8 + Math.random() * 1.6) + 's';
            piece.style.animationDelay = (Math.random() * 0.25) + 's';
            layer.appendChild(piece);
        }
        document.body.appendChild(layer);
        setTimeout(() => layer.remove(), 4200);
    }

    // ---------- Copy + reveal ----------
    async function copyText(text) {
        try { await navigator.clipboard.writeText(text); return true; }
        catch (e) {
            // Fallback for browsers that block the Clipboard API (e.g. plain http).
            const ta = document.createElement('textarea');
            ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta); ta.select();
            const ok = document.execCommand('copy');
            ta.remove();
            return ok;
        }
    }
    root.querySelectorAll('[data-copy]').forEach(btn => btn.addEventListener('click', async () => {
        const original = btn.innerHTML;
        const ok = await copyText(btn.dataset.copy);
        btn.innerHTML = ok ? '<i class="bi bi-check2 me-1"></i>Copied!' : 'Copy failed';
        btn.classList.toggle('copied', ok);
        setTimeout(() => { btn.innerHTML = original; btn.classList.remove('copied'); }, 1800);
    }));

    const reveal = root.querySelector('[data-reveal]');
    const display = root.querySelector('[data-acct-display]');
    if (reveal && display) {
        const masked = display.textContent;
        reveal.addEventListener('click', () => {
            const showing = reveal.getAttribute('aria-pressed') === 'true';
            display.textContent = showing ? masked : reveal.dataset.reveal;
            reveal.setAttribute('aria-pressed', showing ? 'false' : 'true');
            reveal.innerHTML = showing ? '<i class="bi bi-eye me-1"></i>Show' : '<i class="bi bi-eye-slash me-1"></i>Hide';
        });
    }
})();
