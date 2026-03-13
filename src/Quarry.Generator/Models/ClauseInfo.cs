using System.Collections.Generic;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents the analyzed result of a clause expression (Where, OrderBy, GroupBy, Having, Set).
/// Contains the SQL fragment and parameter information for code generation.
/// </summary>
internal class ClauseInfo
{
    public ClauseInfo(
        ClauseKind kind,
        string sqlFragment,
        IReadOnlyList<ParameterInfo> parameters,
        bool isSuccess = true,
        string? errorMessage = null)
    {
        Kind = kind;
        SqlFragment = sqlFragment;
        Parameters = parameters;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the kind of clause (Where, OrderBy, GroupBy, Having, Set).
    /// </summary>
    public ClauseKind Kind { get; }

    /// <summary>
    /// Gets the generated SQL fragment.
    /// </summary>
    public string SqlFragment { get; }

    /// <summary>
    /// Gets the parameters extracted from the expression.
    /// </summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    /// <summary>
    /// Gets whether the clause was successfully analyzed.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the error message if analysis failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful clause info.
    /// </summary>
    public static ClauseInfo Success(ClauseKind kind, string sqlFragment, IReadOnlyList<ParameterInfo> parameters)
    {
        return new ClauseInfo(kind, sqlFragment, parameters, isSuccess: true);
    }

    /// <summary>
    /// Creates a failed clause info.
    /// </summary>
    public static ClauseInfo Failure(ClauseKind kind, string error)
    {
        return new ClauseInfo(kind, string.Empty, System.Array.Empty<ParameterInfo>(), isSuccess: false, errorMessage: error);
    }
}

/// <summary>
/// Represents information about an OrderBy clause, including column and direction.
/// </summary>
internal sealed class OrderByClauseInfo : ClauseInfo
{
    public OrderByClauseInfo(
        string columnSql,
        bool isDescending,
        IReadOnlyList<ParameterInfo> parameters)
        : base(ClauseKind.OrderBy, columnSql, parameters)
    {
        ColumnSql = columnSql;
        IsDescending = isDescending;
    }

    /// <summary>
    /// Gets the SQL for the column being ordered by.
    /// </summary>
    public string ColumnSql { get; }

    /// <summary>
    /// Gets whether the order is descending.
    /// </summary>
    public bool IsDescending { get; }
}

/// <summary>
/// Represents information about a Set clause for Update operations.
/// </summary>
internal sealed class SetClauseInfo : ClauseInfo
{
    public SetClauseInfo(
        string columnSql,
        int parameterIndex,
        IReadOnlyList<ParameterInfo> parameters,
        string? customTypeMappingClass = null)
        : base(ClauseKind.Set, $"{columnSql} = @p{parameterIndex}", parameters)
    {
        ColumnSql = columnSql;
        ParameterIndex = parameterIndex;
        CustomTypeMappingClass = customTypeMappingClass;
    }

    /// <summary>
    /// Gets the SQL for the column being set.
    /// </summary>
    public string ColumnSql { get; }

    /// <summary>
    /// Gets the parameter index for the value.
    /// </summary>
    public int ParameterIndex { get; }

    /// <summary>
    /// Gets the custom type mapping class for the column, if any.
    /// When set, the value should be wrapped with ToDb() before binding.
    /// </summary>
    public string? CustomTypeMappingClass { get; }
}

/// <summary>
/// Represents information about a Join clause.
/// </summary>
internal sealed class JoinClauseInfo : ClauseInfo
{
    public JoinClauseInfo(
        JoinClauseKind joinKind,
        string joinedEntityName,
        string joinedTableName,
        string onConditionSql,
        IReadOnlyList<ParameterInfo> parameters,
        string? joinedSchemaName = null,
        string? tableAlias = null)
        : base(ClauseKind.Join, onConditionSql, parameters)
    {
        JoinKind = joinKind;
        JoinedEntityName = joinedEntityName;
        JoinedTableName = joinedTableName;
        OnConditionSql = onConditionSql;
        JoinedSchemaName = joinedSchemaName;
        TableAlias = tableAlias;
    }

    /// <summary>
    /// Gets the kind of join (Inner, Left, Right).
    /// </summary>
    public JoinClauseKind JoinKind { get; }

    /// <summary>
    /// Gets the joined entity name.
    /// </summary>
    public string JoinedEntityName { get; }

    /// <summary>
    /// Gets the database table name for the joined entity.
    /// </summary>
    public string JoinedTableName { get; }

    /// <summary>
    /// Gets the ON condition SQL.
    /// </summary>
    public string OnConditionSql { get; }

    /// <summary>
    /// Gets the schema name for the joined table, or null if unqualified.
    /// </summary>
    public string? JoinedSchemaName { get; }

    /// <summary>
    /// Gets the alias for the joined table (e.g., "t1"), or null if no alias.
    /// </summary>
    public string? TableAlias { get; }
}

/// <summary>
/// The kind of join operation for clause translation.
/// </summary>
internal enum JoinClauseKind
{
    Inner,
    Left,
    Right
}

/// <summary>
/// Specifies the kind of clause.
/// </summary>
internal enum ClauseKind
{
    /// <summary>
    /// WHERE clause for filtering.
    /// </summary>
    Where,

    /// <summary>
    /// ORDER BY clause for sorting.
    /// </summary>
    OrderBy,

    /// <summary>
    /// GROUP BY clause for grouping.
    /// </summary>
    GroupBy,

    /// <summary>
    /// HAVING clause for filtering groups.
    /// </summary>
    Having,

    /// <summary>
    /// SET clause for Update operations.
    /// </summary>
    Set,

    /// <summary>
    /// JOIN clause for joining tables.
    /// </summary>
    Join
}
