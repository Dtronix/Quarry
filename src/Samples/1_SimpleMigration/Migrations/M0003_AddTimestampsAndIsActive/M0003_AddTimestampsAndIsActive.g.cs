using System;
using Quarry;
using Quarry.Migration;

namespace SimpleMigration.Migrations;

[MigrationSnapshot(Version = 3, Name = "AddTimestampsAndIsActive", Timestamp = "2026-03-10T21:56:38.8035685+00:00", ParentVersion = 2, SchemaHash = "54901ae7de104f3d")]
[Migration(Version = 3, Name = "AddTimestampsAndIsActive")]
internal static partial class M0003_AddTimestampsAndIsActive
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);
        builder.AddColumn("posts", "CreatedAt", c => c.ClrType("DateTime").NotNull());
        builder.AddColumn("users", "IsActive", c => c.ClrType("bool").NotNull());
        builder.AddColumn("users", "CreatedAt", c => c.ClrType("DateTime").NotNull());
        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropColumn("users", "CreatedAt");
        builder.DropColumn("users", "IsActive");
        builder.DropColumn("posts", "CreatedAt");
        AfterDowngrade(builder);
    }

    public static void Backup(MigrationBuilder builder)
    {
        // Auto-generated backup for destructive steps.
        // Actual backup SQL is generated at apply-time using BackupGenerator.
    }

    static partial void BeforeUpgrade(MigrationBuilder builder);
    static partial void AfterUpgrade(MigrationBuilder builder);
    static partial void BeforeDowngrade(MigrationBuilder builder);
    static partial void AfterDowngrade(MigrationBuilder builder);

    internal static SchemaSnapshot Build()
    {
        var builder = new SchemaSnapshotBuilder()
            .SetVersion(3)
            .SetName("AddTimestampsAndIsActive")
            .SetTimestamp(DateTimeOffset.Parse("2026-03-10T21:56:38.8035685+00:00"))
            .SetParentVersion(2);

        builder.AddTable(t => t
            .Name("posts")
            .AddColumn(c => c.Name("PostId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserId").ClrType("int").ForeignKey("User"))
            .AddColumn(c => c.Name("Title").ClrType("string"))
            .AddColumn(c => c.Name("Body").ClrType("string").Nullable())
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddForeignKey("FK_posts_UserId", "UserId", "User", "id")
        );

        builder.AddTable(t => t
            .Name("users")
            .AddColumn(c => c.Name("UserId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserName").ClrType("string"))
            .AddColumn(c => c.Name("Email").ClrType("string").Nullable())
            .AddColumn(c => c.Name("IsActive").ClrType("bool"))
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddIndex("IX_Email", new[] { "Email" }, isUnique: true)
        );

        return builder.Build();
    }
}
