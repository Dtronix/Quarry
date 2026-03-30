using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.IR;

/// <summary>
/// Fully translated call site with SQL expression and parameters.
/// Produced by Stage 3 (translation) from BoundCallSite.
/// </summary>
internal sealed class TranslatedCallSite : IEquatable<TranslatedCallSite>
{
    public TranslatedCallSite(
        BoundCallSite bound,
        TranslatedClause? clause = null,
        string? keyTypeName = null,
        string? valueTypeName = null)
    {
        Bound = bound;
        Clause = clause;
        KeyTypeName = keyTypeName;
        ValueTypeName = valueTypeName;
    }

    /// <summary>Underlying bound call site (composition).</summary>
    public BoundCallSite Bound { get; }

    /// <summary>
    /// Translated clause with resolved SQL expression and parameters.
    /// Null for non-clause sites (Limit, Distinct, ChainRoot, execution terminals).
    /// </summary>
    public TranslatedClause? Clause { get; }

    /// <summary>Resolved key type for OrderBy/ThenBy/GroupBy.</summary>
    public string? KeyTypeName { get; }

    /// <summary>Resolved value type for Set.</summary>
    public string? ValueTypeName { get; }

    // Convenience accessors to reduce verbosity in emitters
    public string UniqueId => Bound.Raw.UniqueId;
    public InterceptorKind Kind => Bound.Raw.Kind;
    public BuilderKind BuilderKind => Bound.Raw.BuilderKind;
    public string EntityTypeName => Bound.Raw.EntityTypeName;
    public string? ResultTypeName => Bound.Raw.ResultTypeName;
    public string MethodName => Bound.Raw.MethodName;
    public string FilePath => Bound.Raw.FilePath;
    public int Line => Bound.Raw.Line;
    public int Column => Bound.Raw.Column;
    public bool IsAnalyzable => Bound.Raw.IsAnalyzable;
    public string? NonAnalyzableReason => Bound.Raw.NonAnalyzableReason;
    public string? InterceptableLocationData => Bound.Raw.InterceptableLocationData;
    public int InterceptableLocationVersion => Bound.Raw.InterceptableLocationVersion;
    public SqlDialect Dialect => Bound.Dialect;
    public string? ContextClassName => Bound.ContextClassName;
    public string? ContextNamespace => Bound.ContextNamespace;
    public string TableName => Bound.TableName;
    public string? SchemaName => Bound.SchemaName;
    public ProjectionInfo? ProjectionInfo => Bound.Raw.ProjectionInfo;
    public InsertInfo? InsertInfo => Bound.InsertInfo;
    public InsertInfo? UpdateInfo => Bound.UpdateInfo;
    public string? JoinedEntityTypeName => Bound.JoinedEntity?.EntityName ?? Bound.Raw.JoinedEntityTypeName;
    public System.Collections.Generic.IReadOnlyList<string>? JoinedEntityTypeNames => Bound.JoinedEntityTypeNames;
    public bool IsNavigationJoin => Bound.Raw.IsNavigationJoin;
    public int? ConstantIntValue => Bound.Raw.ConstantIntValue;
    public RawSqlTypeInfo? RawSqlTypeInfo => Bound.RawSqlTypeInfo;
    public DiagnosticLocation Location => Bound.Raw.Location;
    public string BuilderTypeName => Bound.Raw.BuilderTypeName ?? Bound.Entity?.EntityName ?? Bound.Raw.EntityTypeName;
    public System.Collections.Immutable.ImmutableArray<string>? InitializedPropertyNames => Bound.Raw.InitializedPropertyNames;
    public bool IsPreparedTerminal => Bound.Raw.IsPreparedTerminal;
    public bool IsValueTypeResult => Bound.Raw.IsValueTypeResult;
    public string? DisplayClassName => Bound.Raw.DisplayClassName;
    public System.Collections.Generic.IReadOnlyDictionary<string, string>? CapturedVariableTypes => Bound.Raw.CapturedVariableTypes;
    public CaptureKind CaptureKind => Bound.Raw.CaptureKind;
    public System.Collections.Generic.IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? SetActionAllCapturedIdentifiers => Bound.Raw.SetActionAllCapturedIdentifiers;

    /// <summary>
    /// Creates a copy with updated JoinedEntityTypeNames and JoinedEntities.
    /// Used by ChainAnalyzer to propagate resolved names from the Join site to the execution site.
    /// </summary>
    public TranslatedCallSite WithJoinedEntityTypeNames(
        IReadOnlyList<string> joinedEntityTypeNames,
        IReadOnlyList<EntityRef>? joinedEntities)
    {
        var newBound = new BoundCallSite(
            raw: Bound.Raw,
            contextClassName: Bound.ContextClassName,
            contextNamespace: Bound.ContextNamespace,
            dialect: Bound.Dialect,
            tableName: Bound.TableName,
            schemaName: Bound.SchemaName,
            entity: Bound.Entity,
            joinedEntity: Bound.JoinedEntity,
            joinedEntityTypeNames: joinedEntityTypeNames,
            joinedEntities: joinedEntities,
            insertInfo: Bound.InsertInfo,
            updateInfo: Bound.UpdateInfo,
            rawSqlTypeInfo: Bound.RawSqlTypeInfo);
        return new TranslatedCallSite(newBound, Clause, KeyTypeName, ValueTypeName);
    }

    /// <summary>
    /// Creates a copy with a resolved ResultTypeName.
    /// Used by PipelineOrchestrator to patch clause sites whose ResultTypeName was
    /// unresolved during discovery (e.g., tuple types in reassigned query variables).
    /// </summary>
    internal TranslatedCallSite WithResolvedResultType(string resolvedResultTypeName)
    {
        var newRaw = Bound.Raw.WithResultTypeName(resolvedResultTypeName);
        var newBound = Bound.WithRaw(newRaw);
        return new TranslatedCallSite(newBound, Clause, KeyTypeName, ValueTypeName);
    }

    public bool Equals(TranslatedCallSite? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Bound.Equals(other.Bound)
            && Equals(Clause, other.Clause)
            && KeyTypeName == other.KeyTypeName
            && ValueTypeName == other.ValueTypeName;
    }

    public override bool Equals(object? obj) => Equals(obj as TranslatedCallSite);

    public override int GetHashCode()
    {
        return HashCode.Combine(Bound.GetHashCode(), KeyTypeName, ValueTypeName);
    }
}

/// <summary>
/// A fully translated clause with resolved SQL expression and parameters.
/// </summary>
internal sealed class TranslatedClause : IEquatable<TranslatedClause>
{
    public TranslatedClause(
        ClauseKind kind,
        SqlExpr resolvedExpression,
        IReadOnlyList<ParameterInfo> parameters,
        bool isSuccess = true,
        string? errorMessage = null,
        bool isDescending = false,
        JoinClauseKind? joinKind = null,
        string? joinedTableName = null,
        string? joinedSchemaName = null,
        string? tableAlias = null,
        IReadOnlyList<SetActionAssignment>? setAssignments = null,
        string? customTypeMappingClass = null)
    {
        Kind = kind;
        ResolvedExpression = resolvedExpression;
        Parameters = parameters;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        IsDescending = isDescending;
        JoinKind = joinKind;
        JoinedTableName = joinedTableName;
        JoinedSchemaName = joinedSchemaName;
        TableAlias = tableAlias;
        SetAssignments = setAssignments;
        CustomTypeMappingClass = customTypeMappingClass;
    }

    public ClauseKind Kind { get; }
    public SqlExpr ResolvedExpression { get; }
    public IReadOnlyList<ParameterInfo> Parameters { get; }
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public bool IsDescending { get; }
    public JoinClauseKind? JoinKind { get; }
    public string? JoinedTableName { get; }
    public string? JoinedSchemaName { get; }
    public string? TableAlias { get; }
    public IReadOnlyList<SetActionAssignment>? SetAssignments { get; }
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// Renders the resolved expression to a SQL fragment string using generic @p{n} parameter format.
    /// Convenience property for emitters that need the pre-rendered SQL.
    /// </summary>
    public string SqlFragment => _sqlFragment ??= SqlExprRenderer.Render(ResolvedExpression, Sql.SqlDialect.PostgreSQL, useGenericParamFormat: true, stripOuterParens: true);
    private string? _sqlFragment;

    /// <summary>
    /// Gets the column SQL for OrderBy/Set clauses (same as SqlFragment for these clause types).
    /// </summary>
    public string ColumnSql => SqlFragment;

    public bool Equals(TranslatedClause? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && IsSuccess == other.IsSuccess
            && ErrorMessage == other.ErrorMessage
            && IsDescending == other.IsDescending
            && JoinKind == other.JoinKind
            && JoinedTableName == other.JoinedTableName
            && JoinedSchemaName == other.JoinedSchemaName
            && TableAlias == other.TableAlias
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && ResolvedExpression.Equals(other.ResolvedExpression)
            && EqualityHelpers.SequenceEqual(Parameters, other.Parameters)
            && EqualityHelpers.NullableSequenceEqual(SetAssignments, other.SetAssignments);
    }

    public override bool Equals(object? obj) => Equals(obj as TranslatedClause);

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, IsSuccess, IsDescending, Parameters.Count);
    }
}
