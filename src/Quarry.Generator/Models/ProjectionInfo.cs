using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents the analyzed projection from a Select() lambda expression.
/// Contains all information needed to generate column list and reader delegate.
/// </summary>
internal sealed class ProjectionInfo : IEquatable<ProjectionInfo>
{
    public ProjectionInfo(
        ProjectionKind kind,
        string resultTypeName,
        IReadOnlyList<ProjectedColumn> columns,
        bool isOptimalPath = true,
        string? nonOptimalReason = null,
        ProjectionFailureReason failureReason = ProjectionFailureReason.None,
        string? customEntityReaderClass = null,
        string? joinedEntityAlias = null)
    {
        Kind = kind;
        ResultTypeName = resultTypeName;
        Columns = columns;
        IsOptimalPath = isOptimalPath;
        NonOptimalReason = nonOptimalReason;
        FailureReason = failureReason;
        CustomEntityReaderClass = customEntityReaderClass;
        JoinedEntityAlias = joinedEntityAlias;
    }

    /// <summary>
    /// Gets the kind of projection (entity, anonymous, DTO, tuple).
    /// </summary>
    public ProjectionKind Kind { get; }

    /// <summary>
    /// Gets the fully qualified result type name.
    /// </summary>
    public string ResultTypeName { get; }

    /// <summary>
    /// Gets the columns included in the projection.
    /// </summary>
    public IReadOnlyList<ProjectedColumn> Columns { get; }

    /// <summary>
    /// Gets whether this projection can be optimally compiled.
    /// </summary>
    public bool IsOptimalPath { get; }

    /// <summary>
    /// Gets the reason why this projection is not optimal, if applicable.
    /// </summary>
    public string? NonOptimalReason { get; }

    /// <summary>
    /// Gets the reason for projection failure, if any.
    /// </summary>
    public ProjectionFailureReason FailureReason { get; }

    /// <summary>
    /// Gets the fully qualified class name of a custom EntityReader&lt;T&gt; for this projection.
    /// When set, the reader delegate delegates to the cached reader instance instead of
    /// generating inline column-by-column materialization code.
    /// </summary>
    public string? CustomEntityReaderClass { get; }

    /// <summary>
    /// Gets the table alias of the joined entity selected in a whole-entity projection
    /// (e.g., "t1" for <c>.Select((s, u) => u)</c>).
    /// When set, BuildProjection populates all columns from the entity at this alias
    /// using the registry, because the placeholder analysis path cannot resolve columns
    /// at discovery time. Null for non-joined or non-entity projections.
    /// </summary>
    public string? JoinedEntityAlias { get; }

    /// <summary>
    /// Creates a projection info for a failed analysis.
    /// </summary>
    public static ProjectionInfo CreateFailed(
        string resultTypeName,
        string reason,
        ProjectionFailureReason failureReason = ProjectionFailureReason.AnalysisFailed)
    {
        return new ProjectionInfo(
            ProjectionKind.Unknown,
            resultTypeName,
            System.Array.Empty<ProjectedColumn>(),
            isOptimalPath: false,
            nonOptimalReason: reason,
            failureReason: failureReason);
    }

    public bool Equals(ProjectionInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && ResultTypeName == other.ResultTypeName
            && IsOptimalPath == other.IsOptimalPath
            && NonOptimalReason == other.NonOptimalReason
            && FailureReason == other.FailureReason
            && CustomEntityReaderClass == other.CustomEntityReaderClass
            && JoinedEntityAlias == other.JoinedEntityAlias
            && EqualityHelpers.SequenceEqual(Columns, other.Columns);
    }

    public override bool Equals(object? obj) => Equals(obj as ProjectionInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, ResultTypeName, IsOptimalPath, Columns.Count, JoinedEntityAlias);
    }
}

/// <summary>
/// Represents a single column in a projection.
/// </summary>
internal sealed class ProjectedColumn : IEquatable<ProjectedColumn>
{
    public ProjectedColumn(
        string propertyName,
        string columnName,
        string clrType,
        string fullClrType,
        bool isNullable,
        int ordinal,
        string? alias = null,
        string? sqlExpression = null,
        bool isAggregateFunction = false,
        string? customTypeMapping = null,
        bool isValueType = false,
        string readerMethodName = "GetValue",
        string? tableAlias = null,
        bool isForeignKey = false,
        string? foreignKeyEntityName = null,
        bool isEnum = false,
        IReadOnlyList<string>? navigationHops = null)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        ClrType = clrType;
        FullClrType = fullClrType;
        IsNullable = isNullable;
        Ordinal = ordinal;
        Alias = alias;
        SqlExpression = sqlExpression;
        IsAggregateFunction = isAggregateFunction;
        CustomTypeMapping = customTypeMapping;
        IsValueType = isValueType;
        ReaderMethodName = readerMethodName;
        TableAlias = tableAlias;
        IsForeignKey = isForeignKey;
        ForeignKeyEntityName = foreignKeyEntityName;
        IsEnum = isEnum;
        NavigationHops = navigationHops;
    }

    /// <summary>
    /// Gets the property name in the result type.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the simple CLR type (e.g., "int", "string").
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// Gets the fully qualified CLR type.
    /// </summary>
    public string FullClrType { get; }

    /// <summary>
    /// Gets whether this column is nullable.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// Gets the ordinal position in the result set (0-based).
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// Gets the alias for this column in the SELECT list, if different from column name.
    /// Used for computed expressions or property renames.
    /// </summary>
    public string? Alias { get; }

    /// <summary>
    /// Gets the SQL expression for this column if it's not a simple column reference.
    /// Used for computed columns or aggregate functions.
    /// </summary>
    public string? SqlExpression { get; }

    /// <summary>
    /// Gets whether this column is an aggregate function (COUNT, SUM, etc.).
    /// </summary>
    public bool IsAggregateFunction { get; }

    /// <summary>
    /// Gets the custom type mapping class name, if this column uses one.
    /// </summary>
    public string? CustomTypeMapping { get; }

    /// <summary>
    /// Gets whether this column's CLR type is a value type (struct).
    /// Used to determine nullability handling in reader code generation.
    /// </summary>
    public bool IsValueType { get; }

    /// <summary>
    /// Gets the DbDataReader method name for reading this column (e.g., "GetInt32", "GetString").
    /// Computed from the ITypeSymbol during analysis to avoid string parsing later.
    /// </summary>
    public string ReaderMethodName { get; }

    /// <summary>
    /// Gets the table alias for this column in joined queries (e.g., "t0", "t1").
    /// Null for non-joined queries where no aliasing is needed.
    /// </summary>
    public string? TableAlias { get; }

    /// <summary>
    /// Gets whether this column is a foreign key (Ref&lt;TEntity, TKey&gt;).
    /// When true, reader code must wrap the raw key value in new Ref&lt;TEntity, TKey&gt;(...).
    /// </summary>
    public bool IsForeignKey { get; }

    /// <summary>
    /// Gets the referenced entity type name for foreign key columns (e.g., "User").
    /// Null for non-FK columns.
    /// </summary>
    public string? ForeignKeyEntityName { get; }

    /// <summary>
    /// Gets whether this column is an enum type.
    /// When true, reader code must cast the integral value to the enum type.
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// Navigation chain hops for One&lt;T&gt; navigation access in projections.
    /// E.g., ["User"] for o.User.UserName, or ["User", "Department"] for o.User.Department.Name.
    /// Null for non-navigation columns.
    /// </summary>
    public IReadOnlyList<string>? NavigationHops { get; }

    public bool Equals(ProjectedColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && ColumnName == other.ColumnName
            && ClrType == other.ClrType
            && FullClrType == other.FullClrType
            && IsNullable == other.IsNullable
            && Ordinal == other.Ordinal
            && Alias == other.Alias
            && SqlExpression == other.SqlExpression
            && IsAggregateFunction == other.IsAggregateFunction
            && CustomTypeMapping == other.CustomTypeMapping
            && IsValueType == other.IsValueType
            && ReaderMethodName == other.ReaderMethodName
            && TableAlias == other.TableAlias
            && IsForeignKey == other.IsForeignKey
            && ForeignKeyEntityName == other.ForeignKeyEntityName
            && IsEnum == other.IsEnum
            && EqualityHelpers.SequenceEqual(NavigationHops, other.NavigationHops);
    }

    public override bool Equals(object? obj) => Equals(obj as ProjectedColumn);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ColumnName, ClrType, Ordinal, IsNullable);
    }
}

/// <summary>
/// Reasons why projection analysis failed.
/// </summary>
internal enum ProjectionFailureReason
{
    /// <summary>
    /// No failure - projection analysis succeeded.
    /// </summary>
    None,

    /// <summary>
    /// Anonymous type projections are not supported.
    /// </summary>
    AnonymousTypeNotSupported,

    /// <summary>
    /// General analysis failure.
    /// </summary>
    AnalysisFailed
}

/// <summary>
/// The kind of projection in a Select() expression.
/// </summary>
internal enum ProjectionKind
{
    /// <summary>
    /// Full entity projection: Select(u => u)
    /// </summary>
    Entity,

    /// <summary>
    /// Anonymous type projection: Select(u => new { u.Id, u.Name })
    /// </summary>
    Anonymous,

    /// <summary>
    /// Named DTO projection: Select(u => new UserDto { Id = u.Id })
    /// </summary>
    Dto,

    /// <summary>
    /// Tuple projection: Select(u => (u.Id, u.Name))
    /// </summary>
    Tuple,

    /// <summary>
    /// Single column projection: Select(u => u.Id)
    /// </summary>
    SingleColumn,

    /// <summary>
    /// Unknown or unsupported projection type.
    /// </summary>
    Unknown
}
