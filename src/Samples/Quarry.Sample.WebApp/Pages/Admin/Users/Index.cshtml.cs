using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quarry;
using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Pages.Admin.Users;

[Authorize(Policy = "Admin")]
public class IndexModel(AppDb db) : PageModel
{
    public List<UserListItem> Users { get; set; } = [];
    public string? Search { get; set; }
    public UserRole? RoleFilter { get; set; }
    public bool? ActiveOnly { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }

    private const int PageSize = 20;

    public async Task OnGetAsync(string? search, string? roleFilter, string? activeOnly, int page = 1)
    {
        Search = search;
        Page = page < 1 ? 1 : page;
        ActiveOnly = activeOnly == "true" ? true : null;
        RoleFilter = Enum.TryParse<UserRole>(roleFilter, out var rf) ? rf : null;

        // Conditional chain construction — each if branch is a conditional clause
        var query = db.Users().Select(u => new UserListItem
        {
            UserId = u.UserId, UserName = u.UserName, Email = u.Email,
            Role = u.Role, IsActive = u.IsActive, LastLoginAt = u.LastLoginAt
        });

        // Count query applies the same filters as the data query
        var countQuery = db.Users().Select(u => Sql.Count());

        if (!string.IsNullOrEmpty(Search))
        {
            query = query.Where(u => u.UserName.Contains(Search) || u.Email.Contains(Search));
            countQuery = countQuery.Where(u => u.UserName.Contains(Search) || u.Email.Contains(Search));
        }

        if (RoleFilter.HasValue)
        {
            var role = RoleFilter.Value;
            query = query.Where(u => u.Role == role);
            countQuery = countQuery.Where(u => u.Role == role);
        }

        if (ActiveOnly == true)
        {
            query = query.Where(u => u.IsActive);
            countQuery = countQuery.Where(u => u.IsActive);
        }

        Users = await query
            .OrderBy(u => u.UserName)
            .Limit(PageSize)
            .Offset((Page - 1) * PageSize)
            .ExecuteFetchAllAsync();

        var totalCount = await countQuery.ExecuteScalarAsync<int>();

        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
    }
}
