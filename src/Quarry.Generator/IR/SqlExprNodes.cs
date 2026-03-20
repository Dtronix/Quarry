using System;
using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.IR;

/// <summary>
/// Unresolved column reference. Created during discovery from syntax.
/// Resolved to ResolvedColumnExpr during binding.
/// </summary>
internal sealed class ColumnRefExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.ColumnRef;

    /// <summary>Lambda parameter name (e.g., "u").</summary>
    public string ParameterName { get; }

    /// <summary>Entity property name (e.g., "UserName").</summary>
    public string PropertyName { get; }

    /// <summary>For Ref&lt;T&gt;.Id access: "Id", otherwise null.</summary>
    public string? NestedProperty { get; }

    public ColumnRefExpr(string parameterName, string propertyName, string? nestedProperty = null)
        : base(HashCode.Combine(SqlExprKind.ColumnRef, parameterName, propertyName, nestedProperty))
    {
        ParameterName = parameterName;
        PropertyName = propertyName;
        NestedProperty = nestedProperty;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (ColumnRefExpr)other;
        return ParameterName == o.ParameterName
            && PropertyName == o.PropertyName
            && NestedProperty == o.NestedProperty;
    }
}

/// <summary>
/// Resolved column with quoted identifiers and optional table qualifier.
/// </summary>
internal sealed class ResolvedColumnExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.ResolvedColumn;

    /// <summary>Quoted column name (e.g., "\"user_name\"").</summary>
    public string QuotedColumnName { get; }

    /// <summary>Quoted table qualifier (e.g., "\"t0\""), or null.</summary>
    public string? TableQualifier { get; }

    public ResolvedColumnExpr(string quotedColumnName, string? tableQualifier = null)
        : base(HashCode.Combine(SqlExprKind.ResolvedColumn, quotedColumnName, tableQualifier))
    {
        QuotedColumnName = quotedColumnName;
        TableQualifier = tableQualifier;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (ResolvedColumnExpr)other;
        return QuotedColumnName == o.QuotedColumnName
            && TableQualifier == o.TableQualifier;
    }
}

/// <summary>
/// Parameter placeholder. Carries clause-local index and type metadata.
/// </summary>
internal sealed class ParamSlotExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.ParamSlot;

    /// <summary>0-based index within this clause.</summary>
    public int LocalIndex { get; }

    /// <summary>CLR type name (e.g., "string", "int").</summary>
    public string ClrType { get; }

    /// <summary>C# expression to extract value at runtime.</summary>
    public string ValueExpression { get; }

    /// <summary>Whether this is a captured variable from a closure.</summary>
    public bool IsCaptured { get; }

    /// <summary>Expression tree path for direct extraction.</summary>
    public string? ExpressionPath { get; }

    /// <summary>Whether this is an IN-clause collection parameter.</summary>
    public bool IsCollection { get; }

    /// <summary>Collection element type name.</summary>
    public string? ElementTypeName { get; }

    /// <summary>Custom type mapping class for ToDb() wrapping.</summary>
    public string? CustomTypeMappingClass { get; }

    /// <summary>Whether the CLR type is an enum.</summary>
    public bool IsEnum { get; }

    /// <summary>Underlying integral type for enums (e.g., "int").</summary>
    public string? EnumUnderlyingType { get; }

    /// <summary>Collection receiver symbol for direct-access eligibility.</summary>
    public Microsoft.CodeAnalysis.ISymbol? CollectionReceiverSymbol { get; }

    public ParamSlotExpr(
        int localIndex,
        string clrType,
        string valueExpression,
        bool isCaptured = false,
        string? expressionPath = null,
        bool isCollection = false,
        string? elementTypeName = null,
        string? customTypeMappingClass = null,
        bool isEnum = false,
        string? enumUnderlyingType = null,
        Microsoft.CodeAnalysis.ISymbol? collectionReceiverSymbol = null)
        : base(HashCode.Combine(SqlExprKind.ParamSlot, localIndex, clrType, valueExpression, isCaptured, isCollection))
    {
        LocalIndex = localIndex;
        ClrType = clrType;
        ValueExpression = valueExpression;
        IsCaptured = isCaptured;
        ExpressionPath = expressionPath;
        IsCollection = isCollection;
        ElementTypeName = elementTypeName;
        CustomTypeMappingClass = customTypeMappingClass;
        IsEnum = isEnum;
        EnumUnderlyingType = enumUnderlyingType;
        CollectionReceiverSymbol = collectionReceiverSymbol;
    }

    /// <summary>
    /// Creates a copy with updated custom type mapping class.
    /// </summary>
    public ParamSlotExpr WithCustomTypeMappingClass(string? mappingClass)
    {
        if (CustomTypeMappingClass == mappingClass) return this;
        return new ParamSlotExpr(
            LocalIndex, ClrType, ValueExpression, IsCaptured, ExpressionPath,
            IsCollection, ElementTypeName, mappingClass, IsEnum, EnumUnderlyingType,
            CollectionReceiverSymbol);
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (ParamSlotExpr)other;
        return LocalIndex == o.LocalIndex
            && ClrType == o.ClrType
            && ValueExpression == o.ValueExpression
            && IsCaptured == o.IsCaptured
            && ExpressionPath == o.ExpressionPath
            && IsCollection == o.IsCollection
            && ElementTypeName == o.ElementTypeName
            && CustomTypeMappingClass == o.CustomTypeMappingClass
            && IsEnum == o.IsEnum
            && EnumUnderlyingType == o.EnumUnderlyingType;
    }
}

/// <summary>
/// SQL literal value.
/// </summary>
internal sealed class LiteralExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.Literal;

    /// <summary>SQL text (e.g., "42", "'hello'", "NULL", "TRUE").</summary>
    public string SqlText { get; }

    /// <summary>CLR type (e.g., "int", "string").</summary>
    public string ClrType { get; }

    /// <summary>Whether this is a NULL literal.</summary>
    public bool IsNull { get; }

    public LiteralExpr(string sqlText, string clrType, bool isNull = false)
        : base(HashCode.Combine(SqlExprKind.Literal, sqlText, clrType, isNull))
    {
        SqlText = sqlText;
        ClrType = clrType;
        IsNull = isNull;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (LiteralExpr)other;
        return SqlText == o.SqlText
            && ClrType == o.ClrType
            && IsNull == o.IsNull;
    }
}

/// <summary>
/// Binary operation.
/// </summary>
internal sealed class BinaryOpExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.BinaryOp;

    public SqlExpr Left { get; }
    public SqlBinaryOperator Operator { get; }
    public SqlExpr Right { get; }

    public BinaryOpExpr(SqlExpr left, SqlBinaryOperator op, SqlExpr right)
        : base(HashCode.Combine(SqlExprKind.BinaryOp, left.GetHashCode(), (int)op, right.GetHashCode()))
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (BinaryOpExpr)other;
        return Operator == o.Operator
            && Left.Equals(o.Left)
            && Right.Equals(o.Right);
    }
}

/// <summary>
/// Unary operation.
/// </summary>
internal sealed class UnaryOpExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.UnaryOp;

    public SqlUnaryOperator Operator { get; }
    public SqlExpr Operand { get; }

    public UnaryOpExpr(SqlUnaryOperator op, SqlExpr operand)
        : base(HashCode.Combine(SqlExprKind.UnaryOp, (int)op, operand.GetHashCode()))
    {
        Operator = op;
        Operand = operand;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (UnaryOpExpr)other;
        return Operator == o.Operator
            && Operand.Equals(o.Operand);
    }
}

/// <summary>
/// SQL function call (LOWER, UPPER, COALESCE, COUNT, SUM, etc.).
/// </summary>
internal sealed class FunctionCallExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.FunctionCall;

    public string FunctionName { get; }
    public IReadOnlyList<SqlExpr> Arguments { get; }
    public bool IsAggregate { get; }

    public FunctionCallExpr(string functionName, IReadOnlyList<SqlExpr> arguments, bool isAggregate = false)
        : base(ComputeHash(functionName, arguments, isAggregate))
    {
        FunctionName = functionName;
        Arguments = arguments;
        IsAggregate = isAggregate;
    }

    private static int ComputeHash(string functionName, IReadOnlyList<SqlExpr> arguments, bool isAggregate)
    {
        var hc = new HashCode();
        hc.Add(SqlExprKind.FunctionCall);
        hc.Add(functionName);
        hc.Add(arguments.Count);
        hc.Add(isAggregate);
        return hc.ToHashCode();
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (FunctionCallExpr)other;
        if (FunctionName != o.FunctionName) return false;
        if (IsAggregate != o.IsAggregate) return false;
        if (Arguments.Count != o.Arguments.Count) return false;
        for (int i = 0; i < Arguments.Count; i++)
            if (!Arguments[i].Equals(o.Arguments[i])) return false;
        return true;
    }
}

/// <summary>
/// IN expression with a list of values or a collection parameter.
/// </summary>
internal sealed class InExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.InExpr;

    public SqlExpr Operand { get; }
    public IReadOnlyList<SqlExpr> Values { get; }
    public bool IsNegated { get; }

    public InExpr(SqlExpr operand, IReadOnlyList<SqlExpr> values, bool isNegated = false)
        : base(HashCode.Combine(SqlExprKind.InExpr, operand.GetHashCode(), values.Count, isNegated))
    {
        Operand = operand;
        Values = values;
        IsNegated = isNegated;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (InExpr)other;
        if (IsNegated != o.IsNegated) return false;
        if (!Operand.Equals(o.Operand)) return false;
        if (Values.Count != o.Values.Count) return false;
        for (int i = 0; i < Values.Count; i++)
            if (!Values[i].Equals(o.Values[i])) return false;
        return true;
    }
}

/// <summary>
/// IS NULL / IS NOT NULL check.
/// </summary>
internal sealed class IsNullCheckExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.IsNullCheck;

    public SqlExpr Operand { get; }
    public bool IsNegated { get; }

    public IsNullCheckExpr(SqlExpr operand, bool isNegated = false)
        : base(HashCode.Combine(SqlExprKind.IsNullCheck, operand.GetHashCode(), isNegated))
    {
        Operand = operand;
        IsNegated = isNegated;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (IsNullCheckExpr)other;
        return IsNegated == o.IsNegated
            && Operand.Equals(o.Operand);
    }
}

/// <summary>
/// LIKE expression with optional escape character.
/// </summary>
internal sealed class LikeExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.LikeExpr;

    public SqlExpr Operand { get; }
    public SqlExpr Pattern { get; }
    public bool IsNegated { get; }
    /// <summary>Prefix for LIKE pattern (e.g., "%" for Contains).</summary>
    public string? LikePrefix { get; }
    /// <summary>Suffix for LIKE pattern (e.g., "%" for Contains).</summary>
    public string? LikeSuffix { get; }
    /// <summary>Whether the pattern needs ESCAPE '\'.</summary>
    public bool NeedsEscape { get; }

    public LikeExpr(SqlExpr operand, SqlExpr pattern, bool isNegated = false,
        string? likePrefix = null, string? likeSuffix = null, bool needsEscape = false)
        : base(HashCode.Combine(SqlExprKind.LikeExpr, operand.GetHashCode(), pattern.GetHashCode(), isNegated))
    {
        Operand = operand;
        Pattern = pattern;
        IsNegated = isNegated;
        LikePrefix = likePrefix;
        LikeSuffix = likeSuffix;
        NeedsEscape = needsEscape;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (LikeExpr)other;
        return IsNegated == o.IsNegated
            && LikePrefix == o.LikePrefix
            && LikeSuffix == o.LikeSuffix
            && NeedsEscape == o.NeedsEscape
            && Operand.Equals(o.Operand)
            && Pattern.Equals(o.Pattern);
    }
}

/// <summary>
/// Captured runtime value that needs expression tree extraction.
/// This is the pre-parameter form; becomes a ParamSlotExpr during translation.
/// </summary>
internal sealed class CapturedValueExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.CapturedValue;

    public string VariableName { get; }
    public string SyntaxText { get; }
    public string? ExpressionPath { get; }
    public string ClrType { get; }
    public bool CanGenerateDirectPath => ExpressionPath != null;

    public CapturedValueExpr(string variableName, string syntaxText, string clrType = "object", string? expressionPath = null)
        : base(HashCode.Combine(SqlExprKind.CapturedValue, variableName, syntaxText, expressionPath))
    {
        VariableName = variableName;
        SyntaxText = syntaxText;
        ClrType = clrType;
        ExpressionPath = expressionPath;
    }

    /// <summary>Creates a copy with an updated CLR type.</summary>
    public CapturedValueExpr WithClrType(string clrType)
    {
        if (ClrType == clrType) return this;
        return new CapturedValueExpr(VariableName, SyntaxText, clrType, ExpressionPath);
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (CapturedValueExpr)other;
        return VariableName == o.VariableName
            && SyntaxText == o.SyntaxText
            && ExpressionPath == o.ExpressionPath
            && ClrType == o.ClrType;
    }
}

/// <summary>
/// Pre-rendered SQL text (escape hatch for cases not modeled by the IR).
/// </summary>
internal sealed class SqlRawExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.SqlRaw;

    public string SqlText { get; }

    public SqlRawExpr(string sqlText)
        : base(HashCode.Combine(SqlExprKind.SqlRaw, sqlText))
    {
        SqlText = sqlText;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        return SqlText == ((SqlRawExpr)other).SqlText;
    }
}

/// <summary>
/// Comma-separated expression list (used in function arguments, etc.).
/// </summary>
internal sealed class ExprListExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.ExprList;

    public IReadOnlyList<SqlExpr> Expressions { get; }

    public ExprListExpr(IReadOnlyList<SqlExpr> expressions)
        : base(ComputeHash(expressions))
    {
        Expressions = expressions;
    }

    private static int ComputeHash(IReadOnlyList<SqlExpr> expressions)
    {
        var hc = new HashCode();
        hc.Add(SqlExprKind.ExprList);
        hc.Add(expressions.Count);
        for (int i = 0; i < Math.Min(expressions.Count, 4); i++)
            hc.Add(expressions[i].GetHashCode());
        return hc.ToHashCode();
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (ExprListExpr)other;
        if (Expressions.Count != o.Expressions.Count) return false;
        for (int i = 0; i < Expressions.Count; i++)
            if (!Expressions[i].Equals(o.Expressions[i])) return false;
        return true;
    }
}

/// <summary>
/// Boolean column access in WHERE context — maps to "column = TRUE/1".
/// </summary>
internal sealed class BooleanColumnExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.ColumnRef;

    /// <summary>The underlying column reference.</summary>
    public ColumnRefExpr Column { get; }

    /// <summary>Whether this is in a boolean context (WHERE clause).</summary>
    public bool InBooleanContext { get; }

    public BooleanColumnExpr(ColumnRefExpr column, bool inBooleanContext)
        : base(HashCode.Combine(SqlExprKind.ColumnRef, column.GetHashCode(), inBooleanContext, "bool"))
    {
        Column = column;
        InBooleanContext = inBooleanContext;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        if (other is BooleanColumnExpr o)
            return InBooleanContext == o.InBooleanContext && Column.Equals(o.Column);
        return false;
    }
}
