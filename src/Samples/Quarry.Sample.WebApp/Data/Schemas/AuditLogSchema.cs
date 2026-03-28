using Quarry;

namespace Quarry.Sample.WebApp.Data.Schemas;

public class AuditLogSchema : Schema
{
    public static string Table => "audit_logs";

    public Key<int> AuditLogId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<AuditAction> Action { get; }
    public Col<string?> Detail => Length(500);   // CS8619: Length() returns ColumnBuilder<string>; nullable column with length constraint
    public Col<string?> IpAddress => Length(45); // CS8619: same — tracked for framework-level fix
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
}
