using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Quarry.Internal;

/// <summary>
/// Internal immutable state container for query builder.
/// Each modification creates a new instance, enabling the immutable builder pattern.
/// </summary>
internal sealed partial class QueryState
{
    [GeneratedRegex(@"@p(\d+)")]
    private static partial Regex ParameterPlaceholderRegex();
    /// <summary>
    /// Gets the SQL dialect kind for this query.
    /// </summary>
    public SqlDialect Dialect { get; }

    /// <summary>
    /// Gets the table name for the FROM clause.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the schema name for qualified table reference.
    /// </summary>
    public string? SchemaName { get; }

    /// <summary>
    /// Gets the selected columns/expressions.
    /// </summary>
    public ImmutableArray<string> SelectColumns { get; }

    /// <summary>
    /// Gets whether a select has been specified.
    /// </summary>
    public bool HasSelect => !SelectColumns.IsDefaultOrEmpty;

    /// <summary>
    /// Gets the WHERE clause conditions.
    /// </summary>
    public ImmutableArray<string> WhereConditions { get; }

    /// <summary>
    /// Gets the ORDER BY clauses.
    /// </summary>
    public ImmutableArray<OrderByClause> OrderByClauses { get; }

    /// <summary>
    /// Gets the JOIN clauses.
    /// </summary>
    public ImmutableArray<JoinClause> JoinClauses { get; }

    /// <summary>
    /// Gets the GROUP BY columns.
    /// </summary>
    public ImmutableArray<string> GroupByColumns { get; }

    /// <summary>
    /// Gets the HAVING conditions.
    /// </summary>
    public ImmutableArray<string> HavingConditions { get; }

    /// <summary>
    /// Gets the OFFSET value.
    /// </summary>
    public int? Offset { get; }

    /// <summary>
    /// Gets the LIMIT value.
    /// </summary>
    public int? Limit { get; }

    /// <summary>
    /// Gets whether DISTINCT is applied.
    /// </summary>
    public bool IsDistinct { get; }

    /// <summary>
    /// Gets the parameters for this query.
    /// </summary>
    public ImmutableArray<QueryParameter> Parameters { get; }

    /// <summary>
    /// Gets the alias for the FROM table when joins are present (e.g., "t0").
    /// Null when no joins are present.
    /// </summary>
    public string? FromTableAlias { get; }

    /// <summary>
    /// Gets the query timeout override.
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Gets the query execution context.
    /// </summary>
    public IQueryExecutionContext? ExecutionContext { get; }

    /// <summary>
    /// Gets the clause bitmask for conditional clause tracking.
    /// Each bit corresponds to a conditionally-applied clause interceptor.
    /// Used by execution interceptors to select the correct pre-built SQL variant.
    /// </summary>
    public ulong ClauseMask { get; internal set; }

    /// <summary>
    /// Gets the parameter index for a parameterized OFFSET (pre-built SQL path).
    /// Null when offset is expressed as an integer via <see cref="Offset"/>.
    /// </summary>
    public int? OffsetParameterIndex { get; }

    /// <summary>
    /// Gets the parameter index for a parameterized LIMIT (pre-built SQL path).
    /// Null when limit is expressed as an integer via <see cref="Limit"/>.
    /// </summary>
    public int? LimitParameterIndex { get; }

    /// <summary>
    /// Gets the pre-quoted SELECT fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltSelectFragment { get; }

    /// <summary>
    /// Gets the pre-quoted FROM fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltFromFragment { get; }

    /// <summary>
    /// Gets the pre-quoted JOIN fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltJoinFragment { get; }

    /// <summary>
    /// Gets the pre-quoted ORDER BY fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltOrderByFragment { get; }

    /// <summary>
    /// Gets the pre-quoted GROUP BY fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltGroupByFragment { get; }

    /// <summary>
    /// Gets the pre-quoted HAVING fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltHavingFragment { get; }

    /// <summary>
    /// Gets the pre-quoted pagination fragment for tier 2 optimization.
    /// </summary>
    public string? PrebuiltPaginationFragment { get; }

    /// <summary>
    /// Creates a new query state with required initial values.
    /// </summary>
    public QueryState(SqlDialect dialectKind, string tableName, string? schemaName)
        : this(dialectKind, tableName, schemaName, null)
    {
    }

    /// <summary>
    /// Creates a new query state with required initial values and execution context.
    /// </summary>
    public QueryState(SqlDialect dialectKind, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        Dialect = dialectKind;
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        SchemaName = schemaName;
        SelectColumns = ImmutableArray<string>.Empty;
        WhereConditions = ImmutableArray<string>.Empty;
        OrderByClauses = ImmutableArray<OrderByClause>.Empty;
        JoinClauses = ImmutableArray<JoinClause>.Empty;
        GroupByColumns = ImmutableArray<string>.Empty;
        HavingConditions = ImmutableArray<string>.Empty;
        Offset = null;
        Limit = null;
        IsDistinct = false;
        Parameters = ImmutableArray<QueryParameter>.Empty;
        Timeout = null;
        ExecutionContext = executionContext;
    }

    /// <summary>
    /// Private constructor for immutable updates.
    /// </summary>
    private QueryState(
        SqlDialect dialectKind,
        string tableName,
        string? schemaName,
        ImmutableArray<string> selectColumns,
        ImmutableArray<string> whereConditions,
        ImmutableArray<OrderByClause> orderByClauses,
        ImmutableArray<JoinClause> joinClauses,
        ImmutableArray<string> groupByColumns,
        ImmutableArray<string> havingConditions,
        int? offset,
        int? limit,
        bool isDistinct,
        ImmutableArray<QueryParameter> parameters,
        TimeSpan? timeout,
        IQueryExecutionContext? executionContext,
        string? fromTableAlias,
        ulong clauseMask,
        int? offsetParameterIndex,
        int? limitParameterIndex,
        string? prebuiltSelectFragment,
        string? prebuiltFromFragment,
        string? prebuiltJoinFragment,
        string? prebuiltOrderByFragment,
        string? prebuiltGroupByFragment,
        string? prebuiltHavingFragment,
        string? prebuiltPaginationFragment)
    {
        Dialect = dialectKind;
        TableName = tableName;
        SchemaName = schemaName;
        SelectColumns = selectColumns;
        WhereConditions = whereConditions;
        OrderByClauses = orderByClauses;
        JoinClauses = joinClauses;
        GroupByColumns = groupByColumns;
        HavingConditions = havingConditions;
        Offset = offset;
        Limit = limit;
        IsDistinct = isDistinct;
        Parameters = parameters;
        Timeout = timeout;
        ExecutionContext = executionContext;
        FromTableAlias = fromTableAlias;
        ClauseMask = clauseMask;
        OffsetParameterIndex = offsetParameterIndex;
        LimitParameterIndex = limitParameterIndex;
        PrebuiltSelectFragment = prebuiltSelectFragment;
        PrebuiltFromFragment = prebuiltFromFragment;
        PrebuiltJoinFragment = prebuiltJoinFragment;
        PrebuiltOrderByFragment = prebuiltOrderByFragment;
        PrebuiltGroupByFragment = prebuiltGroupByFragment;
        PrebuiltHavingFragment = prebuiltHavingFragment;
        PrebuiltPaginationFragment = prebuiltPaginationFragment;
    }

    /// <summary>
    /// Creates a clone of this state with all fields preserved.
    /// Callers modify the returned state by passing different values to the private constructor.
    /// </summary>
    private QueryState Clone(
        ImmutableArray<string>? selectColumns = null,
        ImmutableArray<string>? whereConditions = null,
        ImmutableArray<OrderByClause>? orderByClauses = null,
        ImmutableArray<JoinClause>? joinClauses = null,
        ImmutableArray<string>? groupByColumns = null,
        ImmutableArray<string>? havingConditions = null,
        int? offset = null,
        bool clearOffset = false,
        int? limit = null,
        bool clearLimit = false,
        bool? isDistinct = null,
        ImmutableArray<QueryParameter>? parameters = null,
        TimeSpan? timeout = null,
        bool clearTimeout = false,
        string? fromTableAlias = null,
        bool clearFromTableAlias = false,
        ulong? clauseMask = null,
        int? offsetParameterIndex = null,
        bool clearOffsetParameterIndex = false,
        int? limitParameterIndex = null,
        bool clearLimitParameterIndex = false,
        string? prebuiltSelectFragment = null,
        string? prebuiltFromFragment = null,
        string? prebuiltJoinFragment = null,
        string? prebuiltOrderByFragment = null,
        string? prebuiltGroupByFragment = null,
        string? prebuiltHavingFragment = null,
        string? prebuiltPaginationFragment = null)
    {
        return new QueryState(
            Dialect, TableName, SchemaName,
            selectColumns ?? SelectColumns,
            whereConditions ?? WhereConditions,
            orderByClauses ?? OrderByClauses,
            joinClauses ?? JoinClauses,
            groupByColumns ?? GroupByColumns,
            havingConditions ?? HavingConditions,
            clearOffset ? null : (offset ?? Offset),
            clearLimit ? null : (limit ?? Limit),
            isDistinct ?? IsDistinct,
            parameters ?? Parameters,
            clearTimeout ? null : (timeout ?? Timeout),
            ExecutionContext,
            clearFromTableAlias ? null : (fromTableAlias ?? FromTableAlias),
            clauseMask ?? ClauseMask,
            clearOffsetParameterIndex ? null : (offsetParameterIndex ?? OffsetParameterIndex),
            clearLimitParameterIndex ? null : (limitParameterIndex ?? LimitParameterIndex),
            prebuiltSelectFragment ?? PrebuiltSelectFragment,
            prebuiltFromFragment ?? PrebuiltFromFragment,
            prebuiltJoinFragment ?? PrebuiltJoinFragment,
            prebuiltOrderByFragment ?? PrebuiltOrderByFragment,
            prebuiltGroupByFragment ?? PrebuiltGroupByFragment,
            prebuiltHavingFragment ?? PrebuiltHavingFragment,
            prebuiltPaginationFragment ?? PrebuiltPaginationFragment);
    }

    /// <summary>
    /// Creates a new state with the specified select columns.
    /// </summary>
    public QueryState WithSelect(ImmutableArray<string> columns)
        => Clone(selectColumns: columns);

    /// <summary>
    /// Creates a new state with an additional WHERE condition.
    /// </summary>
    public QueryState WithWhere(string condition)
        => Clone(whereConditions: WhereConditions.Add(condition));

    /// <summary>
    /// Creates a new state with an additional WHERE condition and its associated parameters.
    /// </summary>
    public QueryState WithWhereAndParameters(string condition, params object?[] parameterValues)
    {
        var newParameters = Parameters;
        var baseIndex = newParameters.Length;

        // Renumber @pN placeholders when prior parameters exist to avoid conflicts
        var adjustedCondition = condition;
        if (baseIndex > 0 && parameterValues.Length > 0)
        {
            adjustedCondition = ParameterPlaceholderRegex().Replace(condition, match =>
            {
                var idx = int.Parse(match.Groups[1].Value);
                return $"@p{baseIndex + idx}";
            });
        }

        foreach (var value in parameterValues)
        {
            var index = newParameters.Length;
            newParameters = newParameters.Add(new QueryParameter(index, value));
        }

        return Clone(
            whereConditions: WhereConditions.Add(adjustedCondition),
            parameters: newParameters);
    }

    /// <summary>
    /// Creates a new state with an additional ORDER BY clause.
    /// </summary>
    public QueryState WithOrderBy(string column, Direction direction)
        => Clone(orderByClauses: OrderByClauses.Add(new OrderByClause(column, direction)));

    /// <summary>
    /// Creates a new state with an additional JOIN clause.
    /// </summary>
    public QueryState WithJoin(JoinClause join)
    {
        // Auto-assign alias if not already set
        var aliasedJoin = join.TableAlias == null
            ? new JoinClause(join.Kind, join.JoinedTableName, join.JoinedSchemaName,
                $"t{JoinClauses.Length + 1}", join.OnConditionSql)
            : join;

        return Clone(
            joinClauses: JoinClauses.Add(aliasedJoin),
            fromTableAlias: FromTableAlias ?? "t0");
    }

    /// <summary>
    /// Creates a new state with an additional GROUP BY column.
    /// </summary>
    public QueryState WithGroupBy(string column)
        => Clone(groupByColumns: GroupByColumns.Add(column));

    /// <summary>
    /// Creates a new state with GROUP BY columns from an SQL fragment (may be comma-separated).
    /// </summary>
    public QueryState WithGroupByFragment(string columnFragment)
        => Clone(groupByColumns: GroupByColumns.Add(columnFragment));

    /// <summary>
    /// Creates a new state with an additional HAVING condition.
    /// </summary>
    public QueryState WithHaving(string condition)
        => Clone(havingConditions: HavingConditions.Add(condition));

    /// <summary>
    /// Creates a new state with the specified OFFSET.
    /// </summary>
    public QueryState WithOffset(int offset)
        => Clone(offset: offset);

    /// <summary>
    /// Creates a new state with the specified LIMIT.
    /// </summary>
    public QueryState WithLimit(int limit)
        => Clone(limit: limit);

    /// <summary>
    /// Creates a new state with DISTINCT applied.
    /// </summary>
    public QueryState WithDistinct()
        => Clone(isDistinct: true);

    /// <summary>
    /// Creates a new state with an additional parameter.
    /// </summary>
    public QueryState WithParameter(object? value)
    {
        var index = Parameters.Length;
        return Clone(parameters: Parameters.Add(new QueryParameter(index, value)));
    }

    /// <summary>
    /// Creates a new state with the specified timeout.
    /// </summary>
    public QueryState WithTimeout(TimeSpan timeout)
        => Clone(timeout: timeout);

    /// <summary>
    /// Creates a new state with the specified clause bit set on the ClauseMask.
    /// </summary>
    public QueryState WithClauseBit(int bit)
        => Clone(clauseMask: ClauseMask | (1UL << bit));

    /// <summary>
    /// Creates a new state with offset stored as a parameter for pre-built SQL.
    /// The value is added as a QueryParameter and the index is tracked for @pN reference.
    /// </summary>
    public QueryState WithOffsetParameter(object? value)
    {
        var index = Parameters.Length;
        return Clone(
            parameters: Parameters.Add(new QueryParameter(index, value)),
            offsetParameterIndex: index);
    }

    /// <summary>
    /// Creates a new state with limit stored as a parameter for pre-built SQL.
    /// The value is added as a QueryParameter and the index is tracked for @pN reference.
    /// </summary>
    public QueryState WithLimitParameter(object? value)
    {
        var index = Parameters.Length;
        return Clone(
            parameters: Parameters.Add(new QueryParameter(index, value)),
            limitParameterIndex: index);
    }

    /// <summary>
    /// Creates a new state with the specified pre-quoted SELECT fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltSelectFragment(string fragment)
        => Clone(prebuiltSelectFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted FROM fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltFromFragment(string fragment)
        => Clone(prebuiltFromFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted JOIN fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltJoinFragment(string fragment)
        => Clone(prebuiltJoinFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted ORDER BY fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltOrderByFragment(string fragment)
        => Clone(prebuiltOrderByFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted GROUP BY fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltGroupByFragment(string fragment)
        => Clone(prebuiltGroupByFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted HAVING fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltHavingFragment(string fragment)
        => Clone(prebuiltHavingFragment: fragment);

    /// <summary>
    /// Creates a new state with the specified pre-quoted pagination fragment (tier 2).
    /// </summary>
    public QueryState WithPrebuiltPaginationFragment(string fragment)
        => Clone(prebuiltPaginationFragment: fragment);

    /// <summary>
    /// Gets the next parameter index.
    /// </summary>
    public int NextParameterIndex => Parameters.Length;

    /// <summary>
    /// Mutates the ClauseMask in place by setting the specified bit.
    /// Used by prebuilt chain interceptors where the builder is reused (single-owner linear flow).
    /// </summary>
    internal void SetClauseBitMutable(int bit)
    {
        ClauseMask |= (1UL << bit);
    }

    /// <summary>
    /// Creates a new state with parameters populated from a pre-allocated array.
    /// Used at terminal time to hydrate the immutable state for execution.
    /// </summary>
    public QueryState WithPrebuiltParams(object?[] prebuiltParams, int paramCount)
    {
        var builder = ImmutableArray.CreateBuilder<QueryParameter>(paramCount);
        for (int i = 0; i < paramCount; i++)
            builder.Add(new QueryParameter(i, prebuiltParams[i]));
        return Clone(parameters: builder.MoveToImmutable());
    }
}

/// <summary>
/// Represents an ORDER BY clause.
/// </summary>
internal readonly struct OrderByClause
{
    /// <summary>
    /// Gets the column or expression to order by.
    /// </summary>
    public string Column { get; }

    /// <summary>
    /// Gets the sort direction.
    /// </summary>
    public Direction Direction { get; }

    public OrderByClause(string column, Direction direction)
    {
        Column = column;
        Direction = direction;
    }
}

/// <summary>
/// Represents a query parameter.
/// </summary>
internal readonly struct QueryParameter
{
    /// <summary>
    /// Gets the parameter index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the parameter value.
    /// </summary>
    public object? Value { get; }

    public QueryParameter(int index, object? value)
    {
        Index = index;
        Value = value;
    }
}

/// <summary>
/// Represents a JOIN clause.
/// </summary>
internal readonly struct JoinClause
{
    /// <summary>
    /// Gets the kind of join (Inner, Left, Right).
    /// </summary>
    public JoinKind Kind { get; }

    /// <summary>
    /// Gets the joined table name.
    /// </summary>
    public string JoinedTableName { get; }

    /// <summary>
    /// Gets the joined table's schema name.
    /// </summary>
    public string? JoinedSchemaName { get; }

    /// <summary>
    /// Gets the table alias for the joined table.
    /// </summary>
    public string? TableAlias { get; }

    /// <summary>
    /// Gets the ON condition SQL fragment.
    /// </summary>
    public string OnConditionSql { get; }

    public JoinClause(
        JoinKind kind,
        string joinedTableName,
        string? joinedSchemaName,
        string? tableAlias,
        string onConditionSql)
    {
        Kind = kind;
        JoinedTableName = joinedTableName;
        JoinedSchemaName = joinedSchemaName;
        TableAlias = tableAlias;
        OnConditionSql = onConditionSql;
    }
}

/// <summary>
/// The kind of join operation.
/// </summary>
public enum JoinKind
{
    /// <summary>
    /// INNER JOIN - returns only matching rows from both tables.
    /// </summary>
    Inner,

    /// <summary>
    /// LEFT JOIN - returns all rows from the left table with matching rows from the right table.
    /// </summary>
    Left,

    /// <summary>
    /// RIGHT JOIN - returns all rows from the right table with matching rows from the left table.
    /// </summary>
    Right
}
