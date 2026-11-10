// =====================================================================
// Send & Request page (Views/Payments/Index.cshtml)
//  • tabs kept in the URL hash (#send, #request, #inbox, #activity)
//  • live recipient preview: GET /api/payments/recipient?q=… shows "Sending to Maya C." as you type
//  • split a bill: total ÷ people (+ you), or custom amounts that can't exceed the total
// The server recalculates and re-checks everything; this is only for a nicer form.
// User text is always inserted with textContent.
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('paymentsPage');
    if (!root) return;
    const $ = (s, el = root) => el.querySelector(s);
    const $$ = (s, el = root) => Array.from(el.querySelectorAll(s));
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });

    // ---------- Tabs <-> URL hash ----------
    function showTab(hash) {
        const id = (hash || '').replace('#', '') === 'requests' ? 'activity' : (hash || '').replace('#', '');
        const btn = id && $(`[data-bs-target="#${id}"]`);
        if (btn && window.bootstrap) bootstrap.Tab.getOrCreateInstance(btn).show();
        if (hash === '#requests') setTimeout(() => document.getElementById('requests')?.scrollIntoView({ behavior: 'smooth' }), 150);
    }
    $$('[data-bs-toggle="tab"]').forEach(b => b.addEventListener('shown.bs.tab', e => history.replaceState(null, '', e.target.dataset.bsTarget)));
    showTab(location.hash);
    window.addEventListener('hashchange', () => showTab(location.hash));

    // ---------- Recipient preview ----------
    const url = root.dataset.recipientUrl;
    function wirePreview(input) {
        const box = input.closest('div').parentElement.querySelector('[data-recipient-preview]') ||
                    input.parentElement.querySelector('[data-recipient-preview]');
        let timer, last = '';
        input.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(async () => {
                const q = input.value.trim();
                if (q === last) return;
                last = q;
                if (!box) return;
                box.textContent = ''; box.className = box.className.replace(/ is-(ok|bad)/g, '');
                if (q.length < 5) return;
                try {
                    const res = await fetch(`${url}?q=${encodeURIComponent(q)}`, { headers: { Accept: 'application/json' } });
                    const data = await res.json();
                    if (input.value.trim() !== q) return;   // they kept typing
                    const icon = document.createElement('i');
                    icon.className = 'bi me-1 ' + (res.ok ? 'bi-person-check-fill' : 'bi-exclamation-circle');
                    box.append(icon, document.createTextNode(res.ok ? `${box.closest('#sendForm') ? 'Sending to' : 'Requesting from'} ${data.displayName}` : (data.title || 'Not found')));
                    box.classList.add(res.ok ? 'is-ok' : 'is-bad');
                } catch { /* offline: the server will validate on submit */ }
            }, 350);
        });
    }
    $$('[data-recipient-input]').forEach(wirePreview);

    // ---------- Request / split a bill ----------
    const form = document.getElementById('requestForm');
    if (!form) return;
    const rows = $$('[data-person]', form);
    const modeInput = $('[data-mode]', form);
    const splitBox = $('[data-split]', form);
    const totalInput = $('[data-total]', form);
    const includeMe = $('[data-include-me]', form);
    const summary = $('[data-split-summary]', form);
    const kind = () => ($('[data-kind]:checked', form) || {}).value;
    const how = () => ($('[data-how]:checked', form) || {}).value;
    const visible = () => rows.filter(r => !r.hidden);

    function recalc() {
        const split = kind() === 'split';
        splitBox.hidden = !split;
        modeInput.value = split ? how() : 'single';
        const people = visible();
        const amounts = people.map(r => $('[data-amount]', r));
        amounts.forEach(a => { a.readOnly = split && how() === 'even'; });
        summary.textContent = '';
        if (!split) return;

        const total = parseFloat(totalInput.value);
        if (!(total > 0)) { summary.textContent = 'Enter the bill total.'; return; }
        if (how() === 'even') {
            // Same math as PaymentRequestService.SplitEvenly (in cents; leftover cent stays with you).
            const shares = people.length + (includeMe.checked ? 1 : 0);
            const cents = Math.round(total * 100), each = Math.floor(cents / shares), left = cents - each * shares;
            amounts.forEach((a, i) => { a.value = ((each + (!includeMe.checked && i < left ? 1 : 0)) / 100).toFixed(2); });
            summary.textContent = `${money(total)} ÷ ${shares} = ${money(each / 100)} each` + (includeMe.checked ? ' (you pay your share yourself).' : '.');
        } else {
            const sum = amounts.reduce((s, a) => s + (parseFloat(a.value) || 0), 0);
            const rest = Math.round((total - sum) * 100) / 100;
            summary.textContent = rest < 0 ? `That's ${money(-rest)} more than the bill.` : `Requesting ${money(sum)}; ${money(rest)} is your share.`;
        }
    }

    $('[data-add-person]', form).addEventListener('click', () => {
        const next = rows.find(r => r.hidden);
        if (next) { next.hidden = false; $('input', next).focus(); }
        if (!rows.some(r => r.hidden)) $('[data-add-person]', form).disabled = true;
        recalc();
    });
    rows.forEach(r => $('[data-remove]', r)?.addEventListener('click', () => {
        r.hidden = true;
        $$('input', r).forEach(i => { i.value = ''; });
        const box = $('[data-recipient-preview]', r); if (box) box.textContent = '';
        $('[data-add-person]', form).disabled = false;
        recalc();
    }));
    form.addEventListener('input', recalc);
    form.addEventListener('change', recalc);
    recalc();
})();
