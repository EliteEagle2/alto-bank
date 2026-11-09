// Admin overview (Areas/Admin/Views/Dashboard/Index.cshtml)
// Loads GET /api/admin/stats and fills the KPI tiles + Chart.js charts.
(function () {
    'use strict';
    const daysSelect = document.getElementById('statsDays');
    if (!daysSelect) return;

    const css = getComputedStyle(document.documentElement);
    const color = name => css.getPropertyValue(name).trim();
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const num = v => v.toLocaleString('en-US');
    const charts = {};

    if (window.Chart) {
        Chart.defaults.color = color('--muted');
        Chart.defaults.font.family = 'Inter, system-ui, sans-serif';
        Chart.defaults.borderColor = 'rgba(138,147,166,0.14)';
    }

    function setKpi(key, text) {
        const el = document.querySelector(`[data-kpi="${key}"]`);
        if (el) el.textContent = text;
    }

    function draw(id, config) {
        const canvas = document.getElementById(id);
        if (!canvas || !window.Chart) return;
        if (charts[id]) charts[id].destroy();
        charts[id] = new Chart(canvas, config);
    }

    async function load(days) {
        const err = document.getElementById('statsError');
        err.classList.add('d-none');
        try {
            const res = await fetch(`/api/admin/stats?days=${days}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!res.ok) throw new Error('HTTP ' + res.status);
            const s = await res.json();

            setKpi('customers', num(s.totalCustomers));
            setKpi('deposits', money(s.totalDeposits));
            setKpi('accounts', num(s.totalAccounts));
            setKpi('applications', num(s.applicationsLast30Days));

            const days = s.transactionsPerDay;
            const label = document.querySelector('[data-range-label]');
            if (label) label.textContent = `${num(days.reduce((a, d) => a + d.count, 0))} transactions`;

            const ctx = document.getElementById('txChart')?.getContext('2d');
            let fill = 'rgba(124,77,255,0.15)';
            if (ctx) { fill = ctx.createLinearGradient(0, 0, 0, 260); fill.addColorStop(0, 'rgba(124,77,255,0.4)'); fill.addColorStop(1, 'rgba(0,229,255,0.02)'); }
            draw('txChart', {
                type: 'line',
                data: { labels: days.map(d => new Date(d.date + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' })),
                        datasets: [{ label: 'Transactions', data: days.map(d => d.count), fill: true, backgroundColor: fill,
                                     borderColor: color('--accent'), borderWidth: 2, tension: .35, pointRadius: 0, pointHoverRadius: 4 }] },
                options: { maintainAspectRatio: false, interaction: { mode: 'index', intersect: false },
                           plugins: { legend: { display: false } },
                           scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { ticks: { maxTicksLimit: 8 } } } }
            });

            const types = Object.keys(s.accountsByType);
            draw('typeChart', {
                type: 'doughnut',
                data: { labels: types, datasets: [{ data: types.map(t => s.accountsByType[t]),
                        backgroundColor: [color('--accent'), color('--primary'), color('--success')], borderColor: '#141A2A', borderWidth: 3 }] },
                options: { maintainAspectRatio: false, cutout: '66%', plugins: { legend: { position: 'bottom' } } }
            });
        } catch (e) {
            err.textContent = 'Could not load statistics (' + e.message + ').';
            err.classList.remove('d-none');
        }
    }

    daysSelect.addEventListener('change', () => load(parseInt(daysSelect.value, 10)));
    load(30);
})();
