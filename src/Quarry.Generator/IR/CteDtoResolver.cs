using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.IR;

/// <summary>
/// Resolves a CTE DTO type symbol to <see cref="CteColumn"/> metadata.
/// The DTO's public instance properties with getters and setters become CTE columns.
/// No schema attribute is required — the DTO is a plain C# class.
/// </summary>
internal static class CteDtoResolver
{
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
