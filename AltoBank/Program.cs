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
    options.AccessDeniedPath = "/Auth/Login";
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

// 4) Swagger / OpenAPI docs for the JSON API (only turned on in Development, below).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Alto Bank API", Version = "v1",
        Description = "JSON API for Alto Bank. Log in on the website first; the API uses the same login cookie." });
});

var app = builder.Build();

// Create/upgrade the database (migrations) and add the demo users on startup.
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

// Conventional route: /{controller}/{action}/{id?}. Attribute routes ([Route], [HttpGet("...")])
// on individual controllers/actions are mapped by this call too.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
