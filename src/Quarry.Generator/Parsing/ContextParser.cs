using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Parses classes decorated with QuarryContextAttribute to extract context configuration
/// and discover entity types.
/// </summary>
internal static class ContextParser
{
    private const string QuarryContextAttributeName = "Quarry.QuarryContextAttribute";
    private const string QuarryContextAttributeShortName = "QuarryContextAttribute";
    private const string QuarryContextAttributeWithoutSuffix = "QuarryContext";

    /// <summary>
    /// Checks if a class declaration has the QuarryContext attribute.
    /// </summary>
    public static bool HasQuarryContextAttribute(ClassDeclarationSyntax classDeclaration)
    {
        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = GetAttributeName(attr);
                return name == QuarryContextAttributeShortName
                    || name == QuarryContextAttributeWithoutSuffix
                    || name == QuarryContextAttributeName;
            });
    }

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        switch (attribute.Name)
        {
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text;
            case QualifiedNameSyntax qualified:
                return qualified.Right.Identifier.Text;
            default:
                return attribute.Name.ToString();
        }
    }

    /// <summary>
    /// Checks if the class inherits from QuarryContext.
    /// </summary>
    private static bool InheritsFromQuarryContext(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.ToDisplayString() == "Quarry.QuarryContext")
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Parses a context class to extract configuration and entity mappings.
    /// </summary>
    public static ContextInfo? ParseContext(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (classSymbol == null)
            return null;

        // Verify the class inherits from QuarryContext
        if (!InheritsFromQuarryContext(classSymbol))
            return null;

        // Extract attribute data
        var attributeData = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == QuarryContextAttributeName);

        if (attributeData == null)
            return null;

        // Extract Dialect and Schema from attribute
        var dialect = SqlDialect.SQLite; // Default
        string? schema = null;

        foreach (var namedArg in attributeData.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "Dialect":
                    if (namedArg.Value.Value is int dialectValue)
                        dialect = (SqlDialect)dialectValue;
                    break;
                case "Schema":
                    schema = namedArg.Value.Value as string;
                    break;
            }
        }

        // Discover entities via partial QueryBuilder<T> properties
        var (entities, mappings) = DiscoverEntities(classDeclaration, semanticModel, cancellationToken);

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : classSymbol.ContainingNamespace.ToDisplayString();

        return new ContextInfo(
            className: classSymbol.Name,
            @namespace: namespaceName,
            dialect: dialect,
            schema: schema,
            entities: entities,
            entityMappings: mappings,
            location: classDeclaration.GetLocation());
    }

    /// <summary>
    /// Discovers entities from partial QueryBuilder&lt;T&gt; properties on the context class.
    /// Returns both the entity list and the entity mappings with property names.
    /// </summary>
    private static (IReadOnlyList<EntityInfo> Entities, IReadOnlyList<EntityMapping> Mappings) DiscoverEntities(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var entities = new List<EntityInfo>();
        var mappings = new List<EntityMapping>();

        // Find all partial property declarations with QueryBuilder<T> type
        var properties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            .Where(p => IsQueryBuilderProperty(p));

        foreach (var property in properties)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityTypeName = ExtractEntityTypeName(property);
            if (entityTypeName == null)
                continue;

            // Find the corresponding schema class
            var schemaInfo = SchemaParser.FindAndParseSchema(
                entityTypeName,
                semanticModel,
                cancellationToken);

            if (schemaInfo != null)
            {
                entities.Add(schemaInfo);
                mappings.Add(new EntityMapping(property.Identifier.Text, schemaInfo));
            }
        }

        return (entities, mappings);
    }

    /// <summary>
    /// Checks if a property declaration is a QueryBuilder&lt;T&gt; property.
    /// </summary>
    private static bool IsQueryBuilderProperty(PropertyDeclarationSyntax property)
    {
        var genericName = property.Type as GenericNameSyntax;
        if (genericName == null)
            return false;

        return genericName.Identifier.Text is "QueryBuilder" or "IQueryBuilder";
    }

    /// <summary>
    /// Extracts the entity type name from a QueryBuilder&lt;T&gt; property.
    /// </summary>
    private static string? ExtractEntityTypeName(PropertyDeclarationSyntax property)
    {
        var genericName = property.Type as GenericNameSyntax;
        if (genericName == null)
            return null;

        if (genericName.TypeArgumentList.Arguments.Count != 1)
            return null;

        var typeArg = genericName.TypeArgumentList.Arguments[0];
        switch (typeArg)
        {
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text;
            case QualifiedNameSyntax qualified:
                return qualified.Right.Identifier.Text;
            default:
                return typeArg.ToString();
        }
    }
}
