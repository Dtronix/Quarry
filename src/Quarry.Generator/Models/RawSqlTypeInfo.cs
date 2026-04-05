using System;
using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Describes the resolved result type T for a RawSqlAsync&lt;T&gt; or RawSqlScalarAsync&lt;T&gt; call site.
/// </summary>
internal sealed class RawSqlTypeInfo : IEquatable<RawSqlTypeInfo>
{
    public RawSqlTypeInfo(
        string resultTypeName,
        RawSqlTypeKind typeKind,
        IReadOnlyList<RawSqlPropertyInfo> properties,
        bool hasCancellationToken = false,
        string? scalarReaderMethod = null,
        string? sqlLiteral = null)
    {
        ResultTypeName = resultTypeName;
        TypeKind = typeKind;
        Properties = properties;
        HasCancellationToken = hasCancellationToken;
        ScalarReaderMethod = scalarReaderMethod;
        SqlLiteral = sqlLiteral;
    }

    /// <summary>
    /// The fully qualified result type name (e.g., "UserDto", "int", "string").
    /// </summary>
    public string ResultTypeName { get; }

    /// <summary>
    /// The kind of result type (scalar, entity, or DTO).
    /// </summary>
    public RawSqlTypeKind TypeKind { get; }

    /// <summary>
    /// The properties of the result type. Empty for scalar types.
    /// </summary>
    public IReadOnlyList<RawSqlPropertyInfo> Properties { get; }

    /// <summary>
    /// Whether the called overload includes a CancellationToken parameter.
    /// </summary>
    public bool HasCancellationToken { get; }

    /// <summary>
    /// For scalar types, the DbDataReader method to call (e.g., "GetInt32", "GetString").
    /// </summary>
    public string? ScalarReaderMethod { get; }

    /// <summary>
    /// The raw SQL string literal from the call site, if the first argument was a compile-time constant.
    /// Used for compile-time column resolution (Phase 2 of #183).
    /// Null when the SQL is a variable, interpolated string, or otherwise non-literal.
    /// </summary>
    public string? SqlLiteral { get; }

    public bool Equals(RawSqlTypeInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ResultTypeName == other.ResultTypeName
            && TypeKind == other.TypeKind
            && HasCancellationToken == other.HasCancellationToken
            && ScalarReaderMethod == other.ScalarReaderMethod
            && SqlLiteral == other.SqlLiteral
            && EqualityHelpers.SequenceEqual(Properties, other.Properties);
    }

    public override bool Equals(object? obj) => Equals(obj as RawSqlTypeInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(ResultTypeName, TypeKind, HasCancellationToken, Properties.Count, SqlLiteral);
    }
}

/// <summary>
/// Classifies the result type T for code generation strategy.
/// </summary>
internal enum RawSqlTypeKind
{
    /// <summary>
    /// A scalar type (int, string, DateTime, etc.) — read via reader.GetXxx(0).
    /// </summary>
    Scalar,

    /// <summary>
    /// A known entity from Pipeline 1 — can be enriched with column metadata.
    /// </summary>
    Entity,

    /// <summary>
    /// An arbitrary DTO/POCO — properties discovered via semantic model.
    /// </summary>
    Dto
}

/// <summary>
/// Describes a single property on the result type for reader code generation.
/// </summary>
internal sealed class RawSqlPropertyInfo : IEquatable<RawSqlPropertyInfo>
{
    public RawSqlPropertyInfo(
        string propertyName,
        string clrType,
        string readerMethodName,
        bool isNullable,
        bool isEnum = false,
        string? fullClrType = null,
        string? customTypeMappingClass = null,
        string? dbReaderMethodName = null,
        bool isForeignKey = false,
        string? referencedEntityName = null)
    {
        PropertyName = propertyName;
        ClrType = clrType;
        ReaderMethodName = readerMethodName;
        IsNullable = isNullable;
        IsEnum = isEnum;
        FullClrType = fullClrType ?? clrType;
        CustomTypeMappingClass = customTypeMappingClass;
        DbReaderMethodName = dbReaderMethodName;
        IsForeignKey = isForeignKey;
        ReferencedEntityName = referencedEntityName;
    }

    /// <summary>
    /// The property name on the result type (e.g., "Name", "Email").
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The short CLR type (e.g., "int", "string", "DateTime").
    /// </summary>
    public string ClrType { get; }

    /// <summary>
    /// The fully qualified CLR type.
    /// </summary>
    public string FullClrType { get; }

    /// <summary>
    /// The DbDataReader method name (e.g., "GetInt32", "GetString").
    /// </summary>
    public string ReaderMethodName { get; }

    /// <summary>
    /// Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; }

    /// <summary>
    /// Whether the property is an enum type.
    /// </summary>
    public bool IsEnum { get; }

    /// <summary>
    /// The fully qualified custom TypeMapping class, if any.
    /// </summary>
    public string? CustomTypeMappingClass { get; }

    /// <summary>
    /// The DbDataReader method for reading the database type when CustomTypeMappingClass is set.
    /// </summary>
    public string? DbReaderMethodName { get; }

    /// <summary>
    /// Whether this property is a Ref&lt;TEntity, TKey&gt; foreign key.
    /// </summary>
    public bool IsForeignKey { get; }

    /// <summary>
    /// The referenced entity type name for FK properties.
    /// </summary>
    public string? ReferencedEntityName { get; }

    public bool Equals(RawSqlPropertyInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return PropertyName == other.PropertyName
            && ClrType == other.ClrType
            && FullClrType == other.FullClrType
            && ReaderMethodName == other.ReaderMethodName
            && IsNullable == other.IsNullable
            && IsEnum == other.IsEnum
            && CustomTypeMappingClass == other.CustomTypeMappingClass
            && DbReaderMethodName == other.DbReaderMethodName
            && IsForeignKey == other.IsForeignKey
            && ReferencedEntityName == other.ReferencedEntityName;
    }

    public override bool Equals(object? obj) => Equals(obj as RawSqlPropertyInfo);

    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ClrType, ReaderMethodName, IsNullable);
    }
}
