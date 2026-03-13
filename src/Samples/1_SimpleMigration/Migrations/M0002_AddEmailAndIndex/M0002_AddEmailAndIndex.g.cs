using System;
using Quarry;
using Quarry.Migration;

namespace SimpleMigration.Migrations;

[MigrationSnapshot(Version = 2, Name = "AddEmailAndIndex", Timestamp = "2026-03-10T21:56:19.6861727+00:00", ParentVersion = 1, SchemaHash = "da1df62eb5c55fdf")]
[Migration(Version = 2, Name = "AddEmailAndIndex")]
internal static partial class M0002_AddEmailAndIndex
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);
        builder.AddColumn("users", "Email", c => c.ClrType("string").Nullable());
        builder.AddIndex("IX_Email", "users", new[] { "Email" }, unique: true);
        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropIndex("IX_Email", "users");
        builder.DropColumn("users", "Email");
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
            .SetVersion(2)
            .SetName("AddEmailAndIndex")
            .SetTimestamp(DateTimeOffset.Parse("2026-03-10T21:56:19.6861727+00:00"))
            .SetParentVersion(1);

        builder.AddTable(t => t
            .Name("posts")
            .AddColumn(c => c.Name("PostId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserId").ClrType("int").ForeignKey("User"))
            .AddColumn(c => c.Name("Title").ClrType("string"))
            .AddColumn(c => c.Name("Body").ClrType("string").Nullable())
            .AddForeignKey("FK_posts_UserId", "UserId", "User", "id")
        );

        builder.AddTable(t => t
            .Name("users")
            .AddColumn(c => c.Name("UserId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("UserName").ClrType("string"))
            .AddColumn(c => c.Name("Email").ClrType("string").Nullable())
            .AddIndex("IX_Email", new[] { "Email" }, isUnique: true)
        );

        return builder.Build();
    }
}
