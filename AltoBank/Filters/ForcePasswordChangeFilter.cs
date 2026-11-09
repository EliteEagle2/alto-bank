using AltoBank.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltoBank.Filters;

/// <summary>
/// GLOBAL ACTION FILTER (registered in Program.cs). If the logged-in user has MustChangePassword = true
/// (an admin created their account or reset their password), every MVC page redirects to
/// /auth/changepassword until they pick a new password. Login/logout and the change page itself are allowed.
/// API calls get 403 with a message instead of a redirect.
/// </summary>
public class ForcePasswordChangeFilter : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _users;

    public ForcePasswordChangeFilter(UserManager<ApplicationUser> users) => _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;
        var controller = context.RouteData.Values["controller"]?.ToString();

        if (principal.Identity?.IsAuthenticated == true && !string.Equals(controller, "Auth", StringComparison.OrdinalIgnoreCase))
        {
            var user = await _users.GetUserAsync(principal);
            if (user?.MustChangePassword == true)
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                    context.Result = new ObjectResult(new { message = "You must change your password before using the API." }) { StatusCode = 403 };
                else
                    context.Result = new RedirectToActionResult("ChangePassword", "Auth", new { area = "" });
                return;
            }
        }
        await next();
    }
}
