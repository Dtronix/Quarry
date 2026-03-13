namespace Quarry.Generators.Models;

/// <summary>
/// Metadata extracted from a [MigrationSnapshot] attributed class.
/// </summary>
internal sealed class SnapshotInfo
{
    public int Version { get; }
    public string Name { get; }
    public string ClassName { get; }
    public string Namespace { get; }
    public string SchemaHash { get; }

    public SnapshotInfo(int version, string name, string className, string ns, string schemaHash)
    {
        Version = version;
        Name = name;
        ClassName = className;
        Namespace = ns;
        SchemaHash = schemaHash;
    }
}
