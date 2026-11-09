# Alto Bank: course requirements, features, and API reference

All paths are relative to `AltoBank/`. Locations name the file and the class/method, so they stay correct as code moves.

## Two sides of the app

| Side | URL | Who | Code |
|---|---|---|---|
| Customer bank | `/dashboard`, `/accounts`, `/accounts/{id}`, `/transactions/...`, `/profile` | `Customer` role | `Controllers/`, `Views/`, layout `Views/Shared/_BankingLayout.cshtml` |
| Admin portal | `/admin/...` | `Admin` role | `Areas/Admin/` (own controllers, views, `_AdminLayout`, `_AdminSidebar`, `_ViewStart`, `_ViewImports`) |
| Public site | `/`, `/products/...`, `/company/...` | everyone | `Controllers/HomeController.cs` and friends, layout `_Layout.cshtml` |

Customers who open `/admin` (or admins who open customer pages) get a styled **Access Denied** page (`Views/Auth/AccessDenied.cshtml`).

## Requirements checklist (term → where it's used)

| Requirement | Where it's used |
|---|---|
| **Models** | `Models/Account.cs`, `Transaction.cs`, `ApplicationUser.cs`, `AccountApplication.cs`, `Card.cs`, `AuditLog.cs`, `CustomerNote.cs`, `BankSetting.cs` |
| **Views / Controllers / Actions** | `Views/**`, `Areas/Admin/Views/**`; `Controllers/*.cs`, `Areas/Admin/Controllers/*.cs` (e.g. `TransactionsController.Deposit` GET + POST) |
| **Conventional + area routing** | `Program.cs`: `MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/...")` and `"default"` |
| **Attribute routes** | `AccountsController.Details` → `accounts/{id:int}`, `.Statement` → `accounts/{id}/statements/{year}/{month}`, `.ActivityCsv`; `CardsController` (`accounts/{id}/cards/{cardId}/freeze` …); `TransactionsController.History` → `accounts/{id}/history`; all API controllers |
| **Migrations** (history kept) | `Migrations/`: `InitialCreate` → `AccountOpeningDetails` → `AccountApplication` → `AccountBalanceConcurrency` → `AdminPortal` (converts old "Declined" applications to "Denied") |
| **Seeder** (3 customers + admin, accounts opened across the last 11 weeks with daily activity) | `Data/SeedData.cs` (`InitializeAsync`, `GenerateHistory`); command: `dotnet run -- --seed` / `--reset-db` (`Program.cs`) |
| **Master layout + partials** | `_Layout`, `_BankingLayout`, `_ApplyLayout`, `Areas/Admin/Views/Shared/_AdminLayout`; partials: `_AccountCard` (dashboard, accounts, admin view-as), `_TransactionTable`, `_StatusAlert` (all layouts), `_ApplicationStepNav` (apply + decision), `_AccountHeader`, `_TransactionRow`, `_TransactionFilters`, `_CopyField` (account info + direct deposit), `_CardArt` (home, products, apply, account header, card tab), `_ConfirmModal`, `_KpiTile`, `_AdminSidebar`, `_Pager`, `_StatusPill` |
| **Razor C# in views** | loops/grouping in `Views/Accounts/Details.cshtml` (groups activity by day), `Areas/Admin/Views/Accounts/Details.cshtml` (builds confirm modals), formatting helpers (`ToString("C")`, dates) |
| **Tag helpers** | `asp-*` everywhere; custom `<money>` tag helper `TagHelpers/MoneyTagHelper.cs` (green/red amounts), registered in both `_ViewImports` |
| **Validation attributes** | `[Required]`, `[Range]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`, `[Compare]` in `ViewModels/*`, `Areas/Admin/ViewModels/*`, `Dtos/*`; custom **`[MinimumAge(18)]`** in `Validation/MinimumAgeAttribute.cs` (used on the application and admin "create customer") |
| **TempData + Post/Redirect/Get** | Every POST sets `TempData["Success"/"Error"]` then redirects (e.g. `TransactionsController.Deposit`, `CardsController.Run`, `AdminControllerBase.Flash`); shown by `_StatusAlert` |
| **ViewData / ViewBag** | `ViewData["Title"]` on every page, `ViewData["ShowDemo"]`; `ViewBag.States` etc. for application dropdowns (`AccountsController.PrepareFormAsync`), `ViewBag.Query` for admin list filters |
| **Dependency injection** | `IBankService`, `ICardService`, `IAuditLogger`, `IAdminService` registered in `Program.cs` (`AddScoped<…>`) and injected into controllers |
| **Custom middleware** | `Middleware/RequestTimingMiddleware.cs` (`app.UseMiddleware<…>()` in `Program.cs`) |
| **Global action filter** | `Filters/ForcePasswordChangeFilter.cs` (forces a password change after an admin reset) |
| **EF Core** (DbContext, DbSets, navigation properties, LINQ, SaveChanges, change tracking) | `Data/ApplicationDbContext.cs`; `Services/BankService.cs`; `Areas/Admin/Services/AdminService.cs` |
| **Eager loading** | `BankService.GetDashboardDataAsync` (`Include`/`ThenInclude`), `BankService.GetAccountDetailsAsync` (account page), `AdminService.GetAccountAsync` / `GetCustomerAsync` (admin detail pages) |
| **Explicit loading** | `BankService.GetStatementAsync`: `Entry(account).Collection(a => a.Transactions).Query().Where(…).LoadAsync()` |
| **Lazy loading (why not)** | Class comment at the top of `Services/BankService.cs` |
| **DB transactions + concurrency** | `BankService.InTransactionAsync`, `AdminService.ExecuteAsync`; `[ConcurrencyCheck]` on `Account.Balance` |
| **Identity + roles** | `Program.cs` (`AddIdentity`), roles `Admin` / `Customer` seeded; `[Authorize(Roles = …)]` on every controller (`AdminControllerBase` for the admin area) |
| **IDOR protection** | Every customer lookup filters by the signed-in user's id (`BankService.GetAccountAsync(id, userId)` …); explained in `AccountsController` (Account Details section) |
| **JSON API + DTOs + Swagger** | `Controllers/Api/AccountsApiController.cs`, `Areas/Admin/Controllers/Api/AdminApiController.cs`, DTOs in `Dtos/ApiDtos.cs`; Swagger in `Program.cs` (Development only) |
| **Charts (Chart.js from cdnjs)** | Customer dashboard 30-day in/out (`wwwroot/js/dashboard.js`); admin overview (`wwwroot/js/admin-overview.js` → `/api/admin/stats`) |
| **Demo mode** | `AuthController.DemoLogin` + "Demo Customer / Demo Admin" buttons (Development only) |

## Admin portal features (`/admin`)

Every action that changes data goes through **`IAdminService`**: one database transaction, an **AuditLog** row (admin, action, target, before/after, reason, time), a **required reason** for destructive or money-moving actions, a confirmation modal that states exactly what will happen, and a flash message after the redirect. Nothing is ever hard-deleted: accounts are **Closed**, transactions **Reversed**, cards **Cancelled**, logins **Locked**, applications **Denied**.

- **Overview**: KPI tiles (customers, deposits, accounts by type, transactions today, pending applications, frozen accounts), 3 charts, and a "Needs attention" panel
- **Customers**: search/sort/page; create (temporary password shown once, forced change at first login); profile edit; open an account for them; internal notes; lock/unlock login; reset password; force password change; read-only **View as customer**
- **Accounts**: filter by type/status/balance/owner; freeze/unfreeze; close (moving any balance first) and reopen; manual credit/debit adjustments; nickname and daily transfer limit; post interest (once per month)
- **Transactions**: filters + CSV export; details; **reverse** (offsetting transaction, original marked Reversed, both transfer legs together, can't reverse twice)
- **Applications**: Pending/Approved/Denied lists; approve (opens the account and moves the opening deposit) or deny (reason shown to the customer)
- **Settings**: savings/checking APY, default daily transfer limit, large-transaction alert threshold, auto-approve toggle, **Run monthly interest** (with summary)
- **Audit log**: filters, paging, CSV export

## Customer Account Details (`/accounts/{id}`)

Header (card graphic, balances, status banner, quick actions disabled when frozen/closed, savings APY/interest/goal) and tabs kept in the URL hash: **Activity** (search, type, date presets, grouped by day, expandable rows, money in/out/net, Load more, CSV), **Account Info** (inline nickname edit, Show/Copy account number re-masked after 30 s, rates & fees, printable monthly statements), **Direct Deposit** (routing `031300000`, Show/Copy, printable pre-filled form), **Card** (virtual debit card: show details, freeze/unfreeze, cancel, request a new card).

## API endpoints

Log in on the website first, then try them at **/swagger** (same cookie). No login → **401**. Customer endpoints → **403** for admins; `/api/admin/*` → **403** for customers. Another customer's id → **404**.

| Method | URL | Description |
|---|---|---|
| GET | `/api/accounts` | Your accounts |
| GET | `/api/accounts/{id}` | One account + balance + status |
| GET | `/api/accounts/{id}/transactions?page=1&pageSize=20&search=&kind=&from=&to=` | Activity page + money in/out for the filtered range (`kind`: in, out, deposit, withdrawal, transfer, interest, adjustment, reversal) |
| GET | `/api/accounts/{id}/number` | Full account number + routing number (audited) |
| POST | `/api/accounts/{id}/nickname` | `{ "nickname": "Rainy Day" }` |
| GET | `/api/accounts/{id}/cards/{cardId}/reveal` | Card number, expiry, CVV (CVV computed, never stored; audited) |
| POST | `/api/transfers` | `{ "fromAccountId": 1, "toAccountId": 2, "amount": 25.00, "memo": "rent" }`; 400 on frozen/closed, daily limit, insufficient funds |
| GET | `/api/admin/stats?days=30` | Admin KPIs + chart series |
| GET | `/api/admin/customers?search=` | Customers matching a name/email |
| GET | `/api/admin/accounts/{id}` | Admin view of any account |
| POST | `/api/admin/accounts/{id}/freeze` | `{ "reason": "…" }` (via `IAdminService`, audited) |
| POST | `/api/admin/accounts/{id}/unfreeze` | `{ "reason": "…" }` |
