using System;
using Quarry;
using Quarry.Migration;

namespace SimpleMigration.Migrations;

[MigrationSnapshot(Version = 1, Name = "InitialCreate", Timestamp = "2026-03-10T21:54:47.2982209+00:00", SchemaHash = "6e86606876911d13")]
[Migration(Version = 1, Name = "InitialCreate")]
internal static partial class M0001_InitialCreate
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);
        builder.CreateTable("posts", null, t =>
        {
            t.Column("PostId", c => c.ClrType("int").NotNull());
            t.Column("UserId", c => c.ClrType("int").NotNull());
            t.Column("Title", c => c.ClrType("string").NotNull());
            t.Column("Body", c => c.ClrType("string").Nullable());
            t.PrimaryKey("PK_posts", "PostId");
        });
        builder.CreateTable("users", null, t =>
        {
            t.Column("UserId", c => c.ClrType("int").NotNull());
            t.Column("UserName", c => c.ClrType("string").NotNull());
            t.PrimaryKey("PK_users", "UserId");
        });
        builder.AddForeignKey("FK_posts_UserId", "posts", "UserId", "User", "id");
        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropForeignKey("FK_posts_UserId", "posts");
        builder.DropTable("users");
        builder.DropTable("posts");
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
            .SetTimestamp(DateTimeOffset.Parse("2026-03-10T21:54:47.2982209+00:00"));

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
        );

        return builder.Build();
    }
}
