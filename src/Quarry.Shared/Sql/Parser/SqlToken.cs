using System;

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql.Parser;
#else
namespace Quarry.Shared.Sql.Parser;
#endif

/// <summary>
/// Classifies a token produced by <see cref="SqlTokenizer"/>.
/// </summary>
internal enum SqlTokenKind
{
    // ── Sentinel ──────────────────────────────────────────
    /// <summary>End of input.</summary>
    Eof,
    /// <summary>Unrecognized character.</summary>
    Unknown,

    // ── Literals / identifiers ───────────────────────────
    /// <summary>A numeric literal (integer or decimal).</summary>
    Number,
    /// <summary>A string literal enclosed in single quotes.</summary>
    String,
    /// <summary>An unquoted identifier or keyword candidate.</summary>
    Identifier,
    /// <summary>A quoted identifier (double-quoted, backtick, or bracket).</summary>
    QuotedIdentifier,
    /// <summary>A parameter placeholder (@name, $n, or ?).</summary>
    Parameter,

    // ── Punctuation ──────────────────────────────────────
    /// <summary>,</summary>
    Comma,
    /// <summary>.</summary>
    Dot,
    /// <summary>(</summary>
    OpenParen,
    /// <summary>)</summary>
    CloseParen,
    /// <summary>;</summary>
    Semicolon,

    // ── Operators ────────────────────────────────────────
    /// <summary>=</summary>
    Equal,
    /// <summary>&lt;&gt; or !=</summary>
    NotEqual,
    /// <summary>&lt;</summary>
    LessThan,
    /// <summary>&gt;</summary>
    GreaterThan,
    /// <summary>&lt;=</summary>
    LessThanOrEqual,
    /// <summary>&gt;=</summary>
    GreaterThanOrEqual,
    /// <summary>+</summary>
    Plus,
    /// <summary>-</summary>
    Minus,
    /// <summary>*</summary>
    Star,
    /// <summary>/</summary>
    Slash,
    /// <summary>%</summary>
    Percent,

    // ── Keywords ─────────────────────────────────────────
    Select,
    Distinct,
    From,
    Where,
    And,
    Or,
    Not,
    In,
    Between,
    Like,
    Is,
    Null,
    As,
    On,
    Join,
    Inner,
    Left,
    Right,
    Cross,
    Full,
    Outer,
    Group,
    By,
    Having,
    Order,
    Asc,
    Desc,
    Limit,
    Offset,
    Fetch,
    Next,
    Rows,
    Only,
    Row,
    Case,
    When,
    Then,
    Else,
    End,
    Cast,
    Exists,
    True,
    False,
    With,
    Union,
    Intersect,
    Except,
    All,
    Over,
    First,
}

/// <summary>
/// A single token produced by <see cref="SqlTokenizer"/>.
/// Stores only offsets into the original SQL string to avoid allocations.
/// </summary>
internal readonly struct SqlToken : IEquatable<SqlToken>
{
    /// <summary>Token classification.</summary>
    public SqlTokenKind Kind { get; }

    /// <summary>Start offset in the original SQL string (inclusive).</summary>
    public int Start { get; }

    /// <summary>Length of the token text in the original SQL string.</summary>
    public int Length { get; }

    public SqlToken(SqlTokenKind kind, int start, int length)
    {
        Kind = kind;
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Extracts the raw text of this token from the source SQL.
    /// </summary>
    public ReadOnlySpan<char> GetText(ReadOnlySpan<char> sql) => sql.Slice(Start, Length);

    /// <summary>
    /// Extracts the raw text of this token as a string.
    /// </summary>
    public string GetTextString(string sql) => sql.Substring(Start, Length);

    public bool Equals(SqlToken other) =>
        Kind == other.Kind && Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj) => obj is SqlToken other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (int)Kind;
            hash = hash * 31 + Start;
            hash = hash * 31 + Length;
            return hash;
        }
    }

    public override string ToString() => $"{Kind}[{Start}..{Start + Length}]";
}
