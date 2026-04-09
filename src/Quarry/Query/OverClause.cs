namespace Quarry;

/// <summary>
/// Runtime dummy implementation of <see cref="IOverClause"/>.
/// All methods throw — the source generator intercepts these calls at compile time.
/// </summary>
internal sealed class OverClause : IOverClause
{
    public IOverClause PartitionBy<T>(params T[] columns) =>
        throw new InvalidOperationException(
            "IOverClause.PartitionBy() cannot be invoked at runtime. It is translated to SQL at compile-time.");

    public IOverClause OrderBy<T>(T column) =>
        throw new InvalidOperationException(
            "IOverClause.OrderBy() cannot be invoked at runtime. It is translated to SQL at compile-time.");

    public IOverClause OrderByDescending<T>(T column) =>
        throw new InvalidOperationException(
            "IOverClause.OrderByDescending() cannot be invoked at runtime. It is translated to SQL at compile-time.");
}
