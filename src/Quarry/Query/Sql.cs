using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Quarry;

/// <summary>
/// Provides SQL aggregate functions and raw SQL fragments for use in Quarry queries.
/// </summary>
/// <remarks>
/// <para>
/// These methods are translated at compile-time by the source generator to their
/// SQL equivalents. They cannot be invoked at runtime - attempting to do so will
/// throw an exception.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var stats = await db.Orders()
///     .Select(o => new {
///         TotalOrders = Sql.Count(),
///         TotalRevenue = Sql.Sum(o.Total),
///         AvgOrder = Sql.Avg(o.Total)
///     })
///     .ExecuteFetchFirstAsync();
/// </code>
/// </para>
/// </remarks>
public static class Sql
{
    /// <summary>
    /// Generates COUNT(*) to count all rows.
    /// </summary>
    /// <returns>The row count (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static int Count()
    {
        throw new InvalidOperationException(
            "Sql.Count() cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates COUNT(column) to count non-null values of a column.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="column">The column to count.</param>
    /// <returns>The count of non-null values (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static int Count<T>(T column)
    {
        throw new InvalidOperationException(
            "Sql.Count(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates SUM(column) to calculate the sum of values.
    /// </summary>
    /// <param name="column">The column to sum.</param>
    /// <returns>The sum (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static int Sum(int column)
    {
        throw new InvalidOperationException(
            "Sql.Sum(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates SUM(column) to calculate the sum of values.
    /// </summary>
    /// <param name="column">The column to sum.</param>
    /// <returns>The sum (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static long Sum(long column)
    {
        throw new InvalidOperationException(
            "Sql.Sum(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates SUM(column) to calculate the sum of values.
    /// </summary>
    /// <param name="column">The column to sum.</param>
    /// <returns>The sum (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static decimal Sum(decimal column)
    {
        throw new InvalidOperationException(
            "Sql.Sum(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates SUM(column) to calculate the sum of values.
    /// </summary>
    /// <param name="column">The column to sum.</param>
    /// <returns>The sum (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static double Sum(double column)
    {
        throw new InvalidOperationException(
            "Sql.Sum(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates AVG(column) to calculate the average of values.
    /// </summary>
    /// <param name="column">The column to average.</param>
    /// <returns>The average (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static double Avg(int column)
    {
        throw new InvalidOperationException(
            "Sql.Avg(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates AVG(column) to calculate the average of values.
    /// </summary>
    /// <param name="column">The column to average.</param>
    /// <returns>The average (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static double Avg(long column)
    {
        throw new InvalidOperationException(
            "Sql.Avg(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates AVG(column) to calculate the average of values.
    /// </summary>
    /// <param name="column">The column to average.</param>
    /// <returns>The average (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static decimal Avg(decimal column)
    {
        throw new InvalidOperationException(
            "Sql.Avg(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates AVG(column) to calculate the average of values.
    /// </summary>
    /// <param name="column">The column to average.</param>
    /// <returns>The average (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static double Avg(double column)
    {
        throw new InvalidOperationException(
            "Sql.Avg(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates MIN(column) to find the minimum value.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="column">The column to find the minimum of.</param>
    /// <returns>The minimum value (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static T Min<T>(T column)
    {
        throw new InvalidOperationException(
            "Sql.Min(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Generates MAX(column) to find the maximum value.
    /// </summary>
    /// <typeparam name="T">The column type.</typeparam>
    /// <param name="column">The column to find the maximum of.</param>
    /// <returns>The maximum value (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static T Max<T>(T column)
    {
        throw new InvalidOperationException(
            "Sql.Max(column) cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Injects a raw SQL fragment into the query.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="sql">The SQL fragment. Use {0}, {1}, etc. for argument placeholders.</param>
    /// <param name="parameters">Optional parameter values.</param>
    /// <returns>A value of type T (the actual SQL is evaluated by the database).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    /// <remarks>
    /// <para>
    /// Use Sql.Raw to inject database-specific SQL that cannot be expressed through
    /// the standard query methods. Parameters are properly escaped to prevent SQL injection.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// var users = await db.Users()
    ///     .Where(u => Sql.Raw&lt;bool&gt;("CONTAINS(user_name, {0})", searchTerm))
    ///     .Select(u => u)
    ///     .ExecuteFetchAllAsync();
    /// </code>
    /// </para>
    /// </remarks>
    public static T Raw<T>(string sql, params object?[] parameters)
    {
        throw new InvalidOperationException(
            "Sql.Raw() cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }

    /// <summary>
    /// Checks if a subquery returns any rows (EXISTS).
    /// </summary>
    /// <typeparam name="T">The subquery result type.</typeparam>
    /// <param name="subquery">The subquery to check.</param>
    /// <returns>True if the subquery returns any rows (translated to SQL at compile-time).</returns>
    /// <exception cref="InvalidOperationException">Always throws - this method is translated at compile-time.</exception>
    public static bool Exists<T>(IEnumerable<T> subquery)
    {
        throw new InvalidOperationException(
            "Sql.Exists() cannot be invoked at runtime. It is translated to SQL at compile-time. " +
            "Ensure the query is built in a single fluent chain.");
    }
}
