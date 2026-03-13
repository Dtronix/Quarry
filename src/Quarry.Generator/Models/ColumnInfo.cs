using Microsoft.CodeAnalysis;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents a column defined in a schema.
/// </summary>
internal sealed class ColumnInfo
{
    /// <summary>
    /// The property name in the schema (e.g., "UserId").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The database column name (after naming convention or MapTo).
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// The CLR type of the column value.
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// The fully qualified CLR type for code generation.
    /// </summary>
    public string FullClrType { get; }

    /// <summary>
    /// Whether the column type is nullable.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// The kind of column (Key, Col, or Ref).
    /// </summary>
    public ColumnKind Kind { get; }

    /// <summary>
    /// For Ref columns, the referenced entity type name.
    /// </summary>
    public string? ReferencedEntityName { get; }

    /// <summary>
    /// Column modifiers extracted from the schema definition.
    /// </summary>
    public ColumnModifiers Modifiers { get; }

    /// <summary>
    /// Whether the CLR type is a value type (struct).
    /// </summary>
    public bool IsValueType { get; }

    /// <summary>
    /// The DbDataReader method name for reading this column (e.g., "GetInt32").
    /// </summary>
    public string ReaderMethodName { get; }

    /// <summary>
    /// Whether the CLR type is an enum type.
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// The fully qualified name of the custom TypeMapping class, if any.
    /// </summary>
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// The database CLR type (TDb from TypeMapping&lt;TCustom, TDb&gt;), e.g. "decimal".
    /// Only set when CustomTypeMappingClass is set.
    /// </summary>
    public string? DbClrType { get; }

    /// <summary>
    /// The DbDataReader method name for reading the database type (e.g. "GetDecimal").
    /// Only set when CustomTypeMappingClass is set; reader code should use this instead of ReaderMethodName.
    /// </summary>
    public string? DbReaderMethodName { get; }

    /// <summary>
    /// When non-null, indicates the column type mismatches the TypeMapping's TCustom.
    /// Contains the display name of TCustom from the mapping (for QRY017 diagnostic).
    /// </summary>
    public string? MappingMismatchExpectedType { get; }

    public ColumnInfo(
        string propertyName,
        string columnName,
        string clrType,
        string fullClrType,
        bool isNullable,
        ColumnKind kind,
        string? referencedEntityName,
        ColumnModifiers modifiers,
        bool isValueType = false,
        string readerMethodName = "GetValue",
        bool isEnum = false,
        string? customTypeMappingClass = null,
        string? dbClrType = null,
        string? dbReaderMethodName = null,
        string? mappingMismatchExpectedType = null)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        ClrType = clrType;
        FullClrType = fullClrType;
        IsNullable = isNullable;
        Kind = kind;
        ReferencedEntityName = referencedEntityName;
        Modifiers = modifiers;
        IsValueType = isValueType;
        ReaderMethodName = readerMethodName;
        IsEnum = isEnum;
        CustomTypeMappingClass = customTypeMappingClass;
        DbClrType = dbClrType;
        DbReaderMethodName = dbReaderMethodName;
        MappingMismatchExpectedType = mappingMismatchExpectedType;
    }

    /// <summary>
    /// Gets type metadata from an ITypeSymbol for creating ColumnInfo.
    /// </summary>
    public static (bool IsValueType, string ReaderMethodName, bool IsEnum) GetTypeMetadata(ITypeSymbol type)
    {
        // Unwrap nullable value types
        var underlyingType = type;
        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length > 0)
        {
            underlyingType = namedType.TypeArguments[0];
        }

        var isValueType = underlyingType.IsValueType;
        var isEnum = underlyingType.TypeKind == TypeKind.Enum;

        // For enum types, use the reader method for the underlying integral type
        string readerMethod;
        if (isEnum && underlyingType is INamedTypeSymbol enumType && enumType.EnumUnderlyingType != null)
        {
            readerMethod = GetReaderMethodFromType(enumType.EnumUnderlyingType);
        }
        else
        {
            readerMethod = GetReaderMethodFromType(underlyingType);
        }

        return (isValueType, readerMethod, isEnum);
    }

    /// <summary>
    /// Gets the DbDataReader method name for a type symbol.
    /// </summary>
    private static string GetReaderMethodFromType(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "GetBoolean",
            SpecialType.System_Byte => "GetByte",
            SpecialType.System_SByte => "GetByte",
            SpecialType.System_Int16 => "GetInt16",
            SpecialType.System_UInt16 => "GetInt16",
            SpecialType.System_Int32 => "GetInt32",
            SpecialType.System_UInt32 => "GetInt32",
            SpecialType.System_Int64 => "GetInt64",
            SpecialType.System_UInt64 => "GetInt64",
            SpecialType.System_Single => "GetFloat",
            SpecialType.System_Double => "GetDouble",
            SpecialType.System_Decimal => "GetDecimal",
            SpecialType.System_String => "GetString",
            SpecialType.System_Char => "GetChar",
            SpecialType.System_DateTime => "GetDateTime",
            _ => GetReaderMethodByTypeName(type)
        };
    }

    /// <summary>
    /// Gets the reader method for types not covered by SpecialType.
    /// </summary>
    private static string GetReaderMethodByTypeName(ITypeSymbol type)
    {
        var fullName = type.ToDisplayString();
        return fullName switch
        {
            "Guid" or "System.Guid" => "GetGuid",
            "DateTime" or "System.DateTime" => "GetDateTime",
            "DateTimeOffset" or "System.DateTimeOffset" => "GetValue", // Needs conversion
            "TimeSpan" or "System.TimeSpan" => "GetValue", // Needs conversion
            "DateOnly" or "System.DateOnly" => "GetValue", // Needs conversion
            "TimeOnly" or "System.TimeOnly" => "GetValue", // Needs conversion
            _ => "GetValue" // Fallback for custom types
        };
    }
}


/// <summary>
/// Column modifiers extracted from fluent configuration.
/// </summary>
internal sealed class ColumnModifiers
{
    /// <summary>Whether this is an identity/auto-increment column.</summary>
    public bool IsIdentity { get; }

    /// <summary>Whether this is a client-generated column (for GUIDs).</summary>
    public bool IsClientGenerated { get; }

    /// <summary>Whether this is a computed/read-only column.</summary>
    public bool IsComputed { get; }

    /// <summary>The maximum length for string columns.</summary>
    public int? MaxLength { get; }

    /// <summary>The precision for decimal columns.</summary>
    public int? Precision { get; }

    /// <summary>The scale for decimal columns.</summary>
    public int? Scale { get; }

    /// <summary>Whether a default value is specified.</summary>
    public bool HasDefault { get; }

    /// <summary>Whether this is a foreign key.</summary>
    public bool IsForeignKey { get; }

    /// <summary>Explicit column name from MapTo().</summary>
    public string? MappedName { get; }

    /// <summary>Custom type mapping class name from Mapped&lt;TMapping&gt;().</summary>
    public string? CustomTypeMapping { get; }

    /// <summary>Whether this column has a unique constraint (from .Unique() modifier).</summary>
    public bool IsUnique { get; }

    /// <summary>Whether this column contains sensitive data (from .Sensitive() modifier).</summary>
    public bool IsSensitive { get; }

    public ColumnModifiers(
        bool isIdentity = false,
        bool isClientGenerated = false,
        bool isComputed = false,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        bool hasDefault = false,
        bool isForeignKey = false,
        string? mappedName = null,
        string? customTypeMapping = null,
        bool isUnique = false,
        bool isSensitive = false)
    {
        IsIdentity = isIdentity;
        IsClientGenerated = isClientGenerated;
        IsComputed = isComputed;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        HasDefault = hasDefault;
        IsForeignKey = isForeignKey;
        MappedName = mappedName;
        CustomTypeMapping = customTypeMapping;
        IsUnique = isUnique;
        IsSensitive = isSensitive;
    }
}
