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
| **Total** | | **943** |

## Phase 2

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
