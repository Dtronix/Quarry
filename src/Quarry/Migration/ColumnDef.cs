using System;

namespace Quarry.Migration;

/// <summary>
/// Immutable definition of a database column for snapshot comparison.
/// </summary>
public sealed class ColumnDef : IEquatable<ColumnDef>
{
    public string Name { get; }
    public string ClrType { get; }
    public bool IsNullable { get; }
    public ColumnKind Kind { get; }
    public bool IsIdentity { get; }
    public bool IsClientGenerated { get; }
    public bool IsComputed { get; }
    public int? MaxLength { get; }
    public int? Precision { get; }
    public int? Scale { get; }
    public bool HasDefault { get; }
    public string? DefaultExpression { get; }
    public string? MappedName { get; }
    public string? ReferencedEntityName { get; }
    public string? CustomTypeMapping { get; }

    public ColumnDef(
        string name,
        string clrType,
        bool isNullable,
        ColumnKind kind,
        bool isIdentity = false,
        bool isClientGenerated = false,
        bool isComputed = false,
        int? maxLength = null,
        int? precision = null,
        int? scale = null,
        bool hasDefault = false,
        string? defaultExpression = null,
        string? mappedName = null,
        string? referencedEntityName = null,
        string? customTypeMapping = null)
    {
        Name = name;
        ClrType = clrType;
        IsNullable = isNullable;
        Kind = kind;
        IsIdentity = isIdentity;
        IsClientGenerated = isClientGenerated;
        IsComputed = isComputed;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        HasDefault = hasDefault;
        DefaultExpression = defaultExpression;
        MappedName = mappedName;
        ReferencedEntityName = referencedEntityName;
        CustomTypeMapping = customTypeMapping;
    }

    public bool Equals(ColumnDef? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && ClrType == other.ClrType
            && IsNullable == other.IsNullable
            && Kind == other.Kind
            && IsIdentity == other.IsIdentity
            && IsClientGenerated == other.IsClientGenerated
            && IsComputed == other.IsComputed
            && MaxLength == other.MaxLength
            && Precision == other.Precision
            && Scale == other.Scale
            && HasDefault == other.HasDefault
            && DefaultExpression == other.DefaultExpression
            && MappedName == other.MappedName
            && ReferencedEntityName == other.ReferencedEntityName
            && CustomTypeMapping == other.CustomTypeMapping;
    }

    public override bool Equals(object? obj) => Equals(obj as ColumnDef);

    public override int GetHashCode()
    {
        var h1 = HashCode.Combine(Name, ClrType, IsNullable, Kind, IsIdentity, IsClientGenerated, IsComputed, MaxLength);
        var h2 = HashCode.Combine(Precision, Scale, HasDefault, DefaultExpression, MappedName, ReferencedEntityName, CustomTypeMapping);
        return HashCode.Combine(h1, h2);
    }
}
