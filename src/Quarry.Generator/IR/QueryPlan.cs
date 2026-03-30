using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.IR;

// Uses QueryKind from Models/PrebuiltChainInfo.cs
// Uses OptimizationTier from Models/ChainAnalysisResult.cs

/// <summary>
/// A complete query plan built from an analyzed chain.
/// Dialect-agnostic. Describes the logical structure of a query
/// independent of SQL dialect or parameter formatting.
/// </summary>
internal sealed class QueryPlan : IEquatable<QueryPlan>
{
    public QueryPlan(
        QueryKind kind,
        TableRef primaryTable,
        IReadOnlyList<JoinPlan> joins,
        IReadOnlyList<WhereTerm> whereTerms,
        IReadOnlyList<OrderTerm> orderTerms,
        IReadOnlyList<SqlExpr> groupByExprs,
        IReadOnlyList<SqlExpr> havingExprs,
        SelectProjection projection,
        PaginationPlan? pagination,
        bool isDistinct,
        IReadOnlyList<SetTerm> setTerms,
        IReadOnlyList<InsertColumn> insertColumns,
        IReadOnlyList<ConditionalTerm> conditionalTerms,
        IReadOnlyList<int> possibleMasks,
        IReadOnlyList<QueryParameter> parameters,
        OptimizationTier tier,
        string? notAnalyzableReason = null,
        IReadOnlyList<string>? unmatchedMethodNames = null,
        string? forkedVariableName = null)
    {
        Kind = kind;
        PrimaryTable = primaryTable;
        Joins = joins;
        WhereTerms = whereTerms;
        OrderTerms = orderTerms;
        GroupByExprs = groupByExprs;
        HavingExprs = havingExprs;
        Projection = projection;
        Pagination = pagination;
        IsDistinct = isDistinct;
        SetTerms = setTerms;
        InsertColumns = insertColumns;
        ConditionalTerms = conditionalTerms;
        PossibleMasks = possibleMasks;
        Parameters = parameters;
        Tier = tier;
        NotAnalyzableReason = notAnalyzableReason;
        UnmatchedMethodNames = unmatchedMethodNames;
        ForkedVariableName = forkedVariableName;
    }

    public QueryKind Kind { get; }
    public TableRef PrimaryTable { get; }
    public IReadOnlyList<JoinPlan> Joins { get; }
    public IReadOnlyList<WhereTerm> WhereTerms { get; }
    public IReadOnlyList<OrderTerm> OrderTerms { get; }
    public IReadOnlyList<SqlExpr> GroupByExprs { get; }
    public IReadOnlyList<SqlExpr> HavingExprs { get; }
    public SelectProjection Projection { get; }
    public PaginationPlan? Pagination { get; }
    public bool IsDistinct { get; }
    public IReadOnlyList<SetTerm> SetTerms { get; }
    public IReadOnlyList<InsertColumn> InsertColumns { get; }
    public IReadOnlyList<ConditionalTerm> ConditionalTerms { get; }
    public IReadOnlyList<int> PossibleMasks { get; }
    public IReadOnlyList<QueryParameter> Parameters { get; }
    public OptimizationTier Tier { get; }
    public string? NotAnalyzableReason { get; }
    public IReadOnlyList<string>? UnmatchedMethodNames { get; }
    public string? ForkedVariableName { get; }

    public bool Equals(QueryPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && Tier == other.Tier
            && IsDistinct == other.IsDistinct
            && PrimaryTable.Equals(other.PrimaryTable)
            && Projection.Equals(other.Projection)
            && Equals(Pagination, other.Pagination)
            && NotAnalyzableReason == other.NotAnalyzableReason
            && EqualityHelpers.SequenceEqual(Joins, other.Joins)
            && EqualityHelpers.SequenceEqual(WhereTerms, other.WhereTerms)
            && EqualityHelpers.SequenceEqual(OrderTerms, other.OrderTerms)
            && EqualityHelpers.SequenceEqual(SetTerms, other.SetTerms)
            && EqualityHelpers.SequenceEqual(InsertColumns, other.InsertColumns)
            && EqualityHelpers.SequenceEqual(ConditionalTerms, other.ConditionalTerms)
            && EqualityHelpers.SequenceEqual(Parameters, other.Parameters)
            && EqualityHelpers.SqlExprSequenceEqual(GroupByExprs, other.GroupByExprs)
            && EqualityHelpers.SqlExprSequenceEqual(HavingExprs, other.HavingExprs)
            && EqualityHelpers.SequenceEqual(PossibleMasks, other.PossibleMasks)
            && EqualityHelpers.NullableStringSequenceEqual(UnmatchedMethodNames, other.UnmatchedMethodNames)
            && ForkedVariableName == other.ForkedVariableName;
    }

    public override bool Equals(object? obj) => Equals(obj as QueryPlan);

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, Tier, PrimaryTable, WhereTerms.Count, Parameters.Count);
    }
}

/// <summary>
/// Reference to a database table.
/// </summary>
internal sealed class TableRef : IEquatable<TableRef>
{
    public TableRef(string tableName, string? schemaName = null, string? alias = null)
    {
        TableName = tableName;
        SchemaName = schemaName;
        Alias = alias;
    }

    public string TableName { get; }
    public string? SchemaName { get; }
    public string? Alias { get; }

    public bool Equals(TableRef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TableName == other.TableName
            && SchemaName == other.SchemaName
            && Alias == other.Alias;
    }

    public override bool Equals(object? obj) => Equals(obj as TableRef);
    public override int GetHashCode() => HashCode.Combine(TableName, SchemaName, Alias);
}

/// <summary>
/// A JOIN clause in a query plan.
/// </summary>
internal sealed class JoinPlan : IEquatable<JoinPlan>
{
    public JoinPlan(JoinClauseKind kind, TableRef table, SqlExpr onCondition, bool isNavigationJoin = false)
    {
        Kind = kind;
        Table = table;
        OnCondition = onCondition;
        IsNavigationJoin = isNavigationJoin;
    }

    public JoinClauseKind Kind { get; }
    public TableRef Table { get; }
    public SqlExpr OnCondition { get; }
    public bool IsNavigationJoin { get; }

    public bool Equals(JoinPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && Table.Equals(other.Table)
            && OnCondition.Equals(other.OnCondition)
            && IsNavigationJoin == other.IsNavigationJoin;
    }

    public override bool Equals(object? obj) => Equals(obj as JoinPlan);
    public override int GetHashCode() => HashCode.Combine(Kind, Table, IsNavigationJoin);
}

/// <summary>
/// A WHERE condition term. May be conditional (part of bitmask dispatch).
/// </summary>
internal sealed class WhereTerm : IEquatable<WhereTerm>
{
    public WhereTerm(SqlExpr condition, int? bitIndex = null)
    {
        Condition = condition;
        BitIndex = bitIndex;
    }

    public SqlExpr Condition { get; }
    public int? BitIndex { get; }

    public bool Equals(WhereTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return BitIndex == other.BitIndex
            && Condition.Equals(other.Condition);
    }

    public override bool Equals(object? obj) => Equals(obj as WhereTerm);
    public override int GetHashCode() => HashCode.Combine(BitIndex, Condition.GetHashCode());
}

/// <summary>
/// An ORDER BY term with direction.
/// </summary>
internal sealed class OrderTerm : IEquatable<OrderTerm>
{
    public OrderTerm(SqlExpr expression, bool isDescending = false, int? bitIndex = null)
    {
        Expression = expression;
        IsDescending = isDescending;
        BitIndex = bitIndex;
    }

    public SqlExpr Expression { get; }
    public bool IsDescending { get; }
    public int? BitIndex { get; }

    public bool Equals(OrderTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsDescending == other.IsDescending
            && BitIndex == other.BitIndex
            && Expression.Equals(other.Expression);
    }

    public override bool Equals(object? obj) => Equals(obj as OrderTerm);
    public override int GetHashCode() => HashCode.Combine(IsDescending, BitIndex, Expression.GetHashCode());
}

/// <summary>
/// A SET assignment for UPDATE operations.
/// </summary>
internal sealed class SetTerm : IEquatable<SetTerm>
{
    public SetTerm(ResolvedColumnExpr column, SqlExpr value, string? customTypeMappingClass = null, int? bitIndex = null)
    {
        Column = column;
        Value = value;
        CustomTypeMappingClass = customTypeMappingClass;
        BitIndex = bitIndex;
    }

    public ResolvedColumnExpr Column { get; }
    public SqlExpr Value { get; }
    public string? CustomTypeMappingClass { get; }
    public int? BitIndex { get; }

    public bool Equals(SetTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Column.Equals(other.Column)
            && Value.Equals(other.Value)
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && BitIndex == other.BitIndex;
    }

    public override bool Equals(object? obj) => Equals(obj as SetTerm);
    public override int GetHashCode() => HashCode.Combine(Column.GetHashCode(), BitIndex);
}

/// <summary>
/// Pagination with support for both literal and parameterized values.
/// </summary>
internal sealed class PaginationPlan : IEquatable<PaginationPlan>
{
    public PaginationPlan(
        int? literalLimit = null,
        int? literalOffset = null,
        int? limitParamIndex = null,
        int? offsetParamIndex = null)
    {
        LiteralLimit = literalLimit;
        LiteralOffset = literalOffset;
        LimitParamIndex = limitParamIndex;
        OffsetParamIndex = offsetParamIndex;
    }

    public int? LiteralLimit { get; }
    public int? LiteralOffset { get; }
    public int? LimitParamIndex { get; }
    public int? OffsetParamIndex { get; }

    public bool Equals(PaginationPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return LiteralLimit == other.LiteralLimit
            && LiteralOffset == other.LiteralOffset
            && LimitParamIndex == other.LimitParamIndex
            && OffsetParamIndex == other.OffsetParamIndex;
    }

    public override bool Equals(object? obj) => Equals(obj as PaginationPlan);
    public override int GetHashCode() => HashCode.Combine(LiteralLimit, LiteralOffset);
}

/// <summary>
/// A conditional clause term with bitmask metadata.
/// </summary>
internal sealed class ConditionalTerm : IEquatable<ConditionalTerm>
{
    public ConditionalTerm(int bitIndex, ClauseRole role)
    {
        BitIndex = bitIndex;
        Role = role;
    }

    public int BitIndex { get; }
    public ClauseRole Role { get; }

    public bool Equals(ConditionalTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return BitIndex == other.BitIndex && Role == other.Role;
    }

    public override bool Equals(object? obj) => Equals(obj as ConditionalTerm);
    public override int GetHashCode() => HashCode.Combine(BitIndex, Role);
}

/// <summary>
/// A globally-indexed parameter in the query plan.
/// </summary>
internal sealed class QueryParameter : IEquatable<QueryParameter>
{
    public QueryParameter(
        int globalIndex,
        string clrType,
        string valueExpression,
        bool isCaptured = false,
        string? expressionPath = null,
        bool isCollection = false,
        string? elementTypeName = null,
        string? typeMappingClass = null,
        bool isEnum = false,
        string? enumUnderlyingType = null,
        bool isSensitive = false,
        string? entityPropertyExpression = null,
        bool needsUnsafeAccessor = false,
        bool isDirectAccessible = false,
        string? collectionAccessExpression = null,
        string? capturedFieldName = null,
        string? capturedFieldType = null,
        bool isStaticCapture = false,
        bool isEnumerableCollection = false)
    {
        GlobalIndex = globalIndex;
        ClrType = clrType;
        ValueExpression = valueExpression;
        IsCaptured = isCaptured;
        ExpressionPath = expressionPath;
        IsCollection = isCollection;
        ElementTypeName = elementTypeName;
        TypeMappingClass = typeMappingClass;
        IsEnum = isEnum;
        EnumUnderlyingType = enumUnderlyingType;
        IsSensitive = isSensitive;
        EntityPropertyExpression = entityPropertyExpression;
        NeedsUnsafeAccessor = needsUnsafeAccessor;
        IsDirectAccessible = isDirectAccessible;
        CollectionAccessExpression = collectionAccessExpression;
        CapturedFieldName = capturedFieldName;
        CapturedFieldType = capturedFieldType;
        IsStaticCapture = isStaticCapture;
        IsEnumerableCollection = isEnumerableCollection;
    }

    public int GlobalIndex { get; }
    public string ClrType { get; }
    public string ValueExpression { get; }
    public bool IsCaptured { get; }
    public string? ExpressionPath { get; }
    public bool IsCollection { get; }
    public string? ElementTypeName { get; }
    public string? TypeMappingClass { get; }
    public bool IsEnum { get; }
    public string? EnumUnderlyingType { get; }
    public bool IsSensitive { get; }
    public string? EntityPropertyExpression { get; }
    public bool NeedsUnsafeAccessor { get; }
    public bool IsDirectAccessible { get; }
    public string? CollectionAccessExpression { get; }
    public string? CapturedFieldName { get; }
    public string? CapturedFieldType { get; }
    public bool IsStaticCapture { get; }
    public bool IsEnumerableCollection { get; }

    public bool Equals(QueryParameter? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return GlobalIndex == other.GlobalIndex
            && ClrType == other.ClrType
            && ValueExpression == other.ValueExpression
            && IsCaptured == other.IsCaptured
            && ExpressionPath == other.ExpressionPath
            && IsCollection == other.IsCollection
            && ElementTypeName == other.ElementTypeName
            && TypeMappingClass == other.TypeMappingClass
            && IsEnum == other.IsEnum
            && EnumUnderlyingType == other.EnumUnderlyingType
            && IsSensitive == other.IsSensitive
            && CapturedFieldName == other.CapturedFieldName
            && CapturedFieldType == other.CapturedFieldType
            && IsStaticCapture == other.IsStaticCapture
            && IsEnumerableCollection == other.IsEnumerableCollection;
    }

    public override bool Equals(object? obj) => Equals(obj as QueryParameter);

    public override int GetHashCode()
    {
        return HashCode.Combine(GlobalIndex, ClrType, ValueExpression, IsCaptured, CapturedFieldName);
    }
}

/// <summary>
/// SELECT projection plan.
/// </summary>
internal sealed class SelectProjection : IEquatable<SelectProjection>
{
    public SelectProjection(
        ProjectionKind kind,
        string resultTypeName,
        IReadOnlyList<ProjectedColumn> columns,
        string? customEntityReaderClass = null,
        bool isIdentity = false)
    {
        Kind = kind;
        ResultTypeName = resultTypeName;
        Columns = columns;
        CustomEntityReaderClass = customEntityReaderClass;
        IsIdentity = isIdentity;
    }

    public ProjectionKind Kind { get; }
    public string ResultTypeName { get; }
    public IReadOnlyList<ProjectedColumn> Columns { get; }
    public string? CustomEntityReaderClass { get; }
    public bool IsIdentity { get; }

    public bool Equals(SelectProjection? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && ResultTypeName == other.ResultTypeName
            && IsIdentity == other.IsIdentity
            && CustomEntityReaderClass == other.CustomEntityReaderClass
            && EqualityHelpers.SequenceEqual(Columns, other.Columns);
    }

    public override bool Equals(object? obj) => Equals(obj as SelectProjection);
    public override int GetHashCode() => HashCode.Combine(Kind, ResultTypeName, IsIdentity, Columns.Count);
}

/// <summary>
/// A column in an INSERT statement.
/// </summary>
internal sealed class InsertColumn : IEquatable<InsertColumn>
{
    public InsertColumn(string quotedColumnName, int parameterIndex, bool isIdentity = false)
    {
        QuotedColumnName = quotedColumnName;
        ParameterIndex = parameterIndex;
        IsIdentity = isIdentity;
    }

    public string QuotedColumnName { get; }
    public int ParameterIndex { get; }
    public bool IsIdentity { get; }

    public bool Equals(InsertColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return QuotedColumnName == other.QuotedColumnName
            && ParameterIndex == other.ParameterIndex
            && IsIdentity == other.IsIdentity;
    }

    public override bool Equals(object? obj) => Equals(obj as InsertColumn);
    public override int GetHashCode() => HashCode.Combine(QuotedColumnName, ParameterIndex, IsIdentity);
}
