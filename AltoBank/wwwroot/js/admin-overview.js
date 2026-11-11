// Admin overview (Areas/Admin/Views/Dashboard/Index.cshtml)
// Loads GET /api/admin/stats and draws three Chart.js charts:
//   transactions per day, deposits by account type, new accounts per week.
(function () {
    'use strict';
    const daysSelect = document.getElementById('statsDays');
    if (!daysSelect || !window.Chart) return;

    const css = getComputedStyle(document.documentElement);
    const color = name => css.getPropertyValue(name).trim();
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const shortDate = d => new Date(d + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
    const charts = {};

    Chart.defaults.color = color('--muted');
    Chart.defaults.font.family = 'Inter, system-ui, sans-serif';
    Chart.defaults.borderColor = 'rgba(135, 150, 174,0.14)';

    function draw(id, config) {
        const canvas = document.getElementById(id);
        if (!canvas) return;
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

            const perDay = s.transactionsPerDay;
            const label = document.querySelector('[data-range-label]');
            if (label) label.textContent = `${perDay.reduce((a, d) => a + d.count, 0).toLocaleString('en-US')} transactions`;

            const ctx = document.getElementById('txChart').getContext('2d');
            const fill = ctx.createLinearGradient(0, 0, 0, 260);
            fill.addColorStop(0, 'rgba(143, 184, 232,0.40)');
            fill.addColorStop(1, 'rgba(242, 181, 68,0.02)');
            draw('txChart', {
                type: 'line',
                data: { labels: perDay.map(d => shortDate(d.date)),
                        datasets: [{ label: 'Transactions', data: perDay.map(d => d.count), fill: true, backgroundColor: fill,
                                     borderColor: color('--accent'), borderWidth: 2, tension: .35, pointRadius: 0, pointHoverRadius: 4 }] },
                options: { maintainAspectRatio: false, interaction: { mode: 'index', intersect: false },
                           plugins: { legend: { display: false },
                                      tooltip: { callbacks: { afterLabel: c => 'Volume: ' + money(perDay[c.dataIndex].volume) } } },
                           scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { ticks: { maxTicksLimit: 8 } } } }
            });

            const types = Object.keys(s.depositsByType);
            draw('typeChart', {
                type: 'doughnut',
                data: { labels: types, datasets: [{ data: types.map(t => s.depositsByType[t]),
                        backgroundColor: [color('--accent'), color('--primary'), color('--success')], borderColor: '#18233A', borderWidth: 3 }] },
                options: { maintainAspectRatio: false, cutout: '64%',
                           plugins: { legend: { position: 'bottom' }, tooltip: { callbacks: { label: c => `${c.label}: ${money(c.parsed)}` } } } }
            });

            draw('weekChart', {
                type: 'bar',
                data: { labels: s.newAccountsPerWeek.map(w => shortDate(w.weekStart)),
                        datasets: [{ label: 'New accounts', data: s.newAccountsPerWeek.map(w => w.count),
                                     backgroundColor: color('--primary'), borderRadius: 5, maxBarThickness: 22 }] },
                options: { maintainAspectRatio: false, plugins: { legend: { display: false } },
                           scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { ticks: { maxTicksLimit: 6 } } } }
            });
        } catch (e) {
            err.textContent = 'Could not load chart data (' + e.message + ').';
            err.classList.remove('d-none');
        }
    }

    daysSelect.addEventListener('change', () => load(parseInt(daysSelect.value, 10)));
    load(30);
})();
