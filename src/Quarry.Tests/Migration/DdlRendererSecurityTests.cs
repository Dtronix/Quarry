using Quarry.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Tests that SQL injection via identifier names is prevented in INFORMATION_SCHEMA queries (fix 1.1).
/// </summary>
[TestFixture]
public class DdlRendererSecurityTests
{
    [Test]
    public void CreateTable_SqlServer_Idempotent_EscapesSingleQuotesInTableName()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.CreateTable("test'table", null, t =>
            t.Column("id", c => c.ClrType("int").NotNull()));

        var sql = builder.BuildIdempotentSql(dialect);

        // The INFORMATION_SCHEMA check should have escaped single quotes
        Assert.That(sql, Does.Contain("TABLE_NAME = 'test''table'"));
    }

    [Test]
    public void DropTable_SqlServer_Idempotent_EscapesSingleQuotesInTableName()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.DropTable("test'table");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("TABLE_NAME = 'test''table'"));
    }

    [Test]
    public void AddColumn_SqlServer_Idempotent_EscapesColumnName()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "col'inject", c => c.ClrType("int"));

        var sql = builder.BuildIdempotentSql(dialect);

        // sys.columns check should escape both table and column names
        Assert.That(sql, Does.Contain("name = 'col''inject'"));
    }

    [Test]
    public void AddColumn_SqlServer_Idempotent_EscapesSchemaQualifiedTableName()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.AddColumn("tbl'bad", "col", c => c.ClrType("int"));

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("OBJECT_ID('tbl''bad')"));
    }

    [Test]
    public void AddColumn_MySQL_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.AddColumn("tbl'bad", "col'bad", c => c.ClrType("string").Length(50));

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("TABLE_NAME = 'tbl''bad'"));
        Assert.That(sql, Does.Contain("COLUMN_NAME = 'col''bad'"));
    }

    [Test]
    public void DropColumn_SqlServer_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.DropColumn("tbl'bad", "col'bad");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("OBJECT_ID('tbl''bad')"));
        Assert.That(sql, Does.Contain("name = 'col''bad'"));
    }

    [Test]
    public void AddIndex_SqlServer_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.AddIndex("idx'bad", "tbl'bad", ["col1"]);

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("name = 'idx''bad'"));
        Assert.That(sql, Does.Contain("OBJECT_ID('tbl''bad')"));
    }

    [Test]
    public void AddIndex_MySQL_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.AddIndex("idx'bad", "tbl'bad", ["col1"]);

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("TABLE_NAME = 'tbl''bad'"));
        Assert.That(sql, Does.Contain("INDEX_NAME = 'idx''bad'"));
    }

    [Test]
    public void DropIndex_SqlServer_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.DropIndex("idx'bad", "tbl'bad");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("name = 'idx''bad'"));
        Assert.That(sql, Does.Contain("OBJECT_ID('tbl''bad')"));
    }

    [Test]
    public void DropIndex_MySQL_Idempotent_EscapesIdentifiers()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.DropIndex("idx'bad", "tbl'bad");

        var sql = builder.BuildIdempotentSql(dialect);

        Assert.That(sql, Does.Contain("TABLE_NAME = 'tbl''bad'"));
        Assert.That(sql, Does.Contain("INDEX_NAME = 'idx''bad'"));
    }
}
