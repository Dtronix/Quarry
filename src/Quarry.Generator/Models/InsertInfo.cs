using System;
using Quarry.Generators.Sql;
using Quarry;
namespace Quarry.Generators.Models;

/// <summary>
/// Contains insert operation metadata for interceptor generation.
/// </summary>
internal sealed class InsertInfo : IEquatable<InsertInfo>
{
    /// <summary>
    /// Gets the columns to insert (excluding Identity and Computed columns).
    /// </summary>
    public IReadOnlyList<InsertColumnInfo> Columns { get; }

    /// <summary>
    /// Gets the identity column name (for RETURNING clause), or null if none.
    /// This is the unquoted column name - the dialect will apply proper quoting.
    /// </summary>
    public string? IdentityColumnName { get; }

    /// <summary>
    /// Gets the identity column's property name, or null if none.
    /// </summary>
    public string? IdentityPropertyName { get; }

    /// <summary>
    /// Gets the dialect-quoted identity column name (for RETURNING/OUTPUT clause), or null if none.
    /// </summary>
    public string? QuotedIdentityColumnName { get; }

    public InsertInfo(
        IReadOnlyList<InsertColumnInfo> columns,
        string? identityColumnName,
        string? identityPropertyName,
        string? quotedIdentityColumnName = null)
    {
        Columns = columns;
        IdentityColumnName = identityColumnName;
        IdentityPropertyName = identityPropertyName;
        QuotedIdentityColumnName = quotedIdentityColumnName;
    }

    /// <summary>
    /// Creates InsertInfo from an EntityInfo, excluding Identity and Computed columns.
    /// </summary>
    public static InsertInfo FromEntityInfo(EntityInfo entity, SqlDialect dialect, System.Collections.Generic.HashSet<string>? initializedPropertyNames = null)
    {
        var columns = new List<InsertColumnInfo>();
        string? identityColumnName = null;
        string? identityPropertyName = null;

        foreach (var column in entity.Columns)
        {
            // Skip computed columns - they cannot be inserted
            if (column.Modifiers.IsComputed)
                continue;

            // Skip identity columns from the insert list
            if (column.Modifiers.IsIdentity)
            {
                // Remember the identity column for RETURNING clause (unquoted - dialect will quote it)
                identityColumnName = column.ColumnName;
                identityPropertyName = column.PropertyName;
                continue;
            }

            // Skip columns not explicitly set in the object initializer
            if (initializedPropertyNames != null && !initializedPropertyNames.Contains(column.PropertyName))
                continue;

            columns.Add(new InsertColumnInfo(
                propertyName: column.PropertyName,
                columnName: column.ColumnName,
                quotedColumnName: FormatColumnName(column.ColumnName, dialect),
                clrType: column.ClrType,
                fullClrType: column.FullClrType,
                isNullable: column.IsNullable,
                isValueType: column.IsValueType,
                isForeignKey: column.Modifiers.IsForeignKey,
                foreignKeyEntityName: column.ReferencedEntityName,
                customTypeMappingClass: column.CustomTypeMappingClass,
                isSensitive: column.Modifiers.IsSensitive));
        }

        string? quotedIdentityColumnName = identityColumnName != null
            ? FormatColumnName(identityColumnName, dialect)
            : null;

        return new InsertInfo(columns, identityColumnName, identityPropertyName, quotedIdentityColumnName);
    }

    /// <summary>
    /// Formats a column name with dialect-specific quoting.
    /// </summary>
    private static string FormatColumnName(string columnName, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"`{columnName}`",
            SqlDialect.SqlServer => $"[{columnName}]",
            _ => $"\"{columnName}\""  // SQLite, PostgreSQL
        };
    }

    public bool Equals(InsertInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IdentityColumnName == other.IdentityColumnName
            && IdentityPropertyName == other.IdentityPropertyName
            && QuotedIdentityColumnName == other.QuotedIdentityColumnName
            && EqualityHelpers.SequenceEqual(Columns, other.Columns);
    }

    public override bool Equals(object? obj) => Equals(obj as InsertInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(IdentityColumnName, IdentityPropertyName, Columns.Count);
    }
}

/// <summary>
/// Information about a column in an insert operation.
/// </summary>
internal sealed class InsertColumnInfo : IEquatable<InsertColumnInfo>
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
    /// When set, insert code should wrap the value with mapper.ToDb(value).
    /// </summary>
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// Gets whether this column contains sensitive data.
    /// When true, parameter values are redacted in log output.
    /// </summary>
    public bool IsSensitive { get; }

    public InsertColumnInfo(
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
        bool isSensitive = false)
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
    }

    public bool Equals(InsertColumnInfo? other)
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
            && IsSensitive == other.IsSensitive;
    }

    public override bool Equals(object? obj) => Equals(obj as InsertColumnInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ColumnName, ClrType, IsNullable);
    }
}
