using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Resolves Quarry entity schema definitions from a Roslyn compilation,
/// producing a <see cref="SchemaMap"/> that maps SQL table/column names to entity types and properties.
/// </summary>
internal sealed class SchemaResolver
{
    public SchemaMap Resolve(Compilation compilation)
    {
        var entities = new Dictionary<string, EntityMapping>(StringComparer.OrdinalIgnoreCase);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(classDecl);
                if (symbol == null || symbol.IsAbstract)
                    continue;

                if (!InheritsFromSchema(symbol))
                    continue;

                var mapping = ParseSchemaClass(classDecl, symbol, model);
                if (mapping != null)
                    entities[mapping.TableName] = mapping;
            }
        }

        return new SchemaMap(entities);
    }

    private static bool InheritsFromSchema(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.Name == "Schema" &&
                current.ContainingNamespace?.ToDisplayString() == "Quarry")
                return true;

            current = current.BaseType;
        }

        return false;
    }

    private static EntityMapping? ParseSchemaClass(
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol symbol,
        SemanticModel model)
    {
        var tableName = ExtractStaticStringProperty(classDecl, "Table");
        if (tableName == null)
            return null;

        var schemaName = ExtractStaticStringProperty(classDecl, "SchemaName");
        var namingStyle = ExtractNamingStyle(classDecl);
        var className = symbol.Name;
        var accessorName = DeriveAccessorName(className);

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax property)
                continue;

            // Skip static properties (Table, SchemaName)
            if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                continue;

            var propSymbol = model.GetDeclaredSymbol(property);
            if (propSymbol == null)
                continue;

            var propType = propSymbol.Type as INamedTypeSymbol;
            if (propType == null || !propType.IsGenericType)
                continue;

            var typeName = propType.Name;
            if (typeName != "Col" && typeName != "Key" && typeName != "Ref")
                continue;

            var propertyName = propSymbol.Name;
            var columnName = ExtractMapToName(property) ?? ApplyNamingStyle(propertyName, namingStyle);
            columns[columnName] = propertyName;
        }

        return new EntityMapping(tableName, schemaName, className, accessorName, columns);
    }

    private static string? ExtractStaticStringProperty(ClassDeclarationSyntax classDecl, string propertyName)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax property)
                continue;

            if (!property.Modifiers.Any(SyntaxKind.StaticKeyword))
                continue;

            if (property.Identifier.Text != propertyName)
                continue;

            // Expression body: public static string Table => "users";
            if (property.ExpressionBody?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
                return literal.Token.ValueText;
        }

        return null;
    }

    private static NamingStyle ExtractNamingStyle(ClassDeclarationSyntax classDecl)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax property)
                continue;

            if (property.Identifier.Text != "NamingStyle")
                continue;

            // Expression body: protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;
            if (property.ExpressionBody?.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var name = memberAccess.Name.Identifier.Text;
                return name switch
                {
                    "SnakeCase" => NamingStyle.SnakeCase,
                    "CamelCase" => NamingStyle.CamelCase,
                    "LowerCase" => NamingStyle.LowerCase,
                    _ => NamingStyle.Exact,
                };
            }
        }

        return NamingStyle.Exact;
    }

    private static string? ExtractMapToName(PropertyDeclarationSyntax property)
    {
        // Look for .MapTo("column_name") in the expression body
        var expression = property.ExpressionBody?.Expression;
        if (expression == null)
            return null;

        // Walk invocation chain looking for MapTo call
        return FindMapToInChain(expression);
    }

    private static string? FindMapToInChain(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            // Check if this invocation is MapTo(...)
            var methodName = GetMethodName(invocation);
            if (methodName == "MapTo" &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            // Check the receiver of this invocation (for chained calls)
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                return FindMapToInChain(memberAccess.Expression);
        }

        return null;
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            _ => null,
        };
    }

    internal static string DeriveAccessorName(string schemaClassName)
    {
        // Strip "Schema" suffix if present
        var baseName = schemaClassName.EndsWith("Schema", StringComparison.Ordinal)
            ? schemaClassName.Substring(0, schemaClassName.Length - "Schema".Length)
            : schemaClassName;

        // Simple pluralization: add "s" if not already ending in "s"
        if (!baseName.EndsWith("s", StringComparison.Ordinal))
            baseName += "s";

        return baseName;
    }

    internal static string ApplyNamingStyle(string propertyName, NamingStyle style)
    {
        switch (style)
        {
            case NamingStyle.SnakeCase:
                return ToSnakeCase(propertyName);
            case NamingStyle.CamelCase:
                return ToCamelCase(propertyName);
            case NamingStyle.LowerCase:
                return propertyName.ToLowerInvariant();
            default:
                return propertyName;
        }
    }

    internal static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var chars = new List<char>(input.Length + 4);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }

        return new string(chars.ToArray());
    }

    internal static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length == 1)
            return input.ToLowerInvariant();

        return char.ToLowerInvariant(input[0]) + input.Substring(1);
    }
}

internal enum NamingStyle
{
    Exact = 0,
    SnakeCase = 1,
    CamelCase = 2,
    LowerCase = 3,
}
