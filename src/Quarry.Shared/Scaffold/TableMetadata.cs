namespace Quarry.Shared.Scaffold;

internal sealed class TableMetadata
{
    public string Name { get; }
    public string? Schema { get; }

    public TableMetadata(string name, string? schema)
    {
        Name = name;
        Schema = schema;
    }
}
