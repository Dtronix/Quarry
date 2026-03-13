namespace Quarry.Shared.Scaffold;

internal sealed class ColumnMetadata
{
    public string Name { get; }
    public string DataType { get; }
    public bool IsNullable { get; }
    public int? MaxLength { get; }
    public int? Precision { get; }
    public int? Scale { get; }
    public bool IsIdentity { get; }
    public string? DefaultExpression { get; }
    public int OrdinalPosition { get; }

    public ColumnMetadata(
        string name,
        string dataType,
        bool isNullable,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        bool isIdentity = false,
        string? defaultExpression = null,
        int ordinalPosition = 0)
    {
        Name = name;
        DataType = dataType;
        IsNullable = isNullable;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        IsIdentity = isIdentity;
        DefaultExpression = defaultExpression;
        OrdinalPosition = ordinalPosition;
    }
}
