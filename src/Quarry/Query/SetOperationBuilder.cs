using System.Collections.Immutable;
using System.Data.Common;
using System.Text;
using Quarry.Internal;
using Quarry.Shared.Sql;

namespace Quarry;

/// <summary>
/// The kind of set operation.
/// </summary>
public enum SetOperationKind
{
    /// <summary>
    /// UNION - combines results and removes duplicates.
    /// </summary>
    Union,

    /// <summary>
    /// UNION ALL - combines results keeping all duplicates.
    /// </summary>
    UnionAll,

    /// <summary>
    /// EXCEPT - returns rows from the first query not in subsequent queries.
    /// </summary>
    Except,

    /// <summary>
    /// INTERSECT - returns only rows that appear in all queries.
    /// </summary>
    Intersect
}

/// <summary>
/// Builder for set operations (UNION, UNION ALL, EXCEPT, INTERSECT).
/// </summary>
/// <typeparam name="T">The result type of the combined queries.</typeparam>
public sealed class SetOperationBuilder<T>
{
    private readonly ImmutableArray<string> _queries;
    private readonly SetOperationKind _kind;
    private readonly SqlDialect _dialect;
    private readonly IQueryExecutionContext? _executionContext;
    private readonly Func<DbDataReader, T>? _reader;
    private readonly ImmutableArray<QueryParameter> _parameters;
    private readonly int? _offset;
    private readonly int? _limit;
    private readonly ImmutableArray<OrderByClause> _orderByClauses;

    /// <summary>
    /// Creates a new SetOperationBuilder.
    /// </summary>
    internal SetOperationBuilder(
        ImmutableArray<string> queries,
        SetOperationKind kind,
        SqlDialect dialect,
        IQueryExecutionContext? executionContext,
        Func<DbDataReader, T>? reader,
        ImmutableArray<QueryParameter> parameters)
    {
        _queries = queries;
        _kind = kind;
        _dialect = dialect;
        _executionContext = executionContext;
        _reader = reader;
        _parameters = parameters;
        _offset = null;
        _limit = null;
        _orderByClauses = ImmutableArray<OrderByClause>.Empty;
    }

    private SetOperationBuilder(
        ImmutableArray<string> queries,
        SetOperationKind kind,
        SqlDialect dialect,
        IQueryExecutionContext? executionContext,
        Func<DbDataReader, T>? reader,
        ImmutableArray<QueryParameter> parameters,
        int? offset,
        int? limit,
        ImmutableArray<OrderByClause> orderByClauses)
    {
        _queries = queries;
        _kind = kind;
        _dialect = dialect;
        _executionContext = executionContext;
        _reader = reader;
        _parameters = parameters;
        _offset = offset;
        _limit = limit;
        _orderByClauses = orderByClauses;
    }

    /// <summary>
    /// Skips the specified number of rows in the combined result.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>A new builder with the OFFSET applied.</returns>
    public SetOperationBuilder<T> Offset(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        return new SetOperationBuilder<T>(_queries, _kind, _dialect, _executionContext, _reader, _parameters, count, _limit, _orderByClauses);
    }

    /// <summary>
    /// Limits the combined result to the specified number of rows.
    /// </summary>
    /// <param name="count">The maximum number of rows to return.</param>
    /// <returns>A new builder with the LIMIT applied.</returns>
    public SetOperationBuilder<T> Limit(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        return new SetOperationBuilder<T>(_queries, _kind, _dialect, _executionContext, _reader, _parameters, _offset, count, _orderByClauses);
    }

    /// <summary>
    /// Returns the generated SQL without executing the query.
    /// </summary>
    /// <returns>The SQL that would be executed.</returns>
    public string ToSql()
    {
        return BuildSql();
    }

    /// <summary>
    /// Executes the set operation and returns all results.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A list of all matching results.</returns>
    public async Task<List<T>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        var sql = BuildSql();

        if (_executionContext == null)
            throw new InvalidOperationException("No execution context available.");

        var state = CreateDummyState(sql);
        return await QueryExecutor.ExecuteFetchAllAsync(state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the set operation and returns the first result.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The first matching result.</returns>
    public async Task<T> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        var sql = BuildSql();

        if (_executionContext == null)
            throw new InvalidOperationException("No execution context available.");

        var state = CreateDummyState(sql);
        return await QueryExecutor.ExecuteFetchFirstAsync(state, reader, cancellationToken);
    }

    /// <summary>
    /// Executes the set operation and returns the first result, or default if none.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The first matching result, or default.</returns>
    public async Task<T?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var reader = GetReader();
        var sql = BuildSql();

        if (_executionContext == null)
            throw new InvalidOperationException("No execution context available.");

        var state = CreateDummyState(sql);
        return await QueryExecutor.ExecuteFetchFirstOrDefaultAsync(state, reader, cancellationToken);
    }

    private QueryState CreateDummyState(string sql)
    {
        // Create a dummy state with the full SQL as the table name
        // This is a workaround since we're generating complete SQL
        return new QueryState(_dialect, sql, null, _executionContext)
            .WithSelect(ImmutableArray.Create("*"));
    }

    private string BuildSql()
    {
        var sb = new StringBuilder();
        var operationKeyword = GetOperationKeyword();

        // SQLite does not support parenthesized SELECT in set operations
        var wrapInParens = _dialect != SqlDialect.SQLite;

        for (int i = 0; i < _queries.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
                sb.Append(operationKeyword);
                sb.Append(' ');
            }
            if (wrapInParens) sb.Append('(');
            sb.Append(_queries[i]);
            if (wrapInParens) sb.Append(')');
        }

        // ORDER BY for combined result
        if (_orderByClauses.Length > 0)
        {
            sb.Append(" ORDER BY ");
            var first = true;
            foreach (var orderBy in _orderByClauses)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(orderBy.Column);
                sb.Append(orderBy.Direction == Direction.Ascending ? " ASC" : " DESC");
            }
        }

        // Pagination
        if (_limit.HasValue || _offset.HasValue)
        {
            // For SQL Server, ORDER BY is required for OFFSET/FETCH
            if (_dialect == SqlDialect.SqlServer && _orderByClauses.Length == 0)
            {
                sb.Append(" ORDER BY (SELECT NULL)");
            }

            var pagination = SqlFormatting.FormatPagination(_dialect, _limit, _offset);
            if (!string.IsNullOrEmpty(pagination))
            {
                sb.Append(' ');
                sb.Append(pagination);
            }
        }

        return sb.ToString();
    }

    private string GetOperationKeyword()
    {
        return _kind switch
        {
            SetOperationKind.Union => "UNION",
            SetOperationKind.UnionAll => "UNION ALL",
            SetOperationKind.Except => "EXCEPT",
            SetOperationKind.Intersect => "INTERSECT",
            _ => "UNION"
        };
    }

    private Func<DbDataReader, T> GetReader()
    {
        return _reader ?? throw new InvalidOperationException(
            "No reader delegate available. This query may not be analyzable at compile-time.");
    }
}
