using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry.Sample.WebApp.Auth;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Pages;

public class LogoutModel(SessionService sessions, AuditService audit) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        var token = Request.Cookies[SessionAuthDefaults.CookieName];
        if (token is not null)
        {
            await sessions.InvalidateSessionAsync(token);
            Response.Cookies.Delete(SessionAuthDefaults.CookieName);
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdStr, out var userId))
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await audit.LogAsync(userId, AuditAction.Logout, ipAddress: ip);
        }

        return RedirectToPage("/Login");
    }
}
