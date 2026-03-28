using Quarry.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests for MigrationBuilder input validation (fix 1.2).
/// </summary>
[TestFixture]
public class MigrationBuilderValidationTests
{
    [Test]
    public void CreateTable_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateTable(null!, null, _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateTable_EmptyName_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateTable("", null, _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateTable_WhitespaceName_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateTable("  ", null, _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateTable_NullConfigure_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateTable("users", null, null!),
            Throws.InstanceOf<ArgumentNullException>());

    [Test]
    public void DropTable_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropTable(null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void DropTable_EmptyName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropTable(""),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void RenameTable_NullOldName_Throws() =>
        Assert.That(() => new MigrationBuilder().RenameTable(null!, "new_table"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void RenameTable_NullNewName_Throws() =>
        Assert.That(() => new MigrationBuilder().RenameTable("old_table", null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddColumn_NullTable_Throws() =>
        Assert.That(() => new MigrationBuilder().AddColumn(null!, "col", _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddColumn_NullColumn_Throws() =>
        Assert.That(() => new MigrationBuilder().AddColumn("table", null!, _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddColumn_NullConfigure_Throws() =>
        Assert.That(() => new MigrationBuilder().AddColumn("table", "col", null!),
            Throws.InstanceOf<ArgumentNullException>());

    [Test]
    public void DropColumn_NullTable_Throws() =>
        Assert.That(() => new MigrationBuilder().DropColumn(null!, "col"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void DropColumn_NullColumn_Throws() =>
        Assert.That(() => new MigrationBuilder().DropColumn("table", null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void RenameColumn_NullTable_Throws() =>
        Assert.That(() => new MigrationBuilder().RenameColumn(null!, "old", "new"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void RenameColumn_EmptyOldName_Throws() =>
        Assert.That(() => new MigrationBuilder().RenameColumn("table", "", "new"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void RenameColumn_EmptyNewName_Throws() =>
        Assert.That(() => new MigrationBuilder().RenameColumn("table", "old", ""),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AlterColumn_NullTable_Throws() =>
        Assert.That(() => new MigrationBuilder().AlterColumn(null!, "col", _ => { }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddForeignKey_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().AddForeignKey(null!, "t", "c", "rt", "rc"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddForeignKey_NullRefTable_Throws() =>
        Assert.That(() => new MigrationBuilder().AddForeignKey("fk", "t", "c", null!, "rc"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void DropForeignKey_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropForeignKey(null!, "table"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddIndex_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().AddIndex(null!, "table", ["col"]),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void AddIndex_NullColumns_Throws() =>
        Assert.That(() => new MigrationBuilder().AddIndex("idx", "table", null!),
            Throws.InstanceOf<ArgumentNullException>());

    [Test]
    public void DropIndex_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropIndex(null!, "table"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void InsertData_NullTable_Throws() =>
        Assert.That(() => new MigrationBuilder().InsertData(null!, new { Id = 1 }),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Sql_NullSql_Throws() =>
        Assert.That(() => new MigrationBuilder().Sql(null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Sql_EmptySql_Throws() =>
        Assert.That(() => new MigrationBuilder().Sql(""),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateView_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateView(null!, "SELECT 1"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateView_NullSql_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateView("v", null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void DropView_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropView(null!),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void CreateProcedure_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().CreateProcedure(null!, "BEGIN END"),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void DropProcedure_NullName_Throws() =>
        Assert.That(() => new MigrationBuilder().DropProcedure(null!),
            Throws.InstanceOf<ArgumentException>());

    // Verify valid calls still work
    [Test]
    public void CreateTable_ValidArgs_DoesNotThrow() =>
        Assert.DoesNotThrow(() =>
            new MigrationBuilder().CreateTable("users", null, t =>
                t.Column("id", c => c.ClrType("int"))));
}
