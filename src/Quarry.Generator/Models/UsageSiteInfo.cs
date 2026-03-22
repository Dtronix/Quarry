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
        SyntaxNode? invocationSyntax,
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
    public SyntaxNode? InvocationSyntax { get; }

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

    /// <summary>
    /// Creates a UsageSiteInfo from a TranslatedCallSite for backward compatibility.
    /// This is a temporary adapter used during Phase 4 pipeline transition.
    /// The InvocationSyntax will be null since the new pipeline doesn't store SyntaxNode references.
    /// </summary>
    public static UsageSiteInfo FromTranslatedCallSite(IR.TranslatedCallSite translated, SyntaxNode? syntaxNode = null, ProjectionInfo? projectionOverride = null)
    {
        var bound = translated.Bound;
        var raw = bound.Raw;

        // Use the resolved joined entity type name from binding (not the raw unresolved one)
        var resolvedJoinedEntityTypeName = bound.JoinedEntity?.EntityName ?? raw.JoinedEntityTypeName;

        // Convert TranslatedClause back to ClauseInfo for the old pipeline
        ClauseInfo? clauseInfo = null;
        if (translated.Clause != null && translated.Clause.IsSuccess)
        {
            // Strip outer parens for WHERE/Having BinaryOp expressions that are compound (AND/OR)
            // or contain subquery operands (subqueries already provide their own grouping parens)
            var stripParens = false;
            if ((translated.Clause.Kind == ClauseKind.Where || translated.Clause.Kind == ClauseKind.Having)
                && translated.Clause.ResolvedExpression is IR.BinaryOpExpr topBin)
            {
                stripParens = topBin.Operator == IR.SqlBinaryOperator.And
                    || topBin.Operator == IR.SqlBinaryOperator.Or
                    || topBin.Left is IR.SubqueryExpr
                    || topBin.Right is IR.SubqueryExpr;
            }
            var sql = IR.SqlExprRenderer.Render(translated.Clause.ResolvedExpression, bound.Dialect, useGenericParamFormat: true, stripOuterParens: stripParens);
            if (!string.IsNullOrEmpty(sql))
            {
                if (translated.Clause.Kind == ClauseKind.OrderBy || translated.Clause.Kind == ClauseKind.GroupBy)
                {
                    clauseInfo = new OrderByClauseInfo(sql, translated.Clause.IsDescending, translated.Clause.Parameters, translated.KeyTypeName);
                }
                else if (translated.Clause.Kind == ClauseKind.Set && translated.Clause.SetAssignments != null)
                {
                    // SetAction: multiple assignments (Set(Action<T>) overload)
                    clauseInfo = new SetActionClauseInfo(translated.Clause.SetAssignments, translated.Clause.Parameters);
                }
                else if (translated.Clause.Kind == ClauseKind.Set)
                {
                    var setParams = translated.Clause.Parameters;
                    clauseInfo = new SetClauseInfo(sql, setParams.Count > 0 ? setParams[setParams.Count - 1].Index : 0, setParams,
                        translated.Clause.CustomTypeMappingClass, translated.ValueTypeName);
                }
                else if (translated.Clause.Kind == ClauseKind.Join)
                {
                    clauseInfo = new JoinClauseInfo(
                        translated.Clause.JoinKind ?? JoinClauseKind.Inner,
                        resolvedJoinedEntityTypeName ?? "",
                        translated.Clause.JoinedTableName ?? "",
                        sql,
                        translated.Clause.Parameters,
                        translated.Clause.JoinedSchemaName,
                        translated.Clause.TableAlias);
                }
                else
                {
                    clauseInfo = ClauseInfo.Success(translated.Clause.Kind, sql, translated.Clause.Parameters);
                }
            }
        }

        // Fallback for UpdateSetAction: clause data stored on RawCallSite (Action<T> can't be parsed to SqlExpr)
        if (clauseInfo == null && raw.SetActionAssignments != null && raw.SetActionParameters != null)
        {
            clauseInfo = new SetActionClauseInfo(raw.SetActionAssignments, raw.SetActionParameters);
        }

        // Convert ImmutableArray<string> back to HashSet<string>
        System.Collections.Generic.HashSet<string>? initializedPropertyNames = null;
        if (raw.InitializedPropertyNames.HasValue)
        {
            initializedPropertyNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var name in raw.InitializedPropertyNames.Value)
                initializedPropertyNames.Add(name);
        }

        return new UsageSiteInfo(
            methodName: raw.MethodName,
            filePath: raw.FilePath,
            line: raw.Line,
            column: raw.Column,
            builderTypeName: raw.BuilderTypeName ?? bound.Entity.EntityName,
            entityTypeName: raw.EntityTypeName,
            isAnalyzable: raw.IsAnalyzable,
            kind: raw.Kind,
            invocationSyntax: syntaxNode, // Reconstructed from Compilation in the collected stage
            uniqueId: raw.UniqueId,
            resultTypeName: projectionOverride != null ? projectionOverride.ResultTypeName : raw.ResultTypeName,
            nonAnalyzableReason: raw.NonAnalyzableReason,
            contextClassName: bound.ContextClassName,
            contextNamespace: bound.ContextNamespace,
            projectionInfo: projectionOverride ?? raw.ProjectionInfo,
            clauseInfo: clauseInfo,
            interceptableLocationData: raw.InterceptableLocationData,
            interceptableLocationVersion: raw.InterceptableLocationVersion,
            pendingClauseInfo: null, // Pending clauses are resolved during translation
            insertInfo: bound.InsertInfo,
            joinedEntityTypeName: resolvedJoinedEntityTypeName,
            joinedEntityTypeNames: bound.JoinedEntityTypeNames,
            dialect: bound.Dialect,
            initializedPropertyNames: initializedPropertyNames,
            updateInfo: bound.UpdateInfo,
            keyTypeName: translated.KeyTypeName,
            rawSqlTypeInfo: bound.RawSqlTypeInfo,
            isNavigationJoin: raw.IsNavigationJoin,
            constantIntValue: raw.ConstantIntValue,
            builderKind: raw.BuilderKind,
            valueTypeName: translated.ValueTypeName);
    }

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
