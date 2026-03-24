namespace Quarry.Generators.Models;

/// <summary>
/// Specifies the kind of interceptor to generate.
/// </summary>
internal enum InterceptorKind
{
    /// <summary>
    /// Select() method - generates column list and reader delegate.
    /// </summary>
    Select,

    /// <summary>
    /// Where() method - generates WHERE clause SQL fragment.
    /// </summary>
    Where,

    /// <summary>
    /// OrderBy() method - generates ORDER BY clause SQL fragment.
    /// </summary>
    OrderBy,

    /// <summary>
    /// ThenBy() method - generates additional ORDER BY clause SQL fragment.
    /// </summary>
    ThenBy,

    /// <summary>
    /// GroupBy() method - generates GROUP BY clause SQL fragment.
    /// </summary>
    GroupBy,

    /// <summary>
    /// Having() method - generates HAVING clause SQL fragment.
    /// </summary>
    Having,

    /// <summary>
    /// Set() method for Update operations - generates SET clause SQL fragment.
    /// </summary>
    Set,

    /// <summary>
    /// Join() method - generates JOIN clause SQL fragment.
    /// </summary>
    Join,

    /// <summary>
    /// LeftJoin() method - generates LEFT JOIN clause SQL fragment.
    /// </summary>
    LeftJoin,

    /// <summary>
    /// RightJoin() method - generates RIGHT JOIN clause SQL fragment.
    /// </summary>
    RightJoin,

    /// <summary>
    /// ExecuteFetchAllAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchAll,

    /// <summary>
    /// ExecuteFetchFirstAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchFirst,

    /// <summary>
    /// ExecuteFetchFirstOrDefaultAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchFirstOrDefault,

    /// <summary>
    /// ExecuteFetchSingleAsync() - assembles complete SQL and wires reader.
    /// </summary>
    ExecuteFetchSingle,

    /// <summary>
    /// ExecuteScalarAsync() - assembles complete SQL for scalar result.
    /// </summary>
    ExecuteScalar,

    /// <summary>
    /// ExecuteNonQueryAsync() - assembles complete SQL for non-query execution.
    /// </summary>
    ExecuteNonQuery,

    /// <summary>
    /// ToAsyncEnumerable() - assembles complete SQL and wires streaming reader.
    /// </summary>
    ToAsyncEnumerable,

    /// <summary>
    /// ExecuteNonQueryAsync() on InsertBuilder - generates insert with entity property extraction.
    /// </summary>
    InsertExecuteNonQuery,

    /// <summary>
    /// ExecuteScalarAsync() on InsertBuilder - generates insert with identity return.
    /// </summary>
    InsertExecuteScalar,

    /// <summary>
    /// ToDiagnostics() method - returns prebuilt QueryDiagnostics with SQL and optimization metadata.
    /// </summary>
    ToDiagnostics,

    /// <summary>
    /// ToDiagnostics() on InsertBuilder - generates insert SQL with column metadata for QueryDiagnostics.
    /// </summary>
    InsertToDiagnostics,

    /// <summary>
    /// Where() on DeleteBuilder or ExecutableDeleteBuilder - generates WHERE clause for DELETE operations.
    /// </summary>
    DeleteWhere,

    /// <summary>
    /// Set() on UpdateBuilder or ExecutableUpdateBuilder - generates SET clause for UPDATE operations.
    /// </summary>
    UpdateSet,

    /// <summary>
    /// Where() on UpdateBuilder or ExecutableUpdateBuilder - generates WHERE clause for UPDATE operations.
    /// </summary>
    UpdateWhere,

    /// <summary>
    /// Set(entity) on UpdateBuilder or ExecutableUpdateBuilder - generates SET clauses from POCO properties.
    /// </summary>
    UpdateSetPoco,

    /// <summary>
    /// Set(Action&lt;T&gt;) on UpdateBuilder or ExecutableUpdateBuilder - generates SET clauses
    /// from assignment expressions inside an Action&lt;T&gt; lambda body.
    /// </summary>
    UpdateSetAction,

    /// <summary>
    /// RawSqlAsync&lt;T&gt;() - generates a typed reader to replace reflection-based entity materialization.
    /// </summary>
    RawSqlAsync,

    /// <summary>
    /// RawSqlScalarAsync&lt;T&gt;() - generates a typed scalar read to replace Convert.ChangeType.
    /// </summary>
    RawSqlScalarAsync,

    /// <summary>
    /// Limit() method - sets row limit for pagination. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include parameterized LIMIT.
    /// </summary>
    Limit,

    /// <summary>
    /// Offset() method - sets row offset for pagination. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include parameterized OFFSET.
    /// </summary>
    Offset,

    /// <summary>
    /// Distinct() method - sets DISTINCT flag. Not intercepted (no expression),
    /// but tracked for chain analysis so pre-built SQL can include DISTINCT.
    /// </summary>
    Distinct,

    /// <summary>
    /// WithTimeout() method - sets query timeout. Not intercepted on non-carrier path,
    /// but tracked for chain analysis so carrier can store the timeout value.
    /// </summary>
    WithTimeout,

    /// <summary>
    /// Entity set factory method on QuarryContext (e.g., db.Users()).
    /// Chain root -- the first node in a query chain. On the carrier path,
    /// the chain root interceptor creates the carrier unconditionally.
    /// </summary>
    ChainRoot,

    /// <summary>
    /// .Delete() transition on IEntityAccessor -- switches from accessor to IDeleteBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    DeleteTransition,

    /// <summary>
    /// .Update() transition on IEntityAccessor -- switches from accessor to IUpdateBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    UpdateTransition,

    /// <summary>
    /// .All() transition on IDeleteBuilder/IUpdateBuilder -- switches to IExecutableDeleteBuilder/IExecutableUpdateBuilder.
    /// On the carrier path: noop cast (carrier implements both interfaces).
    /// </summary>
    AllTransition,

    /// <summary>
    /// .Insert(entity) transition on IEntityAccessor -- switches from accessor to IInsertBuilder.
    /// On the carrier path: stores entity reference and returns carrier as IInsertBuilder.
    /// </summary>
    InsertTransition,

    /// <summary>
    /// Insert(u => (u.Col1, u.Col2)) — batch insert column declaration on IEntityAccessor.
    /// On the carrier path: returns carrier as IBatchInsertBuilder.
    /// </summary>
    BatchInsertColumnSelector,

    /// <summary>
    /// .Values(entities) — data provision on IBatchInsertBuilder.
    /// On the carrier path: stores entity collection on carrier, returns as IExecutableBatchInsert.
    /// </summary>
    BatchInsertValues,

    /// <summary>
    /// ExecuteNonQueryAsync() on IExecutableBatchInsert — batch insert execution.
    /// </summary>
    BatchInsertExecuteNonQuery,

    /// <summary>
    /// ExecuteScalarAsync&lt;TKey&gt;() on IExecutableBatchInsert — batch insert with identity return.
    /// </summary>
    BatchInsertExecuteScalar,

    /// <summary>
    /// ToDiagnostics() on IExecutableBatchInsert — batch insert diagnostics.
    /// </summary>
    BatchInsertToDiagnostics,

    /// <summary>
    /// .Trace() method - compile-time-only signal for chain tracing.
    /// No interceptor generated; marks the chain as traced for inline comment emission.
    /// </summary>
    Trace,

    /// <summary>
    /// Unknown or unsupported method.
    /// </summary>
    Unknown
}

/// <summary>
/// Classifies the builder type for fast enum-based branching instead of string.Contains() checks.
/// </summary>
internal enum BuilderKind
{
    Query,
    Delete,
    ExecutableDelete,
    Update,
    ExecutableUpdate,
    JoinedQuery,
    EntityAccessor,
    BatchInsert,
    ExecutableBatchInsert,
}
