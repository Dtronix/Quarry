namespace Quarry.Shared.Scaffold;

internal sealed class ReverseTypeResult
{
    public string ClrType { get; }
    public bool IsNullable { get; }
    public int? MaxLength { get; }
    public int? Precision { get; }
    public int? Scale { get; }
    public string? Warning { get; }

    public ReverseTypeResult(
        string clrType,
        bool isNullable,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        string? warning = null)
    {
        ClrType = clrType;
        IsNullable = isNullable;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        Warning = warning;
    }
}
