namespace Quarry.Tests;

/// <summary>
/// Unit tests for InsertBuilder, UpdateBuilder, and DeleteBuilder.
/// These tests construct builders directly (not via a QuarryContext),
/// so the generator correctly cannot analyze them.
/// </summary>
[TestFixture]
public class ModificationBuilderTests
{
    #region InsertBuilder Tests

    [Test]
    public void InsertBuilder_ToSql_GeneratesBasicInsert()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.SetColumns(["\"id\"", "\"name\""]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"users\" (\"id\", \"name\")"));
    }

    [Test]
    public void InsertBuilder_ToSql_GeneratesInsertWithSchema()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", "public");
        builder.SetColumns(["\"id\"", "\"name\""]);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("INSERT INTO \"public\".\"users\" (\"id\", \"name\")"));
    }

    [Test]
    public void InsertBuilder_Values_AddEntityToBuilder()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        var entity = new TestEntity { Id = 1, Name = "Test" };

        var result = builder.Values(entity);

        Assert.That(result, Is.SameAs(builder)); // Mutable pattern
        Assert.That(builder.Entities, Has.Count.EqualTo(1));
    }

    [Test]
    public void InsertBuilder_Values_SupportsMultipleEntities()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        builder.Values(new TestEntity { Id = 1, Name = "One" });
        builder.Values(new TestEntity { Id = 2, Name = "Two" });
        builder.Values(new TestEntity { Id = 3, Name = "Three" });

        Assert.That(builder.Entities, Has.Count.EqualTo(3));
    }

    [Test]
    public void InsertBuilder_Values_ThrowsOnNull()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        Assert.Throws<ArgumentNullException>(() => builder.Values(null!));
    }

    [Test]
    public void InsertBuilder_WithTimeout_ReturnsSameInstance()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var result = builder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(result, Is.SameAs(builder)); // Mutable pattern
    }

    [Test]
    public void InsertBuilder_WithTimeout_ThrowsOnZero()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }

    [Test]
    public void InsertBuilder_WithTimeout_ThrowsOnNegative()
    {
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void InsertBuilder_ConstructorWithEntity_AddsEntity()
    {
        var entity = new TestEntity { Id = 1, Name = "Test" };
        var builder = new InsertBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null, null, entity);

        Assert.That(builder.Entities, Has.Count.EqualTo(1));
    }

    [Test]
    [TestCase("SQLite", "INSERT INTO \"users\"")]
    [TestCase("PostgreSQL", "INSERT INTO \"users\"")]
    [TestCase("MySQL", "INSERT INTO `users`")]
    [TestCase("SqlServer", "INSERT INTO [users]")]
    public void InsertBuilder_ToSql_UsesDialectSpecificQuoting(string dialectName, string expected)
    {
        var dialect = GetDialect(dialectName);
        var builder = new InsertBuilder<TestEntity>(dialect, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Does.StartWith(expected));
    }

    #endregion

    #region UpdateBuilder Tests

    [Test]
    public void UpdateBuilder_ToSql_GeneratesBasicUpdate()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.SQLite, "users", null);
        builder.AddSetClause("\"name\"", "NewName");

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"name\" = @p0"));
    }

    [Test]
    public void UpdateBuilder_ToSql_GeneratesUpdateWithSchema()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.SQLite, "users", "public");
        builder.AddSetClause("\"name\"", "NewName");

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("UPDATE \"public\".\"users\" SET \"name\" = @p0"));
    }

    [Test]
    public void UpdateBuilder_Set_ReturnsSameInstance()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var result = builder.Set(e => e.Name = "NewName");

        Assert.That(result, Is.SameAs(builder)); // Mutable pattern
    }

    [Test]
    public void UpdateBuilder_Where_ReturnsExecutableBuilder()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");

        var executableBuilder = builder.Where(e => e.Id == 1);

        Assert.That(executableBuilder, Is.Not.Null);
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableUpdateBuilder<TestEntity>>());
    }

    [Test]
    public void UpdateBuilder_All_ReturnsExecutableBuilder()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");

        var executableBuilder = builder.All();

        Assert.That(executableBuilder, Is.Not.Null);
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableUpdateBuilder<TestEntity>>());
    }

    [Test]
    public void UpdateBuilder_WithTimeout_ReturnsSameInstance()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var result = builder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(result, Is.SameAs(builder)); // Mutable pattern
    }

    [Test]
    public void UpdateBuilder_WithTimeout_ThrowsOnZero()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }

    [Test]
    public void UpdateBuilder_AddSetClause_AddsMultipleClauses()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.SQLite, "users", null);

        builder.AddSetClause("\"name\"", "NewName");
        builder.AddSetClause("\"id\"", 42);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("UPDATE \"users\" SET \"name\" = @p0, \"id\" = @p1"));
    }

    [Test]
    public void UpdateBuilder_AddWhereClause_GeneratesWhereClause()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");

        var executableBuilder = builder.AddWhereClause("\"id\" = @p1");

        var sql = executableBuilder.ToDiagnostics().Sql;
        Assert.That(sql, Does.Contain("WHERE \"id\" = @p1"));
    }

    [Test]
    [TestCase("SQLite", "UPDATE \"users\"")]
    [TestCase("PostgreSQL", "UPDATE \"users\"")]
    [TestCase("MySQL", "UPDATE `users`")]
    [TestCase("SqlServer", "UPDATE [users]")]
    public void UpdateBuilder_ToSql_UsesDialectSpecificQuoting(string dialectName, string expected)
    {
        var dialect = GetDialect(dialectName);
        var builder = new UpdateBuilder<TestEntity>(dialect, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Does.StartWith(expected));
    }

    #endregion

    #region ExecutableUpdateBuilder Tests

    [Test]
    public void ExecutableUpdateBuilder_Where_ReturnsSameInstance()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");
        var executableBuilder = builder.All();

        var result = executableBuilder.Where(e => e.Id == 1);

        Assert.That(result, Is.SameAs(executableBuilder)); // Mutable pattern
    }

    [Test]
    public void ExecutableUpdateBuilder_Set_ReturnsSameInstance()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");
        var executableBuilder = builder.All();

        var result = executableBuilder.Set(e => e.Name = "AnotherName");

        Assert.That(result, Is.SameAs(executableBuilder)); // Mutable pattern
    }

    [Test]
    public void ExecutableUpdateBuilder_WithTimeout_ReturnsSameInstance()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        builder.AddSetClause("\"name\"", "NewName");
        var executableBuilder = builder.All();

        var result = executableBuilder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(result, Is.SameAs(executableBuilder)); // Mutable pattern
    }

    [Test]
    public void ExecutableUpdateBuilder_ExecuteNonQueryAsync_ThrowsWithoutSetClauses()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        var executableBuilder = builder.All();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => executableBuilder.ExecuteNonQueryAsync());

        Assert.That(exception!.Message, Does.Contain("SET clause"));
    }

    #endregion

    #region DeleteBuilder Tests

    [Test]
    public void DeleteBuilder_ToSql_GeneratesBasicDelete()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\""));
    }

    [Test]
    public void DeleteBuilder_ToSql_GeneratesDeleteWithSchema()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", "public");

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo("DELETE FROM \"public\".\"users\""));
    }

    [Test]
    public void DeleteBuilder_Where_ReturnsExecutableBuilder()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var executableBuilder = builder.Where(e => e.Id == 1);

        Assert.That(executableBuilder, Is.Not.Null);
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableDeleteBuilder<TestEntity>>());
    }

    [Test]
    public void DeleteBuilder_All_ReturnsExecutableBuilder()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var executableBuilder = builder.All();

        Assert.That(executableBuilder, Is.Not.Null);
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableDeleteBuilder<TestEntity>>());
    }

    [Test]
    public void DeleteBuilder_WithTimeout_ReturnsSameInstance()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var result = builder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(result, Is.SameAs(builder)); // Mutable pattern
    }

    [Test]
    public void DeleteBuilder_WithTimeout_ThrowsOnZero()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTimeout(TimeSpan.Zero));
    }

    [Test]
    public void DeleteBuilder_AddWhereClause_GeneratesWhereClause()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        var executableBuilder = builder.AddWhereClause("\"id\" = @p0");

        var sql = executableBuilder.ToDiagnostics().Sql;
        Assert.That(sql, Is.EqualTo("DELETE FROM \"users\" WHERE \"id\" = @p0"));
    }

    [Test]
    [TestCase("SQLite", "DELETE FROM \"users\"")]
    [TestCase("PostgreSQL", "DELETE FROM \"users\"")]
    [TestCase("MySQL", "DELETE FROM `users`")]
    [TestCase("SqlServer", "DELETE FROM [users]")]
    public void DeleteBuilder_ToSql_UsesDialectSpecificQuoting(string dialectName, string expected)
    {
        var dialect = GetDialect(dialectName);
        var builder = new DeleteBuilder<TestEntity>(dialect, "users", null);

        var sql = builder.ToDiagnostics().Sql;

        Assert.That(sql, Is.EqualTo(expected));
    }

    #endregion

    #region ExecutableDeleteBuilder Tests

    [Test]
    public void ExecutableDeleteBuilder_Where_ReturnsSameInstance()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        var executableBuilder = builder.All();

        var result = executableBuilder.Where(e => e.Id == 1);

        Assert.That(result, Is.SameAs(executableBuilder)); // Mutable pattern
    }

    [Test]
    public void ExecutableDeleteBuilder_WithTimeout_ReturnsSameInstance()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);
        var executableBuilder = builder.All();

        var result = executableBuilder.WithTimeout(TimeSpan.FromSeconds(30));

        Assert.That(result, Is.SameAs(executableBuilder)); // Mutable pattern
    }

    #endregion

    #region Safety Guard Tests

    [Test]
    public void UpdateBuilder_TypeSafetyGuard_NoExecuteWithoutWhereOrAll()
    {
        var builder = new UpdateBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        // This should NOT compile:
        // builder.ExecuteNonQueryAsync();

        // Only after Where() or All() should execution be possible
        var executableBuilder = builder.Where(e => e.Id == 1);

        // Now ExecuteNonQueryAsync is accessible (would need execution context to run)
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableUpdateBuilder<TestEntity>>());
    }

    [Test]
    public void DeleteBuilder_TypeSafetyGuard_NoExecuteWithoutWhereOrAll()
    {
        var builder = new DeleteBuilder<TestEntity>(SqlDialect.PostgreSQL, "users", null);

        // This should NOT compile:
        // builder.ExecuteNonQueryAsync();

        // Only after Where() or All() should execution be possible
        var executableBuilder = builder.Where(e => e.Id == 1);

        // Now ExecuteNonQueryAsync is accessible (would need execution context to run)
        Assert.That(executableBuilder, Is.InstanceOf<ExecutableDeleteBuilder<TestEntity>>());
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
    }
}
