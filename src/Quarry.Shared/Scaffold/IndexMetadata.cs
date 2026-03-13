using System.Collections.Generic;

namespace Quarry.Shared.Scaffold;

internal sealed class IndexMetadata
{
    public string Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public bool IsUnique { get; }
    public bool IsPrimaryKey { get; }

    public IndexMetadata(string name, IReadOnlyList<string> columns, bool isUnique = false, bool isPrimaryKey = false)
    {
        Name = name;
        Columns = columns;
        IsUnique = isUnique;
        IsPrimaryKey = isPrimaryKey;
    }
}
