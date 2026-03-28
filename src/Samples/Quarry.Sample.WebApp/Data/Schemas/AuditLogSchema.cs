using Quarry;

namespace Quarry.Sample.WebApp.Data.Schemas;

public class AuditLogSchema : Schema
{
    public static string Table => "audit_logs";

    public Key<int> AuditLogId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<AuditAction> Action { get; }
    public Col<string?> Detail { get; }
    public Col<string?> IpAddress { get; }
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
}
