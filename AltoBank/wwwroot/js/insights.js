// =====================================================================
// Spending Insights page (Views/Insights/Index.cshtml)
//  • Donut by category    ← GET /api/insights/summary?month=yyyy-MM
//  • Month-over-month bars ← GET /api/insights/trend?month=yyyy-MM
//  • Picking a month in the dropdown opens that month
//  • Category picker in the drill-down saves through the JSON API (no reload)
// Every number is also written on the page (the category list is the donut's legend and table), so the charts
// are an extra view, never the only one.
// =====================================================================
(function () {
    'use strict';
    const root = document.getElementById('insights');
    if (!root) return;
    const month = root.dataset.month;
    const money = v => Number(v).toLocaleString('en-US', { style: 'currency', currency: 'USD' });

    // Month dropdown: open the month as soon as it's picked (the "Show" button is there without JavaScript).
    const select = document.querySelector('[data-autosubmit]');
    if (select) select.addEventListener('change', () => select.form.submit());

    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (!res.ok) throw new Error('Request failed (' + res.status + ')');
        return res.json();
    }

    if (window.Chart) {
        const css = getComputedStyle(document.body);
        const color = n => css.getPropertyValue(n).trim();
        Chart.defaults.color = color('--muted');
        Chart.defaults.font.family = "'Public Sans', system-ui, sans-serif";
        const surface = color('--surface') || '#18233A';

        // ---------- Donut: spending by category ----------
        const donut = document.getElementById('categoryChart');
        if (donut) {
            getJson('/api/insights/summary?month=' + encodeURIComponent(month)).then(data => {
                const cats = data.categories.filter(c => c.amount > 0);
                const total = cats.reduce((s, c) => s + c.amount, 0);
                new Chart(donut, {
                    type: 'doughnut',
                    data: {
                        labels: cats.map(c => c.label),
                        datasets: [{
                            data: cats.map(c => c.amount),
                            backgroundColor: cats.map(c => c.color),
                            borderColor: surface, borderWidth: 2,         // a 2px gap between slices
                            hoverOffset: 6
                        }]
                    },
                    options: {
                        maintainAspectRatio: false,
                        cutout: '68%',
                        plugins: {
                            legend: { display: false },                   // the list beside the chart is the legend
                            tooltip: { callbacks: { label: c => ` ${money(c.parsed)} (${Math.round(c.parsed / total * 100)}%)` } }
                        },
                        // Click a slice to see that category's transactions (same as clicking it in the list).
                        onClick: (e, items) => {
                            if (!items.length) return;
                            const key = cats[items[0].index].key;
                            location.href = `/insights?month=${encodeURIComponent(month)}&category=${encodeURIComponent(key)}#drilldown`;
                        },
                        onHover: (e, items) => { e.native.target.style.cursor = items.length ? 'pointer' : 'default'; }
                    }
                });
            }).catch(() => donut.closest('.donut-chart').classList.add('chart-failed'));
        }

        // ---------- Bars: total spending per month ----------
        const trend = document.getElementById('trendChart');
        if (trend) {
            getJson('/api/insights/trend?month=' + encodeURIComponent(month)).then(rows => {
                const accent = color('--accent') || '#8FB8E8';
                new Chart(trend, {
                    type: 'bar',
                    data: {
                        labels: rows.map(r => r.label.split(' ')[0]),
                        datasets: [{
                            label: 'Spending',
                            data: rows.map(r => r.spending),
                            // The month you're looking at is solid; the others are lighter.
                            backgroundColor: rows.map(r => r.month === month ? accent : 'rgba(143, 184, 232, 0.38)'),
                            borderRadius: 4, maxBarThickness: 44
                        }]
                    },
                    options: {
                        maintainAspectRatio: false,
                        plugins: {
                            legend: { display: false },
                            tooltip: { callbacks: { title: c => rows[c[0].dataIndex].label, label: c => ' ' + money(c.parsed.y) } }
                        },
                        scales: {
                            x: { grid: { display: false } },
                            y: { beginAtZero: true, grid: { color: 'rgba(135, 150, 174, 0.12)' }, ticks: { callback: v => money(v).replace('.00', ''), maxTicksLimit: 5 } }
                        },
                        onClick: (e, items) => {
                            if (items.length) location.href = '/insights?month=' + encodeURIComponent(rows[items[0].index].month);
                        },
                        onHover: (e, items) => { e.native.target.style.cursor = items.length ? 'pointer' : 'default'; }
                    }
                });
            }).catch(() => trend.closest('.trend-chart').classList.add('chart-failed'));
        }
    }

    // ---------- Change a category from the drill-down list ----------
    const toastEl = document.getElementById('altoToast');
    function toast(message, isError) {
        if (!toastEl || !window.bootstrap) return;
        toastEl.querySelector('[data-toast-text]').textContent = message;
        toastEl.classList.toggle('is-error', !!isError);
        bootstrap.Toast.getOrCreateInstance(toastEl).show();
    }
    document.querySelectorAll('[data-drill-cat]').forEach(form => {
        const selectEl = form.elements.category;
        const btn = form.querySelector('button[type="submit"]');
        // The Save button only appears once a different category is picked.
        const original = selectEl.value;
        btn.hidden = true;
        selectEl.addEventListener('change', () => { btn.hidden = selectEl.value === original; });
        form.addEventListener('submit', async e => {
            e.preventDefault();
            btn.disabled = true;
            try {
                const res = await fetch('/api/insights/transactions/' + form.dataset.txId + '/category', {
                    method: 'POST', credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                    body: JSON.stringify({ category: selectEl.value })
                });
                const body = await res.json().catch(() => null);
                if (!res.ok) throw new Error((body && body.title) || 'Could not change the category.');
                // It moved out of this category: reload so the totals, chart and list all agree.
                toast(body.message + ' Updating your totals…');
                setTimeout(() => location.reload(), 700);
            } catch (err) {
                toast(err.message, true);
                btn.disabled = false;
            }
        });
    });
})();
