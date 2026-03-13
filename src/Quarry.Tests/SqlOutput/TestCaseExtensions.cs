namespace Quarry.Tests.SqlOutput;

internal static class TestCaseExtensions
{
    // IQueryBuilder<T> (no projection)
    internal static SqlTestCase ToTestCase<T>(this IQueryBuilder<T> b) where T : class
        => new(b.ToSql(), ((QueryBuilder<T>)b).State);

    // IQueryBuilder<TEntity, TResult> (with projection)
    internal static SqlTestCase ToTestCase<TEntity, TResult>(this IQueryBuilder<TEntity, TResult> b) where TEntity : class
        => new(b.ToSql(), ((QueryBuilder<TEntity, TResult>)b).State);

    // IJoinedQueryBuilder<T1, T2> (no projection)
    internal static SqlTestCase ToTestCase<T1, T2>(this IJoinedQueryBuilder<T1, T2> b)
        where T1 : class where T2 : class
        => new(b.ToSql(), ((JoinedQueryBuilder<T1, T2>)b).State);

    // IJoinedQueryBuilder<T1, T2, TResult> (with projection)
    internal static SqlTestCase ToTestCase<T1, T2, TResult>(this IJoinedQueryBuilder<T1, T2, TResult> b)
        where T1 : class where T2 : class
        => new(b.ToSql(), ((JoinedQueryBuilder<T1, T2, TResult>)b).State);

    // IJoinedQueryBuilder3<T1, T2, T3>
    internal static SqlTestCase ToTestCase<T1, T2, T3>(this IJoinedQueryBuilder3<T1, T2, T3> b)
        where T1 : class where T2 : class where T3 : class
        => new(b.ToSql(), ((JoinedQueryBuilder3<T1, T2, T3>)b).State);

    // IJoinedQueryBuilder3<T1, T2, T3, TResult>
    internal static SqlTestCase ToTestCase<T1, T2, T3, TResult>(this IJoinedQueryBuilder3<T1, T2, T3, TResult> b)
        where T1 : class where T2 : class where T3 : class
        => new(b.ToSql(), ((JoinedQueryBuilder3<T1, T2, T3, TResult>)b).State);

    // IJoinedQueryBuilder4<T1, T2, T3, T4>
    internal static SqlTestCase ToTestCase<T1, T2, T3, T4>(this IJoinedQueryBuilder4<T1, T2, T3, T4> b)
        where T1 : class where T2 : class where T3 : class where T4 : class
        => new(b.ToSql(), ((JoinedQueryBuilder4<T1, T2, T3, T4>)b).State);

    // IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>
    internal static SqlTestCase ToTestCase<T1, T2, T3, T4, TResult>(this IJoinedQueryBuilder4<T1, T2, T3, T4, TResult> b)
        where T1 : class where T2 : class where T3 : class where T4 : class
        => new(b.ToSql(), ((JoinedQueryBuilder4<T1, T2, T3, T4, TResult>)b).State);

    // IExecutableUpdateBuilder<T>
    internal static UpdateTestCase ToTestCase<T>(this IExecutableUpdateBuilder<T> b) where T : class
        => new(b.ToSql(), ((ExecutableUpdateBuilder<T>)b).State);

    // IExecutableDeleteBuilder<T>
    internal static DeleteTestCase ToTestCase<T>(this IExecutableDeleteBuilder<T> b) where T : class
        => new(b.ToSql(), ((ExecutableDeleteBuilder<T>)b).State);

    // IInsertBuilder<T>
    internal static InsertTestCase ToTestCase<T>(this IInsertBuilder<T> b) where T : class
    {
        var concrete = (InsertBuilder<T>)b;
        return new(b.ToSql(), concrete.State, concrete.Entities.Count);
    }
}
