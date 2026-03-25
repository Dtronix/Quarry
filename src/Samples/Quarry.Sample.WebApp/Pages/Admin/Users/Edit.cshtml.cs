using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry.Sample.WebApp.Data;
using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Pages.Admin.Users;

[Authorize(Policy = "Admin")]
public class EditModel(AppDb db, AuditService audit) : PageModel
{
    public User? UserDetail { get; set; }
    public List<AuditEntry> RecentAudit { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        UserDetail = await db.Users()
            .Where(u => u.UserId == id)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();

        if (UserDetail is null)
            return Page();

        RecentAudit = await db.AuditLogs()
            .Where(a => a.UserId.Id == id)
            .OrderBy(a => a.CreatedAt, Direction.Descending)
            .Select(a => new AuditEntry
            {
                Action = a.Action, Detail = a.Detail,
                IpAddress = a.IpAddress, CreatedAt = a.CreatedAt
            })
            .Limit(10)
            .ExecuteFetchAllAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var user = await db.Users()
            .Where(u => u.UserId == id)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();

        if (user is null)
            return NotFound();

        var newActive = !user.IsActive;
        await db.Users().Update()
            .Set(u => u.IsActive = newActive)
            .Where(u => u.UserId == id)
            .ExecuteNonQueryAsync();

        var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var action = newActive ? AuditAction.AccountReactivated : AuditAction.AccountDeactivated;
        await audit.LogAsync(adminId, action, $"User {id}", ip);

        TempData["Success"] = newActive ? "User reactivated" : "User deactivated";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(int id, string newRole)
    {
        if (!Enum.TryParse<UserRole>(newRole, out var role))
            return BadRequest();

        await db.Users().Update()
            .Set(u => u.Role = role)
            .Where(u => u.UserId == id)
            .ExecuteNonQueryAsync();

        var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await audit.LogAsync(adminId, AuditAction.RoleChange, $"User {id} → {newRole}", ip);

        TempData["Success"] = $"Role changed to {newRole}";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // Cascade delete: sessions → audit logs → user
        await db.Sessions().Delete().Where(s => s.UserId.Id == id).ExecuteNonQueryAsync();
        await db.AuditLogs().Delete().Where(a => a.UserId.Id == id).ExecuteNonQueryAsync();
        await db.Users().Delete().Where(u => u.UserId == id).ExecuteNonQueryAsync();

        TempData["Success"] = "User deleted";
        return RedirectToPage("/Admin/Users/Index");
    }
}
