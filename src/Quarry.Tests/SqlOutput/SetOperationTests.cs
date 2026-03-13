using System.Collections.Immutable;
using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
#pragma warning disable QRY001
public class SetOperationTests
{
    private class TestEntity;

    private static SetOperationBuilder<TestEntity> Build(SetOperationKind kind, SqlDialect dialect, params string[] queries)
    {
        return new SetOperationBuilder<TestEntity>(
            ImmutableArray.Create(queries), kind, dialect, null, null, ImmutableArray<QueryParameter>.Empty);
    }

    // SQLite: no parentheses around individual queries

    [Test]
    public void Union()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" UNION SELECT * FROM \"admins\""));
    }

    [Test]
    public void UnionAll()
    {
        var builder = Build(SetOperationKind.UnionAll, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" UNION ALL SELECT * FROM \"admins\""));
    }

    [Test]
    public void Except()
    {
        var builder = Build(SetOperationKind.Except, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" EXCEPT SELECT * FROM \"admins\""));
    }

    [Test]
    public void Intersect()
    {
        var builder = Build(SetOperationKind.Intersect, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" INTERSECT SELECT * FROM \"admins\""));
    }

    [Test]
    public void ThreeQueries()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.SQLite,
            "SELECT * FROM \"a\"", "SELECT * FROM \"b\"", "SELECT * FROM \"c\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"a\" UNION SELECT * FROM \"b\" UNION SELECT * FROM \"c\""));
    }

    [Test]
    public void WithLimit()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"")
            .Limit(10);
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" UNION SELECT * FROM \"admins\" LIMIT 10"));
    }

    [Test]
    public void WithLimitAndOffset()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.SQLite,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"")
            .Limit(10).Offset(20);
        Assert.That(builder.ToSql(),
            Is.EqualTo("SELECT * FROM \"users\" UNION SELECT * FROM \"admins\" LIMIT 10 OFFSET 20"));
    }

    // SQL Server: parentheses are supported and used

    [Test]
    public void WithLimit_SqlServer()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.SqlServer,
            "SELECT * FROM [users]", "SELECT * FROM [admins]")
            .Limit(10);
        Assert.That(builder.ToSql(),
            Is.EqualTo("(SELECT * FROM [users]) UNION (SELECT * FROM [admins]) ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY"));
    }

    // PostgreSQL: parentheses are supported

    [Test]
    public void Union_PostgreSql()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.PostgreSQL,
            "SELECT * FROM \"users\"", "SELECT * FROM \"admins\"");
        Assert.That(builder.ToSql(),
            Is.EqualTo("(SELECT * FROM \"users\") UNION (SELECT * FROM \"admins\")"));
    }

    // MySQL: parentheses are supported

    [Test]
    public void Union_MySql()
    {
        var builder = Build(SetOperationKind.Union, SqlDialect.MySQL,
            "SELECT * FROM `users`", "SELECT * FROM `admins`");
        Assert.That(builder.ToSql(),
            Is.EqualTo("(SELECT * FROM `users`) UNION (SELECT * FROM `admins`)"));
    }
}
