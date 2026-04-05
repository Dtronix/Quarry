using System;
using System.Collections.Generic;

#if QUARRY_GENERATOR
using Quarry.Generators.Sql;
namespace Quarry.Generators.Sql.Parser;
#else
using Quarry;
namespace Quarry.Shared.Sql.Parser;
#endif

/// <summary>
/// Recursive-descent SQL parser. Produces an AST from a SQL SELECT statement.
/// </summary>
internal sealed class SqlParser
{
    private readonly string _sql;
    private readonly SqlDialect _dialect;
    private readonly List<SqlToken> _tokens;
    private readonly List<SqlParseDiagnostic> _diagnostics = new List<SqlParseDiagnostic>();
    private int _pos;
    private bool _hasUnsupported;

    private SqlParser(string sql, SqlDialect dialect, List<SqlToken> tokens)
    {
        _sql = sql;
        _dialect = dialect;
        _tokens = tokens;
    }

    /// <summary>
    /// Parses a SQL SELECT statement string and returns the AST.
    /// </summary>
    public static SqlParseResult Parse(string sql, SqlDialect dialect)
    {
        if (sql is null) throw new ArgumentNullException(nameof(sql));

        var tokens = SqlTokenizer.Tokenize(sql, dialect);
        var parser = new SqlParser(sql, dialect, tokens);
        return parser.ParseRoot();
    }

    // ─── Helpers ─────────────────────────────────────────

    private SqlToken Current => _tokens[_pos];

    private SqlToken Peek(int offset = 0) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[_tokens.Count - 1];

    private SqlToken Advance()
    {
        var token = _tokens[_pos];
        if (_pos < _tokens.Count - 1) _pos++;
        return token;
    }

    private bool Check(SqlTokenKind kind) => Current.Kind == kind;

    private bool Match(SqlTokenKind kind)
    {
        if (Current.Kind != kind) return false;
        Advance();
        return true;
    }

    private SqlToken Expect(SqlTokenKind kind)
    {
        if (Current.Kind == kind) return Advance();
        AddDiagnostic($"Expected {kind}, got {Current.Kind}");
        // Advance past the unexpected token to prevent infinite loops
        return Advance();
    }

    private string TokenText(SqlToken token) => token.GetTextString(_sql);

    private void AddDiagnostic(string message)
    {
        var token = Current;
        _diagnostics.Add(new SqlParseDiagnostic(token.Start, token.Length, message));
    }

    /// <summary>
    /// Reads an identifier name. Handles both plain identifiers and quoted identifiers.
    /// </summary>
    private string ReadIdentifierName()
    {
        if (Current.Kind == SqlTokenKind.QuotedIdentifier)
        {
            var text = TokenText(Advance());
            // Strip quotes: "name", `name`, [name]
            if (text.Length >= 2)
            {
                if ((text[0] == '"' && text[text.Length - 1] == '"') ||
                    (text[0] == '`' && text[text.Length - 1] == '`'))
                    return text.Substring(1, text.Length - 2);
                if (text[0] == '[' && text[text.Length - 1] == ']')
                    return text.Substring(1, text.Length - 2);
            }
            return text;
        }

        // Accept keywords as identifiers when used as names
        if (Current.Kind == SqlTokenKind.Identifier || IsKeywordUsableAsIdentifier(Current.Kind))
        {
            return TokenText(Advance());
        }

        AddDiagnostic($"Expected identifier, got {Current.Kind}");
        return TokenText(Advance());
    }

    private static bool IsKeywordUsableAsIdentifier(SqlTokenKind kind)
    {
        // SQL keywords commonly used as column or table names.
        // Excludes structural keywords that would break the parser
        // (SELECT, FROM, WHERE, JOIN, ON, etc.).
        switch (kind)
        {
            case SqlTokenKind.Asc:
            case SqlTokenKind.Desc:
            case SqlTokenKind.All:
            case SqlTokenKind.Null:
            case SqlTokenKind.True:
            case SqlTokenKind.False:
            case SqlTokenKind.Only:
            case SqlTokenKind.Row:
            case SqlTokenKind.Rows:
            case SqlTokenKind.Next:
            case SqlTokenKind.Fetch:
            case SqlTokenKind.First:
            case SqlTokenKind.Over:
            case SqlTokenKind.Exists:
            case SqlTokenKind.Between:
            case SqlTokenKind.Like:
            case SqlTokenKind.Cast:
            case SqlTokenKind.Case:
            case SqlTokenKind.When:
            case SqlTokenKind.Then:
            case SqlTokenKind.Else:
            case SqlTokenKind.End:
            case SqlTokenKind.Limit:
            case SqlTokenKind.Offset:
            case SqlTokenKind.Distinct:
                return true;
            default:
                return false;
        }
    }

    // ─── Root ────────────────────────────────────────────

    private SqlParseResult ParseRoot()
    {
        // Skip leading semicolons
        while (Match(SqlTokenKind.Semicolon)) { }

        if (Check(SqlTokenKind.Eof))
        {
            AddDiagnostic("Empty SQL statement");
            return new SqlParseResult(null, _diagnostics, _hasUnsupported);
        }

        // CTE detection: WITH ... AS
        if (Check(SqlTokenKind.With))
        {
            _hasUnsupported = true;
            var rawText = _sql;
            return new SqlParseResult(null, _diagnostics, true);
        }

        if (!Check(SqlTokenKind.Select))
        {
            AddDiagnostic($"Expected SELECT, got {Current.Kind}");
            return new SqlParseResult(null, _diagnostics, _hasUnsupported);
        }

        var stmt = ParseSelectStatement();

        // Skip trailing semicolons
        while (Match(SqlTokenKind.Semicolon)) { }

        // Check for UNION/INTERSECT/EXCEPT after the statement
        if (Check(SqlTokenKind.Union) || Check(SqlTokenKind.Intersect) || Check(SqlTokenKind.Except))
        {
            _hasUnsupported = true;
            AddDiagnostic($"Set operations ({Current.Kind}) are not yet supported");
        }

        if (!Check(SqlTokenKind.Eof) && _diagnostics.Count == 0)
        {
            AddDiagnostic($"Unexpected token after statement: {Current.Kind}");
        }

        return new SqlParseResult(stmt, _diagnostics, _hasUnsupported);
    }

    // ─── SELECT statement ────────────────────────────────

    private SqlSelectStatement ParseSelectStatement()
    {
        Expect(SqlTokenKind.Select);

        // DISTINCT
        var isDistinct = Match(SqlTokenKind.Distinct);

        // Columns
        var columns = ParseSelectColumns();

        // FROM
        SqlTableSource? from = null;
        var joins = new List<SqlJoin>();
        if (Match(SqlTokenKind.From))
        {
            from = ParseTableSource();

            // Comma-separated tables: FROM t1, t2, t3 → implicit CROSS JOINs
            while (Match(SqlTokenKind.Comma))
            {
                var nextTable = ParseTableSource();
                joins.Add(new SqlJoin(SqlJoinKind.Cross, nextTable, null));
            }

            joins.AddRange(ParseJoins());
        }

        // WHERE
        SqlExpr? where = null;
        if (Match(SqlTokenKind.Where))
            where = ParseExpression();

        // GROUP BY
        List<SqlExpr>? groupBy = null;
        if (Check(SqlTokenKind.Group) && Peek(1).Kind == SqlTokenKind.By)
        {
            Advance(); // GROUP
            Advance(); // BY
            groupBy = ParseExpressionList();
        }

        // HAVING
        SqlExpr? having = null;
        if (Match(SqlTokenKind.Having))
            having = ParseExpression();

        // ORDER BY
        List<SqlOrderTerm>? orderBy = null;
        if (Check(SqlTokenKind.Order) && Peek(1).Kind == SqlTokenKind.By)
        {
            Advance(); // ORDER
            Advance(); // BY
            orderBy = ParseOrderByTerms();
        }

        // LIMIT / OFFSET (dialect-aware)
        SqlExpr? limit = null;
        SqlExpr? offset = null;
        ParseLimitOffset(ref limit, ref offset);

        return new SqlSelectStatement(
            isDistinct, columns, from, joins,
            where, groupBy, having, orderBy, limit, offset);
    }

    // ─── SELECT columns ─────────────────────────────────

    private List<SqlNode> ParseSelectColumns()
    {
        var columns = new List<SqlNode>();

        do
        {
            // Check for * or table.*
            if (Check(SqlTokenKind.Star))
            {
                Advance();
                columns.Add(new SqlStarColumn(null));
                continue;
            }

            // Check for table.* pattern: identifier DOT STAR
            if ((Check(SqlTokenKind.Identifier) || Check(SqlTokenKind.QuotedIdentifier))
                && Peek(1).Kind == SqlTokenKind.Dot
                && Peek(2).Kind == SqlTokenKind.Star)
            {
                var tableName = ReadIdentifierName();
                Advance(); // DOT
                Advance(); // STAR
                columns.Add(new SqlStarColumn(tableName));
                continue;
            }

            var expr = ParseExpression();

            // Check for window function (OVER)
            if (Check(SqlTokenKind.Over))
            {
                _hasUnsupported = true;
                // Skip the OVER(...) clause
                var overStart = Current.Start;
                Advance(); // OVER
                if (Match(SqlTokenKind.OpenParen))
                {
                    SkipBalancedParens();
                }
                var overEnd = _pos < _tokens.Count ? _tokens[_pos - 1].Start + _tokens[_pos - 1].Length : _sql.Length;
                expr = new SqlUnsupported(_sql.Substring(overStart, overEnd - overStart));
            }

            // Optional alias: [AS] name
            string? alias = null;
            if (Match(SqlTokenKind.As))
            {
                alias = ReadIdentifierName();
            }
            else if (Check(SqlTokenKind.Identifier) || Check(SqlTokenKind.QuotedIdentifier))
            {
                // Implicit alias (no AS keyword) — only if not a clause keyword
                alias = ReadIdentifierName();
            }

            columns.Add(new SqlSelectColumn(expr, alias));

        } while (Match(SqlTokenKind.Comma));

        return columns;
    }

    // ─── FROM / JOIN ─────────────────────────────────────

    private SqlTableSource ParseTableSource()
    {
        // Check for subquery in FROM
        if (Check(SqlTokenKind.OpenParen))
        {
            _hasUnsupported = true;
            var start = Current.Start;
            Advance(); // (
            SkipBalancedParens();
            var end = _pos < _tokens.Count ? _tokens[_pos - 1].Start + _tokens[_pos - 1].Length : _sql.Length;
            var rawText = _sql.Substring(start, end - start);

            // Optional alias after subquery
            string? subAlias = null;
            if (Match(SqlTokenKind.As))
                subAlias = ReadIdentifierName();
            else if (Check(SqlTokenKind.Identifier) || Check(SqlTokenKind.QuotedIdentifier))
                subAlias = ReadIdentifierName();

            return new SqlTableSource("(" + rawText + ")", null, subAlias);
        }

        var name = ReadIdentifierName();

        // Schema-qualified: schema.table
        string? schema = null;
        if (Match(SqlTokenKind.Dot))
        {
            schema = name;
            name = ReadIdentifierName();
        }

        // Optional alias
        string? alias = null;
        if (Match(SqlTokenKind.As))
        {
            alias = ReadIdentifierName();
        }
        else if (Check(SqlTokenKind.Identifier) || Check(SqlTokenKind.QuotedIdentifier))
        {
            // Implicit alias — only if not a clause keyword
            alias = ReadIdentifierName();
        }

        return new SqlTableSource(name, schema, alias);
    }

    private List<SqlJoin> ParseJoins()
    {
        var joins = new List<SqlJoin>();

        while (true)
        {
            var joinKind = TryParseJoinKind();
            if (joinKind == null) break;

            var table = ParseTableSource();
            SqlExpr? condition = null;

            // CROSS JOIN has no ON clause
            if (joinKind != SqlJoinKind.Cross)
            {
                Expect(SqlTokenKind.On);
                condition = ParseExpression();
            }

            joins.Add(new SqlJoin(joinKind.Value, table, condition));
        }

        return joins;
    }

    private SqlJoinKind? TryParseJoinKind()
    {
        if (Check(SqlTokenKind.Inner))
        {
            Advance();
            Expect(SqlTokenKind.Join);
            return SqlJoinKind.Inner;
        }
        if (Check(SqlTokenKind.Left))
        {
            Advance();
            Match(SqlTokenKind.Outer); // optional OUTER
            Expect(SqlTokenKind.Join);
            return SqlJoinKind.Left;
        }
        if (Check(SqlTokenKind.Right))
        {
            Advance();
            Match(SqlTokenKind.Outer); // optional OUTER
            Expect(SqlTokenKind.Join);
            return SqlJoinKind.Right;
        }
        if (Check(SqlTokenKind.Cross))
        {
            Advance();
            Expect(SqlTokenKind.Join);
            return SqlJoinKind.Cross;
        }
        if (Check(SqlTokenKind.Full))
        {
            Advance();
            Match(SqlTokenKind.Outer); // optional OUTER
            Expect(SqlTokenKind.Join);
            return SqlJoinKind.FullOuter;
        }
        if (Check(SqlTokenKind.Join))
        {
            Advance();
            return SqlJoinKind.Inner; // bare JOIN = INNER JOIN
        }
        return null;
    }

    // ─── LIMIT / OFFSET (dialect-aware) ──────────────────

    private void ParseLimitOffset(ref SqlExpr? limit, ref SqlExpr? offset)
    {
        // Standard: LIMIT n [OFFSET n]
        if (Match(SqlTokenKind.Limit))
        {
            limit = ParsePrimaryExpr();
            if (Match(SqlTokenKind.Offset))
                offset = ParsePrimaryExpr();
        }

        // SQL Server: OFFSET n ROWS FETCH NEXT|FIRST n ROWS ONLY
        if (Match(SqlTokenKind.Offset))
        {
            offset = ParsePrimaryExpr();
            if (!Match(SqlTokenKind.Rows))
                Match(SqlTokenKind.Row);

            if (Match(SqlTokenKind.Fetch))
            {
                if (!Match(SqlTokenKind.Next))
                    Match(SqlTokenKind.First);
                limit = ParsePrimaryExpr();
                if (!Match(SqlTokenKind.Rows))
                    Match(SqlTokenKind.Row);
                Match(SqlTokenKind.Only);
            }
        }
    }

    // ─── ORDER BY ────────────────────────────────────────

    private List<SqlOrderTerm> ParseOrderByTerms()
    {
        var terms = new List<SqlOrderTerm>();
        do
        {
            var expr = ParseExpression();
            var descending = false;
            if (Match(SqlTokenKind.Desc))
                descending = true;
            else
                Match(SqlTokenKind.Asc); // consume optional ASC

            terms.Add(new SqlOrderTerm(expr, descending));
        } while (Match(SqlTokenKind.Comma));

        return terms;
    }

    // ─── Expression list ─────────────────────────────────

    private List<SqlExpr> ParseExpressionList()
    {
        var exprs = new List<SqlExpr>();
        do
        {
            exprs.Add(ParseExpression());
        } while (Match(SqlTokenKind.Comma));
        return exprs;
    }

    // ─── Expressions (precedence climbing) ───────────────

    private SqlExpr ParseExpression() => ParseOrExpr();

    private SqlExpr ParseOrExpr()
    {
        var left = ParseAndExpr();
        while (Match(SqlTokenKind.Or))
        {
            var right = ParseAndExpr();
            left = new SqlBinaryExpr(left, SqlBinaryOp.Or, right);
        }
        return left;
    }

    private SqlExpr ParseAndExpr()
    {
        var left = ParseNotExpr();
        while (Match(SqlTokenKind.And))
        {
            var right = ParseNotExpr();
            left = new SqlBinaryExpr(left, SqlBinaryOp.And, right);
        }
        return left;
    }

    private SqlExpr ParseNotExpr()
    {
        if (Match(SqlTokenKind.Not))
        {
            var operand = ParseNotExpr();
            return new SqlUnaryExpr(SqlUnaryOp.Not, operand);
        }
        return ParseComparisonExpr();
    }

    private SqlExpr ParseComparisonExpr()
    {
        var left = ParseAddExpr();

        // Postfix operators: IS [NOT] NULL, [NOT] IN (...), [NOT] BETWEEN, [NOT] LIKE
        while (true)
        {
            if (Check(SqlTokenKind.Is))
            {
                Advance(); // IS
                var negated = Match(SqlTokenKind.Not);
                Expect(SqlTokenKind.Null);
                left = new SqlIsNullExpr(left, negated);
                continue;
            }

            if (Check(SqlTokenKind.Not) && Peek(1).Kind == SqlTokenKind.In)
            {
                Advance(); // NOT
                Advance(); // IN
                Expect(SqlTokenKind.OpenParen);
                var values = ParseExpressionList();
                Expect(SqlTokenKind.CloseParen);
                left = new SqlInExpr(left, values, true);
                continue;
            }

            if (Check(SqlTokenKind.In))
            {
                Advance(); // IN
                Expect(SqlTokenKind.OpenParen);
                var values = ParseExpressionList();
                Expect(SqlTokenKind.CloseParen);
                left = new SqlInExpr(left, values, false);
                continue;
            }

            if (Check(SqlTokenKind.Not) && Peek(1).Kind == SqlTokenKind.Between)
            {
                Advance(); // NOT
                Advance(); // BETWEEN
                var low = ParseAddExpr();
                Expect(SqlTokenKind.And);
                var high = ParseAddExpr();
                left = new SqlBetweenExpr(left, low, high, true);
                continue;
            }

            if (Check(SqlTokenKind.Between))
            {
                Advance(); // BETWEEN
                var low = ParseAddExpr();
                Expect(SqlTokenKind.And);
                var high = ParseAddExpr();
                left = new SqlBetweenExpr(left, low, high, false);
                continue;
            }

            if (Check(SqlTokenKind.Not) && Peek(1).Kind == SqlTokenKind.Like)
            {
                Advance(); // NOT
                Advance(); // LIKE
                var right = ParseAddExpr();
                left = new SqlUnaryExpr(SqlUnaryOp.Not, new SqlBinaryExpr(left, SqlBinaryOp.Like, right));
                continue;
            }

            if (Check(SqlTokenKind.Like))
            {
                Advance(); // LIKE
                var right = ParseAddExpr();
                left = new SqlBinaryExpr(left, SqlBinaryOp.Like, right);
                continue;
            }

            // Standard comparison operators
            SqlBinaryOp? op = null;
            if (Check(SqlTokenKind.Equal)) op = SqlBinaryOp.Equal;
            else if (Check(SqlTokenKind.NotEqual)) op = SqlBinaryOp.NotEqual;
            else if (Check(SqlTokenKind.LessThan)) op = SqlBinaryOp.LessThan;
            else if (Check(SqlTokenKind.GreaterThan)) op = SqlBinaryOp.GreaterThan;
            else if (Check(SqlTokenKind.LessThanOrEqual)) op = SqlBinaryOp.LessThanOrEqual;
            else if (Check(SqlTokenKind.GreaterThanOrEqual)) op = SqlBinaryOp.GreaterThanOrEqual;

            if (op != null)
            {
                Advance();
                var right = ParseAddExpr();
                left = new SqlBinaryExpr(left, op.Value, right);
                continue;
            }

            break;
        }

        return left;
    }

    private SqlExpr ParseAddExpr()
    {
        var left = ParseMulExpr();
        while (true)
        {
            SqlBinaryOp? op = null;
            if (Check(SqlTokenKind.Plus)) op = SqlBinaryOp.Add;
            else if (Check(SqlTokenKind.Minus)) op = SqlBinaryOp.Subtract;

            if (op == null) break;
            Advance();
            var right = ParseMulExpr();
            left = new SqlBinaryExpr(left, op.Value, right);
        }
        return left;
    }

    private SqlExpr ParseMulExpr()
    {
        var left = ParseUnaryExpr();
        while (true)
        {
            SqlBinaryOp? op = null;
            if (Check(SqlTokenKind.Star)) op = SqlBinaryOp.Multiply;
            else if (Check(SqlTokenKind.Slash)) op = SqlBinaryOp.Divide;
            else if (Check(SqlTokenKind.Percent)) op = SqlBinaryOp.Modulo;

            if (op == null) break;
            Advance();
            var right = ParseUnaryExpr();
            left = new SqlBinaryExpr(left, op.Value, right);
        }
        return left;
    }

    private SqlExpr ParseUnaryExpr()
    {
        if (Check(SqlTokenKind.Minus))
        {
            Advance();
            var operand = ParsePrimaryExpr();
            return new SqlUnaryExpr(SqlUnaryOp.Negate, operand);
        }
        return ParsePrimaryExpr();
    }

    private SqlExpr ParsePrimaryExpr()
    {
        // Number literal
        if (Check(SqlTokenKind.Number))
        {
            var text = TokenText(Advance());
            return new SqlLiteral(text, SqlLiteralKind.Number);
        }

        // String literal
        if (Check(SqlTokenKind.String))
        {
            var text = TokenText(Advance());
            return new SqlLiteral(text, SqlLiteralKind.String);
        }

        // Boolean literal
        if (Check(SqlTokenKind.True))
        {
            Advance();
            return new SqlLiteral("TRUE", SqlLiteralKind.Boolean);
        }
        if (Check(SqlTokenKind.False))
        {
            Advance();
            return new SqlLiteral("FALSE", SqlLiteralKind.Boolean);
        }

        // NULL literal
        if (Check(SqlTokenKind.Null))
        {
            Advance();
            return new SqlLiteral("NULL", SqlLiteralKind.Null);
        }

        // Parameter
        if (Check(SqlTokenKind.Parameter))
        {
            var text = TokenText(Advance());
            return new SqlParameter(text);
        }

        // CASE expression
        if (Check(SqlTokenKind.Case))
            return ParseCaseExpr();

        // CAST expression
        if (Check(SqlTokenKind.Cast))
            return ParseCastExpr();

        // EXISTS (subquery)
        if (Check(SqlTokenKind.Exists))
        {
            Advance(); // EXISTS
            Expect(SqlTokenKind.OpenParen);
            if (Check(SqlTokenKind.Select))
            {
                var subquery = ParseSelectStatement();
                Expect(SqlTokenKind.CloseParen);
                return new SqlExistsExpr(subquery);
            }
            else
            {
                // Not a select — mark unsupported
                _hasUnsupported = true;
                SkipBalancedParens();
                return new SqlUnsupported("EXISTS(...)");
            }
        }

        // Star in expression context (e.g., COUNT(*))
        if (Check(SqlTokenKind.Star))
        {
            Advance();
            return new SqlColumnRef(null, "*");
        }

        // Parenthesized expression or subquery
        if (Check(SqlTokenKind.OpenParen))
        {
            Advance(); // (
            if (Check(SqlTokenKind.Select))
            {
                // Subquery in expression context
                _hasUnsupported = true;
                var subStart = _tokens[_pos].Start;
                // Skip until matching close paren
                SkipBalancedParens();
                var subEnd = _pos < _tokens.Count ? _tokens[_pos - 1].Start + _tokens[_pos - 1].Length : _sql.Length;
                return new SqlUnsupported(_sql.Substring(subStart, subEnd - subStart));
            }
            var inner = ParseExpression();
            Expect(SqlTokenKind.CloseParen);
            return new SqlParenExpr(inner);
        }

        // Quoted identifier — column reference
        if (Check(SqlTokenKind.QuotedIdentifier))
        {
            var name = ReadIdentifierName();
            if (Match(SqlTokenKind.Dot))
            {
                var colName = ReadIdentifierName();
                return new SqlColumnRef(name, colName);
            }
            return new SqlColumnRef(null, name);
        }

        // Identifier — could be column reference, table.column, or function call
        if (Check(SqlTokenKind.Identifier))
        {
            var name = TokenText(Advance());

            // Function call: name(...)
            if (Check(SqlTokenKind.OpenParen))
            {
                Advance(); // (

                var isDistinct = Match(SqlTokenKind.Distinct);

                var args = new List<SqlExpr>();
                if (!Check(SqlTokenKind.CloseParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Match(SqlTokenKind.Comma));
                }
                Expect(SqlTokenKind.CloseParen);

                var funcExpr = new SqlFunctionCall(name, args, isDistinct);

                // Check for window function: OVER(...)
                if (Check(SqlTokenKind.Over))
                {
                    _hasUnsupported = true;
                    // Capture from function name start through the end of OVER(...)
                    var funcStart = _tokens[_pos].Start - name.Length;
                    // Walk back to find the function name token start
                    for (var i = _pos - 1; i >= 0; i--)
                    {
                        if (TokenText(_tokens[i]) == name)
                        {
                            funcStart = _tokens[i].Start;
                            break;
                        }
                    }
                    Advance(); // OVER
                    if (Match(SqlTokenKind.OpenParen))
                        SkipBalancedParens();
                    var overEnd = _pos > 0 ? _tokens[_pos - 1].Start + _tokens[_pos - 1].Length : _sql.Length;
                    return new SqlUnsupported(_sql.Substring(funcStart, overEnd - funcStart));
                }

                return funcExpr;
            }

            // table.column or table.*
            if (Match(SqlTokenKind.Dot))
            {
                if (Check(SqlTokenKind.Star))
                {
                    Advance();
                    return new SqlColumnRef(name, "*");
                }
                var colName = ReadIdentifierName();
                return new SqlColumnRef(name, colName);
            }

            return new SqlColumnRef(null, name);
        }

        // Soft keyword used as identifier (column ref or function call)
        if (IsKeywordUsableAsIdentifier(Current.Kind))
        {
            var name = TokenText(Advance());

            // Function call: name(...)
            if (Check(SqlTokenKind.OpenParen))
            {
                Advance(); // (
                var isDistinct = Match(SqlTokenKind.Distinct);
                var args = new List<SqlExpr>();
                if (!Check(SqlTokenKind.CloseParen))
                {
                    do
                    {
                        args.Add(ParseExpression());
                    } while (Match(SqlTokenKind.Comma));
                }
                Expect(SqlTokenKind.CloseParen);
                return new SqlFunctionCall(name, args, isDistinct);
            }

            // table.column
            if (Match(SqlTokenKind.Dot))
            {
                if (Check(SqlTokenKind.Star))
                {
                    Advance();
                    return new SqlColumnRef(name, "*");
                }
                var colName = ReadIdentifierName();
                return new SqlColumnRef(name, colName);
            }

            return new SqlColumnRef(null, name);
        }

        // Unexpected token
        AddDiagnostic($"Unexpected token in expression: {Current.Kind}");
        Advance(); // skip to avoid infinite loop
        return new SqlLiteral("", SqlLiteralKind.Null);
    }

    // ─── CASE expression ─────────────────────────────────

    private SqlExpr ParseCaseExpr()
    {
        Advance(); // CASE

        // Simple CASE: CASE operand WHEN value THEN result ...
        // Searched CASE: CASE WHEN condition THEN result ...
        SqlExpr? operand = null;
        if (!Check(SqlTokenKind.When))
            operand = ParseExpression();

        var whenClauses = new List<SqlWhenClause>();
        while (Match(SqlTokenKind.When))
        {
            var condition = ParseExpression();
            Expect(SqlTokenKind.Then);
            var result = ParseExpression();
            whenClauses.Add(new SqlWhenClause(condition, result));
        }

        SqlExpr? elseResult = null;
        if (Match(SqlTokenKind.Else))
            elseResult = ParseExpression();

        Expect(SqlTokenKind.End);
        return new SqlCaseExpr(operand, whenClauses, elseResult);
    }

    // ─── CAST expression ─────────────────────────────────

    private SqlExpr ParseCastExpr()
    {
        Advance(); // CAST
        Expect(SqlTokenKind.OpenParen);
        var expr = ParseExpression();
        Expect(SqlTokenKind.As);

        // Read type name — may be multi-token (e.g., "CHARACTER VARYING", "DOUBLE PRECISION")
        var typeStart = Current.Start;
        while (!Check(SqlTokenKind.CloseParen) && !Check(SqlTokenKind.Eof))
            Advance();
        var typeEnd = Current.Start;
        var typeName = _sql.Substring(typeStart, typeEnd - typeStart).Trim();

        Expect(SqlTokenKind.CloseParen);
        return new SqlCastExpr(expr, typeName);
    }

    // ─── Paren balancing ─────────────────────────────────

    /// <summary>
    /// Skips tokens until the matching close parenthesis is found.
    /// Assumes the open paren has already been consumed.
    /// </summary>
    private void SkipBalancedParens()
    {
        var depth = 1;
        while (depth > 0 && !Check(SqlTokenKind.Eof))
        {
            if (Check(SqlTokenKind.OpenParen)) depth++;
            if (Check(SqlTokenKind.CloseParen)) depth--;
            if (depth > 0) Advance();
        }
        if (Check(SqlTokenKind.CloseParen))
            Advance(); // consume the closing paren
    }
}
