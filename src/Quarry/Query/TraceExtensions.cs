namespace Quarry;

/// <summary>
/// Provides .Trace() extension methods for all Quarry builder interfaces.
/// .Trace() is a compile-time-only signal — at runtime it is a no-op that returns
/// the same builder instance. When the generator detects .Trace() in a chain and
/// QUARRY_TRACE is defined, inline trace comments are emitted in the generated .g.cs file.
/// </summary>
public static class TraceExtensions
{
    public static IQueryBuilder<T> Trace<T>(this IQueryBuilder<T> builder) where T : class
        => builder;

    public static IQueryBuilder<TEntity, TResult> Trace<TEntity, TResult>(this IQueryBuilder<TEntity, TResult> builder) where TEntity : class
        => builder;

    public static IJoinedQueryBuilder<T1, T2> Trace<T1, T2>(this IJoinedQueryBuilder<T1, T2> builder) where T1 : class where T2 : class
        => builder;

    public static IJoinedQueryBuilder<T1, T2, TResult> Trace<T1, T2, TResult>(this IJoinedQueryBuilder<T1, T2, TResult> builder) where T1 : class where T2 : class
        => builder;

    public static IJoinedQueryBuilder3<T1, T2, T3> Trace<T1, T2, T3>(this IJoinedQueryBuilder3<T1, T2, T3> builder) where T1 : class where T2 : class where T3 : class
        => builder;

    public static IJoinedQueryBuilder3<T1, T2, T3, TResult> Trace<T1, T2, T3, TResult>(this IJoinedQueryBuilder3<T1, T2, T3, TResult> builder) where T1 : class where T2 : class where T3 : class
        => builder;

    public static IJoinedQueryBuilder4<T1, T2, T3, T4> Trace<T1, T2, T3, T4>(this IJoinedQueryBuilder4<T1, T2, T3, T4> builder) where T1 : class where T2 : class where T3 : class where T4 : class
        => builder;

    public static IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> Trace<T1, T2, T3, T4, TResult>(this IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> builder) where T1 : class where T2 : class where T3 : class where T4 : class
        => builder;

    public static IEntityAccessor<T> Trace<T>(this IEntityAccessor<T> builder) where T : class
        => builder;

    public static IDeleteBuilder<T> Trace<T>(this IDeleteBuilder<T> builder) where T : class
        => builder;

    public static IExecutableDeleteBuilder<T> Trace<T>(this IExecutableDeleteBuilder<T> builder) where T : class
        => builder;

    public static IUpdateBuilder<T> Trace<T>(this IUpdateBuilder<T> builder) where T : class
        => builder;

    public static IExecutableUpdateBuilder<T> Trace<T>(this IExecutableUpdateBuilder<T> builder) where T : class
        => builder;

    public static IInsertBuilder<T> Trace<T>(this IInsertBuilder<T> builder) where T : class
        => builder;

    public static IBatchInsertBuilder<T> Trace<T>(this IBatchInsertBuilder<T> builder) where T : class
        => builder;

    public static IExecutableBatchInsert<T> Trace<T>(this IExecutableBatchInsert<T> builder) where T : class
        => builder;
}
