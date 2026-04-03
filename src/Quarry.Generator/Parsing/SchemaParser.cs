using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Parses schema classes to extract column and relationship metadata.
/// </summary>
internal static class SchemaParser
{
    private const string SchemaBaseTypeName = "Quarry.Schema";

    /// <summary>
    /// Finds and parses a schema class for the given entity type.
    /// </summary>
    public static EntityInfo? FindAndParseSchema(
        string entityTypeName,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Convention: Schema class is named {EntityName}Schema
        var schemaClassName = entityTypeName + "Schema";

        // Search for the schema class in the compilation
        var compilation = semanticModel.Compilation;
        var schemaSymbol = FindSchemaType(compilation, schemaClassName);

        if (schemaSymbol == null)
            return null;

        // Find the syntax node for the schema class
        var syntaxRef = schemaSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return null;

        var classSyntax = syntaxRef.GetSyntax(cancellationToken) as ClassDeclarationSyntax;
        if (classSyntax == null)
            return null;

        // Get semantic model for the schema class's syntax tree
        var schemaSemanticModel = compilation.GetSemanticModel(classSyntax.SyntaxTree);

        return ParseSchemaClass(entityTypeName, schemaSymbol, classSyntax, schemaSemanticModel, cancellationToken);
    }

    /// <summary>
    /// Finds a schema type by name in the compilation.
    /// </summary>
    private static INamedTypeSymbol? FindSchemaType(Compilation compilation, string schemaClassName)
    {
        // Search all types in the compilation
        var visitor = new SchemaTypeFinder(schemaClassName);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            visitor.Visit(syntaxTree.GetRoot());
        }

        foreach (var candidate in visitor.Candidates)
        {
            var candidateSemanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            var symbol = candidateSemanticModel.GetDeclaredSymbol(candidate) as INamedTypeSymbol;
            if (symbol == null)
                continue;

            // Check if it inherits from Schema
            if (InheritsFromSchema(symbol))
                return symbol;
        }

        return null;
    }

    /// <summary>
    /// Checks if a type inherits from Schema.
    /// </summary>
    private static bool InheritsFromSchema(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == SchemaBaseTypeName)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Parses a schema class to extract metadata.
    /// </summary>
    private static EntityInfo ParseSchemaClass(
        string entityTypeName,
        INamedTypeSymbol schemaSymbol,
        ClassDeclarationSyntax classSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Extract table name from static Table property
        var tableName = ExtractTableName(schemaSymbol) ?? entityTypeName.ToLowerInvariant() + "s";

        // Extract naming style from NamingStyle property override
        var namingStyle = ExtractNamingStyle(schemaSymbol);

        // Parse columns, navigations, indexes, and composite keys
        var columns = new List<ColumnInfo>();
        var navigations = new List<NavigationInfo>();
        var singleNavigations = new List<SingleNavigationInfo>();
        var throughNavigations = new List<ThroughNavigationInfo>();
        var indexes = new List<IndexInfo>();
        List<string>? compositeKeyColumns = null;

        foreach (var member in classSyntax.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var property = member as PropertyDeclarationSyntax;
            if (property == null)
                continue;

            var propertySymbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol == null)
                continue;

            // Skip static properties (Table, SchemaName)
            if (propertySymbol.IsStatic)
                continue;

            // Parse based on property type
            ColumnInfo? columnInfo;
            if (TryParseColumn(property, propertySymbol, namingStyle, semanticModel, out columnInfo) && columnInfo != null)
            {
                columns.Add(columnInfo);
            }
            else
            {
                NavigationInfo? navigationInfo;
                if (TryParseNavigation(property, propertySymbol, out navigationInfo) && navigationInfo != null)
                {
                    navigations.Add(navigationInfo);
                }
                else
                {
                    IndexInfo? indexInfo;
                    if (TryParseIndex(property, propertySymbol, out indexInfo) && indexInfo != null)
                    {
                        indexes.Add(indexInfo);
                    }
                    else
                    {
                        List<string>? ckColumns;
                        if (TryParseCompositeKey(property, propertySymbol, out ckColumns) && ckColumns != null)
                        {
                            compositeKeyColumns = ckColumns;
                        }
                    }
                }
            }
        }

        // Second pass: parse One<T> and HasManyThrough navigations (requires columns to be populated)
        var parseDiagnostics = new List<Diagnostic>();
        foreach (var member in classSyntax.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var property = member as PropertyDeclarationSyntax;
            if (property == null)
                continue;

            var propertySymbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
            if (propertySymbol == null || propertySymbol.IsStatic)
                continue;

            if (TryParseSingleNavigation(property, propertySymbol, columns, out var singleNav, parseDiagnostics, schemaSymbol.Name) && singleNav != null)
            {
                singleNavigations.Add(singleNav);
            }
            else if (TryParseThroughNavigation(property, propertySymbol, out var throughNav) && throughNav != null)
            {
                throughNavigations.Add(throughNav);
            }
        }

        var namespaceName = schemaSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : schemaSymbol.ContainingNamespace.ToDisplayString();

        // Check for [EntityReader] attribute on the schema class
        var entityReaderResolution = ResolveEntityReaderAttribute(schemaSymbol, entityTypeName);

        return new EntityInfo(
            entityName: entityTypeName,
            schemaClassName: schemaSymbol.Name,
            schemaNamespace: namespaceName,
            tableName: tableName,
            namingStyle: namingStyle,
            columns: columns,
            navigations: navigations,
            indexes: indexes,
            location: classSyntax.GetLocation(),
            customEntityReaderClass: entityReaderResolution is { IsValid: true } ? entityReaderResolution.ReaderClassFqn : null,
            invalidEntityReaderClass: entityReaderResolution is { IsValid: false } ? entityReaderResolution.ReaderClassFqn : null,
            compositeKeyColumns: compositeKeyColumns,
            singleNavigations: singleNavigations,
            throughNavigations: throughNavigations,
            diagnostics: parseDiagnostics.Count > 0 ? parseDiagnostics : null);
    }

    /// <summary>
    /// Extracts the table name from the schema's static Table property.
    /// </summary>
    private static string? ExtractTableName(INamedTypeSymbol schemaSymbol)
    {
        var tableProperty = schemaSymbol.GetMembers("Table")
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.IsStatic);

        if (tableProperty == null)
            return null;

        // Try to extract the string value from the property getter
        var syntaxRef = tableProperty.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return null;

        var propSyntax = syntaxRef.GetSyntax() as PropertyDeclarationSyntax;
        if (propSyntax == null)
            return null;

        // Handle expression body: public static string Table => "users";
        var expressionBody = propSyntax.ExpressionBody;
        if (expressionBody != null)
        {
            var literal = expressionBody.Expression as LiteralExpressionSyntax;
            if (literal != null)
            {
                return literal.Token.ValueText;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the naming style from the schema's NamingStyle property override.
    /// </summary>
    private static NamingStyleKind ExtractNamingStyle(INamedTypeSymbol schemaSymbol)
    {
        var namingStyleProperty = schemaSymbol.GetMembers("NamingStyle")
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic && p.IsOverride);

        if (namingStyleProperty == null)
            return NamingStyleKind.Exact;

        // Try to extract the enum value from the property getter
        var syntaxRef = namingStyleProperty.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return NamingStyleKind.Exact;

        var propSyntax = syntaxRef.GetSyntax() as PropertyDeclarationSyntax;
        if (propSyntax == null)
            return NamingStyleKind.Exact;

        // Handle expression body: protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;
        var expressionBody = propSyntax.ExpressionBody;
        if (expressionBody != null)
        {
            var memberAccess = expressionBody.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                var valueName = memberAccess.Name.Identifier.Text;
                switch (valueName)
                {
                    case "Exact": return NamingStyleKind.Exact;
                    case "SnakeCase": return NamingStyleKind.SnakeCase;
                    case "CamelCase": return NamingStyleKind.CamelCase;
                    case "LowerCase": return NamingStyleKind.LowerCase;
                    default: return NamingStyleKind.Exact;
                }
            }
        }

        return NamingStyleKind.Exact;
    }

    /// <summary>
    /// Resolves the [EntityReader] attribute on a schema class, if present.
    /// Returns the result including FQN and validity. Returns null if no attribute found.
    /// </summary>
    internal static EntityReaderResolution? ResolveEntityReaderAttribute(INamedTypeSymbol schemaSymbol, string entityTypeName)
    {
        foreach (var attr in schemaSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is not ("EntityReaderAttribute" or "EntityReader"))
                continue;

            // Match by full name to avoid false positives
            var attrFullName = attr.AttributeClass.ToDisplayString();
            if (attrFullName != "Quarry.EntityReaderAttribute")
                continue;

            // Extract the Type argument
            if (attr.ConstructorArguments.Length < 1 || attr.ConstructorArguments[0].Value is not INamedTypeSymbol readerTypeSymbol)
                return null;

            var readerFqn = readerTypeSymbol.ToDisplayString();

            // Validate: must inherit EntityReader<T> where T matches entityTypeName
            var baseType = readerTypeSymbol.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType &&
                    baseType.OriginalDefinition.ToDisplayString() == "Quarry.EntityReader<T>")
                {
                    var typeArg = baseType.TypeArguments[0];
                    if (typeArg.Name == entityTypeName)
                        return new EntityReaderResolution(readerFqn, isValid: true);

                    return new EntityReaderResolution(readerFqn, isValid: false);
                }
                baseType = baseType.BaseType;
            }

            return new EntityReaderResolution(readerFqn, isValid: false);
        }

        return null;
    }

    /// <summary>
    /// Result of resolving an [EntityReader] attribute.
    /// </summary>
    internal sealed class EntityReaderResolution
    {
        public EntityReaderResolution(string readerClassFqn, bool isValid)
        {
            ReaderClassFqn = readerClassFqn;
            IsValid = isValid;
        }

        public string ReaderClassFqn { get; }
        public bool IsValid { get; }
    }

    /// <summary>
    /// Tries to parse a column property.
    /// </summary>
    private static bool TryParseColumn(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        NamingStyleKind namingStyle,
        SemanticModel semanticModel,
        out ColumnInfo? columnInfo)
    {
        columnInfo = null;

        var propertyType = propertySymbol.Type;
        var namedType = propertyType as INamedTypeSymbol;
        if (namedType == null)
            return false;

        var typeName = namedType.Name;

        ColumnKind columnKind;
        string clrType;
        string fullClrType;
        bool isNullable;
        string? referencedEntityName = null;
        ITypeSymbol valueType;

        // Determine column kind and extract type info
        if (typeName == "Col" && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            columnKind = ColumnKind.Standard;
            valueType = namedType.TypeArguments[0];
            ExtractTypeInfo(valueType, out clrType, out fullClrType, out isNullable);
        }
        else if (typeName == "Key" && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            columnKind = ColumnKind.PrimaryKey;
            valueType = namedType.TypeArguments[0];
            ExtractTypeInfo(valueType, out clrType, out fullClrType, out isNullable);
        }
        else if (typeName == "Ref" && namedType.IsGenericType && namedType.TypeArguments.Length == 2)
        {
            columnKind = ColumnKind.ForeignKey;
            var entityType = namedType.TypeArguments[0];
            valueType = namedType.TypeArguments[1];
            referencedEntityName = NormalizeEntityName(entityType);
            ExtractTypeInfo(valueType, out clrType, out fullClrType, out isNullable);
        }
        else
        {
            return false;
        }

        // Parse modifiers from property expression
        var modifiers = ParseColumnModifiers(property);

        // Determine column name
        var columnName = modifiers.MappedName ?? NamingConventions.ToColumnName(propertySymbol.Name, namingStyle);

        // Get type metadata from the type symbol
        var (isValueTypeFlag, readerMethodName, isEnum) = ColumnInfo.GetTypeMetadata(valueType);

        // Resolve custom type mapping if present
        string? customTypeMappingClass = null;
        string? dbClrType = null;
        string? dbReaderMethodName = null;
        string? mappingMismatchExpectedType = null;

        if (modifiers.CustomTypeMapping != null)
        {
            var mappingInfo = ResolveMappingType(property, semanticModel);
            if (mappingInfo != null)
            {
                customTypeMappingClass = mappingInfo.Value.MappingClassFqn;
                dbClrType = mappingInfo.Value.DbClrType;
                dbReaderMethodName = mappingInfo.Value.DbReaderMethodName;

                // Validate TCustom matches the column's declared type (strip nullability for comparison)
                var columnValueType = valueType;
                if (columnValueType is INamedTypeSymbol nt && nt.IsGenericType && nt.Name == "Nullable")
                    columnValueType = nt.TypeArguments[0];

                var mappingCustomType = mappingInfo.Value.CustomTypeSymbol;

                if (!SymbolEqualityComparer.Default.Equals(columnValueType, mappingCustomType))
                {
                    mappingMismatchExpectedType = GetTypeAlias(mappingCustomType.ToDisplayString());
                }
            }
        }

        columnInfo = new ColumnInfo(
            propertyName: propertySymbol.Name,
            columnName: columnName,
            clrType: clrType,
            fullClrType: fullClrType,
            isNullable: isNullable,
            kind: columnKind,
            referencedEntityName: referencedEntityName,
            modifiers: modifiers,
            isValueType: isValueTypeFlag,
            readerMethodName: readerMethodName,
            isEnum: isEnum,
            customTypeMappingClass: customTypeMappingClass,
            dbClrType: dbClrType,
            dbReaderMethodName: dbReaderMethodName,
            mappingMismatchExpectedType: mappingMismatchExpectedType);

        return true;
    }

    /// <summary>
    /// Resolved mapping type metadata.
    /// </summary>
    private readonly struct MappingTypeResolution
    {
        public MappingTypeResolution(string mappingClassFqn, string dbClrType, string dbReaderMethodName, ITypeSymbol customTypeSymbol)
        {
            MappingClassFqn = mappingClassFqn;
            DbClrType = dbClrType;
            DbReaderMethodName = dbReaderMethodName;
            CustomTypeSymbol = customTypeSymbol;
        }

        public string MappingClassFqn { get; }
        public string DbClrType { get; }
        public string DbReaderMethodName { get; }
        public ITypeSymbol CustomTypeSymbol { get; }
    }

    /// <summary>
    /// Resolves the TypeMapping generic arguments by walking the invocation chain
    /// to find the Mapped&lt;TMapping&gt;() call and using the semantic model to resolve TDb.
    /// </summary>
    private static MappingTypeResolution? ResolveMappingType(
        PropertyDeclarationSyntax property,
        SemanticModel semanticModel)
    {
        // Walk the expression chain to find the Mapped<>() invocation
        ExpressionSyntax? expression = property.ExpressionBody?.Expression;
        if (expression == null && property.AccessorList != null)
        {
            var getter = property.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            expression = getter?.ExpressionBody?.Expression;
        }

        // Walk invocation chain to find Mapped<TMapping>() or Mapped<T, TMapping>()
        var mappingTypeSyntax = FindMappingTypeArgument(expression);
        if (mappingTypeSyntax == null)
            return null;

        // Resolve the type argument via semantic model
        var typeInfo = semanticModel.GetTypeInfo(mappingTypeSyntax);
        var mappingTypeSymbol = typeInfo.Type as INamedTypeSymbol;
        if (mappingTypeSymbol == null)
        {
            // Try symbol info as fallback
            var symbolInfo = semanticModel.GetSymbolInfo(mappingTypeSyntax);
            mappingTypeSymbol = symbolInfo.Symbol as INamedTypeSymbol;
        }

        if (mappingTypeSymbol == null)
            return null;

        // Walk base types to find TypeMapping<TCustom, TDb>
        var mappingTypes = ExtractTypesFromMappingHierarchy(mappingTypeSymbol);
        if (mappingTypes == null)
            return null;

        var (customType, dbType) = mappingTypes.Value;
        var fqn = mappingTypeSymbol.ToDisplayString();
        var dbClrType = GetTypeAlias(dbType.ToDisplayString());
        var dbReaderMethod = GetDbReaderMethodFromTypeSymbol(dbType);

        return new MappingTypeResolution(fqn, dbClrType, dbReaderMethod, customType);
    }

    /// <summary>
    /// Walks an invocation expression chain to find the Mapped&lt;&gt;() call and
    /// returns the TMapping type argument syntax node.
    /// </summary>
    private static TypeSyntax? FindMappingTypeArgument(ExpressionSyntax? expression)
    {
        while (expression != null)
        {
            var invocation = expression as InvocationExpressionSyntax;
            if (invocation == null)
                break;

            var methodName = GetMethodName(invocation);
            if (methodName == "Mapped")
            {
                // Extract the type argument for TMapping
                GenericNameSyntax? generic = invocation.Expression as GenericNameSyntax;
                if (generic == null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    generic = memberAccess.Name as GenericNameSyntax;

                if (generic != null && generic.TypeArgumentList.Arguments.Count > 0)
                {
                    // For Mapped<T, TMapping>() the mapping type is the last argument;
                    // for Mapped<TMapping>() (on ColumnBuilder) it's the only argument.
                    return generic.TypeArgumentList.Arguments[generic.TypeArgumentList.Arguments.Count - 1];
                }
            }

            // Move to the expression the method was called on
            var ma = invocation.Expression as MemberAccessExpressionSyntax;
            if (ma != null)
                expression = ma.Expression;
            else
                expression = null;
        }

        return null;
    }

    /// <summary>
    /// Walks the base type hierarchy of a mapping class to find TypeMapping&lt;TCustom, TDb&gt;
    /// and returns both the TCustom and TDb type symbols.
    /// </summary>
    private static (ITypeSymbol CustomType, ITypeSymbol DbType)? ExtractTypesFromMappingHierarchy(INamedTypeSymbol mappingType)
    {
        var current = mappingType.BaseType;
        while (current != null)
        {
            if (current.IsGenericType &&
                current.OriginalDefinition.ToDisplayString() == "Quarry.TypeMapping<TCustom, TDb>" &&
                current.TypeArguments.Length == 2)
            {
                return (current.TypeArguments[0], current.TypeArguments[1]); // TCustom, TDb
            }

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Gets the DbDataReader method name for a TDb type symbol.
    /// Reuses the same logic as ColumnInfo.GetTypeMetadata.
    /// </summary>
    private static string GetDbReaderMethodFromTypeSymbol(ITypeSymbol dbType)
    {
        var (_, readerMethod, _) = ColumnInfo.GetTypeMetadata(dbType);
        return readerMethod;
    }

    /// <summary>
    /// Strips the "Schema" suffix from a type symbol name to get the entity name.
    /// Schema types must end with "Schema" (e.g., UserSchema → User).
    /// </summary>
    private static string NormalizeEntityName(ITypeSymbol typeSymbol)
    {
        var name = typeSymbol.Name;
        if (name.EndsWith("Schema", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 6);
        return name;
    }

    /// <summary>
    /// Extracts CLR type information from a type symbol.
    /// </summary>
    private static void ExtractTypeInfo(ITypeSymbol typeSymbol, out string shortName, out string fullName, out bool isNullable)
    {
        isNullable = false;
        var actualType = typeSymbol;

        // Handle nullable value types
        var namedType = typeSymbol as INamedTypeSymbol;
        if (namedType != null && namedType.IsGenericType && namedType.Name == "Nullable")
        {
            isNullable = true;
            actualType = namedType.TypeArguments[0];
        }
        // Handle nullable reference types
        else if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            isNullable = true;
        }

        shortName = actualType.Name;
        fullName = actualType.ToDisplayString();

        // Use C# keyword aliases for primitive types
        fullName = GetTypeAlias(fullName);
        shortName = GetTypeAlias(shortName);
    }

    /// <summary>
    /// Gets the C# keyword alias for a type if available.
    /// </summary>
    private static string GetTypeAlias(string typeName)
    {
        switch (typeName)
        {
            case "System.Int32":
            case "Int32":
                return "int";
            case "System.Int64":
            case "Int64":
                return "long";
            case "System.Int16":
            case "Int16":
                return "short";
            case "System.Byte":
            case "Byte":
                return "byte";
            case "System.Boolean":
            case "Boolean":
                return "bool";
            case "System.String":
            case "String":
                return "string";
            case "System.Char":
            case "Char":
                return "char";
            case "System.Single":
            case "Single":
                return "float";
            case "System.Double":
            case "Double":
                return "double";
            case "System.Decimal":
            case "Decimal":
                return "decimal";
            case "System.Object":
            case "Object":
                return "object";
            default:
                return typeName;
        }
    }

    /// <summary>
    /// Parses column modifiers from a property declaration.
    /// </summary>
    private static ColumnModifiers ParseColumnModifiers(PropertyDeclarationSyntax property)
    {
        bool isIdentity = false;
        bool isClientGenerated = false;
        bool isComputed = false;
        bool isForeignKey = false;
        bool hasDefault = false;
        bool isUnique = false;
        bool isSensitive = false;
        int? maxLength = null;
        int? precision = null;
        int? scale = null;
        string? mappedName = null;
        string? customTypeMapping = null;

        // Get the expression from the property (either expression body or getter)
        ExpressionSyntax? expression = null;

        if (property.ExpressionBody != null)
        {
            expression = property.ExpressionBody.Expression;
        }
        else if (property.AccessorList != null)
        {
            var getter = property.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody != null)
            {
                expression = getter.ExpressionBody.Expression;
            }
        }

        // Parse method chain
        ParseModifierChain(expression, ref isIdentity, ref isClientGenerated, ref isComputed,
            ref isForeignKey, ref hasDefault, ref isUnique, ref isSensitive, ref maxLength, ref precision, ref scale, ref mappedName,
            ref customTypeMapping);

        return new ColumnModifiers(
            isIdentity: isIdentity,
            isClientGenerated: isClientGenerated,
            isComputed: isComputed,
            maxLength: maxLength,
            precision: precision,
            scale: scale,
            hasDefault: hasDefault,
            isForeignKey: isForeignKey,
            mappedName: mappedName,
            customTypeMapping: customTypeMapping,
            isUnique: isUnique,
            isSensitive: isSensitive);
    }

    /// <summary>
    /// Recursively parses a method chain to extract modifiers.
    /// </summary>
    private static void ParseModifierChain(
        ExpressionSyntax? expression,
        ref bool isIdentity,
        ref bool isClientGenerated,
        ref bool isComputed,
        ref bool isForeignKey,
        ref bool hasDefault,
        ref bool isUnique,
        ref bool isSensitive,
        ref int? maxLength,
        ref int? precision,
        ref int? scale,
        ref string? mappedName,
        ref string? customTypeMapping)
    {
        while (expression != null)
        {
            var invocation = expression as InvocationExpressionSyntax;
            if (invocation == null)
                break;

            var methodName = GetMethodName(invocation);

            switch (methodName)
            {
                case "Identity":
                    isIdentity = true;
                    break;
                case "ClientGenerated":
                    isClientGenerated = true;
                    break;
                case "Computed":
                    isComputed = true;
                    break;
                case "ForeignKey":
                    isForeignKey = true;
                    break;
                case "Default":
                    hasDefault = true;
                    break;
                case "Length":
                    maxLength = ExtractIntArgument(invocation, 0);
                    break;
                case "Precision":
                    precision = ExtractIntArgument(invocation, 0);
                    scale = ExtractIntArgument(invocation, 1);
                    break;
                case "MapTo":
                    mappedName = ExtractStringArgument(invocation, 0);
                    break;
                case "Mapped":
                    customTypeMapping = ExtractGenericTypeArgument(invocation);
                    break;
                case "Unique":
                    isUnique = true;
                    break;
                case "Sensitive":
                    isSensitive = true;
                    break;
            }

            // Move to the expression the method was called on
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess != null)
            {
                expression = memberAccess.Expression;
            }
            else
            {
                expression = null;
            }
        }
    }

    /// <summary>
    /// Extracts the first generic type argument text from an invocation like Mapped&lt;MoneyMapping&gt;().
    /// Works for both standalone calls (GenericNameSyntax) and chained calls (MemberAccess with GenericName).
    /// </summary>
    private static string? ExtractGenericTypeArgument(InvocationExpressionSyntax invocation)
    {
        GenericNameSyntax? generic = invocation.Expression as GenericNameSyntax;

        // For chained calls: something.Mapped<TMapping>()
        if (generic == null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            generic = memberAccess.Name as GenericNameSyntax;
        }

        if (generic != null && generic.TypeArgumentList.Arguments.Count > 0)
        {
            return generic.TypeArgumentList.Arguments[0].ToString();
        }

        return null;
    }

    /// <summary>
    /// Gets the method name from an invocation expression.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text;
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text;
            case GenericNameSyntax generic:
                return generic.Identifier.Text;
            default:
                return null;
        }
    }

    /// <summary>
    /// Extracts an integer argument from an invocation.
    /// </summary>
    private static int? ExtractIntArgument(InvocationExpressionSyntax invocation, int index)
    {
        if (invocation.ArgumentList.Arguments.Count <= index)
            return null;

        var arg = invocation.ArgumentList.Arguments[index];
        var literal = arg.Expression as LiteralExpressionSyntax;
        if (literal != null && literal.Token.Value is int value)
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// Extracts a string argument from an invocation.
    /// </summary>
    private static string? ExtractStringArgument(InvocationExpressionSyntax invocation, int index)
    {
        if (invocation.ArgumentList.Arguments.Count <= index)
            return null;

        var arg = invocation.ArgumentList.Arguments[index];
        var literal = arg.Expression as LiteralExpressionSyntax;
        if (literal != null)
        {
            return literal.Token.ValueText;
        }

        return null;
    }

    /// <summary>
    /// Tries to parse a navigation property (Many&lt;T&gt;).
    /// </summary>
    private static bool TryParseNavigation(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        out NavigationInfo? navigationInfo)
    {
        navigationInfo = null;

        var propertyType = propertySymbol.Type;
        var namedType = propertyType as INamedTypeSymbol;
        if (namedType == null)
            return false;

        if (namedType.Name != "Many" || !namedType.IsGenericType || namedType.TypeArguments.Length != 1)
            return false;

        var relatedEntityType = namedType.TypeArguments[0];
        var relatedEntityName = NormalizeEntityName(relatedEntityType);

        // Extract foreign key property name from HasMany expression
        var fkPropertyName = ExtractForeignKeyPropertyName(property) ?? "Id";

        navigationInfo = new NavigationInfo(
            propertyName: propertySymbol.Name,
            relatedEntityName: relatedEntityName,
            foreignKeyPropertyName: fkPropertyName);

        return true;
    }

    /// <summary>
    /// Tries to parse a singular navigation property (One&lt;T&gt;).
    /// Requires columns to be populated for FK resolution.
    /// </summary>
    private static bool TryParseSingleNavigation(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        List<ColumnInfo> columns,
        out SingleNavigationInfo? singleNavigationInfo,
        List<Diagnostic>? diagnostics = null,
        string? schemaName = null)
    {
        singleNavigationInfo = null;

        var propertyType = propertySymbol.Type;
        var namedType = propertyType as INamedTypeSymbol;
        if (namedType == null)
            return false;

        if (namedType.Name != "One" || !namedType.IsGenericType || namedType.TypeArguments.Length != 1)
            return false;

        var targetEntityType = namedType.TypeArguments[0];
        var targetEntityName = NormalizeEntityName(targetEntityType);
        var propertyLocation = property.Identifier.GetLocation();

        // Determine FK property name: explicit (HasOne) or auto-detect
        string? fkPropertyName = null;
        bool isExplicitHasOne = false;

        // Check for explicit HasOne<T>(nameof(FkColumn))
        if (property.ExpressionBody != null)
        {
            var invocation = property.ExpressionBody.Expression as InvocationExpressionSyntax;
            if (invocation != null)
            {
                var methodName = GetMethodName(invocation);
                if (methodName == "HasOne" && invocation.ArgumentList.Arguments.Count > 0)
                {
                    isExplicitHasOne = true;
                    var arg = invocation.ArgumentList.Arguments[0];
                    // Handle string literal (nameof() is folded to string constant)
                    if (arg.Expression is LiteralExpressionSyntax literal)
                    {
                        fkPropertyName = literal.Token.ValueText;
                    }
                    // Handle unfolded nameof() expression
                    else if (arg.Expression is InvocationExpressionSyntax nameofExpr
                             && nameofExpr.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" }
                             && nameofExpr.ArgumentList.Arguments.Count > 0)
                    {
                        var nameofArg = nameofExpr.ArgumentList.Arguments[0].Expression;
                        if (nameofArg is IdentifierNameSyntax idName)
                            fkPropertyName = idName.Identifier.Text;
                    }
                }
            }
        }

        // Validate explicit HasOne references a valid FK column
        if (isExplicitHasOne && fkPropertyName != null)
        {
            var referencedCol = columns.FirstOrDefault(c => c.PropertyName == fkPropertyName);
            if (referencedCol == null || referencedCol.Kind != ColumnKind.ForeignKey || referencedCol.ReferencedEntityName != targetEntityName)
            {
                diagnostics?.Add(Diagnostic.Create(
                    DiagnosticDescriptors.HasOneInvalidColumn,
                    propertyLocation,
                    targetEntityName, fkPropertyName));
                return false;
            }
        }

        // Auto-detect: scan Ref<T,K> columns for matching target entity
        if (fkPropertyName == null)
        {
            var matchingRefs = columns
                .Where(c => c.Kind == ColumnKind.ForeignKey && c.ReferencedEntityName == targetEntityName)
                .ToList();

            if (matchingRefs.Count == 1)
            {
                fkPropertyName = matchingRefs[0].PropertyName;
            }
            else if (matchingRefs.Count == 0)
            {
                diagnostics?.Add(Diagnostic.Create(
                    DiagnosticDescriptors.NoFkForOneNavigation,
                    propertyLocation,
                    targetEntityName, propertySymbol.Name, schemaName ?? ""));
                return false;
            }
            else
            {
                diagnostics?.Add(Diagnostic.Create(
                    DiagnosticDescriptors.AmbiguousFkForOneNavigation,
                    propertyLocation,
                    targetEntityName, propertySymbol.Name,
                    string.Join(", ", matchingRefs.Select(r => r.PropertyName))));
                return false;
            }
        }

        // Determine FK nullability
        var fkColumn = columns.FirstOrDefault(c => c.PropertyName == fkPropertyName);
        var isNullableFk = fkColumn?.IsNullable ?? false;

        singleNavigationInfo = new SingleNavigationInfo(
            propertyName: propertySymbol.Name,
            targetEntityName: targetEntityName,
            foreignKeyPropertyName: fkPropertyName,
            isNullableFk: isNullableFk);

        return true;
    }

    /// <summary>
    /// Tries to parse a HasManyThrough skip-navigation.
    /// The property type is Many&lt;T&gt; but with a HasManyThrough expression body.
    /// </summary>
    private static bool TryParseThroughNavigation(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        out ThroughNavigationInfo? throughNavigationInfo)
    {
        throughNavigationInfo = null;

        // Must be Many<T> type
        var propertyType = propertySymbol.Type;
        var namedType = propertyType as INamedTypeSymbol;
        if (namedType == null || namedType.Name != "Many" || !namedType.IsGenericType || namedType.TypeArguments.Length != 1)
            return false;

        // Must have HasManyThrough expression body
        if (property.ExpressionBody == null)
            return false;

        var invocation = property.ExpressionBody.Expression as InvocationExpressionSyntax;
        if (invocation == null)
            return false;

        var methodName = GetMethodName(invocation);
        if (methodName != "HasManyThrough")
            return false;

        // Extract type arguments: HasManyThrough<TTarget, TJunction[, TSelf]>(...)
        string? targetEntityName = null;
        string? junctionEntityName = null;
        if (invocation.Expression is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count >= 2)
        {
            targetEntityName = genericName.TypeArgumentList.Arguments[0] is IdentifierNameSyntax t0
                ? NormalizeSchemaName(t0.Identifier.Text) : null;
            junctionEntityName = genericName.TypeArgumentList.Arguments[1] is IdentifierNameSyntax t1
                ? NormalizeSchemaName(t1.Identifier.Text) : null;
        }
        else if (invocation.Expression is MemberAccessExpressionSyntax ma
                 && ma.Name is GenericNameSyntax gns && gns.TypeArgumentList.Arguments.Count >= 2)
        {
            targetEntityName = gns.TypeArgumentList.Arguments[0] is IdentifierNameSyntax t0
                ? NormalizeSchemaName(t0.Identifier.Text) : null;
            junctionEntityName = gns.TypeArgumentList.Arguments[1] is IdentifierNameSyntax t1
                ? NormalizeSchemaName(t1.Identifier.Text) : null;
        }

        if (targetEntityName == null || junctionEntityName == null)
            return false;

        // Extract the two lambda arguments
        if (invocation.ArgumentList.Arguments.Count < 2)
            return false;

        // First arg: junction => junction.UserAddresses (member access on junction nav)
        string? junctionNavigationName = ExtractLambdaMemberName(invocation.ArgumentList.Arguments[0]);

        // Second arg: through => through.Address (member access on One<T> nav)
        string? targetNavigationName = ExtractLambdaMemberName(invocation.ArgumentList.Arguments[1]);

        if (junctionNavigationName == null || targetNavigationName == null)
            return false;

        throughNavigationInfo = new ThroughNavigationInfo(
            propertyName: propertySymbol.Name,
            targetEntityName: targetEntityName,
            junctionEntityName: junctionEntityName,
            junctionNavigationName: junctionNavigationName,
            targetNavigationName: targetNavigationName);

        return true;
    }

    /// <summary>
    /// Extracts the member name from a lambda expression argument (e.g., x => x.Foo returns "Foo").
    /// </summary>
    private static string? ExtractLambdaMemberName(ArgumentSyntax argument)
    {
        var lambda = argument.Expression as SimpleLambdaExpressionSyntax;
        if (lambda == null)
            return null;

        var memberAccess = lambda.Body as MemberAccessExpressionSyntax;
        if (memberAccess != null)
            return memberAccess.Name.Identifier.Text;

        return null;
    }

    /// <summary>
    /// Normalizes a schema name by stripping the "Schema" suffix.
    /// </summary>
    private static string NormalizeSchemaName(string name)
    {
        if (name.EndsWith("Schema", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 6);
        return name;
    }

    /// <summary>
    /// Tries to parse an index property (Index).
    /// </summary>
    private static bool TryParseIndex(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        out IndexInfo? indexInfo)
    {
        indexInfo = null;

        var propertyType = propertySymbol.Type;
        if (propertyType.Name != "Index" || propertyType.ToDisplayString() != "Quarry.Index")
            return false;

        var indexName = propertySymbol.Name;

        // Get the expression from the property
        ExpressionSyntax? expression = null;
        if (property.ExpressionBody != null)
            expression = property.ExpressionBody.Expression;
        else if (property.AccessorList != null)
        {
            var getter = property.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody != null)
                expression = getter.ExpressionBody.Expression;
        }

        if (expression == null)
            return false;

        // Parse the index builder chain
        var columns = new List<IndexColumnInfo>();
        bool isUnique = false;
        string? indexType = null;
        string? filter = null;
        bool filterIsBoolColumn = false;
        var includeColumns = new List<string>();

        // Walk the method chain from outermost to innermost
        var current = expression;
        InvocationExpressionSyntax? indexInvocation = null;

        while (current is InvocationExpressionSyntax invocation)
        {
            var methodName = GetMethodName(invocation);

            switch (methodName)
            {
                case "Unique":
                    isUnique = true;
                    break;
                case "Using":
                    if (invocation.ArgumentList.Arguments.Count > 0)
                    {
                        var arg = invocation.ArgumentList.Arguments[0].Expression;
                        if (arg is MemberAccessExpressionSyntax memberAccess)
                            indexType = memberAccess.Name.Identifier.Text;
                    }
                    break;
                case "Where":
                    if (invocation.ArgumentList.Arguments.Count > 0)
                    {
                        var arg = invocation.ArgumentList.Arguments[0].Expression;
                        if (arg is LiteralExpressionSyntax literal)
                        {
                            filter = literal.Token.ValueText;
                            filterIsBoolColumn = false;
                        }
                        else if (arg is IdentifierNameSyntax identifier)
                        {
                            filter = identifier.Identifier.Text;
                            filterIsBoolColumn = true;
                        }
                    }
                    break;
                case "Include":
                    foreach (var arg in invocation.ArgumentList.Arguments)
                    {
                        if (arg.Expression is IdentifierNameSyntax id)
                            includeColumns.Add(id.Identifier.Text);
                    }
                    break;
                case "Index":
                    indexInvocation = invocation;
                    break;
            }

            // Move to the expression the method was called on
            var ma = invocation.Expression as MemberAccessExpressionSyntax;
            if (ma != null)
                current = ma.Expression;
            else
                current = null;
        }

        // Parse the Index() arguments for columns
        if (indexInvocation != null)
        {
            foreach (var arg in indexInvocation.ArgumentList.Arguments)
            {
                var argExpr = arg.Expression;

                // Direct property reference: Index(Email)
                if (argExpr is IdentifierNameSyntax colId)
                {
                    columns.Add(new IndexColumnInfo(colId.Identifier.Text));
                }
                // Property with direction: Index(Email.Desc()) or Index(Email.Asc())
                else if (argExpr is InvocationExpressionSyntax dirInvocation &&
                         dirInvocation.Expression is MemberAccessExpressionSyntax dirAccess)
                {
                    var dirMethodName = dirAccess.Name.Identifier.Text;
                    var direction = dirMethodName == "Desc" ? SortDirection.Descending : SortDirection.Ascending;

                    if (dirAccess.Expression is IdentifierNameSyntax colRef)
                    {
                        columns.Add(new IndexColumnInfo(colRef.Identifier.Text, direction));
                    }
                }
            }
        }

        if (columns.Count == 0)
            return false;

        indexInfo = new IndexInfo(
            name: indexName,
            columns: columns,
            isUnique: isUnique,
            indexType: indexType,
            filter: filter,
            filterIsBoolColumn: filterIsBoolColumn,
            includeColumns: includeColumns.Count > 0 ? includeColumns : null);

        return true;
    }

    /// <summary>
    /// Tries to parse a composite key property (CompositeKey).
    /// </summary>
    private static bool TryParseCompositeKey(
        PropertyDeclarationSyntax property,
        IPropertySymbol propertySymbol,
        out List<string>? columns)
    {
        columns = null;

        var propertyType = propertySymbol.Type;
        if (propertyType.Name != "CompositeKey" || propertyType.ToDisplayString() != "Quarry.CompositeKey")
            return false;

        // Get the expression from the property
        ExpressionSyntax? expression = null;
        if (property.ExpressionBody != null)
            expression = property.ExpressionBody.Expression;
        else if (property.AccessorList != null)
        {
            var getter = property.AccessorList.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody != null)
                expression = getter.ExpressionBody.Expression;
        }

        if (expression == null)
            return false;

        // Find the PrimaryKey() invocation
        var invocation = expression as InvocationExpressionSyntax;
        if (invocation == null)
            return false;

        var methodName = GetMethodName(invocation);
        if (methodName != "PrimaryKey")
            return false;

        // Parse the PrimaryKey() arguments for column property references
        var result = new List<string>();
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var argExpr = arg.Expression;

            // Direct property reference: PrimaryKey(StudentId, CourseId)
            if (argExpr is IdentifierNameSyntax colId)
            {
                result.Add(colId.Identifier.Text);
            }
        }

        if (result.Count < 2)
            return false;

        columns = result;
        return true;
    }

    /// <summary>
    /// Extracts the foreign key property name from a HasMany invocation.
    /// </summary>
    private static string? ExtractForeignKeyPropertyName(PropertyDeclarationSyntax property)
    {
        ExpressionSyntax? expression = null;
        if (property.ExpressionBody != null)
        {
            expression = property.ExpressionBody.Expression;
        }

        var invocation = expression as InvocationExpressionSyntax;
        if (invocation != null)
        {
            var methodName = GetMethodName(invocation);
            if (methodName == "HasMany" && invocation.ArgumentList.Arguments.Count > 0)
            {
                var arg = invocation.ArgumentList.Arguments[0];
                // Parse lambda: o => o.UserId
                var lambda = arg.Expression as SimpleLambdaExpressionSyntax;
                if (lambda != null)
                {
                    var memberAccess = lambda.Body as MemberAccessExpressionSyntax;
                    if (memberAccess != null)
                    {
                        return memberAccess.Name.Identifier.Text;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Syntax walker to find schema class candidates.
    /// </summary>
    private sealed class SchemaTypeFinder : CSharpSyntaxWalker
    {
        private readonly string _schemaClassName;
        public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

        public SchemaTypeFinder(string schemaClassName)
        {
            _schemaClassName = schemaClassName;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node.Identifier.Text == _schemaClassName)
            {
                Candidates.Add(node);
            }
            base.VisitClassDeclaration(node);
        }
    }
}
