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
    public bool IsStaticField { get; }
    public bool CanGenerateDirectPath => ExpressionPath != null;

    /// <summary>
    /// Semantic type symbol for carrier analysis (e.g., IReadOnlyList vs IEnumerable detection).
    /// Not included in equality/hash — enrichment field only.
    /// </summary>
    public Microsoft.CodeAnalysis.ITypeSymbol? TypeSymbol { get; }

    public CapturedValueExpr(string variableName, string syntaxText, string clrType = "object", string? expressionPath = null, bool isStaticField = false, Microsoft.CodeAnalysis.ITypeSymbol? typeSymbol = null)
        : base(HashCode.Combine(SqlExprKind.CapturedValue, variableName, syntaxText, expressionPath))
    {
        VariableName = variableName;
        SyntaxText = syntaxText;
        ClrType = clrType;
        ExpressionPath = expressionPath;
        IsStaticField = isStaticField;
        TypeSymbol = typeSymbol;
    }

    /// <summary>Creates a copy with an updated CLR type and optional type symbol.</summary>
    public CapturedValueExpr WithClrType(string clrType, Microsoft.CodeAnalysis.ITypeSymbol? typeSymbol = null)
    {
        if (ClrType == clrType && typeSymbol == null && TypeSymbol == null) return this;
        return new CapturedValueExpr(VariableName, SyntaxText, clrType, ExpressionPath, IsStaticField, typeSymbol ?? TypeSymbol);
    }

    /// <summary>Creates a copy with the IsStaticField flag set.</summary>
    public CapturedValueExpr WithStaticField(bool isStaticField)
    {
        if (IsStaticField == isStaticField) return this;
        return new CapturedValueExpr(VariableName, SyntaxText, ClrType, ExpressionPath, isStaticField, TypeSymbol);
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (CapturedValueExpr)other;
        return VariableName == o.VariableName
            && SyntaxText == o.SyntaxText
            && ExpressionPath == o.ExpressionPath
            && ClrType == o.ClrType
            && IsStaticField == o.IsStaticField;
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
/// Sql.Raw&lt;T&gt;(template, args...) call. Template contains {0}/{1} placeholders
/// that are substituted with the argument expressions during rendering.
/// </summary>
internal sealed class RawCallExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.RawCall;

    /// <summary>SQL template string with @p0, @p1 placeholders.</summary>
    public string Template { get; }

    /// <summary>Argument expressions to substitute for @p0, @p1, etc.</summary>
    public IReadOnlyList<SqlExpr> Arguments { get; }

    public RawCallExpr(string template, IReadOnlyList<SqlExpr> arguments)
        : base(ComputeHash(template, arguments))
    {
        Template = template;
        Arguments = arguments;
    }

    private static int ComputeHash(string template, IReadOnlyList<SqlExpr> arguments)
    {
        var hash = HashCode.Combine(SqlExprKind.RawCall, template);
        for (int i = 0; i < arguments.Count; i++)
            hash = HashCode.Combine(hash, arguments[i]);
        return hash;
    }

    /// <summary>
    /// Validates that the template placeholders are sequential and match the argument count.
    /// Returns null if valid, or an error message if invalid.
    /// </summary>
    public string? Validate()
    {
        // Scan template for {N} placeholders and collect indices
        var foundIndices = new HashSet<int>();
        int maxIndex = -1;
        int pos = 0;
        while (pos < Template.Length)
        {
            if (Template[pos] == '{')
            {
                int numStart = pos + 1;
                int numEnd = numStart;
                while (numEnd < Template.Length && Template[numEnd] >= '0' && Template[numEnd] <= '9')
                    numEnd++;
                if (numEnd > numStart && numEnd < Template.Length && Template[numEnd] == '}'
                    && int.TryParse(Template.Substring(numStart, numEnd - numStart), out int idx))
                {
                    foundIndices.Add(idx);
                    if (idx > maxIndex) maxIndex = idx;
                    pos = numEnd + 1;
                    continue;
                }
            }
            pos++;
        }

        if (foundIndices.Count == 0 && Arguments.Count == 0)
            return null; // no placeholders, no arguments -- valid

        if (foundIndices.Count == 0 && Arguments.Count > 0)
            return $"template has no placeholders but {Arguments.Count} argument(s) were supplied";

        if (Arguments.Count == 0 && foundIndices.Count > 0)
            return $"template has {foundIndices.Count} placeholder(s) but no arguments were supplied";

        // Check sequential from 0
        for (int i = 0; i <= maxIndex; i++)
        {
            if (!foundIndices.Contains(i))
                return $"placeholder {{{i}}} is missing (placeholders must be sequential starting from {{0}})";
        }

        if (maxIndex + 1 > Arguments.Count)
            return $"template references {{{maxIndex}}} but only {Arguments.Count} argument(s) were supplied";

        if (Arguments.Count > maxIndex + 1)
            return $"template has {maxIndex + 1} placeholder(s) but {Arguments.Count} argument(s) were supplied";

        return null;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (RawCallExpr)other;
        if (Template != o.Template || Arguments.Count != o.Arguments.Count)
            return false;
        for (int i = 0; i < Arguments.Count; i++)
            if (!Arguments[i].Equals(o.Arguments[i]))
                return false;
        return true;
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
/// Correlated subquery expression from navigation property collection methods
/// (e.g., u.Orders.Any(), u.Orders.Count(o => o.Total > 100)).
/// Created unresolved by the parser; resolved by the binder with entity metadata.
/// </summary>
internal sealed class SubqueryExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.Subquery;

    // --- Parser-assigned fields (unresolved) ---
    public string OuterParameterName { get; }
    public string NavigationPropertyName { get; }
    public SubqueryKind SubqueryKind { get; }
    public SqlExpr? Predicate { get; }
    public string? InnerParameterName { get; }

    // --- Binder-assigned fields (resolved) ---
    public string? InnerTableQuoted { get; }
    public string? InnerAliasQuoted { get; }
    public string? CorrelationSql { get; }
    public bool IsResolved { get; }

    public SubqueryExpr(
        string outerParameterName,
        string navigationPropertyName,
        SubqueryKind subqueryKind,
        SqlExpr? predicate,
        string? innerParameterName)
        : base(ComputeHash(outerParameterName, navigationPropertyName, subqueryKind, predicate))
    {
        OuterParameterName = outerParameterName;
        NavigationPropertyName = navigationPropertyName;
        SubqueryKind = subqueryKind;
        Predicate = predicate;
        InnerParameterName = innerParameterName;
        IsResolved = false;
    }

    public SubqueryExpr(
        string outerParameterName,
        string navigationPropertyName,
        SubqueryKind subqueryKind,
        SqlExpr? predicate,
        string? innerParameterName,
        string innerTableQuoted,
        string innerAliasQuoted,
        string correlationSql)
        : base(ComputeHash(outerParameterName, navigationPropertyName, subqueryKind, predicate))
    {
        OuterParameterName = outerParameterName;
        NavigationPropertyName = navigationPropertyName;
        SubqueryKind = subqueryKind;
        Predicate = predicate;
        InnerParameterName = innerParameterName;
        InnerTableQuoted = innerTableQuoted;
        InnerAliasQuoted = innerAliasQuoted;
        CorrelationSql = correlationSql;
        IsResolved = true;
    }

    private static int ComputeHash(string outerParam, string navProp, SubqueryKind kind, SqlExpr? predicate)
    {
        var hc = new HashCode();
        hc.Add(SqlExprKind.Subquery);
        hc.Add(outerParam);
        hc.Add(navProp);
        hc.Add((int)kind);
        if (predicate != null) hc.Add(predicate.GetHashCode());
        return hc.ToHashCode();
    }

    /// <summary>
    /// Implicit joins from One&lt;T&gt; navigation access inside the subquery predicate.
    /// Set by the binder; null if no implicit joins exist.
    /// </summary>
    public IReadOnlyList<ImplicitJoinInfo>? ImplicitJoins { get; }

    /// <summary>
    /// Creates a resolved subquery with implicit joins.
    /// </summary>
    public SubqueryExpr WithImplicitJoins(IReadOnlyList<ImplicitJoinInfo>? implicitJoins)
    {
        if (implicitJoins == null || implicitJoins.Count == 0)
            return this;
        return new SubqueryExpr(
            OuterParameterName, NavigationPropertyName, SubqueryKind,
            Predicate, InnerParameterName,
            InnerTableQuoted!, InnerAliasQuoted!, CorrelationSql!,
            implicitJoins);
    }

    private SubqueryExpr(
        string outerParameterName,
        string navigationPropertyName,
        SubqueryKind subqueryKind,
        SqlExpr? predicate,
        string? innerParameterName,
        string innerTableQuoted,
        string innerAliasQuoted,
        string correlationSql,
        IReadOnlyList<ImplicitJoinInfo>? implicitJoins)
        : base(ComputeHash(outerParameterName, navigationPropertyName, subqueryKind, predicate))
    {
        OuterParameterName = outerParameterName;
        NavigationPropertyName = navigationPropertyName;
        SubqueryKind = subqueryKind;
        Predicate = predicate;
        InnerParameterName = innerParameterName;
        InnerTableQuoted = innerTableQuoted;
        InnerAliasQuoted = innerAliasQuoted;
        CorrelationSql = correlationSql;
        IsResolved = true;
        ImplicitJoins = implicitJoins;
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (SubqueryExpr)other;
        return OuterParameterName == o.OuterParameterName
            && NavigationPropertyName == o.NavigationPropertyName
            && SubqueryKind == o.SubqueryKind
            && InnerParameterName == o.InnerParameterName
            && Equals(Predicate, o.Predicate);
    }
}

/// <summary>
/// Navigation access expression representing property access through One&lt;T&gt; navigation chains.
/// Created by the parser when a member access chain traverses a potential navigation property.
/// Resolved by the binder into a ResolvedColumnExpr with an implicit JOIN.
/// Uses a flat representation: NavigationHops lists all intermediate One&lt;T&gt; navigations,
/// and FinalPropertyName is the leaf column on the final entity.
/// </summary>
internal sealed class NavigationAccessExpr : SqlExpr
{
    public override SqlExprKind Kind => SqlExprKind.NavigationAccess;

    /// <summary>The lambda parameter that starts the chain (e.g., "o").</summary>
    public string SourceParameterName { get; }

    /// <summary>Sequence of navigation property names traversed (e.g., ["User", "Department"]).</summary>
    public IReadOnlyList<string> NavigationHops { get; }

    /// <summary>The leaf property on the final entity (e.g., "UserName").</summary>
    public string FinalPropertyName { get; }

    /// <summary>For Ref.Id access on the leaf (e.g., "Id"), otherwise null.</summary>
    public string? FinalNestedProperty { get; }

    public NavigationAccessExpr(
        string sourceParameterName,
        IReadOnlyList<string> navigationHops,
        string finalPropertyName,
        string? finalNestedProperty = null)
        : base(ComputeHash(sourceParameterName, navigationHops, finalPropertyName, finalNestedProperty))
    {
        SourceParameterName = sourceParameterName;
        NavigationHops = navigationHops;
        FinalPropertyName = finalPropertyName;
        FinalNestedProperty = finalNestedProperty;
    }

    private static int ComputeHash(string source, IReadOnlyList<string> hops, string finalProp, string? nestedProp)
    {
        var hc = new HashCode();
        hc.Add(SqlExprKind.NavigationAccess);
        hc.Add(source);
        foreach (var hop in hops) hc.Add(hop);
        hc.Add(finalProp);
        if (nestedProp != null) hc.Add(nestedProp);
        return hc.ToHashCode();
    }

    protected override bool DeepEquals(SqlExpr other)
    {
        var o = (NavigationAccessExpr)other;
        if (SourceParameterName != o.SourceParameterName
            || FinalPropertyName != o.FinalPropertyName
            || FinalNestedProperty != o.FinalNestedProperty
            || NavigationHops.Count != o.NavigationHops.Count)
            return false;
        for (int i = 0; i < NavigationHops.Count; i++)
            if (NavigationHops[i] != o.NavigationHops[i]) return false;
        return true;
    }
}
