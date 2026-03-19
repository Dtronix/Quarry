using Quarry;
using Quarry.Internal;
using System.Collections.Immutable;

namespace Quarry.Tests.Samples;

/// <summary>
/// Tests that demonstrate the generated code is working correctly.
/// These tests verify the generated context, entities, and metadata.
/// </summary>
[TestFixture]
public class GeneratedCodeTests
{
    /// <summary>
    /// Tests that Select with tuple projection generates correct SQL.
    /// This triggers interceptor generation for the Select method.
    /// Uses chained call to ensure analyzability.
    /// </summary>
    [Test]
    public void Select_Tuple_GeneratesCorrectSql()
    {
        using var connection = new MockDbConnection();
        using var db = new TestDbContext(connection);

        // Chained call - analyzable by the generator
        var sql = db.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("FROM"));
    }

    /// <summary>
    /// Tests that Where clause generates correct SQL.
    /// This now works thanks to syntactic pre-analysis with deferred translation.
    /// When semantic analysis fails (entity generated in same run), the generator
    /// parses the lambda syntactically and defers SQL translation to the enrichment
    /// phase when EntityInfo is available.
    /// </summary>
    [Test]
    public void Where_SimpleCondition_GeneratesCorrectSql()
    {
        using var connection = new MockDbConnection();
        using var db = new TestDbContext(connection);

        // Chained call - analyzable by the generator via syntactic fallback
        var sql = db.Users().Where(u => u.IsActive).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
    }

    /// <summary>
    /// Verifies that the generated User entity has the expected properties.
    /// </summary>
    [Test]
    public void GeneratedUser_HasExpectedProperties()
    {
        var user = new User
        {
            UserId = 1,
            UserName = "john",
            Email = "john@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        Assert.That(user.UserId, Is.EqualTo(1));
        Assert.That(user.UserName, Is.EqualTo("john"));
        Assert.That(user.Email, Is.EqualTo("john@example.com"));
        Assert.That(user.IsActive, Is.True);
    }

    /// <summary>
    /// Verifies that the generated Order entity has the foreign key reference.
    /// </summary>
    [Test]
    public void GeneratedOrder_HasForeignKeyRef()
    {
        var order = new Order
        {
            OrderId = 1,
            UserId = new EntityRef<User, int> { Id = 42 },
            Total = 99.99m,
            Status = "Pending"
        };

        Assert.That(order.UserId.Id, Is.EqualTo(42));
        Assert.That(order.Total, Is.EqualTo(99.99m));
    }

    /// <summary>
    /// Verifies that the generated context can build queries using QueryState directly.
    /// This bypasses interceptors but confirms the generated table names work.
    /// </summary>
    [Test]
    public void GeneratedContext_QueryBuilder_GeneratesCorrectSql()
    {
        // Use internal QueryState directly to verify SQL generation
        var state = new QueryState(SqlDialect.SQLite, "users", null, null)
            .WithSelect(ImmutableArray.Create("\"UserId\"", "\"UserName\""))
            .WithWhere("\"IsActive\" = 1")
            .WithLimit(10);

        var sql = SqlBuilder.BuildSelectSql(state);

        Assert.That(sql, Does.Contain("SELECT \"UserId\", \"UserName\""));
        Assert.That(sql, Does.Contain("FROM \"users\""));
        Assert.That(sql, Does.Contain("WHERE \"IsActive\" = 1"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

}
