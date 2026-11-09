// =====================================================================
// Admin dashboard (Views/Admin/Index.cshtml)
// Fetches GET /api/stats?days=N (JSON, same login cookie) and draws KPI tiles + Chart.js charts.
// =====================================================================
(function () {
    'use strict';
    const daysSelect = document.getElementById('statsDays');
    if (!daysSelect) return;

    const css = getComputedStyle(document.documentElement);
    const color = name => css.getPropertyValue(name).trim();
    const fmtMoney = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    let txChart = null, typeChart = null;

    if (window.Chart) {
        Chart.defaults.color = color('--muted');
        Chart.defaults.font.family = 'Inter, system-ui, sans-serif';
        Chart.defaults.borderColor = 'rgba(138,147,166,0.15)';
    }

    async function load(days) {
        const errorBox = document.getElementById('statsError');
        errorBox.classList.add('d-none');
        try {
            const res = await fetch(`/api/stats?days=${days}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (!res.ok) throw new Error(`The stats API returned ${res.status}.`);
            const s = await res.json();
            renderKpis(s);
            renderCharts(s, days);
        } catch (err) {
            errorBox.textContent = 'Could not load statistics: ' + err.message;
            errorBox.classList.remove('d-none');
        }
    }

    function renderKpis(s) {
        const set = (key, text) => { document.querySelector(`[data-kpi="${key}"]`).textContent = text; };
        set('customers', s.totalCustomers.toLocaleString('en-US'));
        set('deposits', fmtMoney(s.totalDeposits));
        set('accounts', s.totalAccounts.toLocaleString('en-US'));
        set('applications', s.applicationsLast30Days.toLocaleString('en-US'));
    }

    function renderCharts(s, days) {
        if (!window.Chart) return;
        const labels = s.transactionsPerDay.map(d => new Date(d.date + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' }));
        const counts = s.transactionsPerDay.map(d => d.count);
        document.querySelector('[data-range-label]').textContent = `${counts.reduce((a, b) => a + b, 0).toLocaleString('en-US')} transactions in ${days} days`;

        const ctx = document.getElementById('txChart').getContext('2d');
        const gradient = ctx.createLinearGradient(0, 0, 0, 280);
        gradient.addColorStop(0, 'rgba(0,229,255,0.35)');
        gradient.addColorStop(1, 'rgba(124,77,255,0.02)');

        if (txChart) txChart.destroy();
        txChart = new Chart(ctx, {
            type: 'line',
            data: { labels, datasets: [{
                label: 'Transactions', data: counts, fill: true, backgroundColor: gradient,
                borderColor: color('--primary'), borderWidth: 2, tension: 0.35, pointRadius: 0, pointHoverRadius: 5,
                pointHoverBackgroundColor: color('--primary')
            }] },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: { legend: { display: false },
                    tooltip: { callbacks: { afterLabel: c => 'Volume: ' + fmtMoney(s.transactionsPerDay[c.dataIndex].volume) } } },
                scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { ticks: { maxTicksLimit: 10 } } }
            }
        });

        const types = Object.keys(s.accountsByType);
        if (typeChart) typeChart.destroy();
        typeChart = new Chart(document.getElementById('typeChart'), {
            type: 'doughnut',
            data: { labels: types, datasets: [{
                data: types.map(t => s.accountsByType[t]),
                backgroundColor: [color('--primary'), color('--accent'), color('--success')],
                borderColor: color('--surface'), borderWidth: 3
            }] },
            options: { maintainAspectRatio: false, cutout: '68%', plugins: { legend: { position: 'bottom' } } }
        });

        const legend = document.querySelector('[data-type-legend]');
        legend.textContent = types.map(t => `${t}: ${fmtMoney(s.balancesByType[t])}`).join(' · ');
    }

    daysSelect.addEventListener('change', () => load(parseInt(daysSelect.value, 10)));
    load(30);
})();
