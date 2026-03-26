using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Shared.Migration;

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
        var result = AnalyzeJoinedExpressionWithPlaceholders(body, perParamLookup, resultType, dialect);

        if (result.Kind == ProjectionKind.Tuple && result.Columns.Count > 0)
        {
            // Don't try to build tuple type name — types are empty placeholders
            // ResultTypeName will be rebuilt during enrichment
        }

        return result;
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
        SqlDialect dialect)
    {
        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax =>
                ProjectionInfo.CreateFailed(resultType,
                    "Anonymous type projections are not supported. Use a named record, class, or tuple instead.",
                    ProjectionFailureReason.AnonymousTypeNotSupported),

            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null =>
                AnalyzeJoinedInitializerWithPlaceholders(objectCreation.Initializer.Expressions, perParamLookup, resultType, ProjectionKind.Dto, dialect),

            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeJoinedInitializerWithPlaceholders(implicitCreation.Initializer.Expressions, perParamLookup, resultType, ProjectionKind.Dto, dialect),

            TupleExpressionSyntax tuple =>
                AnalyzeJoinedTupleWithPlaceholders(tuple, perParamLookup, resultType, dialect),

            MemberAccessExpressionSyntax memberAccess when IsJoinedMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumnWithPlaceholder(memberAccess, perParamLookup, resultType),

            InvocationExpressionSyntax invocation when IsAggregateCall(invocation) =>
                AnalyzeJoinedInvocation(invocation, perParamLookup, resultType, dialect),

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
            var quotedName = Quarry.Generators.Sql.SqlFormatting.QuoteIdentifier(dialect, col.ColumnName);
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
        SqlDialect dialect)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var argument in tuple.Arguments)
        {
            var propertyName = argument.NameColon?.Name.Identifier.Text
                ?? GetImplicitPropertyName(argument.Expression)
                ?? $"Item{ordinal + 1}";

            var col = ResolveJoinedProjectedExpressionWithPlaceholder(argument.Expression, perParamLookup, propertyName, ordinal++, dialect);
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
        SqlDialect dialect)
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

            var col = ResolveJoinedProjectedExpressionWithPlaceholder(assignment.Right, perParamLookup, propertyName, ordinal++, dialect);
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
        SqlDialect dialect)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return ResolveJoinedColumnWithPlaceholder(memberAccess, perParamLookup, propertyName, ordinal);

        if (expression is InvocationExpressionSyntax invocation && IsAggregateCall(invocation))
            return ResolveJoinedAggregate(invocation, perParamLookup, propertyName, ordinal, dialect);

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

                // Placeholder: PropertyName and TableAlias known, types will be enriched later
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
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
            var tupleTypeName = BuildTupleTypeName(result.Columns);
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
        SqlDialect dialect)
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
                AnalyzeJoinedInitializer(objectCreation.Initializer.Expressions, semanticModel, perParamLookup, resultType, ProjectionKind.Dto, dialect),

            // Implicit object creation
            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeJoinedInitializer(implicitCreation.Initializer.Expressions, semanticModel, perParamLookup, resultType, ProjectionKind.Dto, dialect),

            // Tuple: (u, o) => (u.Name, o.Total)
            TupleExpressionSyntax tuple =>
                AnalyzeJoinedTuple(tuple, semanticModel, perParamLookup, resultType, dialect),

            // Single column: (u, o) => u.Name
            MemberAccessExpressionSyntax memberAccess when IsJoinedMemberAccess(memberAccess, perParamLookup) =>
                AnalyzeJoinedSingleColumn(memberAccess, semanticModel, perParamLookup, resultType, dialect),

            // Aggregate function: (u, o) => Sql.Count()
            InvocationExpressionSyntax invocation when IsAggregateCall(invocation) =>
                AnalyzeJoinedInvocation(invocation, perParamLookup, resultType, dialect),

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
        SqlDialect dialect)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetJoinedAggregateInfo(methodName, invocation, perParamLookup, dialect);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                var column = new ProjectedColumn(
                    propertyName: "Value",
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: false,
                    ordinal: 0,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true,
                    readerMethodName: GetReaderMethodForAggregate(aggregateClrType));

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
        SqlDialect dialect)
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

            var col = ResolveJoinedProjectedExpression(assignment.Right, semanticModel, perParamLookup, propertyName, ordinal++, dialect);
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
        SqlDialect dialect)
    {
        var columns = new List<ProjectedColumn>();
        var ordinal = 0;

        foreach (var argument in tuple.Arguments)
        {
            var propertyName = argument.NameColon?.Name.Identifier.Text
                ?? GetImplicitPropertyName(argument.Expression)
                ?? $"Item{ordinal + 1}";

            var col = ResolveJoinedProjectedExpression(argument.Expression, semanticModel, perParamLookup, propertyName, ordinal++, dialect);
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
        SqlDialect dialect)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
            return ResolveJoinedColumn(memberAccess, semanticModel, perParamLookup, propertyName, ordinal);

        // Aggregate functions: Sql.Count(), Sql.Sum(u.Amount)
        if (expression is InvocationExpressionSyntax invocation && IsAggregateCall(invocation))
            return ResolveJoinedAggregate(invocation, perParamLookup, propertyName, ordinal, dialect);

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
        SqlDialect dialect)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetJoinedAggregateInfo(methodName, invocation, perParamLookup, dialect);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: false,
                    ordinal: ordinal,
                    alias: propertyName,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true,
                    readerMethodName: GetReaderMethodForAggregate(aggregateClrType));
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

        // Analyze the lambda body
        var result = AnalyzeExpression(body, semanticModel, columns, columnLookup, lambdaParameterName, resultType, entityName, dialect);

        // Post-analysis fixup: If result type is still invalid and we have column info, derive from columns
        if (!IsValidTypeName(result.ResultTypeName) && result.Columns.Count > 0)
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
                    result.FailureReason);
            }
        }

        // Tuple type fixup: If this is a tuple projection, rebuild the type name from columns
        // This is more reliable than using the semantic model which may have unresolved types
        if (result.Kind == ProjectionKind.Tuple && result.Columns.Count > 0)
        {
            var tupleTypeName = BuildTupleTypeName(result.Columns);
            if (IsValidTupleTypeName(tupleTypeName) && !IsValidTupleTypeName(result.ResultTypeName))
            {
                return new ProjectionInfo(
                    result.Kind,
                    tupleTypeName,
                    result.Columns,
                    result.IsOptimalPath,
                    result.NonOptimalReason,
                    result.FailureReason);
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

            // Determine column kind based on property name patterns
            var kind = ColumnKind.Standard;
            string? referencedEntityName = null;

            if (property.Name.EndsWith("Id") && property.Name.Length > 2)
            {
                // Simple heuristic for FK detection
                kind = ColumnKind.ForeignKey;
            }
            else if (property.Name == "Id" || property.Name.EndsWith("Id") && property.Name == typeSymbol.Name + "Id")
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
        SqlDialect dialect)
    {
        return expression switch
        {
            // Entity projection: u => u
            IdentifierNameSyntax identifier when identifier.Identifier.Text == lambdaParameterName =>
                CreateEntityProjection(entityColumns, resultType, dialect),

            // Anonymous type: u => new { u.Id, u.Name }
            AnonymousObjectCreationExpressionSyntax anonymous =>
                AnalyzeAnonymousType(anonymous, semanticModel, columnLookup, lambdaParameterName, resultType, entityName, dialect),

            // DTO/Object initializer: u => new UserDto { Id = u.Id }
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer != null =>
                AnalyzeObjectInitializer(objectCreation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Implicit object creation: u => new() { Id = u.Id }
            ImplicitObjectCreationExpressionSyntax implicitCreation when implicitCreation.Initializer != null =>
                AnalyzeImplicitObjectInitializer(implicitCreation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Tuple: u => (u.Id, u.Name)
            TupleExpressionSyntax tuple =>
                AnalyzeTuple(tuple, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Single column: u => u.Name
            MemberAccessExpressionSyntax memberAccess when IsMemberOfParameter(memberAccess, lambdaParameterName) =>
                AnalyzeSingleColumn(memberAccess, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

            // Invocation (possibly aggregate): u => Sql.Count()
            InvocationExpressionSyntax invocation =>
                AnalyzeInvocation(invocation, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),

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
        SqlDialect dialect)
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
        SqlDialect dialect)
    {
        return AnalyzeInitializerExpressions(
            objectCreation.Initializer!.Expressions,
            semanticModel,
            columnLookup,
            lambdaParameterName,
            resultType,
            ProjectionKind.Dto,
            dialect);
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
        SqlDialect dialect)
    {
        return AnalyzeInitializerExpressions(
            implicitCreation.Initializer!.Expressions,
            semanticModel,
            columnLookup,
            lambdaParameterName,
            resultType,
            ProjectionKind.Dto,
            dialect);
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
        SqlDialect dialect)
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
                dialect);

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
        SqlDialect dialect)
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
                dialect);

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
    /// Builds a tuple type name from projected columns.
    /// </summary>
    private static string BuildTupleTypeName(IReadOnlyList<ProjectedColumn> columns)
    {
        // Use ClrType which is the simple type name (int, string, etc.)
        // Fall back to FullClrType or "object" if ClrType is empty/unknown
        var elements = columns.Select(c =>
        {
            var typeName = c.ClrType;

            // Check for empty, whitespace, or unresolved type names
            // Note: "?" alone means unresolved, but "int?" is a valid nullable type
            if (IsUnresolvedTypeName(typeName))
            {
                typeName = c.FullClrType;
            }

            if (IsUnresolvedTypeName(typeName))
            {
                typeName = "object";
            }

            // Add nullable suffix if needed and not already present
            if (c.IsNullable && !typeName.EndsWith("?"))
            {
                typeName += "?";
            }

            // Omit default ItemN names — they cause CS9154 warnings when the
            // original tuple has unnamed elements (e.g., (string, int) vs (string, int Item2))
            var isDefaultName = c.PropertyName.StartsWith("Item") &&
                                int.TryParse(c.PropertyName.Substring(4), out var idx) &&
                                idx == c.Ordinal + 1;

            return isDefaultName ? typeName : $"{typeName} {c.PropertyName}";
        });
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Checks if a type name represents an unresolved type (not just a nullable type).
    /// </summary>
    private static bool IsUnresolvedTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return true;

        // Just "?" alone means unresolved type
        if (typeName == "?")
            return true;

        // "? " (question mark followed by space) means unresolved type with a name
        // This shouldn't happen in ClrType but could happen in some edge cases
        if (typeName?.StartsWith("? ") == true)
            return true;

        return false;
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
        var actualResultType = IsValidTypeName(resultType)
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
        SqlDialect dialect)
    {
        // Check for Sql.Count(), Sql.Sum(), etc.
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == "Sql")
        {
            var methodName = memberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetAggregateInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, dialect);

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
                    readerMethodName: GetReaderMethodForAggregate(aggregateClrType));

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
        SqlDialect dialect)
    {
        // Simple column reference: u.Name
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Direct property access: u.Name
            if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == lambdaParameterName)
            {
                var columnPropertyName = memberAccess.Name.Identifier.Text;
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
        }

        // Aggregate functions: Sql.Count(), Sql.Sum(u.Amount)
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax invMemberAccess &&
            invMemberAccess.Expression is IdentifierNameSyntax sqlIdentifier &&
            sqlIdentifier.Identifier.Text == "Sql")
        {
            var methodName = invMemberAccess.Name.Identifier.Text;
            var (sqlExpr, clrType) = GetAggregateInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, dialect);

            if (sqlExpr != null)
            {
                var aggregateClrType = clrType ?? "int";
                return new ProjectedColumn(
                    propertyName: propertyName,
                    columnName: "",
                    clrType: aggregateClrType,
                    fullClrType: aggregateClrType,
                    isNullable: false,
                    ordinal: ordinal,
                    alias: propertyName,
                    sqlExpression: sqlExpr,
                    isAggregateFunction: true,
                    isValueType: true, // Aggregate results are always value types
                    readerMethodName: GetReaderMethodForAggregate(aggregateClrType));
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

            // For unsupported expressions, we can still generate fallback code
            return new ProjectedColumn(
                propertyName: propertyName,
                columnName: "",
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
        SqlDialect dialect)
    {
        var arguments = invocation.ArgumentList.Arguments;

        switch (methodName)
        {
            case "Count":
                if (arguments.Count == 0)
                {
                    return ("COUNT(*)", "int");
                }
                else
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName, dialect);
                    return columnSql != null ? ($"COUNT({columnSql})", "int") : (null, null);
                }

            case "Sum":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName, dialect);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "decimal");
                    return columnSql != null ? ($"SUM({columnSql})", clrType) : (null, null);
                }
                break;

            case "Avg":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName, dialect);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "decimal");
                    return columnSql != null ? ($"AVG({columnSql})", clrType) : (null, null);
                }
                break;

            case "Min":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName, dialect);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "object");
                    return columnSql != null ? ($"MIN({columnSql})", clrType) : (null, null);
                }
                break;

            case "Max":
                if (arguments.Count > 0)
                {
                    var columnSql = GetColumnSql(arguments[0].Expression, columnLookup, lambdaParameterName, dialect);
                    var clrType = ResolveAggregateClrType(arguments[0].Expression, invocation, semanticModel,
                        columnLookup, lambdaParameterName, "object");
                    return columnSql != null ? ($"MAX({columnSql})", clrType) : (null, null);
                }
                break;
        }

        return (null, null);
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
            if (!IsUnresolvedTypeName(name))
                return name;
        }

        // Try 2: invocation return type (e.g., Sql.Sum(decimal) → decimal)
        var invMethodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (invMethodSymbol?.ReturnType != null && invMethodSymbol.ReturnType.TypeKind != TypeKind.Error)
        {
            var name = GetSimpleTypeName(invMethodSymbol.ReturnType);
            if (!IsUnresolvedTypeName(name))
                return name;
        }

        // Try 3: column lookup (handles generated entity types where the semantic model
        // returns error types but the column metadata is available from schema analysis)
        if (argumentExpression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == lambdaParameterName)
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propertyName, out var column) && !IsUnresolvedTypeName(column.ClrType))
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
        string lambdaParameterName,
        SqlDialect dialect)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text == lambdaParameterName)
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propertyName, out var column))
            {
                return QuoteIdentifier(column.ColumnName, dialect);
            }

            // Fallback: use property name as column name. This handles both cases:
            //   - Entity type IS resolved but property wasn't found (naming convention mismatch)
            //   - Entity type is generated and not yet in the semantic model (empty lookup)
            // The enrichment step (FixAggregateSqlExpression) rewrites with correct DB names.
            return QuoteIdentifier(propertyName, dialect);
        }

        return null;
    }

    /// <summary>
    /// Gets the SQL expression and CLR type for an aggregate function in a joined context.
    /// </summary>
    private static (string? SqlExpression, string? ClrType) GetJoinedAggregateInfo(
        string methodName,
        InvocationExpressionSyntax invocation,
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        SqlDialect dialect)
    {
        var arguments = invocation.ArgumentList.Arguments;

        switch (methodName)
        {
            case "Count":
                if (arguments.Count == 0)
                {
                    return ("COUNT(*)", "int");
                }
                else
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup, dialect);
                    return columnSql != null ? ($"COUNT({columnSql})", "int") : (null, null);
                }

            case "Sum":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup, dialect);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal");
                    return columnSql != null ? ($"SUM({columnSql})", clrType) : (null, null);
                }
                break;

            case "Avg":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup, dialect);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "decimal");
                    return columnSql != null ? ($"AVG({columnSql})", clrType) : (null, null);
                }
                break;

            case "Min":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup, dialect);
                    var clrType = ResolveJoinedAggregateClrType(arguments[0].Expression, perParamLookup, "object");
                    return columnSql != null ? ($"MIN({columnSql})", clrType) : (null, null);
                }
                break;

            case "Max":
                if (arguments.Count > 0)
                {
                    var columnSql = GetJoinedColumnSql(arguments[0].Expression, perParamLookup, dialect);
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
        Dictionary<string, (Dictionary<string, ColumnInfo> Lookup, string Alias)> perParamLookup,
        SqlDialect dialect)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            perParamLookup.TryGetValue(identifier.Identifier.Text, out var info))
        {
            var propertyName = memberAccess.Name.Identifier.Text;
            if (info.Lookup.TryGetValue(propertyName, out var column))
                return $"{info.Alias}.{QuoteIdentifier(column.ColumnName, dialect)}";

            // Fallback: use property name with table alias (enrichment rewrites later)
            return $"{info.Alias}.{QuoteIdentifier(propertyName, dialect)}";
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
            if (info.Lookup.TryGetValue(propertyName, out var column) && !IsUnresolvedTypeName(column.ClrType))
                return column.ClrType;
        }

        return defaultType;
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

    /// <summary>
    /// Gets the DbDataReader method name for an aggregate result type.
    /// </summary>
    private static string GetReaderMethodForAggregate(string clrType)
    {
        return clrType switch
        {
            "int" or "Int32" => "GetInt32",
            "long" or "Int64" => "GetInt64",
            "decimal" or "Decimal" => "GetDecimal",
            "double" or "Double" => "GetDouble",
            "float" or "Single" => "GetFloat",
            _ => "GetValue"
        };
    }

    /// <summary>
    /// Checks if a type name is valid (not null, empty, whitespace, or unresolved "?").
    /// </summary>
    private static bool IsValidTypeName(string? typeName)
    {
        return !IsUnresolvedTypeName(typeName);
    }

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
        var actualResultType = IsValidTypeName(resultType) ? resultType : "string";
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
        var columnSql = ResolveColumnSqlFromExpression(methodAccess.Expression, columnLookup, lambdaParameterName, dialect);
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
        string lambdaParameterName,
        SqlDialect dialect)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax id &&
            id.Identifier.Text == lambdaParameterName)
        {
            var propName = memberAccess.Name.Identifier.Text;
            if (columnLookup.TryGetValue(propName, out var column))
            {
                return QuoteIdentifier(column.ColumnName, dialect);
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
}
