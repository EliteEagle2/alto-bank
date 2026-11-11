// Shows only the date fields that matter for the chosen "When" option (Views/Shared/_ScheduleWhen.cshtml),
// updates the submit button text, and lets "Pay" on a payee card pick that payee in the form.
// Hidden fields are disabled so they're not submitted; the server validates everything again.
(function () {
    'use strict';
    document.querySelectorAll('form').forEach(form => {
        const radios = form.querySelectorAll('[data-when]');
        if (!radios.length) return;
        const submit = form.querySelector('[data-submit]');
        function sync() {
            const when = (form.querySelector('[data-when]:checked') || {}).value;
            form.querySelectorAll('[data-show]').forEach(el => {
                const show = el.dataset.show.split(' ').includes(when);
                el.hidden = !show;
                el.querySelectorAll('input, select').forEach(i => { i.disabled = !show; });
            });
            form.querySelectorAll('[data-label-once]').forEach(el => { el.hidden = when !== 'once'; });
            form.querySelectorAll('[data-label-repeat]').forEach(el => { el.hidden = when === 'once'; });
            if (submit) submit.textContent = when === 'now' ? submit.dataset.now : submit.dataset.later;
        }
        radios.forEach(r => r.addEventListener('change', sync));
        sync();
    });

    // Bill Pay: "Pay" on a payee card selects that payee and jumps to the form.
    document.querySelectorAll('[data-pay-payee]').forEach(btn => btn.addEventListener('click', () => {
        const select = document.getElementById('Pay_PayeeId');
        if (!select) return;
        select.value = btn.dataset.payPayee;
        select.closest('form').scrollIntoView({ behavior: 'smooth', block: 'start' });
        setTimeout(() => document.getElementById('Pay_Amount')?.focus(), 350);
    }));
})();
