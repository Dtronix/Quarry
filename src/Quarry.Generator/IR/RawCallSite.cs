using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        NestingContext? nestingContext = null,
        string? chainId = null,
        string? builderTypeName = null,
        IReadOnlyList<string>? joinedEntityTypeNames = null,
        IReadOnlyList<Models.SetActionAssignment>? setActionAssignments = null,
        IReadOnlyList<Translation.ParameterInfo>? setActionParameters = null,
        IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? setActionAllCapturedIdentifiers = null,
        ImmutableArray<string>? lambdaParameterNames = null,
        ImmutableArray<string>? batchInsertColumnNames = null,
        bool isPreparedTerminal = false,
        string? preparedQueryEscapeReason = null,
        bool isValueTypeResult = false,
        string? operandChainId = null,
        int? operandArgEndLine = null,
        int? operandArgEndColumn = null,
        string? cteEntityTypeName = null,
        bool isCteInnerChain = false,
        int? cteInnerArgSpanStart = null,
        IReadOnlyList<CteColumn>? cteColumns = null,
        string? operandEntityTypeName = null,
        int? lambdaInnerSpanStart = null)
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
        NestingContext = nestingContext;
        ChainId = chainId;
        BuilderTypeName = builderTypeName;
        JoinedEntityTypeNames = joinedEntityTypeNames;
        SetActionAssignments = setActionAssignments;
        SetActionParameters = setActionParameters;
        SetActionAllCapturedIdentifiers = setActionAllCapturedIdentifiers;
        LambdaParameterNames = lambdaParameterNames;
        BatchInsertColumnNames = batchInsertColumnNames;
        IsPreparedTerminal = isPreparedTerminal;
        PreparedQueryEscapeReason = preparedQueryEscapeReason;
        IsValueTypeResult = isValueTypeResult;
        OperandChainId = operandChainId;
        OperandArgEndLine = operandArgEndLine;
        OperandArgEndColumn = operandArgEndColumn;
        CteEntityTypeName = cteEntityTypeName;
        IsCteInnerChain = isCteInnerChain;
        CteInnerArgSpanStart = cteInnerArgSpanStart;
        CteColumns = cteColumns;
        OperandEntityTypeName = operandEntityTypeName;
        LambdaInnerSpanStart = lambdaInnerSpanStart;
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
    public NestingContext? NestingContext { get; }
    public string? ChainId { get; }

    // Builder type name for codegen
    public string? BuilderTypeName { get; }

    // Joined entity type names for multi-entity joins (from semantic model during discovery)
    public IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    // SetAction data (from discovery -- Action<T> lambdas can't be parsed into SqlExpr)
    public IReadOnlyList<Models.SetActionAssignment>? SetActionAssignments { get; }
    public IReadOnlyList<Translation.ParameterInfo>? SetActionParameters { get; }

    // All external identifiers from SetAction computed expressions (for per-variable extraction)
    public IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? SetActionAllCapturedIdentifiers { get; }

    // Ordered lambda parameter names for multi-entity join ON clause resolution
    public ImmutableArray<string>? LambdaParameterNames { get; }

    // Column names from batch insert column selector lambda (e.g., u => (u.Username, u.Password) → ["Username", "Password"])
    public ImmutableArray<string>? BatchInsertColumnNames { get; }

    // True when this terminal is called on a PreparedQuery variable rather than directly on a builder
    public bool IsPreparedTerminal { get; }

    // Non-null when a .Prepare() result variable escapes scope (returned, field-assigned, passed as arg, captured in lambda)
    public string? PreparedQueryEscapeReason { get; }

    // True when TResult is a value type (tuple, primitive, enum, struct).
    // For ExecuteFetchFirstOrDefault, the interceptor must NOT append ? for value types
    // because the interface uses unconstrained TResult? which doesn't create Nullable<T> for value types.
    public bool IsValueTypeResult { get; }

    // ChainId of the operand (right-hand) query for set operations (Union, Intersect, Except, etc.)
    public string? OperandChainId { get; }

    // End line/column of the set operation argument expression (for inline operand boundary detection)
    public int? OperandArgEndLine { get; }
    public int? OperandArgEndColumn { get; }

    // CTE DTO type name (TDto) for CteDefinition and FromCte sites
    public string? CteEntityTypeName { get; }

    // True when this site is inside an inner query argument of a With<TDto>() call
    public bool IsCteInnerChain { get; }

    // For CteDefinition sites: SpanStart of the With() argument, for matching to inner chain groups
    public int? CteInnerArgSpanStart { get; }

    // CTE DTO column metadata resolved during discovery (for CteDefinition sites)
    public IReadOnlyList<CteColumn>? CteColumns { get; }

    // Fully-qualified entity type name of the operand in cross-entity set operations (e.g., Union<TOther>).
    // Null for same-entity set operations.
    public string? OperandEntityTypeName { get; }

    // For CteDefinition and set-op sites with lambda arguments: SpanStart of the lambda expression.
    // Used to link to the inner chain group whose ChainId contains `:lambda-inner:{LambdaInnerSpanStart}`.
    public int? LambdaInnerSpanStart { get; }

    // Display class name for lambda closures (computed during discovery via DisplayClassNameResolver)
    public string? DisplayClassName { get; set; }

    // Captured variable types: field name → CLR type (for UnsafeAccessor return types)
    public IReadOnlyDictionary<string, string>? CapturedVariableTypes { get; set; }

    // Capture classification set by DisplayClassEnricher. Not part of Equals/GetHashCode.
    public CaptureKind CaptureKind { get; set; }

    // RawSql type info resolved by DisplayClassEnricher using supplemental compilation.
    // Not part of Equals/GetHashCode — computed after Collect() to avoid cache instability.
    public RawSqlTypeInfo? RawSqlTypeInfo { get; set; }

    // Transient: lambda syntax for deferred batch enrichment. Not part of Equals/GetHashCode.
    public LambdaExpressionSyntax? EnrichmentLambda { get; set; }

    // Transient: invocation syntax for deferred RawSql type enrichment. Not part of Equals/GetHashCode.
    public InvocationExpressionSyntax? EnrichmentInvocation { get; set; }

    /// <summary>
    /// Returns a copy with OperandEntityTypeName replaced. Used by CallSiteBinder to normalize
    /// the cross-entity set operation operand to the per-context generated entity class.
    /// </summary>
    internal RawCallSite WithOperandEntityTypeName(string newOperandEntityTypeName)
    {
        var copy = new RawCallSite(
            methodName: MethodName,
            filePath: FilePath,
            line: Line,
            column: Column,
            uniqueId: UniqueId,
            kind: Kind,
            builderKind: BuilderKind,
            entityTypeName: EntityTypeName,
            resultTypeName: ResultTypeName,
            isAnalyzable: IsAnalyzable,
            nonAnalyzableReason: NonAnalyzableReason,
            interceptableLocationData: InterceptableLocationData,
            interceptableLocationVersion: InterceptableLocationVersion,
            location: Location,
            expression: Expression,
            clauseKind: ClauseKind,
            isDescending: IsDescending,
            projectionInfo: ProjectionInfo,
            joinedEntityTypeName: JoinedEntityTypeName,
            initializedPropertyNames: InitializedPropertyNames,
            constantIntValue: ConstantIntValue,
            isNavigationJoin: IsNavigationJoin,
            contextClassName: ContextClassName,
            contextNamespace: ContextNamespace,
            isInsideLoop: IsInsideLoop,
            isInsideTryCatch: IsInsideTryCatch,
            isCapturedInLambda: IsCapturedInLambda,
            isPassedAsArgument: IsPassedAsArgument,
            isAssignedFromNonQuarryMethod: IsAssignedFromNonQuarryMethod,
            nestingContext: NestingContext,
            chainId: ChainId,
            builderTypeName: BuilderTypeName,
            joinedEntityTypeNames: JoinedEntityTypeNames,
            setActionAssignments: SetActionAssignments,
            setActionParameters: SetActionParameters,
            setActionAllCapturedIdentifiers: SetActionAllCapturedIdentifiers,
            lambdaParameterNames: LambdaParameterNames,
            batchInsertColumnNames: BatchInsertColumnNames,
            isPreparedTerminal: IsPreparedTerminal,
            preparedQueryEscapeReason: PreparedQueryEscapeReason,
            isValueTypeResult: IsValueTypeResult,
            operandChainId: OperandChainId,
            operandArgEndLine: OperandArgEndLine,
            operandArgEndColumn: OperandArgEndColumn,
            operandEntityTypeName: newOperandEntityTypeName,
            lambdaInnerSpanStart: LambdaInnerSpanStart);
        // Propagate mutable properties set after construction
        copy.DisplayClassName = DisplayClassName;
        copy.CapturedVariableTypes = CapturedVariableTypes;
        copy.CaptureKind = CaptureKind;
        copy.RawSqlTypeInfo = RawSqlTypeInfo;
        copy.EnrichmentLambda = EnrichmentLambda;
        copy.EnrichmentInvocation = EnrichmentInvocation;
        return copy;
    }

    /// <summary>
    /// Returns a copy with EntityTypeName replaced. Used by CallSiteBinder to normalize the
    /// entity type to the context-qualified form (the per-context generated entity class)
    /// when the discovery's resolution picked up a user-written class in a different namespace.
    /// </summary>
    internal RawCallSite WithEntityTypeName(string newEntityTypeName)
    {
        var copy = new RawCallSite(
            methodName: MethodName,
            filePath: FilePath,
            line: Line,
            column: Column,
            uniqueId: UniqueId,
            kind: Kind,
            builderKind: BuilderKind,
            entityTypeName: newEntityTypeName,
            resultTypeName: ResultTypeName,
            isAnalyzable: IsAnalyzable,
            nonAnalyzableReason: NonAnalyzableReason,
            interceptableLocationData: InterceptableLocationData,
            interceptableLocationVersion: InterceptableLocationVersion,
            location: Location,
            expression: Expression,
            clauseKind: ClauseKind,
            isDescending: IsDescending,
            projectionInfo: ProjectionInfo,
            joinedEntityTypeName: JoinedEntityTypeName,
            initializedPropertyNames: InitializedPropertyNames,
            constantIntValue: ConstantIntValue,
            isNavigationJoin: IsNavigationJoin,
            contextClassName: ContextClassName,
            contextNamespace: ContextNamespace,
            isInsideLoop: IsInsideLoop,
            isInsideTryCatch: IsInsideTryCatch,
            isCapturedInLambda: IsCapturedInLambda,
            isPassedAsArgument: IsPassedAsArgument,
            isAssignedFromNonQuarryMethod: IsAssignedFromNonQuarryMethod,
            nestingContext: NestingContext,
            chainId: ChainId,
            builderTypeName: BuilderTypeName,
            joinedEntityTypeNames: JoinedEntityTypeNames,
            setActionAssignments: SetActionAssignments,
            setActionParameters: SetActionParameters,
            setActionAllCapturedIdentifiers: SetActionAllCapturedIdentifiers,
            lambdaParameterNames: LambdaParameterNames,
            batchInsertColumnNames: BatchInsertColumnNames,
            isPreparedTerminal: IsPreparedTerminal,
            preparedQueryEscapeReason: PreparedQueryEscapeReason,
            isValueTypeResult: IsValueTypeResult,
            operandChainId: OperandChainId,
            operandArgEndLine: OperandArgEndLine,
            operandArgEndColumn: OperandArgEndColumn,
            operandEntityTypeName: OperandEntityTypeName,
            lambdaInnerSpanStart: LambdaInnerSpanStart);
        // Propagate mutable properties set after construction
        copy.DisplayClassName = DisplayClassName;
        copy.CapturedVariableTypes = CapturedVariableTypes;
        copy.CaptureKind = CaptureKind;
        copy.RawSqlTypeInfo = RawSqlTypeInfo;
        copy.EnrichmentLambda = EnrichmentLambda;
        copy.EnrichmentInvocation = EnrichmentInvocation;
        return copy;
    }

    /// <summary>
    /// Creates a copy with a different ResultTypeName.
    /// Used by PipelineOrchestrator to patch unresolved tuple types after chain analysis.
    /// </summary>
    internal RawCallSite WithResultTypeName(string resolvedResultTypeName)
    {
        var copy = new RawCallSite(
            methodName: MethodName,
            filePath: FilePath,
            line: Line,
            column: Column,
            uniqueId: UniqueId,
            kind: Kind,
            builderKind: BuilderKind,
            entityTypeName: EntityTypeName,
            resultTypeName: resolvedResultTypeName,
            isAnalyzable: IsAnalyzable,
            nonAnalyzableReason: NonAnalyzableReason,
            interceptableLocationData: InterceptableLocationData,
            interceptableLocationVersion: InterceptableLocationVersion,
            location: Location,
            expression: Expression,
            clauseKind: ClauseKind,
            isDescending: IsDescending,
            projectionInfo: ProjectionInfo,
            joinedEntityTypeName: JoinedEntityTypeName,
            initializedPropertyNames: InitializedPropertyNames,
            constantIntValue: ConstantIntValue,
            isNavigationJoin: IsNavigationJoin,
            contextClassName: ContextClassName,
            contextNamespace: ContextNamespace,
            isInsideLoop: IsInsideLoop,
            isInsideTryCatch: IsInsideTryCatch,
            isCapturedInLambda: IsCapturedInLambda,
            isPassedAsArgument: IsPassedAsArgument,
            isAssignedFromNonQuarryMethod: IsAssignedFromNonQuarryMethod,
            nestingContext: NestingContext,
            chainId: ChainId,
            builderTypeName: BuilderTypeName,
            joinedEntityTypeNames: JoinedEntityTypeNames,
            setActionAssignments: SetActionAssignments,
            setActionParameters: SetActionParameters,
            setActionAllCapturedIdentifiers: SetActionAllCapturedIdentifiers,
            lambdaParameterNames: LambdaParameterNames,
            batchInsertColumnNames: BatchInsertColumnNames,
            isPreparedTerminal: IsPreparedTerminal,
            preparedQueryEscapeReason: PreparedQueryEscapeReason,
            isValueTypeResult: IsValueTypeResult,
            operandChainId: OperandChainId,
            operandArgEndLine: OperandArgEndLine,
            operandArgEndColumn: OperandArgEndColumn,
            cteEntityTypeName: CteEntityTypeName,
            isCteInnerChain: IsCteInnerChain,
            cteInnerArgSpanStart: CteInnerArgSpanStart,
            cteColumns: CteColumns,
            operandEntityTypeName: OperandEntityTypeName,
            lambdaInnerSpanStart: LambdaInnerSpanStart);
        // Propagate mutable properties set after construction
        copy.DisplayClassName = DisplayClassName;
        copy.CapturedVariableTypes = CapturedVariableTypes;
        copy.CaptureKind = CaptureKind;
        copy.RawSqlTypeInfo = RawSqlTypeInfo;
        copy.EnrichmentLambda = EnrichmentLambda;
        copy.EnrichmentInvocation = EnrichmentInvocation;
        return copy;
    }

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
            && Equals(NestingContext, other.NestingContext)
            && ImmutableArrayEqual(InitializedPropertyNames, other.InitializedPropertyNames)
            && EqualityHelpers.NullableSequenceEqual(JoinedEntityTypeNames, other.JoinedEntityTypeNames)
            && EqualityHelpers.NullableSequenceEqual(SetActionAssignments, other.SetActionAssignments)
            && EqualityHelpers.NullableSequenceEqual(SetActionParameters, other.SetActionParameters)
            && CapturedIdentifiersEqual(SetActionAllCapturedIdentifiers, other.SetActionAllCapturedIdentifiers)
            && ImmutableArrayEqual(LambdaParameterNames, other.LambdaParameterNames)
            && ImmutableArrayEqual(BatchInsertColumnNames, other.BatchInsertColumnNames)
            && IsPreparedTerminal == other.IsPreparedTerminal
            && PreparedQueryEscapeReason == other.PreparedQueryEscapeReason
            && IsValueTypeResult == other.IsValueTypeResult
            && OperandChainId == other.OperandChainId
            && OperandArgEndLine == other.OperandArgEndLine
            && OperandArgEndColumn == other.OperandArgEndColumn
            && CteEntityTypeName == other.CteEntityTypeName
            && IsCteInnerChain == other.IsCteInnerChain
            && CteInnerArgSpanStart == other.CteInnerArgSpanStart
            && EqualityHelpers.NullableSequenceEqual(CteColumns, other.CteColumns)
            && OperandEntityTypeName == other.OperandEntityTypeName
            && LambdaInnerSpanStart == other.LambdaInnerSpanStart;
    }

    public override bool Equals(object? obj) => Equals(obj as RawCallSite);

    public override int GetHashCode()
    {
        return HashCode.Combine(UniqueId, MethodName, FilePath, Line, Column);
    }

    private static bool CapturedIdentifiersEqual(
        IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? a,
        IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var otherValue) || !kvp.Value.Equals(otherValue))
                return false;
        }
        return true;
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
/// Classifies the type of variable capture for a lambda's enrichment target.
/// Set by <see cref="Parsing.DisplayClassEnricher"/> during batch enrichment.
/// </summary>
internal enum CaptureKind
{
    /// <summary>No enrichment lambda or enricher did not run.</summary>
    None = 0,

    /// <summary>Lambda captures a local variable or parameter via a compiler-generated display class.</summary>
    ClosureCapture,

    /// <summary>Lambda references a static or instance field on the containing class (not a closure local).</summary>
    FieldCapture
}

/// <summary>
/// Structural metadata about where a call site lives in the control flow graph.
/// Always present for sites inside if/else/ternary. Whether the site is genuinely
/// conditionally included is determined later by ChainAnalyzer via baseline depth comparison.
/// </summary>
internal sealed class NestingContext : IEquatable<NestingContext>
{
    public NestingContext(string conditionText, int nestingDepth, BranchKind branchKind = BranchKind.Independent)
    {
        ConditionText = conditionText;
        NestingDepth = nestingDepth;
        BranchKind = branchKind;
    }

    public string ConditionText { get; }
    public int NestingDepth { get; }
    public BranchKind BranchKind { get; }

    public bool Equals(NestingContext? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConditionText == other.ConditionText
            && NestingDepth == other.NestingDepth
            && BranchKind == other.BranchKind;
    }

    public override bool Equals(object? obj) => Equals(obj as NestingContext);
    public override int GetHashCode() => HashCode.Combine(ConditionText, NestingDepth, BranchKind);
}
