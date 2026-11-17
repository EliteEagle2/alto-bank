# Changes

What changed in each phase of Alto Bank, newest first. Each feature was built with its own migration, demo data and test
suite, then all earlier suites were re-run, it was committed, a read-only review checked it against the project rules,
and the review's findings were fixed and re-tested before the next feature started.

## Test counts

Every suite runs against a separate test server on a freshly reset scratch database (`tests/run_all.sh`).

| Suite | What it covers | Checks |
|---|---|---|
| part1 | customer bank vs admin portal, roles, access denied | 46 |
| apply | the 7-step account application | 51 |
| race | 8 concurrent withdrawals: exactly one succeeds | 7 |
| part2 | admin portal: customers, accounts, transactions, applications, settings, audit | 118 |
| review2 | admin confirm modals carry their own anti-forgery token | 26 |
| part3 | account details, activity API, statements, debit card | 88 |
| review3 | card backfill can't issue a second card | 7 |
| p2p | Send & Request, splits, expiry, alerts | 121 |
| billpay | payees, bill pay, scheduled transfers, the scheduler | 116 |
| staff | Owner role, staff page, admins can't act on their own records | 56 |
| profile | personal info, security, alert settings, preferences | 59 |
| insights | categories, Insights page + API, budgets, budget alerts | 127 |
| checkdeposit | uploads, limits, duplicates, races, pending balance, review queue | 121 |
| disputes | eligibility, form rules, provisional credit, partial / P2P refunds, clawback overdraft, races | 134 |
| creditcard | simulated decisions, limit + simultaneous purchases, statement cycle, grace period, interest, late fee + waive, autopay, cash back, card disputes, admin actions | 210 |
| **Total** | | **1,287** |

## Phase 2

### Feature 6: Alto Peak credit card (simulated credit)

- **Applying**: Alto Peak is a third choice in the 7-step application. For a card, step 6 asks for the monthly housing
  payment and the Cardmember Agreement instead of an opening deposit. The decision is **simulated** (`CreditDecisionRules`):
  income range, housing payment and Alto history, with no credit check and no SSN. Approved, sent for review or denied,
  with a limit of $500-$5,000. "Demo decision, no credit check" appears on every page that shows a decision (product page,
  application, decision and status pages, Profile, admin application and card pages). One card per customer.
- **The card** (`/accounts/{id}`): what you owe, available credit, the statement balance, the minimum due and its date, a
  credit-used bar, a past-due banner, and a demo "Make a purchase" (there's no real store). Tabs: Activity (rows show
  "Owed"), Statements (printable), Rewards & autopay, Card (the same card controls as the debit card).
- **Statements** close monthly once the close day is over and cover the activity dated in that period. The minimum is
  the larger of $25 or 2% (or the whole balance if smaller), due 25 days later. **No interest** when the last statement
  was paid in full by its due date; otherwise daily-balance interest at the purchase APR. A late fee if the minimum isn't
  paid by the due date (refunds and credits count toward it). A reminder 5 days before the due date.
- **Pay card** (`/credit-card/{id}/pay`): minimum, statement balance, current balance or another amount, from checking or
  savings; each option says what it means (avoid a late fee, avoid interest). Paying more than you owe is refused.
- **Autopay** (minimum or statement balance) on the due date, with an alert if it can't pay. It turns off, with an alert,
  if the account it pays from is closed.
- **Cash back**: 1.5% on purchases, redeemed (at $10 or more) to checking/savings or as a statement credit. A refunded
  purchase loses its cash back.
- **Disputes** work on card purchases; the refund goes back on the card.
- **Admin Credit Cards** (Money section): every card, a detail page with statements, Change limit, Waive late fee, Refund a
  credit balance, Freeze, and Close (only at $0). Card terms (APR, late fee, cash back, minimum) are on Settings. The
  generic account actions (adjust, close, reopen) send staff to the card page instead.
- **Kept out of deposit flows**: a card can't be used for transfers, deposits, withdrawals, P2P, bill pay, scheduled
  payments, check deposits or as the default account. The dashboard's total counts checking and savings only (net worth
  with cards and loans comes with Feature 7).
- **Seed**: three months of history replayed through the real code. Jordan pays in full with autopay (no interest),
  Maya pays minimums (interest and one late fee), Samuel has an application waiting for review.
- **Review fixes**: statements now assign activity by bank date (correct catch-up after downtime); refunds, waivers and
  statement credits count toward the minimum and the amount due is capped at what's owed; a late fee can't be credited
  twice (waive marks it reversed, Reverse refuses fees); autopay pays every due statement, oldest first; cash back is
  taken back when a provisional credit becomes final; a statement credit can't leave money on the card; cards refused
  for scheduled payments; the idle scheduler no longer takes the database write lock.
- **Docs**: `docs/REQUIREMENTS.md` had an older copy of itself spliced in since Feature 4 (a dollar sign followed by a
  backtick in a text replacement); repaired.

### Feature 5: Dispute a transaction

- **Dispute this transaction** in the expanded activity row, shown only when it's eligible (decision 6): a withdrawal,
  bill payment or P2P send from the last 60 days (bank time), posted, not reversed or refunded, and not already
  disputed. Transfers between your own accounts can't be disputed. The server re-checks everything.
- **The form**: what went wrong (with the expected amount for "Wrong amount", which must be less than the charge, or the
  original charge for "Duplicate"), a 20-1,000 character description, an optional JPG/PNG receipt (the shared upload
  store), a review step, and for "I didn't authorize it" a one-click debit card freeze.
- **Disputes page** (an Accounts tab) and a **timeline** per dispute: Submitted → Under review → Refunded / Denied, with
  dates, the decision note and a link to the refund. Activity shows Disputed / Refunded / Dispute denied badges.
- **Provisional credit** (Settings, off by default, captured on each dispute when it's filed): the money is credited
  right away; approval makes it final (no second refund); denial takes it back, even into an overdraft (decision 5),
  which is audited, flagged in the admin work queue and sent to the customer.
- **Admin Disputes queue** (Money; opens on Submitted, oldest first; "Disputes" chip in the work queue): the charge, the
  money, the attachment, the customer's activity with that merchant and their other disputes. Start review, Approve and
  Deny run through `IAdminService` with a confirmation, an audit row and (for Deny) a reason the customer sees.
- **Refunds use the reversal system**: the whole charge, or only the difference for "Wrong amount"; a P2P payment is
  refunded to the sender only and the recipient keeps it. A dispute can be decided once, never by the person it's about,
  and the admin Reverse refuses a charge that's disputed or partly refunded, so nothing is refunded twice.
- **One dispute per transaction**, enforced by a unique index (simultaneous submits create only one).
- **Insights** nets partial refunds and provisional credits against the original charge's category.
- **API**: `POST /api/disputes` (multipart, needs the anti-forgery token), `GET /api/disputes`, `GET /api/disputes/{id}`.
- **Demo data**: Jordan has one refunded dispute and one under review; Maya has one denied with a provisional credit and
  its clawback.
- Migration `Disputes`.
- Review fixes: a stale dispute form (second tab, charge reversed meanwhile) goes to the existing dispute or the account
  instead of looping; closed accounts never offer "Dispute this transaction"; a dispute refund reads "Dispute refund: …"
  with no counterparty (it comes from Alto, not the person paid); "Partly refunded" badge for wrong-amount refunds; the
  timeline names the refund transaction; a denied dispute whose provisional credit was taken back no longer blocks an
  admin Reverse; the attachment is saved only after the dispute row exists; a failed card freeze can't fail the request.

### Feature 4: Mobile Check Deposit (simulated)

- **Deposit a check** (`/checks`, a Move money tab; the Deposit page offers it too): amount, account (active checking or
  savings), front and back photos with drag and drop on a computer or the camera on a phone, previews with Retake / Replace,
  an "Endorse the back" checklist, and a review step with both thumbnails before anything is submitted.
- **Shared upload store** (`IFileStore`, reused by disputes): files go to `App_Data/uploads` (outside `wwwroot`,
  git-ignored) under random names; JPG/PNG only, decided by the file's signature, not its name or Content-Type; 5 MB each;
  served only to the owner or staff, never cached; never deleted.
- **Limits** in Settings (admins can change them): $5,000 per check and $10,000 per customer per bank day; pending and
  approved checks count, rejected ones don't.
- **Duplicate guard**: a SHA-256 of the front image; the same image can't be deposited twice (by anyone) unless the
  earlier one was rejected.
- **Pending isn't money yet**: the account header shows Current, Available and a separate "Pending +$X" line, and Activity
  lists "Pending - mobile deposit" first, outside the running balance.
- **Status page** per check: Submitted → In review → Deposited / Not accepted, with dates, both photos and the reason.
- **Admin Check Deposits queue** (Money group; opens on Pending, oldest first; a "Checks" chip in the overview work queue):
  both images large with click-to-zoom, duplicate-image warnings, the customer's deposit history. Approve posts the money
  through `IBankService` inside the admin transaction; Reject needs a reason the customer sees. Both are audited, can only
  happen once, and are refused for frozen/closed accounts and for an admin's own check.
- **Alerts**: check received, deposited (with the amount), not accepted (with the reason).
- **API**: `POST /api/checkdeposits` (multipart, needs the anti-forgery token), `GET /api/checkdeposits`, `GET /api/checkdeposits/{id}`.
- **Demo data**: placeholder check images drawn in code and marked "SAMPLE - NOT A REAL CHECK". Jordan has one approved and
  one pending; Maya has one rejected ("the back isn't endorsed"); Samuel has none.
- Migration `MobileCheckDeposit` (the new limit columns default to the real limits for the existing settings row).
- Review fixes: the daily limit and the duplicate guard are checked inside the same transaction as the insert, so two
  submits at the same moment can't both get through (tested with parallel requests); approval refuses a check image that
  was already deposited; the API needs `endorsed=true` like the website's checklist; without JavaScript, photo problems
  are listed together with checklist problems; focus moves to the review step; files saved before a failed insert are
  logged.

### Simpler customer navigation

- The sidebar went from 14 links in 5 sections to 7 places: Home, Accounts, Move money, Pay & request, Insights, then
  Alerts and Profile. Pages that share an entry show tabs at the top (Transfer | Deposit | Withdraw | Deposit a check,
  Send & request | Bill Pay | Upcoming, Spending | Budgets). The phone bar matches.

### Feature 3: Spending Insights and Budgets

- Every transaction gets a category from keyword rules on the merchant name (existing ones backfilled); transfers and P2P
  are Transfers, bill payments Bills & Utilities, reversals keep the category of what they undo. Customers can change a
  category from the expanded transaction row or the Insights list ("Let Alto pick again" undoes it).
- Insights page with a month picker: spending compared with the same days last month, money in/out/net, plain-language
  callouts, a donut by category whose legend is also its table, month-over-month bars, top 5 merchants, a category
  drill-down and a "How Insights works" panel. Charts read from `/api/insights`.
- Budgets: a monthly limit per category, green / amber at 75% / red at 100% (always with words and an icon too), one
  alert at each threshold per month, and a "Budgets to watch" dashboard card.
- Review fixes: alerts after a payment can never fail the payment; whole-month comparisons for past months; exact budget
  states ($99.96 of $100 isn't "over"); budget changes saved with their audit row in one transaction.

### Profile, staff access and the admin console

- Profile & settings: personal info, password and sign-ins, which alerts light up the bell, low-balance and large-purchase
  alerts, a default account, privacy mode and color themes.
- Staff who bank here (the admin has his own accounts) can't use admin tools on their own records. An Owner role, set only
  in configuration (`.env`), manages admins on `/admin/staff`.
- The admin console got its own layout: customer search, grouped navigation, a status strip and a single work queue.
- Ridgeline visual redesign of the whole site.

### Feature 2: Bill Pay and scheduled transfers

- Payees, pay now / on a date / weekly / monthly, scheduled transfers between your own accounts, and an Upcoming calendar
  with edit, skip-next and cancel.
- A `BackgroundService` scheduler pays what's due. A unique (item, date) index means a date can never be paid twice, even
  after a restart; failures are recorded once with an alert; dates missed while the app was off are recorded, not all paid
  at once. Admin Scheduled Payments page with an audited "Run scheduler now".

### Feature 1: Send & Request and the Alerts center

- Send money by email or phone with a recipient preview, request money, split a bill, Pay / Decline, 14-day expiry.
- Alerts center (bell, `/alerts`, mark read). Bank time zone (America/New_York) for "today" everywhere. SQLite WAL mode and
  a busy timeout. Admin P2P Payments page.

### Step 0

- Demo accounts open on different days across the last 11 weeks; the test suites moved into the repo (`tests/`).

## Phase 1

- **Part 3**: Account Details (activity with filters, CSV, statements, direct deposit form, virtual debit card).
- **Part 2**: the admin portal (`IAdminService`: one transaction, an audit row and a required reason per change; nothing
  hard-deleted), settings, monthly interest, reversals, applications review.
- **Part 1**: separate customer bank and admin area with roles; balance concurrency check so racing requests can't overdraw.
