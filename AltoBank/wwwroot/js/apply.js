// =====================================================================
// Alto Bank — online account application (Views/Accounts/Apply.cshtml)
//
//  • One step visible at a time, slide transitions, Back / Continue
//  • Inline validation as you type (green check / red message)
//  • Continue is disabled until the step is valid
//  • Phone auto-format, DOB auto-advance, simulated ZIP → city/state lookup
//  • Simulated identity check + 2-question security quiz
//  • Collapsible review built from your answers
//  • Progress auto-saved to sessionStorage (refresh-safe), cleared after submit
//
// IMPORTANT: this is only for a nicer experience. AccountsController.Apply re-checks
// EVERYTHING on the server, because anything running in the browser can be bypassed.
// =====================================================================
(function () {
    'use strict';

    const form = document.getElementById('applyForm');
    if (!form) return;

    // ---------- Small helpers ----------
    const $ = (sel, root = form) => root.querySelector(sel);
    const $$ = (sel, root = form) => Array.from(root.querySelectorAll(sel));
    const byId = id => document.getElementById(id);
    const fmtMoney = v => v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
    const digits = s => (s || '').replace(/\D/g, '');
    const norm = s => (s || '').trim().replace(/\s+/g, ' ').toLowerCase();
    const wait = ms => new Promise(r => setTimeout(r, ms));
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const TITLES = ['Account', 'About you', 'Contact & address', 'Employment', 'Verify identity', 'Fund account', 'Review & sign'];
    const LAST = 7;
    const CITIZENSHIP = { US: 'U.S. citizen', PR: 'Permanent resident', Other: 'Other' };
    const DESIGNS = { aurora: 'Aurora', violet: 'Violet Nebula', onyx: 'Onyx Glow' };

    // Simulated ZIP lookup table (a real bank would call an address-verification service).
    const ZIPS = {
        '16601': ['Altoona', 'PA'], '16602': ['Altoona', 'PA'], '16603': ['Altoona', 'PA'],
        '16617': ['Bellwood', 'PA'], '16635': ['Duncansville', 'PA'], '16648': ['Hollidaysburg', 'PA'],
        '16686': ['Tyrone', 'PA'], '16801': ['State College', 'PA'], '15601': ['Greensburg', 'PA'],
        '15901': ['Johnstown', 'PA'], '15213': ['Pittsburgh', 'PA'], '15222': ['Pittsburgh', 'PA'],
        '17055': ['Mechanicsburg', 'PA'], '17101': ['Harrisburg', 'PA'], '18101': ['Allentown', 'PA'],
        '19103': ['Philadelphia', 'PA'], '19104': ['Philadelphia', 'PA'], '10001': ['New York', 'NY'],
        '02108': ['Boston', 'MA'], '20001': ['Washington', 'DC'], '30303': ['Atlanta', 'GA'],
        '33131': ['Miami', 'FL'], '60601': ['Chicago', 'IL'], '78701': ['Austin', 'TX'],
        '80202': ['Denver', 'CO'], '90012': ['Los Angeles', 'CA'], '94105': ['San Francisco', 'CA'],
        '98101': ['Seattle', 'WA'],
    };
    const DECOY_STREETS = ['418 Maple Ridge Rd', '77 Lakeview Dr', '2901 Juniata Gap Rd', '15 Orchard Ln',
                           '603 Pine Hollow Ct', '1250 Ridgecrest Blvd', '38 Sycamore St', '910 Logan Blvd'];

    const returning = form.dataset.returning === 'true';
    const hasServerErrors = form.dataset.hasErrors === 'true';
    const storageKey = 'alto.apply.' + form.dataset.userKey;

    // ---------- Reading form values ----------
    // Radios are read by name; everything else by id (asp-for sets id = property name).
    function get(name) {
        if (name === 'DepositPositive') return depositValue() > 0 ? 'true' : 'false';
        const radio = form.querySelector(`input[type=radio][name="${name}"]`);
        if (radio) {
            const checked = form.querySelector(`input[type=radio][name="${name}"]:checked`);
            return checked ? checked.value : '';
        }
        const el = byId(name);
        if (!el) return '';
        if (el.type === 'checkbox') return el.checked;
        return (el.value || '').trim();
    }
    const checkedValues = name => $$(`input[type=checkbox][name="${name}"]:checked`).map(c => c.value);

    // Money: '' = 0. Rejects negatives, letters, more than 2 decimals.
    function money(raw) {
        raw = (raw || '').trim().replace(/[$,]/g, '');
        if (raw === '') return { ok: true, value: 0 };
        if (!/^\d+(\.\d{1,2})?$/.test(raw)) return { ok: false, value: 0 };
        return { ok: true, value: parseFloat(raw) };
    }
    const depositValue = () => { const m = money(get('InitialDeposit')); return m.ok ? m.value : 0; };

    function fundingSource() {
        const sel = byId('FundingAccountId');
        const opt = sel && sel.selectedOptions[0];
        if (!opt || !opt.value) return null; // external bank
        return { name: opt.dataset.name, balance: parseFloat(opt.dataset.balance) };
    }

    // ---------- Show / hide conditional sections ----------
    // data-show-when="Field=Value" (several conditions joined with &)
    const conditionMet = expr => expr.split('&').every(part => {
        const [name, expected] = part.split('=');
        return String(get(name)) === expected;
    });
    function isShown(el) {
        for (let n = el; n && n !== form; n = n.parentElement) {
            if (n.dataset && n.dataset.showWhen && !conditionMet(n.dataset.showWhen)) return false;
        }
        return true;
    }

    // ---------- Validation rules (mirror the server's rules) ----------
    const NAME_RE = /^[A-Za-z][A-Za-z' .\-]*$/;
    const required = (v, msg, max) => !v ? msg : (max && v.length > max ? `Keep this under ${max} characters.` : '');
    const nameRule = (v, label) => !v ? `Enter your ${label}.` : !NAME_RE.test(v) ? 'Use letters, spaces, apostrophes, periods, or hyphens.' : '';
    const zipRule = (v, label) => !v ? `Enter your ${label}.` : /^\d{5}$/.test(v) ? '' : 'ZIP code must be exactly 5 digits.';

    function dobRule() {
        const m = get('DobMonth'), d = get('DobDay'), y = get('DobYear');
        if (!m || !d || !y) return 'Enter your date of birth (MM / DD / YYYY).';
        if (!/^\d{1,2}$/.test(m) || !/^\d{1,2}$/.test(d) || !/^\d{4}$/.test(y)) return 'Use numbers only: MM / DD / YYYY.';
        const mm = +m, dd = +d, yy = +y;
        const date = new Date(yy, mm - 1, dd);
        if (mm < 1 || mm > 12 || date.getMonth() !== mm - 1 || date.getDate() !== dd || yy < 1900) return 'Enter a real date of birth.';
        const today = new Date();
        if (date > today) return 'Enter a real date of birth.';
        let age = today.getFullYear() - yy;
        if (today.getMonth() < mm - 1 || (today.getMonth() === mm - 1 && today.getDate() < dd)) age--;
        if (age < 18) return 'You must be 18 or older to open an account.';
        if (age > 120) return 'Please check the year you were born.';
        return '';
    }

    function phoneRule() {
        let d = digits(get('Phone'));
        if (!d) return 'Enter your mobile phone number.';
        if (d.length === 11 && d[0] === '1') d = d.slice(1);
        return /^[2-9]\d{2}[2-9]\d{6}$/.test(d) ? '' : 'Enter a valid 10-digit U.S. mobile number.';
    }

    function signatureOk(sig) {
        const s = norm(sig.replace(/\./g, ' '));
        const first = get('FirstName'), last = get('LastName'), mi = get('MiddleInitial');
        return s === norm(`${first} ${last}`) || (!!mi && s === norm(`${first} ${mi} ${last}`));
    }

    const RULES = {
        AccountType: () => get('AccountType') ? '' : 'Choose Checking or Savings.',
        Nickname: () => get('Nickname').length > 30 ? 'Nickname can be at most 30 characters.' : '',
        CardDesign: () => DESIGNS[get('CardDesign')] ? '' : 'Choose a debit card design.',
        FirstName: () => nameRule(get('FirstName'), 'first name'),
        MiddleInitial: () => !get('MiddleInitial') || /^[A-Za-z]$/.test(get('MiddleInitial')) ? '' : 'Middle initial must be a single letter.',
        LastName: () => nameRule(get('LastName'), 'last name'),
        Dob: dobRule,
        Citizenship: () => !get('Citizenship') ? 'Select your citizenship.'
            : get('Citizenship') === 'Other' ? 'This demo can only open accounts for U.S. citizens and permanent residents.' : '',
        Phone: phoneRule,
        Street: () => required(get('Street'), 'Enter your street address.', 100),
        Apt: () => get('Apt').length > 50 ? 'Keep this under 50 characters.' : '',
        City: () => required(get('City'), 'Enter your city.', 60),
        State: () => get('State') ? '' : 'Select your state.',
        Zip: () => zipRule(get('Zip'), 'ZIP code'),
        LivedTwoYears: () => get('LivedTwoYears') ? '' : "Let us know whether you've lived here for 2+ years.",
        PrevStreet: () => required(get('PrevStreet'), 'Enter your previous street address.', 100),
        PrevCity: () => required(get('PrevCity'), 'Enter your previous city.', 60),
        PrevState: () => get('PrevState') ? '' : 'Select your previous state.',
        PrevZip: () => zipRule(get('PrevZip'), 'previous ZIP code'),
        EmploymentStatus: () => get('EmploymentStatus') ? '' : 'Select your employment status.',
        Employer: () => required(get('Employer'), "Enter your employer's name.", 80),
        Occupation: () => required(get('Occupation'), 'Enter your occupation.', 80),
        IncomeRange: () => get('IncomeRange') ? '' : 'Select your annual income range.',
        SourceOfFunds: () => checkedValues('SourceOfFunds').length ? '' : 'Choose at least one source of funds.',
        Identity: () => get('IdentityToken') ? ''
            : verifyFailed ? "We couldn't reach the verification service. Please try again." : 'Verify your identity to continue.',
        QuizStreet: () => !get('QuizStreet') ? 'Choose an answer.' : norm(get('QuizStreet')) === norm(get('Street')) ? '' : "That doesn't match our records. Please try again.",
        QuizZip: () => !get('QuizZip') ? 'Choose an answer.' : get('QuizZip') === get('Zip') ? '' : "That doesn't match our records. Please try again.",
        InitialDeposit: () => {
            const m = money(get('InitialDeposit'));
            if (!m.ok) return 'Enter a valid amount: numbers only, no negatives, at most 2 decimal places.';
            if (m.value > 1000000) return "Opening deposit can't be more than $1,000,000.";
            const src = fundingSource();
            if (src && m.value > src.balance) return `That's more than the ${fmtMoney(src.balance)} available in ${src.name}.`;
            return '';
        },
        FundingAccountId: () => '',
        ExternalBankName: () => get('ExternalBankName').length >= 2 ? '' : 'Enter the name of your other bank.',
        ExternalLast4: () => /^\d{4}$/.test(get('ExternalLast4')) ? '' : 'Enter the last 4 digits of that account.',
        AgreeAccountAgreement: () => get('AgreeAccountAgreement') ? '' : 'Please agree to the Account Agreement.',
        AgreePrivacy: () => get('AgreePrivacy') ? '' : 'Please acknowledge the Privacy Notice.',
        AgreeESign: () => get('AgreeESign') ? '' : 'Please agree to the E-Sign Consent.',
        ESignName: () => !get('ESignName') ? 'Type your full name to sign.'
            : signatureOk(get('ESignName')) ? '' : `Your signature must match your name: ${get('FirstName')} ${get('LastName')}.`,
    };
    const OPTIONAL = new Set(['Nickname', 'MiddleInitial', 'Apt', 'FundingAccountId']);

    // ---------- Steps and panes ----------
    const panes = {};
    $$('.step-pane').forEach(p => (panes[p.dataset.step] = p));

    // Field keys (data-field) that belong to a pane, skipping hidden conditional ones.
    const fieldsIn = pane => $$('[data-field]', pane).filter(isShown).map(f => f.dataset.field).filter(k => RULES[k]);
    const fieldError = key => RULES[key] ? RULES[key]() : '';
    const stepError = step => {
        for (const key of fieldsIn(panes[step])) { const e = fieldError(key); if (e) return e; }
        return '';
    };
    const stepValid = step => stepError(step) === '';
    const allValidBefore = step => { for (let s = 1; s < step; s++) if (!stepValid(s)) return false; return true; };

    // ---------- State ----------
    const touched = new Set();      // fields that should show their messages
    let current = 1;
    let profileMode = returning ? 'confirm' : 'edit';   // returning users see "Is this still correct?"

    // ---------- Field UI: green check / red message ----------
    function paintField(key) {
        const wrap = $(`[data-field="${key}"]`);
        if (!wrap) return;
        const msg = $(`[data-msg="${key}"]`, wrap);
        const err = fieldError(key);
        const show = touched.has(key);
        const hasValue = !OPTIONAL.has(key) || !!get(key);
        const valid = !err && show && hasValue;
        const invalid = !!err && show;

        wrap.classList.toggle('field-valid', valid);
        wrap.classList.toggle('field-invalid', invalid);
        $$('input.form-control, select.form-select', wrap).forEach(input => {
            input.classList.toggle('is-valid', valid);
            input.classList.toggle('is-invalid', invalid);
            input.setAttribute('aria-invalid', invalid ? 'true' : 'false');
        });
        if (msg) {
            msg.textContent = '';
            if (invalid) {
                const icon = document.createElement('i');
                icon.className = 'bi bi-exclamation-circle me-1';
                msg.append(icon, err);
            }
        }
    }
    const paintStep = step => $$('[data-field]', panes[step]).forEach(f => paintField(f.dataset.field));

    // ---------- Navigation ----------
    function paneForStep(step) {
        if ((step === 2 || step === 3) && profileMode === 'confirm' && panes.confirm) return panes.confirm;
        return panes[step];
    }

    function goTo(step, opts = {}) {
        step = Math.min(Math.max(step, 1), LAST);
        if (profileMode === 'confirm' && step === 3) step = 2;
        // Going forward requires every earlier step to be valid.
        if (step > current && !opts.force) {
            for (let s = 1; s < step; s++) if (!stepValid(s)) { step = s; break; }
        }
        const fromPane = paneForStep(current);
        const toPane = paneForStep(step);
        const dir = step >= current ? 'forward' : 'back';
        current = step;

        Object.values(panes).forEach(p => p.classList.remove('active', 'enter-forward', 'enter-back'));
        void toPane.offsetWidth;
        toPane.classList.add('active');
        if (fromPane !== toPane && !reducedMotion) toPane.classList.add('enter-' + dir);

        if (step === 5) restoreIdentityUi();
        if (step === 7) buildReview();
        if (toPane === panes.confirm) fillConfirm();
        render();
        save();

        if (!opts.initial) {
            const top = form.getBoundingClientRect().top + window.scrollY - 90;
            if (window.scrollY > top) window.scrollTo({ top, behavior: reducedMotion ? 'auto' : 'smooth' });
            const h = $('.step-title', toPane);
            if (h) { h.setAttribute('tabindex', '-1'); h.focus({ preventScroll: true }); }
        }
    }

    const backBtn = $('[data-back]');
    const nextBtn = $('[data-next]');
    const submitBtn = $('[data-submit]');

    function next() {
        const step = current;
        if (paneForStep(step) === panes.confirm) return confirmYes();
        fieldsIn(panes[step]).forEach(k => touched.add(k));
        paintStep(step);
        if (stepValid(step)) goTo(step + 1);
        else focusFirstError(panes[step]);
    }
    function focusFirstError(pane) {
        const bad = $('.field-invalid input:not([type=hidden]), .field-invalid select', pane);
        if (bad) bad.focus();
    }

    backBtn.addEventListener('click', () => {
        if (profileMode === 'confirm' && current === 4) return goTo(2);
        goTo(current - 1);
    });
    nextBtn.addEventListener('click', next);
    document.addEventListener('click', e => {
        const b = e.target.closest('[data-goto]');
        if (!b || !form.contains(b) && !b.closest('.step-nav')) return;
        e.preventDefault();
        e.stopPropagation();
        const target = parseInt(b.dataset.goto, 10);
        // From the review screen, "Edit" on About/Contact switches a returning user to edit mode.
        if ((target === 2 || target === 3) && profileMode === 'confirm' && b.classList.contains('review-edit')) profileMode = 'edit';
        goTo(target);
    });

    // Enter in a text box = Continue (instead of submitting the whole form early).
    form.addEventListener('keydown', e => {
        if (e.key !== 'Enter' || e.target.tagName !== 'INPUT' || ['checkbox', 'radio'].includes(e.target.type)) return;
        e.preventDefault();
        if (current < LAST) next();
    });

    // ---------- Returning customers: "Is this still correct?" ----------
    function fillConfirm() {
        const set = (k, v) => { const el = $(`[data-confirm="${k}"]`); if (el) el.textContent = v || '—'; };
        set('name', [get('FirstName'), get('MiddleInitial') && get('MiddleInitial').toUpperCase() + '.', get('LastName')].filter(Boolean).join(' '));
        set('dob', get('DobMonth') ? `${get('DobMonth').padStart(2, '0')}/${get('DobDay').padStart(2, '0')}/${get('DobYear')}` : '');
        set('citizenship', CITIZENSHIP[get('Citizenship')] || 'Not provided');
        set('email', get('EmailDisplay'));
        set('phone', get('Phone'));
        set('address', addressText('', 'Street', 'Apt', 'City', 'State', 'Zip'));
    }
    function confirmYes() {
        // If anything saved is missing or invalid (e.g. no citizenship yet), open the form at that step.
        if (stepValid(2) && stepValid(3)) return goTo(4);
        profileMode = 'edit';
        const bad = stepValid(2) ? 3 : 2;
        fieldsIn(panes[bad]).forEach(k => touched.add(k));
        goTo(bad, { force: true });
        paintStep(bad);
    }
    const yesBtn = $('[data-confirm-yes]');
    if (yesBtn) yesBtn.addEventListener('click', confirmYes);
    const editBtn = $('[data-confirm-edit]');
    if (editBtn) editBtn.addEventListener('click', () => { profileMode = 'edit'; goTo(2, { force: true }); });

    // ---------- Input helpers: DOB auto-advance, phone formatting, ZIP lookup, counters ----------
    const dobM = byId('DobMonth'), dobD = byId('DobDay'), dobY = byId('DobYear');
    function dobInput(el, nextEl, maxFirstDigit) {
        el.addEventListener('input', () => {
            el.value = digits(el.value).slice(0, el.maxLength);
            // "4" in the month box can only mean April, so pad it and move on.
            if (el.value.length === 1 && +el.value > maxFirstDigit) el.value = '0' + el.value;
            if (el.value.length === el.maxLength && nextEl) nextEl.focus();
        });
    }
    function dobBackspace(el, prevEl) {
        el.addEventListener('keydown', e => {
            if (e.key === 'Backspace' && el.value === '' && prevEl) { e.preventDefault(); prevEl.focus(); prevEl.value = prevEl.value.slice(0, -1); }
        });
    }
    if (dobM) {
        dobInput(dobM, dobD, 1); dobInput(dobD, dobY, 3); dobInput(dobY, null, 9);
        dobBackspace(dobD, dobM); dobBackspace(dobY, dobD);
    }

    const phone = byId('Phone');
    function formatPhone(value) {
        let d = digits(value);
        if (d.length === 11 && d[0] === '1') d = d.slice(1);
        d = d.slice(0, 10);
        if (d.length > 6) return `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
        if (d.length > 3) return `(${d.slice(0, 3)}) ${d.slice(3)}`;
        return d.length ? `(${d}` : '';
    }
    if (phone) {
        phone.addEventListener('input', e => {
            // Don't fight the user while they delete characters; reformat on the next keystroke / blur.
            if (e.inputType && e.inputType.startsWith('delete')) return;
            phone.value = formatPhone(phone.value);
        });
        phone.addEventListener('blur', () => { phone.value = formatPhone(phone.value); });
    }

    $$('[data-zip-for]').forEach(zipEl => {
        const [cityId, stateId] = zipEl.dataset.zipFor.split(',');
        const hint = $(`[data-zip-hint="${zipEl.id}"]`);
        zipEl.addEventListener('input', () => {
            zipEl.value = digits(zipEl.value).slice(0, 5);
            if (!hint) return;
            hint.textContent = '';
            hint.className = 'zip-hint';
            if (zipEl.value.length !== 5) return;
            const hit = ZIPS[zipEl.value];
            if (hit) {
                byId(cityId).value = hit[0];
                byId(stateId).value = hit[1];
                touched.add(cityId); touched.add(stateId);
                hint.classList.add('found');
                hint.textContent = `Found: ${hit[0]}, ${hit[1]}`;
            } else {
                hint.textContent = "We couldn't look up that ZIP. Please enter your city and state.";
            }
        });
    });

    ['ExternalLast4'].forEach(id => { const el = byId(id); if (el) el.addEventListener('input', () => { el.value = digits(el.value).slice(0, 4); }); });
    const mi = byId('MiddleInitial');
    if (mi) mi.addEventListener('input', () => { mi.value = mi.value.replace(/[^A-Za-z]/g, '').slice(0, 1).toUpperCase(); });

    // ---------- Step 5: simulated identity verification ----------
    const verifyBtn = $('[data-verify-btn]');
    const verifyBox = $('[data-id-verify]');
    const quiz = $('[data-quiz]');
    const tokenInput = byId('IdentityToken');
    const last4Input = byId('VerifiedLast4');
    let verifying = false;
    let verifyFailed = false;

    function showVerified() {
        verifyBox.classList.remove('scanning');
        verifyBox.classList.add('verified');
        $$('[data-check]', verifyBox).forEach(li => { li.classList.remove('active'); li.classList.add('done'); });
        $('[data-ssn-last4]', verifyBox).textContent = last4Input.value || 'XXXX';
        verifyBtn.hidden = true;
        $('[data-verified-badge]').hidden = false;
        quiz.hidden = false;
        buildQuiz();
    }
    function restoreIdentityUi() {
        if (tokenInput.value && !verifying) showVerified();
    }

    async function verifyIdentity() {
        if (verifying) return;
        verifying = true;
        verifyBtn.disabled = true;
        verifyBox.classList.remove('verified', 'failed');
        verifyBox.classList.add('scanning');
        const items = $$('[data-check]', verifyBox);
        items.forEach(li => li.classList.remove('active', 'done'));

        // Ask the server for the simulated check while the animation plays.
        const antiforgery = $('input[name="__RequestVerificationToken"]').value;
        const request = fetch(form.dataset.verifyUrl, {
            method: 'POST',
            headers: { 'RequestVerificationToken': antiforgery },
            credentials: 'same-origin'
        }).then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); });

        try {
            for (let i = 0; i < items.length; i++) {
                items[i].classList.add('active');
                await wait(reducedMotion ? 150 : (i === items.length - 1 ? 700 : 1100));
                if (i === 2) {
                    const data = await request;                // wait for the server's answer
                    tokenInput.value = data.token;
                    last4Input.value = data.last4;
                    $('[data-ssn-last4]', verifyBox).textContent = data.last4;
                    await wait(reducedMotion ? 0 : 350);
                }
                items[i].classList.remove('active');
                items[i].classList.add('done');
            }
            verifying = false;
            verifyFailed = false;
            touched.add('Identity');
            showVerified();
        } catch (err) {
            verifying = false;
            verifyFailed = true;              // RULES.Identity shows the retry message
            touched.add('Identity');
            verifyBox.classList.remove('scanning');
            verifyBox.classList.add('failed');
            items.forEach(li => li.classList.remove('active', 'done'));
            verifyBtn.disabled = false;
        }
        render();
        save();
    }
    if (verifyBtn) verifyBtn.addEventListener('click', verifyIdentity);

    // Seeded shuffle so the quiz options stay in the same order across refreshes.
    function seededShuffle(list, seedText) {
        let h = 2166136261;
        for (const c of seedText) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
        const rand = () => ((h = Math.imul(h ^ (h >>> 15), 2246822507) ^ Math.imul(h ^ (h >>> 13), 3266489909)) >>> 0) / 4294967296;
        const a = list.slice();
        for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
        return a;
    }
    let quizBuiltFor = '';
    function buildQuiz() {
        const street = get('Street'), zip = get('Zip');
        const key = street + '|' + zip + '|' + tokenInput.value;
        if (key === quizBuiltFor) return;
        quizBuiltFor = key;

        const seed = tokenInput.value || street;
        const streets = seededShuffle(DECOY_STREETS.filter(s => norm(s) !== norm(street)), seed + 's').slice(0, 3);
        const zips = seededShuffle(Object.keys(ZIPS).filter(z => z !== zip), seed + 'z').slice(0, 3);
        renderQuiz('QuizStreet', seededShuffle([street, ...streets], seed + 'S'));
        renderQuiz('QuizZip', seededShuffle([zip, ...zips], seed + 'Z'));
        // Add "None of these" so the right answer isn't the only plausible choice.
    }
    function renderQuiz(name, options) {
        const box = $(`[data-quiz-options="${name}"]`);
        const previous = get(name) || box.dataset.initial || '';
        box.textContent = '';
        options.forEach(opt => {
            const label = document.createElement('label');
            label.className = 'pill-option quiz-option';
            const input = document.createElement('input');
            input.type = 'radio';
            input.name = name;
            input.value = opt;
            input.checked = opt === previous;
            const span = document.createElement('span');
            span.textContent = opt;
            label.append(input, span);
            box.append(label);
        });
        box.dataset.initial = '';
    }

    // ---------- Step 6: deposit chips + source hint ----------
    const depositInput = byId('InitialDeposit');
    $$('[data-amount]').forEach(chip => chip.addEventListener('click', () => {
        if (chip.dataset.amount === 'custom') {
            depositInput.value = '';
            depositInput.focus();
        } else {
            depositInput.value = chip.dataset.amount;
        }
        touched.add('InitialDeposit');
        render(); save();
    }));

    // ---------- Step 7: review sections (built with textContent — never innerHTML with user input) ----------
    function addressText(prefix, s, a, c, st, z) {
        const street = [get(prefix + s), get(prefix + a)].filter(Boolean).join(', ');
        const city = get(prefix + c);
        return street ? `${street}, ${city}, ${get(prefix + st)} ${get(prefix + z)}` : '';
    }
    function buildReview() {
        const root = $('[data-review-root]');
        if (!root) return;
        const type = get('AccountType');
        const src = fundingSource();
        const dep = depositValue();
        const sections = [
            [1, 'Account', [
                ['Account type', type === 'Checking' ? 'Alto Checking' : type === 'Savings' ? 'Summit Savings' : '—'],
                ['Nickname', get('Nickname') || 'None'],
                ...(type === 'Checking' ? [['Debit card design', DESIGNS[get('CardDesign')]]] : []),
            ]],
            [2, 'About you', [
                ['Name', [get('FirstName'), get('MiddleInitial') && get('MiddleInitial') + '.', get('LastName')].filter(Boolean).join(' ')],
                ['Date of birth', `${get('DobMonth').padStart(2, '0')}/${get('DobDay').padStart(2, '0')}/${get('DobYear')}`],
                ['Citizenship', CITIZENSHIP[get('Citizenship')] || '—'],
            ]],
            [3, 'Contact & address', [
                ['Email', get('EmailDisplay')],
                ['Mobile phone', get('Phone')],
                ['Home address', addressText('', 'Street', 'Apt', 'City', 'State', 'Zip')],
                ['At this address 2+ years', get('LivedTwoYears') === 'true' ? 'Yes' : 'No'],
                ...(get('LivedTwoYears') === 'false' ? [['Previous address', addressText('Prev', 'Street', '', 'City', 'State', 'Zip')]] : []),
            ]],
            [4, 'Employment & income', [
                ['Employment', get('EmploymentStatus')],
                ...(get('EmploymentStatus') === 'Employed' ? [['Employer', `${get('Occupation')} at ${get('Employer')}`]] : []),
                ['Annual income', get('IncomeRange')],
                ['Source of funds', checkedValues('SourceOfFunds').join(', ')],
            ]],
            [5, 'Identity', [
                ['Status', get('IdentityToken') ? `Verified (SSN on file ***-**-${last4Input.value})` : 'Not verified'],
                ['Security check', get('QuizStreet') && get('QuizZip') ? 'Completed' : 'Not completed'],
            ]],
            [6, 'Funding', [
                ['Opening deposit', dep > 0 ? fmtMoney(dep) : '$0.00 (fund later)'],
                ...(dep > 0 ? [['From', src ? src.name : `${get('ExternalBankName')} ••${get('ExternalLast4')} (simulated)`]] : []),
            ]],
        ];

        root.textContent = '';
        sections.forEach(([step, title, rows]) => {
            const details = document.createElement('details');
            details.className = 'review-section';
            if (!stepValid(step)) details.classList.add('has-issue');
            details.open = window.innerWidth >= 768;

            const summary = document.createElement('summary');
            const label = document.createElement('span');
            label.className = 'review-section-title';
            const icon = document.createElement('i');
            icon.className = stepValid(step) ? 'bi bi-check-circle-fill text-success-alto me-2' : 'bi bi-exclamation-circle text-danger-alto me-2';
            label.append(icon, title);
            const edit = document.createElement('button');
            edit.type = 'button';
            edit.className = 'review-edit';
            edit.dataset.goto = step;
            edit.textContent = 'Edit';
            summary.append(label, edit);

            const dl = document.createElement('dl');
            dl.className = 'detail-list mb-0';
            rows.forEach(([k, v]) => {
                const row = document.createElement('div');
                const dt = document.createElement('dt'); dt.textContent = k;
                const dd = document.createElement('dd'); dd.textContent = v || '—';
                row.append(dt, dd);
                dl.append(row);
            });
            details.append(summary, dl);
            root.append(details);
        });
    }

    // Disclosure modals: "I agree" ticks the matching checkbox.
    document.querySelectorAll('[data-agree-target]').forEach(btn => btn.addEventListener('click', () => {
        const box = byId(btn.dataset.agreeTarget);
        box.checked = true;
        touched.add(btn.dataset.agreeTarget);
        render(); save();
    }));

    // ---------- Live updates ----------
    form.addEventListener('input', e => {
        const field = e.target.closest('[data-field]');
        if (field) {
            const key = field.dataset.field;
            // Show messages while typing once the field is valid or was already visited.
            if (!fieldError(key) || touched.has(key) || e.target.type === 'radio' || e.target.type === 'checkbox') touched.add(key);
        }
        render();
        scheduleSave();
    });
    form.addEventListener('change', e => {
        const field = e.target.closest('[data-field]');
        if (field) touched.add(field.dataset.field);
        render();
        scheduleSave();
    });
    form.addEventListener('focusout', e => {
        const field = e.target.closest('[data-field]');
        // For the 3-box date of birth, wait until focus leaves the whole group.
        if (field && !(field.dataset.field === 'Dob' && field.contains(e.relatedTarget))) {
            touched.add(field.dataset.field);
            paintField(field.dataset.field);
        }
    });

    // ---------- Submit ----------
    form.addEventListener('submit', e => {
        let firstBad = 0;
        for (let s = 1; s <= LAST; s++) if (!stepValid(s)) { firstBad = s; break; }
        if (current !== LAST || firstBad) {
            e.preventDefault();
            if (firstBad) {
                if (firstBad === 2 || firstBad === 3) profileMode = 'edit';
                goTo(firstBad, { force: true });
                fieldsIn(panes[firstBad]).forEach(k => touched.add(k));
                paintStep(firstBad);
                focusFirstError(panes[firstBad]);
            }
            return;
        }
        save();
        submitBtn.disabled = true;
        $('[data-spinner]').classList.remove('d-none');
        $('[data-submit-label]').textContent = 'Submitting…';
    });

    // ---------- Render everything from the current state ----------
    function render() {
        // Conditional sections
        $$('[data-show-when]').forEach(el => { el.hidden = !isShown(el); });

        // Buttons
        const onConfirm = paneForStep(current) === panes.confirm;
        backBtn.style.visibility = current === 1 ? 'hidden' : 'visible';
        nextBtn.classList.toggle('d-none', current === LAST || onConfirm);
        submitBtn.classList.toggle('d-none', current !== LAST);
        nextBtn.disabled = !stepValid(current);
        if (!submitBtn.querySelector('[data-spinner]:not(.d-none)')) {
            submitBtn.disabled = current === LAST && !(allValidBefore(LAST) && stepValid(LAST));
        }

        // Progress tracker (sidebar + mobile bar)
        $$('[data-nav-step]', document).forEach(li => {
            const n = parseInt(li.dataset.navStep, 10);
            const done = n < current && stepValid(n);
            const state = n === current ? 'current' : done ? 'done' : (n < current ? 'issue' : 'todo');
            li.className = 'step-nav-item ' + state;
            li.querySelector('[data-nav-status]').textContent =
                state === 'done' ? 'Completed' : state === 'current' ? 'In progress' : state === 'issue' ? 'Needs attention' : 'Not started';
            const btn = li.querySelector('button');
            btn.disabled = n === current || !allValidBefore(n);
            btn.setAttribute('aria-current', n === current ? 'step' : 'false');
        });
        const num = $('[data-nav-num]', document); if (num) num.textContent = current;
        const title = $('[data-nav-title]', document); if (title) title.textContent = TITLES[current - 1];
        const bar = $('[data-nav-bar]', document); if (bar) bar.style.width = (current / LAST * 100) + '%';

        // Choice styling
        $$('.choice-card, .swatch, .pill-option').forEach(l => {
            const input = l.querySelector('input');
            if (input) l.classList.toggle('selected', input.checked);
        });
        $$('[data-amount]').forEach(c => c.classList.toggle('active', c.dataset.amount !== 'custom' && money(get('InitialDeposit')).value === +c.dataset.amount));

        // Counters, hints, previews
        const count = $('[data-count-for="Nickname"]'); if (count) count.textContent = get('Nickname').length;
        const src = fundingSource();
        const hint = $('[data-source-hint]');
        if (hint) hint.textContent = src ? `Available: ${fmtMoney(src.balance)}. Moves instantly from ${src.name}.` : 'Simulated: money arrives from your other bank.';
        const signHint = $('[data-sign-hint]'); if (signHint) signHint.textContent = `${get('FirstName')} ${get('LastName')}`;
        const preview = $('[data-sign-preview]');
        if (preview) {
            const ok = !RULES.ESignName();
            preview.hidden = !ok;
            if (ok) {
                $('[data-sign-name]').textContent = get('ESignName');
                $('[data-sign-date]').textContent = new Date().toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' });
            }
        }
        if (current === 5 && tokenInput.value) buildQuiz();
        // (The review on step 7 is rebuilt when you arrive there, not on every keystroke,
        //  so sections you open or close stay that way while you sign.)

        // Field messages for the visible step
        const pane = paneForStep(current);
        if (pane !== panes.confirm) paintStep(current);
    }

    // ---------- sessionStorage auto-save (refresh-safe) ----------
    // Everything is wrapped in try/catch: storage can be full, disabled, or blocked (private mode).
    let saveTimer = null;
    const scheduleSave = () => { clearTimeout(saveTimer); saveTimer = setTimeout(save, 250); };
    function save() {
        try {
            const data = { step: current, profileMode, fields: {}, last4: last4Input.value };
            for (const el of form.elements) {
                if (!el.name || el.name === '__RequestVerificationToken') continue;
                if (el.type === 'hidden' && el.name !== 'IdentityToken') continue;   // skip checkbox "false" helpers
                if (el.type === 'radio') { if (el.checked) data.fields[el.name] = el.value; }
                else if (el.type === 'checkbox') {
                    if (el.name === 'SourceOfFunds') { (data.fields.SourceOfFunds ||= []); if (el.checked) data.fields.SourceOfFunds.push(el.value); }
                    else data.fields[el.name] = el.checked;
                }
                else data.fields[el.name] = el.value;
            }
            sessionStorage.setItem(storageKey, JSON.stringify(data));
        } catch (e) { /* storage unavailable — the form still works */ }
    }
    function restore() {
        let data = null;
        try { data = JSON.parse(sessionStorage.getItem(storageKey) || 'null'); } catch (e) { data = null; }
        if (!data || !data.fields) return null;
        for (const [name, value] of Object.entries(data.fields)) {
            if (name === 'SourceOfFunds') {
                $$('input[name="SourceOfFunds"]').forEach(c => { c.checked = value.includes(c.value); });
                continue;
            }
            const radios = $$(`input[type=radio][name="${name}"]`);
            if (radios.length) { radios.forEach(r => { r.checked = r.value === value; }); continue; }
            const el = byId(name);
            if (!el) {
                // Quiz radios are created later; remember the answer for when they're built.
                const box = $(`[data-quiz-options="${name}"]`);
                if (box) box.dataset.initial = value;
                continue;
            }
            if (el.type === 'checkbox') el.checked = !!value; else el.value = value;
        }
        if (data.last4) last4Input.value = data.last4;
        return data;
    }
    $$('[data-save-exit]', document).forEach(a => a.addEventListener('click', save));

    // ---------- Start ----------
    form.classList.add('js-ready');
    let start = parseInt(form.dataset.startStep, 10) || 1;

    if (hasServerErrors) {
        // The server's answers win; show every field's message on the failing step.
        if (start === 2 || start === 3) profileMode = 'edit';
        Object.keys(RULES).forEach(k => { if ($(`[data-msg="${k}"] .field-validation-error`)) touched.add(k); });
        for (let s = 1; s <= LAST; s++) fieldsIn(panes[s]).forEach(k => { if (s === start) touched.add(k); });
        // Quiz answers posted back to the server: restore them into the rebuilt options.
    } else {
        const saved = restore();
        if (saved) {
            profileMode = returning ? (saved.profileMode || 'confirm') : 'edit';
            start = saved.step || 1;
        }
    }

    // Unused previous-address fields shouldn't block anything; make sure state is drawn first.
    current = 1;
    $$('[data-show-when]').forEach(el => { el.hidden = !isShown(el); });
    if (tokenInput.value) restoreIdentityUi();
    goTo(start, { initial: true });
})();
