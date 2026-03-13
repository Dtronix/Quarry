using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationBuilderSQLiteRebuildTests
{
    [Test]
    public void DropColumn_SQLite_WithSourceTable_EmitsRebuildSequence()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.DropColumn("users", "old_col")
            .WithSourceTable(t =>
            {
                t.Column("id", c => c.ClrType("int").NotNull());
                t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                t.Column("old_col", c => c.ClrType("string").Nullable());
                t.PrimaryKey("PK_users", "id");
            });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("RENAME TO"));
        Assert.That(sql, Does.Contain("_quarry_tmp_users"));
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("DROP TABLE"));
        // The new CREATE TABLE should not include old_col
        // But the INSERT SELECT should also omit it
        var createTableSection = sql.Substring(sql.IndexOf("CREATE TABLE"));
        var insertSection = sql.Substring(sql.IndexOf("INSERT INTO"));
        Assert.That(insertSection, Does.Not.Contain("old_col"));
    }

    [Test]
    public void AlterColumn_SQLite_WithSourceTable_EmitsRebuildSequence()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull())
            .WithSourceTable(t =>
            {
                t.Column("id", c => c.ClrType("int").NotNull());
                t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                t.PrimaryKey("PK_users", "id");
            });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("RENAME TO"));
        Assert.That(sql, Does.Contain("_quarry_tmp_users"));
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("DROP TABLE"));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropColumn_NonSQLite_EmitsSimpleDrop(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();
        builder.DropColumn("users", "old_col");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP COLUMN"));
        Assert.That(sql, Does.Not.Contain("RENAME TO"));
    }
}
