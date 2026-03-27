using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry;
using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Pages.Admin;

[Authorize(Policy = "Admin")]
public class DashboardModel(AppDb db) : PageModel
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int ActiveSessions { get; set; }
    public List<RoleCount> UsersByRole { get; set; } = [];
    public List<DailyLoginCount> RecentLogins { get; set; } = [];

    public async Task OnGetAsync()
    {
        TotalUsers = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();

        ActiveUsers = await db.Users()
            .Where(u => u.IsActive)
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();

        var now = DateTime.UtcNow;
        ActiveSessions = await db.Sessions()
            .Where(s => s.ExpiresAt > now)
            .Select(s => Sql.Count())
            .ExecuteScalarAsync<int>();

        UsersByRole = await db.Users()
            .GroupBy(u => u.Role)
            .Select(u => new RoleCount { Role = u.Role, Count = Sql.Count() })
            .ExecuteFetchAllAsync();

        // SQLite date grouping via RawSql
        var sevenDaysAgo = now.AddDays(-7);
        var loginAction = (int)AuditAction.Login;
        RecentLogins = await db.RawSqlAsync<DailyLoginCount>(
            "SELECT date(\"CreatedAt\") as Day, COUNT(*) as Count FROM \"audit_logs\" WHERE \"Action\" = @p0 AND \"CreatedAt\" > @p1 GROUP BY date(\"CreatedAt\") ORDER BY Day DESC",
            loginAction, sevenDaysAgo);
    }
}
