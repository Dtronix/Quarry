using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationBuilderStrategyTests
{
    [Test]
    public void Online_MySQL_EmitsAlgorithmInplace()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable()).Online();

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALGORITHM=INPLACE"));
    }

    [Test]
    public void Online_SqlServer_EmitsOnlineOn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull()).Online();

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("WITH (ONLINE = ON)"));
    }

    [Test]
    public void Online_PostgreSQL_NoExtraClause()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable()).Online();

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Not.Contain("ALGORITHM"));
        Assert.That(sql, Does.Not.Contain("ONLINE"));
    }

    [Test]
    public void ConcurrentIndex_PostgreSQL_EmitsConcurrently()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AddIndex("IX_users_email", "users", new[] { "email" }).ConcurrentIndex();

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CONCURRENTLY"));
    }

    [Test]
    public void EmptyBuilder_ReturnsEmptyString()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Is.Empty);
    }

    [Test]
    public void CreateTable_WithSchema_EmitsQualifiedName()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.CreateTable("users", "dbo", t =>
        {
            t.Column("id", c => c.ClrType("int").Identity().NotNull());
            t.PrimaryKey("PK_users", "id");
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("[dbo].[users]"));
    }

    [Test]
    public void AlterColumn_PostgreSQL_EmitsAlterColumnType()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER COLUMN"));
        Assert.That(sql, Does.Contain("TYPE"));
    }

    [Test]
    public void AlterColumn_MySQL_EmitsModifyColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("MODIFY COLUMN"));
    }

    [Test]
    public void AlterColumn_SqlServer_EmitsAlterColumn()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();
        builder.AlterColumn("users", "name", c => c.ClrType("string").Length(200).NotNull());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER COLUMN"));
    }
}
