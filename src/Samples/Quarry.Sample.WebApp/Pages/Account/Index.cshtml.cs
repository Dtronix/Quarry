using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Pages.Account;

[Authorize]
public class IndexModel(AppDb db, PasswordHasher hasher, AuditService audit) : PageModel
{
    public ProfileDto? Profile { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class ProfileDto
    {
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class InputModel
    {
        [Required]
        public string CurrentPassword { get; set; } = "";

        [Required, MinLength(6)]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
        public string ConfirmNewPassword { get; set; } = "";
    }

    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public async Task OnGetAsync()
    {
        var userId = GetUserId();
        Profile = await db.Users()
            .Where(u => u.UserId == userId)
            .Select(u => new ProfileDto { UserName = u.UserName, Email = u.Email, CreatedAt = u.CreatedAt })
            .ExecuteFetchFirstAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }

        var userId = GetUserId();

        var user = await db.Users()
            .Where(u => u.UserId == userId)
            .Select(u => u)
            .ExecuteFetchFirstAsync();

        if (!hasher.Verify(Input.CurrentPassword, user.PasswordHash, user.Salt))
        {
            ModelState.AddModelError("Input.CurrentPassword", "Current password is incorrect");
            await OnGetAsync();
            return Page();
        }

        var (newHash, newSalt) = hasher.Hash(Input.NewPassword);

        await db.Users().Update()
            .Set(u => { u.PasswordHash = newHash; u.Salt = newSalt; })
            .Where(u => u.UserId == userId)
            .ExecuteNonQueryAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await audit.LogAsync(userId, AuditAction.PasswordChange, ipAddress: ip);

        TempData["Success"] = "Password changed successfully";
        return RedirectToPage();
    }
}
