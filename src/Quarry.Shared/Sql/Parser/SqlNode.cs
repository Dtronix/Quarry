using System;
using System.Collections.Generic;

#if QUARRY_GENERATOR
namespace Quarry.Generators.Sql.Parser;
#else
namespace Quarry.Shared.Sql.Parser;
#endif

// ─────────────────────────────────────────────────────────
//  Enums
// ─────────────────────────────────────────────────────────

/// <summary>Fast type tag for AST nodes, avoiding <c>is</c> casts in hot paths.</summary>
internal enum SqlNodeKind
{
    SelectStatement,
    Unsupported,
    SelectColumn,
    StarColumn,
    TableSource,
    Join,
    BinaryExpr,
    UnaryExpr,
    ColumnRef,
    Literal,
    Parameter,
    FunctionCall,
    InExpr,
    BetweenExpr,
    IsNullExpr,
    ParenExpr,
    CaseExpr,
    CastExpr,
    ExistsExpr,
    OrderTerm,
    WhenClause,
}

/// <summary>Binary operator kinds.</summary>
internal enum SqlBinaryOp
{
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    Like,
    And,
    Or,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
}

/// <summary>Unary operator kinds.</summary>
internal enum SqlUnaryOp
{
    Not,
    Negate,
}

/// <summary>Join type classification.</summary>
internal enum SqlJoinKind
{
    Inner,
    Left,
    Right,
    Cross,
    FullOuter,
}

/// <summary>Literal value classification.</summary>
internal enum SqlLiteralKind
{
    String,
    Number,
    Boolean,
    Null,
}

// ─────────────────────────────────────────────────────────
//  Base class
// ─────────────────────────────────────────────────────────

/// <summary>
/// Abstract base for all SQL AST nodes.
/// Uses reference equality — recursive tree structures make structural
/// equality complex and no current consumer requires it.
/// </summary>
internal abstract class SqlNode
{
    public abstract SqlNodeKind NodeKind { get; }

    /// <summary>Start offset in the original SQL string. -1 if unknown.</summary>
    public int SourceStart { get; internal set; } = -1;

    /// <summary>Length in the original SQL string. -1 if unknown.</summary>
    public int SourceLength { get; internal set; } = -1;
}

/// <summary>Abstract base for expression nodes.</summary>
internal abstract class SqlExpr : SqlNode { }

// ─────────────────────────────────────────────────────────
//  Statement nodes
// ─────────────────────────────────────────────────────────

/// <summary>A parsed SELECT statement.</summary>
internal sealed class SqlSelectStatement : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.SelectStatement;

    public bool IsDistinct { get; }
    public IReadOnlyList<SqlNode> Columns { get; } // SqlSelectColumn or SqlStarColumn
    public SqlTableSource? From { get; }
    public IReadOnlyList<SqlJoin> Joins { get; }
    public SqlExpr? Where { get; }
    public IReadOnlyList<SqlExpr>? GroupBy { get; }
    public SqlExpr? Having { get; }
    public IReadOnlyList<SqlOrderTerm>? OrderBy { get; }
    public SqlExpr? Limit { get; }
    public SqlExpr? Offset { get; }

    public SqlSelectStatement(
        bool isDistinct,
        IReadOnlyList<SqlNode> columns,
        SqlTableSource? from,
        IReadOnlyList<SqlJoin> joins,
        SqlExpr? where,
        IReadOnlyList<SqlExpr>? groupBy,
        SqlExpr? having,
        IReadOnlyList<SqlOrderTerm>? orderBy,
        SqlExpr? limit,
        SqlExpr? offset)
    {
        IsDistinct = isDistinct;
        Columns = columns;
        From = from;
        Joins = joins;
        Where = where;
        GroupBy = groupBy;
        Having = having;
        OrderBy = orderBy;
        Limit = limit;
        Offset = offset;
    }
}

/// <summary>Captures unsupported SQL text (CTEs, UNION, window functions, etc.).</summary>
internal sealed class SqlUnsupported : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Unsupported;
    public string RawText { get; }

    public SqlUnsupported(string rawText)
    {
        RawText = rawText;
    }
}

// ─────────────────────────────────────────────────────────
//  Select column nodes
// ─────────────────────────────────────────────────────────

/// <summary>A named column in a SELECT list: expression [AS alias].</summary>
internal sealed class SqlSelectColumn : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.SelectColumn;

    public SqlExpr Expression { get; }
    public string? Alias { get; }

    public SqlSelectColumn(SqlExpr expression, string? alias)
    {
        Expression = expression;
        Alias = alias;
    }
}

/// <summary>A star column: <c>*</c> or <c>table.*</c>.</summary>
internal sealed class SqlStarColumn : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.StarColumn;

    /// <summary>Table qualifier, if present (e.g., "u" in <c>u.*</c>).</summary>
    public string? TableAlias { get; }

    public SqlStarColumn(string? tableAlias)
    {
        TableAlias = tableAlias;
    }
}

// ─────────────────────────────────────────────────────────
//  Table / join nodes
// ─────────────────────────────────────────────────────────

/// <summary>A table reference: [schema.]table [alias].</summary>
internal sealed class SqlTableSource : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.TableSource;

    public string TableName { get; }
    public string? Schema { get; }
    public string? Alias { get; }

    public SqlTableSource(string tableName, string? schema, string? alias)
    {
        TableName = tableName;
        Schema = schema;
        Alias = alias;
    }
}

/// <summary>A JOIN clause: [INNER|LEFT|RIGHT|CROSS|FULL OUTER] JOIN table ON condition.</summary>
internal sealed class SqlJoin : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Join;

    public SqlJoinKind JoinKind { get; }
    public SqlTableSource Table { get; }
    public SqlExpr? Condition { get; }

    public SqlJoin(SqlJoinKind joinKind, SqlTableSource table, SqlExpr? condition)
    {
        JoinKind = joinKind;
        Table = table;
        Condition = condition;
    }
}

// ─────────────────────────────────────────────────────────
//  Expression nodes
// ─────────────────────────────────────────────────────────

/// <summary>Binary expression: left op right.</summary>
internal sealed class SqlBinaryExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.BinaryExpr;

    public SqlExpr Left { get; }
    public SqlBinaryOp Operator { get; }
    public SqlExpr Right { get; }

    public SqlBinaryExpr(SqlExpr left, SqlBinaryOp op, SqlExpr right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}

/// <summary>Unary expression: NOT expr or -expr.</summary>
internal sealed class SqlUnaryExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.UnaryExpr;

    public SqlUnaryOp Operator { get; }
    public SqlExpr Operand { get; }

    public SqlUnaryExpr(SqlUnaryOp op, SqlExpr operand)
    {
        Operator = op;
        Operand = operand;
    }
}

/// <summary>Column reference: [table.]column.</summary>
internal sealed class SqlColumnRef : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ColumnRef;

    public string? TableAlias { get; }
    public string ColumnName { get; }

    public SqlColumnRef(string? tableAlias, string columnName)
    {
        TableAlias = tableAlias;
        ColumnName = columnName;
    }
}

/// <summary>A literal value: string, number, boolean, or NULL.</summary>
internal sealed class SqlLiteral : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Literal;

    public string Value { get; }
    public SqlLiteralKind LiteralKind { get; }

    public SqlLiteral(string value, SqlLiteralKind literalKind)
    {
        Value = value;
        LiteralKind = literalKind;
    }
}

/// <summary>A parameter placeholder: @userId, $1, ?</summary>
internal sealed class SqlParameter : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Parameter;
    public string RawText { get; }

    public SqlParameter(string rawText)
    {
        RawText = rawText;
    }
}

/// <summary>Function call: name([DISTINCT] args...).</summary>
internal sealed class SqlFunctionCall : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.FunctionCall;

    public string FunctionName { get; }
    public IReadOnlyList<SqlExpr> Arguments { get; }
    public bool IsDistinct { get; }

    public SqlFunctionCall(string functionName, IReadOnlyList<SqlExpr> arguments, bool isDistinct)
    {
        FunctionName = functionName;
        Arguments = arguments;
        IsDistinct = isDistinct;
    }
}

/// <summary>IN expression: expr [NOT] IN (values...).</summary>
internal sealed class SqlInExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.InExpr;

    public SqlExpr Expression { get; }
    public IReadOnlyList<SqlExpr> Values { get; }
    public bool IsNegated { get; }

    public SqlInExpr(SqlExpr expression, IReadOnlyList<SqlExpr> values, bool isNegated)
    {
        Expression = expression;
        Values = values;
        IsNegated = isNegated;
    }
}

/// <summary>BETWEEN expression: expr [NOT] BETWEEN low AND high.</summary>
internal sealed class SqlBetweenExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.BetweenExpr;

    public SqlExpr Expression { get; }
    public SqlExpr Low { get; }
    public SqlExpr High { get; }
    public bool IsNegated { get; }

    public SqlBetweenExpr(SqlExpr expression, SqlExpr low, SqlExpr high, bool isNegated)
    {
        Expression = expression;
        Low = low;
        High = high;
        IsNegated = isNegated;
    }
}

/// <summary>IS NULL / IS NOT NULL expression.</summary>
internal sealed class SqlIsNullExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.IsNullExpr;

    public SqlExpr Expression { get; }
    public bool IsNegated { get; }

    public SqlIsNullExpr(SqlExpr expression, bool isNegated)
    {
        Expression = expression;
        IsNegated = isNegated;
    }
}

/// <summary>Parenthesized expression.</summary>
internal sealed class SqlParenExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ParenExpr;
    public SqlExpr Inner { get; }

    public SqlParenExpr(SqlExpr inner)
    {
        Inner = inner;
    }
}

/// <summary>CASE [operand] WHEN ... THEN ... [ELSE ...] END.</summary>
internal sealed class SqlCaseExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.CaseExpr;

    /// <summary>Simple CASE operand, or null for searched CASE.</summary>
    public SqlExpr? Operand { get; }
    public IReadOnlyList<SqlWhenClause> WhenClauses { get; }
    public SqlExpr? ElseResult { get; }

    public SqlCaseExpr(SqlExpr? operand, IReadOnlyList<SqlWhenClause> whenClauses, SqlExpr? elseResult)
    {
        Operand = operand;
        WhenClauses = whenClauses;
        ElseResult = elseResult;
    }
}

/// <summary>A single WHEN condition THEN result pair.</summary>
internal sealed class SqlWhenClause : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.WhenClause;

    public SqlExpr Condition { get; }
    public SqlExpr Result { get; }

    public SqlWhenClause(SqlExpr condition, SqlExpr result)
    {
        Condition = condition;
        Result = result;
    }
}

/// <summary>CAST(expression AS type).</summary>
internal sealed class SqlCastExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.CastExpr;

    public SqlExpr Expression { get; }
    public string TypeName { get; }

    public SqlCastExpr(SqlExpr expression, string typeName)
    {
        Expression = expression;
        TypeName = typeName;
    }
}

/// <summary>EXISTS (subquery).</summary>
internal sealed class SqlExistsExpr : SqlExpr
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ExistsExpr;
    public SqlSelectStatement Subquery { get; }

    public SqlExistsExpr(SqlSelectStatement subquery)
    {
        Subquery = subquery;
    }
}

// ─────────────────────────────────────────────────────────
//  ORDER BY
// ─────────────────────────────────────────────────────────

/// <summary>A single ORDER BY term: expression [ASC|DESC].</summary>
internal sealed class SqlOrderTerm : SqlNode
{
    public override SqlNodeKind NodeKind => SqlNodeKind.OrderTerm;

    public SqlExpr Expression { get; }
    public bool IsDescending { get; }

    public SqlOrderTerm(SqlExpr expression, bool isDescending)
    {
        Expression = expression;
        IsDescending = isDescending;
    }
}

// ─────────────────────────────────────────────────────────
//  Parse result
// ─────────────────────────────────────────────────────────

/// <summary>Parse diagnostic severity.</summary>
internal enum SqlDiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>A parse diagnostic (error or warning).</summary>
internal sealed class SqlParseDiagnostic
{
    public int Position { get; }
    public int Length { get; }
    public string Message { get; }
    public SqlDiagnosticSeverity Severity { get; }

    public SqlParseDiagnostic(int position, int length, string message, SqlDiagnosticSeverity severity = SqlDiagnosticSeverity.Error)
    {
        Position = position;
        Length = length;
        Message = message;
        Severity = severity;
    }

    public override string ToString() => $"[{Severity}] [{Position}..{Position + Length}] {Message}";
}

/// <summary>Result of parsing a SQL string.</summary>
internal sealed class SqlParseResult
{
    public SqlSelectStatement? Statement { get; }
    public IReadOnlyList<SqlParseDiagnostic> Diagnostics { get; }
    public bool HasUnsupported { get; }

    public SqlParseResult(SqlSelectStatement? statement, IReadOnlyList<SqlParseDiagnostic> diagnostics, bool hasUnsupported)
    {
        Statement = statement;
        Diagnostics = diagnostics;
        HasUnsupported = hasUnsupported;
    }

    public bool Success => Statement != null && Diagnostics.Count == 0;
}
