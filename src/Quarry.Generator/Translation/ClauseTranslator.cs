using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Parsing;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Translation;

/// <summary>
/// Translates clause expressions (Where, OrderBy, GroupBy, Having, Set) to SQL.
/// Works with ITypeSymbol during usage site discovery when EntityInfo is not available.
/// </summary>
internal static class ClauseTranslator
{
    /// <summary>
    /// Translates a Where() clause expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateWhere(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.Where, "Where() requires a lambda expression");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = CreateTranslationContext(semanticModel, entityType, dialect, parameterName);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.Where, result.ErrorMessage ?? "Translation failed");
        }

        return ClauseInfo.Success(ClauseKind.Where, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates a Where() clause expression to SQL using pre-analyzed entity metadata.
    /// Use this overload when EntityInfo is available from schema parsing.
    /// </summary>
    public static ClauseInfo TranslateWhereWithEntityInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        EntityInfo entityInfo,
        SqlDialect dialect,
        IReadOnlyDictionary<string, EntityInfo>? entityRegistry = null,
        Compilation? compilation = null)
    {
        var lambdaInfo = ExtractLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.Where, "Where() requires a lambda expression");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = new ExpressionTranslationContext(semanticModel, entityInfo, dialect, parameterName, entityRegistry, compilation);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.Where, result.ErrorMessage ?? "Translation failed");
        }

        return ClauseInfo.Success(ClauseKind.Where, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates an OrderBy() or ThenBy() clause expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateOrderBy(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.OrderBy, "OrderBy() requires a lambda expression");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = CreateTranslationContext(semanticModel, entityType, dialect, parameterName);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.OrderBy, result.ErrorMessage ?? "Translation failed");
        }

        // Check for direction parameter (second argument)
        var isDescending = false;
        if (invocation.ArgumentList.Arguments.Count >= 2)
        {
            var directionArg = invocation.ArgumentList.Arguments[1].Expression;
            isDescending = IsDescendingDirection(directionArg, semanticModel);
        }

        return new OrderByClauseInfo(result.Sql!, isDescending, result.Parameters);
    }

    /// <summary>
    /// Translates a GroupBy() clause expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateGroupBy(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.GroupBy, "GroupBy() requires a lambda expression");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = CreateTranslationContext(semanticModel, entityType, dialect, parameterName);

        // Handle tuple grouping: u => (u.Col1, u.Col2)
        if (lambdaBody is TupleExpressionSyntax tuple)
        {
            var columns = new List<string>();
            foreach (var element in tuple.Arguments)
            {
                var elementResult = ExpressionSyntaxTranslator.Translate(element.Expression, context);
                if (!elementResult.IsSuccess)
                {
                    return ClauseInfo.Failure(ClauseKind.GroupBy, elementResult.ErrorMessage ?? "Translation failed");
                }
                columns.Add(elementResult.Sql!);
            }
            return ClauseInfo.Success(ClauseKind.GroupBy, string.Join(", ", columns), context.Parameters);
        }

        // Single column grouping: u => u.Col
        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.GroupBy, result.ErrorMessage ?? "Translation failed");
        }

        return ClauseInfo.Success(ClauseKind.GroupBy, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates a Having() clause expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateHaving(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.Having, "Having() requires a lambda expression");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = CreateTranslationContext(semanticModel, entityType, dialect, parameterName);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.Having, result.ErrorMessage ?? "Translation failed");
        }

        return ClauseInfo.Success(ClauseKind.Having, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates a Set() clause expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateSet(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect,
        int existingParameterCount)
    {
        // Set(u => u.Column, value)
        // First argument is the column selector lambda
        // Second argument is the value

        if (invocation.ArgumentList.Arguments.Count < 2)
        {
            return ClauseInfo.Failure(ClauseKind.Set, "Set() requires column selector and value arguments");
        }

        var columnArg = invocation.ArgumentList.Arguments[0].Expression;
        var valueArg = invocation.ArgumentList.Arguments[1].Expression;

        // Extract lambda from column selector
        if (columnArg is not LambdaExpressionSyntax columnLambda)
        {
            return ClauseInfo.Failure(ClauseKind.Set, "Set() column selector must be a lambda expression");
        }

        var lambdaInfo = ExtractLambdaInfo(columnLambda);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.Set, "Invalid column selector lambda");
        }

        var (lambdaBody, parameterName) = lambdaInfo.Value;
        var context = CreateTranslationContext(semanticModel, entityType, dialect, parameterName);

        // Translate the column selector
        var columnResult = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!columnResult.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.Set, columnResult.ErrorMessage ?? "Column translation failed");
        }

        // Get type info from the value
        var typeInfo = semanticModel.GetTypeInfo(valueArg);
        var valueType = typeInfo.Type?.ToDisplayString() ?? "object";
        var valueExpression = valueArg.ToFullString().Trim();

        // Create parameter for the value
        var parameterIndex = existingParameterCount;
        var parameters = new List<ParameterInfo>
        {
            new ParameterInfo(parameterIndex, $"@p{parameterIndex}", valueType, valueExpression)
        };

        return new SetClauseInfo(columnResult.Sql!, parameterIndex, parameters);
    }

    /// <summary>
    /// Creates a translation context from an ITypeSymbol.
    /// </summary>
    private static ExpressionTranslationContext CreateTranslationContext(
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect,
        string lambdaParameterName)
    {
        // Build column info from the type symbol
        var columns = BuildColumnInfoFromTypeSymbol(entityType);

        // Create a minimal EntityInfo for expression translation
        // The table name and other metadata will be replaced at runtime
        var entityInfo = new EntityInfo(
            entityName: entityType.Name,
            schemaClassName: entityType.Name + "Schema",
            schemaNamespace: entityType.ContainingNamespace?.ToDisplayString() ?? "",
            tableName: "table", // Default table name - will be replaced at runtime
            namingStyle: NamingStyleKind.Exact,
            columns: columns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None);

        return new ExpressionTranslationContext(semanticModel, entityInfo, dialect, lambdaParameterName);
    }

    /// <summary>
    /// Builds column info from a type symbol's properties.
    /// </summary>
    private static IReadOnlyList<ColumnInfo> BuildColumnInfoFromTypeSymbol(ITypeSymbol typeSymbol)
    {
        var columns = new List<ColumnInfo>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            // Skip non-public properties
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;

            // Skip static and indexer properties
            if (property.IsStatic || property.IsIndexer)
                continue;

            // Determine column name (use property name by default)
            var columnName = property.Name;

            // Get CLR type info
            var propertyType = property.Type;
            var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated ||
                             (propertyType is INamedTypeSymbol namedType &&
                              namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

            var clrType = GetSimpleTypeName(propertyType);
            var fullClrType = propertyType.ToDisplayString();

            // Determine column kind
            var kind = ColumnKind.Standard;
            if (property.Name.EndsWith("Id") && property.Name.Length > 2)
            {
                kind = ColumnKind.ForeignKey;
            }
            else if (property.Name == "Id" || property.Name == typeSymbol.Name + "Id")
            {
                kind = ColumnKind.PrimaryKey;
            }

            // Get type metadata from the type symbol
            var (isValueType, readerMethodName, _) = ColumnInfo.GetTypeMetadata(propertyType);

            var column = new ColumnInfo(
                propertyName: property.Name,
                columnName: columnName,
                clrType: clrType,
                fullClrType: fullClrType,
                isNullable: isNullable,
                kind: kind,
                referencedEntityName: null,
                modifiers: new ColumnModifiers(),
                isValueType: isValueType,
                readerMethodName: readerMethodName);

            columns.Add(column);
        }

        return columns;
    }

    /// <summary>
    /// Extracts the lambda body and parameter name from an invocation's first argument.
    /// </summary>
    private static (ExpressionSyntax Body, string ParameterName)? ExtractLambdaExpression(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        if (argument is not LambdaExpressionSyntax lambda)
            return null;

        return ExtractLambdaInfo(lambda);
    }

    /// <summary>
    /// Extracts body and parameter name from a lambda expression.
    /// </summary>
    private static (ExpressionSyntax Body, string ParameterName)? ExtractLambdaInfo(LambdaExpressionSyntax lambda)
    {
        if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
        {
            if (simpleLambda.Body is not ExpressionSyntax body)
                return null;

            return (body, simpleLambda.Parameter.Identifier.Text);
        }

        if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            if (parenLambda.Body is not ExpressionSyntax body)
                return null;

            var parameterName = parenLambda.ParameterList.Parameters.Count > 0
                ? parenLambda.ParameterList.Parameters[0].Identifier.Text
                : "x";

            return (body, parameterName);
        }

        return null;
    }

    /// <summary>
    /// Checks if a direction argument specifies descending order.
    /// </summary>
    private static bool IsDescendingDirection(ExpressionSyntax directionArg, SemanticModel semanticModel)
    {
        // Handle Direction.Descending
        if (directionArg is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            return memberName == "Descending" || memberName == "Desc";
        }

        // Handle enum value
        var constantValue = semanticModel.GetConstantValue(directionArg);
        if (constantValue.HasValue && constantValue.Value is int intValue)
        {
            // Assuming 1 = Descending
            return intValue == 1;
        }

        return false;
    }

    /// <summary>
    /// Translates a Join(), LeftJoin(), or RightJoin() condition expression to SQL.
    /// </summary>
    public static ClauseInfo TranslateJoin(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol leftEntityType,
        ITypeSymbol rightEntityType,
        SqlDialect dialect,
        JoinClauseKind joinKind)
    {
        var lambdaInfo = ExtractTwoParameterLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
        {
            return ClauseInfo.Failure(ClauseKind.Join, "Join() requires a two-parameter lambda expression");
        }

        var (lambdaBody, leftParamName, rightParamName) = lambdaInfo.Value;

        // Create a translation context for join expressions
        var context = CreateJoinTranslationContext(
            semanticModel, leftEntityType, rightEntityType, dialect, leftParamName, rightParamName);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
        {
            return ClauseInfo.Failure(ClauseKind.Join, result.ErrorMessage ?? "Join condition translation failed");
        }

        return new JoinClauseInfo(
            joinKind,
            rightEntityType.Name,
            rightEntityType.Name, // Placeholder — replaced during enrichment with actual table name
            result.Sql!,
            result.Parameters);
    }

    /// <summary>
    /// Translates a join condition using pre-built EntityInfo (from enrichment phase).
    /// </summary>
    public static ClauseInfo? TranslateJoinFromEntityInfo(
        InvocationExpressionSyntax invocation,
        EntityInfo leftEntity,
        EntityInfo rightEntity,
        SqlDialect dialect,
        JoinClauseKind joinKind)
    {
        var lambdaInfo = ExtractTwoParameterLambdaExpression(invocation);
        if (!lambdaInfo.HasValue)
            return null;

        var (lambdaBody, leftParamName, rightParamName) = lambdaInfo.Value;

        // Create context from EntityInfo directly
        var context = new ExpressionTranslationContext(null!, leftEntity, dialect, leftParamName);
        context = context.WithJoinedEntity(rightParamName, rightEntity);

        // Set up positional aliases
        var aliases = new Dictionary<string, string>
        {
            [leftParamName] = "t0",
            [rightParamName] = "t1"
        };
        context = context.WithTableAliases(aliases);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
            return null;

        return new JoinClauseInfo(joinKind, rightEntity.EntityName, rightEntity.TableName, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates a navigation-based join (u => u.Orders) using FK metadata from EntityInfo.
    /// Generates the ON condition from the navigation property's foreign key relationship.
    /// </summary>
    public static ClauseInfo? TranslateNavigationJoin(
        InvocationExpressionSyntax invocation,
        EntityInfo leftEntity,
        EntityInfo rightEntity,
        SqlDialect dialect,
        JoinClauseKind joinKind)
    {
        // Extract the navigation property name from the single-parameter lambda body
        var navPropertyName = ExtractNavigationPropertyName(invocation);
        if (navPropertyName == null)
            return null;

        // Find the navigation on the left entity
        NavigationInfo? navInfo = null;
        foreach (var nav in leftEntity.Navigations)
        {
            if (nav.PropertyName == navPropertyName && nav.RelatedEntityName == rightEntity.EntityName)
            {
                navInfo = nav;
                break;
            }
        }

        if (navInfo == null)
            return null;

        // Resolve FK on right entity and PK on left entity
        var correlation = ExpressionSyntaxTranslator.ResolveForeignKeyCorrelationPublic(
            navInfo, rightEntity, leftEntity);
        if (correlation == null)
            return null;

        var (fkColumnName, pkColumnName) = correlation.Value;

        // Quote identifiers
        var onConditionSql = $"t0.{QuoteIdentifier(pkColumnName, dialect)} = t1.{QuoteIdentifier(fkColumnName, dialect)}";

        return new JoinClauseInfo(
            joinKind,
            rightEntity.EntityName,
            rightEntity.TableName,
            onConditionSql,
            System.Array.Empty<ParameterInfo>());
    }

    /// <summary>
    /// Extracts the navigation property name from a single-parameter lambda: u => u.Orders
    /// </summary>
    private static string? ExtractNavigationPropertyName(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        ExpressionSyntax? body = null;

        if (argument is SimpleLambdaExpressionSyntax simpleLambda)
        {
            body = simpleLambda.Body as ExpressionSyntax;
        }
        else if (argument is ParenthesizedLambdaExpressionSyntax parenLambda
                 && parenLambda.ParameterList.Parameters.Count == 1)
        {
            body = parenLambda.Body as ExpressionSyntax;
        }

        if (body is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        return null;
    }

    /// <summary>
    /// Translates a chained join condition (3+ parameter lambda) using pre-built EntityInfo.
    /// For JoinedQueryBuilder&lt;T1,T2&gt;.Join&lt;T3&gt;((a,b,c) => c.FkId == b.Id)
    /// </summary>
    public static ClauseInfo? TranslateChainedJoinFromEntityInfo(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<EntityInfo> priorEntities,
        EntityInfo newEntity,
        SqlDialect dialect,
        JoinClauseKind joinKind)
    {
        var totalParams = priorEntities.Count + 1;
        var lambdaInfo = ExtractMultiParameterLambdaExpression(invocation, totalParams);
        if (!lambdaInfo.HasValue)
            return null;

        var (lambdaBody, paramNames) = lambdaInfo.Value;

        // Create context: first entity is primary, rest are joined
        var context = new ExpressionTranslationContext(null!, priorEntities[0], dialect, paramNames[0]);
        for (int i = 1; i < priorEntities.Count; i++)
        {
            context = context.WithJoinedEntity(paramNames[i], priorEntities[i]);
        }
        context = context.WithJoinedEntity(paramNames[priorEntities.Count], newEntity);

        // Set up positional aliases: t0, t1, t2, ...
        var aliases = new Dictionary<string, string>();
        for (int i = 0; i < paramNames.Count; i++)
        {
            aliases[paramNames[i]] = $"t{i}";
        }
        context = context.WithTableAliases(aliases);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
            return null;

        return new JoinClauseInfo(joinKind, newEntity.EntityName, newEntity.TableName, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates a Where clause on a joined query using N-parameter lambda.
    /// </summary>
    public static ClauseInfo? TranslateJoinedWhere(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<EntityInfo> entities,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractMultiParameterLambdaExpression(invocation, entities.Count);
        if (!lambdaInfo.HasValue)
            return null;

        var (lambdaBody, paramNames) = lambdaInfo.Value;
        var context = BuildMultiEntityContext(entities, paramNames, dialect);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
            return null;

        return ClauseInfo.Success(ClauseKind.Where, result.Sql!, result.Parameters);
    }

    /// <summary>
    /// Translates an OrderBy/ThenBy clause on a joined query using N-parameter lambda.
    /// </summary>
    public static ClauseInfo? TranslateJoinedOrderBy(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<EntityInfo> entities,
        SqlDialect dialect)
    {
        var lambdaInfo = ExtractMultiParameterLambdaExpression(invocation, entities.Count);
        if (!lambdaInfo.HasValue)
            return null;

        var (lambdaBody, paramNames) = lambdaInfo.Value;
        var context = BuildMultiEntityContext(entities, paramNames, dialect);

        var result = ExpressionSyntaxTranslator.Translate(lambdaBody, context);
        if (!result.IsSuccess)
            return null;

        // Check for direction parameter
        var isDescending = false;
        if (invocation.ArgumentList.Arguments.Count >= 2)
        {
            var directionArg = invocation.ArgumentList.Arguments[1].Expression;
            if (directionArg is MemberAccessExpressionSyntax memberAccess)
            {
                var memberName = memberAccess.Name.Identifier.Text;
                isDescending = memberName == "Descending" || memberName == "Desc";
            }
        }

        return new OrderByClauseInfo(result.Sql!, isDescending, result.Parameters);
    }

    /// <summary>
    /// Builds an ExpressionTranslationContext for N entities with positional aliases.
    /// </summary>
    private static ExpressionTranslationContext BuildMultiEntityContext(
        IReadOnlyList<EntityInfo> entities,
        IReadOnlyList<string> paramNames,
        SqlDialect dialect)
    {
        var context = new ExpressionTranslationContext(null!, entities[0], dialect, paramNames[0]);
        for (int i = 1; i < entities.Count; i++)
        {
            context = context.WithJoinedEntity(paramNames[i], entities[i]);
        }

        var aliases = new Dictionary<string, string>();
        for (int i = 0; i < paramNames.Count; i++)
        {
            aliases[paramNames[i]] = $"t{i}";
        }
        return context.WithTableAliases(aliases);
    }

    /// <summary>
    /// Creates a translation context for join expressions with two entity types.
    /// </summary>
    private static ExpressionTranslationContext CreateJoinTranslationContext(
        SemanticModel semanticModel,
        ITypeSymbol leftEntityType,
        ITypeSymbol rightEntityType,
        SqlDialect dialect,
        string leftParamName,
        string rightParamName)
    {
        // Build column info from both type symbols
        var leftColumns = BuildColumnInfoFromTypeSymbol(leftEntityType);
        var rightColumns = BuildColumnInfoFromTypeSymbol(rightEntityType);

        // Create entity info for the primary entity (left side)
        var entityInfo = new EntityInfo(
            entityName: leftEntityType.Name,
            schemaClassName: leftEntityType.Name + "Schema",
            schemaNamespace: leftEntityType.ContainingNamespace?.ToDisplayString() ?? "",
            tableName: leftEntityType.Name.ToLowerInvariant(),
            namingStyle: NamingStyleKind.Exact,
            columns: leftColumns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None);

        // Create a context that knows about both entities
        var context = new ExpressionTranslationContext(semanticModel, entityInfo, dialect, leftParamName);

        // Add the right entity info to the context
        var rightEntityInfo = new EntityInfo(
            entityName: rightEntityType.Name,
            schemaClassName: rightEntityType.Name + "Schema",
            schemaNamespace: rightEntityType.ContainingNamespace?.ToDisplayString() ?? "",
            tableName: rightEntityType.Name.ToLowerInvariant(),
            namingStyle: NamingStyleKind.Exact,
            columns: rightColumns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None);

        context = context.WithJoinedEntity(rightParamName, rightEntityInfo);

        // Set up positional aliases: t0 for left, t1 for right
        var aliases = new Dictionary<string, string>
        {
            [leftParamName] = "t0",
            [rightParamName] = "t1"
        };
        context = context.WithTableAliases(aliases);

        return context;
    }

    /// <summary>
    /// Extracts the lambda body and two parameter names from an invocation's first argument.
    /// </summary>
    private static (ExpressionSyntax Body, string LeftParamName, string RightParamName)? ExtractTwoParameterLambdaExpression(
        InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        if (argument is not ParenthesizedLambdaExpressionSyntax lambda)
            return null;

        if (lambda.ParameterList.Parameters.Count != 2)
            return null;

        if (lambda.Body is not ExpressionSyntax body)
            return null;

        var leftParamName = lambda.ParameterList.Parameters[0].Identifier.Text;
        var rightParamName = lambda.ParameterList.Parameters[1].Identifier.Text;

        return (body, leftParamName, rightParamName);
    }

    /// <summary>
    /// Extracts the lambda body and N parameter names from an invocation's first argument.
    /// </summary>
    internal static (ExpressionSyntax Body, IReadOnlyList<string> ParamNames)?
        ExtractMultiParameterLambdaExpression(InvocationExpressionSyntax invocation, int expectedParamCount)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        if (argument is not ParenthesizedLambdaExpressionSyntax lambda)
            return null;

        if (lambda.ParameterList.Parameters.Count < expectedParamCount)
            return null;

        if (lambda.Body is not ExpressionSyntax body)
            return null;

        var paramNames = new List<string>(expectedParamCount);
        for (int i = 0; i < expectedParamCount; i++)
        {
            paramNames.Add(lambda.ParameterList.Parameters[i].Identifier.Text);
        }

        return (body, paramNames);
    }

    /// <summary>
    /// Gets a simple type name from a type symbol.
    /// </summary>
    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        // Handle Nullable<T>
        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length > 0)
        {
            return GetSimpleTypeName(namedType.TypeArguments[0]);
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_String => "string",
            SpecialType.System_Char => "char",
            SpecialType.System_DateTime => "DateTime",
            _ => type.Name
        };
    }

    /// <summary>
    /// Quotes an identifier according to the SQL dialect.
    /// </summary>
    private static string QuoteIdentifier(string identifier, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\"" // SQLite, PostgreSQL
        };
    }
}
