using Quarry.Migration;

namespace Quarry.Tests.Migration;

public class MigrationBuilderDataOperationTests
{
    // --- InsertData ---

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_SingleRow_GeneratesInsert(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { UserName = "admin", Email = "admin@example.com" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("'admin'"));
        Assert.That(sql, Does.Contain("'admin@example.com'"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_MultipleRows_GeneratesMultiRowValues(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new object[]
        {
            new { UserName = "admin", Email = "admin@example.com" },
            new { UserName = "system", Email = "system@example.com" }
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("'admin'"));
        Assert.That(sql, Does.Contain("'system'"));
        Assert.That(sql, Does.Contain("VALUES"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { Id = 1 }, schema: "dbo");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("dbo"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_WithNullValue_GeneratesNull(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { UserName = "admin", Email = (string?)null });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("NULL"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_WithBoolValue_FormatsPerDialect(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { IsActive = true });

        var sql = builder.BuildSql(dialect);

        if (dialectType == SqlDialect.PostgreSQL)
            Assert.That(sql, Does.Contain("TRUE"));
        else
            Assert.That(sql, Does.Contain("1"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_WithIntValue_FormatsAsLiteral(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { Id = 42, Name = "test" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("42"));
    }

    [Test]
    public void InsertData_EmptyRows_ThrowsArgumentException()
    {
        var builder = new MigrationBuilder();
        Assert.Throws<ArgumentException>(() => builder.InsertData("users", Array.Empty<object>()));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void InsertData_QuotesColumnNames(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { user_name = "admin" });

        var sql = builder.BuildSql(dialect);

        // Column name should be quoted per dialect
        switch (dialectType)
        {
            case SqlDialect.MySQL:
                Assert.That(sql, Does.Contain("`user_name`"));
                break;
            case SqlDialect.SqlServer:
                Assert.That(sql, Does.Contain("[user_name]"));
                break;
            default: // SQLite, PostgreSQL
                Assert.That(sql, Does.Contain("\"user_name\""));
                break;
        }
    }

    // --- UpdateData ---

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void UpdateData_GeneratesUpdate(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { IsActive = false },
            where: new { UserName = "legacy" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("'legacy'"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void UpdateData_MultipleSetColumns_GeneratesCommaSeparated(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { IsActive = false, Email = "updated@example.com" },
            where: new { Id = 1 });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("'updated@example.com'"));

        if (dialectType == SqlDialect.PostgreSQL)
            Assert.That(sql, Does.Contain("FALSE"));
        else
            Assert.That(sql, Does.Contain("0"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void UpdateData_MultipleWhereConditions_UsesAnd(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { IsActive = false },
            where: new { UserName = "legacy", TenantId = 5 });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("AND"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void UpdateData_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { IsActive = false },
            where: new { Id = 1 },
            schema: "dbo");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("dbo"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void UpdateData_SetNull_GeneratesNull(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { Email = (string?)null },
            where: new { Id = 1 });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("= NULL"));
    }

    // --- DeleteData ---

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DeleteData_GeneratesDelete(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DeleteData("users",
            where: new { UserName = "legacy" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DELETE FROM"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("'legacy'"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DeleteData_WithSchema_IncludesSchema(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DeleteData("users",
            where: new { Id = 1 },
            schema: "dbo");

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("dbo"));
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void DeleteData_NullWhereValue_UsesIsNull(SqlDialect dialectType)
    {
        var dialect = SqlDialectFactory.GetDialect(dialectType);
        var builder = new MigrationBuilder();

        builder.DeleteData("users",
            where: new { Email = (string?)null });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("IS NULL"));
        Assert.That(sql, Does.Not.Contain("= NULL"));
    }

    // --- Fluent chaining ---

    [Test]
    public void DataOperations_SupportFluentChaining()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        var result = builder
            .InsertData("users", new { Id = 1, Name = "admin" })
            .UpdateData("users", set: new { Name = "superadmin" }, where: new { Id = 1 })
            .DeleteData("users", where: new { Id = 99 });

        Assert.That(result, Is.SameAs(builder));

        var sql = builder.BuildSql(dialect);
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("DELETE FROM"));
    }

    [Test]
    public void DataOperations_SupportSuppressTransaction()
    {
        var builder = new MigrationBuilder();
        builder.InsertData("users", new { Id = 1 }).SuppressTransaction();

        var ops = builder.GetOperations();
        Assert.That(ops[0].SuppressTransaction, Is.True);
    }

    // --- Specific SQL format verification ---

    [Test]
    public void InsertData_PostgreSQL_GeneratesCorrectSql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new object[]
        {
            new { user_name = "admin", email = "admin@example.com", is_active = true },
            new { user_name = "system", email = "system@example.com", is_active = true }
        });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO \"users\" (\"user_name\", \"email\", \"is_active\") VALUES"));
        Assert.That(sql, Does.Contain("('admin', 'admin@example.com', TRUE)"));
        Assert.That(sql, Does.Contain("('system', 'system@example.com', TRUE)"));
    }

    [Test]
    public void UpdateData_PostgreSQL_GeneratesCorrectSql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.UpdateData("users",
            set: new { is_active = false },
            where: new { user_name = "legacy" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("UPDATE \"users\" SET \"is_active\" = FALSE WHERE \"user_name\" = 'legacy';"));
    }

    [Test]
    public void DeleteData_PostgreSQL_GeneratesCorrectSql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.DeleteData("users",
            where: new { user_name = "legacy" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("DELETE FROM \"users\" WHERE \"user_name\" = 'legacy';"));
    }

    [Test]
    public void InsertData_SqlServer_GeneratesCorrectSql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.SqlServer);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { Id = 1, UserName = "admin", IsActive = true });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO [users] ([Id], [UserName], [IsActive]) VALUES"));
        Assert.That(sql, Does.Contain("(1, 'admin', 1)"));
    }

    [Test]
    public void InsertData_MySQL_GeneratesCorrectSql()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { Id = 1, UserName = "admin" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("INSERT INTO `users` (`Id`, `UserName`) VALUES"));
        Assert.That(sql, Does.Contain("(1, 'admin')"));
    }

    // --- Value type formatting ---

    [Test]
    public void InsertData_DateTimeValue_FormatsAsString()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.InsertData("events", new { CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0) });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("'2026-01-15 10:30:00'"));
    }

    [Test]
    public void InsertData_GuidValue_FormatsAsString()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();
        var guid = new Guid("12345678-1234-1234-1234-123456789abc");

        builder.InsertData("users", new { Id = guid });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("'12345678-1234-1234-1234-123456789abc'"));
    }

    [Test]
    public void InsertData_DecimalValue_FormatsAsLiteral()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.InsertData("products", new { Price = 19.99m });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("19.99"));
    }

    [Test]
    public void InsertData_StringWithSingleQuote_Escapes()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.InsertData("users", new { Name = "O'Brien" });

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("'O''Brien'"));
    }

    // --- Mixed with DDL operations ---

    [Test]
    public void DataOperations_InterleavedWithDdl_AllRendered()
    {
        var dialect = SqlDialectFactory.GetDialect(SqlDialect.PostgreSQL);
        var builder = new MigrationBuilder();

        builder.CreateTable("users", null, t =>
        {
            t.Column("id", c => c.ClrType("int").Identity().NotNull());
            t.Column("name", c => c.ClrType("string").Length(100).NotNull());
            t.PrimaryKey("PK_users", "id");
        });
        builder.InsertData("users", new { name = "admin" });
        builder.AddColumn("users", "email", c => c.ClrType("string").Length(255).Nullable());

        var sql = builder.BuildSql(dialect);

        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("INSERT INTO"));
        Assert.That(sql, Does.Contain("ALTER TABLE"));
    }

    // --- BuildPartitionedSql with data operations ---

    [Test]
    public void DataOperations_PartitionedSql_SuppressedGoesToNonTransactional()
    {
        var builder = new MigrationBuilder();
        builder.InsertData("users", new { Id = 1 });
        builder.InsertData("logs", new { Id = 1 }).SuppressTransaction();

        var (txSql, nonTxSql, allSql) = builder.BuildPartitionedSql(SqlDialect.PostgreSQL);

        Assert.That(txSql, Does.Contain("\"users\""));
        Assert.That(txSql, Does.Not.Contain("\"logs\""));
        Assert.That(nonTxSql, Does.Contain("\"logs\""));
        Assert.That(nonTxSql, Does.Not.Contain("\"users\""));
        Assert.That(allSql, Does.Contain("\"users\""));
        Assert.That(allSql, Does.Contain("\"logs\""));
    }
}
