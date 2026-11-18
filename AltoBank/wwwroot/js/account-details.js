// =====================================================================
// Customer Account Details page (Views/Accounts/Details.cshtml)
//  • Tabs switch without reloading; the active tab is kept in the URL hash
//  • Activity: search / type / date presets, grouped by date, click-to-expand rows,
//    money in/out/net, "Load more" (20 at a time) via GET /api/accounts/{id}/transactions, CSV link
//  • Show / Copy for account + routing numbers (full number fetched on demand, re-masked after 30 s)
//  • Inline nickname edit (POST /api/accounts/{id}/nickname)
//  • Card tab: Show card details (re-masked after 30 s)
//  • Print the pre-filled direct deposit form
// User-provided text is always inserted with textContent (never innerHTML).
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('accountDetails');
    if (!root) return;

    const $ = (sel, el = root) => el.querySelector(sel);
    const $$ = (sel, el = root) => Array.from(el.querySelectorAll(sel));
    const fmt = v => Math.abs(v).toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const REVEAL_SECONDS = 30;

    // ---------- Toast ----------
    const toastEl = document.getElementById('altoToast');
    function toast(message, isError) {
        if (!toastEl || !window.bootstrap) return;
        toastEl.querySelector('[data-toast-text]').textContent = message;
        toastEl.classList.toggle('is-error', !!isError);
        bootstrap.Toast.getOrCreateInstance(toastEl).show();
    }

    async function getJson(url, options) {
        const res = await fetch(url, Object.assign({ credentials: 'same-origin', headers: { Accept: 'application/json' } }, options || {}));
        let body = null;
        try { body = await res.json(); } catch (e) { /* no body */ }
        if (!res.ok) throw new Error((body && (body.title || body.message)) || ('Request failed (' + res.status + ')'));
        return body;
    }

    async function copyText(text) {
        try { await navigator.clipboard.writeText(text); return true; }
        catch (e) {
            const ta = document.createElement('textarea');
            ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta); ta.select();
            const ok = document.execCommand('copy'); ta.remove(); return ok;
        }
    }

    // ---------- Tabs ↔ URL hash ----------
    function showTabFromHash() {
        const id = (location.hash || '').replace('#', '');
        const btn = id && $(`[data-bs-target="#${CSS.escape(id)}"]`);
        if (btn && window.bootstrap) bootstrap.Tab.getOrCreateInstance(btn).show();
    }
    $$('[data-bs-toggle="tab"]').forEach(btn => btn.addEventListener('shown.bs.tab', () => {
        history.replaceState(null, '', btn.dataset.bsTarget);
    }));
    window.addEventListener('hashchange', showTabFromHash);
    showTabFromHash();

    // =================================================================
    // ACTIVITY
    // =================================================================
    const list = document.getElementById('activityList');
    const filters = document.getElementById('activityFilters');
    const loadMoreBtn = $('[data-load-more]');
    const emptyEl = $('[data-empty]');
    const pageSize = parseInt(root.dataset.pageSize, 10) || 20;
    const state = { page: 1, loaded: list.querySelectorAll('.tx-row').length, total: parseInt(root.dataset.total, 10) || 0 };

    // Same map as Transaction.IconFor (Models/Transaction.cs).
    const ICONS = {
        Deposit: 'bi-arrow-down-left', Withdrawal: 'bi-arrow-up-right', TransferIn: 'bi-arrow-left-right', TransferOut: 'bi-arrow-left-right',
        Interest: 'bi-graph-up-arrow', AdjustmentCredit: 'bi-sliders', AdjustmentDebit: 'bi-sliders',
        PaymentSent: 'bi-send', PaymentReceived: 'bi-person-down', BillPayment: 'bi-receipt', CheckDeposit: 'bi-camera',
        ProvisionalCredit: 'bi-shield-check', ProvisionalClawback: 'bi-shield-x',
        CardPurchase: 'bi-bag', CardPayment: 'bi-credit-card-2-back', InterestCharge: 'bi-percent', LateFee: 'bi-exclamation-diamond',
        FeeWaiver: 'bi-patch-check', CashBackCredit: 'bi-gift', RewardRedemption: 'bi-gift',
        LoanPayment: 'bi-bank', LoanDisbursement: 'bi-cash-coin', LoanProceeds: 'bi-cash-coin'
    };
    // Dates follow the BANK's time zone and calendar (not the browser's), same as the server-rendered rows.
    const TZ = root.dataset.tz || 'America/New_York';
    const BANK_TODAY = root.dataset.today;   // "yyyy-MM-dd"
    const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
    const isoDate = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    function dayLabel(iso) {
        if (iso === BANK_TODAY) return 'Today';
        if (iso === addDays(BANK_TODAY, -1)) return 'Yesterday';
        return new Date(iso + 'T12:00:00Z').toLocaleDateString('en-US', { weekday: 'long', month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
    }
    function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

    // Builds the same markup as Views/Accounts/_TransactionRow.cshtml.
    function buildRow(t) {
        const date = new Date(t.date);
        const reversed = t.status === 'Reversed';
        const row = el('div', 'tx-row' + (reversed ? ' is-reversed' : ''));
        row.dataset.txId = t.id;

        const btn = el('button', 'tx-main'); btn.type = 'button';
        btn.setAttribute('aria-expanded', 'false'); btn.setAttribute('aria-controls', 'tx-more-' + t.id);
        const icon = el('span', 'tx-icon ' + (t.isCredit ? 'in' : 'out')); icon.setAttribute('aria-hidden', 'true');
        icon.appendChild(el('i', 'bi ' + (ICONS[t.kind] || 'bi-arrow-counterclockwise')));
        const desc = el('span', 'tx-desc');
        desc.appendChild(el('span', 'tx-title', t.description));
        const sub = el('span', 'tx-sub', `${t.type} · ${date.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit', timeZone: TZ })} `);
        const chip = el('span', 'cat-chip', t.categoryLabel); chip.dataset.catChip = '';
        sub.appendChild(chip);
        if (t.disputeStatus) {
            const refunded = t.disputeStatus === 'Approved', denied = t.disputeStatus === 'Denied';
            sub.appendChild(el('span', 'badge-dispute' + (refunded ? ' is-refunded' : denied ? ' is-denied' : ''),
                refunded ? (t.partlyRefunded ? 'Partly refunded' : 'Refunded') : denied ? 'Dispute denied' : 'Disputed'));
        } else if (reversed) sub.appendChild(el('span', 'badge-reversed', 'Reversed'));
        desc.appendChild(sub);
        const amounts = el('span', 'tx-amounts');
        amounts.appendChild(el('span', 'money tx-amount ' + (t.isCredit ? 'amount-credit' : 'amount-debit'), (t.isCredit ? '+' : '−') + fmt(t.amount)));
        // A credit card's balance is stored negative (money owed): show what's owed instead.
        const onCard = root.dataset.credit === 'true';
        const bal = el('span', 'tx-bal', onCard ? 'Owed ' : 'Bal ');
        bal.appendChild(el('span', 'money', fmt(onCard ? Math.max(0, -t.balanceAfter) : t.balanceAfter)));
        amounts.appendChild(bal);
        const caret = el('i', 'bi bi-chevron-down tx-caret'); caret.setAttribute('aria-hidden', 'true');
        btn.append(icon, desc, amounts, caret);

        const more = el('div', 'tx-more'); more.id = 'tx-more-' + t.id; more.hidden = true;
        const dl = el('dl', 'tx-dl');
        [['Transaction ID', '#' + t.id], ['Date & time', date.toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'medium', timeZone: TZ })],
         ['Type', t.type], ['Counterparty', t.counterparty || '—'], ['Memo', t.memo || '—'], ['Status', reversed ? 'Reversed' : 'Posted']]
            .forEach(([k, v]) => { const d = el('div'); d.append(el('dt', null, k), el('dd', k === 'Transaction ID' ? 'font-monospace' : null, v)); dl.append(d); });
        more.append(dl, buildCategoryForm(t));
        if (t.canDispute || t.disputeStatus) {
            const link = el('a', 'tx-dispute-link');
            link.href = t.canDispute ? '/disputes/new?transactionId=' + t.id : '/disputes/for-transaction/' + t.id;
            const i = el('i', 'bi ' + (t.canDispute ? 'bi-shield-exclamation' : 'bi-shield-check')); i.setAttribute('aria-hidden', 'true');
            link.append(i, document.createTextNode(t.canDispute ? 'Dispute this transaction' : 'See your dispute'));
            more.append(link);
        }
        row.append(btn, more);
        return { row, day: t.bankDate, label: dayLabel(t.bankDate) };
    }

    // ---------- Category picker (same markup as the form in _TransactionRow.cshtml) ----------
    const CATEGORIES = JSON.parse(root.dataset.categories || '[]');
    const pageToken = (root.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    const NOTE_AUTO = 'Alto picked this category from the merchant name.';
    const NOTE_MINE = 'You chose this category.';

    function fillCategorySelect(select, current, overridden) {
        select.replaceChildren();
        CATEGORIES.forEach(c => { const o = el('option', null, c.label); o.value = c.key; o.selected = c.key === current; select.append(o); });
        if (overridden) { const o = el('option', null, 'Let Alto pick again'); o.value = 'auto'; select.append(o); }
    }

    function buildCategoryForm(t) {
        const form = el('form', 'tx-cat-form'); form.method = 'post';
        form.action = '/transactions/' + t.id + '/category'; form.dataset.txCat = '';
        const token = el('input'); token.type = 'hidden'; token.name = '__RequestVerificationToken'; token.value = pageToken;
        const back = el('input'); back.type = 'hidden'; back.name = 'returnUrl'; back.value = location.pathname + '#activity';
        const label = el('label', 'form-label small mb-0', 'Category'); label.htmlFor = 'cat-' + t.id;
        const select = el('select', 'form-select form-select-sm'); select.id = 'cat-' + t.id; select.name = 'category';
        fillCategorySelect(select, t.category, t.categoryOverridden);
        const save = el('button', 'btn btn-alto-outline btn-sm', 'Save'); save.type = 'submit';
        const note = el('span', 'small text-muted-alto', t.categoryOverridden ? NOTE_MINE : NOTE_AUTO); note.dataset.catNote = '';
        form.append(token, back, label, select, save, note);
        return form;
    }

    // Save a category without reloading: POST /api/insights/transactions/{id}/category (JSON).
    // Without JavaScript the same form posts to /transactions/{id}/category and comes back here.
    list.addEventListener('submit', async e => {
        const form = e.target.closest('[data-tx-cat]');
        if (!form) return;
        e.preventDefault();
        const row = form.closest('.tx-row');
        const select = form.elements.category;
        const btn = form.querySelector('button[type="submit"]');
        btn.disabled = true;
        try {
            const res = await getJson('/api/insights/transactions/' + row.dataset.txId + '/category', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify({ category: select.value })
            });
            row.querySelector('[data-cat-chip]').textContent = res.label;
            form.querySelector('[data-cat-note]').textContent = res.overridden ? NOTE_MINE : NOTE_AUTO;
            fillCategorySelect(select, res.category, res.overridden);
            toast(res.message);
        } catch (err) {
            toast(err.message, true);
        } finally {
            btn.disabled = false;
        }
    });

    function appendRows(items) {
        items.forEach(t => {
            const { row, day, label } = buildRow(t);
            let group = list.lastElementChild;
            if (!group || group.dataset.date !== day) {
                group = el('div', 'tx-group'); group.dataset.date = day;
                group.appendChild(el('div', 'tx-date-head', label));
                list.appendChild(group);
            }
            group.appendChild(row);
        });
    }

    // Click a row to expand/collapse its details.
    list.addEventListener('click', e => {
        const btn = e.target.closest('.tx-main');
        if (!btn) return;
        const more = document.getElementById(btn.getAttribute('aria-controls'));
        const open = btn.getAttribute('aria-expanded') === 'true';
        btn.setAttribute('aria-expanded', open ? 'false' : 'true');
        btn.closest('.tx-row').classList.toggle('open', !open);
        if (more) more.hidden = open;
    });

    // Current filter values → query string (shared by the API call and the CSV link).
    function filterParams() {
        const p = new URLSearchParams();
        const search = filters.elements.search.value.trim();
        const kind = filters.elements.kind.value;
        if (search) p.set('search', search);
        if (kind) p.set('kind', kind);
        const preset = ($('.af-chip.active', filters) || {}).dataset?.preset || '90';
        if (preset === 'custom') {
            if (filters.elements.from.value) p.set('from', filters.elements.from.value);
            if (filters.elements.to.value) p.set('to', filters.elements.to.value);
        } else if (preset === 'year') {
            p.set('from', `${BANK_TODAY.slice(0, 4)}-01-01`);
        } else {
            p.set('from', addDays(BANK_TODAY, -(parseInt(preset, 10) - 1)));
        }
        return p;
    }

    function updateCsvLink() {
        const a = $('[data-csv]', filters);
        a.href = a.href.split('?')[0] + '?' + filterParams().toString();
    }

    function updateSummary(data) {
        $('[data-sum="in"]').textContent = '+' + fmt(data.moneyIn);
        $('[data-sum="out"]').textContent = '−' + fmt(data.moneyOut);
        const net = $('[data-sum="net"]');
        if (root.dataset.credit === 'true') {
            // A card or loan: the change in what's OWED over the range (charges minus payments), in plain ink.
            const owed = data.moneyOut - data.moneyIn;
            net.textContent = (owed > 0 ? '+' : owed < 0 ? '−' : '') + fmt(owed);
            net.className = '';
        } else {
            net.textContent = (data.net >= 0 ? '+' : '−') + fmt(data.net);
            net.className = data.net > 0 ? 'amount-credit' : data.net < 0 ? 'amount-debit' : '';
        }
        $('[data-count]').textContent = `${data.totalCount} transaction${data.totalCount === 1 ? '' : 's'}`;
    }

    let requestId = 0;
    async function load(reset) {
        const myRequest = ++requestId;
        const page = reset ? 1 : state.page + 1;
        const p = filterParams(); p.set('page', page); p.set('pageSize', pageSize);
        const spinner = $('[data-spinner]', loadMoreBtn);
        spinner.classList.remove('d-none'); loadMoreBtn.disabled = true;
        try {
            const data = await getJson(root.dataset.activityUrl + '?' + p.toString());
            if (myRequest !== requestId) return;            // a newer filter change already started
            if (reset) { list.textContent = ''; state.loaded = 0; }
            appendRows(data.items);
            state.page = page; state.loaded += data.items.length; state.total = data.totalCount;
            updateSummary(data);
            loadMoreBtn.hidden = !data.hasMore;
            emptyEl.hidden = data.totalCount !== 0;
            list.hidden = data.totalCount === 0;
        } catch (e) {
            toast('Could not load activity: ' + e.message, true);
        } finally {
            spinner.classList.add('d-none'); loadMoreBtn.disabled = false;
        }
    }

    loadMoreBtn.addEventListener('click', () => load(false));
    let searchTimer = null;
    filters.elements.search.addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(() => { updateCsvLink(); load(true); }, 300); });
    filters.elements.kind.addEventListener('change', () => { updateCsvLink(); load(true); });
    [filters.elements.from, filters.elements.to].forEach(i => i.addEventListener('change', () => { updateCsvLink(); load(true); }));
    $$('.af-chip', filters).forEach(chip => chip.addEventListener('click', () => {
        $$('.af-chip', filters).forEach(c => c.classList.toggle('active', c === chip));
        $('[data-custom]', filters).hidden = chip.dataset.preset !== 'custom';
        updateCsvLink();
        if (chip.dataset.preset !== 'custom' || filters.elements.from.value) load(true);
    }));
    updateCsvLink();

    // =================================================================
    // SHOW / COPY fields (_CopyField partial)
    // =================================================================
    $$('.copy-field', document).forEach(field => {
        const valueEl = $('[data-value]', field);
        const timerEl = $('[data-timer]', field);
        const showBtn = $('[data-show]', field);
        const copyBtn = $('[data-copy]', field);
        let timer = null;

        async function realValue() {
            if (field.dataset.copyValue) return field.dataset.copyValue;
            const data = await getJson(field.dataset.revealUrl);   // owner-only endpoint (404 for anyone else)
            return data[field.dataset.revealField];
        }
        function mask() {
            clearInterval(timer); timer = null;
            valueEl.textContent = field.dataset.masked;
            timerEl.hidden = true;
            if (showBtn) { showBtn.innerHTML = '<i class="bi bi-eye me-1"></i>Show'; showBtn.setAttribute('aria-pressed', 'false'); }
        }
        if (showBtn) showBtn.addEventListener('click', async () => {
            if (timer) return mask();
            try {
                valueEl.textContent = await realValue();
                let left = REVEAL_SECONDS;
                timerEl.hidden = false; timerEl.textContent = `${left}s`;
                showBtn.innerHTML = '<i class="bi bi-eye-slash me-1"></i>Hide'; showBtn.setAttribute('aria-pressed', 'true');
                timer = setInterval(() => { left--; timerEl.textContent = `${left}s`; if (left <= 0) mask(); }, 1000);
            } catch (e) { toast(e.message, true); }
        });
        copyBtn.addEventListener('click', async () => {
            try {
                const ok = await copyText(await realValue());
                toast(ok ? `${$('.copy-field-label', field).textContent} copied` : 'Copy failed', !ok);
            } catch (e) { toast(e.message, true); }
        });
    });

    // =================================================================
    // NICKNAME (inline edit)
    // =================================================================
    const nickView = $('[data-nickname-view]');
    const nickForm = $('[data-nickname-form]');
    if (nickForm) {
        const editBtn = $('[data-nickname-edit]');
        const errEl = $('[data-nickname-error]', nickForm);
        const toggle = editing => { nickView.hidden = editing; nickForm.hidden = !editing; errEl.textContent = ''; if (editing) nickForm.elements.nickname.focus(); };
        if (editBtn) editBtn.addEventListener('click', () => toggle(true));
        $('[data-nickname-cancel]', nickForm).addEventListener('click', () => toggle(false));
        nickForm.addEventListener('submit', async e => {
            e.preventDefault();
            const nickname = nickForm.elements.nickname.value.trim();
            if (nickname.length > 30) { errEl.textContent = 'Nickname can be at most 30 characters.'; return; }
            try {
                const acct = await getJson(root.dataset.nicknameUrl, {
                    method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({ nickname: nickname || null })
                });
                $('[data-nickname-text]').textContent = acct.nickname || 'None';
                $$('[data-account-name]').forEach(n => { n.textContent = acct.name; });
                $$('[data-card-nickname]', document).forEach(n => { n.textContent = acct.nickname || ''; });
                document.title = `${acct.name} · Alto Online Banking`;
                toggle(false);
                toast('Nickname saved');
            } catch (err) { errEl.textContent = err.message; }
        });
    }

    // =================================================================
    // CARD: show details for 30 s
    // =================================================================
    const cardArt = document.getElementById('cardArt');
    const revealBtn = $('[data-reveal-card]');
    if (cardArt && revealBtn) {
        const details = $('[data-card-details]');
        const numberEl = $('.card-number', cardArt);
        const maskedNumber = numberEl.textContent;
        let timer = null;
        const hide = () => {
            clearInterval(timer); timer = null;
            details.hidden = true; numberEl.textContent = maskedNumber;
            $$('[data-card-field]', details).forEach(f => { f.textContent = ''; });
            revealBtn.innerHTML = '<i class="bi bi-eye me-1"></i>Show card details';
        };
        revealBtn.addEventListener('click', async () => {
            if (timer) return hide();
            try {
                const card = await getJson(cardArt.dataset.revealUrl);
                $('[data-card-field="cardNumber"]', details).textContent = card.cardNumber;
                $('[data-card-field="expiry"]', details).textContent = card.expiry;
                $('[data-card-field="cvv"]', details).textContent = card.cvv;
                numberEl.textContent = card.cardNumber;
                details.hidden = false;
                revealBtn.innerHTML = '<i class="bi bi-eye-slash me-1"></i>Hide card details';
                let left = REVEAL_SECONDS;
                const t = $('[data-card-timer]', details); t.textContent = left;
                timer = setInterval(() => { left--; t.textContent = left; if (left <= 0) hide(); }, 1000);
            } catch (e) { toast(e.message, true); }
        });
    }

    // =================================================================
    // DIRECT DEPOSIT: print the pre-filled form
    // =================================================================
    const printBtn = $('[data-print-dd]');
    if (printBtn) {
        const cell = $('[data-dd-account]');
        const masked = cell.textContent;
        printBtn.addEventListener('click', async () => {
            try {
                const data = await getJson(root.dataset.numberUrl);   // full number only for the printout
                cell.textContent = data.accountNumber;
            } catch (e) { toast('Could not load the account number: ' + e.message, true); return; }
            document.body.classList.add('print-dd');
            window.print();
        });
        window.addEventListener('afterprint', () => {
            document.body.classList.remove('print-dd');
            cell.textContent = masked;     // re-mask after printing
        });
    }
})();
