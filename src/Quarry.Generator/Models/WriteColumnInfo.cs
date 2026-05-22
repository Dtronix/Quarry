using System;

namespace Quarry.Generators.Models;

/// <summary>
/// Per-column metadata used when emitting columns that are written to the database —
/// shared between insert (<see cref="InsertInfo"/>) and patch (<see cref="PatchInfo"/>)
/// emission paths, and reusable by future write-side flows (e.g. batch update).
/// </summary>
/// <remarks>
/// Contains everything the binder/emitter needs at parameter-build time: SQL-side
/// identifiers (quoted column name), CLR type and shape (value type, nullability,
/// enum underlying type, boolean coercion), and write-side hooks (FK <c>.Id</c>
/// extraction, custom <see cref="ITypeMapping"/> dispatch, sensitive-value redaction).
/// </remarks>
internal sealed class WriteColumnInfo : IEquatable<WriteColumnInfo>
{
    /// <summary>
    /// Gets the property name in the entity class.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the quoted column name for SQL generation.
    /// </summary>
    public string QuotedColumnName { get; }

    /// <summary>
    /// Gets the CLR type name.
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// Gets the fully qualified CLR type name.
    /// </summary>
    public string FullClrType { get; }

    /// <summary>
    /// Gets whether the column is nullable.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// Gets whether the CLR type is a value type.
    /// </summary>
    public bool IsValueType { get; }

    /// <summary>
    /// Gets whether this column is a foreign key (Ref&lt;TEntity, TKey&gt;).
    /// When true, generated code must extract .Id before passing to ADO.NET.
    /// </summary>
    public bool IsForeignKey { get; }

    /// <summary>
    /// Gets the referenced entity type name for FK columns (e.g., "User").
    /// Null for non-FK columns.
    /// </summary>
    public string? ForeignKeyEntityName { get; }

    /// <summary>
    /// Gets the fully qualified custom TypeMapping class name, if this column uses one.
    /// When set, write code should wrap the value with mapper.ToDb(value).
    /// </summary>
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// Gets whether this column contains sensitive data.
    /// When true, parameter values are redacted in log output.
    /// </summary>
    public bool IsSensitive { get; }

    /// <summary>
    /// Gets whether the CLR type is an enum.
    /// When true, carrier write code must cast to the underlying integer type.
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// Gets whether the CLR type is a boolean.
    /// When true, carrier write code must convert to 0/1 and set DbType.Int32
    /// for providers (e.g. SQLite) that reject boxed booleans.
    /// </summary>
    public bool IsBoolean { get; }

    /// <summary>
    /// Gets the underlying type name for enum columns (e.g., "int", "byte").
    /// Null for non-enum columns. Used for the cast in generated write code.
    /// </summary>
    public string? EnumUnderlyingType { get; }

    public WriteColumnInfo(
        string propertyName,
        string columnName,
        string quotedColumnName,
        string clrType,
        string fullClrType,
        bool isNullable,
        bool isValueType,
        bool isForeignKey = false,
        string? foreignKeyEntityName = null,
        string? customTypeMappingClass = null,
        bool isSensitive = false,
        bool isEnum = false,
        bool isBoolean = false,
        string? enumUnderlyingType = null)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        QuotedColumnName = quotedColumnName;
        ClrType = clrType;
        FullClrType = fullClrType;
        IsNullable = isNullable;
        IsValueType = isValueType;
        IsForeignKey = isForeignKey;
        ForeignKeyEntityName = foreignKeyEntityName;
        CustomTypeMappingClass = customTypeMappingClass;
        IsSensitive = isSensitive;
        IsEnum = isEnum;
        IsBoolean = isBoolean;
        EnumUnderlyingType = enumUnderlyingType;
    }

    public bool Equals(WriteColumnInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && ColumnName == other.ColumnName
            && QuotedColumnName == other.QuotedColumnName
            && ClrType == other.ClrType
            && FullClrType == other.FullClrType
            && IsNullable == other.IsNullable
            && IsValueType == other.IsValueType
            && IsForeignKey == other.IsForeignKey
            && ForeignKeyEntityName == other.ForeignKeyEntityName
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && IsSensitive == other.IsSensitive
            && IsEnum == other.IsEnum
            && IsBoolean == other.IsBoolean
            && EnumUnderlyingType == other.EnumUnderlyingType;
    }

    public override bool Equals(object? obj) => Equals(obj as WriteColumnInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ColumnName, ClrType, IsNullable);
    }
}
