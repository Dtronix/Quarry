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

/// <summary>Abstract base for all SQL AST nodes.</summary>
internal abstract class SqlNode
{
    public abstract SqlNodeKind NodeKind { get; }
}

/// <summary>Abstract base for expression nodes.</summary>
internal abstract class SqlExpr : SqlNode { }

// ─────────────────────────────────────────────────────────
//  Statement nodes
// ─────────────────────────────────────────────────────────

/// <summary>A parsed SELECT statement.</summary>
internal sealed class SqlSelectStatement : SqlNode, IEquatable<SqlSelectStatement>
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

    public bool Equals(SqlSelectStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsDistinct == other.IsDistinct;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlSelectStatement);
    public override int GetHashCode() => IsDistinct.GetHashCode();
}

/// <summary>Captures unsupported SQL text (CTEs, UNION, window functions, etc.).</summary>
internal sealed class SqlUnsupported : SqlExpr, IEquatable<SqlUnsupported>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Unsupported;

    public string RawText { get; }

    public SqlUnsupported(string rawText)
    {
        RawText = rawText;
    }

    public bool Equals(SqlUnsupported? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return RawText == other.RawText;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlUnsupported);
    public override int GetHashCode() => RawText.GetHashCode();
}

// ─────────────────────────────────────────────────────────
//  Select column nodes
// ─────────────────────────────────────────────────────────

/// <summary>A named column in a SELECT list: expression [AS alias].</summary>
internal sealed class SqlSelectColumn : SqlNode, IEquatable<SqlSelectColumn>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.SelectColumn;

    public SqlExpr Expression { get; }
    public string? Alias { get; }

    public SqlSelectColumn(SqlExpr expression, string? alias)
    {
        Expression = expression;
        Alias = alias;
    }

    public bool Equals(SqlSelectColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Alias == other.Alias;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlSelectColumn);
    public override int GetHashCode() => Alias?.GetHashCode() ?? 0;
}

/// <summary>A star column: <c>*</c> or <c>table.*</c>.</summary>
internal sealed class SqlStarColumn : SqlNode, IEquatable<SqlStarColumn>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.StarColumn;

    /// <summary>Table qualifier, if present (e.g., "u" in <c>u.*</c>).</summary>
    public string? TableAlias { get; }

    public SqlStarColumn(string? tableAlias)
    {
        TableAlias = tableAlias;
    }

    public bool Equals(SqlStarColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TableAlias == other.TableAlias;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlStarColumn);
    public override int GetHashCode() => TableAlias?.GetHashCode() ?? 0;
}

// ─────────────────────────────────────────────────────────
//  Table / join nodes
// ─────────────────────────────────────────────────────────

/// <summary>A table reference: [schema.]table [alias].</summary>
internal sealed class SqlTableSource : SqlNode, IEquatable<SqlTableSource>
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

    public bool Equals(SqlTableSource? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TableName == other.TableName && Schema == other.Schema && Alias == other.Alias;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlTableSource);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TableName.GetHashCode();
            hash = hash * 31 + (Schema?.GetHashCode() ?? 0);
            hash = hash * 31 + (Alias?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>A JOIN clause: [INNER|LEFT|RIGHT|CROSS|FULL OUTER] JOIN table ON condition.</summary>
internal sealed class SqlJoin : SqlNode, IEquatable<SqlJoin>
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

    public bool Equals(SqlJoin? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return JoinKind == other.JoinKind && Table.Equals(other.Table);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlJoin);
    public override int GetHashCode()
    {
        unchecked { return (int)JoinKind * 31 + Table.GetHashCode(); }
    }
}

// ─────────────────────────────────────────────────────────
//  Expression nodes
// ─────────────────────────────────────────────────────────

/// <summary>Binary expression: left op right.</summary>
internal sealed class SqlBinaryExpr : SqlExpr, IEquatable<SqlBinaryExpr>
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

    public bool Equals(SqlBinaryExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Operator == other.Operator;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlBinaryExpr);
    public override int GetHashCode() => (int)Operator;
}

/// <summary>Unary expression: NOT expr or -expr.</summary>
internal sealed class SqlUnaryExpr : SqlExpr, IEquatable<SqlUnaryExpr>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.UnaryExpr;

    public SqlUnaryOp Operator { get; }
    public SqlExpr Operand { get; }

    public SqlUnaryExpr(SqlUnaryOp op, SqlExpr operand)
    {
        Operator = op;
        Operand = operand;
    }

    public bool Equals(SqlUnaryExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Operator == other.Operator;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlUnaryExpr);
    public override int GetHashCode() => (int)Operator;
}

/// <summary>Column reference: [table.]column.</summary>
internal sealed class SqlColumnRef : SqlExpr, IEquatable<SqlColumnRef>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ColumnRef;

    public string? TableAlias { get; }
    public string ColumnName { get; }

    public SqlColumnRef(string? tableAlias, string columnName)
    {
        TableAlias = tableAlias;
        ColumnName = columnName;
    }

    public bool Equals(SqlColumnRef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TableAlias == other.TableAlias && ColumnName == other.ColumnName;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlColumnRef);
    public override int GetHashCode()
    {
        unchecked { return ColumnName.GetHashCode() * 31 + (TableAlias?.GetHashCode() ?? 0); }
    }
}

/// <summary>A literal value: string, number, boolean, or NULL.</summary>
internal sealed class SqlLiteral : SqlExpr, IEquatable<SqlLiteral>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Literal;

    public string Value { get; }
    public SqlLiteralKind LiteralKind { get; }

    public SqlLiteral(string value, SqlLiteralKind literalKind)
    {
        Value = value;
        LiteralKind = literalKind;
    }

    public bool Equals(SqlLiteral? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value && LiteralKind == other.LiteralKind;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlLiteral);
    public override int GetHashCode()
    {
        unchecked { return Value.GetHashCode() * 31 + (int)LiteralKind; }
    }
}

/// <summary>A parameter placeholder: @userId, $1, ?</summary>
internal sealed class SqlParameter : SqlExpr, IEquatable<SqlParameter>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.Parameter;

    public string RawText { get; }

    public SqlParameter(string rawText)
    {
        RawText = rawText;
    }

    public bool Equals(SqlParameter? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return RawText == other.RawText;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlParameter);
    public override int GetHashCode() => RawText.GetHashCode();
}

/// <summary>Function call: name([DISTINCT] args...).</summary>
internal sealed class SqlFunctionCall : SqlExpr, IEquatable<SqlFunctionCall>
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

    public bool Equals(SqlFunctionCall? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FunctionName == other.FunctionName && IsDistinct == other.IsDistinct;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlFunctionCall);
    public override int GetHashCode()
    {
        unchecked { return FunctionName.GetHashCode() * 31 + IsDistinct.GetHashCode(); }
    }
}

/// <summary>IN expression: expr [NOT] IN (values...).</summary>
internal sealed class SqlInExpr : SqlExpr, IEquatable<SqlInExpr>
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

    public bool Equals(SqlInExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsNegated == other.IsNegated;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlInExpr);
    public override int GetHashCode() => IsNegated.GetHashCode();
}

/// <summary>BETWEEN expression: expr [NOT] BETWEEN low AND high.</summary>
internal sealed class SqlBetweenExpr : SqlExpr, IEquatable<SqlBetweenExpr>
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

    public bool Equals(SqlBetweenExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsNegated == other.IsNegated;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlBetweenExpr);
    public override int GetHashCode() => IsNegated.GetHashCode();
}

/// <summary>IS NULL / IS NOT NULL expression.</summary>
internal sealed class SqlIsNullExpr : SqlExpr, IEquatable<SqlIsNullExpr>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.IsNullExpr;

    public SqlExpr Expression { get; }
    public bool IsNegated { get; }

    public SqlIsNullExpr(SqlExpr expression, bool isNegated)
    {
        Expression = expression;
        IsNegated = isNegated;
    }

    public bool Equals(SqlIsNullExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsNegated == other.IsNegated;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlIsNullExpr);
    public override int GetHashCode() => IsNegated.GetHashCode();
}

/// <summary>Parenthesized expression.</summary>
internal sealed class SqlParenExpr : SqlExpr, IEquatable<SqlParenExpr>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ParenExpr;

    public SqlExpr Inner { get; }

    public SqlParenExpr(SqlExpr inner)
    {
        Inner = inner;
    }

    public bool Equals(SqlParenExpr? other)
    {
        if (other is null) return false;
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlParenExpr);
    public override int GetHashCode() => Inner.GetHashCode();
}

/// <summary>CASE [operand] WHEN ... THEN ... [ELSE ...] END.</summary>
internal sealed class SqlCaseExpr : SqlExpr, IEquatable<SqlCaseExpr>
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

    public bool Equals(SqlCaseExpr? other)
    {
        if (other is null) return false;
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlCaseExpr);
    public override int GetHashCode() => WhenClauses.Count;
}

/// <summary>A single WHEN condition THEN result pair.</summary>
internal sealed class SqlWhenClause : IEquatable<SqlWhenClause>
{
    public SqlExpr Condition { get; }
    public SqlExpr Result { get; }

    public SqlWhenClause(SqlExpr condition, SqlExpr result)
    {
        Condition = condition;
        Result = result;
    }

    public bool Equals(SqlWhenClause? other)
    {
        if (other is null) return false;
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlWhenClause);
    public override int GetHashCode() => Condition.GetHashCode();
}

/// <summary>CAST(expression AS type).</summary>
internal sealed class SqlCastExpr : SqlExpr, IEquatable<SqlCastExpr>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.CastExpr;

    public SqlExpr Expression { get; }
    public string TypeName { get; }

    public SqlCastExpr(SqlExpr expression, string typeName)
    {
        Expression = expression;
        TypeName = typeName;
    }

    public bool Equals(SqlCastExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TypeName == other.TypeName;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlCastExpr);
    public override int GetHashCode() => TypeName.GetHashCode();
}

/// <summary>EXISTS (subquery).</summary>
internal sealed class SqlExistsExpr : SqlExpr, IEquatable<SqlExistsExpr>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.ExistsExpr;

    public SqlSelectStatement Subquery { get; }

    public SqlExistsExpr(SqlSelectStatement subquery)
    {
        Subquery = subquery;
    }

    public bool Equals(SqlExistsExpr? other)
    {
        if (other is null) return false;
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj) => Equals(obj as SqlExistsExpr);
    public override int GetHashCode() => Subquery.GetHashCode();
}

// ─────────────────────────────────────────────────────────
//  ORDER BY
// ─────────────────────────────────────────────────────────

/// <summary>A single ORDER BY term: expression [ASC|DESC].</summary>
internal sealed class SqlOrderTerm : SqlNode, IEquatable<SqlOrderTerm>
{
    public override SqlNodeKind NodeKind => SqlNodeKind.OrderTerm;

    public SqlExpr Expression { get; }
    public bool IsDescending { get; }

    public SqlOrderTerm(SqlExpr expression, bool isDescending)
    {
        Expression = expression;
        IsDescending = isDescending;
    }

    public bool Equals(SqlOrderTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsDescending == other.IsDescending;
    }

    public override bool Equals(object? obj) => Equals(obj as SqlOrderTerm);
    public override int GetHashCode() => IsDescending.GetHashCode();
}

// ─────────────────────────────────────────────────────────
//  Parse result
// ─────────────────────────────────────────────────────────

/// <summary>A parse diagnostic (error or warning).</summary>
internal sealed class SqlParseDiagnostic
{
    public int Position { get; }
    public int Length { get; }
    public string Message { get; }

    public SqlParseDiagnostic(int position, int length, string message)
    {
        Position = position;
        Length = length;
        Message = message;
    }

    public override string ToString() => $"[{Position}..{Position + Length}] {Message}";
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
