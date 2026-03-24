using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Quarry.Generators.Models;

namespace Quarry.Generators.IR;

/// <summary>
/// Lightweight discovery result. Contains only what can be determined from syntax
/// without semantic analysis or entity metadata.
/// Replaces UsageSiteInfo for the discovery stage.
/// </summary>
internal sealed class RawCallSite : IEquatable<RawCallSite>
{
    public RawCallSite(
        string methodName,
        string filePath,
        int line,
        int column,
        string uniqueId,
        InterceptorKind kind,
        BuilderKind builderKind,
        string entityTypeName,
        string? resultTypeName,
        bool isAnalyzable,
        string? nonAnalyzableReason,
        string? interceptableLocationData,
        int interceptableLocationVersion,
        DiagnosticLocation location,
        SqlExpr? expression = null,
        ClauseKind? clauseKind = null,
        bool isDescending = false,
        ProjectionInfo? projectionInfo = null,
        string? joinedEntityTypeName = null,
        ImmutableArray<string>? initializedPropertyNames = null,
        int? constantIntValue = null,
        bool isNavigationJoin = false,
        string? contextClassName = null,
        string? contextNamespace = null,
        bool isInsideLoop = false,
        bool isInsideTryCatch = false,
        bool isCapturedInLambda = false,
        bool isPassedAsArgument = false,
        bool isAssignedFromNonQuarryMethod = false,
        ConditionalInfo? conditionalInfo = null,
        string? chainId = null,
        string? builderTypeName = null,
        IReadOnlyList<string>? joinedEntityTypeNames = null,
        RawSqlTypeInfo? rawSqlTypeInfo = null,
        IReadOnlyList<Models.SetActionAssignment>? setActionAssignments = null,
        IReadOnlyList<Translation.ParameterInfo>? setActionParameters = null,
        ImmutableArray<string>? lambdaParameterNames = null,
        ImmutableArray<string>? batchInsertColumnNames = null,
        bool isPreparedTerminal = false,
        string? preparedQueryEscapeReason = null)
    {
        MethodName = methodName;
        FilePath = filePath;
        Line = line;
        Column = column;
        UniqueId = uniqueId;
        Kind = kind;
        BuilderKind = builderKind;
        EntityTypeName = entityTypeName;
        ResultTypeName = resultTypeName;
        IsAnalyzable = isAnalyzable;
        NonAnalyzableReason = nonAnalyzableReason;
        InterceptableLocationData = interceptableLocationData;
        InterceptableLocationVersion = interceptableLocationVersion;
        Location = location;
        Expression = expression;
        ClauseKind = clauseKind;
        IsDescending = isDescending;
        ProjectionInfo = projectionInfo;
        JoinedEntityTypeName = joinedEntityTypeName;
        InitializedPropertyNames = initializedPropertyNames;
        ConstantIntValue = constantIntValue;
        IsNavigationJoin = isNavigationJoin;
        ContextClassName = contextClassName;
        ContextNamespace = contextNamespace;
        IsInsideLoop = isInsideLoop;
        IsInsideTryCatch = isInsideTryCatch;
        IsCapturedInLambda = isCapturedInLambda;
        IsPassedAsArgument = isPassedAsArgument;
        IsAssignedFromNonQuarryMethod = isAssignedFromNonQuarryMethod;
        ConditionalInfo = conditionalInfo;
        ChainId = chainId;
        BuilderTypeName = builderTypeName;
        JoinedEntityTypeNames = joinedEntityTypeNames;
        RawSqlTypeInfo = rawSqlTypeInfo;
        SetActionAssignments = setActionAssignments;
        SetActionParameters = setActionParameters;
        LambdaParameterNames = lambdaParameterNames;
        BatchInsertColumnNames = batchInsertColumnNames;
        IsPreparedTerminal = isPreparedTerminal;
        PreparedQueryEscapeReason = preparedQueryEscapeReason;
    }

    // Identity and location
    public string MethodName { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string UniqueId { get; }
    public InterceptorKind Kind { get; }
    public BuilderKind BuilderKind { get; }
    public string EntityTypeName { get; }
    public string? ResultTypeName { get; }
    public bool IsAnalyzable { get; }
    public string? NonAnalyzableReason { get; }
    public string? InterceptableLocationData { get; }
    public int InterceptableLocationVersion { get; }
    public DiagnosticLocation Location { get; }

    // Expression data (parsed from syntax, before binding)
    public SqlExpr? Expression { get; }
    public ClauseKind? ClauseKind { get; }
    public bool IsDescending { get; }
    public ProjectionInfo? ProjectionInfo { get; }

    // Join-specific
    public string? JoinedEntityTypeName { get; }

    // Insert-specific (sorted at construction for deterministic equality)
    public ImmutableArray<string>? InitializedPropertyNames { get; }

    // Pagination
    public int? ConstantIntValue { get; }

    // Navigation join flag
    public bool IsNavigationJoin { get; }

    // Context info (resolved during discovery for chain root sites)
    public string? ContextClassName { get; }
    public string? ContextNamespace { get; }

    // Chain analysis support (detected during discovery where SemanticModel is available)
    public bool IsInsideLoop { get; }
    public bool IsInsideTryCatch { get; }
    public bool IsCapturedInLambda { get; }
    public bool IsPassedAsArgument { get; }
    public bool IsAssignedFromNonQuarryMethod { get; }
    public ConditionalInfo? ConditionalInfo { get; }
    public string? ChainId { get; }

    // Builder type name for codegen
    public string? BuilderTypeName { get; }

    // Joined entity type names for multi-entity joins (from semantic model during discovery)
    public IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    // RawSql type info (from discovery)
    public RawSqlTypeInfo? RawSqlTypeInfo { get; }

    // SetAction data (from discovery -- Action<T> lambdas can't be parsed into SqlExpr)
    public IReadOnlyList<Models.SetActionAssignment>? SetActionAssignments { get; }
    public IReadOnlyList<Translation.ParameterInfo>? SetActionParameters { get; }

    // Ordered lambda parameter names for multi-entity join ON clause resolution
    public ImmutableArray<string>? LambdaParameterNames { get; }

    // Column names from batch insert column selector lambda (e.g., u => (u.Username, u.Password) → ["Username", "Password"])
    public ImmutableArray<string>? BatchInsertColumnNames { get; }

    // True when this terminal is called on a PreparedQuery variable rather than directly on a builder
    public bool IsPreparedTerminal { get; }

    // Non-null when a .Prepare() result variable escapes scope (returned, field-assigned, passed as arg, captured in lambda)
    public string? PreparedQueryEscapeReason { get; }

    public bool Equals(RawCallSite? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return UniqueId == other.UniqueId
            && MethodName == other.MethodName
            && FilePath == other.FilePath
            && Line == other.Line
            && Column == other.Column
            && Kind == other.Kind
            && BuilderKind == other.BuilderKind
            && EntityTypeName == other.EntityTypeName
            && ResultTypeName == other.ResultTypeName
            && IsAnalyzable == other.IsAnalyzable
            && NonAnalyzableReason == other.NonAnalyzableReason
            && InterceptableLocationData == other.InterceptableLocationData
            && InterceptableLocationVersion == other.InterceptableLocationVersion
            && IsDescending == other.IsDescending
            && ConstantIntValue == other.ConstantIntValue
            && IsNavigationJoin == other.IsNavigationJoin
            && ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && JoinedEntityTypeName == other.JoinedEntityTypeName
            && ClauseKind == other.ClauseKind
            && IsInsideLoop == other.IsInsideLoop
            && IsInsideTryCatch == other.IsInsideTryCatch
            && IsCapturedInLambda == other.IsCapturedInLambda
            && IsPassedAsArgument == other.IsPassedAsArgument
            && IsAssignedFromNonQuarryMethod == other.IsAssignedFromNonQuarryMethod
            && ChainId == other.ChainId
            && BuilderTypeName == other.BuilderTypeName
            && Equals(Expression, other.Expression)
            && Equals(ProjectionInfo, other.ProjectionInfo)
            && Equals(ConditionalInfo, other.ConditionalInfo)
            && Equals(RawSqlTypeInfo, other.RawSqlTypeInfo)
            && ImmutableArrayEqual(InitializedPropertyNames, other.InitializedPropertyNames)
            && EqualityHelpers.NullableSequenceEqual(JoinedEntityTypeNames, other.JoinedEntityTypeNames)
            && EqualityHelpers.NullableSequenceEqual(SetActionAssignments, other.SetActionAssignments)
            && EqualityHelpers.NullableSequenceEqual(SetActionParameters, other.SetActionParameters)
            && ImmutableArrayEqual(LambdaParameterNames, other.LambdaParameterNames)
            && ImmutableArrayEqual(BatchInsertColumnNames, other.BatchInsertColumnNames)
            && IsPreparedTerminal == other.IsPreparedTerminal
            && PreparedQueryEscapeReason == other.PreparedQueryEscapeReason;
    }

    public override bool Equals(object? obj) => Equals(obj as RawCallSite);

    public override int GetHashCode()
    {
        return HashCode.Combine(UniqueId, MethodName, FilePath, Line, Column);
    }

    private static bool ImmutableArrayEqual(ImmutableArray<string>? a, ImmutableArray<string>? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        var arrA = a.Value;
        var arrB = b.Value;
        if (arrA.Length != arrB.Length) return false;
        for (int i = 0; i < arrA.Length; i++)
        {
            if (arrA[i] != arrB[i]) return false;
        }
        return true;
    }
}

/// <summary>
/// Records whether a call site is inside a conditional branch (if/else or ternary).
/// Used by ChainAnalyzer to assign bitmask indices for conditional clause dispatch.
/// </summary>
internal sealed class ConditionalInfo : IEquatable<ConditionalInfo>
{
    public ConditionalInfo(string conditionText, int nestingDepth, BranchKind branchKind = BranchKind.Independent)
    {
        ConditionText = conditionText;
        NestingDepth = nestingDepth;
        BranchKind = branchKind;
    }

    public string ConditionText { get; }
    public int NestingDepth { get; }
    public BranchKind BranchKind { get; }

    public bool Equals(ConditionalInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConditionText == other.ConditionText
            && NestingDepth == other.NestingDepth
            && BranchKind == other.BranchKind;
    }

    public override bool Equals(object? obj) => Equals(obj as ConditionalInfo);
    public override int GetHashCode() => HashCode.Combine(ConditionText, NestingDepth, BranchKind);
}
