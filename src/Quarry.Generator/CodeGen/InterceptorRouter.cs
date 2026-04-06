using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Routes each call site to the appropriate emitter based on its InterceptorKind.
/// Replaces the large switch statements in InterceptorCodeGenerator.GenerateInterceptorMethod().
/// </summary>
internal static class InterceptorRouter
{
    /// <summary>
    /// Determines the emitter category for a given interceptor kind.
    /// Used to route call sites to the appropriate body emitter.
    /// </summary>
    public static EmitterCategory Categorize(InterceptorKind kind)
    {
        switch (kind)
        {
            // Clause methods
            case InterceptorKind.Where:
            case InterceptorKind.OrderBy:
            case InterceptorKind.ThenBy:
            case InterceptorKind.GroupBy:
            case InterceptorKind.Having:
            case InterceptorKind.Set:
            case InterceptorKind.UpdateSet:
            case InterceptorKind.UpdateSetPoco:
            case InterceptorKind.UpdateSetAction:
            case InterceptorKind.DeleteWhere:
            case InterceptorKind.UpdateWhere:
            case InterceptorKind.Select:
            case InterceptorKind.Distinct:
            case InterceptorKind.Limit:
            case InterceptorKind.Offset:
            case InterceptorKind.WithTimeout:
                return EmitterCategory.Clause;

            // Execution terminals
            case InterceptorKind.ExecuteFetchAll:
            case InterceptorKind.ExecuteFetchFirst:
            case InterceptorKind.ExecuteFetchFirstOrDefault:
            case InterceptorKind.ExecuteFetchSingle:
            case InterceptorKind.ExecuteFetchSingleOrDefault:
            case InterceptorKind.ExecuteScalar:
            case InterceptorKind.ExecuteNonQuery:
            case InterceptorKind.ToAsyncEnumerable:
            case InterceptorKind.InsertExecuteNonQuery:
            case InterceptorKind.InsertExecuteScalar:
            case InterceptorKind.ToDiagnostics:
            case InterceptorKind.InsertToDiagnostics:
                return EmitterCategory.Terminal;

            // Join methods
            case InterceptorKind.Join:
            case InterceptorKind.LeftJoin:
            case InterceptorKind.RightJoin:
            case InterceptorKind.CrossJoin:
            case InterceptorKind.FullOuterJoin:
                return EmitterCategory.Join;

            // Prepare terminal
            case InterceptorKind.Prepare:
                return EmitterCategory.Terminal;

            // Transition methods
            case InterceptorKind.DeleteTransition:
            case InterceptorKind.UpdateTransition:
            case InterceptorKind.AllTransition:
            case InterceptorKind.InsertTransition:
                return EmitterCategory.Transition;

            // Raw SQL methods
            case InterceptorKind.RawSqlAsync:
            case InterceptorKind.RawSqlScalarAsync:
                return EmitterCategory.RawSql;

            // Set operations
            case InterceptorKind.Union:
            case InterceptorKind.UnionAll:
            case InterceptorKind.Intersect:
            case InterceptorKind.IntersectAll:
            case InterceptorKind.Except:
            case InterceptorKind.ExceptAll:
                return EmitterCategory.SetOperation;

            // Chain root
            case InterceptorKind.ChainRoot:
                return EmitterCategory.ChainRoot;

            default:
                return EmitterCategory.Unknown;
        }
    }
}

/// <summary>
/// Categories of interceptor body emitters.
/// </summary>
internal enum EmitterCategory
{
    /// <summary>Clause methods (Where, OrderBy, Select, etc.).</summary>
    Clause,
    /// <summary>Execution terminal methods (FetchAll, Delete, etc.).</summary>
    Terminal,
    /// <summary>Join methods (Join, LeftJoin, RightJoin).</summary>
    Join,
    /// <summary>Transition methods (Delete, Update, Insert builder transitions).</summary>
    Transition,
    /// <summary>Raw SQL methods.</summary>
    RawSql,
    /// <summary>Chain root (entity set factory method).</summary>
    ChainRoot,
    /// <summary>Set operation methods (Union, Intersect, Except).</summary>
    SetOperation,
    /// <summary>Unknown/unhandled kind.</summary>
    Unknown
}
