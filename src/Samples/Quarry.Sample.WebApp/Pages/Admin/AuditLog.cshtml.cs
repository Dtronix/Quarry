using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry;
using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Pages.Admin;

[Authorize(Policy = "Admin")]
public class AuditLogModel(AppDb db) : PageModel
{
    public List<AuditLogEntry> Entries { get; set; } = [];
    public new int Page { get; set; } = 1;
    public int TotalPages { get; set; }

    private const int PageSize = 25;

    public async Task OnGetAsync(int page = 1)
    {
        Page = page < 1 ? 1 : page;

        Entries = await db.AuditLogs()
            .Join<User>((a, u) => a.UserId.Id == u.UserId)
            .OrderBy((a, u) => a.CreatedAt, Direction.Descending)
            .Select((a, u) => new AuditLogEntry
            {
                AuditLogId = a.AuditLogId,
                UserName = u.UserName,
                Action = a.Action,
                Detail = a.Detail,
                IpAddress = a.IpAddress,
                CreatedAt = a.CreatedAt
            })
            .Limit(PageSize)
            .Offset((Page - 1) * PageSize)
            .ExecuteFetchAllAsync();

        var totalCount = await db.AuditLogs()
            .Select(a => Sql.Count())
            .ExecuteScalarAsync<int>();

        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
    }
}
