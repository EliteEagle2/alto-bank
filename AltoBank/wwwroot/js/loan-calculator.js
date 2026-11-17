// Products/Loans: the payment calculator. Every number comes from GET /api/loans/calculator, which uses the same
// LoanMath as a real Alto loan (decimal math, the last payment adjusted so the loan ends at $0.00), so this page can
// never disagree with an offer. Draws a stacked bar per year (principal vs interest) as inline SVG, with a tooltip on
// hover or keyboard focus, and fills the full schedule table (the chart's text alternative).
(function () {
    'use strict';
    const root = document.getElementById('loanCalc');
    if (!root) return;
    const $ = sel => root.querySelector(sel);
    const amount = $('#calc-amount'), range = $('#calc-amount-range'), months = $('#calc-months'), apr = $('#calc-apr');
    const chart = $('[data-calc-chart]'), tip = $('[data-calc-tip]'), error = $('[data-calc-error]');
    const money = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const whole = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const LIMITS = { Personal: { min: 1000, max: 25000, terms: [12, 24, 36, 48, 60] }, Auto: { min: 5000, max: 50000, terms: [24, 36, 48, 60, 72] } };
    const kind = () => (root.querySelector('input[name="calc-kind"]:checked') || {}).value || 'Personal';
    const SVG = 'http://www.w3.org/2000/svg';
    const node = (tag, attrs) => { const n = document.createElementNS(SVG, tag); for (const k in attrs) n.setAttribute(k, attrs[k]); return n; };

    function applyKind(resetRate) {
        const l = LIMITS[kind()];
        [amount, range].forEach(el => { el.min = l.min; el.max = l.max; });
        const a = Math.min(l.max, Math.max(l.min, +amount.value || l.min));
        amount.value = range.value = a;
        Array.from(months.options).forEach(o => { const ok = l.terms.includes(+o.value); o.hidden = !ok; o.disabled = !ok; });
        if (!l.terms.includes(+months.value)) months.value = kind() === 'Auto' ? '60' : '36';
        if (resetRate) apr.value = kind() === 'Auto' ? root.dataset.autoRate : root.dataset.personalRate;
    }

    let timer = null, seq = 0;
    function schedule() { clearTimeout(timer); timer = setTimeout(update, 180); }

    async function update() {
        const l = LIMITS[kind()];
        const a = +amount.value, r = +apr.value, n = +months.value;
        let problem = '';
        if (!(a >= l.min && a <= l.max)) problem = `${kind()} loans are ${whole(l.min)} to ${whole(l.max)}.`;
        else if (!(r >= 0 && r <= 36)) problem = 'Enter an APR from 0% to 36%.';
        error.hidden = !problem;
        error.textContent = problem;
        if (problem) return;
        const mine = ++seq;
        try {
            const res = await fetch(`${root.dataset.api}?amount=${a}&apr=${r}&months=${n}`, { headers: { Accept: 'application/json' } });
            if (!res.ok) throw new Error(res.status);
            const q = await res.json();
            if (mine === seq) render(q);   // ignore answers to older requests
        } catch (e) {
            error.hidden = false;
            error.textContent = "Couldn't calculate right now. Please try again.";
        }
    }

    function render(q) {
        $('[data-calc-payment]').textContent = money(q.monthlyPayment);
        $('[data-calc-interest]').textContent = money(q.totalInterest);
        $('[data-calc-total]').textContent = money(q.totalOfPayments);
        $('[data-calc-final]').textContent = q.finalPayment !== q.monthlyPayment
            ? `${q.termMonths - 1} payments of ${money(q.monthlyPayment)}, then a last payment of ${money(q.finalPayment)} so the loan ends at exactly $0.00.`
            : `${q.termMonths} payments of ${money(q.monthlyPayment)}. The loan ends at exactly $0.00.`;

        // Schedule table (built with textContent only).
        const body = $('[data-calc-rows]');
        body.textContent = '';
        q.schedule.forEach(r => {
            const tr = document.createElement('tr');
            [String(r.number), money(r.payment), money(r.interest), money(r.principal), money(r.balanceAfter)].forEach((t, i) => {
                const td = document.createElement('td'); td.textContent = t; if (i) td.className = 'text-end'; tr.append(td);
            });
            body.append(tr);
        });

        // One bar per year: principal at the bottom, interest on top.
        const years = [];
        q.schedule.forEach((r, i) => {
            const y = Math.floor(i / 12);
            years[y] ||= { principal: 0, interest: 0, endBalance: 0 };
            years[y].principal += r.principal; years[y].interest += r.interest; years[y].endBalance = r.balanceAfter;
        });
        drawChart(years);
    }

    function drawChart(years) {
        chart.querySelectorAll('svg').forEach(s => s.remove());
        const W = 600, H = 250, left = 52, right = 8, top = 10, bottom = 26;
        const max = Math.max(...years.map(y => y.principal + y.interest));
        const step = niceStep(max / 4);
        const top$ = Math.ceil(max / step) * step;
        const yScale = v => top + (H - top - bottom) * (1 - v / top$);
        const svg = node('svg', { viewBox: `0 0 ${W} ${H}`, 'aria-hidden': 'true' });
        const axis = node('g', { class: 'axis' });
        for (let v = 0; v <= top$ + 0.01; v += step) {
            axis.append(node('line', { x1: left, x2: W - right, y1: yScale(v), y2: yScale(v) }));
            const t = node('text', { x: left - 8, y: yScale(v) + 4, 'text-anchor': 'end' }); t.textContent = whole(v); axis.append(t);
        }
        svg.append(axis);
        const slot = (W - left - right) / years.length;
        const bw = Math.min(56, slot * 0.56);
        years.forEach((y, i) => {
            const x = left + slot * i + (slot - bw) / 2;
            const p0 = yScale(0), p1 = yScale(y.principal), i1 = yScale(y.principal + y.interest);
            const gap = y.interest > 0 ? 2 : 0;   // a 2px surface gap between the two segments
            svg.append(node('rect', { class: 'bar-principal', x, y: p1, width: bw, height: Math.max(0, p0 - p1) }));
            if (y.interest > 0) svg.append(roundedTop('bar-interest', x, i1, bw, Math.max(0, p1 - i1 - gap)));
            const label = node('text', { x: x + bw / 2, y: H - 8, 'text-anchor': 'middle', class: 'axis' });
            label.textContent = years.length > 8 ? String(i + 1) : `Year ${i + 1}`;
            svg.append(label);
            // A hit area bigger than the bar, focusable, for the tooltip.
            const hit = node('rect', { class: 'hit', x: left + slot * i, y: top, width: slot, height: H - top - bottom, tabindex: '0',
                'aria-label': `Year ${i + 1}: principal ${money(y.principal)}, interest ${money(y.interest)}` });
            const show = () => {
                tip.hidden = false;
                tip.textContent = '';
                const strong = document.createElement('strong'); strong.textContent = `Year ${i + 1}`;
                const l1 = document.createElement('div'); l1.textContent = `Principal ${money(y.principal)}`;
                const l2 = document.createElement('div'); l2.textContent = `Interest ${money(y.interest)}`;
                const l3 = document.createElement('div'); l3.className = 'text-muted-alto'; l3.textContent = `Left to repay ${money(y.endBalance)}`;
                tip.append(strong, l1, l2, l3);
                const box = chart.getBoundingClientRect(), scale = box.width / W;
                tip.style.left = `${(x + bw / 2) * scale}px`;
                tip.style.top = `${i1 * scale}px`;
            };
            hit.addEventListener('mouseenter', show); hit.addEventListener('focus', show);
            hit.addEventListener('mouseleave', () => { tip.hidden = true; }); hit.addEventListener('blur', () => { tip.hidden = true; });
            svg.append(hit);
        });
        chart.prepend(svg);
    }

    // A bar with 4px rounded top corners (the data end), square at the bottom.
    function roundedTop(cls, x, y, w, h) {
        const rr = Math.min(4, h, w / 2);
        const d = `M${x},${y + h} V${y + rr} Q${x},${y} ${x + rr},${y} H${x + w - rr} Q${x + w},${y} ${x + w},${y + rr} V${y + h} Z`;
        return node('path', { class: cls, d });
    }
    function niceStep(raw) {
        const pow = Math.pow(10, Math.floor(Math.log10(Math.max(raw, 1))));
        return [1, 2, 2.5, 5, 10].map(m => m * pow).find(s => s >= raw) || pow * 10;
    }

    root.querySelectorAll('input[name="calc-kind"]').forEach(r => r.addEventListener('change', () => { applyKind(true); schedule(); }));
    amount.addEventListener('input', () => { range.value = amount.value; schedule(); });
    range.addEventListener('input', () => { amount.value = range.value; schedule(); });
    months.addEventListener('change', schedule);
    apr.addEventListener('input', schedule);
    window.addEventListener('resize', () => { tip.hidden = true; });
    applyKind(false);
    update();
})();
