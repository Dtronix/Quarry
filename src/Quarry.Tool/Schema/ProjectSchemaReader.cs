using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Quarry.Shared.Migration;

namespace Quarry.Tool.Schema;

/// <summary>
/// Opens a user's project via Roslyn and extracts schema information.
/// </summary>
internal sealed class ProjectSchemaReader
{
    private static readonly object _locatorLock = new();
    private static bool _locatorRegistered;

    public static void EnsureLocatorRegistered()
    {
        if (_locatorRegistered) return;
        lock (_locatorLock)
        {
            if (!_locatorRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _locatorRegistered = true;
            }
        }
    }

    public static async Task<Compilation?> OpenProjectAsync(string csprojPath)
    {
        EnsureLocatorRegistered();
        using var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(csprojPath);
        return await project.GetCompilationAsync();
    }

    public static SchemaSnapshot ExtractSchemaSnapshot(Compilation compilation, int version, string name, int? parentVersion)
    {
        var tables = new List<TableDef>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            // Find classes that inherit from Quarry.Schema
            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl) as INamedTypeSymbol;
                if (symbol == null) continue;

                if (!InheritsFromSchema(symbol))
                    continue;

                var table = ExtractTableDef(symbol, model);
                if (table != null)
                    tables.Add(table);
            }
        }

        return new SchemaSnapshot(version, name, DateTimeOffset.UtcNow, parentVersion, tables);
    }

    private static bool InheritsFromSchema(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == "Quarry.Schema")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static TableDef? ExtractTableDef(INamedTypeSymbol schemaClass, SemanticModel model)
    {
        // Look for Table property
        string? tableName = null;
        string? characterSet = null;
        var namingStyle = NamingStyleKind.Exact;

        foreach (var member in schemaClass.GetMembers())
        {
            if (member is IPropertySymbol prop)
            {
                if (prop.Name == "Table" && prop.Type.SpecialType == SpecialType.System_String)
                {
                    // Try to get value from syntax
                    var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (syntax is PropertyDeclarationSyntax propSyntax)
                    {
                        var initializer = propSyntax.Initializer?.Value ??
                            (propSyntax.ExpressionBody?.Expression);
                        if (initializer is LiteralExpressionSyntax literal)
                        {
                            tableName = literal.Token.ValueText;
                        }
                    }
                }
                else if (prop.Name == "CharacterSet" && prop.Type.SpecialType == SpecialType.System_String)
                {
                    var syntax2 = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (syntax2 is PropertyDeclarationSyntax propSyntax2)
                    {
                        var initializer2 = propSyntax2.Initializer?.Value ?? propSyntax2.ExpressionBody?.Expression;
                        if (initializer2 is LiteralExpressionSyntax literal2)
                            characterSet = literal2.Token.ValueText;
                    }
                }
                else if (prop.Name == "Naming")
                {
                    var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    if (syntax is PropertyDeclarationSyntax propSyntax)
                    {
                        var expr = propSyntax.Initializer?.Value ?? propSyntax.ExpressionBody?.Expression;
                        if (expr != null)
                        {
                            var exprText = expr.ToString();
                            if (exprText.Contains("SnakeCase")) namingStyle = NamingStyleKind.SnakeCase;
                            else if (exprText.Contains("CamelCase")) namingStyle = NamingStyleKind.CamelCase;
                            else if (exprText.Contains("LowerCase")) namingStyle = NamingStyleKind.LowerCase;
                        }
                    }
                }
            }
        }

        if (tableName == null) return null;

        var columns = new List<ColumnDef>();
        var foreignKeys = new List<ForeignKeyDef>();
        var indexes = new List<IndexDef>();

        foreach (var member in schemaClass.GetMembers())
        {
            if (member is IPropertySymbol colProp && colProp.Type is INamedTypeSymbol colType)
            {
                var typeName = colType.Name;
                if (typeName is "Col" or "Key" or "Ref" && colType.IsGenericType)
                {
                    var colDef = ExtractColumnDef(colProp, colType, namingStyle);
                    if (colDef != null) columns.Add(colDef);

                    if (typeName == "Ref" && colType.TypeArguments.Length >= 1)
                    {
                        var refEntityType = colType.TypeArguments[0] as INamedTypeSymbol;
                        var refEntity = refEntityType?.Name ?? colType.TypeArguments[0].Name;
                        var colName = NamingConventions.ToColumnName(colProp.Name, namingStyle);

                        // Resolve PK column from referenced entity
                        var refPkColumn = ResolvePrimaryKeyColumn(refEntityType, namingStyle);

                        // Resolve table name from referenced entity
                        var refTableName = ResolveTableName(refEntityType) ?? refEntity;

                        foreignKeys.Add(new ForeignKeyDef(
                            $"FK_{tableName}_{colName}",
                            colName,
                            refTableName,
                            refPkColumn,
                            Shared.Migration.ForeignKeyAction.NoAction,
                            Shared.Migration.ForeignKeyAction.NoAction));
                    }
                }
                else if (typeName == "Index" && colType.ToDisplayString() == "Quarry.Index")
                {
                    var indexDef = ExtractIndexDef(colProp, namingStyle);
                    if (indexDef != null) indexes.Add(indexDef);
                }
            }
        }

        // Detect CompositeKey properties
        List<string>? compositeKeyColumns = null;
        foreach (var member in schemaClass.GetMembers())
        {
            if (member is IPropertySymbol ckProp && ckProp.Type is INamedTypeSymbol ckType)
            {
                if (ckType.Name == "CompositeKey" && ckType.ToDisplayString() == "Quarry.CompositeKey")
                {
                    compositeKeyColumns = ExtractCompositeKeyColumns(ckProp, namingStyle);
                    break;
                }
            }
        }

        return new TableDef(tableName, null, namingStyle, columns, foreignKeys, indexes, compositeKeyColumns, characterSet);
    }

    private static string ResolvePrimaryKeyColumn(INamedTypeSymbol? entityType, NamingStyleKind namingStyle)
    {
        if (entityType == null)
            return NamingConventions.ToColumnName("Id", namingStyle);

        foreach (var member in entityType.GetMembers())
        {
            if (member is IPropertySymbol prop && prop.Type is INamedTypeSymbol propType)
            {
                if (propType.Name == "Key" && propType.IsGenericType)
                    return NamingConventions.ToColumnName(prop.Name, namingStyle);
            }
        }

        return NamingConventions.ToColumnName("Id", namingStyle);
    }

    private static string? ResolveTableName(INamedTypeSymbol? entityType)
    {
        if (entityType == null) return null;

        foreach (var member in entityType.GetMembers())
        {
            if (member is IPropertySymbol prop && prop.Name == "Table" && prop.Type.SpecialType == SpecialType.System_String)
            {
                var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (syntax is PropertyDeclarationSyntax propSyntax)
                {
                    var initializer = propSyntax.Initializer?.Value ?? propSyntax.ExpressionBody?.Expression;
                    if (initializer is LiteralExpressionSyntax literal)
                        return literal.Token.ValueText;
                }
            }
        }

        return null;
    }

    private static ColumnDef? ExtractColumnDef(IPropertySymbol prop, INamedTypeSymbol colType, NamingStyleKind namingStyle)
    {
        var typeName = colType.Name;
        var kind = typeName switch
        {
            "Key" => ColumnKind.PrimaryKey,
            "Ref" => ColumnKind.ForeignKey,
            _ => ColumnKind.Standard
        };

        ITypeSymbol valueType;
        string? referencedEntity = null;

        if (typeName == "Ref" && colType.TypeArguments.Length >= 2)
        {
            referencedEntity = colType.TypeArguments[0].Name;
            valueType = colType.TypeArguments[1];
        }
        else if (colType.TypeArguments.Length >= 1)
        {
            valueType = colType.TypeArguments[0];
        }
        else
        {
            return null;
        }

        var clrType = valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var isNullable = valueType.NullableAnnotation == NullableAnnotation.Annotated ||
            (valueType is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

        if (isNullable && clrType.EndsWith("?"))
            clrType = clrType.TrimEnd('?');

        var columnName = NamingConventions.ToColumnName(prop.Name, namingStyle);

        // Walk fluent chain for additional modifiers
        string? computedExpression = null;
        string? collation = null;

        var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is PropertyDeclarationSyntax propSyntax)
        {
            ExpressionSyntax? expression = propSyntax.ExpressionBody?.Expression;
            if (expression == null && propSyntax.AccessorList != null)
            {
                var getter = propSyntax.AccessorList.Accessors
                    .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                expression = getter?.ExpressionBody?.Expression;
            }

            var current = expression;
            while (current is InvocationExpressionSyntax invocation)
            {
                var methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                };

                if (methodName == "Computed" && invocation.ArgumentList.Arguments.Count > 0)
                {
                    var arg = invocation.ArgumentList.Arguments[0].Expression;
                    if (arg is LiteralExpressionSyntax literal)
                        computedExpression = literal.Token.ValueText;
                }
                else if (methodName == "Collation" && invocation.ArgumentList.Arguments.Count > 0)
                {
                    var arg = invocation.ArgumentList.Arguments[0].Expression;
                    if (arg is LiteralExpressionSyntax literal)
                        collation = literal.Token.ValueText;
                }

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    current = memberAccess.Expression;
                else
                    current = null;
            }
        }

        return new ColumnDef(
            name: columnName,
            clrType: clrType,
            isNullable: isNullable,
            kind: kind,
            referencedEntityName: referencedEntity,
            computedExpression: computedExpression,
            collation: collation);
    }

    private static List<string>? ExtractCompositeKeyColumns(IPropertySymbol prop, NamingStyleKind namingStyle)
    {
        var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not PropertyDeclarationSyntax propSyntax)
            return null;

        ExpressionSyntax? expression = propSyntax.ExpressionBody?.Expression;
        if (expression == null && propSyntax.AccessorList != null)
        {
            var getter = propSyntax.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            expression = getter?.ExpressionBody?.Expression;
        }

        if (expression is not InvocationExpressionSyntax invocation)
            return null;

        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };

        if (methodName != "PrimaryKey")
            return null;

        var columns = new List<string>();
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is IdentifierNameSyntax colId)
                columns.Add(NamingConventions.ToColumnName(colId.Identifier.Text, namingStyle));
        }

        return columns.Count >= 2 ? columns : null;
    }

    private static IndexDef? ExtractIndexDef(IPropertySymbol prop, NamingStyleKind namingStyle)
    {
        var indexName = prop.Name;

        // Get the expression from the property syntax
        var syntax = prop.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not PropertyDeclarationSyntax propSyntax)
            return null;

        ExpressionSyntax? expression = propSyntax.ExpressionBody?.Expression;
        if (expression == null && propSyntax.AccessorList != null)
        {
            var getter = propSyntax.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            expression = getter?.ExpressionBody?.Expression;
        }

        if (expression == null)
            return null;

        // Walk the fluent method chain from outermost to innermost
        var columnNames = new List<string>();
        var isUnique = false;
        string? filter = null;
        string? method = null;
        InvocationExpressionSyntax? indexInvocation = null;

        var current = expression;
        while (current is InvocationExpressionSyntax invocation)
        {
            var methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            };

            switch (methodName)
            {
                case "Unique":
                    isUnique = true;
                    break;
                case "Using":
                    if (invocation.ArgumentList.Arguments.Count > 0 &&
                        invocation.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax usingAccess)
                    {
                        method = usingAccess.Name.Identifier.Text;
                    }
                    break;
                case "Where":
                    if (invocation.ArgumentList.Arguments.Count > 0)
                    {
                        var arg = invocation.ArgumentList.Arguments[0].Expression;
                        if (arg is LiteralExpressionSyntax literal)
                            filter = literal.Token.ValueText;
                        else if (arg is IdentifierNameSyntax filterId)
                            filter = NamingConventions.ToColumnName(filterId.Identifier.Text, namingStyle) + " = TRUE";
                    }
                    break;
                case "Include":
                    // Include columns are not tracked in IndexDef — skip
                    break;
                case "Index":
                    indexInvocation = invocation;
                    break;
            }

            // Move inward through the chain
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                current = memberAccess.Expression;
            else
                current = null;
        }

        // Parse the Index() arguments for columns and sort directions
        var descendingFlags = new List<bool>();
        if (indexInvocation != null)
        {
            foreach (var arg in indexInvocation.ArgumentList.Arguments)
            {
                var argExpr = arg.Expression;

                // Direct property reference: Index(Email) — ascending by default
                if (argExpr is IdentifierNameSyntax colId)
                {
                    columnNames.Add(NamingConventions.ToColumnName(colId.Identifier.Text, namingStyle));
                    descendingFlags.Add(false);
                }
                // Property with direction: Index(Email.Desc()) or Index(Email.Asc())
                else if (argExpr is InvocationExpressionSyntax dirInvocation &&
                         dirInvocation.Expression is MemberAccessExpressionSyntax dirAccess &&
                         dirAccess.Expression is IdentifierNameSyntax colRef)
                {
                    columnNames.Add(NamingConventions.ToColumnName(colRef.Identifier.Text, namingStyle));
                    var dirMethodName = dirAccess.Name.Identifier.Text;
                    descendingFlags.Add(dirMethodName == "Desc");
                }
            }
        }

        if (columnNames.Count == 0)
            return null;

        // Only include descending flags if any column is descending
        bool[]? descArray = descendingFlags.Any(d => d) ? descendingFlags.ToArray() : null;

        return new IndexDef(indexName, columnNames, isUnique, filter, method, descArray);
    }
}
