using Quarry.Generators.Sql.Parser;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class SqlParserEdgeCaseTests
{
    private static GenDialect D(SqlDialect d) => (GenDialect)(int)d;

    private static SqlParseResult Parse(string sql, SqlDialect dialect = SqlDialect.SQLite)
        => SqlParser.Parse(sql, D(dialect));

    // ─── Multiple joins ──────────────────────────────────

    [Test]
    public void Parse_MultipleJoins()
    {
        var result = Parse(
            "SELECT a FROM t1 " +
            "INNER JOIN t2 ON t1.id = t2.t1_id " +
            "LEFT JOIN t3 ON t2.id = t3.t2_id " +
            "RIGHT JOIN t4 ON t3.id = t4.t3_id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Joins, Has.Count.EqualTo(3));
        Assert.That(result.Statement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Inner));
        Assert.That(result.Statement!.Joins[1].JoinKind, Is.EqualTo(SqlJoinKind.Left));
        Assert.That(result.Statement!.Joins[2].JoinKind, Is.EqualTo(SqlJoinKind.Right));
    }

    // ─── Deeply nested expressions ───────────────────────

    [Test]
    public void Parse_DeeplyNestedParens()
    {
        var result = Parse("SELECT ((((a + b)))) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        // Should be 4 layers of SqlParenExpr wrapping SqlBinaryExpr
        var p1 = (SqlParenExpr)col.Expression;
        var p2 = (SqlParenExpr)p1.Inner;
        var p3 = (SqlParenExpr)p2.Inner;
        var p4 = (SqlParenExpr)p3.Inner;
        var add = (SqlBinaryExpr)p4.Inner;
        Assert.That(add.Operator, Is.EqualTo(SqlBinaryOp.Add));
    }

    [Test]
    public void Parse_ComplexNestedWhere()
    {
        var result = Parse(
            "SELECT a FROM t WHERE (x = 1 AND (y > 2 OR z < 3)) OR NOT w = 4");
        Assert.That(result.Success, Is.True);
        var or = (SqlBinaryExpr)result.Statement!.Where!;
        Assert.That(or.Operator, Is.EqualTo(SqlBinaryOp.Or));
    }

    // ─── Empty / whitespace ──────────────────────────────

    [Test]
    public void Parse_WhitespaceOnly_HasDiagnostics()
    {
        var result = Parse("   \t\n  ");
        Assert.That(result.Statement, Is.Null);
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    // ─── Trailing semicolons ─────────────────────────────

    [Test]
    public void Parse_MultipleSemicolons()
    {
        var result = Parse("SELECT a FROM t;;;");
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void Parse_LeadingSemicolons()
    {
        var result = Parse(";;SELECT a FROM t");
        Assert.That(result.Success, Is.True);
    }

    // ─── Mixed-case keywords ─────────────────────────────

    [Test]
    public void Parse_MixedCaseKeywords()
    {
        var result = Parse("sElEcT a FrOm t WheRe x = 1 OrDeR bY a");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Where, Is.Not.Null);
        Assert.That(result.Statement!.OrderBy, Has.Count.EqualTo(1));
    }

    // ─── Quoted identifier as column/table name ──────────

    [Test]
    public void Parse_QuotedIdentifiers_SqlServer()
    {
        var result = Parse("SELECT [select], [from] FROM [table] WHERE [order] = 1", SqlDialect.SqlServer);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Columns, Has.Count.EqualTo(2));
        var col1 = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(((SqlColumnRef)col1.Expression).ColumnName, Is.EqualTo("select"));
    }

    [Test]
    public void Parse_QuotedIdentifiers_MySQL()
    {
        var result = Parse("SELECT `select` FROM `table`", SqlDialect.MySQL);
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(((SqlColumnRef)col.Expression).ColumnName, Is.EqualTo("select"));
    }

    [Test]
    public void Parse_QuotedIdentifiers_ANSI()
    {
        var result = Parse("SELECT \"select\" FROM \"table\"", SqlDialect.PostgreSQL);
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(((SqlColumnRef)col.Expression).ColumnName, Is.EqualTo("select"));
    }

    // ─── String literals containing keywords ─────────────

    [Test]
    public void Parse_StringWithKeywords()
    {
        var result = Parse("SELECT a FROM t WHERE name = 'SELECT FROM WHERE'");
        Assert.That(result.Success, Is.True);
        var bin = (SqlBinaryExpr)result.Statement!.Where!;
        var lit = (SqlLiteral)bin.Right;
        Assert.That(lit.LiteralKind, Is.EqualTo(SqlLiteralKind.String));
        Assert.That(lit.Value, Does.Contain("SELECT FROM WHERE"));
    }

    // ─── Numeric edge cases ──────────────────────────────

    [Test]
    public void Parse_LeadingZeros()
    {
        var result = Parse("SELECT 007 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(((SqlLiteral)col.Expression).Value, Is.EqualTo("007"));
    }

    [Test]
    public void Parse_DecimalNumber()
    {
        var result = Parse("SELECT 3.14159 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(((SqlLiteral)col.Expression).Value, Is.EqualTo("3.14159"));
    }

    // ─── Full complex query ──────────────────────────────

    [Test]
    public void Parse_ComplexRealWorldQuery()
    {
        var sql =
            "SELECT u.id, u.name, COUNT(o.id) AS order_count, COALESCE(SUM(o.total), 0) AS total_spent " +
            "FROM users u " +
            "LEFT JOIN orders o ON u.id = o.user_id " +
            "WHERE u.active = 1 AND u.created_at >= '2025-01-01' " +
            "GROUP BY u.id, u.name " +
            "HAVING COUNT(o.id) > 0 " +
            "ORDER BY total_spent DESC " +
            "LIMIT 50 OFFSET 10";
        var result = Parse(sql);
        Assert.That(result.Success, Is.True);
        var stmt = result.Statement!;

        Assert.That(stmt.Columns, Has.Count.EqualTo(4));
        Assert.That(stmt.Joins, Has.Count.EqualTo(1));
        Assert.That(stmt.Where, Is.Not.Null);
        Assert.That(stmt.GroupBy, Has.Count.EqualTo(2));
        Assert.That(stmt.Having, Is.Not.Null);
        Assert.That(stmt.OrderBy, Has.Count.EqualTo(1));
        Assert.That(stmt.Limit, Is.Not.Null);
        Assert.That(stmt.Offset, Is.Not.Null);
    }

    // ─── All dialects parse the same basic query ─────────

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void Parse_BasicQuery_AllDialects(SqlDialect dialect)
    {
        var result = Parse("SELECT a, b FROM t WHERE x = 1", dialect);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Columns, Has.Count.EqualTo(2));
    }

    // ─── Boolean literals ────────────────────────────────

    [Test]
    public void Parse_BooleanLiterals()
    {
        var result = Parse("SELECT a FROM t WHERE active = TRUE AND deleted = FALSE");
        Assert.That(result.Success, Is.True);
        var and = (SqlBinaryExpr)result.Statement!.Where!;
        var eq1 = (SqlBinaryExpr)and.Left;
        Assert.That(((SqlLiteral)eq1.Right).LiteralKind, Is.EqualTo(SqlLiteralKind.Boolean));
        Assert.That(((SqlLiteral)eq1.Right).Value, Is.EqualTo("TRUE"));
    }

    // ─── NOT LIKE ────────────────────────────────────────

    [Test]
    public void Parse_NotLike()
    {
        var result = Parse("SELECT a FROM t WHERE name NOT LIKE '%test%'");
        Assert.That(result.Success, Is.True);
        // Should be NOT(LIKE(name, '%test%'))
        var notExpr = (SqlUnaryExpr)result.Statement!.Where!;
        Assert.That(notExpr.Operator, Is.EqualTo(SqlUnaryOp.Not));
        var like = (SqlBinaryExpr)notExpr.Operand;
        Assert.That(like.Operator, Is.EqualTo(SqlBinaryOp.Like));
    }

    // ─── SELECT without FROM ─────────────────────────────

    [Test]
    public void Parse_SelectWithoutFrom()
    {
        var result = Parse("SELECT 1");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.From, Is.Null);
    }

    // ─── Multiple star columns ───────────────────────────

    [Test]
    public void Parse_MultipleTableStars()
    {
        var result = Parse("SELECT t1.*, t2.* FROM t1 INNER JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Columns, Has.Count.EqualTo(2));
        Assert.That(((SqlStarColumn)result.Statement!.Columns[0]).TableAlias, Is.EqualTo("t1"));
        Assert.That(((SqlStarColumn)result.Statement!.Columns[1]).TableAlias, Is.EqualTo("t2"));
    }

    // ─── CASE with multiple WHEN clauses ─────────────────

    [Test]
    public void Parse_CaseMultipleWhen()
    {
        var result = Parse(
            "SELECT CASE " +
            "WHEN x = 1 THEN 'one' " +
            "WHEN x = 2 THEN 'two' " +
            "WHEN x = 3 THEN 'three' " +
            "ELSE 'other' END FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        var caseExpr = (SqlCaseExpr)col.Expression;
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(3));
        Assert.That(caseExpr.ElseResult, Is.Not.Null);
    }

    // ─── INTERSECT / EXCEPT detection ────────────────────

    [Test]
    public void Parse_Intersect_MarkedAsUnsupported()
    {
        var result = Parse("SELECT a FROM t1 INTERSECT SELECT b FROM t2");
        Assert.That(result.HasUnsupported, Is.True);
    }

    [Test]
    public void Parse_Except_MarkedAsUnsupported()
    {
        var result = Parse("SELECT a FROM t1 EXCEPT SELECT b FROM t2");
        Assert.That(result.HasUnsupported, Is.True);
    }

    // ─── Full OUTER JOIN without OUTER keyword ───────────

    [Test]
    public void Parse_FullJoinWithoutOuter()
    {
        var result = Parse("SELECT a FROM t1 FULL JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.FullOuter));
    }

    // ─── GROUP BY multiple columns ───────────────────────

    [Test]
    public void Parse_GroupByMultipleColumns()
    {
        var result = Parse("SELECT dept, role, COUNT(*) FROM t GROUP BY dept, role");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.GroupBy, Has.Count.EqualTo(2));
    }

    // ─── Nested function calls ───────────────────────────

    [Test]
    public void Parse_NestedFunctionCalls()
    {
        var result = Parse("SELECT UPPER(TRIM(name)) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        var outer = (SqlFunctionCall)col.Expression;
        Assert.That(outer.FunctionName, Is.EqualTo("UPPER"));
        var inner = (SqlFunctionCall)outer.Arguments[0];
        Assert.That(inner.FunctionName, Is.EqualTo("TRIM"));
    }

    // ─── Aliased function call ───────────────────────────

    [Test]
    public void Parse_FunctionWithAlias()
    {
        var result = Parse("SELECT COUNT(*) AS cnt FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.Statement!.Columns[0];
        Assert.That(col.Alias, Is.EqualTo("cnt"));
        Assert.That(col.Expression, Is.TypeOf<SqlFunctionCall>());
    }

    // ─── SqlServer OFFSET FETCH without ORDER BY ─────────

    [Test]
    public void Parse_OffsetFetch_WithRowSingular()
    {
        var result = Parse("SELECT a FROM t ORDER BY a OFFSET 1 ROW FETCH NEXT 1 ROW ONLY", SqlDialect.SqlServer);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Statement!.Offset, Is.Not.Null);
        Assert.That(result.Statement!.Limit, Is.Not.Null);
    }
}
