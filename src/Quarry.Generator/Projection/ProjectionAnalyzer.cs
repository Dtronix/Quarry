using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Shared.Migration;
using Quarry.Generators.Translation;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.Projection;

/// <summary>
/// Analyzes Select() lambda expressions to extract projection metadata.
/// This runs at compile-time in the source generator.
/// </summary>
internal static class ProjectionAnalyzer
{
    /// <summary>
    /// Analyzes a Select() invocation to extract projection information.
    /// </summary>
    /// <param name="invocation">The Select() invocation syntax.</param>
    /// <param name="semanticModel">The semantic model for type resolution.</param>
    /// <param name="entityInfo">The entity metadata.</param>
    /// <param name="dialect">The SQL dialect for quoting.</param>
    /// <returns>The analyzed projection information.</returns>
    public static ProjectionInfo Analyze(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        EntityInfo entityInfo,
        SqlDialect dialect)
    {
        var columnLookup = BuildColumnLookup(entityInfo);
        return AnalyzeCore(invocation, semanticModel, columnLookup, entityInfo.Columns, entityInfo.EntityName, dialect);
    }

    /// <summary>
    /// Analyzes a Select() invocation using type symbol metadata.
    /// Use this when EntityInfo is not available (during usage site discovery).
    /// </summary>
    /// <param name="invocation">The Select() invocation syntax.</param>
    /// <param name="semanticModel">The semantic model for type resolution.</param>
    /// <param name="entityType">The entity type symbol.</param>
    /// <param name="dialect">The SQL dialect for quoting.</param>
    /// <returns>The analyzed projection information.</returns>
    public static ProjectionInfo AnalyzeFromTypeSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType,
        SqlDialect dialect)
    {
        // Build column info from the type symbol's properties
        var (columns, columnLookup) = BuildColumnInfoFromTypeSymbol(entityType);
        return AnalyzeCore(invocation, semanticModel, columnLookup, columns, entityType.Name, dialect);
    }

    /// <summary>
    /// Analyzes a joined Select() invocation using syntax only — no EntityInfo required.
    /// Creates placeholder columns with PropertyName and TableAlias set but ClrType/ColumnName empty.
    /// These are enriched later using EntityRef columns in the pipeline bridge.
    /// </summary>
    public static ProjectionInfo AnalyzeJoinedSyntaxOnly(
        InvocationExpressionSyntax invocation,
        int entityCount,
        SqlDialect dialect,
        SemanticModel? semanticModel = null)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return ProjectionInfo.CreateFailed("object", "Select() requires a lambda argument");

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (argument is not ParenthesizedLambdaExpressionSyntax lambda)
            return ProjectionInfo.CreateFailed("object", "Joined Select() argument must be a parenthesized lambda");

        if (lambda.Body is not ExpressionSyntax body)
            return ProjectionInfo.CreateFailed("object", "Lambda body must be an expression");

        var paramCount = lambda.ParameterList.Parameters.Count;
        if (paramCount < entityCount)
            return ProjectionInfo.CreateFailed("object", $"Expected {entityCount} lambda parameters, got {paramCount}");

        // Build per-parameter info with empty column lookups (no EntityInfo available)
        var perParamLookup = new Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)>(StringComparer.Ordinal);
        for (int i = 0; i < entityCount; i++)
        {
            var paramName = lambda.ParameterList.Parameters[i].Identifier.Text;
            perParamLookup[paramName] = (new Dictionary<string, ColumnInfo>(StringComparer.Ordinal), $"t{i}");
        }

        var resultType = InferResultTypeFromSyntax(body);

        // Analyze using placeholder resolution — column lookups are empty, so
        // ResolveJoinedColumn will fall through. We use a dedicated placeholder path.
        var projectionParams = new List<ParameterInfo>();
        var result = AnalyzeJoinedExpressionWithPlaceholders(body, perParamLookup, resultType, dialect, semanticModel, projectionParams);

        // Attach collected projection parameters to the result
        if (projectionParams.Count > 0)
        {
            result = new ProjectionInfo(
                result.Kind, result.ResultTypeName, result.Columns,
                result.IsOptimalPath, result.NonOptimalReason, result.FailureReason,
                result.CustomEntityReaderClass, result.JoinedEntityAlias,
                projectionParameters: projectionParams);
        }

        if (result.Kind == ProjectionKind.Tuple && result.Columns.Count > 0)
        {
            // Don't try to build tuple type name — types are empty placeholders
            // ResultTypeName will be rebuilt during enrichment
        }

        return result;
    }

    /// <summary>
    /// Analyzes a single-entity Select() invocation using syntax only — no EntityInfo or SemanticModel required.
    /// Handles both SimpleLambdaExpressionSyntax (u => ...) and ParenthesizedLambdaExpressionSyntax ((u) => ...).
    /// Creates placeholder columns with PropertyName set but ClrType/ColumnName empty.
    /// Used by DiscoverPostCteSites for non-joined CTE chains where the entity type is unresolved.
    /// </summary>
    public static ProjectionInfo AnalyzeSingleEntitySyntaxOnly(
        InvocationExpressionSyntax invocation,
        SqlDialect dialect)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return ProjectionInfo.CreateFailed("object", "Select() requires a lambda argument");

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        ExpressionSyntax? body;
        string paramName;

        if (argument is SimpleLambdaExpressionSyntax simpleLambda)
        {
            body = simpleLambda.Body as ExpressionSyntax;
            paramName = simpleLambda.Parameter.Identifier.Text;
        }
        else if (argument is ParenthesizedLambdaExpressionSyntax parenLambda
                 && parenLambda.ParameterList.Parameters.Count == 1)
        {
            body = parenLambda.Body as ExpressionSyntax;
            paramName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
        }
        else
        {
            return ProjectionInfo.CreateFailed("object", "Select() argument must be a single-parameter lambda");
        }

        if (body == null)
            return ProjectionInfo.CreateFailed("object", "Lambda body must be an expression");

        // Use the joined placeholder path with entityCount=1 and no table alias
        var perParamLookup = new Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)>(StringComparer.Ordinal)
        {
            [paramName] = (new Dictionary<string, ColumnInfo>(StringComparer.Ordinal), "")
        };

        var resultType = InferResultTypeFromSyntax(body);
        return AnalyzeJoinedExpressionWithPlaceholders(body, perParamLookup, resultType, dialect);
    }

    /// <summary>
    /// Analyzes a joined projection body with placeholder column resolution.
    /// When column metadata is unavailable, creates columns with PropertyName and TableAlias
    /// but empty ClrType/ColumnName (to be enriched later).
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedExpressionWithPlaceholders(
        ExpressionSyntax expression,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax =>
                ProjectionInfo.CreateFailed(resultType,
                    "Anonymous type projections are not supported. Use a named record, class, or tuple instead.",
                    ProjectionFailureReason.AnonymousTypeNotSupported),

            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null =>
                AnalyzeJoinedInitializerWithPlaceholders(objectCreation.Initializer.Expressions, perParamLookup, resultType, ProjectionKind.Dto, dialect, semanticModel, projectionParams),

            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeJoinedInitializerWithPlaceholders(implicitCreation.Initializer.Expressions, perParamLookup, resultType, ProjectionKind.Dto, dialect, semanticModel, projectionParams),

            TupleExpressionSyntax tuple =>
                AnalyzeJoinedTupleWithPlaceholders(tuple, perParamLookup, resultType, dialect, semanticModel, projectionParams),

            MemberAccessExpressionSyntax memberAccess when IsJoinedMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumnWithPlaceholder(memberAccess, perParamLookup, resultType),

            // Navigation member access: o.User.UserName (chained member access rooted on a parameter)
            MemberAccessExpressionSyntax memberAccess when IsNavigationMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumnWithPlaceholder(memberAccess, perParamLookup, resultType),

            InvocationExpressionSyntax invocation when IsAggregateCall(invocation) =>
                AnalyzeJoinedInvocation(invocation, perParamLookup, resultType, dialect, semanticModel, projectionParams),

            // Whole entity: (s, u) => u
            IdentifierNameSyntax identifier when perParamLookup.ContainsKey(identifier.Identifier.Text) =>
                AnalyzeJoinedEntityProjection(identifier.Identifier.Text, perParamLookup, resultType, dialect),

            _ => ProjectionInfo.CreateFailed(resultType, $"Unsupported joined projection expression: {expression.Kind()}")
        };
    }

    /// <summary>
    /// Analyzes a joined entity projection where a single lambda parameter is selected:
    /// .Select((s, u) => u) — selects all columns from the entity corresponding to parameter u.
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedEntityProjection(
        string parameterName,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect)
    {
        if (!perParamLookup.TryGetValue(parameterName, out var entry))
            return ProjectionInfo.CreateFailed(resultType, $"Unknown parameter '{parameterName}' in joined entity projection");

        // Placeholder path: column lookup is empty at discovery time (no EntityInfo available).
        // Set JoinedEntityAlias so BuildProjection can populate columns from the registry later.
        if (entry.Lookup.Count == 0)
            return new ProjectionInfo(ProjectionKind.Dto, resultType, Array.Empty<ProjectedColumn>(), joinedEntityAlias: entry.Alias);

        var columns = new List<ProjectedColumn>();
        var ordinal = 0;
        foreach (var kvp in entry.Lookup)
        {
            var col = kvp.Value;
            columns.Add(new ProjectedColumn(
                propertyName: col.PropertyName,
                columnName: col.ColumnName,
                clrType: col.ClrType,
                fullClrType: col.FullClrType,
                isNullable: col.IsNullable,
                ordinal: ordinal++,
                customTypeMapping: col.CustomTypeMappingClass,
                isValueType: col.IsValueType,
                readerMethodName: col.DbReaderMethodName ?? col.ReaderMethodName,
                tableAlias: entry.Alias,
                isForeignKey: col.Kind == ColumnKind.ForeignKey,
                foreignKeyEntityName: col.ReferencedEntityName,
                isEnum: col.IsEnum));
        }

        // Use Dto kind (not Entity) because entity identity projections assume the primary entity.
        // For joined projections selecting a secondary entity, Dto ensures correct column-by-column
        // materialization with the right entity type and table alias.
        return new ProjectionInfo(ProjectionKind.Dto, resultType, columns);
    }

    private static ProjectionInfo AnalyzeJoinedTupleWithPlaceholders(
        TupleExpressionSyntax tuple,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var argument in tuple.Arguments)
        {
            var propertyName = argument.NameColon?.Name.Identifier.Text
                ?? GetImplicitPropertyName(argument.Expression)
                ?? $"Item{ordinal + 1}";

            var col = ResolveJoinedProjectedExpressionWithPlaceholder(argument.Expression, perParamLookup, propertyName, ordinal++, dialect, semanticModel, projectionParams);
            if (col == null)
                return ProjectionInfo.CreateFailed(resultType, $"Could not analyze tuple element at position {ordinal}");

            columns.Add(col);
        }

        return new ProjectionInfo(ProjectionKind.Tuple, resultType, columns);
    }

    private static ProjectionInfo AnalyzeJoinedInitializerWithPlaceholders(
        SeparatedSyntaxList<ExpressionSyntax> expressions,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        ProjectionKind kind,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var expr in expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
                return ProjectionInfo.CreateFailed(resultType, "Object initializer must use property assignments");

            var propertyName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propertyName == null)
                return ProjectionInfo.CreateFailed(resultType, "Could not determine property name in initializer");

            var col = ResolveJoinedProjectedExpressionWithPlaceholder(assignment.Right, perParamLookup, propertyName, ordinal++, dialect, semanticModel, projectionParams);
            if (col == null)
                return ProjectionInfo.CreateFailed(resultType, $"Could not analyze projection for property '{propertyName}'");

            columns.Add(col);
        }

        return new ProjectionInfo(kind, resultType, columns);
    }

    private static ProjectedColumn? ResolveJoinedProjectedExpressionWithPlaceholder(
        ExpressionSyntax expression,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string propertyName,
        int ordinal,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return ResolveJoinedColumnWithPlaceholder(memberAccess, perParamLookup, propertyName, ordinal);

        if (expression is InvocationExpressionSyntax invocation && IsAggregateCall(invocation))
            return ResolveJoinedAggregate(invocation, perParamLookup, propertyName, ordinal, dialect, semanticModel, projectionParams);

        return null;
    }

    /// <summary>
    /// Resolves a joined column with placeholder data when column metadata is unavailable.
    /// </summary>
    private static ProjectedColumn? ResolveJoinedColumnWithPlaceholder(
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string propertyName,
        int ordinal)
    {
        // Direct property access: u.Name
        if (memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            var paramName = identifier.Identifier.Text;
            if (perParamLookup.TryGetValue(paramName, out var info))
            {
                var colName = memberAccess.Name.Identifier.Text;
                // Try column lookup first (will be empty for syntax-only analysis)
                if (info.Lookup.TryGetValue(colName, out var columnInfo))
                {
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: columnInfo.ColumnName,
                        clrType: columnInfo.ClrType,
                        fullClrType: columnInfo.FullClrType,
                        isNullable: columnInfo.IsNullable,
                        ordinal: ordinal,
                        customTypeMapping: columnInfo.CustomTypeMappingClass,
                        isValueType: columnInfo.IsValueType,
                        readerMethodName: columnInfo.DbReaderMethodName ?? columnInfo.ReaderMethodName,
                        tableAlias: info.Alias);
                }

                // Placeholder: PropertyName and TableAlias known, types will be enriched later.
                // Store the entity member name (colName) so enrichment can match by it
                // even when PropertyName is a user alias (e.g. named tuple elements).
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: colName,
                    clrType: "",
                    fullClrType: "",
                    isNullable: false,
                    ordinal: ordinal,
                    tableAlias: info.Alias);
            }
        }

        // Ref<T,K>.Id access: u.OrderId.Id
        if (memberAccess.Name.Identifier.Text == "Id" &&
            memberAccess.Expression is MemberAccessExpressionSyntax nestedAccess &&
            nestedAccess.Expression is IdentifierNameSyntax nestedId)
        {
            var paramName = nestedId.Identifier.Text;
            if (perParamLookup.TryGetValue(paramName, out var info))
            {
                var refPropertyName = nestedAccess.Name.Identifier.Text;
                // Placeholder for FK reference
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
                    clrType: "",
                    fullClrType: "",
                    isNullable: false,
                    ordinal: ordinal,
                    tableAlias: info.Alias,
                    isForeignKey: true);
            }
        }

        // Navigation access: o.User.UserName
        var navChain = TryParseNavigationChainJoined(memberAccess, perParamLookup);
        if (navChain != null)
        {
            return new ProjectedColumn(
                propertyName: propertyName,
                columnName: navChain.Value.FinalProp,
                clrType: "",
                fullClrType: "",
                isNullable: false,
                ordinal: ordinal,
                tableAlias: navChain.Value.SourceAlias,
                navigationHops: navChain.Value.Hops);
        }

        return null;
    }

    private static ProjectionInfo AnalyzeJoinedSingleColumnWithPlaceholder(
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType)
    {
        var propertyName = memberAccess.Name.Identifier.Text;
        var col = ResolveJoinedColumnWithPlaceholder(memberAccess, perParamLookup, propertyName, 0);
        if (col == null)
            return ProjectionInfo.CreateFailed(resultType, "Could not resolve single column projection");
        return new ProjectionInfo(ProjectionKind.SingleColumn, resultType, new[] { col });
    }

    /// <summary>
    /// Analyzes a joined Select() invocation using entity metadata.
    /// Uses EntityInfo for column metadata. Does not require SemanticModel — result type
    /// is inferred from the syntax (DTO type name, tuple structure, or column type).
    /// </summary>
    public static ProjectionInfo AnalyzeJoined(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<EntityInfo> entities,
        SqlDialect dialect)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return ProjectionInfo.CreateFailed("object", "Select() requires a lambda argument");

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (argument is not ParenthesizedLambdaExpressionSyntax lambda)
            return ProjectionInfo.CreateFailed("object", "Joined Select() argument must be a parenthesized lambda");

        if (lambda.Body is not ExpressionSyntax body)
            return ProjectionInfo.CreateFailed("object", "Lambda body must be an expression");

        var paramCount = lambda.ParameterList.Parameters.Count;
        if (paramCount < entities.Count)
            return ProjectionInfo.CreateFailed("object", $"Expected {entities.Count} lambda parameters, got {paramCount}");

        // Build per-parameter column lookups and alias map
        var perParamLookup = new Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)>(StringComparer.Ordinal);
        for (int i = 0; i < entities.Count; i++)
        {
            var paramName = lambda.ParameterList.Parameters[i].Identifier.Text;
            var lookup = BuildColumnLookup(entities[i]);
            perParamLookup[paramName] = (lookup, $"t{i}");
        }

        // Infer result type from the lambda body syntax
        var resultType = InferResultTypeFromSyntax(body);

        // Analyze the body expression (no SemanticModel needed — uses EntityInfo column lookups)
        var result = AnalyzeJoinedExpression(body, null, perParamLookup, resultType, dialect);

        // Tuple type fixup
        if (result.Kind == ProjectionKind.Tuple && result.Columns.Count > 0)
        {
            var tupleTypeName = TypeClassification.BuildTupleTypeName(result.Columns);
            if (IsValidTupleTypeName(tupleTypeName) && !IsValidTupleTypeName(result.ResultTypeName))
            {
                return new ProjectionInfo(result.Kind, tupleTypeName, result.Columns, result.IsOptimalPath, result.NonOptimalReason, result.FailureReason);
            }
        }

        return result;
    }

    /// <summary>
    /// Infers the result type name from the lambda body syntax.
    /// </summary>
    private static string InferResultTypeFromSyntax(ExpressionSyntax body)
    {
        return body switch
        {
            ObjectCreationExpressionSyntax objCreation => objCreation.Type.ToString(),
            ImplicitObjectCreationExpressionSyntax => "object", // Will be fixed during enrichment
            TupleExpressionSyntax => "object", // Will be rebuilt from columns
            _ => "object"
        };
    }

    /// <summary>
    /// Analyzes a joined projection body expression.
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedExpression(
        ExpressionSyntax expression,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        return expression switch
        {
            // Anonymous type: rejected
            AnonymousObjectCreationExpressionSyntax =>
                ProjectionInfo.CreateFailed(resultType,
                    "Anonymous type projections are not supported. Use a named record, class, or tuple instead.",
                    ProjectionFailureReason.AnonymousTypeNotSupported),

            // DTO/Object initializer: (u, o) => new Dto { Name = u.Name, Total = o.Total }
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null =>
                AnalyzeJoinedInitializer(objectCreation.Initializer.Expressions, semanticModel, perParamLookup, resultType, ProjectionKind.Dto, dialect, projectionParams),

            // Implicit object creation
            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeJoinedInitializer(implicitCreation.Initializer.Expressions, semanticModel, perParamLookup, resultType, ProjectionKind.Dto, dialect, projectionParams),

            // Tuple: (u, o) => (u.Name, o.Total)
            TupleExpressionSyntax tuple =>
                AnalyzeJoinedTuple(tuple, semanticModel, perParamLookup, resultType, dialect, projectionParams),

            // Single column: (u, o) => u.Name
            MemberAccessExpressionSyntax memberAccess when IsJoinedMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumn(memberAccess, semanticModel, perParamLookup, resultType, dialect),

            // Navigation member access: o.User.UserName (chained member access rooted on a parameter)
            MemberAccessExpressionSyntax memberAccess when IsNavigationMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumn(memberAccess, semanticModel, perParamLookup, resultType, dialect),

            // Aggregate function: (u, o) => Sql.Count()
            InvocationExpressionSyntax invocation when IsAggregateCall(invocation) =>
                AnalyzeJoinedInvocation(invocation, perParamLookup, resultType, dialect, semanticModel, projectionParams),

            // Whole entity: (s, u) => u
            IdentifierNameSyntax identifier when perParamLookup.ContainsKey(identifier.Identifier.Text) =>
                AnalyzeJoinedEntityProjection(identifier.Identifier.Text, perParamLookup, resultType, dialect),

            _ => ProjectionInfo.CreateFailed(resultType, $"Unsupported joined projection expression: {expression.Kind()}")
        };
    }

    /// <summary>
    /// Checks if an invocation is a Sql.* aggregate call.
    /// </summary>
    private static bool IsAggregateCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is IdentifierNameSyntax identifier &&
               identifier.Identifier.Text == "Sql";
    }

    /// <summary>
    /// Checks if a member access is on one of the joined lambda parameters.
    /// </summary>
    private static bool IsJoinedMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup)
    {
        return memberAccess.Expression is IdentifierNameSyntax id && perParamLookup.ContainsKey(id.Identifier.Text);
    }

    /// <summary>
    /// Analyzes a single column projection in a joined context.
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedSingleColumn(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect)
    {
        var col = ResolveJoinedColumn(memberAccess, semanticModel, perParamLookup, memberAccess.Name.Identifier.Text, 0);
        if (col == null)
            return ProjectionInfo.CreateFailed(resultType, "Could not resolve joined single column");

        // Always derive result type from the resolved column — syntax-inferred "object" is wrong
        var actualResultType = col.IsNullable ? $"{col.FullClrType}?" : col.FullClrType;

        return new ProjectionInfo(ProjectionKind.SingleColumn, actualResultType, new[] { col });
    }

    /// <summary>
    /// Analyzes a standalone aggregate invocation in a joined context: (u, o) => Sql.Count()
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedInvocation(
        InvocationExpressionSyntax invocation,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetJoinedAggregateInfo(methodName, invocation, perParamLookup, semanticModel, projectionParams);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                var column = new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: IsConvertedTypeNullable(semanticModel, invocation),
                    ordinal: 0,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true,
                    readerMethodName: TypeClassification.GetReaderMethod(aggregateClrType));

                return new ProjectionInfo(ProjectionKind.SingleColumn, aggregateClrType, new[] { column });
            }
        }

        return ProjectionInfo.CreateFailed(resultType, "Unsupported invocation in joined projection");
    }

    /// <summary>
    /// Analyzes an object initializer in a joined context.
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedInitializer(
        SeparatedSyntaxList<ExpressionSyntax> expressions,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        ProjectionKind kind,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var expr in expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
                return ProjectionInfo.CreateFailed(resultType, "Object initializer must use property assignments");

            var propertyName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propertyName == null)
                return ProjectionInfo.CreateFailed(resultType, "Could not determine property name in initializer");

            var col = ResolveJoinedProjectedExpression(assignment.Right, semanticModel, perParamLookup, propertyName, ordinal++, dialect, projectionParams);
            if (col == null)
                return ProjectionInfo.CreateFailed(resultType, $"Could not analyze projection for property '{propertyName}'");

            columns.Add(col);
        }

        return new ProjectionInfo(kind, resultType, columns);
    }

    /// <summary>
    /// Analyzes a tuple projection in a joined context.
    /// </summary>
    private static ProjectionInfo AnalyzeJoinedTuple(
        TupleExpressionSyntax tuple,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var argument in tuple.Arguments)
        {
            var propertyName = argument.NameColon?.Name.Identifier.Text
                ?? GetImplicitPropertyName(argument.Expression)
                ?? $"Item{ordinal + 1}";

            var col = ResolveJoinedProjectedExpression(argument.Expression, semanticModel, perParamLookup, propertyName, ordinal++, dialect, projectionParams);
            if (col == null)
                return ProjectionInfo.CreateFailed(resultType, $"Could not analyze tuple element at position {ordinal}");

            columns.Add(col);
        }

        return new ProjectionInfo(ProjectionKind.Tuple, resultType, columns);
    }

    /// <summary>
    /// Resolves a projected expression in a joined context (dispatches to column, Ref, or aggregate resolution).
    /// </summary>
    private static ProjectedColumn? ResolveJoinedProjectedExpression(
        ExpressionSyntax expression,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string propertyName,
        int ordinal,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return ResolveJoinedColumn(memberAccess, semanticModel, perParamLookup, propertyName, ordinal);

        // Aggregate functions: Sql.Count(), Sql.Sum(u.Amount)
        if (expression is InvocationExpressionSyntax invocation && IsAggregateCall(invocation))
            return ResolveJoinedAggregate(invocation, perParamLookup, propertyName, ordinal, dialect, semanticModel, projectionParams);

        return null;
    }

    /// <summary>
    /// Resolves an aggregate invocation (Sql.Count(), Sql.Sum(u.Amount)) in a joined context.
    /// </summary>
    private static ProjectedColumn? ResolveJoinedAggregate(
        InvocationExpressionSyntax invocation,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string propertyName,
        int ordinal,
        SqlDialect dialect,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetJoinedAggregateInfo(methodName, invocation, perParamLookup, semanticModel, projectionParams);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";

                // Extract the table alias from the first column argument (e.g., o.Total → "t1")
                // so enrichment can resolve unresolved types from entity column metadata.
                string? tableAlias = null;
                var args = invocation.ArgumentList.Arguments;
                if (args.Count > 0 &&
                    args[0].Expression is MemberAccessExpressionSyntax colAccess &&
                    colAccess.Expression is IdentifierNameSyntax paramId &&
                    perParamLookup.TryGetValue(paramId.Identifier.Text, out var paramInfo))
                {
                    tableAlias = paramInfo.Alias;
                }

                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: IsConvertedTypeNullable(semanticModel, invocation),
                    ordinal: ordinal,
                    alias: propertyName,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true,
                    readerMethodName: TypeClassification.GetReaderMethod(aggregateClrType),
                    tableAlias: tableAlias);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a member access expression (u.Name or u.OrderId.Id) against the per-parameter column lookup.
    /// </summary>
    private static ProjectedColumn? ResolveJoinedColumn(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel? semanticModel,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string propertyName,
        int ordinal)
    {
        // Direct property access: u.Name
        if (memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            var paramName = identifier.Identifier.Text;
            if (perParamLookup.TryGetValue(paramName, out var info))
            {
                var colName = memberAccess.Name.Identifier.Text;
                if (info.Lookup.TryGetValue(colName, out var columnInfo))
                {
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: columnInfo.ColumnName,
                        clrType: columnInfo.ClrType,
                        fullClrType: columnInfo.FullClrType,
                        isNullable: columnInfo.IsNullable,
                        ordinal: ordinal,
                        customTypeMapping: columnInfo.CustomTypeMappingClass,
                        isValueType: columnInfo.IsValueType,
                        readerMethodName: columnInfo.DbReaderMethodName ?? columnInfo.ReaderMethodName,
                        tableAlias: info.Alias);
                }

                // Fallback to semantic model (when available)
                if (semanticModel == null)
                    return null;
                var memberSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                if (memberSymbol is IPropertySymbol propSymbol)
                {
                    var propType = propSymbol.Type;
                    var (isValueType, readerMethodName, _) = ColumnInfo.GetTypeMetadata(propType);
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: colName,
                        clrType: GetSimpleTypeName(propType),
                        fullClrType: propType.ToDisplayString(),
                        isNullable: propType.NullableAnnotation == NullableAnnotation.Annotated,
                        ordinal: ordinal,
                        isValueType: isValueType,
                        readerMethodName: readerMethodName,
                        tableAlias: info.Alias);
                }
            }
        }

        // Ref<T,K>.Id access: u.OrderId.Id
        if (memberAccess.Name.Identifier.Text == "Id" &&
            memberAccess.Expression is MemberAccessExpressionSyntax nestedAccess &&
            nestedAccess.Expression is IdentifierNameSyntax nestedId)
        {
            var paramName = nestedId.Identifier.Text;
            if (perParamLookup.TryGetValue(paramName, out var info))
            {
                var refPropertyName = nestedAccess.Name.Identifier.Text;
                if (info.Lookup.TryGetValue(refPropertyName, out var refColumn) &&
                    refColumn.Kind == ColumnKind.ForeignKey)
                {
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: refColumn.ColumnName,
                        clrType: refColumn.ClrType,
                        fullClrType: refColumn.FullClrType,
                        isNullable: refColumn.IsNullable,
                        ordinal: ordinal,
                        isValueType: refColumn.IsValueType,
                        readerMethodName: refColumn.ReaderMethodName,
                        tableAlias: info.Alias);
                }
            }
        }

        // Navigation access: o.User.UserName
        var navChainJ = TryParseNavigationChainJoined(memberAccess, perParamLookup);
        if (navChainJ != null)
        {
            return new ProjectedColumn(
                propertyName: propertyName,
                columnName: navChainJ.Value.FinalProp,
                clrType: "",
                fullClrType: "",
                isNullable: false,
                ordinal: ordinal,
                tableAlias: navChainJ.Value.SourceAlias,
                navigationHops: navChainJ.Value.Hops);
        }

        return null;
    }

    /// <summary>
    /// Core analysis logic shared by both overloads.
    /// </summary>
    private static ProjectionInfo AnalyzeCore(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        IReadOnlyList<ColumnInfo> columns,
        string entityName,
        SqlDialect dialect)
    {
        // Get the lambda argument from Select(expr)
        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return ProjectionInfo.CreateFailed("object", "Select() requires a lambda argument");
        }

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        // Handle lambda expressions: u => ...
        if (argument is not LambdaExpressionSyntax lambda)
        {
            return ProjectionInfo.CreateFailed("object", "Select() argument must be a lambda expression");
        }

        // Get the lambda body
        ExpressionSyntax body;
        string lambdaParameterName;

        if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
        {
            body = simpleLambda.Body as ExpressionSyntax
                ?? throw new InvalidOperationException("Lambda body must be an expression");
            lambdaParameterName = simpleLambda.Parameter.Identifier.Text;
        }
        else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            body = parenLambda.Body as ExpressionSyntax
                ?? throw new InvalidOperationException("Lambda body must be an expression");
            lambdaParameterName = parenLambda.ParameterList.Parameters.Count > 0
                ? parenLambda.ParameterList.Parameters[0].Identifier.Text
                : "x";
        }
        else
        {
            return ProjectionInfo.CreateFailed("object", "Unsupported lambda expression type");
        }

        // Determine the result type from the method's type argument
        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var resultType = "object";

        if (methodSymbol?.TypeArguments.Length > 0)
        {
            var typeArg = methodSymbol.TypeArguments[methodSymbol.TypeArguments.Length - 1];

            // For tuple types, build the type name directly from the tuple elements
            // This is more reliable than ToDisplayString() which may show unresolved types
            if (typeArg is INamedTypeSymbol namedType && namedType.IsTupleType)
            {
                resultType = BuildTupleTypeNameFromSymbol(namedType);
            }
            else
            {
                resultType = typeArg.ToDisplayString();
            }
        }

        // Analyze the lambda body — collect projection parameters for window function scalar args
        var projectionParams = new List<ParameterInfo>();
        var result = AnalyzeExpression(body, semanticModel, columns, columnLookup, lambdaParameterName, resultType, entityName, dialect, projectionParams);

        // Attach collected projection parameters to the result
        if (projectionParams.Count > 0)
        {
            result = new ProjectionInfo(
                result.Kind, result.ResultTypeName, result.Columns,
                result.IsOptimalPath, result.NonOptimalReason, result.FailureReason,
                result.CustomEntityReaderClass, result.JoinedEntityAlias,
                projectionParameters: projectionParams);
        }

        // Post-analysis fixup: If result type is still invalid and we have column info, derive from columns
        if (TypeClassification.IsUnresolvedTypeNameLenient(result.ResultTypeName) && result.Columns.Count > 0)
        {
            // For single column, use the column's type
            if (result.Columns.Count == 1)
            {
                var col = result.Columns[0];
                var fixedType = col.IsNullable ? $"{col.FullClrType}?" : col.FullClrType;
                return new ProjectionInfo(
                    result.Kind,
                    fixedType,
                    result.Columns,
                    result.IsOptimalPath,
                    result.NonOptimalReason,
                    result.FailureReason,
                    projectionParameters: result.ProjectionParameters);
            }
        }

        // Tuple type fixup: If this is a tuple projection, rebuild the type name from columns
        // This is more reliable than using the semantic model which may have unresolved types
        if (result.Kind == ProjectionKind.Tuple && result.Columns.Count > 0)
        {
            var tupleTypeName = TypeClassification.BuildTupleTypeName(result.Columns);
            if (IsValidTupleTypeName(tupleTypeName) && !IsValidTupleTypeName(result.ResultTypeName))
            {
                return new ProjectionInfo(
                    result.Kind,
                    tupleTypeName,
                    result.Columns,
                    result.IsOptimalPath,
                    result.NonOptimalReason,
                    result.FailureReason,
                    projectionParameters: result.ProjectionParameters);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds column info from a type symbol's properties.
    /// Used when we don't have full EntityInfo available.
    /// </summary>
    private static (IReadOnlyList<ColumnInfo> Columns, Dictionary<string, ColumnInfo> Lookup) BuildColumnInfoFromTypeSymbol(ITypeSymbol typeSymbol)
    {
        var columns = new List<ColumnInfo>();
        var lookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);

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

            // Determine column name (use property name by default - naming conventions not applied here)
            var columnName = property.Name;

            // Get CLR type info
            var propertyType = property.Type;
            var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated ||
                             (propertyType is INamedTypeSymbol namedType &&
                              namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

            var clrType = GetSimpleTypeName(propertyType);
            var fullClrType = propertyType.ToDisplayString();

            // Determine column kind. Foreign keys are detected by *type* — only the
            // generated `Quarry.EntityRef<TEntity, TKey>` (or its `Nullable` wrapping) is
            // treated as a foreign key. Name-based heuristics ("ends with Id") produce
            // false positives for primary keys like `OrderId` on the `Order` entity, and
            // for unrelated DTO properties, which previously left `ReferencedEntityName`
            // null and triggered an NRE downstream in `EmitDiagnosticsConstruction`.
            var kind = ColumnKind.Standard;
            string? referencedEntityName = null;

            // Unwrap Nullable<T> so a property typed as `EntityRef<User, int>?` still
            // resolves to a foreign key.
            var fkCandidate = propertyType;
            if (fkCandidate is INamedTypeSymbol nullableNamed
                && nullableNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullableNamed.TypeArguments.Length == 1)
            {
                fkCandidate = nullableNamed.TypeArguments[0];
            }

            if (fkCandidate is INamedTypeSymbol entityRefType
                && entityRefType.IsGenericType
                && entityRefType.Name == "EntityRef"
                && entityRefType.TypeArguments.Length == 2
                && IsQuarryNamespace(entityRefType.ContainingNamespace))
            {
                kind = ColumnKind.ForeignKey;
                referencedEntityName = entityRefType.TypeArguments[0].Name;
            }
            else if (property.Name == "Id"
                     || (property.Name.EndsWith("Id") && property.Name == typeSymbol.Name + "Id"))
            {
                kind = ColumnKind.PrimaryKey;
            }

            // Get type metadata from the type symbol
            var (isValueType, readerMethodName, _) = ColumnInfo.GetTypeMetadata(propertyType);

            var column = new ColumnInfo(
                propertyName: property.Name,
                columnName: columnName, // Default 1:1 mapping
                clrType: clrType,
                fullClrType: fullClrType,
                isNullable: isNullable,
                kind: kind,
                referencedEntityName: referencedEntityName,
                modifiers: new ColumnModifiers(),
                isValueType: isValueType,
                readerMethodName: readerMethodName);

            columns.Add(column);
            lookup[property.Name] = column;
        }

        return (columns, lookup);
    }

    /// <summary>
    /// Returns true if the given namespace symbol resolves to <c>Quarry</c> (the assembly
    /// that ships <see cref="Quarry.EntityRef{TEntity, TKey}"/>). Used to disambiguate the
    /// real Quarry foreign-key marker type from any same-named user-defined struct.
    /// </summary>
    private static bool IsQuarryNamespace(INamespaceSymbol? ns)
    {
        if (ns == null || ns.IsGlobalNamespace) return false;
        // Walk up to the top-level namespace to handle nested cases (e.g., Quarry.Schema)
        var current = ns;
        while (current != null && !current.IsGlobalNamespace)
        {
            if (current.Name == "Quarry" && (current.ContainingNamespace?.IsGlobalNamespace ?? true))
                return true;
            current = current.ContainingNamespace;
        }
        return false;
    }

    /// <summary>
    /// Builds a dictionary for fast column lookup by property name.
    /// </summary>
    private static Dictionary<string, ColumnInfo> BuildColumnLookup(EntityInfo entityInfo)
    {
        var lookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        foreach (var column in entityInfo.Columns)
        {
            lookup[column.PropertyName] = column;
        }
        return lookup;
    }

    /// <summary>
    /// Analyzes the lambda body expression to determine projection type and columns.
    /// </summary>
    private static ProjectionInfo AnalyzeExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IReadOnlyList<ColumnInfo> entityColumns,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        string entityName,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        return expression switch
        {
            // Entity projection: u => u
            IdentifierNameSyntax identifier when identifier.Identifier.Text == lambdaParameterName =>
                CreateEntityProjection(entityColumns, resultType, dialect),

            // Anonymous type: u => new { u.Id, u.Name }
            AnonymousObjectCreationExpressionSyntax anonymous =>
                AnalyzeAnonymousType(anonymous, semanticModel, columnLookup, lambdaParameterName, resultType, entityName, dialect, projectionParams),

            // DTO/Object initializer: u => new UserDto { Id = u.Id }
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null =>
                AnalyzeObjectInitializer(objectCreation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect, projectionParams),

            // Implicit object creation: u => new() { Id = u.Id }
            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeImplicitObjectInitializer(implicitCreation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect, projectionParams),

            // Tuple: u => (u.Id, u.Name)
            TupleExpressionSyntax tuple =>
                AnalyzeTuple(tuple, semanticModel, columnLookup, lambdaParameterName, resultType, dialect, projectionParams),

            // Single column: u => u.Name
            MemberAccessExpressionSyntax memberAccess when IsMemberOfParameter(memberAccess, lambdaParameterName) =>
                AnalyzeSingleColumn(memberAccess, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Navigation single column: o => o.User.UserName
            MemberAccessExpressionSyntax memberAccess when IsNavigationMemberAccess(memberAccess, lambdaParameterName) =>
                AnalyzeSingleColumn(memberAccess, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Invocation (possibly aggregate): u => Sql.Count()
            InvocationExpressionSyntax invocation =>
                AnalyzeInvocation(invocation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect, projectionParams),

            _ => ProjectionInfo.CreateFailed(resultType, $"Unsupported projection expression: {expression.Kind()}")
        };
    }

    /// <summary>
    /// Checks if a member access is directly on the lambda parameter.
    /// </summary>
    private static bool IsMemberOfParameter(MemberAccessExpressionSyntax memberAccess, string parameterName)
    {
        return memberAccess.Expression is IdentifierNameSyntax identifier &&
               identifier.Identifier.Text == parameterName;
    }

    /// <summary>
    /// Creates a projection for full entity selection.
    /// </summary>
    private static ProjectionInfo CreateEntityProjection(
        IReadOnlyList<ColumnInfo> entityColumns,
        string resultType,
        SqlDialect dialect)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var column in entityColumns)
        {
            columns.Add(new ProjectedColumn(
                propertyName: column.PropertyName,
                columnName: column.ColumnName,
                clrType: column.ClrType,
                fullClrType: column.FullClrType,
                isNullable: column.IsNullable,
                ordinal: ordinal++,
                customTypeMapping: column.CustomTypeMappingClass,
                isValueType: column.IsValueType,
                readerMethodName: column.DbReaderMethodName ?? column.ReaderMethodName,
                isForeignKey: column.Kind == ColumnKind.ForeignKey,
                foreignKeyEntityName: column.ReferencedEntityName,
                isEnum: column.IsEnum));
        }

        return new ProjectionInfo(ProjectionKind.Entity, resultType, columns);
    }

    /// <summary>
    /// Analyzes an anonymous type projection.
    /// Anonymous types are not supported - returns a failed projection.
    /// </summary>
    private static ProjectionInfo AnalyzeAnonymousType(
        AnonymousObjectCreationExpressionSyntax anonymous,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        string entityName,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        // Anonymous types are not supported - return failed projection
        return ProjectionInfo.CreateFailed(
            resultType,
            "Anonymous type projections are not supported. Use a named record, class, or tuple instead.",
            ProjectionFailureReason.AnonymousTypeNotSupported);
    }

    /// <summary>
    /// Analyzes an object initializer projection (DTO).
    /// </summary>
    private static ProjectionInfo AnalyzeObjectInitializer(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        return AnalyzeInitializerExpressions(
            objectCreation.Initializer!.Expressions,
            semanticModel,
            columnLookup,
            lambdaParameterName,
            resultType,
            ProjectionKind.Dto,
            dialect,
            projectionParams);
    }

    /// <summary>
    /// Analyzes an implicit object creation initializer projection.
    /// </summary>
    private static ProjectionInfo AnalyzeImplicitObjectInitializer(
        ImplicitObjectCreationExpressionSyntax implicitCreation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        return AnalyzeInitializerExpressions(
            implicitCreation.Initializer!.Expressions,
            semanticModel,
            columnLookup,
            lambdaParameterName,
            resultType,
            ProjectionKind.Dto,
            dialect,
            projectionParams);
    }

    /// <summary>
    /// Analyzes initializer expressions from object initializers.
    /// </summary>
    private static ProjectionInfo AnalyzeInitializerExpressions(
        SeparatedSyntaxList<ExpressionSyntax> expressions,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        ProjectionKind kind,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var expr in expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
            {
                return ProjectionInfo.CreateFailed(resultType,
                    "Object initializer must use property assignments");
            }

            var propertyName = (assignment.Left as IdentifierNameSyntax)?.Identifier.Text;
            if (propertyName == null)
            {
                return ProjectionInfo.CreateFailed(resultType,
                    "Could not determine property name in initializer");
            }

            var column = AnalyzeProjectedExpression(
                assignment.Right,
                semanticModel,
                columnLookup,
                lambdaParameterName,
                propertyName,
                ordinal++,
                dialect,
                projectionParams);

            if (column == null)
            {
                return ProjectionInfo.CreateFailed(resultType,
                    $"Could not analyze projection for property '{propertyName}'");
            }

            columns.Add(column);
        }

        return new ProjectionInfo(kind, resultType, columns);
    }

    /// <summary>
    /// Analyzes a tuple projection.
    /// </summary>
    private static ProjectionInfo AnalyzeTuple(
        TupleExpressionSyntax tuple,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var argument in tuple.Arguments)
        {
            // Get tuple element name if specified
            var propertyName = argument.NameColon?.Name.Identifier.Text
                ?? GetImplicitPropertyName(argument.Expression)
                ?? $"Item{ordinal + 1}";

            var column = AnalyzeProjectedExpression(
                argument.Expression,
                semanticModel,
                columnLookup,
                lambdaParameterName,
                propertyName,
                ordinal++,
                dialect,
                projectionParams);

            if (column == null)
            {
                return ProjectionInfo.CreateFailed(resultType,
                    $"Could not analyze tuple element at position {ordinal}");
            }

            columns.Add(column);
        }

        // Use the resultType directly - it's now built from the type symbol in AnalyzeCore
        // which is more reliable than building from columns
        return new ProjectionInfo(ProjectionKind.Tuple, resultType, columns);
    }

    /// <summary>
    /// Builds a tuple type name directly from the INamedTypeSymbol's tuple elements.
    /// This is more reliable than ToDisplayString() for generated types.
    /// </summary>
    private static string BuildTupleTypeNameFromSymbol(INamedTypeSymbol tupleType)
    {
        var elements = tupleType.TupleElements;
        var parts = new List<string>();

        for (int i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            var typeName = GetSimpleTypeName(element.Type);
            var elementName = element.Name;

            // Check if this is a default name like "Item1", "Item2"
            // If so, try to get a better name from the corresponding field
            if (elementName.StartsWith("Item") && element.CorrespondingTupleField != null &&
                element.CorrespondingTupleField.Name != elementName)
            {
                elementName = element.CorrespondingTupleField.Name;
            }

            // Handle nullable types
            if (element.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                (element.Type is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
            {
                if (!typeName.EndsWith("?"))
                    typeName += "?";
            }

            // Omit default ItemN names — they cause CS9154 warnings when the
            // original tuple has unnamed elements
            var isDefaultName = elementName == $"Item{i + 1}";
            parts.Add(isDefaultName ? typeName : $"{typeName} {elementName}");
        }

        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Checks if a result type name is a valid tuple type.
    /// </summary>
    private static bool IsValidTupleTypeName(string typeName)
    {
        // A valid tuple type looks like "(int, string)" or "(int Id, string Name)"
        // It should start with "(" and end with ")", and contain real types (not "object" or "?")
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        if (!typeName.StartsWith("(") || !typeName.EndsWith(")"))
            return false;

        // Check that it doesn't contain "object" types (which indicate failed type resolution)
        // and doesn't contain "?" (unresolved types)
        var inner = typeName.Substring(1, typeName.Length - 2);
        var parts = inner.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // Empty part is invalid
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            // Each part should be "type name" or just "type"
            // Reject if type is "object" (fallback type)
            if (trimmed == "object" || trimmed.StartsWith("object "))
                return false;
            // Reject if type is "?" (unresolved type) - appears as "? Name" format
            if (trimmed == "?" || trimmed.StartsWith("? "))
                return false;

            // Check for missing type (just a name with no type before it)
            // Valid patterns: "int", "int Id", "string?", "string? Name"
            // Invalid patterns: "Id", " Name" (starts with space which was trimmed, but no type)
            // If the first character is uppercase and it's a single word, it's likely just a name
            // Valid type names start with lowercase (int, string, etc.) or are qualified (System.Int32)
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0)
            {
                // Single word - could be a type like "DateTime" or just a name like "UserId"
                // Check if it starts with lowercase (primitive type) or is a known uppercase type
                if (!char.IsLower(trimmed[0]) && !trimmed.Contains('.') && !IsKnownUppercaseTypeName(trimmed))
                {
                    // This is a single uppercase word that's not a known type
                    // It's likely just a name without a type (e.g., "UserId" instead of "int UserId")
                    return false;
                }
            }
            else
            {
                // Two parts: "TypeName ElementName"
                var typePart = trimmed.Substring(0, spaceIdx);
                // If the type part doesn't look like a type, it's invalid
                if (string.IsNullOrWhiteSpace(typePart))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a name is a known .NET type that starts with uppercase.
    /// </summary>
    private static bool IsKnownUppercaseTypeName(string name)
    {
        // Remove nullable suffix for checking
        var baseName = name.TrimEnd('?');
        return baseName switch
        {
            "Boolean" or "Byte" or "SByte" or "Int16" or "UInt16" or
            "Int32" or "UInt32" or "Int64" or "UInt64" or
            "Single" or "Double" or "Decimal" or "String" or "Char" or
            "DateTime" or "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or
            "Guid" or "Object" => true,
            _ => false
        };
    }


    /// <summary>
    /// Analyzes a single column projection.
    /// </summary>
    private static ProjectionInfo AnalyzeSingleColumn(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect)
    {
        var propertyName = memberAccess.Name.Identifier.Text;
        var column = AnalyzeProjectedExpression(
            memberAccess,
            semanticModel,
            columnLookup,
            lambdaParameterName,
            propertyName,
            ordinal: 0,
            dialect);

        if (column == null)
        {
            return ProjectionInfo.CreateFailed(resultType,
                $"Could not analyze single column projection for '{propertyName}'");
        }

        // Fix: If resultType is invalid (e.g., "?"), use the column's type instead
        var actualResultType = !TypeClassification.IsUnresolvedTypeNameLenient(resultType)
            ? resultType
            : (column.IsNullable ? $"{column.FullClrType}?" : column.FullClrType);

        return new ProjectionInfo(ProjectionKind.SingleColumn, actualResultType, new[] { column });
    }

    /// <summary>
    /// Analyzes an invocation expression (aggregates, etc.).
    /// </summary>
    private static ProjectionInfo AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        // Check for Sql.Count(), Sql.Sum(), etc.
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == "Sql")
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetAggregateInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, projectionParams);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                var column = new ProjectedColumn(
                    propertyName: "Value", // Scalar result
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: false,
                    ordinal: 0,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true, // Aggregate results are always value types
                    readerMethodName: TypeClassification.GetReaderMethod(aggregateClrType));

                return new ProjectionInfo(ProjectionKind.SingleColumn, resultType, new[] { column });
            }
        }

        // Check for string method calls on columns: u.UserName.Substring(0, 3), u.UserName.ToLower(), etc.
        var stringMethodResult = TryAnalyzeStringMethodProjection(invocation, columnLookup, lambdaParameterName, resultType, dialect);
        if (stringMethodResult != null)
            return stringMethodResult;

        return ProjectionInfo.CreateFailed(resultType,
            "Unsupported invocation in projection");
    }

    /// <summary>
    /// Analyzes a projected expression and returns column metadata.
    /// </summary>
    private static ProjectedColumn? AnalyzeProjectedExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string propertyName,
        int ordinal,
        SqlDialect dialect,
        List<ParameterInfo>? projectionParams = null)
    {
        // Track the entity member name from member access (e.g. "UserId" from u.UserId)
        // so that downstream enrichment can match even when the tuple element name differs
        // (e.g. named tuple (Id: u.UserId) has PropertyName="Id" but member name="UserId").
        string? sourceMemberName = null;

        // Simple column reference: u.Name
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Direct property access: u.Name
            if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == lambdaParameterName)
            {
                var columnPropertyName = memberAccess.Name.Identifier.Text;
                sourceMemberName = columnPropertyName;
                if (columnLookup.TryGetValue(columnPropertyName, out var columnInfo))
                {
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: columnInfo.ColumnName,
                        clrType: columnInfo.ClrType,
                        fullClrType: columnInfo.FullClrType,
                        isNullable: columnInfo.IsNullable,
                        ordinal: ordinal,
                        customTypeMapping: columnInfo.CustomTypeMappingClass,
                        isValueType: columnInfo.IsValueType,
                        readerMethodName: columnInfo.DbReaderMethodName ?? columnInfo.ReaderMethodName,
                        isForeignKey: columnInfo.Kind == ColumnKind.ForeignKey,
                        foreignKeyEntityName: columnInfo.ReferencedEntityName,
                        isEnum: columnInfo.IsEnum);
                }

                // Column lookup failed - try to get type info from the property symbol directly
                // This handles cases where the column lookup is incomplete (e.g., generated types)
                var memberSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                if (memberSymbol is IPropertySymbol propertySymbol)
                {
                    var propType = propertySymbol.Type;
                    var clrType = GetSimpleTypeName(propType);
                    var fullClrType = propType.ToDisplayString();
                    var isNullable = propType.NullableAnnotation == NullableAnnotation.Annotated ||
                                     (propType is INamedTypeSymbol nt &&
                                      nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

                    // Get type metadata from the property type symbol
                    var (isValueType, readerMethodName, _) = ColumnInfo.GetTypeMetadata(propType);

                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: columnPropertyName, // Use property name as column name
                        clrType: clrType,
                        fullClrType: fullClrType,
                        isNullable: isNullable,
                        ordinal: ordinal,
                        isValueType: isValueType,
                        readerMethodName: readerMethodName);
                }
            }

            // Ref<T,K>.Id access: u.Order.Id
            if (memberAccess.Name.Identifier.Text == "Id" &&
                memberAccess.Expression is MemberAccessExpressionSyntax nestedAccess &&
                nestedAccess.Expression is IdentifierNameSyntax nestedId &&
                nestedId.Identifier.Text == lambdaParameterName)
            {
                var refPropertyName = nestedAccess.Name.Identifier.Text;
                if (columnLookup.TryGetValue(refPropertyName, out var refColumn) &&
                    refColumn.Kind == ColumnKind.ForeignKey)
                {
                    return new ProjectedColumn(
                        propertyName: propertyName,
                        columnName: refColumn.ColumnName,
                        clrType: refColumn.ClrType,
                        fullClrType: refColumn.FullClrType,
                        isNullable: refColumn.IsNullable,
                        ordinal: ordinal,
                        isValueType: refColumn.IsValueType,
                        readerMethodName: refColumn.ReaderMethodName);
                }
            }

            // Navigation access: o.User.UserName or o.User.Department.Name
            var navChain = TryParseNavigationChain(memberAccess, lambdaParameterName);
            if (navChain != null)
            {
                // Try to resolve type from semantic model for the interceptor signature
                var navClrType = "";
                var navFullClrType = "";
                var navIsNullable = false;
                var navIsValueType = false;
                var navReaderMethod = "GetValue";
                var navTypeInfo = semanticModel.GetTypeInfo(expression);
                if (navTypeInfo.Type != null)
                {
                    navClrType = GetSimpleTypeName(navTypeInfo.Type);
                    navFullClrType = navTypeInfo.Type.ToDisplayString();
                    navIsNullable = navTypeInfo.Nullability.FlowState == NullableFlowState.MaybeNull;
                    var meta = ColumnInfo.GetTypeMetadata(navTypeInfo.Type);
                    navIsValueType = meta.IsValueType;
                    navReaderMethod = meta.ReaderMethodName;
                }

                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: navChain.Value.FinalProp,
                    clrType: navClrType,
                    fullClrType: navFullClrType,
                    isNullable: navIsNullable,
                    ordinal: ordinal,
                    isValueType: navIsValueType,
                    readerMethodName: navReaderMethod,
                    navigationHops: navChain.Value.Hops);
            }
        }

        // Aggregate functions: Sql.Count(), Sql.Sum(u.Amount)
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax invMemberAccess &&
            invMemberAccess.Expression is IdentifierNameSyntax sqlIdentifier &&
            sqlIdentifier.Identifier.Text == "Sql")
        {
            var methodName = invMemberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetAggregateInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, projectionParams);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: IsConvertedTypeNullable(semanticModel, expression),
                    ordinal: ordinal,
                    alias: propertyName,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true, // Aggregate results are always value types
                    readerMethodName: TypeClassification.GetReaderMethod(aggregateClrType));
            }
        }

        // String method calls on columns: u.UserName.Substring(0, 3), u.UserName.ToLower()
        if (expression is InvocationExpressionSyntax methodInvocation)
        {
            var stringCol = TryAnalyzeStringMethodColumn(methodInvocation, columnLookup, lambdaParameterName, propertyName, ordinal, dialect);
            if (stringCol != null)
                return stringCol;
        }

        // Cast expression: (int)u.Value
        if (expression is CastExpressionSyntax cast)
        {
            return AnalyzeProjectedExpression(
                cast.Expression,
                semanticModel,
                columnLookup,
                lambdaParameterName,
                propertyName,
                ordinal,
                dialect);
        }

        // Nullable cast: (int?)u.Value
        if (expression is BinaryExpressionSyntax binary &&
            binary.Kind() == SyntaxKind.AsExpression)
        {
            return AnalyzeProjectedExpression(
                binary.Left,
                semanticModel,
                columnLookup,
                lambdaParameterName,
                propertyName,
                ordinal,
                dialect);
        }

        // If we can get type info from the semantic model, use that
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type != null)
        {
            // Get type metadata from the type symbol
            var (isValueType, readerMethodName, _) = ColumnInfo.GetTypeMetadata(typeInfo.Type);

            // For unsupported expressions, we can still generate fallback code.
            // Use the source member name (e.g. "UserId" from u.UserId) as the column name
            // so downstream enrichment from the EntityRegistry can match by entity property name
            // even when the tuple element name differs (e.g. named tuple Id: u.UserId).
            return new ProjectedColumn(
                propertyName: propertyName,
                columnName: sourceMemberName ?? "",
                clrType: GetSimpleTypeName(typeInfo.Type),
                fullClrType: typeInfo.Type.ToDisplayString(),
                isNullable: typeInfo.Nullability.FlowState == NullableFlowState.MaybeNull,
                ordinal: ordinal,
                sqlExpression: expression.ToString(),
                isValueType: isValueType,
                readerMethodName: readerMethodName);
        }

        return null;
    }

    /// <summary>
    /// Gets the implicit property name from an expression (for anonymous types without explicit names).
    /// </summary>
    private static string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        // u.Name -> Name
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        // identifier -> identifier
        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }

        return null;
    }

    /// <summary>
    /// Gets the SQL expression and CLR type for an aggregate function.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) GetAggregateInfo(
        string methodName,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        List<ParameterInfo>? projectionParams = null)
    {
        var arguments = invocation.ArgumentList.Arguments;

        // Check for window functions and aggregate OVER overloads FIRST.
        // Without this early check, aggregate OVER calls like Sql.Sum(col, over => ...)
        // would match the regular Sum case below (arguments.Count > 0) and lose the OVER clause.
        if (HasOverClauseLambda(invocation))
            return GetWindowFunctionInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, projectionParams);

        switch (methodName)
        {
            case "Count":
                if (arguments.Count == 0)
                {
                    return ("COUNT(*)", "int");
                }
                else
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
                    return columnSql != null ? ($"COUNT({columnSql})", "int") : (null, null);
                }

            case "Sum":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "decimal");
                    return columnSql != null ? ($"SUM({columnSql})", clrType) : (null, null);
                }
                break;

            case "Avg":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "decimal");
                    return columnSql != null ? ($"AVG({columnSql})", clrType) : (null, null);
                }
                break;

            case "Min":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "object");
                    return columnSql != null ? ($"MIN({columnSql})", clrType) : (null, null);
                }
                break;

            case "Max":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "object");
                    return columnSql != null ? ($"MAX({columnSql})", clrType) : (null, null);
                }
                break;

            case "Raw":
                return BuildSqlRawInfo(invocation, semanticModel, projectionParams,
                    arg => RenderRawArgToCanonical(arg, semanticModel, projectionParams, lambdaParameterName,
                        colRef => ResolveColumnRefToPlaceholder(colRef, columnLookup, lambdaParameterName)));
        }

        return (null, null);
    }

    /// <summary>
    /// Checks whether the target type (after implicit conversions) of an expression is nullable.
    /// Used to determine if aggregate/window function results need IsDBNull guards in reader code.
    /// For example, Sql.Lag(o.Total, 1, ...) returns decimal, but when assigned to a decimal?
    /// property, the ConvertedType is Nullable&lt;decimal&gt;, so the reader must emit a null check.
    /// </summary>
    private static bool IsConvertedTypeNullable(SemanticModel? semanticModel, ExpressionSyntax expression)
    {
        if (semanticModel == null)
            return false;

        var convertedType = semanticModel.GetTypeInfo(expression).ConvertedType;
        if (convertedType == null)
            return false;

        return convertedType.NullableAnnotation == NullableAnnotation.Annotated ||
               (convertedType is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
    }

    /// <summary>
    /// Resolves the CLR type for an aggregate function argument.
    /// Tries the argument type first, then the invocation return type, then falls back to the default.
    /// This handles generated entity types where the semantic model may return error types.
    /// </summary>
    private static string ResolveAggregateClrType(
        ExpressionSyntax argumentExpression,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string defaultType)
    {
        // Try 1: argument type (e.g., o.Total → decimal)
        var argTypeInfo = semanticModel.GetTypeInfo(argumentExpression);
        if (argTypeInfo.Type != null && argTypeInfo.Type.TypeKind != TypeKind.Error)
        {
            var name = GetSimpleTypeName(argTypeInfo.Type);
            if (!TypeClassification.IsUnresolvedTypeNameLenient(name))
                return name;
        }

        // Try 2: invocation return type (e.g., Sql.Sum(decimal) → decimal)
        var invMethodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (invMethodSymbol?.ReturnType != null && invMethodSymbol.ReturnType.TypeKind != TypeKind.Error)
        {
            var name = GetSimpleTypeName(invMethodSymbol.ReturnType);
            if (!TypeClassification.IsUnresolvedTypeNameLenient(name))
                return name;
        }

        // Try 3: column lookup (handles generated entity types where the semantic model
        // returns error types but the column metadata is available from schema analysis)
        if (argumentExpression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == lambdaParameterName)
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propertyName, out var column) && !TypeClassification.IsUnresolvedTypeNameLenient(column.ClrType))
            {
                return column.ClrType;
            }
        }

        return defaultType;
    }

    /// <summary>
    /// Gets the SQL representation of a column expression.
    /// </summary>
    private static string? GetColumnSql(
        ExpressionSyntax expression,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == lambdaParameterName)
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propertyName, out var column))
            {
                return WrapIdentifier(column.ColumnName);
            }

            // Fallback: use property name as column name. This handles both cases:
            //   - Entity type IS resolved but property wasn't found (naming convention mismatch)
            //   - Entity type is generated and not yet in the semantic model (empty lookup)
            // The enrichment step (FixAggregateSqlExpression) rewrites with correct DB names.
            return WrapIdentifier(propertyName);
        }

        return null;
    }

    // ─── Sql.Raw in Select projection ───────────────────────────────────

    /// <summary>
    /// Builds the canonical projection SqlExpression for a <c>Sql.Raw&lt;T&gt;(template, args...)</c>
    /// call. Returns (finalSql, clrType) on success or (null, null) on failure (unsupported template
    /// form, placeholder/arg mismatch, or one or more unrenderable args).
    ///
    /// The returned SqlExpression uses canonical placeholder conventions shared with aggregate and
    /// window function projections: <c>{ColumnName}</c> for column references and <c>@__proj{N}</c>
    /// for captured runtime variables. Dialect resolution and global parameter remapping happen later
    /// in the pipeline (<see cref="SqlFormatting.QuoteSqlExpression"/> and
    /// <c>ChainAnalyzer.RemapProjectionParameters</c>).
    ///
    /// The caller provides a <paramref name="renderArg"/> delegate that resolves each non-template
    /// argument to its canonical SQL text. This indirection allows single-entity and joined callers
    /// to reuse the same template-substitution logic with their own column-resolver strategies.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildSqlRawInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        List<ParameterInfo>? projectionParams,
        Func<ExpressionSyntax, string?> renderArg)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 1)
            return (null, null);

        // Template must be a compile-time string (string literal or string-valued const field).
        var template = TryExtractConstString(arguments[0].Expression, semanticModel);
        if (template == null)
            return (null, null);

        // Validate placeholder/arg count using the same rules as RawCallExpr.
        var renderedArgs = new string[arguments.Count - 1];
        for (int i = 1; i < arguments.Count; i++)
        {
            var rendered = renderArg(arguments[i].Expression);
            if (rendered == null)
                return (null, null); // unsupported arg — bail out
            renderedArgs[i - 1] = rendered;
        }

        // Validate template by constructing a RawCallExpr shell and invoking Validate().
        // Use placeholder literals to drive the validation — the actual rendered values are
        // substituted below in Substitute.
        var shellArgs = new SqlExpr[renderedArgs.Length];
        for (int i = 0; i < shellArgs.Length; i++)
            shellArgs[i] = new SqlRawExpr(renderedArgs[i]);
        var shell = new RawCallExpr(template, shellArgs);
        if (shell.Validate() != null)
            return (null, null);

        // Resolve the generic type argument T from Sql.Raw<T>.
        var clrType = TryExtractSqlRawTypeArg(invocation, semanticModel) ?? "object";

        return (SubstituteTemplatePlaceholders(template, renderedArgs), clrType);
    }

    /// <summary>
    /// Substitutes <c>{0}, {1}, ...</c> indexed placeholders in <paramref name="template"/> with
    /// the corresponding canonical SQL text from <paramref name="renderedArgs"/>. Non-placeholder
    /// text is passed through verbatim (including any literal <c>{identifier}</c> placeholders that
    /// refer to columns — those are resolved later by <see cref="SqlFormatting.QuoteSqlExpression"/>).
    /// </summary>
    private static string SubstituteTemplatePlaceholders(string template, string[] renderedArgs)
    {
        var sb = new System.Text.StringBuilder(template.Length + 32);
        int pos = 0;
        while (pos < template.Length)
        {
            if (template[pos] == '{')
            {
                int numStart = pos + 1;
                int numEnd = numStart;
                while (numEnd < template.Length && template[numEnd] >= '0' && template[numEnd] <= '9')
                    numEnd++;
                if (numEnd > numStart && numEnd < template.Length && template[numEnd] == '}'
                    && int.TryParse(template.Substring(numStart, numEnd - numStart), out int argIdx)
                    && argIdx >= 0 && argIdx < renderedArgs.Length)
                {
                    sb.Append(renderedArgs[argIdx]);
                    pos = numEnd + 1;
                    continue;
                }
            }
            sb.Append(template[pos]);
            pos++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts a compile-time string constant from an expression. Handles string literals and
    /// string-valued constants. Returns null for runtime expressions, non-string types, or when no
    /// semantic model is available and the expression isn't a literal.
    /// </summary>
    private static string? TryExtractConstString(ExpressionSyntax expr, SemanticModel? semanticModel)
    {
        if (expr is LiteralExpressionSyntax literal && literal.Kind() == SyntaxKind.StringLiteralExpression)
            return literal.Token.ValueText;

        if (semanticModel == null)
            return null;

        var constValue = semanticModel.GetConstantValue(expr);
        return constValue.HasValue && constValue.Value is string s ? s : null;
    }

    /// <summary>
    /// Extracts the generic type argument <c>T</c> from a <c>Sql.Raw&lt;T&gt;(...)</c> invocation.
    /// Returns null when the invocation is not generic or the type cannot be resolved.
    /// </summary>
    private static string? TryExtractSqlRawTypeArg(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax generic
            && generic.TypeArgumentList.Arguments.Count == 1)
        {
            var typeArg = generic.TypeArgumentList.Arguments[0];
            var typeInfo = semanticModel.GetTypeInfo(typeArg);
            if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                return GetSimpleTypeName(typeInfo.Type);
        }

        // Fallback: resolve the method symbol's return type
        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol?.ReturnType != null && methodSymbol.ReturnType.TypeKind != TypeKind.Error)
            return GetSimpleTypeName(methodSymbol.ReturnType);

        return null;
    }

    /// <summary>
    /// Renders a single <c>Sql.Raw&lt;T&gt;</c> argument expression into canonical SQL text with
    /// projection-layer placeholders. Parses via <see cref="SqlExprParser"/> and walks the resulting
    /// tree, delegating column-reference resolution to <paramref name="resolveColumn"/>. Non-column
    /// nodes (literals, captured vars, binary/unary ops, function calls, IS NULL, IN, LIKE, sub-raw)
    /// render inline. Returns null for unsupported node kinds.
    /// </summary>
    private static string? RenderRawArgToCanonical(
        ExpressionSyntax arg,
        SemanticModel semanticModel,
        List<ParameterInfo>? projectionParams,
        string lambdaParameterName,
        Func<ColumnRefExpr, string?> resolveColumn)
    {
        var lambdaParams = new HashSet<string> { lambdaParameterName };
        var parsed = SqlExprParser.Parse(arg, lambdaParams);
        return RenderRawArgNode(parsed, projectionParams, resolveColumn);
    }

    /// <summary>
    /// Shared entry point for joined contexts. Accepts a set of lambda parameter names (one per
    /// joined entity) and uses the caller-supplied column resolver to translate
    /// <see cref="ColumnRefExpr"/> nodes to <c>{alias}.{ColumnName}</c> form.
    /// </summary>
    private static string? RenderRawArgToCanonicalJoined(
        ExpressionSyntax arg,
        SemanticModel semanticModel,
        List<ParameterInfo>? projectionParams,
        HashSet<string> lambdaParameterNames,
        Func<ColumnRefExpr, string?> resolveColumn)
    {
        var parsed = SqlExprParser.Parse(arg, lambdaParameterNames);
        return RenderRawArgNode(parsed, projectionParams, resolveColumn);
    }

    /// <summary>
    /// Recursive walker that emits canonical SQL text for a parsed <see cref="SqlExpr"/> argument
    /// tree. Uses a column-resolver delegate so the same walker serves single-entity and joined
    /// contexts. Returns null if any node in the tree is unsupported (subquery, navigation, nested
    /// raw, unresolved captured variables, etc.).
    /// </summary>
    private static string? RenderRawArgNode(
        SqlExpr node,
        List<ParameterInfo>? projectionParams,
        Func<ColumnRefExpr, string?> resolveColumn)
    {
        switch (node)
        {
            case ColumnRefExpr colRef:
                return resolveColumn(colRef);

            case LiteralExpr literal:
                return FormatLiteralForProjection(literal);

            case CapturedValueExpr captured:
                return AddCapturedAsProjectionParameter(captured, projectionParams);

            case BinaryOpExpr bin:
            {
                var left = RenderRawArgNode(bin.Left, projectionParams, resolveColumn);
                if (left == null) return null;
                var right = RenderRawArgNode(bin.Right, projectionParams, resolveColumn);
                if (right == null) return null;
                return $"({left} {GetRawBinaryOperator(bin.Operator)} {right})";
            }

            case UnaryOpExpr unary:
            {
                var operand = RenderRawArgNode(unary.Operand, projectionParams, resolveColumn);
                if (operand == null) return null;
                return unary.Operator switch
                {
                    SqlUnaryOperator.Not => $"NOT ({operand})",
                    SqlUnaryOperator.Negate => $"-{operand}",
                    _ => null
                };
            }

            case FunctionCallExpr func:
            {
                var parts = new string[func.Arguments.Count];
                for (int i = 0; i < func.Arguments.Count; i++)
                {
                    var p = RenderRawArgNode(func.Arguments[i], projectionParams, resolveColumn);
                    if (p == null) return null;
                    parts[i] = p;
                }
                return $"{func.FunctionName}({string.Join(", ", parts)})";
            }

            case IsNullCheckExpr isNull:
            {
                var operand = RenderRawArgNode(isNull.Operand, projectionParams, resolveColumn);
                if (operand == null) return null;
                return isNull.IsNegated ? $"{operand} IS NOT NULL" : $"{operand} IS NULL";
            }

            case InExpr inExpr:
            {
                var operand = RenderRawArgNode(inExpr.Operand, projectionParams, resolveColumn);
                if (operand == null) return null;
                var vals = new string[inExpr.Values.Count];
                for (int i = 0; i < inExpr.Values.Count; i++)
                {
                    var v = RenderRawArgNode(inExpr.Values[i], projectionParams, resolveColumn);
                    if (v == null) return null;
                    vals[i] = v;
                }
                return $"{operand}{(inExpr.IsNegated ? " NOT IN " : " IN ")}({string.Join(", ", vals)})";
            }

            case LikeExpr like:
            {
                var operand = RenderRawArgNode(like.Operand, projectionParams, resolveColumn);
                if (operand == null) return null;
                var pattern = RenderRawArgNode(like.Pattern, projectionParams, resolveColumn);
                if (pattern == null) return null;
                return $"{operand}{(like.IsNegated ? " NOT LIKE " : " LIKE ")}{pattern}";
            }

            case SqlRawExpr raw:
                return raw.SqlText;

            // Unsupported in projection-arg context: NavigationAccess, Subquery, RawCall (nested),
            // ParamSlot (parser never emits), ExprList. Caller will bail out.
            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a <see cref="ColumnRefExpr"/> from a single-entity projection context to a canonical
    /// <c>{ColumnName}</c> placeholder. Mirrors <see cref="GetColumnSql"/> behavior: when the property
    /// is not in the column lookup, the property name itself is used as the placeholder so downstream
    /// enrichment can rewrite it.
    /// </summary>
    private static string? ResolveColumnRefToPlaceholder(
        ColumnRefExpr colRef,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName)
    {
        // Ignore bare lambda parameter references (entity self-reference), which produce a ColumnRef
        // with ParameterName == PropertyName in SqlExprParser. These are not column references.
        if (colRef.ParameterName == colRef.PropertyName)
            return null;

        if (colRef.ParameterName != lambdaParameterName)
            return null;

        if (columnLookup.TryGetValue(colRef.PropertyName, out var column))
            return WrapIdentifier(column.ColumnName);

        // Fallback: use property name as column name. Enrichment rewrites with the correct DB name
        // if the entity type resolves later in the pipeline.
        return WrapIdentifier(colRef.PropertyName);
    }

    /// <summary>
    /// Formats a <see cref="LiteralExpr"/> node as inline SQL text for projection-argument use.
    /// Matches the conventions of <see cref="SqlExprRenderer"/> for string/char escaping and
    /// NULL/boolean handling.
    /// </summary>
    private static string FormatLiteralForProjection(LiteralExpr literal)
    {
        if (literal.IsNull)
            return "NULL";

        switch (literal.ClrType)
        {
            case "bool":
                return literal.SqlText == "TRUE" || literal.SqlText == "true" || literal.SqlText == "1"
                    ? "TRUE"
                    : "FALSE";

            case "string":
            case "char":
                return $"'{literal.SqlText.Replace("'", "''")}'";

            default:
                return literal.SqlText;
        }
    }

    /// <summary>
    /// Adds a <see cref="CapturedValueExpr"/> as a projection parameter, returning the local
    /// <c>@__proj{N}</c> placeholder that will later be remapped to a global parameter by
    /// <c>ChainAnalyzer.RemapProjectionParameters</c>. Returns null when no
    /// <paramref name="projectionParams"/> list is available or when the captured value cannot be
    /// extracted (missing symbol info).
    /// </summary>
    private static string? AddCapturedAsProjectionParameter(
        CapturedValueExpr captured,
        List<ParameterInfo>? projectionParams)
    {
        if (projectionParams == null)
            return null;

        var localIndex = projectionParams.Count;
        var placeholder = $"@__proj{localIndex}";

        var paramInfo = new ParameterInfo(
            index: localIndex,
            name: placeholder,
            clrType: captured.ClrType,
            valueExpression: captured.SyntaxText,
            isCaptured: true)
        {
            CapturedFieldName = captured.VariableName,
            CapturedFieldType = captured.ClrType,
        };

        projectionParams.Add(paramInfo);
        return placeholder;
    }

    /// <summary>
    /// Returns the SQL operator text for a <see cref="SqlBinaryOperator"/>. Mirrors the mapping in
    /// <see cref="SqlExprRenderer"/>.
    /// </summary>
    private static string GetRawBinaryOperator(SqlBinaryOperator op) => op switch
    {
        SqlBinaryOperator.Equal => "=",
        SqlBinaryOperator.NotEqual => "<>",
        SqlBinaryOperator.LessThan => "<",
        SqlBinaryOperator.GreaterThan => ">",
        SqlBinaryOperator.LessThanOrEqual => "<=",
        SqlBinaryOperator.GreaterThanOrEqual => ">=",
        SqlBinaryOperator.And => "AND",
        SqlBinaryOperator.Or => "OR",
        SqlBinaryOperator.Add => "+",
        SqlBinaryOperator.Subtract => "-",
        SqlBinaryOperator.Multiply => "*",
        SqlBinaryOperator.Divide => "/",
        SqlBinaryOperator.Modulo => "%",
        SqlBinaryOperator.BitwiseAnd => "&",
        SqlBinaryOperator.BitwiseOr => "|",
        SqlBinaryOperator.BitwiseXor => "^",
        _ => "?"
    };

    // ─── End Sql.Raw in Select projection ──────────────────────────────────

    /// <summary>
    /// Gets the SQL expression and CLR type for an aggregate function in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) GetJoinedAggregateInfo(
        string methodName,
        InvocationExpressionSyntax invocation,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        var arguments = invocation.ArgumentList.Arguments;

        // Check for window functions and aggregate OVER overloads FIRST.
        if (HasOverClauseLambda(invocation))
            return GetJoinedWindowFunctionInfo(methodName, invocation, perParamLookup, semanticModel, projectionParams);

        switch (methodName)
        {
            case "Count":
                if (arguments.Count == 0)
                {
                    return ("COUNT(*)", "int");
                }
                else
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
                    return columnSql != null ? ($"COUNT({columnSql})", "int") : (null, null);
                }

            case "Sum":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal");
                    return columnSql != null ? ($"SUM({columnSql})", clrType) : (null, null);
                }
                break;

            case "Avg":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal");
                    return columnSql != null ? ($"AVG({columnSql})", clrType) : (null, null);
                }
                break;

            case "Min":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object");
                    return columnSql != null ? ($"MIN({columnSql})", clrType) : (null, null);
                }
                break;

            case "Max":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object");
                    return columnSql != null ? ($"MAX({columnSql})", clrType) : (null, null);
                }
                break;
        }

        return (null, null);
    }

    /// <summary>
    /// Gets the SQL representation of a column expression in a joined context (e.g., u.Amount → t0."Amount").
    /// </summary>
    private static string? GetJoinedColumnSql(
        ExpressionSyntax expression,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            perParamLookup.TryGetValue(identifier.Identifier.Text, out var info))
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (info.Lookup.TryGetValue(propertyName, out var column))
                return $"{WrapIdentifier(info.Alias)}.{WrapIdentifier(column.ColumnName)}";

            // Fallback: use property name with table alias (enrichment rewrites later)
            return $"{WrapIdentifier(info.Alias)}.{WrapIdentifier(propertyName)}";
        }

        return null;
    }

    /// <summary>
    /// Resolves the CLR type for an aggregate function argument in a joined context.
    /// </summary>
    private static string ResolveJoinedAggregateClrType(
        ExpressionSyntax argumentExpression,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string defaultType)
    {
        if (argumentExpression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            perParamLookup.TryGetValue(identifier.Identifier.Text, out var info))
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (info.Lookup.TryGetValue(propertyName, out var column) && !TypeClassification.IsUnresolvedTypeNameLenient(column.ClrType))
                return column.ClrType;
        }

        return defaultType;
    }

    #region Window Function Analysis

    /// <summary>
    /// Known window function method names mapped to their SQL function names.
    /// </summary>
    private static readonly HashSet<string> WindowFunctionNames = new(StringComparer.Ordinal)
    {
        "RowNumber", "Rank", "DenseRank", "Ntile",
        "Lag", "Lead", "FirstValue", "LastValue"
    };

    /// <summary>
    /// Checks if the last argument of an invocation is a lambda (the OVER clause lambda).
    /// </summary>
    private static bool HasOverClauseLambda(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return false;
        return args[args.Count - 1].Expression is LambdaExpressionSyntax;
    }

    /// <summary>
    /// Gets the SQL expression and CLR type for a window function call.
    /// Handles: Sql.RowNumber(over => ...), Sql.Lag(o.Col, over => ...),
    /// and aggregate OVER overloads like Sql.Sum(o.Col, over => ...).
    /// </summary>
    private static (string? SqlExpression, string? ClrType) GetWindowFunctionInfo(
        string methodName,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        List<ParameterInfo>? projectionParams = null)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0) return (null, null);

        // The OVER lambda is always the last argument
        var lastArg = arguments[arguments.Count - 1].Expression;
        if (lastArg is not LambdaExpressionSyntax overLambda)
            return (null, null);

        var overClause = ParseOverClause(overLambda, columnLookup, lambdaParameterName);
        if (overClause == null) return (null, null);

        // Dedicated window functions
        if (WindowFunctionNames.Contains(methodName))
        {
            return methodName switch
            {
                "RowNumber" => ($"ROW_NUMBER() OVER ({overClause})", "int"),
                "Rank" => ($"RANK() OVER ({overClause})", "int"),
                "DenseRank" => ($"DENSE_RANK() OVER ({overClause})", "int"),
                "Ntile" when arguments.Count >= 2 =>
                    BuildNtileSql(arguments[0].Expression, semanticModel, projectionParams, overClause),
                "Lag" => BuildLagLeadSql("LAG", arguments, columnLookup, lambdaParameterName, semanticModel, invocation, projectionParams, overClause),
                "Lead" => BuildLagLeadSql("LEAD", arguments, columnLookup, lambdaParameterName, semanticModel, invocation, projectionParams, overClause),
                "FirstValue" when arguments.Count >= 2 =>
                    BuildValueFunctionSql("FIRST_VALUE", arguments[0].Expression, columnLookup, lambdaParameterName, semanticModel, invocation, overClause),
                "LastValue" when arguments.Count >= 2 =>
                    BuildValueFunctionSql("LAST_VALUE", arguments[0].Expression, columnLookup, lambdaParameterName, semanticModel, invocation, overClause),
                _ => (null, null)
            };
        }

        // Aggregate OVER overloads: Sum, Avg, Min, Max, Count with a trailing lambda
        return methodName switch
        {
            "Count" when arguments.Count == 1 => ($"COUNT(*) OVER ({overClause})", "int"),
            "Count" when arguments.Count == 2 =>
                BuildAggregateOverSql("COUNT", arguments[0].Expression, columnLookup, lambdaParameterName, overClause, "int"),
            "Sum" when arguments.Count == 2 =>
                BuildAggregateOverSql("SUM", arguments[0].Expression, columnLookup, lambdaParameterName, overClause,
                    ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel, columnLookup, lambdaParameterName, "decimal")),
            "Avg" when arguments.Count == 2 =>
                BuildAggregateOverSql("AVG", arguments[0].Expression, columnLookup, lambdaParameterName, overClause,
                    ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel, columnLookup, lambdaParameterName, "decimal")),
            "Min" when arguments.Count == 2 =>
                BuildAggregateOverSql("MIN", arguments[0].Expression, columnLookup, lambdaParameterName, overClause,
                    ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel, columnLookup, lambdaParameterName, "object")),
            "Max" when arguments.Count == 2 =>
                BuildAggregateOverSql("MAX", arguments[0].Expression, columnLookup, lambdaParameterName, overClause,
                    ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel, columnLookup, lambdaParameterName, "object")),
            _ => (null, null)
        };
    }

    /// <summary>
    /// Gets the SQL expression and CLR type for a window function call in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) GetJoinedWindowFunctionInfo(
        string methodName,
        InvocationExpressionSyntax invocation,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        SemanticModel? semanticModel = null,
        List<ParameterInfo>? projectionParams = null)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0) return (null, null);

        var lastArg = arguments[arguments.Count - 1].Expression;
        if (lastArg is not LambdaExpressionSyntax overLambda)
            return (null, null);

        var overClause = ParseJoinedOverClause(overLambda, perParamLookup);
        if (overClause == null) return (null, null);

        // Dedicated window functions
        if (WindowFunctionNames.Contains(methodName))
        {
            return methodName switch
            {
                "RowNumber" => ($"ROW_NUMBER() OVER ({overClause})", "int"),
                "Rank" => ($"RANK() OVER ({overClause})", "int"),
                "DenseRank" => ($"DENSE_RANK() OVER ({overClause})", "int"),
                "Ntile" when arguments.Count >= 2 =>
                    BuildNtileSql(arguments[0].Expression, semanticModel, projectionParams, overClause),
                "Lag" => BuildJoinedLagLeadSql("LAG", arguments, perParamLookup, semanticModel, projectionParams, overClause),
                "Lead" => BuildJoinedLagLeadSql("LEAD", arguments, perParamLookup, semanticModel, projectionParams, overClause),
                "FirstValue" when arguments.Count >= 2 =>
                    BuildJoinedValueFunctionSql("FIRST_VALUE", arguments[0].Expression, perParamLookup, overClause),
                "LastValue" when arguments.Count >= 2 =>
                    BuildJoinedValueFunctionSql("LAST_VALUE", arguments[0].Expression, perParamLookup, overClause),
                _ => (null, null)
            };
        }

        // Aggregate OVER overloads
        return methodName switch
        {
            "Count" when arguments.Count == 1 => ($"COUNT(*) OVER ({overClause})", "int"),
            "Count" when arguments.Count == 2 =>
                BuildJoinedAggregateOverSql("COUNT", arguments[0].Expression, perParamLookup, overClause, "int"),
            "Sum" when arguments.Count == 2 =>
                BuildJoinedAggregateOverSql("SUM", arguments[0].Expression, perParamLookup, overClause,
                    ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal")),
            "Avg" when arguments.Count == 2 =>
                BuildJoinedAggregateOverSql("AVG", arguments[0].Expression, perParamLookup, overClause,
                    ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal")),
            "Min" when arguments.Count == 2 =>
                BuildJoinedAggregateOverSql("MIN", arguments[0].Expression, perParamLookup, overClause,
                    ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object")),
            "Max" when arguments.Count == 2 =>
                BuildJoinedAggregateOverSql("MAX", arguments[0].Expression, perParamLookup, overClause,
                    ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object")),
            _ => (null, null)
        };
    }

    /// <summary>
    /// Parses the OVER clause lambda body to extract PARTITION BY and ORDER BY columns.
    /// The lambda body is a fluent chain like: over.PartitionBy(o.Category).OrderBy(o.Price)
    /// Returns the inner text of the OVER clause (e.g., "PARTITION BY \"Category\" ORDER BY \"Price\"").
    /// </summary>
    private static string? ParseOverClause(
        LambdaExpressionSyntax lambda,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName)
    {
        // Extract the lambda body expression
        var body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
        if (body == null) return null;

        // Walk the fluent chain and collect PartitionBy / OrderBy calls
        var partitionColumns = new List<string>();
        var orderColumns = new List<(string Sql, bool Descending)>();

        if (!WalkOverChain(body, partitionColumns, orderColumns,
            (expr) => GetColumnSql(expr, columnLookup, lambdaParameterName)))
            return null;

        return BuildOverClauseString(partitionColumns, orderColumns);
    }

    /// <summary>
    /// Parses the OVER clause lambda body in a joined context.
    /// </summary>
    private static string? ParseJoinedOverClause(
        LambdaExpressionSyntax lambda,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup)
    {
        var body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
        if (body == null) return null;

        var partitionColumns = new List<string>();
        var orderColumns = new List<(string Sql, bool Descending)>();

        if (!WalkOverChain(body, partitionColumns, orderColumns,
            (expr) => GetJoinedColumnSql(expr, perParamLookup)))
            return null;

        return BuildOverClauseString(partitionColumns, orderColumns);
    }

    /// <summary>
    /// Recursively walks the fluent OVER chain (innermost to outermost), collecting
    /// PARTITION BY and ORDER BY column specifications.
    /// Chain structure: OrderBy(PartitionBy(over, col1), col2) — outermost is last applied.
    /// </summary>
    private static bool WalkOverChain(
        ExpressionSyntax expression,
        List<string> partitionColumns,
        List<(string Sql, bool Descending)> orderColumns,
        Func<ExpressionSyntax, string?> resolveColumn)
    {
        // Base case: lambda parameter (e.g., "over") — stop recursion
        if (expression is IdentifierNameSyntax)
            return true;

        if (expression is not InvocationExpressionSyntax invocation)
            return false;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;

        // Recurse into the receiver first (builds chain from inside out)
        if (!WalkOverChain(memberAccess.Expression, partitionColumns, orderColumns, resolveColumn))
            return false;

        var args = invocation.ArgumentList.Arguments;

        switch (methodName)
        {
            case "PartitionBy":
                // PartitionBy has params — process all arguments
                foreach (var arg in args)
                {
                    var colSql = resolveColumn(arg.Expression);
                    if (colSql == null) return false;
                    partitionColumns.Add(colSql);
                }
                break;

            case "OrderBy":
                if (args.Count < 1) return false;
                var orderSql = resolveColumn(args[0].Expression);
                if (orderSql == null) return false;
                orderColumns.Add((orderSql, false));
                break;

            case "OrderByDescending":
                if (args.Count < 1) return false;
                var descSql = resolveColumn(args[0].Expression);
                if (descSql == null) return false;
                orderColumns.Add((descSql, true));
                break;

            default:
                return false; // Unknown method in OVER chain
        }

        return true;
    }

    /// <summary>
    /// Builds the OVER clause string from collected partition and order columns.
    /// </summary>
    private static string BuildOverClauseString(
        List<string> partitionColumns,
        List<(string Sql, bool Descending)> orderColumns)
    {
        var parts = new List<string>();

        if (partitionColumns.Count > 0)
            parts.Add("PARTITION BY " + string.Join(", ", partitionColumns));

        if (orderColumns.Count > 0)
        {
            var orderParts = orderColumns.Select(o =>
                o.Descending ? $"{o.Sql} DESC" : o.Sql);
            parts.Add("ORDER BY " + string.Join(", ", orderParts));
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Formats a CLR constant value as a SQL literal, stripping C# type suffixes.
    /// </summary>
    private static string? FormatConstantForSql(object? value)
    {
        // Delegate to shared formatter; fall back to IFormattable/ToString for
        // types not handled by the core formatter (e.g., DateTime, Guid).
        return Translation.SqlLikeHelpers.FormatConstantAsSqlLiteral(value)
            ?? (value is IFormattable fmt
                ? fmt.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString());
    }

    /// <summary>
    /// Resolves a scalar (non-column) argument expression to its SQL representation.
    /// For compile-time constants: returns the value formatted as a SQL literal.
    /// For runtime variables: creates a projection parameter with a placeholder.
    /// Returns null if the expression cannot be resolved (triggers runtime fallback).
    /// </summary>
    private static string? ResolveScalarArgSql(
        ExpressionSyntax expr,
        SemanticModel? semanticModel,
        List<ParameterInfo>? projectionParams)
    {
        // Try compile-time constant evaluation via semantic model
        if (semanticModel != null)
        {
            var constantValue = semanticModel.GetConstantValue(expr);
            if (constantValue.HasValue)
                return FormatConstantForSql(constantValue.Value);
        }

        // Fallback for literal expressions when no semantic model is available
        if (expr is LiteralExpressionSyntax literal)
        {
            if (literal.Kind() == SyntaxKind.NumericLiteralExpression)
                return FormatConstantForSql(literal.Token.Value);
            if (literal.Kind() == SyntaxKind.StringLiteralExpression)
                return FormatConstantForSql(literal.Token.ValueText);
            if (literal.Kind() == SyntaxKind.TrueLiteralExpression)
                return "TRUE";
            if (literal.Kind() == SyntaxKind.FalseLiteralExpression)
                return "FALSE";
            if (literal.Kind() == SyntaxKind.NullLiteralExpression)
                return "NULL";
        }

        // Non-constant expression: parameterize if we have the infrastructure
        if (projectionParams == null || semanticModel == null)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(expr);
        var clrType = typeInfo.Type != null ? GetSimpleTypeName(typeInfo.Type) : "object";
        var localIndex = projectionParams.Count;
        var placeholder = $"@__proj{localIndex}";

        var paramInfo = new ParameterInfo(
            index: localIndex,
            name: placeholder,
            clrType: clrType,
            valueExpression: expr.ToString(),
            isCaptured: true);

        // Detect captured variable name for display class extraction
        var symbolInfo = semanticModel.GetSymbolInfo(expr);
        if (symbolInfo.Symbol is ILocalSymbol localSymbol)
        {
            paramInfo.CapturedFieldName = localSymbol.Name;
            paramInfo.CapturedFieldType = GetSimpleTypeName(localSymbol.Type);
        }
        else if (symbolInfo.Symbol is IParameterSymbol paramSymbol)
        {
            paramInfo.CapturedFieldName = paramSymbol.Name;
            paramInfo.CapturedFieldType = GetSimpleTypeName(paramSymbol.Type);
        }
        else
        {
            // Complex expression (member access, method call, etc.) — not directly extractable
            // Fall back to runtime for now
            return null;
        }

        projectionParams.Add(paramInfo);
        return placeholder;
    }

    /// <summary>
    /// Builds SQL for NTILE(n) OVER (...).
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildNtileSql(
        ExpressionSyntax bucketsExpr,
        SemanticModel? semanticModel,
        List<ParameterInfo>? projectionParams,
        string overClause)
    {
        var bucketsText = ResolveScalarArgSql(bucketsExpr, semanticModel, projectionParams);
        if (bucketsText == null) return (null, null);
        return ($"NTILE({bucketsText}) OVER ({overClause})", "int");
    }

    /// <summary>
    /// Builds SQL for LAG/LEAD with 1-3 column arguments + OVER clause.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildLagLeadSql(
        string functionName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        List<ParameterInfo>? projectionParams,
        string overClause)
    {
        // arguments: [column, (offset?,) (default?,) overLambda]
        // column is always first, overLambda is always last
        var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName);
        if (columnSql == null) return (null, null);

        var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
            columnLookup, lambdaParameterName, "object");

        var nonLambdaArgCount = arguments.Count - 1; // Exclude the OVER lambda
        if (nonLambdaArgCount == 1)
            return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);

        if (nonLambdaArgCount == 2)
        {
            var offsetSql = ResolveScalarArgSql(arguments[1].Expression, semanticModel, projectionParams);
            if (offsetSql == null) return (null, null);
            return ($"{functionName}({columnSql}, {offsetSql}) OVER ({overClause})", clrType);
        }

        if (nonLambdaArgCount == 3)
        {
            var offsetSql = ResolveScalarArgSql(arguments[1].Expression, semanticModel, projectionParams);
            if (offsetSql == null) return (null, null);
            var defaultSql = ResolveScalarArgSql(arguments[2].Expression, semanticModel, projectionParams);
            if (defaultSql == null) return (null, null);
            return ($"{functionName}({columnSql}, {offsetSql}, {defaultSql}) OVER ({overClause})", clrType);
        }

        return (null, null);
    }

    /// <summary>
    /// Builds SQL for LAG/LEAD in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildJoinedLagLeadSql(
        string functionName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        SemanticModel? semanticModel,
        List<ParameterInfo>? projectionParams,
        string overClause)
    {
        var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup);
        if (columnSql == null) return (null, null);

        var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object");

        var nonLambdaArgCount = arguments.Count - 1;
        if (nonLambdaArgCount == 1)
            return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);

        if (nonLambdaArgCount == 2)
        {
            var offsetSql = ResolveScalarArgSql(arguments[1].Expression, semanticModel, projectionParams);
            if (offsetSql == null) return (null, null);
            return ($"{functionName}({columnSql}, {offsetSql}) OVER ({overClause})", clrType);
        }

        if (nonLambdaArgCount == 3)
        {
            var offsetSql = ResolveScalarArgSql(arguments[1].Expression, semanticModel, projectionParams);
            if (offsetSql == null) return (null, null);
            var defaultSql = ResolveScalarArgSql(arguments[2].Expression, semanticModel, projectionParams);
            if (defaultSql == null) return (null, null);
            return ($"{functionName}({columnSql}, {offsetSql}, {defaultSql}) OVER ({overClause})", clrType);
        }

        return (null, null);
    }

    /// <summary>
    /// Builds SQL for FIRST_VALUE/LAST_VALUE.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildValueFunctionSql(
        string functionName,
        ExpressionSyntax columnExpr,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        string overClause)
    {
        var columnSql = GetColumnSql(columnExpr, columnLookup, lambdaParameterName);
        if (columnSql == null) return (null, null);

        var clrType = ResolveAggregateClrType(columnExpr, invocation, semanticModel,
            columnLookup, lambdaParameterName, "object");

        return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);
    }

    /// <summary>
    /// Builds SQL for FIRST_VALUE/LAST_VALUE in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildJoinedValueFunctionSql(
        string functionName,
        ExpressionSyntax columnExpr,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string overClause)
    {
        var columnSql = GetJoinedColumnSql(columnExpr, perParamLookup);
        if (columnSql == null) return (null, null);

        var clrType = ResolveJoinedAggregateClrType(columnExpr, perParamLookup, "object");
        return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);
    }

    /// <summary>
    /// Builds SQL for aggregate OVER forms like SUM(col) OVER (...).
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildAggregateOverSql(
        string functionName,
        ExpressionSyntax columnExpr,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string overClause,
        string clrType)
    {
        var columnSql = GetColumnSql(columnExpr, columnLookup, lambdaParameterName);
        if (columnSql == null) return (null, null);
        return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);
    }

    /// <summary>
    /// Builds SQL for aggregate OVER forms in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) BuildJoinedAggregateOverSql(
        string functionName,
        ExpressionSyntax columnExpr,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        string overClause,
        string clrType)
    {
        var columnSql = GetJoinedColumnSql(columnExpr, perParamLookup);
        if (columnSql == null) return (null, null);
        return ($"{functionName}({columnSql}) OVER ({overClause})", clrType);
    }

    #endregion

    /// <summary>
    /// Wraps an identifier in the canonical <c>{identifier}</c> placeholder format.
    /// Dialect-specific quoting is deferred to render time via
    /// <see cref="Quarry.Generators.Sql.SqlFormatting.QuoteSqlExpression"/>.
    /// </summary>
    private static string WrapIdentifier(string identifier) => $"{{{identifier}}}";


    /// <summary>
    /// Gets a simple type name from a type symbol.
    /// </summary>
    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        // Handle Nullable<T> - extract the underlying type
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

    // ─── String method projection helpers ────────────────────────────────

    /// <summary>
    /// Known string methods that can be translated to SQL in projections.
    /// </summary>
    private static readonly HashSet<string> StringProjectionMethods = new(StringComparer.Ordinal)
    {
        "Substring", "ToLower", "ToLowerInvariant", "ToUpper", "ToUpperInvariant", "Trim"
    };

    /// <summary>
    /// Tries to analyze a string method call as a single-column projection.
    /// Handles: u => u.UserName.Substring(0, 3), u => u.UserName.ToLower(), etc.
    /// </summary>
    private static ProjectionInfo? TryAnalyzeStringMethodProjection(
        InvocationExpressionSyntax invocation,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string resultType,
        SqlDialect dialect)
    {
        var col = TryAnalyzeStringMethodColumn(invocation, columnLookup, lambdaParameterName, "Value", 0, dialect);
        if (col == null)
            return null;

        // String methods always return string
        var actualResultType = !TypeClassification.IsUnresolvedTypeNameLenient(resultType) ? resultType : "string";
        return new ProjectionInfo(ProjectionKind.SingleColumn, actualResultType, new[] { col });
    }

    /// <summary>
    /// Tries to analyze a string method call and return a ProjectedColumn with SQL expression.
    /// </summary>
    private static ProjectedColumn? TryAnalyzeStringMethodColumn(
        InvocationExpressionSyntax invocation,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName,
        string propertyName,
        int ordinal,
        SqlDialect dialect)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax methodAccess)
            return null;

        var methodName = methodAccess.Name.Identifier.Text;
        if (!StringProjectionMethods.Contains(methodName))
            return null;

        // Resolve the column from the receiver chain
        var columnSql = ResolveColumnSqlFromExpression(methodAccess.Expression, columnLookup, lambdaParameterName);
        if (columnSql == null)
            return null;

        // Translate the method call to SQL
        var sqlExpr = TranslateStringMethodToSql(methodName, columnSql, invocation.ArgumentList, dialect);
        if (sqlExpr == null)
            return null;

        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: "",
            clrType: "string",
            fullClrType: "string",
            isNullable: false,
            ordinal: ordinal,
            alias: propertyName,
            sqlExpression: sqlExpr,
            isValueType: false,
            readerMethodName: "GetString");
    }

    /// <summary>
    /// Resolves a column expression to its quoted SQL form.
    /// Handles: u.UserName → "UserName"
    /// </summary>
    private static string? ResolveColumnSqlFromExpression(
        ExpressionSyntax expression,
        Dictionary<string, ColumnInfo> columnLookup,
        string lambdaParameterName)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax id &&
            id.Identifier.Text == lambdaParameterName)
        {
            var propName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propName, out var column))
            {
                return WrapIdentifier(column.ColumnName);
            }
        }

        return null;
    }

    /// <summary>
    /// Translates a string method call to SQL.
    /// </summary>
    private static string? TranslateStringMethodToSql(
        string methodName,
        string columnSql,
        ArgumentListSyntax arguments,
        SqlDialect dialect)
    {
        return methodName switch
        {
            "Substring" => TranslateSubstringToSql(columnSql, arguments, dialect),
            "ToLower" or "ToLowerInvariant" => $"LOWER({columnSql})",
            "ToUpper" or "ToUpperInvariant" => $"UPPER({columnSql})",
            "Trim" => $"TRIM({columnSql})",
            _ => null
        };
    }

    /// <summary>
    /// Translates Substring(start, length) to SQL SUBSTRING.
    /// </summary>
    private static string? TranslateSubstringToSql(
        string columnSql,
        ArgumentListSyntax arguments,
        SqlDialect dialect)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var startArg = arguments.Arguments[0].Expression;
        if (startArg is not LiteralExpressionSyntax startLit)
            return null;

        var startSql = $"({startLit.Token.ValueText} + 1)";

        if (arguments.Arguments.Count >= 2)
        {
            var lengthArg = arguments.Arguments[1].Expression;
            if (lengthArg is not LiteralExpressionSyntax lengthLit)
                return null;

            return $"SUBSTRING({columnSql}, {startSql}, {lengthLit.Token.ValueText})";
        }

        return dialect switch
        {
            SqlDialect.SqlServer => $"SUBSTRING({columnSql}, {startSql}, LEN({columnSql}))",
            _ => $"SUBSTRING({columnSql} FROM {startSql})"
        };
    }

    /// <summary>
    /// Attempts to parse a member access chain as a navigation access (single-entity path).
    /// Returns the navigation hops and final property name, or null if
    /// the chain root is not the lambda parameter or has zero hops.
    /// </summary>
    private static (List<string> Hops, string FinalProp)? TryParseNavigationChain(
        MemberAccessExpressionSyntax memberAccess,
        string lambdaParameterName)
    {
        var hops = new List<string>();
        var finalProp = memberAccess.Name.Identifier.Text;
        var current = memberAccess.Expression;

        // Walk through member access chain, unwrapping null-forgiving (!) operators
        while (true)
        {
            // Unwrap null-forgiving operator: o.User!.UserName -> o.User
            if (current is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
                current = postfix.Operand;

            if (current is MemberAccessExpressionSyntax inner)
            {
                hops.Insert(0, inner.Name.Identifier.Text);
                current = inner.Expression;
            }
            else
                break;
        }

        // Unwrap final null-forgiving on root
        if (current is PostfixUnaryExpressionSyntax rootPostfix &&
            rootPostfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
            current = rootPostfix.Operand;

        if (current is IdentifierNameSyntax id &&
            id.Identifier.Text == lambdaParameterName &&
            hops.Count > 0)
            return (hops, finalProp);

        return null;
    }

    /// <summary>
    /// Attempts to parse a member access chain as a navigation access (joined path).
    /// Returns the navigation hops, final property name, and source alias.
    /// </summary>
    private static (List<string> Hops, string FinalProp, string SourceAlias)? TryParseNavigationChainJoined(
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup)
    {
        var hops = new List<string>();
        var finalProp = memberAccess.Name.Identifier.Text;
        var current = memberAccess.Expression;

        while (true)
        {
            if (current is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
                current = postfix.Operand;

            if (current is MemberAccessExpressionSyntax inner)
            {
                hops.Insert(0, inner.Name.Identifier.Text);
                current = inner.Expression;
            }
            else
                break;
        }

        if (current is PostfixUnaryExpressionSyntax rootPostfix &&
            rootPostfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
            current = rootPostfix.Operand;

        if (current is IdentifierNameSyntax id &&
            perParamLookup.TryGetValue(id.Identifier.Text, out var entry) &&
            hops.Count > 0)
            return (hops, finalProp, entry.Alias);

        return null;
    }

    /// <summary>
    /// Checks if a member access chain is rooted on a lambda parameter (single-entity).
    /// Used to detect navigation chains like o.User.UserName where the root is the lambda param.
    /// </summary>
    private static bool IsNavigationMemberAccess(MemberAccessExpressionSyntax memberAccess, string lambdaParameterName)
    {
        var current = (ExpressionSyntax)memberAccess;
        while (true)
        {
            if (current is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
                current = postfix.Operand;
            else if (current is MemberAccessExpressionSyntax inner)
                current = inner.Expression;
            else
                break;
        }
        return current is IdentifierNameSyntax id && id.Identifier.Text == lambdaParameterName;
    }

    /// <summary>
    /// Checks if a member access chain is rooted on a joined lambda parameter.
    /// </summary>
    private static bool IsNavigationMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup)
    {
        var current = (ExpressionSyntax)memberAccess;
        while (true)
        {
            if (current is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression)
                current = postfix.Operand;
            else if (current is MemberAccessExpressionSyntax inner)
                current = inner.Expression;
            else
                break;
        }
        return current is IdentifierNameSyntax id && perParamLookup.ContainsKey(id.Identifier.Text);
    }
}
