using System;
using Quarry.Generators.Sql;
using Quarry;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a discovered method call site on a Quarry builder type.
/// Contains the location information needed for [InterceptsLocation] attribute generation.
/// </summary>
internal sealed class UsageSiteInfo : IEquatable<UsageSiteInfo>
{
    public UsageSiteInfo(
        string methodName,
        string filePath,
        int line,
        int column,
        string builderTypeName,
        string entityTypeName,
        bool isAnalyzable,
        InterceptorKind kind,
        SyntaxNode invocationSyntax,
        string uniqueId,
        string? resultTypeName = null,
        string? nonAnalyzableReason = null,
        string? contextClassName = null,
        string? contextNamespace = null,
        ProjectionInfo? projectionInfo = null,
        ClauseInfo? clauseInfo = null,
        string? interceptableLocationData = null,
        int interceptableLocationVersion = 1,
        PendingClauseInfo? pendingClauseInfo = null,
        InsertInfo? insertInfo = null,
        string? joinedEntityTypeName = null,
        System.Collections.Generic.IReadOnlyList<string>? joinedEntityTypeNames = null,
        SqlDialect? dialect = null,
        System.Collections.Generic.HashSet<string>? initializedPropertyNames = null,
        InsertInfo? updateInfo = null,
        string? keyTypeName = null,
        RawSqlTypeInfo? rawSqlTypeInfo = null,
        bool isNavigationJoin = false,
        int? constantIntValue = null,
        BuilderKind builderKind = BuilderKind.Query,
        string? valueTypeName = null)
    {
        MethodName = methodName;
        FilePath = filePath;
        Line = line;
        Column = column;
        BuilderTypeName = builderTypeName;
        BuilderKind = builderKind;
        EntityTypeName = entityTypeName;
        ResultTypeName = resultTypeName;
        IsAnalyzable = isAnalyzable;
        NonAnalyzableReason = nonAnalyzableReason;
        Kind = kind;
        InvocationSyntax = invocationSyntax;
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        UniqueId = uniqueId;
        ProjectionInfo = projectionInfo;
        ClauseInfo = clauseInfo;
        InterceptableLocationData = interceptableLocationData;
        InterceptableLocationVersion = interceptableLocationVersion;
        PendingClauseInfo = pendingClauseInfo;
        InsertInfo = insertInfo;
        JoinedEntityTypeName = joinedEntityTypeName;
        JoinedEntityTypeNames = joinedEntityTypeNames;
        Dialect = dialect;
        InitializedPropertyNames = initializedPropertyNames;
        UpdateInfo = updateInfo;
        KeyTypeName = keyTypeName;
        RawSqlTypeInfo = rawSqlTypeInfo;
        IsNavigationJoin = isNavigationJoin;
        ConstantIntValue = constantIntValue;
        ValueTypeName = valueTypeName;
    }

    /// <summary>
    /// Gets the name of the method being called (e.g., "Select", "Where", "ExecuteFetchAllAsync").
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the file path where the method call occurs.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the 1-based line number where the method call occurs.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the 1-based column number where the method name starts.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Gets the fully qualified name of the builder type (e.g., "Quarry.QueryBuilder`1").
    /// </summary>
    public string BuilderTypeName { get; }

    /// <summary>
    /// Gets the classified builder kind for fast enum-based branching.
    /// </summary>
    public BuilderKind BuilderKind { get; }

    /// <summary>
    /// Gets the entity type name from the builder generic parameter.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// Gets the result type name for QueryBuilder{TEntity, TResult}, or null for QueryBuilder{TEntity}.
    /// </summary>
    public string? ResultTypeName { get; }

    /// <summary>
    /// Gets whether this usage site is analyzable (fluent chain in single expression).
    /// </summary>
    public bool IsAnalyzable { get; }

    /// <summary>
    /// Gets the reason why the usage site is not analyzable, if applicable.
    /// </summary>
    public string? NonAnalyzableReason { get; }

    /// <summary>
    /// Gets the kind of interceptor method to generate.
    /// </summary>
    public InterceptorKind Kind { get; }

    /// <summary>
    /// Gets the syntax node for the invocation expression.
    /// Used for semantic analysis during code generation.
    /// </summary>
    public SyntaxNode InvocationSyntax { get; }

    /// <summary>
    /// Gets the context class name associated with this usage site.
    /// </summary>
    public string? ContextClassName { get; }

    /// <summary>
    /// Gets the namespace of the owning QuarryContext, used for generating using directives.
    /// </summary>
    public string? ContextNamespace { get; }

    /// <summary>
    /// Gets the unique identifier for this usage site.
    /// Used for generating unique interceptor method names.
    /// </summary>
    public string UniqueId { get; }

    /// <summary>
    /// Gets the projection information for Select() interceptors.
    /// </summary>
    public ProjectionInfo? ProjectionInfo { get; }

    /// <summary>
    /// Gets the clause information for Where/OrderBy/GroupBy/Having/Set interceptors.
    /// </summary>
    public ClauseInfo? ClauseInfo { get; }

    /// <summary>
    /// Gets the pending clause information for deferred translation.
    /// This is set when semantic analysis fails but syntactic analysis succeeds.
    /// The clause will be translated during the enrichment phase when EntityInfo is available.
    /// </summary>
    public PendingClauseInfo? PendingClauseInfo { get; }

    /// <summary>
    /// Gets the insert operation information for InsertBuilder interceptors.
    /// Contains column metadata for generating insert SQL.
    /// </summary>
    public InsertInfo? InsertInfo { get; }

    /// <summary>
    /// Gets the opaque data string for the InterceptableLocation.
    /// Used with the new InterceptsLocation attribute format (version, data).
    /// </summary>
    public string? InterceptableLocationData { get; }

    /// <summary>
    /// Gets the version of the InterceptableLocation encoding.
    /// </summary>
    public int InterceptableLocationVersion { get; }

    /// <summary>
    /// Gets the joined entity type name for Join/LeftJoin/RightJoin calls.
    /// Extracted from the method's type argument (e.g., "Order" from Join&lt;Order&gt;()).
    /// </summary>
    public string? JoinedEntityTypeName { get; }

    /// <summary>
    /// Gets the ordered list of all entity type names from the builder's type arguments.
    /// For JoinedQueryBuilder&lt;T1,T2&gt;, this is [T1, T2].
    /// For JoinedQueryBuilder3&lt;T1,T2,T3&gt;, this is [T1, T2, T3].
    /// Null for non-joined queries.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    /// <summary>
    /// Gets the SQL dialect for this usage site, resolved from the owning QuarryContext.
    /// Null when the context could not be determined.
    /// </summary>
    public SqlDialect? Dialect { get; }

    /// <summary>
    /// Gets the set of property names explicitly set in object initializers at the call site.
    /// Null when initializer analysis is not possible (variable args, factory methods, etc.),
    /// in which case all non-identity/non-computed columns are included.
    /// </summary>
    public System.Collections.Generic.HashSet<string>? InitializedPropertyNames { get; }

    /// <summary>
    /// Gets the update operation information for UpdateSetPoco interceptors.
    /// Contains column metadata for generating SET clauses from POCO properties.
    /// </summary>
    public InsertInfo? UpdateInfo { get; }

    /// <summary>
    /// Gets the resolved key type name for OrderBy/ThenBy/GroupBy interceptors.
    /// For example, "string" from <c>.OrderBy(u => u.UserName)</c>.
    /// Used to emit non-generic (arity 0) interceptors instead of unbound &lt;TKey&gt;.
    /// </summary>
    public string? KeyTypeName { get; }

    /// <summary>
    /// Gets the resolved type information for RawSqlAsync&lt;T&gt; and RawSqlScalarAsync&lt;T&gt; interceptors.
    /// Contains the result type metadata (properties, scalar type, reader methods).
    /// </summary>
    public RawSqlTypeInfo? RawSqlTypeInfo { get; }

    /// <summary>
    /// Gets whether this join uses the navigation overload (Expression&lt;Func&lt;T, NavigationList&lt;TJoined&gt;&gt;&gt;)
    /// instead of the explicit-lambda overload (Expression&lt;Func&lt;T, TJoined, bool&gt;&gt;).
    /// </summary>
    public bool IsNavigationJoin { get; }

    /// <summary>
    /// Gets the compile-time constant integer value for Limit/Offset calls.
    /// Non-null only when the argument is a constant (literal or const variable).
    /// Used by ToDiagnostics prebuilt chains to inline literal pagination values instead of parameter placeholders.
    /// </summary>
    public int? ConstantIntValue { get; }

    /// <summary>
    /// Gets the resolved CLR type name of the Set value parameter (TValue).
    /// For example, "string" from <c>.Set(u => u.UserName, "NewName")</c>.
    /// Used to emit concrete-typed (non-generic) Set interceptor signatures for carrier optimization.
    /// </summary>
    public string? ValueTypeName { get; }

    public bool Equals(UsageSiteInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName
            && FilePath == other.FilePath
            && Line == other.Line
            && Column == other.Column
            && BuilderTypeName == other.BuilderTypeName
            && BuilderKind == other.BuilderKind
            && EntityTypeName == other.EntityTypeName
            && ResultTypeName == other.ResultTypeName
            && IsAnalyzable == other.IsAnalyzable
            && NonAnalyzableReason == other.NonAnalyzableReason
            && Kind == other.Kind
            && ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && UniqueId == other.UniqueId
            && (ProjectionInfo is null ? other.ProjectionInfo is null : ProjectionInfo.Equals(other.ProjectionInfo))
            && (ClauseInfo is null ? other.ClauseInfo is null : ClauseInfo.Equals(other.ClauseInfo))
            && (PendingClauseInfo is null ? other.PendingClauseInfo is null : PendingClauseInfo.Equals(other.PendingClauseInfo))
            && (InsertInfo is null ? other.InsertInfo is null : InsertInfo.Equals(other.InsertInfo))
            && InterceptableLocationData == other.InterceptableLocationData
            && InterceptableLocationVersion == other.InterceptableLocationVersion
            && JoinedEntityTypeName == other.JoinedEntityTypeName
            && EqualityHelpers.NullableSequenceEqual(JoinedEntityTypeNames, other.JoinedEntityTypeNames)
            && Dialect == other.Dialect
            && (UpdateInfo is null ? other.UpdateInfo is null : UpdateInfo.Equals(other.UpdateInfo))
            && KeyTypeName == other.KeyTypeName
            && (RawSqlTypeInfo is null ? other.RawSqlTypeInfo is null : RawSqlTypeInfo.Equals(other.RawSqlTypeInfo))
            && IsNavigationJoin == other.IsNavigationJoin
            && ConstantIntValue == other.ConstantIntValue
            && ValueTypeName == other.ValueTypeName;
    }

    public override bool Equals(object? obj) => Equals(obj as UsageSiteInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(MethodName, FilePath, Line, Column, UniqueId);
    }
}

/// <summary>
/// Specifies the kind of interceptor to generate.
/// </summary>
internal enum InterceptorKind
{
    /// <summary>
    /// Select() method - generates column list and reader delegate.
    /// </summary>
    Select,

    /// <summary>
    /// Where() method - generates WHERE clause SQL fragment.
    /// </summary>
    Where,

    /// <summary>
    /// OrderBy() method - generates ORDER BY clause SQL fragment.
    /// </summary>
    OrderBy,

    /// <summary>
    /// ThenBy() method - generates additional ORDER BY clause SQL fragment.
    /// </summary>
    ThenBy,

    /// <summary>
    /// GroupBy() method - generates GROUP BY clause SQL fragment.
    /// </summary>
    GroupBy,

    /// <summary>
    /// Having() method - generates HAVING clause SQL fragment.
    /// </summary>
    Having,

    /// <summary>
    /// Set() method for Update operations - generates SET clause SQL fragment.
    /// </summary>
    Set,

    /// <summary>
    /// Join() method - generates JOIN clause SQL fragment.
    /// </summary>
    Join,

    /// <summary>
    /// LeftJoin() method - generates LEFT JOIN clause SQL fragment.
    /// </summary>
    LeftJoin,

    /// <summary>
    /// RightJoin() method - generates RIGHT JOIN clause SQL fragment.
    /// </summary>
    RightJoin,

    /// <summary>
    /// ExecuteFetchAllAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchAll,

    /// <summary>
    /// ExecuteFetchFirstAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchFirst,

    /// <summary>
    /// ExecuteFetchFirstOrDefaultAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchFirstOrDefault,

    /// <summary>
    /// ExecuteFetchSingleAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchSingle,

    /// <summary>
    /// ExecuteScalarAsync() - assembles complete SQL for scalar result.
    /// </summary>
    ExecuteScalar,

    /// <summary>
    /// ExecuteNonQueryAsync() - assembles complete SQL for non-query execution.
    /// </summary>
    ExecuteNonQuery,

    /// <summary>
    /// ToAsyncEnumerable() - assembles complete SQL and wires streaming reader.
    /// </summary>
    ToAsyncEnumerable,

    /// <summary>
    /// ExecuteNonQueryAsync() on InsertBuilder - generates insert with entity property extraction.
    /// </summary>
    InsertExecuteNonQuery,

    /// <summary>
    /// ExecuteScalarAsync() on InsertBuilder - generates insert with identity return.
    /// </summary>
    InsertExecuteScalar,

    /// <summary>
    /// ToDiagnostics() method - returns prebuilt QueryDiagnostics with SQL and optimization metadata.
    /// </summary>
    ToDiagnostics,

    /// <summary>
    /// ToDiagnostics() on InsertBuilder - generates insert SQL with column metadata for QueryDiagnostics.
    /// </summary>
    InsertToDiagnostics,

    /// <summary>
    /// Where() on DeleteBuilder or ExecutableDeleteBuilder - generates WHERE clause for DELETE operations.
    /// </summary>
    DeleteWhere,

    /// <summary>
    /// Set() on UpdateBuilder or ExecutableUpdateBuilder - generates SET clause for UPDATE operations.
    /// </summary>
    UpdateSet,

    /// <summary>
    /// Where() on UpdateBuilder or ExecutableUpdateBuilder - generates WHERE clause for UPDATE operations.
    /// </summary>
    UpdateWhere,

    /// <summary>
    /// Set(entity) on UpdateBuilder or ExecutableUpdateBuilder - generates SET clauses from POCO properties.
    /// </summary>
    UpdateSetPoco,

    /// <summary>
    /// RawSqlAsync&lt;T&gt;() - generates a typed reader to replace reflection-based entity materialization.
    /// </summary>
    RawSqlAsync,

    /// <summary>
    /// RawSqlScalarAsync&lt;T&gt;() - generates a typed scalar read to replace Convert.ChangeType.
    /// </summary>
    RawSqlScalarAsync,

    /// <summary>
    /// Limit() method - sets row limit for pagination. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include parameterized LIMIT.
    /// </summary>
    Limit,

    /// <summary>
    /// Offset() method - sets row offset for pagination. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include parameterized OFFSET.
    /// </summary>
    Offset,

    /// <summary>
    /// Distinct() method - sets DISTINCT flag. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include DISTINCT.
    /// </summary>
    Distinct,

    /// <summary>
    /// WithTimeout() method - sets query timeout. Not intercepted on non-carrier path,
    /// but tracked for chain analysis so carrier can store the timeout value.
    /// </summary>
    WithTimeout,

    /// <summary>
    /// Entity set factory method on QuarryContext (e.g., db.Users()).
    /// Chain root — the first node in a query chain. On the carrier path,
    /// the chain root interceptor creates the carrier unconditionally.
    /// </summary>
    ChainRoot,

    /// <summary>
    /// .Delete() transition on IEntityAccessor — switches from accessor to IDeleteBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    DeleteTransition,

    /// <summary>
    /// .Update() transition on IEntityAccessor — switches from accessor to IUpdateBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    UpdateTransition,

    /// <summary>
    /// .All() transition on IDeleteBuilder/IUpdateBuilder — switches to IExecutableDeleteBuilder/IExecutableUpdateBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    AllTransition,

    /// <summary>
    /// .Insert(entity) transition on IEntityAccessor — switches from accessor to IInsertBuilder.
    /// On the carrier path: stores entity reference and returns carrier as IInsertBuilder.
    /// </summary>
    InsertTransition,

    /// <summary>
    /// Unknown or unsupported method.
    /// </summary>
    Unknown
}

/// <summary>
/// Classifies the builder type for fast enum-based branching instead of string.Contains() checks.
/// </summary>
internal enum BuilderKind
{
    Query,
    Delete,
    ExecutableDelete,
    Update,
    ExecutableUpdate,
    JoinedQuery,
    EntityAccessor,
}
