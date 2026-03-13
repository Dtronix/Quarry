using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.Translation;

/// <summary>
/// Represents one level of subquery nesting, with its own entity, parameter name, and table alias.
/// </summary>
internal sealed class SubqueryScope
{
    public string ParameterName { get; }
    public EntityInfo EntityInfo { get; }
    public string TableAlias { get; }
    public Dictionary<string, ColumnInfo> ColumnLookup { get; }

    public SubqueryScope(
        string parameterName,
        EntityInfo entityInfo,
        string tableAlias)
    {
        ParameterName = parameterName;
        EntityInfo = entityInfo;
        TableAlias = tableAlias;

        ColumnLookup = new Dictionary<string, ColumnInfo>();
        foreach (var column in entityInfo.Columns)
        {
            ColumnLookup[column.PropertyName] = column;
        }
    }
}
