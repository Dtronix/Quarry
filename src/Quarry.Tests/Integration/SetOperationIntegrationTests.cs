using System.Collections.Immutable;
using System.Data.Common;
using Quarry.Internal;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

/// <summary>
/// Integration tests for set operations (UNION, EXCEPT, INTERSECT) executing
/// against a real SQLite database. Uses SetOperationBuilder.ToSql() to verify
/// correct SQL generation, then executes the raw SQL directly to validate
/// correctness, since the full execution path requires compile-time interceptors.
/// </summary>
[TestFixture]
internal class SetOperationIntegrationTests : SqliteIntegrationTestBase
{
    private async Task<List<string>> ExecuteStringQueryAsync(string sql)
    {
        var ctx = (IQueryExecutionContext)Db;
        await ctx.EnsureConnectionOpenAsync(CancellationToken.None);
        await using var cmd = ctx.Connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<string>();
        while (await reader.ReadAsync())
            results.Add(reader.GetString(0));
        return results;
    }

    [Test]
    public async Task Union_RemovesDuplicates()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"
            ),
            SetOperationKind.Union,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();
        Assert.That(sql, Does.Contain("UNION"));

        var results = await ExecuteStringQueryAsync(sql);
        // UNION removes duplicates: Alice, Bob
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
    }

    [Test]
    public async Task UnionAll_KeepsDuplicates()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"
            ),
            SetOperationKind.UnionAll,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();
        Assert.That(sql, Does.Contain("UNION ALL"));

        var results = await ExecuteStringQueryAsync(sql);
        // UNION ALL keeps duplicates: Alice, Bob, Alice, Bob
        Assert.That(results, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task Except_ReturnsRowsOnlyInFirstQuery()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\"",
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"
            ),
            SetOperationKind.Except,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();
        Assert.That(sql, Does.Contain("EXCEPT"));

        var results = await ExecuteStringQueryAsync(sql);
        // EXCEPT: only Charlie is in first but not second
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task Intersect_ReturnsCommonRows()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\"",
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"
            ),
            SetOperationKind.Intersect,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();
        Assert.That(sql, Does.Contain("INTERSECT"));

        var results = await ExecuteStringQueryAsync(sql);
        // INTERSECT: Alice, Bob are in both
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
    }

    [Test]
    public async Task Union_WithLimit_ReturnsLimitedRows()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1",
                "SELECT \"UserName\" FROM \"users\" WHERE \"IsActive\" = 0"
            ),
            SetOperationKind.Union,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty)
            .Limit(2);

        var sql = builder.ToSql();
        Assert.That(sql, Does.Contain("LIMIT 2"));

        var results = await ExecuteStringQueryAsync(sql);
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Union_ThreeQueries_CombinesAll()
    {
        var builder = new SetOperationBuilder<object>(
            ImmutableArray.Create(
                "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = 1",
                "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = 2",
                "SELECT \"UserName\" FROM \"users\" WHERE \"UserId\" = 3"
            ),
            SetOperationKind.Union,
            SqlDialect.SQLite,
            null, null,
            ImmutableArray<QueryParameter>.Empty);

        var sql = builder.ToSql();
        var results = await ExecuteStringQueryAsync(sql);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results, Does.Contain("Alice"));
        Assert.That(results, Does.Contain("Bob"));
        Assert.That(results, Does.Contain("Charlie"));
    }
}
