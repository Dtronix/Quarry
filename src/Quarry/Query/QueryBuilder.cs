using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Quarry.Internal;

namespace Quarry;

/// <summary>
/// Builder for constructing SELECT queries.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
/// <remarks>
/// <para>
/// QueryBuilder uses an immutable builder pattern - each method returns a new instance,
/// leaving the original unchanged. This enables safe query composition and reuse.
/// </para>
/// <para>
/// Example:
/// <code>
/// var baseQuery = db.Users().Where(u => u.IsActive);
/// var ordered = baseQuery.OrderBy(u => u.UserName, Direction.Ascending);
/// var paged = ordered.Offset(10).Limit(20);
/// </code>
/// </para>
/// </remarks>
public sealed class QueryBuilder<T> : IQueryBuilder<T> where T : class
{
    private readonly QueryState _state;

    /// <summary>
    /// Creates a new QueryBuilder with the specified dialect and table information.
    /// </summary>
    /// <param name="dialect">The SQL dialect for query generation.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="schemaName">The optional schema name.</param>
    internal QueryBuilder(SqlDialect dialect, string tableName, string? schemaName)
        : this(dialect, tableName, schemaName, null)
    {
    }

    /// <summary>
    /// Creates a new QueryBuilder with the specified dialect, table information, and execution context.
    /// </summary>
    /// <param name="dialect">The SQL dialect for query generation.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="schemaName">The optional schema name.</param>
    /// <param name="executionContext">The execution context for query execution.</param>
    internal QueryBuilder(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
    {
        _state = new QueryState(dialect, tableName, schemaName, executionContext);
    }

    /// <summary>
    /// Creates a new QueryBuilder and returns it as an IQueryBuilder.
    /// Ensures the concrete type never leaks to caller scope.
    /// </summary>
    public static IQueryBuilder<T> Create(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? executionContext)
        => new QueryBuilder<T>(dialect, tableName, schemaName, executionContext);

    /// <summary>
    /// Creates a new QueryBuilder with the given state.
    /// </summary>
    private QueryBuilder(QueryState state)
    {
        _state = state;
    }

    /// <summary>
    /// Specifies the columns or projection to select.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="selector">The selection expression.</param>
    /// <returns>A new query builder with the projection applied.</returns>
    /// <remarks>
    /// <para>Select() is required before execution. Supports:</para>
    /// <list type="bullet">
    /// <item>Entity projection: <c>Select(u => u)</c></item>
    /// <item>DTOs: <c>Select(u => new UserDto { Id = u.Id })</c></item>
    /// <item>Tuples: <c>Select(u => (u.Id, u.Name))</c></item>
    /// </list>
    /// Note: Anonymous types are not supported.
    /// </remarks>
    public IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        // The actual column extraction will be done by the source generator via interceptors
        // At runtime, this returns a builder that can be used for additional operations
        // The interceptor will replace this call with a version that has the column list pre-computed
        return new QueryBuilder<T, TResult>(_state, selector);
    }

    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>A new query builder with the WHERE clause applied.</returns>
    /// <remarks>
    /// Multiple Where() calls are combined with AND.
    /// The predicate expression is translated to SQL at compile-time by the source generator.
    /// </remarks>
    public IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        // At runtime, this is a no-op placeholder
        // The actual SQL generation happens via interceptors at compile-time
        // For fallback path, we'll need to evaluate the expression at runtime
        return new QueryBuilder<T>(_state);
    }

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The expression selecting the sort key.</param>
    /// <param name="direction">The sort direction.</param>
    /// <returns>A new query builder with the ORDER BY clause applied.</returns>
    public IQueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // Placeholder for runtime - interceptor will provide actual implementation
        return new QueryBuilder<T>(_state);
    }

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The expression selecting the sort key.</param>
    /// <param name="direction">The sort direction.</param>
    /// <returns>A new query builder with the ORDER BY clause applied.</returns>
    public IQueryBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // Placeholder for runtime - interceptor will provide actual implementation
        return new QueryBuilder<T>(_state);
    }

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>A new query builder with the OFFSET applied.</returns>
    public IQueryBuilder<T> Offset(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new QueryBuilder<T>(_state.WithOffset(count));
    }

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    /// <param name="count">The maximum number of rows to return.</param>
    /// <returns>A new query builder with the LIMIT applied.</returns>
    public IQueryBuilder<T> Limit(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new QueryBuilder<T>(_state.WithLimit(count));
    }

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    /// <returns>A new query builder with DISTINCT applied.</returns>
    public IQueryBuilder<T> Distinct()
    {
        return new QueryBuilder<T>(_state.WithDistinct());
    }

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A new query builder with the timeout applied.</returns>
    public IQueryBuilder<T> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        return new QueryBuilder<T>(_state.WithTimeout(timeout));
    }

    #region GroupBy and Having Methods

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <param name="keySelector">The expression selecting the grouping key.</param>
    /// <returns>A new query builder with the GROUP BY clause applied.</returns>
    /// <remarks>
    /// <para>Example:</para>
    /// <code>
    /// var ordersByUser = await db.Orders()
    ///     .GroupBy(o => o.UserId)
    ///     .Select(o => new { o.UserId, Total = Sql.Sum(o.Total) })
    ///     .ExecuteFetchAllAsync();
    /// </code>
    /// </remarks>
    public IQueryBuilder<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // At runtime, this is a placeholder - interceptor provides actual implementation
        return new QueryBuilder<T>(_state);
    }

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate (typically contains aggregate functions).</param>
    /// <returns>A new query builder with the HAVING clause applied.</returns>
    /// <remarks>
    /// <para>Having() is used after GroupBy() to filter groups based on aggregate conditions.</para>
    /// <para>Example:</para>
    /// <code>
    /// var bigOrders = await db.Orders()
    ///     .GroupBy(o => o.UserId)
    ///     .Having(o => Sql.Count() > 5)
    ///     .Select(o => new { o.UserId, OrderCount = Sql.Count() })
    ///     .ExecuteFetchAllAsync();
    /// </code>
    /// </remarks>
    public IQueryBuilder<T> Having(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        // At runtime, this is a placeholder - interceptor provides actual implementation
        return new QueryBuilder<T>(_state);
    }

    #endregion

    #region Generated Code Methods for GroupBy/Having

    /// <summary>
    /// Adds a GROUP BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<T> AddGroupByClause(string groupBySql)
    {
        return new QueryBuilder<T>(_state.WithGroupByFragment(groupBySql));
    }

    /// <summary>
    /// Adds a HAVING clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<T> AddHavingClause(string havingSql)
    {
        return new QueryBuilder<T>(_state.WithHaving(havingSql));
    }

    /// <summary>
    /// Adds a WHERE clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<T> AddWhereClause(string whereSql)
    {
        return new QueryBuilder<T>(_state.WithWhere(whereSql));
    }

    /// <summary>
    /// Adds a WHERE clause with pre-analyzed SQL and captured parameter values.
    /// Used by generated interceptors when the WHERE clause contains captured variables.
    /// </summary>
    public QueryBuilder<T> AddWhereClause(string whereSql, params object?[] parameters)
    {
        return new QueryBuilder<T>(_state.WithWhereAndParameters(whereSql, parameters));
    }

    /// <summary>
    /// Adds an ORDER BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<T> AddOrderByClause(string columnSql, Direction direction)
    {
        return new QueryBuilder<T>(_state.WithOrderBy(columnSql, direction));
    }

    /// <summary>
    /// Adds a THEN BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<T> AddThenByClause(string columnSql, Direction direction)
    {
        return new QueryBuilder<T>(_state.WithOrderBy(columnSql, direction));
    }

    /// <summary>
    /// Returns a new builder with the specified clause bit set on the state.
    /// Used by generated clause interceptors for conditional clause tracking.
    /// </summary>
    public QueryBuilder<T> WithClauseBit(int bit)
    {
        return new QueryBuilder<T>(_state.WithClauseBit(bit));
    }

    #endregion

    #region Join Methods

    /// <summary>
    /// Adds an INNER JOIN with another table using an explicit condition.
    /// </summary>
    /// <typeparam name="TJoined">The entity type to join.</typeparam>
    /// <param name="condition">The join condition expression.</param>
    /// <returns>A new joined query builder.</returns>
    /// <remarks>
    /// <para>Example:</para>
    /// <code>
    /// var results = await db.Users()
    ///     .Join&lt;Order&gt;((u, o) => u.UserId == o.UserId.Id)
    ///     .Select((u, o) => new { u.UserName, o.Total })
    ///     .ExecuteFetchAllAsync();
    /// </code>
    /// </remarks>
    public IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        where TJoined : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        // At runtime, this is a placeholder. The interceptor will generate proper join SQL.
        return new JoinedQueryBuilder<T, TJoined>(_state);
    }

    /// <summary>
    /// Adds a LEFT JOIN with another table using an explicit condition.
    /// </summary>
    /// <typeparam name="TJoined">The entity type to join.</typeparam>
    /// <param name="condition">The join condition expression.</param>
    /// <returns>A new joined query builder.</returns>
    /// <remarks>
    /// LEFT JOIN returns all rows from the left table (this entity) with matching rows from the right table.
    /// Columns from the right table will be null when there's no match.
    /// </remarks>
    public IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        where TJoined : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        // At runtime, this is a placeholder. The interceptor will generate proper join SQL.
        return new JoinedQueryBuilder<T, TJoined>(_state);
    }

    /// <summary>
    /// Adds a RIGHT JOIN with another table using an explicit condition.
    /// </summary>
    /// <typeparam name="TJoined">The entity type to join.</typeparam>
    /// <param name="condition">The join condition expression.</param>
    /// <returns>A new joined query builder.</returns>
    /// <remarks>
    /// RIGHT JOIN returns all rows from the right table (joined entity) with matching rows from the left table.
    /// Columns from the left table will be null when there's no match.
    /// </remarks>
    public IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        where TJoined : class
    {
        ArgumentNullException.ThrowIfNull(condition);
        // At runtime, this is a placeholder. The interceptor will generate proper join SQL.
        return new JoinedQueryBuilder<T, TJoined>(_state);
    }

    /// <summary>
    /// Adds an INNER JOIN via a navigation property relationship.
    /// </summary>
    /// <typeparam name="TJoined">The related entity type.</typeparam>
    /// <param name="navigation">Expression selecting the navigation property.</param>
    /// <returns>A new joined query builder.</returns>
    /// <remarks>
    /// <para>Uses the relationship defined in the schema to generate the ON condition automatically.</para>
    /// <para>Example:</para>
    /// <code>
    /// var results = await db.Users()
    ///     .Join(u => u.Orders)
    ///     .Select((u, o) => new { u.UserName, o.Total })
    ///     .ExecuteFetchAllAsync();
    /// </code>
    /// </remarks>
    public IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        where TJoined : class
    {
        ArgumentNullException.ThrowIfNull(navigation);
        // At runtime, this is a placeholder. The interceptor will generate proper join SQL from navigation.
        return new JoinedQueryBuilder<T, TJoined>(_state);
    }

    /// <summary>
    /// Adds a LEFT JOIN via a navigation property relationship.
    /// </summary>
    /// <typeparam name="TJoined">The related entity type.</typeparam>
    /// <param name="navigation">Expression selecting the navigation property.</param>
    /// <returns>A new joined query builder.</returns>
    public IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation)
        where TJoined : class
    {
        ArgumentNullException.ThrowIfNull(navigation);
        // At runtime, this is a placeholder. The interceptor will generate proper join SQL from navigation.
        return new JoinedQueryBuilder<T, TJoined>(_state);
    }

    #endregion

    #region Generated Code Methods

    /// <summary>
    /// Adds a join clause with pre-analyzed ON condition SQL.
    /// Used by generated interceptors.
    /// </summary>
    /// <typeparam name="TJoined">The joined entity type.</typeparam>
    /// <param name="kind">The join kind.</param>
    /// <param name="onConditionSql">The ON condition SQL fragment.</param>
    /// <returns>A new joined query builder.</returns>
    public JoinedQueryBuilder<T, TJoined> AddJoinClause<TJoined>(JoinKind kind, string tableName, string onConditionSql)
        where TJoined : class
    {
        var joinClause = new JoinClause(kind, tableName, null, null, onConditionSql);
        var newState = _state.WithJoin(joinClause);
        return new JoinedQueryBuilder<T, TJoined>(newState);
    }

    #endregion

    /// <summary>
    /// Returns the generated SQL without executing the query.
    /// </summary>
    /// <returns>The SQL that would be executed.</returns>
    /// <remarks>
    /// This is useful for debugging and logging. Parameter placeholders are included.
    /// </remarks>
    public string ToSql()
    {
        return SqlBuilder.BuildSelectSql(_state);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        ToSql(),
        DiagnosticsHelper.ConvertParameters(State.Parameters),
        DiagnosticQueryKind.Select,
        State.Dialect,
        State.TableName,
        rawState: State);

    /// <summary>
    /// Gets the internal state for use by generated code.
    /// </summary>
    internal QueryState State => _state;

    #region Prebuilt Parameter Binding (Generated Code API)

    /// <summary>
    /// Pre-allocated parameter storage for prebuilt chains.
    /// Written by clause interceptors via <see cref="BindParam"/>, read by terminal interceptor.
    /// </summary>
    internal object?[]? PrebuiltParams;

    /// <summary>
    /// Next write index into <see cref="PrebuiltParams"/>.
    /// </summary>
    internal int PrebuiltParamIndex;

    /// <summary>
    /// Allocates the pre-built parameter array. Called by the first clause interceptor
    /// in a prebuilt chain. Size is a compile-time constant.
    /// </summary>
    public void AllocatePrebuiltParams(int count)
    {
        Debug.Assert(count >= 0, "Prebuilt parameter count must be non-negative.");
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    /// <summary>
    /// Writes a captured parameter value to the next slot and returns this builder (mutate-in-place).
    /// Safe because the generator guarantees single-owner linear flow in prebuilt chains.
    /// </summary>
    public QueryBuilder<T> BindParam(object? value)
    {
        Debug.Assert(PrebuiltParams != null, "AllocatePrebuiltParams must be called before BindParam.");
        Debug.Assert(PrebuiltParamIndex < PrebuiltParams!.Length, $"BindParam called more times ({PrebuiltParamIndex + 1}) than allocated ({PrebuiltParams.Length}).");
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    /// <summary>
    /// Sets a clause bit on the state's ClauseMask in place. Used by prebuilt chain interceptors
    /// for conditional clause tracking without allocating a new builder.
    /// </summary>
    public QueryBuilder<T> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    /// <summary>
    /// Creates a projected builder for the prebuilt path, transferring the PrebuiltParams array.
    /// No selector or reader — the terminal interceptor provides the reader.
    /// </summary>
    public QueryBuilder<T, TResult> AsProjected<TResult>()
    {
        var projected = new QueryBuilder<T, TResult>(_state);
        projected.PrebuiltParams = PrebuiltParams;
        projected.PrebuiltParamIndex = PrebuiltParamIndex;
        return projected;
    }

    /// <summary>
    /// Creates a joined builder for the prebuilt path, transferring the PrebuiltParams array.
    /// Performs only a type conversion without modifying state (no JoinClause/alias mutation).
    /// </summary>
    public JoinedQueryBuilder<T, TJoined> AsJoined<TJoined>() where TJoined : class
    {
        var joined = new JoinedQueryBuilder<T, TJoined>(_state);
        joined.PrebuiltParams = PrebuiltParams;
        joined.PrebuiltParamIndex = PrebuiltParamIndex;
        return joined;
    }

    #endregion

    #region Generated Code Methods for Select

    /// <summary>
    /// Creates a query builder with pre-computed column names and a typed reader delegate.
    /// Used by generated interceptors for optimal path execution.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="columns">The column names to select.</param>
    /// <param name="reader">The reader delegate that maps DbDataReader rows to TResult.</param>
    /// <returns>A new query builder with the projection applied.</returns>
    public QueryBuilder<T, TResult> SelectWithReader<TResult>(
        string[] columns,
        Func<DbDataReader, TResult> reader)
    {
        // Add columns to state
        var newState = _state.WithSelect(columns.ToImmutableArray());
        return new QueryBuilder<T, TResult>(newState, reader);
    }

    #endregion
}

/// <summary>
/// Builder for constructing SELECT queries with a specified projection.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <typeparam name="TResult">The result type after projection.</typeparam>
/// <remarks>
/// This builder type is returned after calling Select() and allows additional
/// query operations while maintaining type safety for the projection.
/// </remarks>
public sealed class QueryBuilder<TEntity, TResult> : IQueryBuilder<TEntity, TResult> where TEntity : class
{
    private readonly QueryState _state;
    private readonly Func<TEntity, TResult>? _selector;
    private readonly Func<DbDataReader, TResult>? _reader;

    /// <summary>
    /// Creates a new QueryBuilder with the given state and selector.
    /// </summary>
    internal QueryBuilder(QueryState state, Func<TEntity, TResult> selector)
    {
        _state = state;
        _selector = selector;
        _reader = null;
    }

    /// <summary>
    /// Creates a new QueryBuilder with the given state, selector, and reader.
    /// </summary>
    internal QueryBuilder(QueryState state, Func<TEntity, TResult> selector, Func<DbDataReader, TResult>? reader)
    {
        _state = state;
        _selector = selector;
        _reader = reader;
    }

    /// <summary>
    /// Creates a new QueryBuilder with the given state and reader (used by generated interceptors).
    /// The selector is set to null since the interceptor has already analyzed the projection.
    /// </summary>
    internal QueryBuilder(QueryState state, Func<DbDataReader, TResult> reader)
    {
        _state = state;
        _selector = null!; // Selector not needed when we have a pre-computed reader
        _reader = reader;
    }

    /// <summary>
    /// Creates a new QueryBuilder for the prebuilt parameter path (no selector, no reader yet).
    /// The terminal interceptor provides the reader at execution time.
    /// </summary>
    internal QueryBuilder(QueryState state)
    {
        _state = state;
        _selector = null!;
        _reader = null;
    }

    /// <summary>
    /// Creates a new QueryBuilder with the given state and selector (for immutable operations).
    /// </summary>
    private QueryBuilder(QueryState state, Func<TEntity, TResult>? selector, Func<DbDataReader, TResult>? reader, bool _)
    {
        _state = state;
        _selector = selector;
        _reader = reader;
    }

    /// <summary>
    /// Adds a WHERE clause to filter rows.
    /// </summary>
    /// <param name="predicate">The filter predicate.</param>
    /// <returns>A new query builder with the WHERE clause applied.</returns>
    public IQueryBuilder<TEntity, TResult> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        // Placeholder - interceptor provides actual implementation
        return new QueryBuilder<TEntity, TResult>(_state, _selector, _reader, true);
    }

    /// <summary>
    /// Specifies the primary sort order.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The expression selecting the sort key.</param>
    /// <param name="direction">The sort direction.</param>
    /// <returns>A new query builder with the ORDER BY clause applied.</returns>
    public IQueryBuilder<TEntity, TResult> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // Placeholder - interceptor provides actual implementation
        return new QueryBuilder<TEntity, TResult>(_state, _selector, _reader, true);
    }

    /// <summary>
    /// Specifies an additional sort order.
    /// </summary>
    /// <typeparam name="TKey">The type of the sort key.</typeparam>
    /// <param name="keySelector">The expression selecting the sort key.</param>
    /// <param name="direction">The sort direction.</param>
    /// <returns>A new query builder with the ORDER BY clause applied.</returns>
    public IQueryBuilder<TEntity, TResult> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, Direction direction = Direction.Ascending)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // Placeholder - interceptor provides actual implementation
        return new QueryBuilder<TEntity, TResult>(_state, _selector, _reader, true);
    }

    /// <summary>
    /// Skips the specified number of rows.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>A new query builder with the OFFSET applied.</returns>
    public IQueryBuilder<TEntity, TResult> Offset(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new QueryBuilder<TEntity, TResult>(_state.WithOffset(count), _selector, _reader, true);
    }

    /// <summary>
    /// Limits the result to the specified number of rows.
    /// </summary>
    /// <param name="count">The maximum number of rows to return.</param>
    /// <returns>A new query builder with the LIMIT applied.</returns>
    public IQueryBuilder<TEntity, TResult> Limit(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new QueryBuilder<TEntity, TResult>(_state.WithLimit(count), _selector, _reader, true);
    }

    /// <summary>
    /// Removes duplicate rows from the result.
    /// </summary>
    /// <returns>A new query builder with DISTINCT applied.</returns>
    public IQueryBuilder<TEntity, TResult> Distinct()
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithDistinct(), _selector, _reader, true);
    }

    /// <summary>
    /// Sets a custom timeout for this query.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A new query builder with the timeout applied.</returns>
    public IQueryBuilder<TEntity, TResult> WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        return new QueryBuilder<TEntity, TResult>(_state.WithTimeout(timeout), _selector, _reader, true);
    }

    #region GroupBy and Having Methods

    /// <summary>
    /// Groups rows by the specified key selector.
    /// </summary>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <param name="keySelector">The expression selecting the grouping key.</param>
    /// <returns>A new query builder with the GROUP BY clause applied.</returns>
    public IQueryBuilder<TEntity, TResult> GroupBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        // Placeholder - interceptor provides actual implementation
        return new QueryBuilder<TEntity, TResult>(_state, _selector, _reader, true);
    }

    /// <summary>
    /// Filters groups using the specified predicate.
    /// </summary>
    /// <param name="predicate">The filter predicate (typically contains aggregate functions).</param>
    /// <returns>A new query builder with the HAVING clause applied.</returns>
    public IQueryBuilder<TEntity, TResult> Having(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        // Placeholder - interceptor provides actual implementation
        return new QueryBuilder<TEntity, TResult>(_state, _selector, _reader, true);
    }

    /// <summary>
    /// Adds a GROUP BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddGroupByClause(string groupBySql)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithGroupByFragment(groupBySql), _selector, _reader, true);
    }

    /// <summary>
    /// Adds a HAVING clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddHavingClause(string havingSql)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithHaving(havingSql), _selector, _reader, true);
    }

    #endregion

    #region Generated Code Methods for Clauses

    /// <summary>
    /// Adds a WHERE clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddWhereClause(string whereSql)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithWhere(whereSql), _selector, _reader, true);
    }

    /// <summary>
    /// Adds a WHERE clause with pre-analyzed SQL and captured parameter values.
    /// Used by generated interceptors when the WHERE clause contains captured variables.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddWhereClause(string whereSql, params object?[] parameters)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithWhereAndParameters(whereSql, parameters), _selector, _reader, true);
    }

    /// <summary>
    /// Adds an ORDER BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddOrderByClause(string columnSql, Direction direction)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithOrderBy(columnSql, direction), _selector, _reader, true);
    }

    /// <summary>
    /// Adds a THEN BY clause with pre-analyzed SQL.
    /// Used by generated interceptors.
    /// </summary>
    public QueryBuilder<TEntity, TResult> AddThenByClause(string columnSql, Direction direction)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithOrderBy(columnSql, direction), _selector, _reader, true);
    }

    #endregion

    /// <summary>
    /// Returns the generated SQL without executing the query.
    /// </summary>
    /// <returns>The SQL that would be executed.</returns>
    public string ToSql()
    {
        return SqlBuilder.BuildSelectSql(_state);
    }

    public QueryDiagnostics ToDiagnostics() => new(
        ToSql(),
        DiagnosticsHelper.ConvertParameters(State.Parameters),
        DiagnosticQueryKind.Select,
        State.Dialect,
        State.TableName,
        rawState: State);

    #region Execution Methods

    /// <summary>
    /// Executes the query and returns all results as a list.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A list of all matching results.</returns>
    public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        return QueryExecutor.ExecuteFetchAllAsync(_state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns the first result.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The first matching result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the sequence contains no elements.</exception>
    public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        return QueryExecutor.ExecuteFetchFirstAsync(_state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns the first result, or default if no results.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The first matching result, or default if no results.</returns>
    public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        return QueryExecutor.ExecuteFetchFirstOrDefaultAsync(_state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns exactly one result.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The single matching result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the sequence contains zero or more than one element.</exception>
    public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        return QueryExecutor.ExecuteFetchSingleAsync(_state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns the scalar result.
    /// </summary>
    /// <typeparam name="TScalar">The scalar type to return.</typeparam>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The scalar value.</returns>
    public Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default)
    {
        return QueryExecutor.ExecuteScalarAsync<TScalar>(_state, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns the number of affected rows.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The number of affected rows.</returns>
    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        return QueryExecutor.ExecuteNonQueryAsync(_state, cancellationToken);
    }

    /// <summary>
    /// Executes the query and returns results as an async enumerable for streaming.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An async enumerable of results.</returns>
    public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        return QueryExecutor.ToAsyncEnumerable(_state, reader, cancellationToken);
    }

    /// <summary>
    /// Gets the reader delegate, throwing if not available.
    /// </summary>
    private Func<DbDataReader, TResult> GetReader()
    {
        return _reader ?? throw new InvalidOperationException(
            "No reader delegate available. This query may not be analyzable at compile-time. " +
            "Ensure the query is built in a single fluent chain without variable assignments or conditionals.");
    }

    #endregion

    #region Pre-built SQL Execution (Generated Code API)

    /// <summary>
    /// Gets the clause bitmask for pre-built SQL dispatch.
    /// Used by generated execution interceptors to select the correct SQL variant.
    /// </summary>
    public ulong ClauseMask => _state.ClauseMask;

    /// <summary>
    /// Returns a new builder with the specified clause bit set on the state.
    /// Used by generated clause interceptors for conditional clause tracking.
    /// </summary>
    public QueryBuilder<TEntity, TResult> WithClauseBit(int bit)
    {
        return new QueryBuilder<TEntity, TResult>(_state.WithClauseBit(bit), _selector, _reader, true);
    }

    /// <summary>
    /// Executes a query with pre-built SQL and returns all results as a list.
    /// Used by generated execution interceptors.
    /// </summary>
    public Task<List<TResult>> ExecuteWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    /// <summary>
    /// Executes a query with pre-built SQL and returns the first result.
    /// Used by generated execution interceptors.
    /// </summary>
    public Task<TResult> ExecuteFirstWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    /// <summary>
    /// Executes a query with pre-built SQL and returns the first result or default.
    /// Used by generated execution interceptors.
    /// </summary>
    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    /// <summary>
    /// Executes a query with pre-built SQL and returns exactly one result.
    /// Used by generated execution interceptors.
    /// </summary>
    public Task<TResult> ExecuteSingleWithPrebuiltSqlAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(_state, sql, reader, cancellationToken);

    /// <summary>
    /// Executes a query with pre-built SQL and returns the scalar result.
    /// Used by generated execution interceptors.
    /// </summary>
    public Task<TScalar> ExecuteScalarWithPrebuiltSqlAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
        => QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(_state, sql, cancellationToken);

    /// <summary>
    /// Executes a query with pre-built SQL and returns results as an async enumerable.
    /// Used by generated execution interceptors.
    /// </summary>
    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltSql(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
        => QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(_state, sql, reader, cancellationToken);

    #endregion

    /// <summary>
    /// Gets the internal state for use by generated code.
    /// </summary>
    internal QueryState State => _state;

    /// <summary>
    /// Gets the selector for use by generated code.
    /// </summary>
    internal Func<TEntity, TResult>? Selector => _selector;

    /// <summary>
    /// Gets the reader delegate for use by generated code.
    /// </summary>
    internal Func<DbDataReader, TResult>? Reader => _reader;

    #region Prebuilt Parameter Binding (Generated Code API)

    /// <summary>
    /// Pre-allocated parameter storage for prebuilt chains.
    /// </summary>
    internal object?[]? PrebuiltParams;

    /// <summary>
    /// Next write index into <see cref="PrebuiltParams"/>.
    /// </summary>
    internal int PrebuiltParamIndex;

    /// <summary>
    /// Allocates the pre-built parameter array.
    /// </summary>
    public void AllocatePrebuiltParams(int count)
    {
        Debug.Assert(count >= 0, "Prebuilt parameter count must be non-negative.");
        PrebuiltParams = new object?[count];
        PrebuiltParamIndex = 0;
    }

    /// <summary>
    /// Writes a captured parameter value to the next slot (mutate-in-place).
    /// </summary>
    public QueryBuilder<TEntity, TResult> BindParam(object? value)
    {
        Debug.Assert(PrebuiltParams != null, "AllocatePrebuiltParams must be called before BindParam.");
        Debug.Assert(PrebuiltParamIndex < PrebuiltParams!.Length, $"BindParam called more times ({PrebuiltParamIndex + 1}) than allocated ({PrebuiltParams.Length}).");
        PrebuiltParams![PrebuiltParamIndex++] = value;
        return this;
    }

    /// <summary>
    /// Sets a clause bit on the state's ClauseMask in place.
    /// </summary>
    public QueryBuilder<TEntity, TResult> SetClauseBit(int bit)
    {
        _state.SetClauseBitMutable(bit);
        return this;
    }

    /// <summary>
    /// Executes a FetchAll query using prebuilt params. Hydrates state at terminal time.
    /// </summary>
    public Task<List<TResult>> ExecuteWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a FetchFirst query using prebuilt params.
    /// </summary>
    public Task<TResult> ExecuteFirstWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a FetchFirstOrDefault query using prebuilt params.
    /// </summary>
    public Task<TResult?> ExecuteFirstOrDefaultWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteFirstOrDefaultWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a FetchSingle query using prebuilt params.
    /// </summary>
    public Task<TResult> ExecuteSingleWithPrebuiltParamsAsync(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteSingleWithPrebuiltSqlAsync(state, sql, reader, cancellationToken);
    }

    /// <summary>
    /// Executes a scalar query using prebuilt params.
    /// </summary>
    public Task<TScalar> ExecuteScalarWithPrebuiltParamsAsync<TScalar>(
        string sql, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ExecuteScalarWithPrebuiltSqlAsync<TScalar>(state, sql, cancellationToken);
    }

    /// <summary>
    /// Returns an async enumerable using prebuilt params.
    /// </summary>
    public IAsyncEnumerable<TResult> ToAsyncEnumerableWithPrebuiltParams(
        string sql, Func<DbDataReader, TResult> reader, CancellationToken cancellationToken)
    {
        var state = _state.WithPrebuiltParams(PrebuiltParams!, PrebuiltParamIndex);
        return QueryExecutor.ToAsyncEnumerableWithPrebuiltSql(state, sql, reader, cancellationToken);
    }

    #endregion
}
