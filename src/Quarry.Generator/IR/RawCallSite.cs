using System;
using System.Collections.Generic;
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
        HashSet<string>? initializedPropertyNames = null,
        int? constantIntValue = null,
        bool isNavigationJoin = false,
        string? contextClassName = null,
        string? contextNamespace = null)
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

    // Insert-specific
    public HashSet<string>? InitializedPropertyNames { get; }

    // Pagination
    public int? ConstantIntValue { get; }

    // Navigation join flag
    public bool IsNavigationJoin { get; }

    // Context info (resolved during discovery for chain root sites)
    public string? ContextClassName { get; }
    public string? ContextNamespace { get; }

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
            && InterceptableLocationData == other.InterceptableLocationData
            && InterceptableLocationVersion == other.InterceptableLocationVersion
            && IsDescending == other.IsDescending
            && ConstantIntValue == other.ConstantIntValue
            && IsNavigationJoin == other.IsNavigationJoin
            && ContextClassName == other.ContextClassName
            && ContextNamespace == other.ContextNamespace
            && Equals(Expression, other.Expression)
            && Equals(ProjectionInfo, other.ProjectionInfo);
    }

    public override bool Equals(object? obj) => Equals(obj as RawCallSite);

    public override int GetHashCode()
    {
        return HashCode.Combine(UniqueId, MethodName, FilePath, Line, Column);
    }
}
