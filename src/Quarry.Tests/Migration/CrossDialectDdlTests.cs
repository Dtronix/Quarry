using Quarry.Migration;

namespace Quarry.Tests.Migration;

/// <summary>
/// Cross-dialect DDL generation tests verifying MigrationBuilder + DdlRenderer output
/// for all 4 supported SQL dialects.
/// </summary>
public class CrossDialectDdlTests
{
    private static readonly SqlDialect[] AllDialects =
    {
        SqlDialect.SQLite, SqlDialect.PostgreSQL, SqlDialect.MySQL, SqlDialect.SqlServer
    };

    private static readonly SqlDialect[] NonSQLiteDialects =
    {
        SqlDialect.PostgreSQL, SqlDialect.MySQL, SqlDialect.SqlServer
    };

    #region CREATE TABLE

    [TestCaseSource(nameof(AllDialects))]
    public void CreateTable_BasicWithPrimaryKey(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").Identity().NotNull());
            t.Column("name", c => c.ClrType("string").Length(100).NotNull());
            t.Column("email", c => c.ClrType("string").Length(255).Nullable());
            t.PrimaryKey("PK_users", "id");
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("NOT NULL").IgnoreCase);
        Assert.That(sql, Does.Contain("PRIMARY KEY").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void CreateTable_WithForeignKey(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateTable("posts", null, t =>
        {
            t.Column("id", c => c.ClrType("int").Identity().NotNull());
            t.Column("user_id", c => c.ClrType("int").NotNull());
            t.Column("title", c => c.ClrType("string").Length(200).NotNull());
            t.PrimaryKey("PK_posts", "id");
            t.ForeignKey("FK_posts_users", "user_id", "users", "id",
                ForeignKeyAction.Cascade, ForeignKeyAction.NoAction);
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("FOREIGN KEY").IgnoreCase);
        Assert.That(sql, Does.Contain("REFERENCES").IgnoreCase);
        Assert.That(sql, Does.Contain("CASCADE").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void CreateTable_AllCommonTypes(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.CreateTable("type_test", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("big_num", c => c.ClrType("long").NotNull());
            t.Column("flag", c => c.ClrType("bool").NotNull());
            t.Column("amount", c => c.ClrType("decimal").Precision(18, 2).NotNull());
            t.Column("name", c => c.ClrType("string").Length(100).NotNull());
            t.Column("notes", c => c.ClrType("string").Nullable());
            t.Column("uuid", c => c.ClrType("Guid").NotNull());
            t.Column("data", c => c.ClrType("byte[]").Nullable());
            t.Column("created", c => c.ClrType("DateTime").NotNull());
            t.Column("rating", c => c.ClrType("double").NotNull());
            t.PrimaryKey("PK_type_test", "id");
        });

        var sql = builder.BuildSql(dialect);

        // Should not throw and should produce valid SQL
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql.Length, Is.GreaterThan(100));
    }

    #endregion

    #region ALTER TABLE

    [TestCaseSource(nameof(AllDialects))]
    public void AddColumn_NullableString(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddColumn("users", "bio", c => c.ClrType("string").Nullable());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("ALTER TABLE").IgnoreCase);
        Assert.That(sql, Does.Contain("bio").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void AddColumn_WithDefault(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddColumn("users", "active", c => c.ClrType("bool").NotNull().DefaultExpression("1"));

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DEFAULT").IgnoreCase);
    }

    #endregion

    #region DROP TABLE

    [TestCaseSource(nameof(AllDialects))]
    public void DropTable_GeneratesDropStatement(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropTable("legacy_table");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP TABLE").IgnoreCase);
    }

    #endregion

    #region RENAME

    [TestCaseSource(nameof(AllDialects))]
    public void RenameTable_GeneratesDialectSpecificSyntax(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.RenameTable("old_name", "new_name");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Is.Not.Empty);
        // Each dialect handles rename differently
        if (dialectType == SqlDialect.SqlServer)
            Assert.That(sql, Does.Contain("sp_rename").IgnoreCase);
        else
            Assert.That(sql, Does.Contain("RENAME").IgnoreCase.Or.Contain("ALTER").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void RenameColumn_GeneratesDialectSpecificSyntax(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.RenameColumn("users", "old_col", "new_col");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Is.Not.Empty);
    }

    #endregion

    #region INDEX

    [TestCaseSource(nameof(AllDialects))]
    public void AddIndex_UniqueIndex(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddIndex("IX_users_email", "users", new[] { "email" }, unique: true);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE").IgnoreCase);
        Assert.That(sql, Does.Contain("UNIQUE").IgnoreCase);
        Assert.That(sql, Does.Contain("INDEX").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void AddIndex_CompositeIndex(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddIndex("IX_users_name_email", "users", new[] { "last_name", "email" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INDEX").IgnoreCase);
    }

    [TestCaseSource(nameof(AllDialects))]
    public void DropIndex_GeneratesDropStatement(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropIndex("IX_users_email", "users");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP INDEX").IgnoreCase);
    }

    #endregion

    #region FOREIGN KEY

    [TestCaseSource(nameof(NonSQLiteDialects))]
    public void AddForeignKey_WithCascadeDelete(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("FOREIGN KEY").IgnoreCase);
        Assert.That(sql, Does.Contain("CASCADE").IgnoreCase);
    }

    [Test]
    public void AddForeignKey_SQLite_Standalone_EmitsComment()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("-- SQLite does not support"));
    }

    [Test]
    public void AddForeignKey_SQLite_WithCreateTable_FoldsInline()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.CreateTable("posts", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("user_id", c => c.ClrType("int").NotNull());
            t.PrimaryKey("PK_posts", "id");
        });
        builder.AddForeignKey("FK_posts_users", "posts", "user_id", "users", "id",
            onDelete: ForeignKeyAction.Cascade);

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("FOREIGN KEY").IgnoreCase);
        Assert.That(sql, Does.Contain("CASCADE").IgnoreCase);
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }

    [TestCaseSource(nameof(NonSQLiteDialects))]
    public void DropForeignKey_GeneratesDropStatement(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DropForeignKey("FK_posts_users", "posts");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DROP CONSTRAINT"));
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

    #endregion

    #region RAW SQL

    [TestCaseSource(nameof(AllDialects))]
    public void RawSql_PassedThrough(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.Sql("INSERT INTO config (key, value) VALUES ('version', '1.0');");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO config"));
    }

    #endregion

    #region Combined operations

    [TestCaseSource(nameof(AllDialects))]
    public void FullMigration_CreateAndModify(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder
            .CreateTable("users", null, t =>
            {
                t.Column("id", c => c.ClrType("int").Identity().NotNull());
                t.Column("name", c => c.ClrType("string").Length(100).NotNull());
                t.PrimaryKey("PK_users", "id");
            })
            .CreateTable("posts", null, t =>
            {
                t.Column("id", c => c.ClrType("int").Identity().NotNull());
                t.Column("user_id", c => c.ClrType("int").NotNull());
                t.Column("title", c => c.ClrType("string").Length(200).NotNull());
                t.PrimaryKey("PK_posts", "id");
                t.ForeignKey("FK_posts_users", "user_id", "users", "id",
                    ForeignKeyAction.Cascade, ForeignKeyAction.NoAction);
            })
            .AddIndex("IX_posts_user_id", "posts", new[] { "user_id" });

        var sql = builder.BuildSql(dialect);

        // Should contain both CREATE TABLE statements and the index
        Assert.That(sql, Does.Contain("users").IgnoreCase);
        Assert.That(sql, Does.Contain("posts").IgnoreCase);
        Assert.That(sql, Does.Contain("INDEX").IgnoreCase);
    }

    #endregion

    #region Type-specific dialect output

    [Test]
    public void PostgreSQL_UsesCorrectTypes()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.CreateTable("test", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("flag", c => c.ClrType("bool").NotNull());
            t.Column("uuid", c => c.ClrType("Guid").NotNull());
            t.PrimaryKey("PK_test", "id");
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("integer").IgnoreCase);
        Assert.That(sql, Does.Contain("boolean").IgnoreCase);
        Assert.That(sql, Does.Contain("uuid").IgnoreCase);
    }

    [Test]
    public void SQLite_UsesSimpleTypes()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SQLite);
        var builder = new MigrationBuilder();

        builder.CreateTable("test", null, t =>
        {
            t.Column("id", c => c.ClrType("int").NotNull());
            t.Column("flag", c => c.ClrType("bool").NotNull());
            t.Column("uuid", c => c.ClrType("Guid").NotNull());
            t.PrimaryKey("PK_test", "id");
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INTEGER"));
        Assert.That(sql, Does.Contain("TEXT"));
    }

    [Test]
    public void SqlServer_UsesNVarchar()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.CreateTable("test", null, t =>
        {
            t.Column("name", c => c.ClrType("string").Length(100).NotNull());
            t.Column("notes", c => c.ClrType("string").Nullable());
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("NVARCHAR(100)"));
        Assert.That(sql, Does.Contain("NVARCHAR(MAX)"));
    }

    [Test]
    public void MySQL_UsesTinyIntForBool()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();

        builder.CreateTable("test", null, t =>
        {
            t.Column("flag", c => c.ClrType("bool").NotNull());
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("TINYINT(1)"));
    }

    #endregion
}
