namespace Quarry.Migration;

/// <summary>
/// Fluent builder for constructing <see cref="ColumnDef"/> instances.
/// </summary>
public sealed class ColumnDefBuilder
{
    private string _name = "";
    private string _clrType = "string";
    private bool _isNullable;
    private ColumnKind _kind;
    private bool _isIdentity;
    private bool _isClientGenerated;
    private bool _isComputed;
    private int? _maxLength;
    private int? _precision;
    private int? _scale;
    private bool _hasDefault;
    private string? _defaultExpression;
    private string? _mappedName;
    private string? _referencedEntityName;
    private string? _customTypeMapping;
    private string? _computedExpression;
    private string? _collation;

    public ColumnDefBuilder Name(string name) { _name = name; return this; }
    public ColumnDefBuilder ClrType(string clrType) { _clrType = clrType; return this; }
    public ColumnDefBuilder Nullable() { _isNullable = true; return this; }
    public ColumnDefBuilder PrimaryKey() { _kind = ColumnKind.PrimaryKey; return this; }
    public ColumnDefBuilder ForeignKey(string referencedEntityName)
    {
        _kind = ColumnKind.ForeignKey;
        _referencedEntityName = referencedEntityName;
        return this;
    }
    public ColumnDefBuilder Identity() { _isIdentity = true; return this; }
    public ColumnDefBuilder ClientGenerated() { _isClientGenerated = true; return this; }
    public ColumnDefBuilder Computed() { _isComputed = true; return this; }
    public ColumnDefBuilder Computed(string expression) { _isComputed = true; _computedExpression = expression; return this; }
    public ColumnDefBuilder Collation(string collation) { _collation = collation; return this; }
    public ColumnDefBuilder Length(int maxLength) { _maxLength = maxLength; return this; }
    public ColumnDefBuilder Precision(int precision, int scale) { _precision = precision; _scale = scale; return this; }
    public ColumnDefBuilder Default(string expression) { _hasDefault = true; _defaultExpression = expression; return this; }
    public ColumnDefBuilder HasDefault() { _hasDefault = true; return this; }
    public ColumnDefBuilder MapTo(string mappedName) { _mappedName = mappedName; return this; }
    public ColumnDefBuilder CustomTypeMapping(string mapping) { _customTypeMapping = mapping; return this; }
    public ColumnDefBuilder NotNull() { _isNullable = false; return this; }

    public ColumnDef Build()
    {
        return new ColumnDef(
            _name, _clrType, _isNullable, _kind,
            _isIdentity, _isClientGenerated, _isComputed,
            _maxLength, _precision, _scale,
            _hasDefault, _defaultExpression, _mappedName,
            _referencedEntityName, _customTypeMapping,
            _computedExpression, _collation);
    }
}
