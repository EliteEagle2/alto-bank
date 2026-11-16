// Budgets page (Views/Budgets/Index.cshtml): picking a category shows what you spent on it last month and,
// if the limit box is still empty, fills that in as a starting point (you can change it before saving).
(function () {
    'use strict';
    const select = document.querySelector('[data-budget-category]');
    const hint = document.querySelector('[data-limit-hint]');
    if (!select || !hint) return;
    const input = select.form.querySelector('input[name="Form.MonthlyLimit"]');
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const defaultHint = hint.textContent;
    let suggested = null;   // the value we filled in, so we only replace our own suggestion, never what you typed

    select.addEventListener('change', () => {
        const opt = select.selectedOptions[0];
        const last = parseFloat(opt && opt.dataset.last);
        if (!opt || !opt.value) { hint.textContent = defaultHint; return; }
        const name = opt.textContent.trim();
        if (last > 0) {
            const round = Math.ceil(last / 5) * 5;   // a round number just above last month
            hint.textContent = `Last month you spent ${money(last)} on ${name}. We filled in ${money(round)} as a starting point; change it if you like.`;
            if (!input.value || input.value === suggested) { input.value = round.toFixed(2); suggested = input.value; }
        } else {
            hint.textContent = `You didn't spend anything on ${name} last month. Enter the most you want to spend in a month.`;
            if (input.value === suggested) { input.value = ''; suggested = null; }
        }
    });
})();
