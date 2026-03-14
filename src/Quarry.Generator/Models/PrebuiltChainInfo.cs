using System;
using System.Collections.Generic;
using System.Linq;
using Quarry.Generators.Sql;

namespace Quarry.Generators.Models;

/// <summary>
/// The kind of query for execution interceptor routing.
/// </summary>
internal enum QueryKind
{
    Select,
    Delete,
    Update
}

/// <summary>
/// Bundles a <see cref="ChainAnalysisResult"/> with the pre-built SQL map and metadata
/// needed by <see cref="Quarry.Generators.Generation.InterceptorCodeGenerator"/> to emit
/// an execution interceptor.
/// </summary>
internal sealed class PrebuiltChainInfo : IEquatable<PrebuiltChainInfo>
{
    public PrebuiltChainInfo(
        ChainAnalysisResult analysis,
        Dictionary<ulong, PrebuiltSqlResult> sqlMap,
        string? readerDelegateCode,
        string entityTypeName,
        string? resultTypeName,
        SqlDialect dialect,
        string tableName,
        string? schemaName,
        QueryKind queryKind,
        ProjectionInfo? projectionInfo,
        IReadOnlyList<string>? joinedEntityTypeNames = null,
        IReadOnlyList<(string TableName, string? SchemaName)>? joinedTableInfos = null)
    {
        Analysis = analysis;
        SqlMap = sqlMap;
        ReaderDelegateCode = readerDelegateCode;
        EntityTypeName = entityTypeName;
        ResultTypeName = resultTypeName;
        Dialect = dialect;
        TableName = tableName;
        SchemaName = schemaName;
        QueryKind = queryKind;
        ProjectionInfo = projectionInfo;
        JoinedEntityTypeNames = joinedEntityTypeNames;
        JoinedTableInfos = joinedTableInfos;
        MaxParameterCount = sqlMap.Count > 0 ? sqlMap.Values.Max(v => v.ParameterCount) : 0;
    }

    /// <summary>
    /// Gets the chain analysis result.
    /// </summary>
    public ChainAnalysisResult Analysis { get; }

    /// <summary>
    /// Gets the pre-built SQL strings keyed by ClauseMask value.
    /// </summary>
    public Dictionary<ulong, PrebuiltSqlResult> SqlMap { get; }

    /// <summary>
    /// Gets the C# source for the static reader lambda (SELECT queries only).
    /// </summary>
    public string? ReaderDelegateCode { get; }

    /// <summary>
    /// Gets the fully qualified entity type name.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// Gets the fully qualified result type name (SELECT queries only).
    /// </summary>
    public string? ResultTypeName { get; }

    /// <summary>
    /// Gets the SQL dialect.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the database table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the optional schema name.
    /// </summary>
    public string? SchemaName { get; }

    /// <summary>
    /// Gets the kind of query (Select, Delete, Update).
    /// </summary>
    public QueryKind QueryKind { get; }

    /// <summary>
    /// Gets the projection info for reader delegate generation (SELECT queries only).
    /// </summary>
    public ProjectionInfo? ProjectionInfo { get; }

    /// <summary>
    /// Gets the joined entity type names for multi-entity join chains (null for single-entity chains).
    /// </summary>
    public IReadOnlyList<string>? JoinedEntityTypeNames { get; }

    /// <summary>
    /// Gets the joined table infos for multi-entity join chains (null for single-entity chains).
    /// </summary>
    public IReadOnlyList<(string TableName, string? SchemaName)>? JoinedTableInfos { get; }

    /// <summary>
    /// Returns true if this chain is a multi-entity join chain.
    /// </summary>
    public bool IsJoinChain => JoinedEntityTypeNames != null && JoinedEntityTypeNames.Count >= 2;

    /// <summary>
    /// Gets the maximum parameter count across all dispatch variants.
    /// Used by the first clause interceptor to pre-allocate the parameter array.
    /// </summary>
    public int MaxParameterCount { get; }

    public bool Equals(PrebuiltChainInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityTypeName == other.EntityTypeName
            && ResultTypeName == other.ResultTypeName
            && Dialect == other.Dialect
            && TableName == other.TableName
            && SchemaName == other.SchemaName
            && QueryKind == other.QueryKind
            && MaxParameterCount == other.MaxParameterCount
            && Analysis.Equals(other.Analysis)
            && EqualityHelpers.DictionaryEqual(SqlMap, other.SqlMap)
            && ReaderDelegateCode == other.ReaderDelegateCode
            && (ProjectionInfo is null ? other.ProjectionInfo is null : ProjectionInfo.Equals(other.ProjectionInfo))
            && EqualityHelpers.NullableSequenceEqual(JoinedEntityTypeNames, other.JoinedEntityTypeNames)
            && EqualityHelpers.TupleListEqual(JoinedTableInfos, other.JoinedTableInfos);
    }

    public override bool Equals(object? obj) => Equals(obj as PrebuiltChainInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(EntityTypeName, TableName, Dialect, QueryKind, SqlMap.Count);
    }
}
