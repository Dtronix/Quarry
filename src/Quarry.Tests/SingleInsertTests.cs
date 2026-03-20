using Quarry.Internal;
using Quarry.Shared.Sql;


namespace Quarry.Tests;

/// <summary>
/// Unit tests for single insert SQL generation.
/// </summary>
[TestFixture]
public class SingleInsertTests
{
    #region SQL Generation Tests

    [Test]
    public void InsertBuilder_ToSql_GeneratesBasicInsertWithColumns()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\"", "\"email\""]);

        // Add a single row with parameter indices
        builder.AddRow([0, 1]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"name\", \"email\") VALUES (@p0, @p1)"));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesInsertWithSchema()
    {
        // Use SQLite to test schema - PostgreSQL uses $1 parameters
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", "public");
        builder.SetColumns(["\"name\""]);
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"public\".\"users\" (\"name\") VALUES (@p0)"));
    }

    [Test]
    [TestCase("SQLite", "INSERT INTO \"users\" (\"name\") VALUES (@p0)")]
    [TestCase("PostgreSQL", "INSERT INTO \"users\" (\"name\") VALUES ($1)")]
    [TestCase("MySQL", "INSERT INTO `users` (`name`) VALUES (?)")]
    [TestCase("SqlServer", "INSERT INTO [users] ([name]) VALUES (@p0)")]
    public void InsertBuilder_ToSql_UsesDialectSpecificSyntax(string dialectName, string expected)
    {
        var dialect = GetDialect(dialectName);
        var builder = new InsertBuilder<TestEntity>(dialect, "users", null);

        // Use dialect-specific column quoting
        var quotedColumn = SqlFormatting.QuoteIdentifier(dialect, "name");
        builder.SetColumns([quotedColumn]);
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(expected));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesReturningClause_SQLite()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\""]);
        // Identity column name should be unquoted - dialect handles quoting
        builder.SetIdentityColumn("id");
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"name\") VALUES (@p0) RETURNING \"id\""));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesReturningClause_PostgreSQL()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.SetColumns(["\"name\""]);
        // Identity column name should be unquoted - dialect handles quoting
        builder.SetIdentityColumn("id");
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"name\") VALUES ($1) RETURNING \"id\""));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesOutputClause_SqlServer()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SqlServer, "users", null);
        builder.SetColumns(["[name]"]);
        // Identity column name should be unquoted - dialect handles quoting
        builder.SetIdentityColumn("id");
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        // Note: SQL Server OUTPUT clause is appended after VALUES in the current implementation
        Assert.That(sql, Is.EqualTo("INSERT INTO [users] ([name]) VALUES (@p0) OUTPUT INSERTED.[id]"));
    }

    [Test]
    public void InsertBuilder_ToSql_MySqlNoReturning()
    {
        // MySQL doesn't support RETURNING - uses separate LAST_INSERT_ID() call
        var builder = new InsertBuilder<TestEntity>(SqlDialect.MySQL, "users", null);
        builder.SetColumns(["`name`"]);
        // Identity column name should be unquoted
        builder.SetIdentityColumn("id");
        builder.AddRow([0]);

        var sql = builder.ToDiagnostics().Sql;

        // MySQL doesn't include RETURNING in the INSERT - it uses SELECT LAST_INSERT_ID() after
        Assert.That(sql, Is.EqualTo("INSERT INTO `users` (`name`) VALUES (?)"));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesMultiRowInsert()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\"", "\"email\""]);

        // Add multiple rows
        builder.AddRow([0, 1]);
        builder.AddRow([2, 3]);
        builder.AddRow([4, 5]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"name\", \"email\") VALUES (@p0, @p1), (@p2, @p3), (@p4, @p5)"));
    }

    #endregion

    #region Parameter Tests

    [Test]
    public void InsertBuilder_AddParameter_AddsParameterToState()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);

        var idx0 = builder.AddParameter("John");
        var idx1 = builder.AddParameter("john@example.com");
        var idx2 = builder.AddParameter(42);

        Assert.That(idx0, Is.EqualTo(0));
        Assert.That(idx1, Is.EqualTo(1));
        Assert.That(idx2, Is.EqualTo(2));

        Assert.That(builder.State.Parameters, Has.Count.EqualTo(3));
        Assert.That(builder.State.Parameters[0].Value, Is.EqualTo("John"));
        Assert.That(builder.State.Parameters[1].Value, Is.EqualTo("john@example.com"));
        Assert.That(builder.State.Parameters[2].Value, Is.EqualTo(42));
    }

    [Test]
    public void InsertBuilder_AddParameterBoxed_AddsParameterToState()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);

        var idx0 = builder.AddParameterBoxed("John");
        var idx1 = builder.AddParameterBoxed(null);
        var idx2 = builder.AddParameterBoxed(true);

        Assert.That(idx0, Is.EqualTo(0));
        Assert.That(idx1, Is.EqualTo(1));
        Assert.That(idx2, Is.EqualTo(2));

        Assert.That(builder.State.Parameters, Has.Count.EqualTo(3));
        Assert.That(builder.State.Parameters[0].Value, Is.EqualTo("John"));
        Assert.That(builder.State.Parameters[1].Value, Is.Null);
        Assert.That(builder.State.Parameters[2].Value, Is.EqualTo(true));
    }

    #endregion

    #region Identity Column Tests

    [Test]
    public void InsertBuilder_SetIdentityColumn_SetsIdentityOnState()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);

        builder.SetIdentityColumn("\"user_id\"");

        Assert.That(builder.State.IdentityColumn, Is.EqualTo("\"user_id\""));
    }

    [Test]
    public void SqlModificationBuilder_GetLastInsertIdQuery_ReturnsCorrectQuery()
    {
        // SQLite uses RETURNING, no separate query needed
        Assert.That(SqlModificationBuilder.GetLastInsertIdQuery(SqlDialect.SQLite), Is.Null);

        // PostgreSQL uses RETURNING, no separate query needed
        Assert.That(SqlModificationBuilder.GetLastInsertIdQuery(SqlDialect.PostgreSQL), Is.Null);

        // MySQL uses separate query
        Assert.That(SqlModificationBuilder.GetLastInsertIdQuery(SqlDialect.MySQL), Is.EqualTo("SELECT LAST_INSERT_ID()"));

        // SQL Server uses OUTPUT, no separate query needed
        Assert.That(SqlModificationBuilder.GetLastInsertIdQuery(SqlDialect.SqlServer), Is.Null);
    }

    #endregion

    #region State Tests

    [Test]
    public void InsertState_HasColumns_ReturnsTrueWhenColumnsSet()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);

        Assert.That(state.HasColumns, Is.False);

        state.Columns.AddRange(["\"name\"", "\"email\""]);

        Assert.That(state.HasColumns, Is.True);
    }

    [Test]
    public void InsertState_AddParameter_TracksParameterIndices()
    {
        var state = new InsertState(SqlDialect.SQLite, "users", null, null);

        var idx0 = state.AddParameter("value1");
        var idx1 = state.AddParameter(42);
        var idx2 = state.AddParameter(true);

        Assert.That(idx0, Is.EqualTo(0));
        Assert.That(idx1, Is.EqualTo(1));
        Assert.That(idx2, Is.EqualTo(2));
        Assert.That(state.Parameters, Has.Count.EqualTo(3));
    }

    #endregion

    #region ToSql Verification Tests

    [Test]
    public void ToSql_SingleEntityInsert_SQLite_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\"", "\"email\""]);
        builder.AddParameter("John");
        builder.AddParameter("john@example.com");
        builder.AddRow([0, 1]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert - Verify exact SQL matches expected format
        const string expected = "INSERT INTO \"users\" (\"name\", \"email\") VALUES (@p0, @p1)";
        Assert.That(sql, Is.EqualTo(expected), "SQLite INSERT SQL should use double-quote identifiers and @p0 parameters");
    }

    [Test]
    public void ToSql_SingleEntityInsert_PostgreSQL_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.SetColumns(["\"name\"", "\"email\""]);
        builder.AddParameter("John");
        builder.AddParameter("john@example.com");
        builder.AddRow([0, 1]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert - Verify exact SQL matches expected format
        const string expected = "INSERT INTO \"users\" (\"name\", \"email\") VALUES ($1, $2)";
        Assert.That(sql, Is.EqualTo(expected), "PostgreSQL INSERT SQL should use double-quote identifiers and $1 parameters");
    }

    [Test]
    public void ToSql_SingleEntityInsert_MySQL_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.MySQL, "users", null);
        builder.SetColumns(["`name`", "`email`"]);
        builder.AddParameter("John");
        builder.AddParameter("john@example.com");
        builder.AddRow([0, 1]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert - Verify exact SQL matches expected format
        const string expected = "INSERT INTO `users` (`name`, `email`) VALUES (?, ?)";
        Assert.That(sql, Is.EqualTo(expected), "MySQL INSERT SQL should use backtick identifiers and ? parameters");
    }

    [Test]
    public void ToSql_SingleEntityInsert_SqlServer_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SqlServer, "users", null);
        builder.SetColumns(["[name]", "[email]"]);
        builder.AddParameter("John");
        builder.AddParameter("john@example.com");
        builder.AddRow([0, 1]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert - Verify exact SQL matches expected format
        const string expected = "INSERT INTO [users] ([name], [email]) VALUES (@p0, @p1)";
        Assert.That(sql, Is.EqualTo(expected), "SQL Server INSERT SQL should use bracket identifiers and @p0 parameters");
    }

    [Test]
    public void ToSql_InsertWithIdentityReturn_SQLite_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\""]);
        builder.SetIdentityColumn("id");  // Unquoted - dialect will quote
        builder.AddParameter("John");
        builder.AddRow([0]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert
        const string expected = "INSERT INTO \"users\" (\"name\") VALUES (@p0) RETURNING \"id\"";
        Assert.That(sql, Is.EqualTo(expected), "SQLite should use RETURNING clause for identity");
    }

    [Test]
    public void ToSql_InsertWithIdentityReturn_PostgreSQL_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.SetColumns(["\"name\""]);
        builder.SetIdentityColumn("id");  // Unquoted - dialect will quote
        builder.AddParameter("John");
        builder.AddRow([0]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert
        const string expected = "INSERT INTO \"users\" (\"name\") VALUES ($1) RETURNING \"id\"";
        Assert.That(sql, Is.EqualTo(expected), "PostgreSQL should use RETURNING clause for identity");
    }

    [Test]
    public void ToSql_InsertWithIdentityReturn_MySQL_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.MySQL, "users", null);
        builder.SetColumns(["`name`"]);
        builder.SetIdentityColumn("id");  // MySQL doesn't use RETURNING
        builder.AddParameter("John");
        builder.AddRow([0]);

        // Act
        var sql = builder.ToDiagnostics().Sql;
        var lastInsertIdQuery = SqlModificationBuilder.GetLastInsertIdQuery(SqlDialect.MySQL);

        // Assert - MySQL doesn't include RETURNING, uses separate query
        const string expectedInsert = "INSERT INTO `users` (`name`) VALUES (?)";
        const string expectedLastInsertId = "SELECT LAST_INSERT_ID()";
        Assert.That(sql, Is.EqualTo(expectedInsert), "MySQL INSERT should not have RETURNING clause");
        Assert.That(lastInsertIdQuery, Is.EqualTo(expectedLastInsertId), "MySQL should use SELECT LAST_INSERT_ID()");
    }

    [Test]
    public void ToSql_InsertWithIdentityReturn_SqlServer_MatchesExpected()
    {
        // Arrange
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SqlServer, "users", null);
        builder.SetColumns(["[name]"]);
        builder.SetIdentityColumn("id");  // Unquoted - dialect will quote
        builder.AddParameter("John");
        builder.AddRow([0]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert
        const string expected = "INSERT INTO [users] ([name]) VALUES (@p0) OUTPUT INSERTED.[id]";
        Assert.That(sql, Is.EqualTo(expected), "SQL Server should use OUTPUT INSERTED clause for identity");
    }

    [Test]
    public void ToSql_InsertWithSchema_AllDialects_MatchesExpected()
    {
        // SQLite with schema
        var sqliteBuilder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", "main");
        sqliteBuilder.SetColumns(["\"name\""]);
        sqliteBuilder.AddRow([0]);
        Assert.That(sqliteBuilder.ToDiagnostics().Sql, Is.EqualTo("INSERT INTO \"main\".\"users\" (\"name\") VALUES (@p0)"));

        // PostgreSQL with schema
        var pgBuilder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", "public");
        pgBuilder.SetColumns(["\"name\""]);
        pgBuilder.AddRow([0]);
        Assert.That(pgBuilder.ToDiagnostics().Sql, Is.EqualTo("INSERT INTO \"public\".\"users\" (\"name\") VALUES ($1)"));

        // MySQL with schema (database name)
        var mysqlBuilder = new InsertBuilder<TestEntity>(SqlDialect.MySQL, "users", "mydb");
        mysqlBuilder.SetColumns(["`name`"]);
        mysqlBuilder.AddRow([0]);
        Assert.That(mysqlBuilder.ToDiagnostics().Sql, Is.EqualTo("INSERT INTO `mydb`.`users` (`name`) VALUES (?)"));

        // SQL Server with schema
        var sqlServerBuilder = new InsertBuilder<TestEntity>(SqlDialect.SqlServer, "users", "dbo");
        sqlServerBuilder.SetColumns(["[name]"]);
        sqlServerBuilder.AddRow([0]);
        Assert.That(sqlServerBuilder.ToDiagnostics().Sql, Is.EqualTo("INSERT INTO [dbo].[users] ([name]) VALUES (@p0)"));
    }

    [Test]
    public void ToSql_BatchInsert_MatchesExpected()
    {
        // Arrange - 3 entities in a batch
        var builder = new InsertBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.SetColumns(["\"name\"", "\"email\""]);

        // Add 3 rows
        builder.AddParameter("John");
        builder.AddParameter("john@example.com");
        builder.AddRow([0, 1]);

        builder.AddParameter("Jane");
        builder.AddParameter("jane@example.com");
        builder.AddRow([2, 3]);

        builder.AddParameter("Bob");
        builder.AddParameter("bob@example.com");
        builder.AddRow([4, 5]);

        // Act
        var sql = builder.ToDiagnostics().Sql;

        // Assert
        const string expected = "INSERT INTO \"users\" (\"name\", \"email\") VALUES (@p0, @p1), (@p2, @p3), (@p4, @p5)";
        Assert.That(sql, Is.EqualTo(expected), "Batch insert should use multi-row VALUES syntax");
    }

    #endregion

    #region Helper Methods

    private static SqlDialect GetDialect(string dialectName)
    {
        return dialectName switch
        {
            "SQLite" => SqlDialect.SQLite,
            "PostgreSQL" => SqlDialect.PostgreSQL,
            "MySQL" => SqlDialect.MySQL,
            "SqlServer" => SqlDialect.SqlServer,
            _ => throw new ArgumentException($"Unknown dialect: {dialectName}", nameof(dialectName))
        };
    }

    #endregion

    // Test entity class
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
    }
}
