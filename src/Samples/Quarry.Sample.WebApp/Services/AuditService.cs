using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Services;

public sealed class AuditService(AppDb db)
{
    public async Task LogAsync(int userId, AuditAction action, string? detail = null, string? ipAddress = null)
    {
        await db.AuditLogs().Insert(new AuditLog
        {
            UserId = userId,
            Action = action,
            Detail = detail,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        }).ExecuteNonQueryAsync();
    }
}
