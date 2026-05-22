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
    public IReadOnlyList<WriteColumnInfo> Columns { get; }

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
        IReadOnlyList<WriteColumnInfo> columns,
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
        var columns = new List<WriteColumnInfo>();
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

            columns.Add(new WriteColumnInfo(
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
                isSensitive: column.Modifiers.IsSensitive,
                isEnum: column.IsEnum,
                isBoolean: column.ClrType is "bool" or "Boolean",
                enumUnderlyingType: column.IsEnum ? (column.DbClrType ?? "int") : null));
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
