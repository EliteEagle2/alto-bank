// =====================================================================
// Deposit a check (Views/CheckDeposits/Index.cshtml)
//  • Photo zones: drag and drop on desktop, the camera on phones (accept="image/*" capture="environment"),
//    a preview with Retake / Replace, and a quick check of type and size (the server checks again, by signature)
//  • The "Endorse the back" checklist and the other steps must be done before "Review deposit" unlocks
//  • Review step: amount, account and both thumbnails, then "Deposit check"
// Without JavaScript the whole form is visible and posts normally.
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('checkDeposit');
    const form = root && root.querySelector('[data-check-form]');
    if (!form) return;

    const MAX = 5 * 1024 * 1024;
    const perCheck = parseFloat(root.dataset.perCheck) || 0;
    const leftToday = parseFloat(root.dataset.leftToday) || 0;
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const $ = sel => form.querySelector(sel);

    const amount = $('[data-amount]');
    const account = $('[data-account]');
    const amountMsg = $('[data-amount-msg]');
    const endorse = Array.from(form.querySelectorAll('[data-endorse]'));
    const gate = $('[data-review-gate]');
    const openBtn = $('[data-review-open]');
    const gateMsg = $('[data-gate-msg]');
    const review = $('[data-review]');
    const backBtn = $('[data-review-back-btn]');
    const steps = Array.from(form.querySelectorAll('.check-step:not(.check-review)'));
    const photos = {};   // front / back -> object URL of a valid photo

    // With JavaScript, the review step waits behind "Review deposit".
    gate.hidden = false;
    review.hidden = true;
    backBtn.hidden = false;
    $('[data-review-list]').hidden = false;
    $('[data-review-thumbs]').hidden = false;

    // ---------- Photo zones ----------
    form.querySelectorAll('[data-drop]').forEach(zone => {
        const side = zone.dataset.drop;
        const input = zone.querySelector('[data-file]');
        const empty = zone.querySelector('.drop-empty');
        const preview = zone.querySelector('.drop-preview');
        const img = zone.querySelector('[data-preview]');
        const error = zone.querySelector('[data-file-error]');

        function show(file) {
            error.textContent = '';
            if (photos[side]) { URL.revokeObjectURL(photos[side]); delete photos[side]; }
            if (!file) { empty.hidden = false; preview.hidden = true; zone.classList.remove('has-photo'); update(); return; }
            // Quick checks here; the server decides by the file's real signature.
            if (!/^image\/(jpeg|png)$/.test(file.type)) {
                error.textContent = 'Use a JPG or PNG photo. ' + (file.type ? 'That file is ' + file.type + '.' : 'That file isn\'t an image.');
                input.value = ''; return show(null);
            }
            if (file.size > MAX) {
                error.textContent = 'That photo is ' + (file.size / 1048576).toFixed(1) + ' MB. Photos can be up to 5 MB.';
                input.value = ''; return show(null);
            }
            photos[side] = URL.createObjectURL(file);
            img.src = photos[side];
            empty.hidden = true; preview.hidden = false; zone.classList.add('has-photo');
            update();
        }

        input.addEventListener('change', () => show(input.files[0]));

        // Drag and drop (desktop). The dropped file is put into the real input, so the form posts it.
        ['dragenter', 'dragover'].forEach(ev => zone.addEventListener(ev, e => { e.preventDefault(); zone.classList.add('is-over'); }));
        ['dragleave', 'drop'].forEach(ev => zone.addEventListener(ev, () => zone.classList.remove('is-over')));
        zone.addEventListener('drop', e => {
            e.preventDefault();
            const file = e.dataTransfer.files && e.dataTransfer.files[0];
            if (!file) return;
            const dt = new DataTransfer();
            dt.items.add(file);
            input.files = dt.files;
            show(file);
        });
    });

    // ---------- Is every step done? ----------
    function problems() {
        const list = [];
        const v = parseFloat(amount.value);
        if (!(v > 0)) list.push('the amount');
        else if (v > perCheck) list.push('an amount up to ' + money(perCheck));
        else if (v > leftToday) list.push('an amount within today\'s limit');
        if (!account.value) list.push('an account');
        if (!photos.front) list.push('the front photo');
        if (!photos.back) list.push('the back photo');
        if (endorse.some(c => !c.checked)) list.push('the endorsement checklist');
        return list;
    }

    function update() {
        const v = parseFloat(amount.value);
        amountMsg.className = 'small mt-2 mb-0';
        if (v > perCheck) { amountMsg.textContent = 'Mobile deposits are limited to ' + money(perCheck) + ' per check.'; amountMsg.classList.add('text-danger-alto'); }
        else if (v > leftToday) { amountMsg.textContent = 'You can deposit up to ' + money(leftToday) + ' more today.'; amountMsg.classList.add('text-danger-alto'); }
        else amountMsg.textContent = '';

        const missing = problems();
        openBtn.disabled = missing.length > 0;
        gateMsg.textContent = missing.length ? 'Still needed: ' + missing.join(', ') + '.' : 'Everything looks good.';
    }
    [amount, account, ...endorse].forEach(el => el.addEventListener('input', update));
    endorse.forEach(el => el.addEventListener('change', update));

    // ---------- Review ----------
    openBtn.addEventListener('click', () => {
        if (problems().length) return;
        $('[data-review-amount]').textContent = money(parseFloat(amount.value));
        $('[data-review-account]').textContent = account.options[account.selectedIndex].text;
        $('[data-review-front]').src = photos.front;
        $('[data-review-back]').src = photos.back;
        steps.forEach(s => s.hidden = true);
        gate.hidden = true;
        review.hidden = false;
        // Move focus to the review so keyboard and screen-reader users land on it (the button they pressed is now hidden).
        review.scrollIntoView({ behavior: 'smooth', block: 'start' });
        $('[data-review-title]').focus({ preventScroll: true });
    });
    backBtn.addEventListener('click', () => {
        steps.forEach(s => s.hidden = false);
        gate.hidden = false;
        review.hidden = true;
        openBtn.focus();
    });
    // One submit only (a double tap shouldn't upload twice).
    form.addEventListener('submit', () => { const b = $('[data-submit]'); b.disabled = true; b.textContent = 'Uploading…'; });

    update();
})();
