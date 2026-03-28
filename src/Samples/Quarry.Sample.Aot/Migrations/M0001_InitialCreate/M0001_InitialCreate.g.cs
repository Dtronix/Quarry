using System;
using Quarry;
using Quarry.Migration;

namespace Quarry.Sample.Aot.Migrations;

[MigrationSnapshot(Version = 1, Name = "InitialCreate", Timestamp = "2026-03-28T06:20:33.1926970+00:00", SchemaHash = "47159ce3045b3f55")]
[Migration(Version = 1, Name = "InitialCreate")]
internal static partial class M0001_InitialCreate
{
    public static void Upgrade(MigrationBuilder builder)
    {
        BeforeUpgrade(builder);
        builder.CreateTable("categories", null, t =>
        {
            t.Column("CategoryId", c => c.ClrType("int").NotNull());
            t.Column("Name", c => c.ClrType("string").NotNull());
            t.PrimaryKey("PK_categories", "CategoryId");
        });
        builder.CreateTable("order_items", null, t =>
        {
            t.Column("OrderItemId", c => c.ClrType("int").NotNull());
            t.Column("Quantity", c => c.ClrType("int").NotNull());
            t.Column("UnitPrice", c => c.ClrType("decimal").NotNull());
            t.Column("ProductId", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_order_items", "OrderItemId");
        });
        builder.CreateTable("products", null, t =>
        {
            t.Column("ProductId", c => c.ClrType("int").NotNull());
            t.Column("Name", c => c.ClrType("string").NotNull());
            t.Column("Price", c => c.ClrType("Money").NotNull());
            t.Column("IsActive", c => c.ClrType("bool").NotNull());
            t.Column("CreatedAt", c => c.ClrType("DateTime").NotNull());
            t.Column("Priority", c => c.ClrType("Priority").NotNull());
            t.Column("Description", c => c.ClrType("string").Nullable());
            t.Column("CategoryId", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_products", "ProductId");
        });
        builder.AddForeignKey("FK_order_items_ProductId", "order_items", "ProductId", "products", "ProductId");
        builder.AddForeignKey("FK_products_CategoryId", "products", "CategoryId", "categories", "CategoryId");
        AfterUpgrade(builder);
    }

    public static void Downgrade(MigrationBuilder builder)
    {
        BeforeDowngrade(builder);
        builder.DropForeignKey("FK_products_CategoryId", "products");
        builder.DropForeignKey("FK_order_items_ProductId", "order_items");
        builder.DropTable("products");
        builder.DropTable("order_items");
        builder.DropTable("categories");
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
            .SetTimestamp(DateTimeOffset.Parse("2026-03-28T06:20:33.1926970+00:00"));

        builder.AddTable(t => t
            .Name("categories")
            .AddColumn(c => c.Name("CategoryId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("Name").ClrType("string"))
        );

        builder.AddTable(t => t
            .Name("order_items")
            .AddColumn(c => c.Name("OrderItemId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("Quantity").ClrType("int"))
            .AddColumn(c => c.Name("UnitPrice").ClrType("decimal"))
            .AddColumn(c => c.Name("ProductId").ClrType("int").ForeignKey("ProductSchema"))
            .AddForeignKey("FK_order_items_ProductId", "ProductId", "products", "ProductId")
        );

        builder.AddTable(t => t
            .Name("products")
            .AddColumn(c => c.Name("ProductId").ClrType("int").PrimaryKey())
            .AddColumn(c => c.Name("Name").ClrType("string"))
            .AddColumn(c => c.Name("Price").ClrType("Money"))
            .AddColumn(c => c.Name("IsActive").ClrType("bool"))
            .AddColumn(c => c.Name("CreatedAt").ClrType("DateTime"))
            .AddColumn(c => c.Name("Priority").ClrType("Priority"))
            .AddColumn(c => c.Name("Description").ClrType("string").Nullable())
            .AddColumn(c => c.Name("CategoryId").ClrType("int").ForeignKey("CategorySchema"))
            .AddForeignKey("FK_products_CategoryId", "CategoryId", "categories", "CategoryId")
            .AddIndex("IX_Name", new[] { "Name" })
        );

        return builder.Build();
    }
}
