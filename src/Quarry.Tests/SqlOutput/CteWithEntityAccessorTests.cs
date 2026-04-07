using Quarry.Tests.Samples;
using Cte = Quarry.Tests.Samples.Cte;

namespace Quarry.Tests.SqlOutput;

/// <summary>
/// Integration tests for CTE chains that combine With&lt;TDto&gt;() with entity accessors
/// and further builder methods (Join, Where, Select). These chains require the context
/// to inherit from <see cref="QuarryContext{TSelf}"/> so that the source generator's
/// discovery SemanticModel can resolve the return type of With&lt;&gt; as the concrete
/// context class. Regression tests for issue #205.
/// </summary>
[TestFixture]
internal class CteWithEntityAccessorTests
{
    [Test]
    public async Task Cte_Users_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > 100))
            .Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"UserId\", \"UserName\" FROM \"users\""));

        // Seed data: 3 users (Alice, Bob, Charlie)
        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
    }

    [Test]
    public async Task Cte_Users_Where_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > 100))
            .Users()
            .Where(u => u.IsActive == true)
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));

        // Seed data: Active users are Alice (1) and Bob (2); Charlie is inactive
        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
    }

    [Test]
    public async Task Cte_Users_Join_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        // Core regression target: With<> followed by entity accessor + Join + Select
        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > 100))
            .Users()
            .Join<Cte.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT u.\"UserName\", o.\"Total\" FROM \"users\" u INNER JOIN \"Order\" o ON u.\"UserId\" = o.\"UserId\""));

        // Seed data: Orders with Total > 100: OrderId=1 (Alice, 250.00), OrderId=3 (Bob, 150.00)
        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
        Assert.That(results[1], Is.EqualTo(("Bob", 150.00m)));
    }

    [Test]
    public async Task Cte_Users_Join_Where_Select()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > 100))
            .Users()
            .Join<Cte.Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 200)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT u.\"UserName\", o.\"Total\" FROM \"users\" u INNER JOIN \"Order\" o ON u.\"UserId\" = o.\"UserId\" WHERE o.\"Total\" > 200"));

        // Only Alice's order (250.00) exceeds 200
        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(("Alice", 250.00m)));
    }

    [Test]
    public async Task Cte_Users_Join_Select_WithParameter()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        decimal cutoff = 100m;
        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > cutoff))
            .Users()
            .Join<Cte.Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .Prepare();

        // Verify parameterized SQL
        Assert.That(q.ToDiagnostics().Sql, Does.Contain("@p0"));

        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Cte_FromCte_StillWorks_OnGenericBase()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        // Regression guard: the existing With + FromCte pattern still works on the generic base
        var q = cte.With<Cte.Order>(cte.Orders().Where(o => o.Total > 100))
            .FromCte<Cte.Order>()
            .Select(o => (o.OrderId, o.Total))
            .Prepare();

        Assert.That(q.ToDiagnostics().Sql, Is.EqualTo(
            "WITH \"Order\" AS (SELECT \"OrderId\", \"UserId\", \"Total\", \"Status\", \"Priority\", \"OrderDate\", \"Notes\" FROM \"orders\" WHERE \"Total\" > 100) SELECT \"OrderId\", \"Total\" FROM \"Order\""));

        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo((1, 250.00m)));
        Assert.That(results[1], Is.EqualTo((3, 150.00m)));
    }

    [Test]
    public async Task Cte_ProjectedInnerQuery_Users_Join()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var cte = new Cte.CteDb(t.Lite.Connection);

        // Two-parameter With<TEntity, TDto> overload with projected inner query
        var innerQuery = cte.Orders().Where(o => o.Total > 100)
            .Select(o => new OrderSummaryDto { OrderId = o.OrderId, Total = o.Total, Status = o.Status });
        var q = cte.With<Cte.Order, OrderSummaryDto>(innerQuery)
            .Users()
            .Select(u => (u.UserId, u.UserName))
            .Prepare();

        // CTE should use the projected columns from the DTO
        Assert.That(q.ToDiagnostics().Sql, Does.Contain("WITH \"OrderSummaryDto\" AS"));

        var results = await q.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
    }
}
