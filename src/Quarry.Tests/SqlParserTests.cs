using Quarry.Generators.Sql.Parser;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class SqlParserTests
{
    private static GenDialect D(SqlDialect d) => (GenDialect)(int)d;

    private static SqlParseResult Parse(string sql, SqlDialect dialect = SqlDialect.SQLite)
        => SqlParser.Parse(sql, D(dialect));

    // ─── Basic SELECT ────────────────────────────────────

    [Test]
    public void Parse_BasicSelectFrom()
    {
        var result = Parse("SELECT col FROM table1");
        Assert.That(result.Success, Is.True);
        var stmt = result.SelectStatement!;

        Assert.That(stmt.IsDistinct, Is.False);
        Assert.That(stmt.Columns, Has.Count.EqualTo(1));
        var col = (SqlSelectColumn)stmt.Columns[0];
        var colRef = (SqlColumnRef)col.Expression;
        Assert.That(colRef.ColumnName, Is.EqualTo("col"));
        Assert.That(colRef.TableAlias, Is.Null);
        Assert.That(col.Alias, Is.Null);

        Assert.That(stmt.From, Is.Not.Null);
        Assert.That(stmt.From!.TableName, Is.EqualTo("table1"));
    }

    [Test]
    public void Parse_MultipleColumns()
    {
        var result = Parse("SELECT a, b, c FROM t");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Columns, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_SelectStar()
    {
        var result = Parse("SELECT * FROM t");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Columns[0], Is.TypeOf<SqlStarColumn>());
        Assert.That(((SqlStarColumn)result.SelectStatement!.Columns[0]).TableAlias, Is.Null);
    }

    [Test]
    public void Parse_SelectTableStar()
    {
        var result = Parse("SELECT t.* FROM t");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Columns[0], Is.TypeOf<SqlStarColumn>());
        Assert.That(((SqlStarColumn)result.SelectStatement!.Columns[0]).TableAlias, Is.EqualTo("t"));
    }

    [Test]
    public void Parse_SelectDistinct()
    {
        var result = Parse("SELECT DISTINCT a FROM t");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.IsDistinct, Is.True);
    }

    // ─── WHERE ───────────────────────────────────────────

    [Test]
    public void Parse_WhereEquals()
    {
        var result = Parse("SELECT a FROM t WHERE x = 1");
        Assert.That(result.Success, Is.True);
        var where = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(where.Operator, Is.EqualTo(SqlBinaryOp.Equal));
        Assert.That(((SqlColumnRef)where.Left).ColumnName, Is.EqualTo("x"));
        Assert.That(((SqlLiteral)where.Right).Value, Is.EqualTo("1"));
    }

    [Test]
    public void Parse_WhereAndOr()
    {
        var result = Parse("SELECT a FROM t WHERE x = 1 AND y = 2 OR z = 3");
        Assert.That(result.Success, Is.True);

        // OR has lower precedence: OR(AND(x=1, y=2), z=3)
        var or = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(or.Operator, Is.EqualTo(SqlBinaryOp.Or));

        var and = (SqlBinaryExpr)or.Left;
        Assert.That(and.Operator, Is.EqualTo(SqlBinaryOp.And));
    }

    [Test]
    public void Parse_WhereNot()
    {
        var result = Parse("SELECT a FROM t WHERE NOT x = 1");
        Assert.That(result.Success, Is.True);
        var notExpr = (SqlUnaryExpr)result.SelectStatement!.Where!;
        Assert.That(notExpr.Operator, Is.EqualTo(SqlUnaryOp.Not));
    }

    // ─── Comparison operators ────────────────────────────

    [Test]
    public void Parse_ComparisonOperators()
    {
        var cases = new (string Where, SqlBinaryOp Op)[]
        {
            ("x <> 1", SqlBinaryOp.NotEqual),
            ("x != 1", SqlBinaryOp.NotEqual),
            ("x < 1", SqlBinaryOp.LessThan),
            ("x > 1", SqlBinaryOp.GreaterThan),
            ("x <= 1", SqlBinaryOp.LessThanOrEqual),
            ("x >= 1", SqlBinaryOp.GreaterThanOrEqual),
        };

        foreach (var (where, op) in cases)
        {
            var result = Parse($"SELECT a FROM t WHERE {where}");
            Assert.That(result.Success, Is.True, $"Failed for: {where}");
            var bin = (SqlBinaryExpr)result.SelectStatement!.Where!;
            Assert.That(bin.Operator, Is.EqualTo(op), $"Failed for: {where}");
        }
    }

    // ─── IN expression ──────────────────────────────────

    [Test]
    public void Parse_InExpression()
    {
        var result = Parse("SELECT a FROM t WHERE x IN (1, 2, 3)");
        Assert.That(result.Success, Is.True);
        var inExpr = (SqlInExpr)result.SelectStatement!.Where!;
        Assert.That(inExpr.IsNegated, Is.False);
        Assert.That(inExpr.Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_NotInExpression()
    {
        var result = Parse("SELECT a FROM t WHERE x NOT IN (1, 2)");
        Assert.That(result.Success, Is.True);
        var inExpr = (SqlInExpr)result.SelectStatement!.Where!;
        Assert.That(inExpr.IsNegated, Is.True);
    }

    // ─── BETWEEN ─────────────────────────────────────────

    [Test]
    public void Parse_BetweenExpression()
    {
        var result = Parse("SELECT a FROM t WHERE x BETWEEN 1 AND 10");
        Assert.That(result.Success, Is.True);
        var between = (SqlBetweenExpr)result.SelectStatement!.Where!;
        Assert.That(between.IsNegated, Is.False);
        Assert.That(((SqlLiteral)between.Low).Value, Is.EqualTo("1"));
        Assert.That(((SqlLiteral)between.High).Value, Is.EqualTo("10"));
    }

    [Test]
    public void Parse_NotBetweenExpression()
    {
        var result = Parse("SELECT a FROM t WHERE x NOT BETWEEN 1 AND 10");
        Assert.That(result.Success, Is.True);
        var between = (SqlBetweenExpr)result.SelectStatement!.Where!;
        Assert.That(between.IsNegated, Is.True);
    }

    // ─── IS NULL ─────────────────────────────────────────

    [Test]
    public void Parse_IsNull()
    {
        var result = Parse("SELECT a FROM t WHERE x IS NULL");
        Assert.That(result.Success, Is.True);
        var isNull = (SqlIsNullExpr)result.SelectStatement!.Where!;
        Assert.That(isNull.IsNegated, Is.False);
    }

    [Test]
    public void Parse_IsNotNull()
    {
        var result = Parse("SELECT a FROM t WHERE x IS NOT NULL");
        Assert.That(result.Success, Is.True);
        var isNull = (SqlIsNullExpr)result.SelectStatement!.Where!;
        Assert.That(isNull.IsNegated, Is.True);
    }

    // ─── LIKE ────────────────────────────────────────────

    [Test]
    public void Parse_LikeExpression()
    {
        var result = Parse("SELECT a FROM t WHERE name LIKE '%test%'");
        Assert.That(result.Success, Is.True);
        var like = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(like.Operator, Is.EqualTo(SqlBinaryOp.Like));
    }

    // ─── JOIN ────────────────────────────────────────────

    [Test]
    public void Parse_InnerJoin()
    {
        var result = Parse("SELECT a FROM t1 INNER JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins, Has.Count.EqualTo(1));
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Inner));
    }

    [Test]
    public void Parse_LeftJoin()
    {
        var result = Parse("SELECT a FROM t1 LEFT JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Left));
    }

    [Test]
    public void Parse_LeftOuterJoin()
    {
        var result = Parse("SELECT a FROM t1 LEFT OUTER JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Left));
    }

    [Test]
    public void Parse_RightJoin()
    {
        var result = Parse("SELECT a FROM t1 RIGHT JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Right));
    }

    [Test]
    public void Parse_CrossJoin()
    {
        var result = Parse("SELECT a FROM t1 CROSS JOIN t2");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Cross));
        Assert.That(result.SelectStatement!.Joins[0].Condition, Is.Null);
    }

    [Test]
    public void Parse_FullOuterJoin()
    {
        var result = Parse("SELECT a FROM t1 FULL OUTER JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.FullOuter));
    }

    [Test]
    public void Parse_BareJoin_TreatedAsInner()
    {
        var result = Parse("SELECT a FROM t1 JOIN t2 ON t1.id = t2.id");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Joins[0].JoinKind, Is.EqualTo(SqlJoinKind.Inner));
    }

    // ─── GROUP BY / HAVING ───────────────────────────────

    [Test]
    public void Parse_GroupBy()
    {
        var result = Parse("SELECT dept, COUNT(*) FROM t GROUP BY dept");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.GroupBy, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_GroupByHaving()
    {
        var result = Parse("SELECT dept, COUNT(*) FROM t GROUP BY dept HAVING COUNT(*) > 5");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.GroupBy, Has.Count.EqualTo(1));
        Assert.That(result.SelectStatement!.Having, Is.Not.Null);
    }

    // ─── ORDER BY ────────────────────────────────────────

    [Test]
    public void Parse_OrderBy()
    {
        var result = Parse("SELECT a FROM t ORDER BY a ASC, b DESC");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.OrderBy, Has.Count.EqualTo(2));
        Assert.That(result.SelectStatement!.OrderBy![0].IsDescending, Is.False);
        Assert.That(result.SelectStatement!.OrderBy![1].IsDescending, Is.True);
    }

    // ─── LIMIT / OFFSET ─────────────────────────────────

    [Test]
    public void Parse_Limit()
    {
        var result = Parse("SELECT a FROM t LIMIT 10");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.Limit, Is.Not.Null);
        Assert.That(((SqlLiteral)result.SelectStatement!.Limit!).Value, Is.EqualTo("10"));
    }

    [Test]
    public void Parse_LimitOffset()
    {
        var result = Parse("SELECT a FROM t LIMIT 10 OFFSET 20");
        Assert.That(result.Success, Is.True);
        Assert.That(((SqlLiteral)result.SelectStatement!.Limit!).Value, Is.EqualTo("10"));
        Assert.That(((SqlLiteral)result.SelectStatement!.Offset!).Value, Is.EqualTo("20"));
    }

    [Test]
    public void Parse_OffsetFetch_SqlServer()
    {
        var result = Parse("SELECT a FROM t ORDER BY a OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY", SqlDialect.SqlServer);
        Assert.That(result.Success, Is.True);
        Assert.That(((SqlLiteral)result.SelectStatement!.Offset!).Value, Is.EqualTo("20"));
        Assert.That(((SqlLiteral)result.SelectStatement!.Limit!).Value, Is.EqualTo("10"));
    }

    // ─── Function calls ──────────────────────────────────

    [Test]
    public void Parse_FunctionCall_CountStar()
    {
        var result = Parse("SELECT COUNT(*) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var func = (SqlFunctionCall)col.Expression;
        Assert.That(func.FunctionName, Is.EqualTo("COUNT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
        Assert.That(func.IsDistinct, Is.False);
    }

    [Test]
    public void Parse_FunctionCall_CountDistinct()
    {
        var result = Parse("SELECT COUNT(DISTINCT x) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var func = (SqlFunctionCall)col.Expression;
        Assert.That(func.FunctionName, Is.EqualTo("COUNT"));
        Assert.That(func.IsDistinct, Is.True);
    }

    [Test]
    public void Parse_FunctionCall_Coalesce()
    {
        var result = Parse("SELECT COALESCE(a, b, 0) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var func = (SqlFunctionCall)col.Expression;
        Assert.That(func.FunctionName, Is.EqualTo("COALESCE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    // ─── CASE expression ─────────────────────────────────

    [Test]
    public void Parse_CaseWhen()
    {
        var result = Parse("SELECT CASE WHEN x = 1 THEN 'one' ELSE 'other' END FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var caseExpr = (SqlCaseExpr)col.Expression;
        Assert.That(caseExpr.Operand, Is.Null); // searched CASE
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(1));
        Assert.That(caseExpr.ElseResult, Is.Not.Null);
    }

    [Test]
    public void Parse_SimpleCaseExpression()
    {
        var result = Parse("SELECT CASE status WHEN 1 THEN 'active' WHEN 2 THEN 'inactive' END FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var caseExpr = (SqlCaseExpr)col.Expression;
        Assert.That(caseExpr.Operand, Is.Not.Null); // simple CASE
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(2));
    }

    // ─── CAST expression ─────────────────────────────────

    [Test]
    public void Parse_CastExpression()
    {
        var result = Parse("SELECT CAST(x AS INTEGER) FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var cast = (SqlCastExpr)col.Expression;
        Assert.That(cast.TypeName, Is.EqualTo("INTEGER"));
    }

    // ─── Aliases ─────────────────────────────────────────

    [Test]
    public void Parse_ColumnAlias_WithAs()
    {
        var result = Parse("SELECT a AS alias1 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        Assert.That(col.Alias, Is.EqualTo("alias1"));
    }

    [Test]
    public void Parse_ColumnAlias_WithoutAs()
    {
        var result = Parse("SELECT a alias1 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        Assert.That(col.Alias, Is.EqualTo("alias1"));
    }

    [Test]
    public void Parse_TableAlias()
    {
        var result = Parse("SELECT u.name FROM users u");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.From!.Alias, Is.EqualTo("u"));
    }

    [Test]
    public void Parse_TableAlias_WithAs()
    {
        var result = Parse("SELECT u.name FROM users AS u");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.From!.Alias, Is.EqualTo("u"));
    }

    // ─── Parameters per dialect ──────────────────────────

    [Test]
    public void Parse_SqliteParameter()
    {
        var result = Parse("SELECT a FROM t WHERE x = @userId", SqlDialect.SQLite);
        Assert.That(result.Success, Is.True);
        var bin = (SqlBinaryExpr)result.SelectStatement!.Where!;
        var param = (SqlParameter)bin.Right;
        Assert.That(param.RawText, Is.EqualTo("@userId"));
    }

    [Test]
    public void Parse_PostgreSqlParameter()
    {
        var result = Parse("SELECT a FROM t WHERE x = $1", SqlDialect.PostgreSQL);
        Assert.That(result.Success, Is.True);
        var bin = (SqlBinaryExpr)result.SelectStatement!.Where!;
        var param = (SqlParameter)bin.Right;
        Assert.That(param.RawText, Is.EqualTo("$1"));
    }

    [Test]
    public void Parse_MySqlParameter()
    {
        var result = Parse("SELECT a FROM t WHERE x = ?", SqlDialect.MySQL);
        Assert.That(result.Success, Is.True);
        var bin = (SqlBinaryExpr)result.SelectStatement!.Where!;
        var param = (SqlParameter)bin.Right;
        Assert.That(param.RawText, Is.EqualTo("?"));
    }

    // ─── Qualified columns/tables ────────────────────────

    [Test]
    public void Parse_QualifiedColumn()
    {
        var result = Parse("SELECT u.name FROM users u");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var colRef = (SqlColumnRef)col.Expression;
        Assert.That(colRef.TableAlias, Is.EqualTo("u"));
        Assert.That(colRef.ColumnName, Is.EqualTo("name"));
    }

    [Test]
    public void Parse_SchemaQualifiedTable()
    {
        var result = Parse("SELECT a FROM public.users");
        Assert.That(result.Success, Is.True);
        Assert.That(result.SelectStatement!.From!.Schema, Is.EqualTo("public"));
        Assert.That(result.SelectStatement!.From!.TableName, Is.EqualTo("users"));
    }

    // ─── Nested expressions ──────────────────────────────

    [Test]
    public void Parse_NestedParens()
    {
        var result = Parse("SELECT a FROM t WHERE (x = 1 OR y = 2) AND z = 3");
        Assert.That(result.Success, Is.True);
        var and = (SqlBinaryExpr)result.SelectStatement!.Where!;
        Assert.That(and.Operator, Is.EqualTo(SqlBinaryOp.And));
        var paren = (SqlParenExpr)and.Left;
        var or = (SqlBinaryExpr)paren.Inner;
        Assert.That(or.Operator, Is.EqualTo(SqlBinaryOp.Or));
    }

    // ─── Unsupported constructs ──────────────────────────

    [Test]
    public void Parse_CTE_MarkedAsUnsupported()
    {
        var result = Parse("WITH cte AS (SELECT 1) SELECT * FROM cte");
        Assert.That(result.HasUnsupported, Is.True);
        Assert.That(result.Statement, Is.Null);
    }

    [Test]
    public void Parse_Union_MarkedAsUnsupported()
    {
        var result = Parse("SELECT a FROM t1 UNION SELECT b FROM t2");
        Assert.That(result.HasUnsupported, Is.True);
    }

    [Test]
    public void Parse_WindowFunction_MarkedAsUnsupported()
    {
        var result = Parse("SELECT ROW_NUMBER() OVER (ORDER BY id) FROM t");
        Assert.That(result.HasUnsupported, Is.True);
    }

    // ─── Error recovery ──────────────────────────────────

    [Test]
    public void Parse_EmptySql_HasDiagnostics()
    {
        var result = Parse("");
        Assert.That(result.Statement, Is.Null);
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Parse_NonSelectStatement_NowSupported()
    {
        var result = Parse("INSERT INTO t VALUES (1)");
        Assert.That(result.Statement, Is.Not.Null);
        Assert.That(result.Statement, Is.TypeOf<SqlInsertStatement>());
    }

    [Test]
    public void Parse_UnknownStatement_HasDiagnostics()
    {
        var result = Parse("TRUNCATE TABLE t");
        Assert.That(result.Statement, Is.Null);
        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
    }

    // ─── Trailing semicolons ─────────────────────────────

    [Test]
    public void Parse_TrailingSemicolon_Accepted()
    {
        var result = Parse("SELECT a FROM t;");
        Assert.That(result.Success, Is.True);
    }

    // ─── EXISTS subquery ─────────────────────────────────

    [Test]
    public void Parse_ExistsSubquery()
    {
        var result = Parse("SELECT a FROM t WHERE EXISTS (SELECT 1 FROM t2 WHERE t2.id = t.id)");
        Assert.That(result.Success, Is.True);
        var exists = (SqlExistsExpr)result.SelectStatement!.Where!;
        Assert.That(exists.Subquery, Is.Not.Null);
        Assert.That(exists.Subquery.From!.TableName, Is.EqualTo("t2"));
    }

    // ─── Arithmetic in expressions ───────────────────────

    [Test]
    public void Parse_ArithmeticPrecedence()
    {
        // 1 + 2 * 3 should be Add(1, Mul(2, 3))
        var result = Parse("SELECT 1 + 2 * 3 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var add = (SqlBinaryExpr)col.Expression;
        Assert.That(add.Operator, Is.EqualTo(SqlBinaryOp.Add));
        var mul = (SqlBinaryExpr)add.Right;
        Assert.That(mul.Operator, Is.EqualTo(SqlBinaryOp.Multiply));
    }

    // ─── Unary minus ─────────────────────────────────────

    [Test]
    public void Parse_UnaryMinus()
    {
        var result = Parse("SELECT -1 FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var unary = (SqlUnaryExpr)col.Expression;
        Assert.That(unary.Operator, Is.EqualTo(SqlUnaryOp.Negate));
    }

    // ─── String literals ─────────────────────────────────

    [Test]
    public void Parse_StringLiteral()
    {
        var result = Parse("SELECT 'hello' FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var lit = (SqlLiteral)col.Expression;
        Assert.That(lit.LiteralKind, Is.EqualTo(SqlLiteralKind.String));
        Assert.That(lit.Value, Is.EqualTo("'hello'"));
    }

    // ─── NULL literal ────────────────────────────────────

    [Test]
    public void Parse_NullLiteral()
    {
        var result = Parse("SELECT NULL FROM t");
        Assert.That(result.Success, Is.True);
        var col = (SqlSelectColumn)result.SelectStatement!.Columns[0];
        var lit = (SqlLiteral)col.Expression;
        Assert.That(lit.LiteralKind, Is.EqualTo(SqlLiteralKind.Null));
    }
}
