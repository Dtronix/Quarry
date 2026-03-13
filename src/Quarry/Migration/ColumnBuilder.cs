namespace Quarry.Migration;

/// <summary>
/// Builder for defining a column's properties.
/// </summary>
public sealed class ColumnBuilder
{
    private string? _sqlType;
    private string? _clrType;
    private int? _maxLength;
    private int? _precision;
    private int? _scale;
    private bool _nullable;
    private string? _defaultValue;
    private string? _defaultExpression;
    private bool _identity;
    private bool _notNull;

    public ColumnBuilder Type(string sqlType) { _sqlType = sqlType; return this; }
    public ColumnBuilder ClrType(string clrType) { _clrType = clrType; return this; }
    public ColumnBuilder Length(int maxLength) { _maxLength = maxLength; return this; }
    public ColumnBuilder Precision(int precision, int scale) { _precision = precision; _scale = scale; return this; }
    public ColumnBuilder Nullable(bool nullable = true) { _nullable = nullable; return this; }
    public ColumnBuilder DefaultValue(string expression) { _defaultValue = expression; return this; }
    public ColumnBuilder DefaultExpression(string sql) { _defaultExpression = sql; return this; }
    public ColumnBuilder Identity() { _identity = true; return this; }
    public ColumnBuilder NotNull() { _notNull = true; return this; }

    internal ColumnDefinition Build()
    {
        return new ColumnDefinition(
            Name: "",
            SqlType: _sqlType,
            ClrType: _clrType,
            MaxLength: _maxLength,
            Precision: _precision,
            Scale: _scale,
            IsNullable: _nullable && !_notNull,
            DefaultValue: _defaultValue,
            DefaultExpression: _defaultExpression,
            IsIdentity: _identity);
    }
}
