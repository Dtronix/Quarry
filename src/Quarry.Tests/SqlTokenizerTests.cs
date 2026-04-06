using Quarry.Generators.Sql.Parser;
using GenDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class SqlTokenizerTests
{
    /// <summary>
    /// Converts the public <see cref="SqlDialect"/> to the internal generator dialect.
    /// Both enums have identical values; this avoids ambiguity in test code.
    /// </summary>
    private static GenDialect D(SqlDialect d) => (GenDialect)(int)d;
    [Test]
    public void Tokenize_SimpleSelect_ProducesCorrectTokens()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT col FROM table1", D(SqlDialect.SQLite));

        Assert.That(tokens, Has.Count.EqualTo(5)); // SELECT, col, FROM, table1, EOF
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Select));
        Assert.That(tokens[1].Kind, Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(tokens[1].GetTextString("SELECT col FROM table1"), Is.EqualTo("col"));
        Assert.That(tokens[2].Kind, Is.EqualTo(SqlTokenKind.From));
        Assert.That(tokens[3].Kind, Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(tokens[3].GetTextString("SELECT col FROM table1"), Is.EqualTo("table1"));
        Assert.That(tokens[4].Kind, Is.EqualTo(SqlTokenKind.Eof));
    }

    [Test]
    public void Tokenize_AllOperators_Recognized()
    {
        var tokens = SqlTokenizer.Tokenize("= <> != < > <= >= + - * / %", D(SqlDialect.SQLite));

        var kinds = tokens.GetRange(0, tokens.Count - 1).ConvertAll(t => t.Kind);
        Assert.That(kinds, Is.EqualTo(new[]
        {
            SqlTokenKind.Equal,
            SqlTokenKind.NotEqual,
            SqlTokenKind.NotEqual,
            SqlTokenKind.LessThan,
            SqlTokenKind.GreaterThan,
            SqlTokenKind.LessThanOrEqual,
            SqlTokenKind.GreaterThanOrEqual,
            SqlTokenKind.Plus,
            SqlTokenKind.Minus,
            SqlTokenKind.Star,
            SqlTokenKind.Slash,
            SqlTokenKind.Percent,
        }));
    }

    [Test]
    public void Tokenize_Punctuation_Recognized()
    {
        var tokens = SqlTokenizer.Tokenize(", . ( ) ;", D(SqlDialect.SQLite));

        var kinds = tokens.GetRange(0, tokens.Count - 1).ConvertAll(t => t.Kind);
        Assert.That(kinds, Is.EqualTo(new[]
        {
            SqlTokenKind.Comma,
            SqlTokenKind.Dot,
            SqlTokenKind.OpenParen,
            SqlTokenKind.CloseParen,
            SqlTokenKind.Semicolon,
        }));
    }

    // ── Parameter syntax per dialect ─────────────────────

    [TestCase(SqlDialect.SQLite, "@userId")]
    [TestCase(SqlDialect.SqlServer, "@userId")]
    [TestCase(SqlDialect.PostgreSQL, "$1")]
    [TestCase(SqlDialect.MySQL, "?")]
    public void Tokenize_Parameters_PerDialect(SqlDialect dialect, string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql, D(dialect));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Parameter));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo(sql));
    }

    // ── Quoted identifiers per dialect ───────────────────

    [TestCase(SqlDialect.SQLite, "\"my col\"")]
    [TestCase(SqlDialect.PostgreSQL, "\"my col\"")]
    [TestCase(SqlDialect.MySQL, "`my col`")]
    [TestCase(SqlDialect.SqlServer, "[my col]")]
    public void Tokenize_QuotedIdentifiers_PerDialect(SqlDialect dialect, string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql, D(dialect));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.QuotedIdentifier));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo(sql));
    }

    // ── String literals ──────────────────────────────────

    [Test]
    public void Tokenize_StringLiteral_Simple()
    {
        var sql = "'hello world'";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.String));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo("'hello world'"));
    }

    [Test]
    public void Tokenize_StringLiteral_EscapedQuote()
    {
        var sql = "'it''s'";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.String));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo("'it''s'"));
    }

    // ── Numeric literals ─────────────────────────────────

    [TestCase("42", "42")]
    [TestCase("3.14", "3.14")]
    [TestCase("0", "0")]
    public void Tokenize_NumericLiterals(string sql, string expected)
    {
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Number));
        Assert.That(tokens[0].GetTextString(sql), Is.EqualTo(expected));
    }

    // ── Comments ─────────────────────────────────────────

    [Test]
    public void Tokenize_SingleLineComment_Skipped()
    {
        var sql = "SELECT -- this is a comment\ncol";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Select));
        Assert.That(tokens[1].Kind, Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(tokens[1].GetTextString(sql), Is.EqualTo("col"));
    }

    [Test]
    public void Tokenize_BlockComment_Skipped()
    {
        var sql = "SELECT /* block */ col";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Select));
        Assert.That(tokens[1].Kind, Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(tokens[1].GetTextString(sql), Is.EqualTo("col"));
    }

    // ── Keywords (case-insensitive) ──────────────────────

    [Test]
    public void Tokenize_AllKeywords_Recognized()
    {
        var keywordMap = new (string Text, SqlTokenKind Kind)[]
        {
            ("SELECT", SqlTokenKind.Select),
            ("select", SqlTokenKind.Select),
            ("SeLeCt", SqlTokenKind.Select),
            ("FROM", SqlTokenKind.From),
            ("where", SqlTokenKind.Where),
            ("JOIN", SqlTokenKind.Join),
            ("INNER", SqlTokenKind.Inner),
            ("LEFT", SqlTokenKind.Left),
            ("RIGHT", SqlTokenKind.Right),
            ("CROSS", SqlTokenKind.Cross),
            ("FULL", SqlTokenKind.Full),
            ("OUTER", SqlTokenKind.Outer),
            ("AND", SqlTokenKind.And),
            ("OR", SqlTokenKind.Or),
            ("NOT", SqlTokenKind.Not),
            ("IN", SqlTokenKind.In),
            ("BETWEEN", SqlTokenKind.Between),
            ("LIKE", SqlTokenKind.Like),
            ("IS", SqlTokenKind.Is),
            ("NULL", SqlTokenKind.Null),
            ("GROUP", SqlTokenKind.Group),
            ("HAVING", SqlTokenKind.Having),
            ("ORDER", SqlTokenKind.Order),
            ("LIMIT", SqlTokenKind.Limit),
            ("OFFSET", SqlTokenKind.Offset),
            ("DISTINCT", SqlTokenKind.Distinct),
            ("CASE", SqlTokenKind.Case),
            ("WHEN", SqlTokenKind.When),
            ("THEN", SqlTokenKind.Then),
            ("ELSE", SqlTokenKind.Else),
            ("END", SqlTokenKind.End),
            ("CAST", SqlTokenKind.Cast),
            ("EXISTS", SqlTokenKind.Exists),
            ("TRUE", SqlTokenKind.True),
            ("FALSE", SqlTokenKind.False),
            ("WITH", SqlTokenKind.With),
            ("UNION", SqlTokenKind.Union),
            ("OVER", SqlTokenKind.Over),
            ("ASC", SqlTokenKind.Asc),
            ("DESC", SqlTokenKind.Desc),
            ("FETCH", SqlTokenKind.Fetch),
            ("NEXT", SqlTokenKind.Next),
            ("ROWS", SqlTokenKind.Rows),
            ("ONLY", SqlTokenKind.Only),
            ("ALL", SqlTokenKind.All),
            ("AS", SqlTokenKind.As),
            ("BY", SqlTokenKind.By),
            ("ON", SqlTokenKind.On),
            ("ROW", SqlTokenKind.Row),
            ("EXCEPT", SqlTokenKind.Except),
            ("INTERSECT", SqlTokenKind.Intersect),
            ("DELETE", SqlTokenKind.Delete),
            ("delete", SqlTokenKind.Delete),
            ("UPDATE", SqlTokenKind.Update),
            ("update", SqlTokenKind.Update),
            ("INSERT", SqlTokenKind.Insert),
            ("insert", SqlTokenKind.Insert),
            ("SET", SqlTokenKind.Set),
            ("set", SqlTokenKind.Set),
            ("VALUES", SqlTokenKind.Values),
            ("values", SqlTokenKind.Values),
            ("INTO", SqlTokenKind.Into),
            ("into", SqlTokenKind.Into),
        };

        foreach (var (text, kind) in keywordMap)
        {
            var tokens = SqlTokenizer.Tokenize(text, D(SqlDialect.SQLite));
            Assert.That(tokens[0].Kind, Is.EqualTo(kind), $"Keyword '{text}' should produce {kind}");
        }
    }

    // ── Edge cases ───────────────────────────────────────

    [Test]
    public void Tokenize_EmptyInput_ReturnsOnlyEof()
    {
        var tokens = SqlTokenizer.Tokenize("", D(SqlDialect.SQLite));
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Eof));
    }

    [Test]
    public void Tokenize_WhitespaceOnly_ReturnsOnlyEof()
    {
        var tokens = SqlTokenizer.Tokenize("   \t\n  ", D(SqlDialect.SQLite));
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Eof));
    }

    [Test]
    public void Tokenize_UnknownCharacter_ProducesUnknownToken()
    {
        var tokens = SqlTokenizer.Tokenize("~", D(SqlDialect.SQLite));
        Assert.That(tokens[0].Kind, Is.EqualTo(SqlTokenKind.Unknown));
    }

    [Test]
    public void Tokenize_FullSelectStatement_CorrectSequence()
    {
        var sql = "SELECT u.name, u.age FROM users u WHERE u.active = 1 ORDER BY u.name ASC";
        var tokens = SqlTokenizer.Tokenize(sql, D(SqlDialect.SQLite));

        // Verify key tokens
        var kinds = tokens.ConvertAll(t => t.Kind);
        Assert.That(kinds[0], Is.EqualTo(SqlTokenKind.Select));
        // u.name
        Assert.That(kinds[1], Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(kinds[2], Is.EqualTo(SqlTokenKind.Dot));
        Assert.That(kinds[3], Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(kinds[4], Is.EqualTo(SqlTokenKind.Comma));
        // u.age
        Assert.That(kinds[5], Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(kinds[6], Is.EqualTo(SqlTokenKind.Dot));
        Assert.That(kinds[7], Is.EqualTo(SqlTokenKind.Identifier));
        // FROM users u
        Assert.That(kinds[8], Is.EqualTo(SqlTokenKind.From));
        Assert.That(kinds[9], Is.EqualTo(SqlTokenKind.Identifier));
        Assert.That(kinds[10], Is.EqualTo(SqlTokenKind.Identifier));
        // WHERE u.active = 1
        Assert.That(kinds[11], Is.EqualTo(SqlTokenKind.Where));
        Assert.That(kinds[15], Is.EqualTo(SqlTokenKind.Equal));
        Assert.That(kinds[16], Is.EqualTo(SqlTokenKind.Number));
        // ORDER BY u.name ASC
        Assert.That(kinds[17], Is.EqualTo(SqlTokenKind.Order));
        Assert.That(kinds[18], Is.EqualTo(SqlTokenKind.By));
        Assert.That(kinds[22], Is.EqualTo(SqlTokenKind.Asc));
        // EOF
        Assert.That(kinds[kinds.Count - 1], Is.EqualTo(SqlTokenKind.Eof));
    }
}
