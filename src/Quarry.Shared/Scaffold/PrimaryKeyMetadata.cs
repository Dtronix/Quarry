using System.Collections.Generic;

namespace Quarry.Shared.Scaffold;

internal sealed class PrimaryKeyMetadata
{
    public string? ConstraintName { get; }
    public IReadOnlyList<string> Columns { get; }

    public PrimaryKeyMetadata(string? constraintName, IReadOnlyList<string> columns)
    {
        ConstraintName = constraintName;
        Columns = columns;
    }
}
