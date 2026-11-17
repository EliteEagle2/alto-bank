// ALTO PEAK: Pay card page. Shows the amount box only when "Other amount" is picked, and focuses it.
(function () {
    'use strict';
    const form = document.getElementById('payCardForm');
    if (!form) return;
    const box = form.querySelector('[data-custom-amount]');
    const input = box && box.querySelector('input');
    form.addEventListener('change', function (e) {
        if (e.target.name !== 'Option' || !box) return;
        const custom = e.target.hasAttribute('data-custom-option');
        box.hidden = !custom;
        if (custom && input) input.focus();
    });
})();
