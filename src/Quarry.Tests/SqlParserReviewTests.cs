using Quarry.Generators.Sql.Parser;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests added during the review pass — covers gaps identified by the two review agents.
/// </summary>
[TestFixture]
public class SqlParserReviewTests
{
    private static GenDialect D(SqlDialect d) => (GenDialect)(int)d;

    private static SqlParseResult Parse(string sql, SqlDialect dialect = SqlDialect.SQLite)
        => SqlParser.Parse(sql, D(dialect));

    // ─── T1: Subquery in IN clause ───────────────────────

    [Test]
    public void Parse_SubqueryInIn_MarkedAsUnsupported()
    {
        var result = Parse("SELECT a FROM t WHERE x IN (SELECT id FROM t2)");
        Assert.That(result.HasUnsupported, Is.True);
    }

    [Test]
    public void Parse_SubqueryInNotIn_MarkedAsUnsupported()
    {
        var result = Parse("SELECT a FROM t WHERE x NOT IN (SELECT id FROM t2 WHERE t2.active = 1)");
        Assert.That(result.HasUnsupported, Is.True);
    }

    // ─── T2: Arithmetic mixed with comparison ────────────

    [Test]
    public void Parse_ArithmeticInWhere()
    {
        var result = Parse("SELECT a FROM t WHERE price * quantity > 100");
        Assert.That(result.Success, Is.True);
        var gt = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(gt.Operator, Is.EqualTo(SqlBinaryOp.GreaterThan));
        var mul = (SqlBinaryExpr)gt.Left;
        Assert.That(mul.Operator, Is.EqualTo(SqlBinaryOp.Multiply));
    }

    [Test]
    public void Parse_ComplexArithmeticComparison()
    {
        var result = Parse("SELECT a FROM t WHERE (a + b) / c >= threshold");
        Assert.That(result.Success, Is.True);
        var gte = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(gte.Operator, Is.EqualTo(SqlBinaryOp.GreaterThanOrEqual));
    }

    // ─── T3: Aliases on everything simultaneously ────────

    [Test]
    public void Parse_AllAliasTypes()
    {
        var sql = "SELECT u.name AS user_name, COUNT(*) total, o.amount " +
                  "FROM users AS u " +
                  "LEFT JOIN orders o ON u.id = o.user_id " +
                  "GROUP BY u.name, o.amount";
        var result = Parse(sql);
        Assert.That(result.Success, Is.True);
        var stmt = result.SelectStatement!;

        // Column with AS alias
        Assert.That(((SqlSelectColumn)stmt.Columns[0]).Alias, Is.EqualTo("user_name"));
        // Column with implicit alias
        Assert.That(((SqlSelectColumn)stmt.Columns[1]).Alias, Is.EqualTo("total"));
        // Column without alias
        Assert.That(((SqlSelectColumn)stmt.Columns[2]).Alias, Is.Null);

        // Table with AS alias
        Assert.That(stmt.From!.Alias, Is.EqualTo("u"));
        // Join table with implicit alias
        Assert.That(stmt.Joins[0].Table.Alias, Is.EqualTo("o"));
    }

    // ─── T4: CAST with multi-word type ───────────────────

    [Test]
    public void Parse_CastWithMultiWordType()
    {
        var result = Parse("SELECT CAST(x AS DOUBLE PRECISION) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var cast = (SqlCastExpr)col.Expression;
        Assert.That(cast.TypeName, Is.EqualTo("DOUBLE PRECISION"));
    }

    [Test]
    public void Parse_CastWithParenthesizedType()
    {
        var result = Parse("SELECT CAST(x AS VARCHAR(255)) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var cast = (SqlCastExpr)col.Expression;
        Assert.That(cast.TypeName, Does.Contain("VARCHAR"));
    }

    // ─── T5: Diagnostic message quality ──────────────────

    [Test]
    public void Parse_CteError_HasActionableMessage()
    {
        var result = Parse("WITH cte AS (SELECT 1) SELECT * FROM cte");
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("CTEs"));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("WITH"));
    }

    [Test]
    public void Parse_MissingCloseParen_HasReadableMessage()
    {
        var result = Parse("SELECT COUNT(* FROM t");
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("')'"));
    }

    [Test]
    public void Parse_DiagnosticHasPosition()
    {
        var result = Parse("SELECT a FROM t WHERE");
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
        Assert.That(result.Diagnostics[0].Position, Is.GreaterThanOrEqualTo(0));
    }

    // ─── T6: Additional coverage ─────────────────────────

    [Test]
    public void Parse_MultipleParameters()
    {
        var result = Parse("SELECT a FROM t WHERE a = @p1 AND b = @p2 AND c = @p3", SqlDialect.SQLite);
        Assert.That(result.Success, Is.True);
        var allParams = SqlNodeWalker.FindAll<SqlParameter>(result.SelectStatement!);
        Assert.That(allParams, Has.Count.EqualTo(3));
        Assert.That(allParams[0].RawText, Is.EqualTo("@p1"));
        Assert.That(allParams[1].RawText, Is.EqualTo("@p2"));
        Assert.That(allParams[2].RawText, Is.EqualTo("@p3"));
    }

    [Test]
    public void Parse_NullInComparison()
    {
        var result = Parse("SELECT a FROM t WHERE x = NULL");
        Assert.That(result.Success, Is.True);
        var eq = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(((SqlLiteral)eq.Right).LiteralKind, Is.EqualTo(SqlLiteralKind.Null));
    }

    [Test]
    public void Parse_NegativeNumberInComparison()
    {
        var result = Parse("SELECT a FROM t WHERE x > -42");
        Assert.That(result.Success, Is.True);
        var gt = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(gt.Right, Is.TypeOf<SqlUnaryExpr>());
        var neg = (SqlUnaryExpr)gt.Right;
        Assert.That(neg.Operator, Is.EqualTo(SqlUnaryOp.Negate));
    }

    [Test]
    public void Parse_CaseInWhere()
    {
        var result = Parse("SELECT a FROM t WHERE CASE WHEN x = 1 THEN 1 ELSE 0 END = 1");
        Assert.That(result.Success, Is.True);
        var eq = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(eq.Left, Is.TypeOf<SqlCaseExpr>());
    }

    [Test]
    public void Parse_BareBooleanInWhere()
    {
        var result = Parse("SELECT a FROM t WHERE TRUE");
        Assert.That(result.Success, Is.True);
        var literal = (SqlLiteral)result.SelectStatement!.Where!;
        Assert.That(literal.LiteralKind, Is.EqualTo(SqlLiteralKind.Boolean));
    }

    // ─── C1: Bracket escape in SQL Server ────────────────

    [Test]
    public void Tokenize_SqlServer_BracketEscape()
    {
        var sql = "[col]]name]";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SqlServer));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.QuotedIdentifier));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo("[col]]name]"));
    }

    // ─── C2: Unterminated block comment ──────────────────

    [Test]
    public void Tokenize_UnterminatedBlockComment_NoSpuriousTokens()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT /* unterminated comment", D(SqlDialect.SQLite));
        // Only SELECT and EOF — the comment body should not leak
        Assert.That(tokens, Has.Count.EqualTo(2));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Select));
        Assert.That(tokens[1].Kind, Is.EqualTo(SqlTokenKind.Eof));
    }

    // ─── A2: Walker utility ──────────────────────────────

    [Test]
    public void Walker_FindAllColumnRefs()
    {
        var result = Parse("SELECT u.name, u.age FROM users u WHERE u.active = 1");
        Assert.That(result.Success, Is.True);
        var colRefs = SqlNodeWalker.FindAll<SqlColumnRef>(result.SelectStatement!);
        Assert.That(colRefs, Has.Count.GreaterThanOrEqualTo(3)); // u.name, u.age, u.active
    }

    [Test]
    public void Walker_FindAllTables()
    {
        var result = Parse("SELECT a FROM t1 INNER JOIN t2 ON t1.id = t2.id LEFT JOIN t3 ON t2.id = t3.id");
        Assert.That(result.Success, Is.True);
        var tables = SqlNodeWalker.FindAll<SqlTableSource>(result.SelectStatement!);
        Assert.That(tables, Has.Count.EqualTo(3)); // t1, t2, t3
    }

    [Test]
    public void Walker_FindAllParameters()
    {
        var result = Parse("SELECT a FROM t WHERE x = @p1 AND y = @p2", SqlDialect.SQLite);
        Assert.That(result.Success, Is.True);
        var parameters = SqlNodeWalker.FindAll<SqlParameter>(result.SelectStatement!);
        Assert.That(parameters, Has.Count.EqualTo(2));
    }

    [Test]
    public void Walker_WalksIntoWhenClauses()
    {
        var result = Parse("SELECT CASE WHEN x = @p1 THEN @p2 ELSE @p3 END FROM t", SqlDialect.SQLite);
        Assert.That(result.Success, Is.True);
        var parameters = SqlNodeWalker.FindAll<SqlParameter>(result.SelectStatement!);
        Assert.That(parameters, Has.Count.EqualTo(3));
    }

    // ─── A5: Source positions on nodes ───────────────────

    [Test]
    public void Parse_Nodes_HaveSourcePositions()
    {
        // Source positions default to -1 (not yet set by parser)
        // This test documents the current state — positions will be populated
        // when the analyzer consumer (#184) needs them
        var result = Parse("SELECT a FROM t");
        Assert.That(result.Success, Is.True);
        // SourceStart/SourceLength properties exist on all nodes
        Assert.That(result.SelectStatement!.SourceStart, Is.EqualTo(-1));
    }

    // ─── A6: SqlWhenClause is now a SqlNode ──────────────

    [Test]
    public void WhenClause_HasNodeKind()
    {
        var result = Parse("SELECT CASE WHEN x = 1 THEN 'a' END FROM t");
        Assert.That(result.Success, Is.True);
        var caseExpr = (SqlCaseExpr)((SqlSelectColumn)result.SelectStatement!.Columns[0]).Expression;
        var whenClause = caseExpr.WhenClauses[0];
        Assert.That(whenClause.NodeKind, Is.EqualTo(SqlNodeKind.WhenClause));
    }

    // ─── Diagnostic severity ─────────────────────────────

    [Test]
    public void Parse_Diagnostic_HasSeverity()
    {
        var result = Parse("");
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(SqlDiagnosticSeverity.Error));
    }
}
