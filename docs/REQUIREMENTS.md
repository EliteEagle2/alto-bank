# Alto Bank: course requirements, features, and API reference

All paths are relative to `AltoBank/`. Locations name the file and the class/method, so they stay correct as code moves.

## Two sides of the app

| Side | URL | Who | Code |
|---|---|---|---|
| Customer bank | `/dashboard`, `/accounts`, `/accounts/{id}`, `/transactions/...`, `/payments`, `/bills`, `/scheduled`, `/insights`, `/budgets`, `/alerts`, `/profile` | `Customer` role | `Controllers/`, `Views/`, layout `Views/Shared/_BankingLayout.cshtml` |
| Admin portal | `/admin/...` | `Admin` role (`/admin/staff` changes need `Owner`) | `Areas/Admin/` (own controllers, views, `_AdminLayout`, `_AdminSidebar`, `_ViewStart`, `_ViewImports`) |
| Public site | `/`, `/products/...`, `/company/...` | everyone | `Controllers/HomeController.cs` and friends, layout `_Layout.cshtml` |

Customers who open `/admin` (or admins who open customer pages) get a styled **Access Denied** page (`Views/Auth/AccessDenied.cshtml`).

## Requirements checklist (term → where it's used)

| Requirement | Where it's used |
|---|---|
| **Models** | `Models/Account.cs`, `Transaction.cs`, `ApplicationUser.cs`, `AccountApplication.cs`, `Card.cs`, `AuditLog.cs`, `CustomerNote.cs`, `BankSetting.cs`; Phase 2: `Payment.cs` (`Payment`, `MoneyRequest`), `BillPay.cs` (`Payee`, `ScheduledPayment`, `ScheduledRun`, `SchedulerRunLog`), `Alert.cs`, `UserPreferences.cs`, `Spending.cs` (`Budget`, `TransactionCategory`) |
| **Views / Controllers / Actions** | `Views/**`, `Areas/Admin/Views/**`; `Controllers/*.cs`, `Areas/Admin/Controllers/*.cs` (e.g. `TransactionsController.Deposit` GET + POST) |
| **Conventional + area routing** | `Program.cs`: `MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/...")` and `"default"` |
| **Attribute routes** | `AccountsController.Details` → `accounts/{id:int}`, `.Statement` → `accounts/{id}/statements/{year}/{month}`, `.ActivityCsv`; `CardsController` (`accounts/{id}/cards/{cardId}/freeze` …); `TransactionsController.History` → `accounts/{id}/history`; all API controllers |
| **Migrations** (history kept) | `Migrations/`: `InitialCreate` → `AccountOpeningDetails` → `AccountApplication` → `AccountBalanceConcurrency` → `AdminPortal` (converts old "Declined" applications to "Denied") → `SendAndRequest` → `BillPayAndScheduling` → `ProfileSettings` → `SpendingInsights` → `MobileCheckDeposit` (new limit columns default to $5,000 / $10,000 for the existing settings row) → `Disputes` |
| **Seeder** (3 customers + admin, accounts opened across the last 11 weeks with daily activity) | `Data/SeedData.cs` (`InitializeAsync`, `GenerateHistory`); command: `dotnet run -- --seed` / `--reset-db` (`Program.cs`) |
| **Master layout + partials** | `_Layout`, `_BankingLayout`, `_ApplyLayout`, `Areas/Admin/Views/Shared/_AdminLayout`; partials: `_AccountCard` (dashboard, accounts, admin view-as), `_TransactionTable`, `_StatusAlert` (all layouts), `_ApplicationStepNav` (apply + decision), `_AccountHeader`, `_TransactionRow`, `_TransactionFilters`, `_CopyField` (account info + direct deposit), `_CardArt` (home, products, apply, account header, card tab), `_ConfirmModal`, `_KpiTile`, `_AdminSidebar`, `_Pager`, `_StatusPill` |
| **Razor C# in views** | loops/grouping in `Views/Accounts/Details.cshtml` (groups activity by day), `Areas/Admin/Views/Accounts/Details.cshtml` (builds confirm modals), formatting helpers (`ToString("C")`, dates) |
| **Tag helpers** | `asp-*` everywhere; custom `<money>` tag helper `TagHelpers/MoneyTagHelper.cs` (green/red amounts), registered in both `_ViewImports` |
| **Validation attributes** | `[Required]`, `[Range]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`, `[Compare]` in `ViewModels/*`, `Areas/Admin/ViewModels/*`, `Dtos/*`; custom **`[MinimumAge(18)]`** in `Validation/MinimumAgeAttribute.cs` (used on the application and admin "create customer") |
| **TempData + Post/Redirect/Get** | Every POST sets `TempData["Success"/"Error"]` then redirects (e.g. `TransactionsController.Deposit`, `CardsController.Run`, `AdminControllerBase.Flash`); shown by `_StatusAlert` |
| **ViewData / ViewBag** | `ViewData["Title"]` on every page, `ViewData["ShowDemo"]`; `ViewBag.States` etc. for application dropdowns (`AccountsController.PrepareFormAsync`), `ViewBag.Query` for admin list filters |
| **Dependency injection** | `IBankService`, `ICardService`, `IAuditLogger`, `IAdminService`, and in Phase 2 `IAlertService`, `IPreferencesService`, `IPaymentRequestService`, `IBillPayService`, `IBudgetService`, `IInsightsService`, `ISchedulerRunner`, all registered in `Program.cs` (`AddScoped<…>`) and injected into controllers; the scheduler is `AddHostedService<SchedulerService>()` |
| **Custom middleware** | `Middleware/RequestTimingMiddleware.cs` (`app.UseMiddleware<…>()` in `Program.cs`) |
| **Global action filter** | `Filters/ForcePasswordChangeFilter.cs` (forces a password change after an admin reset) |
| **EF Core** (DbContext, DbSets, navigation properties, LINQ, SaveChanges, change tracking) | `Data/ApplicationDbContext.cs`; `Services/BankService.cs`; `Areas/Admin/Services/AdminService.cs` |
| **Eager loading** | `BankService.GetDashboardDataAsync` (`Include`/`ThenInclude`), `BankService.GetAccountDetailsAsync` (account page), `AdminService.GetAccountAsync` / `GetCustomerAsync` (admin detail pages) |
| **Explicit loading** | `BankService.GetStatementAsync`: `Entry(account).Collection(a => a.Transactions).Query().Where(…).LoadAsync()` |
| **Lazy loading (why not)** | Class comment at the top of `Services/BankService.cs` |
| **DB transactions + concurrency** | `BankService.InTransactionAsync`, `AdminService.ExecuteAsync`; `[ConcurrencyCheck]` on `Account.Balance` |
| **Identity + roles** | `Program.cs` (`AddIdentity`), roles `Admin` / `Customer` seeded; `[Authorize(Roles = …)]` on every controller (`AdminControllerBase` for the admin area) |
| **IDOR protection** | Every customer lookup filters by the signed-in user's id (`BankService.GetAccountAsync(id, userId)` …); explained in `AccountsController` (Account Details section) |
| **JSON API + DTOs + Swagger** | `Controllers/Api/AccountsApiController.cs`, `PaymentsApiController.cs`, `InsightsApiController.cs`, `Areas/Admin/Controllers/Api/AdminApiController.cs`, DTOs in `Dtos/ApiDtos.cs`; Swagger in `Program.cs` (Development only) |
| **Charts (Chart.js from cdnjs)** | Customer dashboard 30-day in/out (`wwwroot/js/dashboard.js`); admin overview (`wwwroot/js/admin-overview.js` → `/api/admin/stats`); Insights donut + month-over-month bars (`wwwroot/js/insights.js` → `/api/insights/summary`, `/api/insights/trend`) |
| **Demo mode** | `AuthController.DemoLogin` + "Demo Customer / Demo Admin" buttons (Development only) |

## Phase 2: where each term is used in the new code

Phase 2 added Send & Request, Bill Pay with a background scheduler, an Alerts center, Profile & settings, staff access,
Spending Insights & Budgets, Mobile Check Deposit (simulated) and Disputes. The money side of every new feature still goes through `IBankService` (`Services/BankService.Payments.cs`,
`Services/BankService.BillPay.cs`), so frozen/closed accounts, the daily limit and the balance concurrency check apply to all of it.

| Requirement | Where it's used in Phase 2 |
|---|---|
| **Models + relationships** | `Payment` links sender/recipient users, both accounts and both transaction legs; `MoneyRequest` (requester → payer, `SplitGroupId` for split bills); `ScheduledPayment` → `Payee` or `ToAccount`, with many `ScheduledRun`s; `Budget` → `ApplicationUser`. All foreign keys use `DeleteBehavior.Restrict` (`Data/ApplicationDbContext.cs`) |
| **Enums stored as text, defaults, sentinels** | `HasConversion<string>()` on every new status/kind enum; `Transaction.Category` uses `HasDefaultValue(Other).HasSentinel(Other)` so Food & Dining (enum value 0) isn't replaced by the database default (`ApplicationDbContext.OnModelCreating`) |
| **Indexes (including unique)** | `ScheduledRun (ScheduledPaymentId, RunDate)` is **unique**, so the database itself refuses to pay one date twice; `Alert (UserId, DedupeKey)` unique for once-only alerts; `Budget (UserId, Category)` unique; query indexes such as `ScheduledPayment (Status, NextRunDate)` and `MoneyRequest (Status, ExpiresAt)` |
| **Migrations with data changes** | `SendAndRequest`, `BillPayAndScheduling`, `ProfileSettings`, `SpendingInsights` (adds `Category` with default "Other"; the seeder's `CategorizeTransactionsAsync` then backfills the real categories) |
| **BackgroundService (hosted service)** | `Services/Scheduling/SchedulerService.cs`: a `BackgroundService` with a `PeriodicTimer` (`Scheduler:IntervalSeconds`, default 60). It's a singleton, so each run creates its own DI scope for the scoped `DbContext` and calls `SchedulerRunner.RunAsync`, which pays due items, expires 14-day-old requests and writes a `SchedulerRunLog`. Admin "Run scheduler now": `Areas/Admin/Controllers/ScheduledController.cs` |
| **Idempotency + failure handling** | `BankService.ExecuteScheduledAsync`: the `ScheduledRun` row, the money and the next date commit in one transaction; failures are recorded once with an alert (no silent retries); missed dates after downtime are recorded as Skipped |
| **DB transactions + concurrency** | `InTransactionAsync` wraps every P2P payment, bill payment and scheduled run; `[ConcurrencyCheck]` on `MoneyRequest.Status` stops a request being paid twice; budget alerts run after the commit and can never undo a payment (`BankService.RaiseBudgetAlertsAsync`) |
| **SQLite under concurrent load** | WAL journal mode (`SeedData.InitializeAsync`) and a busy timeout (`Data/SqliteBusyTimeoutInterceptor.cs`, an EF Core `DbConnectionInterceptor` registered with `AddInterceptors`) so the scheduler and web requests don't fail with "database is locked" |
| **LINQ + projections** | `Services/InsightsService.cs`: `Spending.LoadAsync` projects rows with `Select`, then groups by category/month/merchant in memory (`GroupBy`, `Sum`, `OrderByDescending`, `Take(5)`), because SQLite can't `SUM` decimals |
| **Attribute routing** | `PaymentsController` `[Route("payments")]`, `BillPayController` `[Route("bills")]`, `UpcomingController` `[Route("scheduled")]`, `AlertsController` `[Route("alerts")]`, `BudgetsController` `[Route("budgets")]`, `InsightsController` `[HttpGet("insights")]` and `[HttpPost("transactions/{id:int}/category")]` |
| **Model binding** | `[Bind(Prefix = "Pay")]` / `"Transfer"` / `"NewPayee"` / `"Form"` for pages with several forms (`BillPayController`, `UpcomingController`, `BudgetsController.Save`); `[FromQuery]` on `PaymentsApiController.Recipient`; JSON bodies bound to DTOs (`SendPaymentDto`, `RequestMoneyDto`, `RecategorizeDto`) |
| **Validation** | Attributes on `ViewModels/BillPayViewModels.cs`, `PaymentViewModels.cs`, `ProfileViewModels.cs`, `InsightsViewModels.cs` (`BudgetFormModel`) and the payment DTOs (`[MinLength]`/`[MaxLength]` on split recipients); rules that need the database (recipient exists, payee owned, schedule date on the repeat day) are checked in the services, and the message is shown on the form or as a flash message |
| **TempData + PRG + antiforgery** | Every new MVC POST sets `TempData["Success"/"Error"]` and redirects; `[AutoValidateAntiforgeryToken]` on the new controllers. The category picker is a real form with a token that JavaScript upgrades to the JSON API (`Views/Accounts/_TransactionRow.cshtml`, `wwwroot/js/account-details.js`) |
| **Authorization + IDOR** | `[Authorize(Roles = Customer)]` on every customer page/API; every lookup filters by the signed-in user (`BudgetService.RecategorizeAsync`, `RemoveAsync`, `BillPayService`, `PaymentRequestService`), so another customer's id returns 404. Staff changes need the `Owner` role (`StaffController`, `[Authorize(Roles = SeedData.OwnerRole)]`) |
| **Rate limiting** | `AddRateLimiter` policy `recipient-lookup` (60 lookups a minute per user) on `GET /api/payments/recipient` (`Program.cs`, `PaymentsApiController`) |
| **Configuration** | `Bank:TimeZone` (`Services/BankTime.cs`: dates in UTC, "today"/months in America/New_York); `Scheduler:IntervalSeconds`; `Bank:OwnerEmail` read from a git-ignored `.env` by `Configuration/DotEnv.cs` |
| **Audit logging** | `IAuditLogger` rows for payments, payees, schedules, budgets and recategorizing; `AuditLogger.ActAs` (an `AsyncLocal`) records scheduler work as "System" even when an admin started the run |
| **Partials, view components, layout** | New partials `_ScheduleWhen`, `_BudgetBar` (Budgets, Insights, dashboard), `_SectionTabs` (tabs for Move money, Pay & request, Insights, rendered by `_BankingLayout`), `Views/Dashboard/_BudgetWidget`; `AdminStatusBarViewComponent` (`Areas/Admin/ViewComponents`) on every admin page; one nav definition in `ViewModels/CustomerNav.cs` drives the sidebar, tabs and phone bar |
| **File upload model binding (`IFormFile`)** | `ViewModels/CheckDepositViewModels.cs`: `CheckDepositForm.Front` / `.Back` are `IFormFile` properties bound from a `multipart/form-data` POST (`<form enctype="multipart/form-data">` in `Views/Checks/Index.cshtml`, input names `Form.Front` / `Form.Back` with `[Bind(Prefix = "Form")]` in `ChecksController.Submit`). The API binds the same way with `[FromForm] CheckDepositUploadDto` (`Controllers/Api/CheckDepositsApiController.cs`) |
| **File validation** | `Services/FileStore.cs` (`IFileStore`): reads at most 5 MB, decides the type from the file's **signature** (JPEG `FF D8 FF`, PNG `89 50 4E 47…`), never from its name or Content-Type, and computes a SHA-256; nothing is written until every check passes. Saved under a random `Guid` name with `FileMode.CreateNew`; `Open()` only accepts names matching `^[0-9a-f]{32}\.(jpg|png)# Alto Bank: course requirements, features, and API reference

All paths are relative to `AltoBank/`. Locations name the file and the class/method, so they stay correct as code moves.

## Two sides of the app

| Side | URL | Who | Code |
|---|---|---|---|
| Customer bank | `/dashboard`, `/accounts`, `/accounts/{id}`, `/transactions/...`, `/payments`, `/bills`, `/scheduled`, `/insights`, `/budgets`, `/alerts`, `/profile` | `Customer` role | `Controllers/`, `Views/`, layout `Views/Shared/_BankingLayout.cshtml` |
| Admin portal | `/admin/...` | `Admin` role (`/admin/staff` changes need `Owner`) | `Areas/Admin/` (own controllers, views, `_AdminLayout`, `_AdminSidebar`, `_ViewStart`, `_ViewImports`) |
| Public site | `/`, `/products/...`, `/company/...` | everyone | `Controllers/HomeController.cs` and friends, layout `_Layout.cshtml` |

Customers who open `/admin` (or admins who open customer pages) get a styled **Access Denied** page (`Views/Auth/AccessDenied.cshtml`).

## Requirements checklist (term → where it's used)

| Requirement | Where it's used |
|---|---|
| **Models** | `Models/Account.cs`, `Transaction.cs`, `ApplicationUser.cs`, `AccountApplication.cs`, `Card.cs`, `AuditLog.cs`, `CustomerNote.cs`, `BankSetting.cs`; Phase 2: `Payment.cs` (`Payment`, `MoneyRequest`), `BillPay.cs` (`Payee`, `ScheduledPayment`, `ScheduledRun`, `SchedulerRunLog`), `Alert.cs`, `UserPreferences.cs`, `Spending.cs` (`Budget`, `TransactionCategory`) |
| **Views / Controllers / Actions** | `Views/**`, `Areas/Admin/Views/**`; `Controllers/*.cs`, `Areas/Admin/Controllers/*.cs` (e.g. `TransactionsController.Deposit` GET + POST) |
| **Conventional + area routing** | `Program.cs`: `MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/...")` and `"default"` |
| **Attribute routes** | `AccountsController.Details` → `accounts/{id:int}`, `.Statement` → `accounts/{id}/statements/{year}/{month}`, `.ActivityCsv`; `CardsController` (`accounts/{id}/cards/{cardId}/freeze` …); `TransactionsController.History` → `accounts/{id}/history`; all API controllers |
| **Migrations** (history kept) | `Migrations/`: `InitialCreate` → `AccountOpeningDetails` → `AccountApplication` → `AccountBalanceConcurrency` → `AdminPortal` (converts old "Declined" applications to "Denied") → `SendAndRequest` → `BillPayAndScheduling` → `ProfileSettings` → `SpendingInsights` |
| **Seeder** (3 customers + admin, accounts opened across the last 11 weeks with daily activity) | `Data/SeedData.cs` (`InitializeAsync`, `GenerateHistory`); command: `dotnet run -- --seed` / `--reset-db` (`Program.cs`) |
| **Master layout + partials** | `_Layout`, `_BankingLayout`, `_ApplyLayout`, `Areas/Admin/Views/Shared/_AdminLayout`; partials: `_AccountCard` (dashboard, accounts, admin view-as), `_TransactionTable`, `_StatusAlert` (all layouts), `_ApplicationStepNav` (apply + decision), `_AccountHeader`, `_TransactionRow`, `_TransactionFilters`, `_CopyField` (account info + direct deposit), `_CardArt` (home, products, apply, account header, card tab), `_ConfirmModal`, `_KpiTile`, `_AdminSidebar`, `_Pager`, `_StatusPill` |
| **Razor C# in views** | loops/grouping in `Views/Accounts/Details.cshtml` (groups activity by day), `Areas/Admin/Views/Accounts/Details.cshtml` (builds confirm modals), formatting helpers (`ToString("C")`, dates) |
| **Tag helpers** | `asp-*` everywhere; custom `<money>` tag helper `TagHelpers/MoneyTagHelper.cs` (green/red amounts), registered in both `_ViewImports` |
| **Validation attributes** | `[Required]`, `[Range]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`, `[Compare]` in `ViewModels/*`, `Areas/Admin/ViewModels/*`, `Dtos/*`; custom **`[MinimumAge(18)]`** in `Validation/MinimumAgeAttribute.cs` (used on the application and admin "create customer") |
| **TempData + Post/Redirect/Get** | Every POST sets `TempData["Success"/"Error"]` then redirects (e.g. `TransactionsController.Deposit`, `CardsController.Run`, `AdminControllerBase.Flash`); shown by `_StatusAlert` |
| **ViewData / ViewBag** | `ViewData["Title"]` on every page, `ViewData["ShowDemo"]`; `ViewBag.States` etc. for application dropdowns (`AccountsController.PrepareFormAsync`), `ViewBag.Query` for admin list filters |
| **Dependency injection** | `IBankService`, `ICardService`, `IAuditLogger`, `IAdminService`, and in Phase 2 `IAlertService`, `IPreferencesService`, `IPaymentRequestService`, `IBillPayService`, `IBudgetService`, `IInsightsService`, `ISchedulerRunner`, all registered in `Program.cs` (`AddScoped<…>`) and injected into controllers; the scheduler is `AddHostedService<SchedulerService>()` |
| **Custom middleware** | `Middleware/RequestTimingMiddleware.cs` (`app.UseMiddleware<…>()` in `Program.cs`) |
| **Global action filter** | `Filters/ForcePasswordChangeFilter.cs` (forces a password change after an admin reset) |
| **EF Core** (DbContext, DbSets, navigation properties, LINQ, SaveChanges, change tracking) | `Data/ApplicationDbContext.cs`; `Services/BankService.cs`; `Areas/Admin/Services/AdminService.cs` |
| **Eager loading** | `BankService.GetDashboardDataAsync` (`Include`/`ThenInclude`), `BankService.GetAccountDetailsAsync` (account page), `AdminService.GetAccountAsync` / `GetCustomerAsync` (admin detail pages) |
| **Explicit loading** | `BankService.GetStatementAsync`: `Entry(account).Collection(a => a.Transactions).Query().Where(…).LoadAsync()` |
| **Lazy loading (why not)** | Class comment at the top of `Services/BankService.cs` |
| **DB transactions + concurrency** | `BankService.InTransactionAsync`, `AdminService.ExecuteAsync`; `[ConcurrencyCheck]` on `Account.Balance` |
| **Identity + roles** | `Program.cs` (`AddIdentity`), roles `Admin` / `Customer` seeded; `[Authorize(Roles = …)]` on every controller (`AdminControllerBase` for the admin area) |
| **IDOR protection** | Every customer lookup filters by the signed-in user's id (`BankService.GetAccountAsync(id, userId)` …); explained in `AccountsController` (Account Details section) |
| **JSON API + DTOs + Swagger** | `Controllers/Api/AccountsApiController.cs`, `PaymentsApiController.cs`, `InsightsApiController.cs`, `Areas/Admin/Controllers/Api/AdminApiController.cs`, DTOs in `Dtos/ApiDtos.cs`; Swagger in `Program.cs` (Development only) |
| **Charts (Chart.js from cdnjs)** | Customer dashboard 30-day in/out (`wwwroot/js/dashboard.js`); admin overview (`wwwroot/js/admin-overview.js` → `/api/admin/stats`); Insights donut + month-over-month bars (`wwwroot/js/insights.js` → `/api/insights/summary`, `/api/insights/trend`) |
| **Demo mode** | `AuthController.DemoLogin` + "Demo Customer / Demo Admin" buttons (Development only) |

## Phase 2: where each term is used in the new code

Phase 2 added Send & Request, Bill Pay with a background scheduler, an Alerts center, Profile & settings, staff access, and
Spending Insights & Budgets. The money side of every new feature still goes through `IBankService` (`Services/BankService.Payments.cs`,
`Services/BankService.BillPay.cs`), so frozen/closed accounts, the daily limit and the balance concurrency check apply to all of it.

| Requirement | Where it's used in Phase 2 |
|---|---|
| **Models + relationships** | `Payment` links sender/recipient users, both accounts and both transaction legs; `MoneyRequest` (requester → payer, `SplitGroupId` for split bills); `ScheduledPayment` → `Payee` or `ToAccount`, with many `ScheduledRun`s; `Budget` → `ApplicationUser`. All foreign keys use `DeleteBehavior.Restrict` (`Data/ApplicationDbContext.cs`) |
| **Enums stored as text, defaults, sentinels** | `HasConversion<string>()` on every new status/kind enum; `Transaction.Category` uses `HasDefaultValue(Other).HasSentinel(Other)` so Food & Dining (enum value 0) isn't replaced by the database default (`ApplicationDbContext.OnModelCreating`) |
| **Indexes (including unique)** | `ScheduledRun (ScheduledPaymentId, RunDate)` is **unique**, so the database itself refuses to pay one date twice; `Alert (UserId, DedupeKey)` unique for once-only alerts; `Budget (UserId, Category)` unique; query indexes such as `ScheduledPayment (Status, NextRunDate)` and `MoneyRequest (Status, ExpiresAt)` |
| **Migrations with data changes** | `SendAndRequest`, `BillPayAndScheduling`, `ProfileSettings`, `SpendingInsights` (adds `Category` with default "Other"; the seeder's `CategorizeTransactionsAsync` then backfills the real categories) |
| **BackgroundService (hosted service)** | `Services/Scheduling/SchedulerService.cs`: a `BackgroundService` with a `PeriodicTimer` (`Scheduler:IntervalSeconds`, default 60). It's a singleton, so each run creates its own DI scope for the scoped `DbContext` and calls `SchedulerRunner.RunAsync`, which pays due items, expires 14-day-old requests and writes a `SchedulerRunLog`. Admin "Run scheduler now": `Areas/Admin/Controllers/ScheduledController.cs` |
| **Idempotency + failure handling** | `BankService.ExecuteScheduledAsync`: the `ScheduledRun` row, the money and the next date commit in one transaction; failures are recorded once with an alert (no silent retries); missed dates after downtime are recorded as Skipped |
| **DB transactions + concurrency** | `InTransactionAsync` wraps every P2P payment, bill payment and scheduled run; `[ConcurrencyCheck]` on `MoneyRequest.Status` stops a request being paid twice; budget alerts run after the commit and can never undo a payment (`BankService.RaiseBudgetAlertsAsync`) |
| **SQLite under concurrent load** | WAL journal mode (`SeedData.InitializeAsync`) and a busy timeout (`Data/SqliteBusyTimeoutInterceptor.cs`, an EF Core `DbConnectionInterceptor` registered with `AddInterceptors`) so the scheduler and web requests don't fail with "database is locked" |
| **LINQ + projections** | `Services/InsightsService.cs`: `Spending.LoadAsync` projects rows with `Select`, then groups by category/month/merchant in memory (`GroupBy`, `Sum`, `OrderByDescending`, `Take(5)`), because SQLite can't `SUM` decimals |
| **Attribute routing** | `PaymentsController` `[Route("payments")]`, `BillPayController` `[Route("bills")]`, `UpcomingController` `[Route("scheduled")]`, `AlertsController` `[Route("alerts")]`, `BudgetsController` `[Route("budgets")]`, `InsightsController` `[HttpGet("insights")]` and `[HttpPost("transactions/{id:int}/category")]` |
| **Model binding** | `[Bind(Prefix = "Pay")]` / `"Transfer"` / `"NewPayee"` / `"Form"` for pages with several forms (`BillPayController`, `UpcomingController`, `BudgetsController.Save`); `[FromQuery]` on `PaymentsApiController.Recipient`; JSON bodies bound to DTOs (`SendPaymentDto`, `RequestMoneyDto`, `RecategorizeDto`) |
| **Validation** | Attributes on `ViewModels/BillPayViewModels.cs`, `PaymentViewModels.cs`, `ProfileViewModels.cs`, `InsightsViewModels.cs` (`BudgetFormModel`) and the payment DTOs (`[MinLength]`/`[MaxLength]` on split recipients); rules that need the database (recipient exists, payee owned, schedule date on the repeat day) are checked in the services, and the message is shown on the form or as a flash message |
| **TempData + PRG + antiforgery** | Every new MVC POST sets `TempData["Success"/"Error"]` and redirects; `[AutoValidateAntiforgeryToken]` on the new controllers. The category picker is a real form with a token that JavaScript upgrades to the JSON API (`Views/Accounts/_TransactionRow.cshtml`, `wwwroot/js/account-details.js`) |
| **Authorization + IDOR** | `[Authorize(Roles = Customer)]` on every customer page/API; every lookup filters by the signed-in user (`BudgetService.RecategorizeAsync`, `RemoveAsync`, `BillPayService`, `PaymentRequestService`), so another customer's id returns 404. Staff changes need the `Owner` role (`StaffController`, `[Authorize(Roles = SeedData.OwnerRole)]`) |
| **Rate limiting** | `AddRateLimiter` policy `recipient-lookup` (60 lookups a minute per user) on `GET /api/payments/recipient` (`Program.cs`, `PaymentsApiController`) |
| **Configuration** | `Bank:TimeZone` (`Services/BankTime.cs`: dates in UTC, "today"/months in America/New_York); `Scheduler:IntervalSeconds`; `Bank:OwnerEmail` read from a git-ignored `.env` by `Configuration/DotEnv.cs` |
| **Audit logging** | `IAuditLogger` rows for payments, payees, schedules, budgets and recategorizing; `AuditLogger.ActAs` (an `AsyncLocal`) records scheduler work as "System" even when an admin started the run |
| **Partials, view components, layout** | New partials `_ScheduleWhen`, `_BudgetBar` (Budgets, Insights, dashboard), `_SectionTabs` (tabs for Move money, Pay & request, Insights, rendered by `_BankingLayout`), `Views/Dashboard/_BudgetWidget`; `AdminStatusBarViewComponent` (`Areas/Admin/ViewComponents`) on every admin page; one nav definition in `ViewModels/CustomerNav.cs` drives the sidebar, tabs and phone bar |
, so a stored path can't escape the folder. `[RequestSizeLimit]` / `[RequestFormLimits]` cap the whole request |
| **Serving files securely** | `ChecksController.Image` (owner only, filtered by user id) and the admin `CheckDepositsController.Image` (`[Authorize(Roles = Admin)]`) return `File(stream, contentType)` with `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`; the folder is outside `wwwroot` so there's no static URL |
| **Duplicate guard + concurrency** | `CheckDepositService.SubmitAsync` refuses a front image whose SHA-256 matches a deposit that wasn't rejected (index on `FrontHash`); `[ConcurrencyCheck]` on `CheckDeposit.Status` stops two admins deciding the same check |
| **Money through IBankService** | Approval (`AdminService.ApproveCheckDepositAsync`, inside `ExecuteAsync`'s transaction with an audit row and a required reason) calls `IBankService.PostCheckDepositInCurrentTransactionAsync` (`Services/BankService.CheckDeposits.cs`), which refuses frozen/closed accounts and uses the balance concurrency token |
| **Unique index as a concurrency guard** | `Dispute.TransactionId` is unique (`ApplicationDbContext`), so two simultaneous dispute submits on one transaction create only one; `DisputeService.SubmitAsync` also runs its checks and the insert in one transaction and turns the constraint error into "This transaction already has a dispute" |
| **Reusing the reversal system** | `AdminService.PostReversalLeg` (extracted from `ReverseTransactionAsync`) posts the offsetting transaction for one leg and a given amount; dispute approval (`AdminService.ApproveDisputeAsync`) uses it for a full or partial refund, on the sender's leg only for a P2P payment. `ReverseTransactionAsync` now refuses a charge with an open dispute or an earlier partial refund |
| **Settings captured on the record** | `Dispute.ProvisionalCreditEnabled` copies `BankSetting.ProvisionalCreditOnDisputes` when the dispute is filed, so changing the setting later doesn't change open disputes |
| **Conditional form + business validation** | `ViewModels/DisputeViewModels.cs` (`DisputeForm`: `[Required]`, `[StringLength(1000, MinimumLength = 20)]`, `[Range]`, `IFormFile? Attachment`); rules that depend on the chosen reason (expected amount below the charge, the duplicate's original must be one of the similar charges) are checked in `DisputeService`; `DisputeRules.WhyNot` is the single eligibility rule used by the rows, the form and the API |
| **Seeder** | `SeedData`: demo P2P payments and requests, payees and schedules, preferences, then `CategorizeTransactionsAsync` (backfill), `EnsureInsightsDemoDataAsync` (budgets set from each customer's real month-to-date spending so every seed shows under / near / over) `EnsureCheckDepositDemoDataAsync` and `EnsureDisputeDemoDataAsync` (disputes filed and decided through the real services) (placeholder check images drawn in code by `Data/SampleCheckImage.cs`, a tiny PNG encoder, marked "SAMPLE - NOT A REAL CHECK") |

## Admin portal features (`/admin`)

Every action that changes data goes through **`IAdminService`**: one database transaction, an **AuditLog** row (admin, action, target, before/after, reason, time), a **required reason** for destructive or money-moving actions, a confirmation modal that states exactly what will happen, and a flash message after the redirect. Nothing is ever hard-deleted: accounts are **Closed**, transactions **Reversed**, cards **Cancelled**, logins **Locked**, applications **Denied**.

- **Overview**: KPI tiles (customers, deposits, accounts by type, transactions today, pending applications, frozen accounts), 3 charts, and a "Needs attention" panel
- **Customers**: search/sort/page; create (temporary password shown once, forced change at first login); profile edit; open an account for them; internal notes; lock/unlock login; reset password; force password change; read-only **View as customer**
- **Accounts**: filter by type/status/balance/owner; freeze/unfreeze; close (moving any balance first) and reopen; manual credit/debit adjustments; nickname and daily transfer limit; post interest (once per month)
- **Transactions**: filters + CSV export; details; **reverse** (offsetting transaction, original marked Reversed, both transfer legs together, can't reverse twice)
- **Applications**: Pending/Approved/Denied lists; approve (opens the account and moves the opening deposit) or deny (reason shown to the customer)
- **Settings**: savings/checking APY, default daily transfer limit, large-transaction alert threshold, auto-approve toggle, **Run monthly interest** (with summary)
- **Audit log**: filters, paging, CSV export
- **P2P Payments** (`/admin/payments`): every payment and request, with filters
- **Scheduled Payments** (`/admin/scheduled`): all items, a failures filter, the last scheduler run, and an audited **Run scheduler now**
- **Check Deposits** (`/admin/checkdeposits`): Pending / Approved / Rejected (opens on Pending, oldest first), detail page with both images and click-to-zoom, duplicate-image warning, the customer's deposit history; Approve (posts the money) or Reject (reason shown to the customer), each with a confirmation modal and a required reason; refused for frozen/closed accounts and for an admin's own check. Limits are on Settings
- **Disputes** (`/admin/disputes`): Submitted / Under review / Approved / Denied (opens on Submitted, oldest first; a "Disputes" chip in the overview work queue, which also flags accounts a clawback overdrew); detail page with the charge, the money (provisional credit, refund, clawback), the attachment, the customer's activity with that merchant and their other disputes; Start review, Approve (refund through the reversal system) and Deny (reason shown to the customer). The "Provisional credit on new disputes" switch is on Settings
- **Staff** (`/admin/staff`, Owner only): promote a user to admin, create a staff login, remove admin access; admins can't use admin tools on their own records

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
| GET | `/api/payments/recipient?q=maya@altobank.com` | Recipient preview ("Maya C."); rate-limited |
| POST | `/api/payments/send` | `{ "fromAccountId": 1, "to": "maya@altobank.com", "amount": 20.00, "memo": "lunch" }` |
| POST | `/api/payments/request` | `{ "recipients": [{ "to": "…", "amount": 10 }], "memo": "…", "splitTotal": 30, "splitEvenly": true, "includeMe": true }` |
| GET | `/api/payments/requests` | Your incoming and outgoing requests |
| POST | `/api/payments/requests/{id}/pay` | `{ "fromAccountId": 1 }` |
| POST | `/api/payments/requests/{id}/decline` · `/cancel` | Decline a request to you, or cancel your own |
| GET | `/api/insights/categories` | Every category with its label and chart color |
| GET | `/api/insights/summary?month=2026-11` | One month: spending by category, top merchants, money in/out, callouts, budgets |
| GET | `/api/insights/trend?month=2026-11` | Spending per month (up to 6 months ending at `month`) |
| GET | `/api/insights/budgets` | This month's budget progress |
| POST | `/api/checkdeposits` | `multipart/form-data`: `accountId`, `amount`, `front`, `back` (JPG/PNG, 5 MB each) plus the anti-forgery token (header `RequestVerificationToken`); 201 with the pending deposit |
| GET | `/api/checkdeposits` · `/api/checkdeposits/{id}` | Your check deposits / one of them (another customer's id → 404) |
| POST | `/api/disputes` | `multipart/form-data`: `transactionId`, `reason` (NotAuthorized, WrongAmount, NotReceived, Duplicate, Other), `description` (20-1000 chars), `expectedAmount`, `duplicateOfTransactionId`, `freezeCard`, optional `attachment` (JPG/PNG), plus the anti-forgery token; 201 with the dispute |
| GET | `/api/disputes` · `/api/disputes/{id}` | Your disputes / one of them (another customer's id → 404) |
| POST | `/api/insights/transactions/{id}/category` | `{ "category": "Groceries" }` or `"auto"` to go back to the automatic category |
| GET | `/api/admin/stats?days=30` | Admin KPIs + chart series |
| GET | `/api/admin/customers?search=` | Customers matching a name/email |
| GET | `/api/admin/accounts/{id}` | Admin view of any account |
| POST | `/api/admin/accounts/{id}/freeze` | `{ "reason": "…" }` (via `IAdminService`, audited) |
| POST | `/api/admin/accounts/{id}/unfreeze` | `{ "reason": "…" }` |
