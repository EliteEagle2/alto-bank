// Shared admin console behavior (loaded by _AdminLayout on every /admin page).
(function () {
    'use strict';

    // Tooltips on icon-only buttons (e.g. "API docs" on the { } button).
    if (window.bootstrap) document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => new bootstrap.Tooltip(el));

    // Close the mobile navigation drawer after a link is tapped.
    document.querySelectorAll('#adminSidebar .admin-link').forEach(a => a.addEventListener('click', () => {
        const drawer = window.bootstrap && bootstrap.Offcanvas.getInstance(document.getElementById('adminSidebar'));
        if (drawer) drawer.hide();
    }));

    // Press "/" anywhere (outside a form field) to jump to the customer search.
    const search = document.querySelector('.console-top .console-search input');
    document.addEventListener('keydown', e => {
        if (e.key !== '/' || !search || e.ctrlKey || e.metaKey || e.altKey) return;
        const t = e.target;
        if (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)) return;
        e.preventDefault();
        search.focus();
    });

    // Staff page: show the name fields only when creating a new staff login.
    const create = document.getElementById("modeCreate");
    if (create) {
        document.querySelectorAll("[data-staff-mode]").forEach(r => r.addEventListener("change", () => {
            document.querySelector("[data-create-only]").hidden = !create.checked;
            document.getElementById("staffFirst").required = create.checked;
            document.getElementById("staffLast").required = create.checked;
        }));
    }

    // Overview work queue: filter chips (All / Applications / Frozen / ...).
    const queue = document.querySelector('[data-queue]');
    if (queue) {
        const chips = queue.querySelectorAll('[data-queue-filter]');
        const rows = queue.querySelectorAll('[data-kind]');
        const empty = queue.querySelector('[data-queue-empty]');
        chips.forEach(chip => chip.addEventListener('click', () => {
            const kind = chip.dataset.queueFilter;
            chips.forEach(c => c.setAttribute('aria-pressed', c === chip ? 'true' : 'false'));
            let shown = 0;
            rows.forEach(r => { const on = kind === 'all' || r.dataset.kind === kind; r.hidden = !on; if (on) shown++; });
            if (empty) empty.hidden = shown > 0;
        }));
    }
})();
