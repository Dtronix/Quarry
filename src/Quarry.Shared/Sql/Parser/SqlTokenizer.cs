using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if QUARRY_GENERATOR
using Quarry.Generators.Sql;
namespace Quarry.Generators.Sql.Parser;
#else
using Quarry;
namespace Quarry.Shared.Sql.Parser;
#endif

/// <summary>
/// Dialect-aware SQL tokenizer. Scans the input once and produces a list of <see cref="SqlToken"/> values.
/// </summary>
internal static class SqlTokenizer
{
    /// <summary>
    /// Tokenizes the full SQL string into a list of tokens.
    /// The final token is always <see cref="SqlTokenKind.Eof"/>.
    /// </summary>
    public static List<SqlToken> Tokenize(string sql, SqlDialect dialect)
    {
        if (sql is null) throw new ArgumentNullException(nameof(sql));

        var tokens = new List<SqlToken>();
        var span = sql.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(span[pos]))
            {
                pos++;
                continue;
            }

            // Skip single-line comment: --
            if (pos + 1 < span.Length && span[pos] == '-' && span[pos + 1] == '-')
            {
                pos += 2;
                while (pos < span.Length && span[pos] != '\n')
                    pos++;
                continue;
            }

            // Skip block comment: /* ... */
            if (pos + 1 < span.Length && span[pos] == '/' && span[pos + 1] == '*')
            {
                pos += 2;
                while (pos + 1 < span.Length && !(span[pos] == '*' && span[pos + 1] == '/'))
                    pos++;
                if (pos + 1 < span.Length) pos += 2; // skip */
                continue;
            }

            var token = ReadToken(span, ref pos, dialect);
            tokens.Add(token);
        }

        tokens.Add(new SqlToken(SqlTokenKind.Eof, pos, 0));
        return tokens;
    }

    private static SqlToken ReadToken(ReadOnlySpan<char> sql, ref int pos, SqlDialect dialect)
    {
        var ch = sql[pos];

        // ── String literal ───────────────────────────────
        if (ch == '\'')
            return ReadStringLiteral(sql, ref pos, dialect);

        // ── Quoted identifier ────────────────────────────
        if (ch == '"' || (ch == '`' && dialect == SqlDialect.MySQL) ||
            (ch == '[' && dialect == SqlDialect.SqlServer))
            return ReadQuotedIdentifier(sql, ref pos, dialect);

        // ── Parameter ────────────────────────────────────
        if (ch == '?' && dialect == SqlDialect.MySQL)
            return MakeSingle(SqlTokenKind.Parameter, ref pos);

        if (ch == '@' && (dialect == SqlDialect.SQLite || dialect == SqlDialect.SqlServer))
            return ReadPrefixedParameter(sql, ref pos);

        if (ch == '$' && dialect == SqlDialect.PostgreSQL)
            return ReadPrefixedParameter(sql, ref pos);

        // ── Numeric literal ──────────────────────────────
        if (char.IsDigit(ch))
            return ReadNumber(sql, ref pos);

        // ── Identifier / keyword ─────────────────────────
        if (IsIdentStart(ch))
            return ReadIdentifierOrKeyword(sql, ref pos);

        // ── Operators & punctuation ──────────────────────
        return ReadOperator(sql, ref pos);
    }

    // ─── Literal readers ─────────────────────────────────

    private static SqlToken ReadStringLiteral(ReadOnlySpan<char> sql, ref int pos, SqlDialect dialect)
    {
        var start = pos;
        pos++; // skip opening quote
        while (pos < sql.Length)
        {
            if (sql[pos] == '\'')
            {
                pos++;
                // escaped quote: ''
                if (pos < sql.Length && sql[pos] == '\'')
                {
                    pos++;
                    continue;
                }
                break;
            }
            // MySQL-only backslash escape (e.g., \' , \\ , \n)
            if (dialect == SqlDialect.MySQL && sql[pos] == '\\' && pos + 1 < sql.Length)
            {
                pos += 2;
                continue;
            }
            pos++;
        }
        return new SqlToken(SqlTokenKind.String, start, pos - start);
    }

    private static SqlToken ReadQuotedIdentifier(ReadOnlySpan<char> sql, ref int pos, SqlDialect dialect)
    {
        var start = pos;
        var openChar = sql[pos];
        var closeChar = openChar;
        if (openChar == '[') closeChar = ']';
        pos++; // skip opening quote

        while (pos < sql.Length)
        {
            if (sql[pos] == closeChar)
            {
                pos++;
                // doubled close char is an escape (e.g., "" in ANSI, ]] in SqlServer)
                if (pos < sql.Length && sql[pos] == closeChar && closeChar != ']')
                {
                    pos++;
                    continue;
                }
                break;
            }
            pos++;
        }
        return new SqlToken(SqlTokenKind.QuotedIdentifier, start, pos - start);
    }

    private static SqlToken ReadPrefixedParameter(ReadOnlySpan<char> sql, ref int pos)
    {
        var start = pos;
        pos++; // skip @ or $
        while (pos < sql.Length && (char.IsLetterOrDigit(sql[pos]) || sql[pos] == '_'))
            pos++;
        return new SqlToken(SqlTokenKind.Parameter, start, pos - start);
    }

    private static SqlToken ReadNumber(ReadOnlySpan<char> sql, ref int pos)
    {
        var start = pos;
        while (pos < sql.Length && char.IsDigit(sql[pos]))
            pos++;
        // decimal part
        if (pos < sql.Length && sql[pos] == '.' && pos + 1 < sql.Length && char.IsDigit(sql[pos + 1]))
        {
            pos++; // skip dot
            while (pos < sql.Length && char.IsDigit(sql[pos]))
                pos++;
        }
        return new SqlToken(SqlTokenKind.Number, start, pos - start);
    }

    // ─── Identifier / keyword reader ─────────────────────

    private static SqlToken ReadIdentifierOrKeyword(ReadOnlySpan<char> sql, ref int pos)
    {
        var start = pos;
        while (pos < sql.Length && IsIdentPart(sql[pos]))
            pos++;

        var text = sql.Slice(start, pos - start);
        var kind = ClassifyKeyword(text);
        return new SqlToken(kind, start, pos - start);
    }

    /// <summary>
    /// Case-insensitive keyword classification. Falls back to <see cref="SqlTokenKind.Identifier"/>.
    /// </summary>
    internal static SqlTokenKind ClassifyKeyword(ReadOnlySpan<char> text)
    {
        // Fast path: switch on length then compare
        switch (text.Length)
        {
            case 2:
                if (SpanEqualsIgnoreCase(text, "AS")) return SqlTokenKind.As;
                if (SpanEqualsIgnoreCase(text, "BY")) return SqlTokenKind.By;
                if (SpanEqualsIgnoreCase(text, "IN")) return SqlTokenKind.In;
                if (SpanEqualsIgnoreCase(text, "IS")) return SqlTokenKind.Is;
                if (SpanEqualsIgnoreCase(text, "ON")) return SqlTokenKind.On;
                if (SpanEqualsIgnoreCase(text, "OR")) return SqlTokenKind.Or;
                break;
            case 3:
                if (SpanEqualsIgnoreCase(text, "ALL")) return SqlTokenKind.All;
                if (SpanEqualsIgnoreCase(text, "AND")) return SqlTokenKind.And;
                if (SpanEqualsIgnoreCase(text, "ASC")) return SqlTokenKind.Asc;
                if (SpanEqualsIgnoreCase(text, "END")) return SqlTokenKind.End;
                if (SpanEqualsIgnoreCase(text, "NOT")) return SqlTokenKind.Not;
                if (SpanEqualsIgnoreCase(text, "ROW")) return SqlTokenKind.Row;
                break;
            case 4:
                if (SpanEqualsIgnoreCase(text, "CASE")) return SqlTokenKind.Case;
                if (SpanEqualsIgnoreCase(text, "CAST")) return SqlTokenKind.Cast;
                if (SpanEqualsIgnoreCase(text, "DESC")) return SqlTokenKind.Desc;
                if (SpanEqualsIgnoreCase(text, "ELSE")) return SqlTokenKind.Else;
                if (SpanEqualsIgnoreCase(text, "FROM")) return SqlTokenKind.From;
                if (SpanEqualsIgnoreCase(text, "FULL")) return SqlTokenKind.Full;
                if (SpanEqualsIgnoreCase(text, "JOIN")) return SqlTokenKind.Join;
                if (SpanEqualsIgnoreCase(text, "LEFT")) return SqlTokenKind.Left;
                if (SpanEqualsIgnoreCase(text, "LIKE")) return SqlTokenKind.Like;
                if (SpanEqualsIgnoreCase(text, "NULL")) return SqlTokenKind.Null;
                if (SpanEqualsIgnoreCase(text, "ONLY")) return SqlTokenKind.Only;
                if (SpanEqualsIgnoreCase(text, "OVER")) return SqlTokenKind.Over;
                if (SpanEqualsIgnoreCase(text, "ROWS")) return SqlTokenKind.Rows;
                if (SpanEqualsIgnoreCase(text, "THEN")) return SqlTokenKind.Then;
                if (SpanEqualsIgnoreCase(text, "TRUE")) return SqlTokenKind.True;
                if (SpanEqualsIgnoreCase(text, "WHEN")) return SqlTokenKind.When;
                if (SpanEqualsIgnoreCase(text, "WITH")) return SqlTokenKind.With;
                if (SpanEqualsIgnoreCase(text, "NEXT")) return SqlTokenKind.Next;
                break;
            case 5:
                if (SpanEqualsIgnoreCase(text, "CROSS")) return SqlTokenKind.Cross;
                if (SpanEqualsIgnoreCase(text, "FALSE")) return SqlTokenKind.False;
                if (SpanEqualsIgnoreCase(text, "FETCH")) return SqlTokenKind.Fetch;
                if (SpanEqualsIgnoreCase(text, "FIRST")) return SqlTokenKind.First;
                if (SpanEqualsIgnoreCase(text, "GROUP")) return SqlTokenKind.Group;
                if (SpanEqualsIgnoreCase(text, "INNER")) return SqlTokenKind.Inner;
                if (SpanEqualsIgnoreCase(text, "LIMIT")) return SqlTokenKind.Limit;
                if (SpanEqualsIgnoreCase(text, "ORDER")) return SqlTokenKind.Order;
                if (SpanEqualsIgnoreCase(text, "OUTER")) return SqlTokenKind.Outer;
                if (SpanEqualsIgnoreCase(text, "RIGHT")) return SqlTokenKind.Right;
                if (SpanEqualsIgnoreCase(text, "UNION")) return SqlTokenKind.Union;
                if (SpanEqualsIgnoreCase(text, "WHERE")) return SqlTokenKind.Where;
                break;
            case 6:
                if (SpanEqualsIgnoreCase(text, "EXCEPT")) return SqlTokenKind.Except;
                if (SpanEqualsIgnoreCase(text, "EXISTS")) return SqlTokenKind.Exists;
                if (SpanEqualsIgnoreCase(text, "HAVING")) return SqlTokenKind.Having;
                if (SpanEqualsIgnoreCase(text, "OFFSET")) return SqlTokenKind.Offset;
                if (SpanEqualsIgnoreCase(text, "SELECT")) return SqlTokenKind.Select;
                break;
            case 7:
                if (SpanEqualsIgnoreCase(text, "BETWEEN")) return SqlTokenKind.Between;
                break;
            case 8:
                if (SpanEqualsIgnoreCase(text, "DISTINCT")) return SqlTokenKind.Distinct;
                break;
            case 9:
                if (SpanEqualsIgnoreCase(text, "INTERSECT")) return SqlTokenKind.Intersect;
                break;
        }

        return SqlTokenKind.Identifier;
    }

    // ─── Operator reader ─────────────────────────────────

    private static SqlToken ReadOperator(ReadOnlySpan<char> sql, ref int pos)
    {
        var start = pos;
        var ch = sql[pos];

        switch (ch)
        {
            case ',': return MakeSingle(SqlTokenKind.Comma, ref pos);
            case '.': return MakeSingle(SqlTokenKind.Dot, ref pos);
            case '(': return MakeSingle(SqlTokenKind.OpenParen, ref pos);
            case ')': return MakeSingle(SqlTokenKind.CloseParen, ref pos);
            case ';': return MakeSingle(SqlTokenKind.Semicolon, ref pos);
            case '+': return MakeSingle(SqlTokenKind.Plus, ref pos);
            case '-': return MakeSingle(SqlTokenKind.Minus, ref pos);
            case '*': return MakeSingle(SqlTokenKind.Star, ref pos);
            case '/': return MakeSingle(SqlTokenKind.Slash, ref pos);
            case '%': return MakeSingle(SqlTokenKind.Percent, ref pos);
            case '=': return MakeSingle(SqlTokenKind.Equal, ref pos);
            case '<':
                if (pos + 1 < sql.Length)
                {
                    if (sql[pos + 1] == '=') { pos += 2; return new SqlToken(SqlTokenKind.LessThanOrEqual, start, 2); }
                    if (sql[pos + 1] == '>') { pos += 2; return new SqlToken(SqlTokenKind.NotEqual, start, 2); }
                }
                return MakeSingle(SqlTokenKind.LessThan, ref pos);
            case '>':
                if (pos + 1 < sql.Length && sql[pos + 1] == '=')
                {
                    pos += 2;
                    return new SqlToken(SqlTokenKind.GreaterThanOrEqual, start, 2);
                }
                return MakeSingle(SqlTokenKind.GreaterThan, ref pos);
            case '!':
                if (pos + 1 < sql.Length && sql[pos + 1] == '=')
                {
                    pos += 2;
                    return new SqlToken(SqlTokenKind.NotEqual, start, 2);
                }
                pos++;
                return new SqlToken(SqlTokenKind.Unknown, start, 1);
            default:
                pos++;
                return new SqlToken(SqlTokenKind.Unknown, start, 1);
        }
    }

    // ─── Helpers ─────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SqlToken MakeSingle(SqlTokenKind kind, ref int pos)
    {
        var token = new SqlToken(kind, pos, 1);
        pos++;
        return token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentStart(char c) =>
        char.IsLetter(c) || c == '_';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentPart(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Case-insensitive span comparison against an uppercase ASCII literal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpanEqualsIgnoreCase(ReadOnlySpan<char> span, ReadOnlySpan<char> upper)
    {
        if (span.Length != upper.Length) return false;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            // Fast ASCII uppercase
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            if (c != upper[i]) return false;
        }
        return true;
    }
}
