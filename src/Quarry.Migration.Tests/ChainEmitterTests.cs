using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Migration;
using Quarry.Shared.Sql.Parser;

namespace Quarry.Migration.Tests;

[TestFixture]
public class ChainEmitterTests
{
    /// <summary>Helper to build a SchemaMap programmatically for translation tests.</summary>
    private static SchemaMap BuildSchemaMap(params EntityMapping[] entities)
    {
        var dict = new Dictionary<string, EntityMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entities)
            dict[e.TableName] = e;
        return new SchemaMap(dict);
    }

    private static EntityMapping UsersEntity() => new EntityMapping(
        "users", null, "UserSchema", "Users",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_id"] = "UserId",
            ["user_name"] = "UserName",
            ["email"] = "Email",
            ["is_active"] = "IsActive",
            ["created_at"] = "CreatedAt",
            ["last_login"] = "LastLogin",
            ["salary"] = "Salary",
            ["dept"] = "Dept",
        });

    private static EntityMapping OrdersEntity() => new EntityMapping(
        "orders", null, "OrderSchema", "Orders",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_id"] = "OrderId",
            ["user_id"] = "UserId",
            ["total"] = "Total",
            ["status"] = "Status",
            ["order_date"] = "OrderDate",
        });

    private static EntityMapping EmployeesEntity() => new EntityMapping(
        "employees", null, "EmployeeSchema", "Employees",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["employee_id"] = "EmployeeId",
            ["first_name"] = "FirstName",
            ["last_name"] = "LastName",
            ["dept"] = "Dept",
            ["salary"] = "Salary",
        });

    private static DapperCallSite FakeCallSite(
        string sql,
        string method = "QueryAsync",
        string? resultType = "User",
        IReadOnlyList<string>? parameterNames = null)
    {
        return new DapperCallSite(
            sql: sql,
            parameterNames: parameterNames ?? Array.Empty<string>(),
            methodName: method,
            resultTypeName: resultType,
            location: Location.None,
            invocationSyntax: null!);
    }

    // ─── Phase 4: Core FROM/SELECT/WHERE ───────────────────

    [Test]
    public void SelectStarFromTable()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Not.Null);
        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Select(u => u)"));
        Assert.That(result.ChainCode, Does.Contain(".ExecuteFetchAllAsync()"));
    }

    [Test]
    public void SelectSpecificColumns()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT user_id, user_name FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Select(u => (u.UserId, u.UserName))"));
    }

    [Test]
    public void SelectSingleColumn()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT email FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Select(u => u.Email)"));
    }

    [Test]
    public void WhereWithLiteral()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE is_active = 1";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.IsActive == 1)"));
    }

    [Test]
    public void WhereWithParameter()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id = @userId";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, parameterNames: new[] { "userId" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId == userId)"));
    }

    [Test]
    public void WhereWithAndCondition()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id = @userId AND user_name = @name";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, parameterNames: new[] { "userId", "name" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId == userId && u.UserName == name)"));
    }

    [Test]
    public void WhereWithOrCondition()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE is_active = 1 OR user_id > 100";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.IsActive == 1 || u.UserId > 100)"));
    }

    [Test]
    public void QueryFirstAsync_Terminal()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id = 1";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "QueryFirstAsync");

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".ExecuteFetchFirstAsync()"));
    }

    [Test]
    public void QueryFirstOrDefaultAsync_Terminal()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id = 1";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "QueryFirstOrDefaultAsync");

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".ExecuteFetchFirstOrDefaultAsync()"));
    }

    [Test]
    public void ExecuteAsync_Terminal()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync");

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
    }

    [Test]
    public void UnknownTable_ReturnsNull()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM unknown_table";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Null);
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    [Test]
    public void WhereWithStringLiteral()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_name = 'admin'";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserName == \"admin\")"));
    }

    [Test]
    public void WhereWithNullLiteral()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE last_login IS NULL";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.LastLogin == null)"));
    }

    [Test]
    public void WhereWithIsNotNull()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE last_login IS NOT NULL";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.LastLogin != null)"));
    }

    [Test]
    public void WhereWithComparisonOperators()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id >= 10 AND user_id <= 100";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId >= 10 && u.UserId <= 100)"));
    }

    [Test]
    public void TerminalMapping_AllVariants()
    {
        Assert.That(ChainEmitter.MapTerminal("QueryAsync"), Is.EqualTo("ExecuteFetchAllAsync()"));
        Assert.That(ChainEmitter.MapTerminal("QueryFirstAsync"), Is.EqualTo("ExecuteFetchFirstAsync()"));
        Assert.That(ChainEmitter.MapTerminal("QueryFirstOrDefaultAsync"), Is.EqualTo("ExecuteFetchFirstOrDefaultAsync()"));
        Assert.That(ChainEmitter.MapTerminal("QuerySingleAsync"), Is.EqualTo("ExecuteFetchSingleAsync()"));
        Assert.That(ChainEmitter.MapTerminal("QuerySingleOrDefaultAsync"), Is.EqualTo("ExecuteFetchSingleOrDefaultAsync()"));
        Assert.That(ChainEmitter.MapTerminal("ExecuteAsync"), Is.EqualTo("ExecuteNonQueryAsync()"));
        Assert.That(ChainEmitter.MapTerminal("ExecuteScalarAsync"), Is.EqualTo("ExecuteScalarAsync()"));
        // Sync variants
        Assert.That(ChainEmitter.MapTerminal("Query"), Is.EqualTo("ExecuteFetchAllAsync()"));
        Assert.That(ChainEmitter.MapTerminal("QueryFirst"), Is.EqualTo("ExecuteFetchFirstAsync()"));
    }

    // ─── Phase 5: JOINs, Aggregates, ORDER BY, LIMIT ──────

    [Test]
    public void InnerJoin()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = "SELECT u.user_name, o.total FROM users u INNER JOIN orders o ON u.user_id = o.user_id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Join<OrderSchema>((u, o) => u.UserId == o.UserId)"));
        Assert.That(result.ChainCode, Does.Contain(".Select((u, o) => (u.UserName, o.Total))"));
    }

    [Test]
    public void LeftJoin()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = "SELECT u.user_name, o.total FROM users u LEFT JOIN orders o ON u.user_id = o.user_id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".LeftJoin<OrderSchema>((u, o) => u.UserId == o.UserId)"));
    }

    [Test]
    public void RightJoin()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = "SELECT u.user_name, o.total FROM users u RIGHT JOIN orders o ON u.user_id = o.user_id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".RightJoin<OrderSchema>((u, o) => u.UserId == o.UserId)"));
    }

    [Test]
    public void CrossJoin()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = "SELECT u.user_name, o.total FROM users u CROSS JOIN orders o";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".CrossJoin<OrderSchema>()"));
    }

    [Test]
    public void FullOuterJoin()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = "SELECT u.user_name, o.total FROM users u FULL OUTER JOIN orders o ON u.user_id = o.user_id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".FullOuterJoin<OrderSchema>((u, o) => u.UserId == o.UserId)"));
    }

    [Test]
    public void GroupByWithCount()
    {
        var schema = BuildSchemaMap(EmployeesEntity());
        var sql = "SELECT dept, COUNT(*) FROM employees GROUP BY dept";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".GroupBy(e => e.Dept)"));
        Assert.That(result.ChainCode, Does.Contain("Sql.Count()"));
    }

    [Test]
    public void GroupByWithHaving()
    {
        var schema = BuildSchemaMap(EmployeesEntity());
        var sql = "SELECT dept, AVG(salary) FROM employees GROUP BY dept HAVING AVG(salary) > 50000";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".GroupBy(e => e.Dept)"));
        Assert.That(result.ChainCode, Does.Contain(".Having(e => Sql.Avg(e.Salary) > 50000)"));
        Assert.That(result.ChainCode, Does.Contain("Sql.Avg(e.Salary)"));
    }

    [Test]
    public void OrderByAscending()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users ORDER BY user_name";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".OrderBy(u => u.UserName)"));
        Assert.That(result.ChainCode, Does.Not.Contain("Direction.Descending"));
    }

    [Test]
    public void OrderByDescending()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users ORDER BY created_at DESC";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".OrderBy(u => u.CreatedAt, Direction.Descending)"));
    }

    [Test]
    public void OrderByMultiple()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users ORDER BY is_active DESC, user_name ASC";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".OrderBy(u => u.IsActive, Direction.Descending)"));
        Assert.That(result.ChainCode, Does.Contain(".ThenBy(u => u.UserName)"));
    }

    [Test]
    public void LimitOnly()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users LIMIT 10";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Limit(10)"));
    }

    [Test]
    public void LimitAndOffset()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users LIMIT 10 OFFSET 20";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Limit(10)"));
        Assert.That(result.ChainCode, Does.Contain(".Offset(20)"));
    }

    // ─── Phase 6: Parameters, IN, BETWEEN, LIKE ───────────

    [Test]
    public void InExpressionWithLiterals()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id IN (1, 2, 3)";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("new[] { 1, 2, 3 }.Contains(u.UserId)"));
    }

    [Test]
    public void InExpressionWithParameters()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id IN (@a, @b, @c)";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, parameterNames: new[] { "a", "b", "c" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("new[] { a, b, c }.Contains(u.UserId)"));
    }

    [Test]
    public void BetweenExpression()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE salary BETWEEN @min AND @max";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, parameterNames: new[] { "min", "max" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("u.Salary >= min && u.Salary <= max"));
    }

    [Test]
    public void LikeExpression()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_name LIKE @pattern";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, parameterNames: new[] { "pattern" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("Sql.Like(u.UserName, pattern)"));
    }

    [Test]
    public void CaseExpression_FallsBackToRaw()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT CASE WHEN is_active = 1 THEN 'yes' ELSE 'no' END FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("Sql.Raw"));
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    [Test]
    public void NotInExpression()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_id NOT IN (1, 2)";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("!new[] { 1, 2 }.Contains(u.UserId)"));
    }

    [Test]
    public void NotBetweenExpression()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE salary NOT BETWEEN 1000 AND 5000";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("u.Salary < 1000 || u.Salary > 5000"));
    }

    [Test]
    public void NotLikeExpression()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT * FROM users WHERE user_name NOT LIKE '%test%'";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("!Sql.Like(u.UserName, \"%test%\")"));
    }

    [Test]
    public void UnknownFunction_FallsBackToRaw()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "SELECT COALESCE(email, 'none') FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("Sql.Raw"));
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    [Test]
    public void JoinedQueryWithWhereAndOrderBy()
    {
        var schema = BuildSchemaMap(UsersEntity(), OrdersEntity());
        var sql = @"SELECT u.user_name, o.total
                    FROM users u
                    INNER JOIN orders o ON u.user_id = o.user_id
                    WHERE o.total > 100
                    ORDER BY o.total DESC
                    LIMIT 10";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Join<OrderSchema>"));
        Assert.That(result.ChainCode, Does.Contain(".Where((u, o) => o.Total > 100)"));
        Assert.That(result.ChainCode, Does.Contain(".OrderBy((u, o) => o.Total, Direction.Descending)"));
        Assert.That(result.ChainCode, Does.Contain(".Limit(10)"));
    }

    // ─── DELETE emission ──────────────────────────────────

    [Test]
    public void Delete_WithWhere()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "DELETE FROM users WHERE user_id = @id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null, parameterNames: new[] { "id" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Not.Null);
        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Delete()"));
        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId == id)"));
        Assert.That(result.ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
        Assert.That(result.ChainCode, Does.Not.Contain(".All()"));
    }

    [Test]
    public void Delete_WithoutWhere_EmitsAllAndWarning()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "DELETE FROM users";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Not.Null);
        Assert.That(result.ChainCode, Does.Contain(".Delete()"));
        Assert.That(result.ChainCode, Does.Contain(".All()"));
        Assert.That(result.ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
        Assert.That(result.Diagnostics, Has.Some.Matches<ConversionDiagnostic>(d =>
            d.Message.Contains("DELETE without WHERE")));
    }

    [Test]
    public void Delete_WithComplexWhere()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "DELETE FROM users WHERE is_active = 0 AND created_at < @cutoff";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null, parameterNames: new[] { "cutoff" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Delete()"));
        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.IsActive == 0 && u.CreatedAt < cutoff)"));
    }

    [Test]
    public void Delete_UnknownTable_ReturnsNull()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "DELETE FROM unknown_table WHERE id = 1";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Null);
    }

    // ─── UPDATE emission ──────────────────────────────────

    [Test]
    public void Update_SingleColumn_WithWhere()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "UPDATE users SET is_active = 0 WHERE user_id = @id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null, parameterNames: new[] { "id" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Not.Null);
        Assert.That(result.ChainCode, Does.Contain("db.Users()"));
        Assert.That(result.ChainCode, Does.Contain(".Update()"));
        Assert.That(result.ChainCode, Does.Contain(".Set(u => { u.IsActive = 0; })"));
        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId == id)"));
        Assert.That(result.ChainCode, Does.Contain(".ExecuteNonQueryAsync()"));
    }

    [Test]
    public void Update_MultipleColumns_WithWhere()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "UPDATE users SET email = @email, user_name = @name WHERE user_id = @id";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null, parameterNames: new[] { "email", "name", "id" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Update()"));
        Assert.That(result.ChainCode, Does.Contain(".Set(u => { u.Email = email; u.UserName = name; })"));
        Assert.That(result.ChainCode, Does.Contain(".Where(u => u.UserId == id)"));
    }

    [Test]
    public void Update_WithoutWhere_EmitsAllAndWarning()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "UPDATE users SET is_active = 0";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Does.Contain(".Update()"));
        Assert.That(result.ChainCode, Does.Contain(".All()"));
        Assert.That(result.Diagnostics, Has.Some.Matches<ConversionDiagnostic>(d =>
            d.Message.Contains("UPDATE without WHERE")));
    }

    [Test]
    public void Update_UnknownTable_ReturnsNull()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "UPDATE unknown_table SET col = 1 WHERE id = 1";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Null);
    }

    // ─── INSERT emission ──────────────────────────────────

    [Test]
    public void Insert_EmitsComment()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "INSERT INTO users (user_name, email) VALUES (@name, @email)";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null, parameterNames: new[] { "name", "email" });

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Not.Null);
        Assert.That(result.ChainCode, Does.Contain("// TODO:"));
        Assert.That(result.ChainCode, Does.Contain("db.Users().Insert(entity).ExecuteNonQueryAsync()"));
        Assert.That(result.ChainCode, Does.Contain("UserName"));
        Assert.That(result.ChainCode, Does.Contain("Email"));
        Assert.That(result.Diagnostics, Has.Some.Matches<ConversionDiagnostic>(d =>
            d.Message.Contains("INSERT requires entity construction")));
    }

    [Test]
    public void Insert_UnknownTable_ReturnsNull()
    {
        var schema = BuildSchemaMap(UsersEntity());
        var sql = "INSERT INTO unknown_table (col) VALUES (1)";
        var parseResult = SqlParser.Parse(sql, SqlDialect.SQLite);
        var callSite = FakeCallSite(sql, method: "ExecuteAsync", resultType: null);

        var emitter = new ChainEmitter(schema);
        var result = emitter.Translate(parseResult, callSite);

        Assert.That(result.ChainCode, Is.Null);
    }
}
