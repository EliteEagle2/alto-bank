// Pay card (Alto Peak) and Make a loan payment: shows the amount box only when "Other amount" / "Extra toward
// principal" is picked, and focuses it.
(function () {
    'use strict';
    const form = document.getElementById('payCardForm') || document.getElementById('payLoanForm');
    if (!form) return;
    const box = form.querySelector('[data-custom-amount]');
    const input = box && box.querySelector('input');
    form.addEventListener('change', function (e) {
        if ((e.target.name !== 'Option' && e.target.name !== 'Kind') || !box) return;
        const custom = e.target.hasAttribute('data-custom-option');
        box.hidden = !custom;
        if (custom && input) input.focus();
    });
})();
