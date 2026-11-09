using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// Base class for every admin page controller. The attributes are inherited, so EVERY admin
/// controller is automatically in the Admin area (/admin/...) and admin-only:
///   [Area("Admin")]                       → URLs under /admin, views in Areas/Admin/Views
///   [Authorize(Roles = "Admin")]          → customers get the Access Denied page
///   [AutoValidateAntiforgeryToken]        → every POST must carry the anti-forgery token
///   [ApiExplorerSettings(IgnoreApi=true)] → HTML pages stay out of Swagger
/// </summary>
[Area("Admin")]
[Authorize(Roles = SeedData.AdminRole)]
[AutoValidateAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class AdminControllerBase : Controller
{
    /// <summary>Post/Redirect/Get: store the result as a flash message for the next page.</summary>
    protected void Flash(AdminResult result) =>
        TempData[result.Success ? "Success" : "Error"] = result.Message;
}
