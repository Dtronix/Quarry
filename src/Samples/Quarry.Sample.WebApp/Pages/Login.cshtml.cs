using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry.Sample.WebApp.Auth;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Pages;

public class LoginModel(AppDb db, PasswordHasher hasher, SessionService sessions, AuditService audit) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return Page();

        var user = await db.Users()
            .Where(u => u.Email == Input.Email)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password");
            return Page();
        }

        if (!hasher.Verify(Input.Password, user.PasswordHash, user.Salt))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password");
            return Page();
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Account deactivated");
            return Page();
        }

        // Update last login — extract to local variable for generator analyzability
        var loginUserId = user.UserId;
        await db.Users().Update()
            .Set(u => u.LastLoginAt = DateTime.UtcNow)
            .Where(u => u.UserId == loginUserId)
            .ExecuteNonQueryAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var token = await sessions.CreateSessionAsync(loginUserId, ip);
        Response.Cookies.Append(SessionAuthDefaults.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(24)
        });

        await audit.LogAsync(loginUserId, AuditAction.Login, ipAddress: ip);

        return LocalRedirect(returnUrl ?? "/");
    }
}
