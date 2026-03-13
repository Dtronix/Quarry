using Quarry.Migration;
using ForeignKeyAction = Quarry.Migration.ForeignKeyAction;

namespace Quarry.Tests.Migration;

public class MigrationBuilderTests
{
    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void CreateTable_GeneratesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").Identity().NotNull());
            t.Column("name", c => c.ClrType("string").Length(100).NotNull());
            t.PrimaryKey("PK_users", "id");
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("users").IgnoreCase);
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropTable_GeneratesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropTable("users");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP TABLE"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddColumn_GeneratesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("email").IgnoreCase);
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropColumn_GeneratesValidDdl(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropColumn("users", "legacy_field");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Is.Not.Empty);
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void AddForeignKey_NonSQLite_GeneratesAlterTable(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("FOREIGN KEY"));
        Assert.That(sql, Does.Contain("CASCADE"));
    }

    [Test]
    public void AddForeignKey_SQLite_Standalone_EmitsComment()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("-- SQLite does not support"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }

    [Test]
    public void AddForeignKey_SQLite_FoldsIntoCreateTable()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        builder.CreateTable("posts", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("user_id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_posts", "id");
        });
        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        // FK should be inline in CREATE TABLE, not a standalone ALTER TABLE
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("FOREIGN KEY"));
        Assert.That(sql, Does.Contain("REFERENCES"));
        Assert.That(sql, Does.Contain("CASCADE"));
    }

    [Test]
    public void DropForeignKey_SQLite_EmitsComment()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.DropForeignKey("FK_posts_users", "posts");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("-- SQLite does not support"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }

    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DropForeignKey_NonSQLite_GeneratesAlterTable(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropForeignKey("FK_posts_users", "posts");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Contain("DROP CONSTRAINT"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void RawSql_IncludesVerbatim(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.Sql("UPDATE users SET active = 1 WHERE active IS NULL;");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("UPDATE users SET active = 1 WHERE active IS NULL;"));
    }

    [Test]
    public void FluentChaining_Works()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        var result = builder
            .CreateTable("users", null, t =>
            {
                t.Column("id", c => c.ClrType("int").Identity().NotNull());
                t.PrimaryKey("PK_users", "id");
            })
            .AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable())
            .AddIndex("IX_users_email", "users", new[] { "email" }, unique: true);

        Assert.That(result, Is.SameAs(builder));

        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("email"));
        Assert.That(sql, Does.Contain("INDEX"));
    }
}
