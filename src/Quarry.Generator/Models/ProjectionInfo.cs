using System;
using System.Collections.Generic;
using Quarry.Generators.IR;
using Quarry.Generators.Translation;

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
        string? joinedEntityAlias = null,
        IReadOnlyList<Translation.ParameterInfo>? projectionParameters = null,
        IReadOnlyList<string>? lambdaParameterNames = null)
    {
        Kind = kind;
        ResultTypeName = resultTypeName;
        Columns = columns;
        IsOptimalPath = isOptimalPath;
        NonOptimalReason = nonOptimalReason;
        FailureReason = failureReason;
        CustomEntityReaderClass = customEntityReaderClass;
        JoinedEntityAlias = joinedEntityAlias;
        ProjectionParameters = projectionParameters;
        LambdaParameterNames = lambdaParameterNames;
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
    /// Gets parameters captured from non-constant scalar arguments in window functions
    /// (e.g., variable references in Sql.Ntile(n, ...) or Sql.Lag(col, offset, ...)).
    /// Null when no projection parameters exist.
    /// </summary>
    public IReadOnlyList<Translation.ParameterInfo>? ProjectionParameters { get; }

    /// <summary>
    /// Gets Sql.Raw template validation errors encountered during projection analysis
    /// (e.g. placeholder/argument count mismatch). When set, the pipeline emits a QRY029
    /// diagnostic per entry, matching the Where-path behavior for invalid Sql.Raw templates.
    /// Null when analysis encountered no Sql.Raw validation failures.
    /// </summary>
    public IReadOnlyList<string>? SqlRawValidationErrors { get; init; }

    /// <summary>
    /// Gets the lambda parameter names of the Select() in source order
    /// (e.g., ["u"] for u =&gt; ..., ["u", "o"] for (u, o) =&gt; ...).
    /// Used by BuildProjection to map a navigation-aggregate column's
    /// <see cref="ProjectedColumn.OuterParameterName"/> to its owning entity (and table alias).
    /// Null when no aggregate-subquery columns are present.
    /// </summary>
    public IReadOnlyList<string>? LambdaParameterNames { get; }

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
            && EqualityHelpers.SequenceEqual(Columns, other.Columns)
            && EqualityHelpers.SequenceEqual(ProjectionParameters, other.ProjectionParameters)
            && EqualityHelpers.SequenceEqual(SqlRawValidationErrors, other.SqlRawValidationErrors)
            && EqualityHelpers.SequenceEqual(LambdaParameterNames, other.LambdaParameterNames);
    }

    public override bool Equals(object? obj) => Equals(obj as ProjectionInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, ResultTypeName, IsOptimalPath, Columns.Count, JoinedEntityAlias,
            LambdaParameterNames?.Count ?? 0);
    }
}

/// <summary>
/// Represents a single column in a projection.
/// </summary>
internal sealed record ProjectedColumn : IEquatable<ProjectedColumn>
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
        IReadOnlyList<string>? navigationHops = null,
        bool isJoinNullable = false,
        SqlExpr? subqueryExpression = null,
        string? outerParameterName = null,
        DiagnosticLocation? subqueryInvocationLocation = null)
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
        IsJoinNullable = isJoinNullable;
        SubqueryExpression = subqueryExpression;
        OuterParameterName = outerParameterName;
        SubqueryInvocationLocation = subqueryInvocationLocation;
    }

    /// <summary>
    /// Gets the property name in the result type.
    /// </summary>
    public string PropertyName { get; init; }

    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string ColumnName { get; init; }

    /// <summary>
    /// Gets the simple CLR type (e.g., "int", "string").
    /// </summary>
    public string ClrType { get; init; }

    /// <summary>
    /// Gets the fully qualified CLR type.
    /// </summary>
    public string FullClrType { get; init; }

    /// <summary>
    /// Gets whether this column is nullable (based on schema metadata).
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Gets whether this column is effectively nullable due to being on the nullable side
    /// of an outer join (LEFT, RIGHT, or FULL OUTER). When true, reader code must emit
    /// IsDBNull checks even though the schema declares the column as NOT NULL.
    /// This is separate from <see cref="IsNullable"/> to preserve the user's declared
    /// result type for interceptor signature matching.
    /// </summary>
    public bool IsJoinNullable { get; init; }

    /// <summary>
    /// Gets whether this column requires a null check in reader code generation.
    /// True when the column is schema-nullable or join-nullable.
    /// </summary>
    public bool EffectivelyNullable => IsNullable || IsJoinNullable;

    /// <summary>
    /// Gets the ordinal position in the result set (0-based).
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets the alias for this column in the SELECT list, if different from column name.
    /// Used for computed expressions or property renames.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Gets the SQL expression for this column if it's not a simple column reference.
    /// Used for computed columns or aggregate functions.
    /// </summary>
    public string? SqlExpression { get; init; }

    /// <summary>
    /// Gets whether this column is an aggregate function (COUNT, SUM, etc.).
    /// </summary>
    public bool IsAggregateFunction { get; init; }

    /// <summary>
    /// Gets the custom type mapping class name, if this column uses one.
    /// </summary>
    public string? CustomTypeMapping { get; init; }

    /// <summary>
    /// Gets whether this column's CLR type is a value type (struct).
    /// Used to determine nullability handling in reader code generation.
    /// </summary>
    public bool IsValueType { get; init; }

    /// <summary>
    /// Gets the DbDataReader method name for reading this column (e.g., "GetInt32", "GetString").
    /// Computed from the ITypeSymbol during analysis to avoid string parsing later.
    /// </summary>
    public string ReaderMethodName { get; init; }

    /// <summary>
    /// Gets the table alias for this column in joined queries (e.g., "t0", "t1").
    /// Null for non-joined queries where no aliasing is needed.
    /// </summary>
    public string? TableAlias { get; init; }

    /// <summary>
    /// Gets whether this column is a foreign key (Ref&lt;TEntity, TKey&gt;).
    /// When true, reader code must wrap the raw key value in new Ref&lt;TEntity, TKey&gt;(...).
    /// </summary>
    public bool IsForeignKey { get; init; }

    /// <summary>
    /// Gets the referenced entity type name for foreign key columns (e.g., "User").
    /// Null for non-FK columns.
    /// </summary>
    public string? ForeignKeyEntityName { get; init; }

    /// <summary>
    /// Gets whether this column is an enum type.
    /// When true, reader code must cast the integral value to the enum type.
    /// </summary>
    public bool IsEnum { get; init; }

    /// <summary>
    /// Navigation chain hops for One&lt;T&gt; navigation access in projections.
    /// E.g., ["User"] for o.User.UserName, or ["User", "Department"] for o.User.Department.Name.
    /// Null for non-navigation columns.
    /// </summary>
    public IReadOnlyList<string>? NavigationHops { get; init; }

    /// <summary>
    /// Unbound SubqueryExpr for navigation aggregates in Select projection
    /// (e.g., u.Orders.Sum(o =&gt; o.Total)). Set by ProjectionAnalyzer when it
    /// recognizes a navigation-aggregate invocation; consumed by BuildProjection
    /// which binds and renders it into <see cref="SqlExpression"/>. Null for all
    /// other column kinds.
    /// </summary>
    public SqlExpr? SubqueryExpression { get; init; }

    /// <summary>
    /// The lambda parameter name owning the navigation in <see cref="SubqueryExpression"/>
    /// (e.g., "u" for u.Orders.Sum(...)). Used by BuildProjection to look up the
    /// owning entity for binding. Null for non-subquery columns.
    /// </summary>
    public string? OuterParameterName { get; init; }

    /// <summary>
    /// Source location of the aggregate invocation (e.g., the <c>.Sum(...)</c> call) that
    /// produced <see cref="SubqueryExpression"/>. Used by BuildProjection as the QRY074
    /// emission site so the error squiggle points at the offending navigation aggregate
    /// rather than the enclosing chain. Null for non-subquery columns.
    /// </summary>
    public DiagnosticLocation? SubqueryInvocationLocation { get; init; }

    public bool Equals(ProjectedColumn? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && ColumnName == other.ColumnName
            && ClrType == other.ClrType
            && FullClrType == other.FullClrType
            && IsNullable == other.IsNullable
            && IsJoinNullable == other.IsJoinNullable
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
            && EqualityHelpers.SequenceEqual(NavigationHops, other.NavigationHops)
            && EqualityComparer<IR.SqlExpr?>.Default.Equals(SubqueryExpression, other.SubqueryExpression)
            && OuterParameterName == other.OuterParameterName
            && Nullable.Equals(SubqueryInvocationLocation, other.SubqueryInvocationLocation);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ColumnName, ClrType, Ordinal, IsNullable, IsJoinNullable);
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
    AnalysisFailed,

    /// <summary>
    /// An Sql.Raw&lt;T&gt; call inside a Select projection failed template validation
    /// (placeholder/argument count mismatch, etc.). The projection degrades to runtime build
    /// and the pipeline emits a QRY029 diagnostic from
    /// <see cref="ProjectionInfo.SqlRawValidationErrors"/>.
    /// </summary>
    SqlRawValidationError
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
