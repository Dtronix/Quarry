using Quarry.Migration;
using ForeignKeyAction = Quarry.Migration.ForeignKeyAction;

namespace Quarry.Tests.Migration;

public class TableBuilderTests
{
    [Test]
    public void Column_SetsNameOnDefinition()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("test", null, t => t.Column("my_col", c => c.ClrType("int")));
        var ops = builder.GetOperations();
        var create = (CreateTableOperation)ops[0];
        Assert.That(create.Table.Columns[0].Name, Is.EqualTo("my_col"));
    }

    [Test]
    public void PrimaryKey_AddsPrimaryKeyConstraint()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("test", null, t =>
        {
            t.Column("id", c => c.ClrType("int"));
            t.PrimaryKey("PK_test", "id");
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        Assert.That(create.Table.Constraints, Has.Count.EqualTo(1));
        var pk = (PrimaryKeyConstraint)create.Table.Constraints[0];
        Assert.That(pk.Name, Is.EqualTo("PK_test"));
        Assert.That(pk.Columns, Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public void PrimaryKey_MultiColumn_AllColumnsPresent()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("test", null, t =>
        {
            t.Column("a", c => c.ClrType("int"));
            t.Column("b", c => c.ClrType("int"));
            t.PrimaryKey("PK_test", "a", "b");
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        var pk = (PrimaryKeyConstraint)create.Table.Constraints[0];
        Assert.That(pk.Columns, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ForeignKey_AddsForeignKeyConstraint()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("orders", null, t =>
        {
            t.Column("user_id", c => c.ClrType("int"));
            t.ForeignKey("FK_orders_users", "user_id", "users", "id");
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        var fk = (ForeignKeyConstraint)create.Table.Constraints[0];
        Assert.That(fk.Name, Is.EqualTo("FK_orders_users"));
        Assert.That(fk.Column, Is.EqualTo("user_id"));
        Assert.That(fk.RefTable, Is.EqualTo("users"));
        Assert.That(fk.RefColumn, Is.EqualTo("id"));
    }

    [Test]
    public void ForeignKey_WithActions_SetsOnDeleteOnUpdate()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("orders", null, t =>
        {
            t.Column("user_id", c => c.ClrType("int"));
            t.ForeignKey("FK_orders_users", "user_id", "users", "id",
                onDelete: ForeignKeyAction.Cascade, onUpdate: ForeignKeyAction.SetNull);
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        var fk = (ForeignKeyConstraint)create.Table.Constraints[0];
        Assert.That(fk.OnDelete, Is.EqualTo(ForeignKeyAction.Cascade));
        Assert.That(fk.OnUpdate, Is.EqualTo(ForeignKeyAction.SetNull));
    }

    [Test]
    public void Index_AddsIndexConstraint()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("email", c => c.ClrType("string"));
            t.Index("IX_users_email", new[] { "email" });
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        var idx = (IndexConstraint)create.Table.Constraints[0];
        Assert.That(idx.Name, Is.EqualTo("IX_users_email"));
        Assert.That(idx.Columns, Is.EqualTo(new[] { "email" }));
        Assert.That(idx.IsUnique, Is.False);
        Assert.That(idx.Filter, Is.Null);
    }

    [Test]
    public void Index_WithUniqueAndFilter_SetsBoth()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("users", null, t =>
        {
            t.Column("email", c => c.ClrType("string"));
            t.Index("IX_users_email", new[] { "email" }, unique: true, filter: "email IS NOT NULL");
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        var idx = (IndexConstraint)create.Table.Constraints[0];
        Assert.That(idx.IsUnique, Is.True);
        Assert.That(idx.Filter, Is.EqualTo("email IS NOT NULL"));
    }

    [Test]
    public void MultipleColumns_PreservesOrder()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("test", null, t =>
        {
            t.Column("first", c => c.ClrType("int"));
            t.Column("second", c => c.ClrType("string"));
            t.Column("third", c => c.ClrType("bool"));
        });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        Assert.That(create.Table.Columns, Has.Count.EqualTo(3));
        Assert.That(create.Table.Columns[0].Name, Is.EqualTo("first"));
        Assert.That(create.Table.Columns[1].Name, Is.EqualTo("second"));
        Assert.That(create.Table.Columns[2].Name, Is.EqualTo("third"));
    }

    [Test]
    public void Build_EmptyTable_NoColumnsOrConstraints()
    {
        var builder = new MigrationBuilder();
        builder.CreateTable("empty", null, t => { });
        var create = (CreateTableOperation)builder.GetOperations()[0];
        Assert.That(create.Table.Columns, Is.Empty);
        Assert.That(create.Table.Constraints, Is.Empty);
    }
}
