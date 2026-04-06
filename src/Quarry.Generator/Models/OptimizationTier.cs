namespace Quarry.Generators.Models;

/// <summary>
/// The optimization tier determined for a query chain.
/// </summary>
internal enum OptimizationTier
{
    /// <summary>
    /// All clause combinations are enumerable (up to 8 conditional bits = 256 variants).
    /// The execution interceptor carries const string SQL for every possible path.
    /// Zero runtime string work.
    /// </summary>
    PrebuiltDispatch,

    /// <summary>
    /// Chain cannot be analyzed statically. Produces a compile-time error
    /// directing the user to restructure.
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
    CteDefinition,
    FromCte,
    ChainRoot,
    DeleteTransition,
    UpdateTransition,
    AllTransition,
    InsertTransition,
    BatchInsertValues,
    SetOperation
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
