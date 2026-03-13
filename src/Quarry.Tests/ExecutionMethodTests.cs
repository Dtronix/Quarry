namespace Quarry.Tests;

/// <summary>
/// Unit tests for execution methods on QueryBuilder.
/// These tests construct QueryBuilder directly (not via a QuarryContext),
/// so the generator correctly cannot analyze them.
/// </summary>
[TestFixture]
#pragma warning disable QRY001 // Query is not fully analyzable - intentional for unit tests
public class ExecutionMethodTests
{
    #region No Reader Throws

    [Test]
    public void ExecuteFetchAllAsync_ThrowsWhenNoReader()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteFetchAllAsync());
    }

    [Test]
    public void ExecuteFetchFirstAsync_ThrowsWhenNoReader()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteFetchFirstAsync());
    }

    [Test]
    public void ExecuteFetchFirstOrDefaultAsync_ThrowsWhenNoReader()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteFetchFirstOrDefaultAsync());
    }

    [Test]
    public void ExecuteFetchSingleAsync_ThrowsWhenNoReader()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteFetchSingleAsync());
    }

    [Test]
    public void ToAsyncEnumerable_ThrowsWhenNoReader()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        // ToAsyncEnumerable returns immediately, but iterating will throw
        Assert.Throws<InvalidOperationException>(() =>
        {
            var enumerable = projected.ToAsyncEnumerable();
            var enumerator = enumerable.GetAsyncEnumerator();
        });
    }

    #endregion

    #region No Execution Context Throws

    [Test]
    public void ExecuteScalarAsync_ThrowsWhenNoContext()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = builder.Select(e => e);

        // ExecuteScalarAsync doesn't need a reader but needs a context
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteScalarAsync<int>());
    }

    [Test]
    public void ExecuteNonQueryAsync_ThrowsWhenNoContext()
    {
        var builder = new QueryBuilder<TestEntity>(SqlDialectFactory.PostgreSQL, "users", null);
        var projected = (QueryBuilder<TestEntity, TestEntity>)builder.Select(e => e);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await projected.ExecuteNonQueryAsync());
    }

    #endregion

    // Test entity class for testing
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
