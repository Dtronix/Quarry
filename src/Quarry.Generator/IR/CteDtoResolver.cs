using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Generators.IR;

/// <summary>
/// Resolves a CTE DTO type symbol to column metadata and a pseudo-EntityInfo.
/// The DTO's public instance properties with getters and setters become CTE columns.
/// No schema attribute is required — the DTO is a plain C# class.
/// </summary>
internal static class CteDtoResolver
{
    /// <summary>
    /// Builds a pseudo-EntityInfo from a DTO type symbol.
    /// The entity name and table name are both set to the DTO class name
    /// (CTE name = DTO class name in the generated SQL).
    /// </summary>
    // TODO: Wire up for CTE+Join support — currently unused because CTE+Join chains
    // are blocked by the QuarryContext.With() return type limitation (see workflow.md).
    // This method will be needed when Join<CteDto>() requires EntityInfo for the CTE DTO.
    public static EntityInfo? Resolve(INamedTypeSymbol dtoType)
    {
        if (dtoType == null)
            return null;

        var className = dtoType.Name;
        var columns = new List<ColumnInfo>();

        foreach (var member in dtoType.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            // Only include public instance properties with getter and setter
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (property.IsStatic || property.IsIndexer)
                continue;
            if (property.GetMethod == null || property.SetMethod == null)
                continue;

            var propType = property.Type;
            var (isValueType, readerMethod, isEnum) = ColumnInfo.GetTypeMetadata(propType);

            var isNullable = propType.NullableAnnotation == NullableAnnotation.Annotated
                || (propType is INamedTypeSymbol named
                    && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

            var clrType = GetSimpleTypeName(propType);
            var fullClrType = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            columns.Add(new ColumnInfo(
                propertyName: property.Name,
                columnName: property.Name, // CTE DTOs use property name as column name
                clrType: clrType,
                fullClrType: fullClrType,
                isNullable: isNullable,
                kind: ColumnKind.Standard,
                referencedEntityName: null,
                modifiers: new ColumnModifiers(),
                isValueType: isValueType,
                readerMethodName: readerMethod,
                isEnum: isEnum));
        }

        if (columns.Count == 0)
            return null;

        return new EntityInfo(
            entityName: className,
            schemaClassName: className, // No schema class for DTOs
            schemaNamespace: dtoType.ContainingNamespace?.ToDisplayString() ?? "",
            tableName: className, // CTE name = class name
            namingStyle: NamingStyleKind.Exact, // DTOs use exact property names
            columns: columns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None);
    }

    /// <summary>
    /// Builds CteColumn metadata from a DTO type symbol.
    /// </summary>
    public static IReadOnlyList<CteColumn> ResolveColumns(INamedTypeSymbol dtoType)
    {
        var columns = new List<CteColumn>();

        foreach (var member in dtoType.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (property.IsStatic || property.IsIndexer)
                continue;
            if (property.GetMethod == null || property.SetMethod == null)
                continue;

            columns.Add(new CteColumn(
                propertyName: property.Name,
                columnName: property.Name,
                clrType: GetSimpleTypeName(property.Type)));
        }

        return columns;
    }

    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        // Use the minimal display format for common types
        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
