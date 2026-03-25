using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry.Sample.WebApp.Auth;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Pages;

public class RegisterModel(AppDb db, PasswordHasher hasher, SessionService sessions, AuditService audit) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(3)]
        public string UserName { get; set; } = "";

        [Required, MinLength(6)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        // Check email uniqueness
        var exists = await db.Users()
            .Where(u => u.Email == Input.Email)
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();

        if (exists > 0)
        {
            ModelState.AddModelError("Input.Email", "Email already registered");
            return Page();
        }

        var (hash, salt) = hasher.Hash(Input.Password);

        var userId = await db.Users().Insert(new User
        {
            Email = Input.Email,
            UserName = Input.UserName,
            PasswordHash = hash,
            Salt = salt,
            Role = UserRole.User,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteScalarAsync<int>();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var token = await sessions.CreateSessionAsync(userId, ip);
        Response.Cookies.Append(SessionAuthDefaults.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(24)
        });

        await audit.LogAsync(userId, AuditAction.AccountCreated, ipAddress: ip);

        return RedirectToPage("/Index");
    }
}
