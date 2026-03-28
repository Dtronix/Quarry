using System;
using Quarry;
using Quarry.Migration;

namespace Quarry.Sample.WebApp.Migrations;

[MigrationSnapshot(Version = 1, Name = "InitialCreate", Timestamp = "2026-03-25T00:00:00.0000000+00:00", SchemaHash = "a1b2c3d4e5f6g7h8")]
[Migration(Version = 1, Name = "InitialCreate")]
internal static partial class M0001_InitialCreate
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);

        builder.CreateTable("users", null, t =>
        {
            t.Column("UserId", c => c.ClrType("int").NotNull());
            t.Column("Email", c => c.ClrType("string").Length(255).NotNull());
            t.Column("UserName", c => c.ClrType("string").Length(100).NotNull());
            t.Column("PasswordHash", c => c.ClrType("byte[]").NotNull());
            t.Column("Salt", c => c.ClrType("byte[]").NotNull());
            t.Column("Role", c => c.ClrType("int").NotNull());
            t.Column("IsActive", c => c.ClrType("bool").NotNull().DefaultValue("1"));
            t.Column("CreatedAt", c => c.ClrType("DateTime").NotNull());
            t.Column("LastLoginAt", c => c.ClrType("DateTime").Nullable());
            t.PrimaryKey("PK_users", "UserId");
        });

        builder.CreateTable("sessions", null, t =>
        {
            t.Column("SessionId", c => c.ClrType("int").NotNull());
            t.Column("UserId", c => c.ClrType("int").NotNull());
            t.Column("Token", c => c.ClrType("string").Length(64).NotNull());
            t.Column("ExpiresAt", c => c.ClrType("DateTime").NotNull());
            t.Column("CreatedAt", c => c.ClrType("DateTime").NotNull());
            t.PrimaryKey("PK_sessions", "SessionId");
        });

        builder.CreateTable("audit_logs", null, t =>
        {
            t.Column("AuditLogId", c => c.ClrType("int").NotNull());
            t.Column("UserId", c => c.ClrType("int").NotNull());
            t.Column("Action", c => c.ClrType("int").NotNull());
            t.Column("Detail", c => c.ClrType("string").Length(500).Nullable());
            t.Column("IpAddress", c => c.ClrType("string").Length(45).Nullable());
            t.Column("CreatedAt", c => c.ClrType("DateTime").NotNull());
            t.PrimaryKey("PK_audit_logs", "AuditLogId");
        });

        builder.AddForeignKey("FK_sessions_UserId", "sessions", "UserId", "users", "UserId");
        builder.AddForeignKey("FK_audit_logs_UserId", "audit_logs", "UserId", "users", "UserId");

        builder.AddIndex("IX_Email", "users", ["Email"], unique: true);
        builder.AddIndex("IX_Role", "users", ["Role"]);
        builder.AddIndex("IX_Token", "sessions", ["Token"], unique: true);
        builder.AddIndex("IX_ExpiresAt", "sessions", ["ExpiresAt"]);

        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropForeignKey("FK_audit_logs_UserId", "audit_logs");
        builder.DropForeignKey("FK_sessions_UserId", "sessions");
        builder.DropTable("audit_logs");
        builder.DropTable("sessions");
        builder.DropTable("users");
        AfterDowngrade(builder);
    }

    public static void Backup(MigrationBuilder builder)
    {
    }

    static partial void BeforeUpgrade(MigrationBuilder builder);
    static partial void AfterUpgrade(MigrationBuilder builder);
    static partial void BeforeDowngrade(MigrationBuilder builder);
    static partial void AfterDowngrade(MigrationBuilder builder);

    internal static SchemaSnapshot Build()
    {
        var builder = new SchemaSnapshotBuilder()
            .SetVersion(1)
            .SetName("InitialCreate")
            .SetTimestamp(DateTimeOffset.Parse("2026-03-25T00:00:00.0000000+00:00"));

        builder.AddTable(t => t
            .Name("users")
            .AddColumn(c => c.Name("UserId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("Email").ClrType("string").Length(255))
            .AddColumn(c => c.Name("UserName").ClrType("string").Length(100))
            .AddColumn(c => c.Name("PasswordHash").ClrType("byte[]"))
            .AddColumn(c => c.Name("Salt").ClrType("byte[]"))
            .AddColumn(c => c.Name("Role").ClrType("int"))
            .AddColumn(c => c.Name("IsActive").ClrType("bool").DefaultValue("1"))
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddColumn(c => c.Name("LastLoginAt").ClrType("DateTime").Nullable())
            .AddIndex("IX_Email", ["Email"], isUnique: true)
            .AddIndex("IX_Role", ["Role"])
        );

        builder.AddTable(t => t
            .Name("sessions")
            .AddColumn(c => c.Name("SessionId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserId").ClrType("int").ForeignKey("users"))
            .AddColumn(c => c.Name("Token").ClrType("string").Length(64))
            .AddColumn(c => c.Name("ExpiresAt").ClrType("DateTime"))
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddForeignKey("FK_sessions_UserId", "UserId", "users", "UserId")
            .AddIndex("IX_Token", ["Token"], isUnique: true)
            .AddIndex("IX_ExpiresAt", ["ExpiresAt"])
        );

        builder.AddTable(t => t
            .Name("audit_logs")
            .AddColumn(c => c.Name("AuditLogId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserId").ClrType("int").ForeignKey("users"))
            .AddColumn(c => c.Name("Action").ClrType("int"))
            .AddColumn(c => c.Name("Detail").ClrType("string").Length(500).Nullable())
            .AddColumn(c => c.Name("IpAddress").ClrType("string").Length(45).Nullable())
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddForeignKey("FK_audit_logs_UserId", "UserId", "users", "UserId")
        );

        return builder.Build();
    }
}
