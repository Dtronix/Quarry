using System;
using System.Collections.Generic;
using Quarry.Generators.Sql;

namespace Quarry.Generators.Models;

/// <summary>
/// Patch (partial update) column metadata for the runtime-assembled SET clause.
/// Attached to a <see cref="IR.BoundCallSite"/> when the call site is a
/// <c>Set(Entity.Patch)</c> or <c>Set(PatchAction&lt;Entity.Patch&gt;)</c> overload.
/// Mirrors the <see cref="WriteColumnInfo"/> shape for the column list — the per-column
/// metadata Phase 7's runtime binder needs (FK <c>.Id</c> extraction, enum cast,
/// custom mapper, sensitive redaction) is identical between insert and patch.
/// </summary>
internal sealed class PatchInfo : IEquatable<PatchInfo>
{
    /// <summary>
    /// Updatable columns on the entity (Identity and Computed columns excluded).
    /// Bit position in the Patch struct's <c>__mask</c> matches the index here.
    /// </summary>
    public IReadOnlyList<WriteColumnInfo> Columns { get; }

    /// <summary>
    /// True when the Patch call site uses the lambda overload
    /// (<c>Set(PatchAction&lt;Entity.Patch&gt;)</c>); false for the value overload
    /// (<c>Set(Entity.Patch)</c>).
    /// </summary>
    public bool IsLambdaForm { get; }

    /// <summary>
    /// The entity type name whose generated <c>Patch</c> nested struct is referenced
    /// (e.g. <c>"User"</c>). Used by emission to construct the carrier field type
    /// <c>User.Patch</c>.
    /// </summary>
    public string EntityTypeName { get; }

    /// <summary>
    /// The resolved name of the nested Patch struct on this entity — usually
    /// <c>"Patch"</c>, but auto-renamed to <c>"_Patch"</c> (or more underscores)
    /// when the entity has a column literally named "Patch". See
    /// <c>EntityCodeGenerator.ResolvePatchStructName</c> and QRY047.
    /// </summary>
    public string StructName { get; }

    public PatchInfo(string entityTypeName, bool isLambdaForm, IReadOnlyList<WriteColumnInfo> columns, string structName = "Patch")
    {
        EntityTypeName = entityTypeName;
        IsLambdaForm = isLambdaForm;
        Columns = columns;
        StructName = structName;
    }

    /// <summary>
    /// Builds a <see cref="PatchInfo"/> from an entity, applying the same
    /// Identity + Computed exclusion as <see cref="InsertInfo.FromEntityInfo"/>.
    /// The resulting column order — and therefore the Patch struct's mask bit
    /// positions — matches <see cref="EntityInfo.Columns"/> declaration order.
    /// </summary>
    public static PatchInfo FromEntityInfo(EntityInfo entity, SqlDialect dialect, bool isLambdaForm)
    {
        var columns = new List<WriteColumnInfo>();

        foreach (var column in entity.Columns)
        {
            if (column.Modifiers.IsComputed) continue;
            if (column.Modifiers.IsIdentity) continue;

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

        // Resolve the nested Patch struct name. Default "Patch"; auto-renames to
        // "_Patch" / "__Patch" / ... when a column collides. Null means even the
        // maximally-prefixed name collides — in that case the Patch struct isn't
        // emitted at all; falling back to "Patch" here keeps PatchInfo construction
        // total (downstream consumers never see this codepath because Phase 2's
        // self-suppression already short-circuited entity-level emission).
        var structName = Generation.EntityCodeGenerator.ResolvePatchStructName(entity) ?? "Patch";

        return new PatchInfo(entity.EntityName, isLambdaForm, columns, structName);
    }

    private static string FormatColumnName(string columnName, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"`{columnName}`",
            SqlDialect.SqlServer => $"[{columnName}]",
            _ => $"\"{columnName}\""
        };
    }

    public bool Equals(PatchInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EntityTypeName == other.EntityTypeName
            && IsLambdaForm == other.IsLambdaForm
            && StructName == other.StructName
            && EqualityHelpers.SequenceEqual(Columns, other.Columns);
    }

    public override bool Equals(object? obj) => Equals(obj as PatchInfo);

    public override int GetHashCode() => HashCode.Combine(EntityTypeName, IsLambdaForm, StructName, Columns.Count);
}
