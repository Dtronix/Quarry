using System.Collections.Generic;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents the analyzed execution context for an Execute*Async() or ToAsyncEnumerable() call.
/// Contains all information needed to assemble complete SQL and wire up the reader.
/// </summary>
internal sealed class ExecutionInfo
{
    public ExecutionInfo(
        ExecutionKind kind,
        string entityTypeName,
        string resultTypeName,
        bool isOptimalPath,
        string? selectSql,
        IReadOnlyList<string> whereClauses,
        IReadOnlyList<OrderByClause> orderByClauses,
        string? groupBySql,
        string? havingSql,
        IReadOnlyList<JoinClause> joinClauses,
        int? offset,
        int? limit,
        bool isDistinct,
        IReadOnlyList<ParameterInfo> parameters,
        ProjectionInfo? projectionInfo,
        string? nonOptimalReason = null)
    {
        Kind = kind;
        EntityTypeName = entityTypeName;
        ResultTypeName = resultTypeName;
        IsOptimalPath = isOptimalPath;
        SelectSql = selectSql;
        WhereClauses = whereClauses;
        OrderByClauses = orderByClauses;
        GroupBySql = groupBySql;
        HavingSql = havingSql;
        JoinClauses = joinClauses;
        Offset = offset;
        Limit = limit;
        IsDistinct = isDistinct;
        Parameters = parameters;
        ProjectionInfo = projectionInfo;
        NonOptimalReason = nonOptimalReason;
    }

    /// <summary>
    /// Gets the kind of execution (FetchAll, FetchFirst, etc.).
    /// </summary>
    public ExecutionKind Kind { get; }

    /// <summary>
    /// Gets the entity type name for the query.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// Gets the result type name for the projection.
    /// </summary>
    public string ResultTypeName { get; }

    /// <summary>
    /// Gets whether this execution can use the optimal path with compile-time reader.
    /// </summary>
    public bool IsOptimalPath { get; }

    /// <summary>
    /// Gets the SELECT clause SQL, or null if not determined.
    /// </summary>
    public string? SelectSql { get; }

    /// <summary>
    /// Gets the WHERE clause SQL fragments to be ANDed together.
    /// </summary>
    public IReadOnlyList<string> WhereClauses { get; }

    /// <summary>
    /// Gets the ORDER BY clause information.
    /// </summary>
    public IReadOnlyList<OrderByClause> OrderByClauses { get; }

    /// <summary>
    /// Gets the GROUP BY clause SQL, or null if none.
    /// </summary>
    public string? GroupBySql { get; }

    /// <summary>
    /// Gets the HAVING clause SQL, or null if none.
    /// </summary>
    public string? HavingSql { get; }

    /// <summary>
    /// Gets the JOIN clause information.
    /// </summary>
    public IReadOnlyList<JoinClause> JoinClauses { get; }

    /// <summary>
    /// Gets the OFFSET value, or null if none.
    /// </summary>
    public int? Offset { get; }

    /// <summary>
    /// Gets the LIMIT value, or null if none.
    /// </summary>
    public int? Limit { get; }

    /// <summary>
    /// Gets whether DISTINCT is specified.
    /// </summary>
    public bool IsDistinct { get; }

    /// <summary>
    /// Gets the parameters for the query.
    /// </summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    /// <summary>
    /// Gets the projection info for reader generation.
    /// </summary>
    public ProjectionInfo? ProjectionInfo { get; }

    /// <summary>
    /// Gets the reason why this execution is not optimal, if applicable.
    /// </summary>
    public string? NonOptimalReason { get; }

}

/// <summary>
/// Represents an ORDER BY clause element.
/// </summary>
internal sealed class OrderByClause
{
    public OrderByClause(string columnSql, bool isDescending)
    {
        ColumnSql = columnSql;
        IsDescending = isDescending;
    }

    /// <summary>
    /// Gets the column SQL expression.
    /// </summary>
    public string ColumnSql { get; }

    /// <summary>
    /// Gets whether the order is descending.
    /// </summary>
    public bool IsDescending { get; }
}

/// <summary>
/// Represents a JOIN clause element.
/// </summary>
internal sealed class JoinClause
{
    public JoinClause(
        JoinKind kind,
        string joinedEntityName,
        string joinedTableName,
        string onConditionSql)
    {
        Kind = kind;
        JoinedEntityName = joinedEntityName;
        JoinedTableName = joinedTableName;
        OnConditionSql = onConditionSql;
    }

    /// <summary>
    /// Gets the kind of join (Inner, Left, Right).
    /// </summary>
    public JoinKind Kind { get; }

    /// <summary>
    /// Gets the joined entity name.
    /// </summary>
    public string JoinedEntityName { get; }

    /// <summary>
    /// Gets the joined table name.
    /// </summary>
    public string JoinedTableName { get; }

    /// <summary>
    /// Gets the ON condition SQL.
    /// </summary>
    public string OnConditionSql { get; }
}

/// <summary>
/// The kind of join operation.
/// </summary>
internal enum JoinKind
{
    Inner,
    Left,
    Right
}

/// <summary>
/// The kind of execution method.
/// </summary>
internal enum ExecutionKind
{
    /// <summary>
    /// ExecuteFetchAllAsync() - returns List&lt;T&gt;
    /// </summary>
    FetchAll,

    /// <summary>
    /// ExecuteFetchFirstAsync() - returns T, throws if empty
    /// </summary>
    FetchFirst,

    /// <summary>
    /// ExecuteFetchFirstOrDefaultAsync() - returns T?
    /// </summary>
    FetchFirstOrDefault,

    /// <summary>
    /// ExecuteFetchSingleAsync() - returns T, throws if empty or multiple
    /// </summary>
    FetchSingle,

    /// <summary>
    /// ExecuteScalarAsync&lt;T&gt;() - returns T
    /// </summary>
    Scalar,

    /// <summary>
    /// ExecuteNonQueryAsync() - returns int
    /// </summary>
    NonQuery,

    /// <summary>
    /// ToAsyncEnumerable() - returns IAsyncEnumerable&lt;T&gt;
    /// </summary>
    AsyncEnumerable
}
