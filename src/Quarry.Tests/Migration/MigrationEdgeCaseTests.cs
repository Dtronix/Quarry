using Quarry.Migration;
using ForeignKeyAction = Quarry.Migration.ForeignKeyAction;

namespace Quarry.Tests.Migration;

/// <summary>
/// Edge case tests for migration DDL generation (item 5.3).
/// Covers ForeignKeyAction enum values, chained column modifiers, and ColumnDefBuilder API consistency.
/// </summary>
[TestFixture]
public class MigrationEdgeCaseTests
{
    #region ForeignKeyAction enum values

    [TestCase(ForeignKeyAction.Cascade, "CASCADE")]
    [TestCase(ForeignKeyAction.SetNull, "SET NULL")]
    [TestCase(ForeignKeyAction.SetDefault, "SET DEFAULT")]
    [TestCase(ForeignKeyAction.Restrict, "RESTRICT")]
    public void AddForeignKey_ExplicitActions_ProduceCorrectDdl(ForeignKeyAction action, string expected)
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AddForeignKey("FK_test", "child", "parent_id", "parent", "id",
            onDelete: action, onUpdate: action);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain($"ON DELETE {expected}"));
        Assert.That(sql, Does.Contain($"ON UPDATE {expected}"));
    }

    [Test]
    public void AddForeignKey_NoAction_OmitsClause()
    {
        // NoAction is the default — renderer omits it
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AddForeignKey("FK_test", "child", "parent_id", "parent", "id",
            onDelete: ForeignKeyAction.NoAction);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("FOREIGN KEY"));
        Assert.That(sql, Does.Not.Contain("ON DELETE"));
    }

    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddForeignKey_Cascade_WorksAcrossDialects(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddForeignKey("FK_test", "child", "parent_id", "parent", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CASCADE"));
    }

    #endregion

    #region Chained column modifiers

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddColumn_NullableWithDefault_ProducesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "status", c =>
            c.ClrType("string").Length(50).Nullable().DefaultValue("'active'"));

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DEFAULT"));
        Assert.That(sql, Does.Not.Contain("NOT NULL"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddColumn_NotNullWithDefault_ProducesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "status", c =>
            c.ClrType("string").Length(50).NotNull().DefaultValue("'active'"));

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("NOT NULL"));
        Assert.That(sql, Does.Contain("DEFAULT"));
    }

    #endregion

    #region ColumnDefBuilder API consistency (3.1, 3.2)

    [Test]
    public void ColumnDefBuilder_DefaultValue_SetsExpression()
    {
        var col = new ColumnDefBuilder()
            .Name("status").ClrType("string")
            .DefaultValue("'active'")
            .Build();

        Assert.That(col.HasDefault, Is.True);
        Assert.That(col.DefaultExpression, Is.EqualTo("'active'"));
    }

    [Test]
    public void ColumnDefBuilder_HasDefault_SetsFlag()
    {
        var col = new ColumnDefBuilder()
            .Name("created").ClrType("DateTime")
            .HasDefault()
            .Build();

        Assert.That(col.HasDefault, Is.True);
        Assert.That(col.DefaultExpression, Is.Null);
    }

    [Test]
    public void ColumnDefBuilder_Nullable_DefaultsToTrue()
    {
        var col = new ColumnDefBuilder()
            .Name("email").ClrType("string")
            .Nullable()
            .Build();

        Assert.That(col.IsNullable, Is.True);
    }

    [Test]
    public void ColumnDefBuilder_Nullable_False_SetsNotNull()
    {
        var col = new ColumnDefBuilder()
            .Name("email").ClrType("string")
            .Nullable(false)
            .Build();

        Assert.That(col.IsNullable, Is.False);
    }

    [Test]
    public void ColumnDefBuilder_Nullable_Toggle_LastWins()
    {
        var col = new ColumnDefBuilder()
            .Name("email").ClrType("string")
            .Nullable()
            .Nullable(false)
            .Build();

        Assert.That(col.IsNullable, Is.False);
    }

    #endregion

    #region Self-referential foreign key

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddForeignKey_SelfReferential_ProducesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.AddForeignKey("FK_employees_manager", "employees", "manager_id",
            "employees", "id", onDelete: ForeignKeyAction.SetNull);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("FOREIGN KEY"));
        Assert.That(sql, Does.Contain("REFERENCES"));
        Assert.That(sql, Does.Contain("SET NULL"));
    }

    #endregion
}
