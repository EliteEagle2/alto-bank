using System.Globalization;
using AltoBank.Data;
using AltoBank.Middleware;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Use US formatting everywhere so money always shows as $1,234.56.
var usCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = usCulture;
CultureInfo.DefaultThreadCurrentUICulture = usCulture;

// ===================== SERVICES (dependency injection container) =====================

// 1) Database: EF Core with SQLite (connection string lives in appsettings.json).
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2) ASP.NET Core Identity: password hashing, login cookies, users, and ROLES (for Admin).
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Where [Authorize] sends people who aren't logged in. For the JSON API (/api/...) we return
// 401/403 status codes instead of redirecting to the HTML login page.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";   // signed in, but wrong role (e.g. customer -> /admin)
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

// 3) Our banking logic. Controllers ask for IBankService; DI hands them a BankService.
//    Scoped = one instance per web request (same lifetime as the DbContext it uses).
builder.Services.AddScoped<IBankService, BankService>();

builder.Services.AddControllersWithViews();

// Generate lowercase URLs everywhere (/admin, /dashboard, /accounts/5) instead of /Admin, /Dashboard.
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// 4) Swagger / OpenAPI docs for the JSON API (only turned on in Development, below).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Alto Bank API", Version = "v1",
        Description = "JSON API for Alto Bank. Log in on the website first; the API uses the same login cookie." });
});

var app = builder.Build();

// ===================== DATABASE SEEDING =====================
// Command-line seeding (the app seeds and then EXITS instead of starting the web server):
//   dotnet run -- --seed        apply migrations + add any missing demo data (safe to repeat)
//   dotnet run -- --reset-db    DELETE the database, rebuild it from migrations, seed fresh demo data
if (args.Contains("--seed") || args.Contains("--reset-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (args.Contains("--reset-db"))
    {
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine("Database deleted.");
    }
    await SeedData.InitializeAsync(scope.ServiceProvider);   // runs MigrateAsync() first, then seeds
    Console.WriteLine(await SeedData.SummaryAsync(scope.ServiceProvider));
    return; // don't start the web server
}

// Normal startup: create/upgrade the database (migrations) and add any missing demo data.
using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

// ===================== MIDDLEWARE PIPELINE (runs in this order for every request) =====================

app.UseMiddleware<RequestTimingMiddleware>(); // our custom middleware: logs + times every request

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();      // serves /swagger/v1/swagger.json
    app.UseSwaggerUI();    // serves the interactive page at /swagger
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // who are you?
app.UseAuthorization();  // are you allowed here?

// AREA route (must come first): /admin/... goes to controllers marked [Area("Admin")]
// in Areas/Admin/Controllers. "area:exists" only matches names of real areas.
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

// Customer + public site: /{controller}/{action}/{id?}. Attribute routes ([Route], [HttpGet("...")])
// on individual controllers/actions are mapped by these calls too.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
