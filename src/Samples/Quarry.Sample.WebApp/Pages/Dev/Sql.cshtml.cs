using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry;
using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Pages.Dev;

[Authorize(Policy = "Admin")]
public class SqlModel(AppDb db) : PageModel
{
    public List<DiagnosticEntry> Queries { get; set; } = [];

    public class DiagnosticEntry
    {
        public required string Label { get; set; }
        public required string Description { get; set; }
        public required QueryDiagnostics Diagnostics { get; set; }
    }

    public void OnGet()
    {
        Queries =
        [
            new()
            {
                Label = "Simple Select",
                Description = "Entity projection with full column list",
                Diagnostics = db.Users().Select(u => u).ToDiagnostics()
            },
            new()
            {
                Label = "Filtered Select",
                Description = "Where with enum comparison and DTO projection",
                Diagnostics = db.Users()
                    .Where(u => u.IsActive && u.Role == UserRole.Admin)
                    .Select(u => new UserNameEmail { UserName = u.UserName, Email = u.Email })
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Pagination",
                Description = "OrderBy, Limit, Offset",
                Diagnostics = db.Users()
                    .Select(u => u)
                    .OrderBy(u => u.UserName)
                    .Limit(10).Offset(20)
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Join",
                Description = "2-table join with tuple projection",
                Diagnostics = db.AuditLogs()
                    .Join<User>((a, u) => a.UserId.Id == u.UserId)
                    .Select((a, u) => (u.UserName, a.Action))
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Aggregate",
                Description = "GroupBy with Sql.Count()",
                Diagnostics = db.Users()
                    .GroupBy(u => u.Role)
                    .Select(u => (u.Role, Sql.Count()))
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Navigation Subquery",
                Description = "EXISTS subquery via Many<T>.Any()",
                Diagnostics = db.Users()
                    .Where(u => u.Sessions.Any(s => s.ExpiresAt > DateTime.UtcNow))
                    .Select(u => u)
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Insert",
                Description = "Initializer-aware insert (only set properties generate columns)",
                Diagnostics = db.Users().Insert(new User
                {
                    UserName = "example",
                    Email = "ex@example.com",
                    PasswordHash = new byte[] { 1, 2, 3 },
                    Salt = new byte[] { 4, 5, 6 },
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }).ToDiagnostics()
            },
            new()
            {
                Label = "Update",
                Description = "Multi-column Set with Where",
                Diagnostics = db.Users().Update()
                    .Set(u => { u.UserName = "updated"; u.IsActive = true; })
                    .Where(u => u.UserId == 1)
                    .ToDiagnostics()
            },
            new()
            {
                Label = "Delete",
                Description = "Delete with Where clause",
                Diagnostics = db.Users().Delete()
                    .Where(u => u.UserId == 1)
                    .ToDiagnostics()
            },
        ];
    }
}
