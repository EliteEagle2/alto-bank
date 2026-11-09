# Alto Bank: course requirements and API reference

## Requirements checklist (term → where it's used)

All paths are relative to `AltoBank/`.

| Requirement | Where it's used |
|---|---|
| **Models** | `Models/Account.cs`, `Models/Transaction.cs`, `Models/ApplicationUser.cs`, `Models/AccountApplication.cs` |
| **Views** | `Views/**` (e.g. `Views/Dashboard/Index.cshtml`, `Views/Accounts/Apply.cshtml`) |
| **Controllers / action methods** | `Controllers/*.cs`, e.g. `TransactionsController.Deposit` (GET + POST), `AccountsController.Apply` (GET + POST) |
| **Conventional routing** | `Program.cs:99` (`MapControllerRoute` `{controller=Home}/{action=Index}/{id?}`) |
| **Custom / attribute route** | `Controllers/TransactionsController.cs:116`: `[HttpGet("accounts/{id:int}/history")]`; API routes in `Controllers/Api/AccountsApiController.cs` |
| **Migrations** (history kept) | `Migrations/*_InitialCreate.cs`, `*_AccountOpeningDetails.cs`, `*_AccountApplication.cs` |
| **Seeder** (3 customers + admin, multiple accounts, ~90 days of transactions) | `Data/SeedData.cs:20` (applies migrations at line 81; transaction generator `GenerateHistory`) |
| **Master layout** | `Views/Shared/_Layout.cshtml` (public site); also `_BankingLayout.cshtml` (signed-in) and `_ApplyLayout.cshtml` (application) |
| **Partial views, used on multiple pages** | `_AccountCard` (Dashboard + Accounts), `_TransactionTable` (Dashboard + History), `_StatusAlert` (both main layouts), `_ApplicationStepNav` (Apply + Decision), `_EmptyState`, `_CardArt`, `_Navbar`, `_Footer`, and more in `Views/Shared` |
| **Razor C# in views** | Loops: `Views/Dashboard/Index.cshtml:35`, `Views/Company/Careers.cshtml:35` (`GroupBy`); conditionals: `Views/Shared/_TransactionTable.cshtml`; formatting: `ToString("MMM d, yyyy")`, `ToString("C")` |
| **Built-in tag helpers** | `asp-controller` / `asp-action` / `asp-for` / `asp-validation-for` / `asp-items` throughout, e.g. `Views/Transactions/_MoneyForm.cshtml:25` |
| **Custom tag helper** `<money>` | `TagHelpers/MoneyTagHelper.cs:19`; registered in `Views/_ViewImports.cshtml`; used in `_AccountCard.cshtml:10`, `_TransactionTable.cshtml` (green/red), `Dashboard/Index.cshtml:15`, `Admin/Index.cshtml:86` |
| **Model binding + validation attributes** | `[Required]`, `[Range]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`, `[Compare]` in `ViewModels/*.cs` (e.g. `AuthViewModels.cs:16`, `Dtos/ApiDtos.cs:26`) |
| **Custom validation attribute** `[MinimumAge(18)]` | `Validation/MinimumAgeAttribute.cs`; applied at `ViewModels/ApplicationViewModels.cs:55` |
| **TempData flash messages (Post/Redirect/Get)** | Set in `TransactionsController.cs:45,72,102` (deposit/withdraw/transfer), `:123` (error), `AccountsController.cs:178` (account opening); shown by `Views/Shared/_StatusAlert.cshtml` |
| **ViewData** | `ViewData["Title"]` on every page; `ViewData["ShowDemo"]` in `AuthController.cs:73` |
| **ViewBag** | Dropdown lists for the application: `AccountsController.cs:335` (`ViewBag.States`, `EmploymentStatuses`, `IncomeRanges`, ...), used in `Views/Accounts/_ApplyStep3Contact.cshtml` and `_ApplyStep4Employment.cshtml` |
| **Dependency injection** behind an interface | `Services/IBankService.cs`, `Services/BankService.cs`; registered at `Program.cs:55` (`AddScoped<IBankService, BankService>()`); injected into every banking controller |
| **Custom middleware** | `Middleware/RequestTimingMiddleware.cs` (logs `[AltoBank] GET /path -> 200 in 12 ms`, adds `X-Response-Time-ms`); registered at `Program.cs:77` |
| **EF Core: DbContext + DbSets** | `Data/ApplicationDbContext.cs:15-17` |
| **Navigation properties** | `ApplicationUser.Accounts/Applications`, `Account.Transactions`, `Transaction.Account`, `AccountApplication.Account/User` |
| **LINQ queries** | `Services/BankService.cs` (`Where`, `OrderBy`, `Skip/Take` paging, `GroupBy` for stats) |
| **SaveChanges + change tracking** | `Services/BankService.cs:114` (comment) and the deposit/withdraw/transfer methods; database transactions at `:149`, `:184`, `:206` |
| **Eager loading** (`Include` / `ThenInclude`) | `BankService.GetDashboardDataAsync` (`Services/BankService.cs:77`), used by `DashboardController` |
| **Explicit loading** (`Entry().Collection().LoadAsync()`) | `BankService.GetAccountWithHistoryAsync` (`Services/BankService.cs:61`), used by the History page |
| **Lazy loading: why it's not enabled** | Comment at `Services/BankService.cs:17` |
| **Identity + roles** | `Program.cs` (`AddIdentity<ApplicationUser, IdentityRole>`); `Admin` and `Customer` roles seeded in `Data/SeedData.cs`; `[Authorize(Roles = "Customer")]` on customer controllers; `[Area("Admin")] [Authorize(Roles = "Admin")]` on `Areas/Admin/Controllers/*` |
| **Areas** (admin vs. customer) | `Areas/Admin/` (own Controllers, Views, `_AdminLayout`, `_ViewStart`, `_ViewImports`); area route in `Program.cs` (`{area:exists}/{controller=Dashboard}/{action=Index}/{id?}`) |
| **Seeding command** | `Program.cs`: `dotnet run -- --seed` and `dotnet run -- --reset-db` |
| **JSON API with DTOs** | `Controllers/Api/AccountsApiController.cs:28` (`[ApiController]`), DTOs in `Dtos/ApiDtos.cs` |
| **401 for the API when logged out** | `Program.cs:39` (cookie `OnRedirectToLogin` returns 401 for `/api/*`) |
| **Swagger** | `Program.cs:61` (`AddSwaggerGen`), `:81` (`UseSwagger` / `UseSwaggerUI`, Development only) |
| **Admin dashboard + Chart.js** | `Areas/Admin/Views/Dashboard/Index.cshtml` (Chart.js 4.4.1 from cdnjs), `wwwroot/js/admin-overview.js` (fetches `/api/admin/stats`); customer 30-day chart in `Views/Dashboard/Index.cshtml` + `wwwroot/js/dashboard.js` |
| **Demo mode** | `AuthController.DemoLogin` (`Controllers/AuthController.cs:105`, Development only) + buttons in `Views/Auth/Login.cshtml` |

---

## API endpoints

All endpoints use the same login cookie as the website. Log in first, then try them at **/swagger**.
Without a login they return **401**. Customer endpoints return **403** for admins; `/api/admin/*` returns **403** for customers.

| Method | URL | Description | Errors |
|---|---|---|---|
| GET | `/api/accounts` | The signed-in user's accounts (`AccountDto[]`) | 401 |
| GET | `/api/accounts/{id}` | One account with its balance | 401, 404 if not yours |
| GET | `/api/accounts/{id}/transactions?page=1` | Transactions, newest first, 10 per page (`PagedResult<TransactionDto>`) | 400 if `page < 1`, 404 |
| POST | `/api/transfers` | Body `{ "fromAccountId": 1, "toAccountId": 2, "amount": 25.00, "memo": "rent" }`; transfers between your accounts via `IBankService` | **400** for invalid amount, same account, or insufficient funds |
| GET | `/api/admin/stats?days=30` | Admin totals: customers, deposits, accounts by type, transactions per day | 401, 403 |
