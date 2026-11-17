// =====================================================================
// Dispute a charge (Views/Disputes/New.cshtml)
//  • Shows only the extra question that goes with the chosen reason (expected amount / original charge / card freeze)
//  • Character count for the description; a quick type and size check of the optional receipt
//  • "Review dispute" unlocks when everything is filled in; the review shows what will happen
// Without JavaScript every part is visible and the form posts normally (the server checks everything again).
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('disputeForm');
    const form = root && root.querySelector('[data-dispute-form]');
    if (!form) return;

    const charge = parseFloat(root.dataset.amount) || 0;
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const $ = sel => form.querySelector(sel);
    const reasons = Array.from(form.querySelectorAll('[data-reason]'));
    const extras = Array.from(form.querySelectorAll('.reason-extra'));
    const expected = $('[data-expected]');
    const expectedMsg = $('[data-expected-msg]');
    const description = $('[data-description]');
    const count = $('[data-count]');
    const attachment = $('[data-attachment]');
    const attachmentError = $('[data-attachment-error]');
    const gate = $('[data-review-gate]');
    const openBtn = $('[data-review-open]');
    const gateMsg = $('[data-gate-msg]');
    const review = $('[data-review]');
    const backBtn = $('[data-review-back-btn]');
    const steps = Array.from(form.querySelectorAll('[data-step]'));
    const originalExpectedMsg = expectedMsg ? expectedMsg.innerHTML : '';

    gate.hidden = false; review.hidden = true; backBtn.hidden = false; $('[data-review-list]').hidden = false;

    const reason = () => (reasons.find(r => r.checked) || {}).value || '';
    const label = () => { const r = reasons.find(x => x.checked); return r ? r.closest('label').querySelector('strong').textContent : ''; };

    function showExtras() {
        const r = reason();
        // A hidden extra is also disabled, so its value isn't sent (e.g. a card freeze ticked, then another reason chosen).
        extras.forEach(x => {
            const on = x.dataset.for === r;
            x.hidden = !on;
            x.querySelectorAll('input').forEach(i => { i.disabled = !on; });
        });
    }

    function problems() {
        const list = [];
        const r = reason();
        if (!r) list.push('what went wrong');
        if (r === 'WrongAmount') {
            const e = parseFloat(expected.value);
            if (!(e > 0)) list.push('the amount you expected');
            else if (e >= charge) list.push('an expected amount less than ' + money(charge));
        }
        if (r === 'Duplicate' && !form.querySelector('[data-duplicate]:checked')) list.push('the original charge');
        if (description.value.trim().length < 20) list.push('what happened (20 characters or more)');
        if (attachmentError.textContent) list.push('a JPG or PNG attachment (or none)');
        return list;
    }

    function update() {
        showExtras();
        const n = description.value.trim().length;
        count.textContent = n < 20 ? (20 - n) + ' more to go' : n + ' / 1,000';
        if (expected && expectedMsg) {
            const e = parseFloat(expected.value);
            if (e > 0 && e < charge) expectedMsg.textContent = 'If we agree, we refund the difference: ' + money(charge - e) + '.';
            else if (e >= charge) expectedMsg.textContent = 'This has to be less than the ' + money(charge) + ' you were charged.';
            else expectedMsg.innerHTML = originalExpectedMsg;   // our own server-rendered markup
        }
        const missing = problems();
        openBtn.disabled = missing.length > 0;
        gateMsg.textContent = missing.length ? 'Still needed: ' + missing.join(', ') + '.' : 'Everything looks good.';
    }

    attachment.addEventListener('change', () => {
        attachmentError.textContent = '';
        const f = attachment.files[0];
        if (f && !/^image\/(jpeg|png)$/.test(f.type)) { attachmentError.textContent = 'Use a JPG or PNG image, or leave this empty.'; attachment.value = ''; }
        else if (f && f.size > 5 * 1024 * 1024) { attachmentError.textContent = 'That file is ' + (f.size / 1048576).toFixed(1) + ' MB. Attachments can be up to 5 MB.'; attachment.value = ''; }
        update();
    });
    form.addEventListener('input', update);
    form.addEventListener('change', update);

    openBtn.addEventListener('click', () => {
        if (problems().length) return;
        const r = reason();
        const refund = r === 'WrongAmount' ? charge - parseFloat(expected.value) : charge;
        $('[data-review-reason]').textContent = label();
        $('[data-review-refund]').textContent = money(refund) + (r === 'WrongAmount' ? ' (the difference)' : '');
        $('[data-review-attachment]').textContent = attachment.files[0] ? attachment.files[0].name : 'None';
        steps.forEach(s => s.hidden = true);
        gate.hidden = true;
        review.hidden = false;
        review.scrollIntoView({ behavior: 'smooth', block: 'start' });
        $('[data-review-title]').focus({ preventScroll: true });
    });
    backBtn.addEventListener('click', () => { steps.forEach(s => s.hidden = false); gate.hidden = false; review.hidden = true; openBtn.focus(); });
    form.addEventListener('submit', () => { const b = $('[data-submit]'); b.disabled = true; b.textContent = 'Submitting…'; });

    update();
})();
