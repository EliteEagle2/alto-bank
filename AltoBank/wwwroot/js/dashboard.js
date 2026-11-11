// Customer dashboard: 30-day money in vs. out bar chart (Chart.js from cdnjs).
(function () {
    'use strict';
    const canvas = document.getElementById('flowChart');
    if (!canvas || !window.Chart) return;

    let flows = [];
    try { flows = JSON.parse(canvas.dataset.flows || '[]'); } catch (e) { return; }

    const css = getComputedStyle(document.documentElement);
    const color = n => css.getPropertyValue(n).trim();
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    Chart.defaults.color = color('--muted');
    Chart.defaults.font.family = 'Inter, system-ui, sans-serif';

    new Chart(canvas, {
        type: 'bar',
        data: {
            labels: flows.map(f => new Date(f.date + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' })),
            datasets: [
                { label: 'Money in', data: flows.map(f => f.in), backgroundColor: color('--success'), borderRadius: 4, maxBarThickness: 10 },
                { label: 'Money out', data: flows.map(f => -f.out), backgroundColor: color('--danger'), borderRadius: 4, maxBarThickness: 10 }
            ]
        },
        options: {
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            plugins: {
                legend: { position: 'bottom', labels: { boxWidth: 10, boxHeight: 10 } },
                tooltip: { callbacks: { label: c => `${c.dataset.label}: ${money(Math.abs(c.parsed.y))}` } }
            },
            scales: {
                x: { stacked: true, grid: { display: false }, ticks: { maxTicksLimit: 6 } },
                y: { stacked: true, grid: { color: 'rgba(135, 150, 174,0.12)' }, ticks: { callback: v => money(Math.abs(v)).replace('.00', '') } }
            }
        }
    });
})();
