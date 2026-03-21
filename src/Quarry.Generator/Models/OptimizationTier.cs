namespace Quarry.Generators.Models;

/// <summary>
/// The optimization tier determined for a query chain.
/// </summary>
internal enum OptimizationTier
{
    /// <summary>
    /// Tier 1: All clause combinations are enumerable (up to 4 conditional bits = 16 variants).
    /// The execution interceptor carries const string SQL for every possible path.
    /// Zero runtime string work.
    /// </summary>
    PrebuiltDispatch,

    /// <summary>
    /// Tier 2: Clauses are intercepted and pre-quoted, but too many combinations for a dispatch table.
    /// A lightweight concat assembles the final SQL at runtime.
    /// </summary>
    PrequotedFragments,

    /// <summary>
    /// Tier 3: Dynamic/opaque composition. No execution interceptor emitted.
    /// Current SqlBuilder path runs unchanged.
    /// </summary>
    RuntimeBuild
}

/// <summary>
/// The role a clause plays in a query chain.
/// Distinct from <see cref="ClauseKind"/> which is used for SQL clause translation.
/// ClauseRole tracks clause position in a chain and includes Select/Limit/Offset
/// which ClauseKind does not cover.
/// </summary>
internal enum ClauseRole
{
    Select,
    Where,
    OrderBy,
    ThenBy,
    GroupBy,
    Having,
    Join,
    Set,
    Limit,
    Offset,
    Distinct,
    DeleteWhere,
    UpdateWhere,
    UpdateSet,
    WithTimeout,
    ChainRoot,
    DeleteTransition,
    UpdateTransition,
    AllTransition,
    InsertTransition
}

/// <summary>
/// How a conditional clause relates to other clauses at the same branch point.
/// </summary>
internal enum BranchKind
{
    /// <summary>
    /// An if block with no else, or with an else that does not assign to the variable.
    /// The clause is either applied or not. Consumes 1 bit.
    /// </summary>
    Independent,

    /// <summary>
    /// An if/else where both branches assign to the variable.
    /// The clauses are alternatives. For if/else with one clause each, consumes 1 bit.
    /// </summary>
    MutuallyExclusive
}
