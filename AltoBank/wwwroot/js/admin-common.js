// Shared admin portal behavior (loaded by _AdminLayout on every /admin page).
(function () {
    'use strict';

    // Close the mobile sidebar drawer after a link is tapped.
    document.querySelectorAll('#adminSidebar .admin-link').forEach(a => a.addEventListener('click', () => {
        const drawer = window.bootstrap && bootstrap.Offcanvas.getInstance(document.getElementById('adminSidebar'));
        if (drawer) drawer.hide();
    }));
})();
