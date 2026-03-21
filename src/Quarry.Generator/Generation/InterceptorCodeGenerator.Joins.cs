using System.Text;
using Quarry.Generators.Models;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    // Join, JoinedWhere, JoinedOrderBy, JoinedSelect methods moved to CodeGen.JoinBodyEmitter

    /// <summary>
    /// Gets the builder type name for a given entity count in joins.
    /// Kept here for use by Execution.cs; also duplicated in JoinBodyEmitter.
    /// </summary>
    internal static string GetJoinedBuilderTypeName(int entityCount)
    {
        return entityCount switch
        {
            2 => "IJoinedQueryBuilder",
            3 => "IJoinedQueryBuilder3",
            4 => "IJoinedQueryBuilder4",
            _ => throw new System.ArgumentOutOfRangeException(nameof(entityCount), $"Unsupported entity count: {entityCount}")
        };
    }
}
