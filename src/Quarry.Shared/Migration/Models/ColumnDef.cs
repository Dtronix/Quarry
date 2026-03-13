using System;

namespace Quarry.Shared.Migration;

/// <summary>
/// Immutable definition of a database column for snapshot comparison.
/// </summary>
#if QUARRY_GENERATOR
internal
#else
public
#endif
sealed class ColumnDef : IEquatable<ColumnDef>
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
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (Name?.GetHashCode() ?? 0);
            hash = hash * 31 + (ClrType?.GetHashCode() ?? 0);
            hash = hash * 31 + IsNullable.GetHashCode();
            hash = hash * 31 + Kind.GetHashCode();
            hash = hash * 31 + IsIdentity.GetHashCode();
            hash = hash * 31 + IsClientGenerated.GetHashCode();
            hash = hash * 31 + IsComputed.GetHashCode();
            hash = hash * 31 + (MaxLength?.GetHashCode() ?? 0);
            hash = hash * 31 + (Precision?.GetHashCode() ?? 0);
            hash = hash * 31 + (Scale?.GetHashCode() ?? 0);
            hash = hash * 31 + HasDefault.GetHashCode();
            hash = hash * 31 + (DefaultExpression?.GetHashCode() ?? 0);
            hash = hash * 31 + (MappedName?.GetHashCode() ?? 0);
            hash = hash * 31 + (ReferencedEntityName?.GetHashCode() ?? 0);
            hash = hash * 31 + (CustomTypeMapping?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
