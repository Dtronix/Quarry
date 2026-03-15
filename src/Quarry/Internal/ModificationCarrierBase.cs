using System.Linq.Expressions;

namespace Quarry.Internal;

public abstract class DeleteCarrierBase<T> : IDeleteBuilder<T>, IExecutableDeleteBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // IDeleteBuilder<T> explicit implementations

    IExecutableDeleteBuilder<T> IDeleteBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableDeleteBuilder<T> IDeleteBuilder<T>.All()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");

    IDeleteBuilder<T> IDeleteBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IExecutableDeleteBuilder<T> explicit implementations

    IExecutableDeleteBuilder<T> IExecutableDeleteBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableDeleteBuilder<T> IExecutableDeleteBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<int> IExecutableDeleteBuilder<T>.ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ToSql — both interfaces declare it independently, so both need explicit impls.

    string IDeleteBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IDeleteBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IExecutableDeleteBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IExecutableDeleteBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}

public abstract class UpdateCarrierBase<T> : IUpdateBuilder<T>, IExecutableUpdateBuilder<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // IUpdateBuilder<T> explicit implementations

    IUpdateBuilder<T> IUpdateBuilder<T>.Set<TValue>(Expression<Func<T, TValue>> column, TValue value)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IUpdateBuilder<T> IUpdateBuilder<T>.Set(T entity)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IUpdateBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IUpdateBuilder<T>.All()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.All is not intercepted in this optimized chain. This indicates a code generation bug.");

    IUpdateBuilder<T> IUpdateBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    // IExecutableUpdateBuilder<T> explicit implementations

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Set<TValue>(Expression<Func<T, TValue>> column, TValue value)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Set(T entity)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Set is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.Where(Expression<Func<T, bool>> predicate)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.Where is not intercepted in this optimized chain. This indicates a code generation bug.");

    IExecutableUpdateBuilder<T> IExecutableUpdateBuilder<T>.WithTimeout(TimeSpan timeout)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.WithTimeout is not intercepted in this optimized chain. This indicates a code generation bug.");

    Task<int> IExecutableUpdateBuilder<T>.ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ExecuteNonQueryAsync is not intercepted in this optimized chain. This indicates a code generation bug.");

    // ToSql — both interfaces declare it independently, so both need explicit impls.

    string IUpdateBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IUpdateBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");

    string IExecutableUpdateBuilder<T>.ToSql()
        => throw new InvalidOperationException("Carrier method IExecutableUpdateBuilder.ToSql is not intercepted in this optimized chain. This indicates a code generation bug.");
}
