using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;
using Quarry.Generators.Utilities;
using System.Security.Cryptography;
using System.Text;
// InterceptableLocation extension method is in Microsoft.CodeAnalysis.CSharp namespace

namespace Quarry.Generators.Parsing;

/// <summary>
/// Discovers method call sites on Quarry builder types for interceptor generation.
/// </summary>
internal static class UsageSiteDiscovery
{
    // Placeholder dialect used during discovery; overwritten during enrichment when context info is available.
    private const SqlDialect DefaultDiscoveryDialect = SqlDialect.PostgreSQL;

    // Quarry builder interface type names that we intercept.
    // Concrete types never leak to user code — context properties use QueryBuilder<T>.Create()
    // which returns IQueryBuilder<T>, and all fluent methods return interface types.
    private static readonly HashSet<string> BuilderTypeNames = new(StringComparer.Ordinal)
    {
        "IQueryBuilder",
        "IJoinedQueryBuilder",
        "IJoinedQueryBuilder3",
        "IJoinedQueryBuilder4",
        "IUpdateBuilder",
        "IExecutableUpdateBuilder",
        "IDeleteBuilder",
        "IExecutableDeleteBuilder",
        "IInsertBuilder",
        "IEntityAccessor",
        "EntityAccessor"
    };

    // Methods that can be intercepted (excluding builder-specific methods that need context)
    internal static readonly Dictionary<string, InterceptorKind> InterceptableMethods = new(StringComparer.Ordinal)
    {
        ["Select"] = InterceptorKind.Select,
        ["Where"] = InterceptorKind.Where,
        ["OrderBy"] = InterceptorKind.OrderBy,
        ["ThenBy"] = InterceptorKind.ThenBy,
        ["GroupBy"] = InterceptorKind.GroupBy,
        ["Having"] = InterceptorKind.Having,
        ["Set"] = InterceptorKind.Set,
        ["Join"] = InterceptorKind.Join,
        ["LeftJoin"] = InterceptorKind.LeftJoin,
        ["RightJoin"] = InterceptorKind.RightJoin,
        ["ExecuteFetchAllAsync"] = InterceptorKind.ExecuteFetchAll,
        ["ExecuteFetchFirstAsync"] = InterceptorKind.ExecuteFetchFirst,
        ["ExecuteFetchFirstOrDefaultAsync"] = InterceptorKind.ExecuteFetchFirstOrDefault,
        ["ExecuteFetchSingleAsync"] = InterceptorKind.ExecuteFetchSingle,
        ["ExecuteScalarAsync"] = InterceptorKind.ExecuteScalar,
        ["ExecuteNonQueryAsync"] = InterceptorKind.ExecuteNonQuery,
        ["ToAsyncEnumerable"] = InterceptorKind.ToAsyncEnumerable,
        ["ToDiagnostics"] = InterceptorKind.ToDiagnostics,
        ["Limit"] = InterceptorKind.Limit,
        ["Offset"] = InterceptorKind.Offset,
        ["Distinct"] = InterceptorKind.Distinct,
        ["WithTimeout"] = InterceptorKind.WithTimeout,
        ["Delete"] = InterceptorKind.DeleteTransition,
        ["Update"] = InterceptorKind.UpdateTransition,
        ["All"] = InterceptorKind.AllTransition,
        ["Insert"] = InterceptorKind.InsertTransition
    };

    // Methods on InsertBuilder that need special handling
    private static readonly HashSet<string> InsertBuilderMethods = new(StringComparer.Ordinal)
    {
        "ExecuteNonQueryAsync",
        "ExecuteScalarAsync",
        "ToDiagnostics"
    };

    // RawSql methods on QuarryContext that we intercept
    private static readonly Dictionary<string, InterceptorKind> RawSqlMethods = new(StringComparer.Ordinal)
    {
        ["RawSqlAsync"] = InterceptorKind.RawSqlAsync,
        ["RawSqlScalarAsync"] = InterceptorKind.RawSqlScalarAsync
    };

    /// <summary>
    /// Checks if an invocation expression is a potential Quarry builder method call.
    /// This is a quick syntactic check for the incremental generator's predicate.
    /// </summary>
    public static bool IsQuarryMethodCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        // Get the method name from the invocation
        var methodName = GetMethodName(invocation);
        if (methodName == null)
            return false;

        // Check if this is an interceptable method
        if (InterceptableMethods.ContainsKey(methodName)
            || RawSqlMethods.ContainsKey(methodName))
            return true;

        // Could be a context entity factory method (Users(), Orders(), Delete<T>(), Update<T>())
        if (invocation.ArgumentList.Arguments.Count == 0
            && invocation.Expression is MemberAccessExpressionSyntax
            && methodName.Length > 0 && char.IsUpper(methodName[0]))
            return true;

        return false;
    }

    /// <summary>
    /// Discovers a usage site from an invocation expression.
    /// Returns null if the invocation is not a Quarry builder method call.
    /// </summary>
    public static UsageSiteInfo? DiscoverUsageSite(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Get method symbol
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        // When a generated entity type is invisible, generic methods like Set<TValue>()
        // may fail to resolve (TValue inference fails). Fall back to CandidateSymbols.
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            if (symbolInfo.CandidateSymbols.Length == 1
                && symbolInfo.CandidateSymbols[0] is IMethodSymbol candidate)
            {
                methodSymbol = candidate;
            }
            else if (symbolInfo.CandidateSymbols.Length > 1)
            {
                // Multiple candidates — disambiguate by matching argument count to parameter count.
                // This handles overloads like Set(T entity) vs Set<TValue>(Expression, TValue)
                // when generated entity types make the type parameter invisible.
                var argCount = invocation.ArgumentList.Arguments.Count;
                IMethodSymbol? matched = null;
                int matchCount = 0;
                foreach (var sym in symbolInfo.CandidateSymbols)
                {
                    if (sym is IMethodSymbol ms && ms.Parameters.Length == argCount)
                    {
                        matched = ms;
                        matchCount++;
                    }
                }
                if (matchCount == 1 && matched != null)
                {
                    methodSymbol = matched;
                }
                else if (matchCount > 1)
                {
                    // Still ambiguous after argument count — try lambda parameter count.
                    // Disambiguates Join(u => u.Nav) [1-param] from Join<T>((a,b) => cond) [2-param].
                    var lambdaParamCount = GetLambdaParameterCount(invocation);
                    if (lambdaParamCount > 0)
                    {
                        matched = null;
                        matchCount = 0;
                        foreach (var sym in symbolInfo.CandidateSymbols)
                        {
                            if (sym is IMethodSymbol ms && ms.Parameters.Length == argCount
                                && GetExpressionLambdaParameterCount(ms) == lambdaParamCount)
                            {
                                matched = ms;
                                matchCount++;
                            }
                        }
                        if (matchCount == 1 && matched != null)
                            methodSymbol = matched;
                        else
                            return null;
                    }
                    else
                    {
                        // Non-lambda argument with multiple candidate overloads.
                        // Disambiguate Set(T entity) from Set(Action<T>) by excluding
                        // candidates whose first parameter is a delegate type.
                        matched = null;
                        matchCount = 0;
                        foreach (var sym in symbolInfo.CandidateSymbols)
                        {
                            if (sym is IMethodSymbol ms && ms.Parameters.Length == argCount
                                && !IsDelegateParameterType(ms.Parameters[0].Type))
                            {
                                matched = ms;
                                matchCount++;
                            }
                        }
                        if (matchCount == 1 && matched != null)
                            methodSymbol = matched;
                        else
                            return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // No candidates at all — try syntactic-only discovery for execution methods
                // on Update/Delete builders where generated entity types make the entire
                // receiver chain unresolvable.
                return TryDiscoverExecutionSiteSyntactically(invocation, semanticModel, cancellationToken);
            }
        }

        // Check if the method is on a Quarry builder type or QuarryContext (for RawSql methods)
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        var methodName = methodSymbol.Name;

        // Check for RawSql methods on QuarryContext first
        if (RawSqlMethods.TryGetValue(methodName, out var rawSqlKind) && IsQuarryContextType(containingType))
        {
            return DiscoverRawSqlUsageSite(invocation, methodSymbol, containingType, rawSqlKind, semanticModel, cancellationToken);
        }

        // Check for chain root: entity set factory methods on QuarryContext (e.g., db.Users())
        if (IsQuarryContextType(containingType)
            && methodSymbol.Parameters.Length == 0
            && methodSymbol.ReturnType is INamedTypeSymbol returnType
            && returnType is { Arity: 1 }
            && returnType.Name is "IQueryBuilder" or "EntityAccessor" or "IEntityAccessor")
        {
            var rootEntityType = returnType.TypeArguments[0];
            var rootEntityTypeName = rootEntityType.ToFullyQualifiedDisplayString();

            // Get interceptable location data
            string? rootLocationData = null;
            int rootLocationVersion = 1;
#if QUARRY_GENERATOR
            try
            {
#pragma warning disable RSEXPERIMENTAL002
                var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
                if (interceptableLocation != null)
                {
                    rootLocationData = interceptableLocation.Data;
                    rootLocationVersion = interceptableLocation.Version;
                }
            }
            catch { }
#endif
            if (rootLocationData == null)
                return null;

            var rootFilePath = invocation.SyntaxTree.FilePath;
            var rootLineSpan = invocation.SyntaxTree.GetLineSpan(invocation.Span);
            var rootLine = rootLineSpan.StartLinePosition.Line + 1;
            var rootColumn = rootLineSpan.StartLinePosition.Character + 1;
            var rootUniqueId = GenerateUniqueId(rootFilePath, rootLine, rootColumn, methodName);

            // Resolve context class name and namespace for the interceptor signature
            var contextClassName = containingType.Name;
            var contextNamespace = containingType.ContainingNamespace?.IsGlobalNamespace == false
                ? containingType.ContainingNamespace.ToDisplayString()
                : null;

            return new UsageSiteInfo(
                methodName: methodName,
                filePath: rootFilePath,
                line: rootLine,
                column: rootColumn,
                builderTypeName: "IQueryBuilder",
                entityTypeName: rootEntityTypeName,
                isAnalyzable: true,
                kind: InterceptorKind.ChainRoot,
                invocationSyntax: invocation,
                uniqueId: rootUniqueId,
                contextClassName: contextClassName,
                contextNamespace: contextNamespace,
                interceptableLocationData: rootLocationData,
                interceptableLocationVersion: rootLocationVersion);
        }

        if (!IsQuarryBuilderType(containingType))
            return null;

        // Get the method name and kind
        if (!InterceptableMethods.TryGetValue(methodName, out var kind))
        {
            if (methodName == "ToDiagnostics" && IsInsertBuilderType(containingType.Name))
                kind = InterceptorKind.InsertToDiagnostics;
            else
                return null;
        }

        // Check if this is an InsertBuilder execution method - use special kinds
        if (IsInsertBuilderType(containingType.Name) && InsertBuilderMethods.Contains(methodName))
        {
            kind = methodName switch
            {
                "ExecuteNonQueryAsync" => InterceptorKind.InsertExecuteNonQuery,
                "ExecuteScalarAsync" => InterceptorKind.InsertExecuteScalar,
                "ToDiagnostics" => InterceptorKind.InsertToDiagnostics,
                _ => kind
            };
        }

        // Extract initialized property names for insert interceptors
        HashSet<string>? initializedPropertyNames = null;
        if (kind is InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics)
        {
            initializedPropertyNames = ExtractInitializedPropertyNames(invocation);
        }

        // Check if this is a Where() call on DeleteBuilder or ExecutableDeleteBuilder
        if (methodName == "Where" &&
            (containingType.Name.Contains("DeleteBuilder")))
        {
            kind = InterceptorKind.DeleteWhere;
        }

        // Check if this is a Set() or Where() call on UpdateBuilder or ExecutableUpdateBuilder
        if (methodName == "Set" &&
            (containingType.Name.Contains("UpdateBuilder")))
        {
            // Distinguish Set(T entity) (1 arg, POCO) / Set(Action<T>) / Set<TValue>(lambda, value)
            if (invocation.ArgumentList.Arguments.Count == 1 && !methodSymbol.IsGenericMethod)
            {
                // 1-arg non-generic: Set(T entity) or Set(Action<T>).
                // If the argument is a lambda expression, it's Set(Action<T>).
                var singleArg = invocation.ArgumentList.Arguments[0].Expression;
                if (singleArg is LambdaExpressionSyntax)
                {
                    kind = InterceptorKind.UpdateSetAction;
                }
                else
                {
                    kind = InterceptorKind.UpdateSetPoco;
                    initializedPropertyNames = ExtractInitializedPropertyNamesFromSetPoco(invocation);
                }
            }
            else
            {
                kind = InterceptorKind.UpdateSet;
            }
        }
        if (methodName == "Where" &&
            (containingType.Name.Contains("UpdateBuilder")))
        {
            kind = InterceptorKind.UpdateWhere;
        }

        // Get location information
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        var (filePath, line, column) = location.Value;

        // Get the InterceptableLocation for the new attribute format
        string? interceptableLocationData = null;
        int interceptableLocationVersion = 1;
#if QUARRY_GENERATOR
        try
        {
#pragma warning disable RSEXPERIMENTAL002 // GetInterceptableLocation is experimental
            var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
            if (interceptableLocation != null)
            {
                interceptableLocationData = interceptableLocation.Data;
                interceptableLocationVersion = interceptableLocation.Version;
            }
        }
        catch
        {
            // If GetInterceptableLocation fails, we'll fall back to old behavior
            // This can happen with older Roslyn versions
        }
#endif

        // Extract entity type from the builder
        var (entityTypeName, resultTypeName) = ExtractTypeArguments(containingType);
        if (entityTypeName == null)
            return null;

        // When the builder comes from a generic method return (e.g. base Insert<T>),
        // the type argument may be an unsubstituted type parameter because the entity
        // types are generated by Phase 1 and invisible to Phase 2's semantic model.
        var resolvedSyntactically = false;
        if (containingType.TypeArguments.Length > 0 && containingType.TypeArguments[0].TypeKind == TypeKind.TypeParameter
            && invocation.Expression is MemberAccessExpressionSyntax memberAccessForType)
        {
            // Try semantic resolution first (works when entity types are from referenced assemblies)
            var concreteType = ResolveTypeParameterFromReceiverChain(
                memberAccessForType.Expression, semanticModel, cancellationToken);
            if (concreteType != null)
            {
                containingType = containingType.OriginalDefinition.Construct(concreteType);
                (entityTypeName, resultTypeName) = ExtractTypeArguments(containingType);
                if (entityTypeName == null)
                    return null;
            }
            else
            {
                // Semantic resolution failed (entity types are generated and invisible).
                // Extract the type name syntactically from the Insert/InsertMany argument.
                var syntacticTypeName = ExtractEntityTypeNameFromChain(memberAccessForType.Expression);
                if (syntacticTypeName != null)
                {
                    entityTypeName = syntacticTypeName;
                    resolvedSyntactically = true;
                }
            }
        }

        // If the entity type is still an unresolved type parameter after all resolution attempts,
        // skip this site — generating an interceptor with a type parameter would cause CS0246.
        if (!resolvedSyntactically
            && containingType.TypeArguments.Length > 0
            && containingType.TypeArguments[0].TypeKind == TypeKind.TypeParameter)
            return null;

        // Check analyzability
        var (isAnalyzable, reason) = AnalyzabilityChecker.CheckAnalyzability(invocation, semanticModel);

        // Generate unique ID
        var uniqueId = GenerateUniqueId(filePath, line, column, methodName);

        // For Select() calls, analyze the projection (skip for joined builders — analyzed during enrichment)
        ProjectionInfo? projectionInfo = null;
        if (kind == InterceptorKind.Select && isAnalyzable && !containingType.Name.Contains("JoinedQueryBuilder"))
        {
            try
            {
                var entityType = containingType.TypeArguments[0];
                // Use default dialect for now - will be refined later when context info is available
                projectionInfo = ProjectionAnalyzer.AnalyzeFromTypeSymbol(
                    invocation,
                    semanticModel,
                    entityType,
                    DefaultDiscoveryDialect);
            }
            catch
            {
                // If projection analysis fails, mark as non-analyzable
                isAnalyzable = false;
                reason = "Failed to analyze Select() projection";
            }
        }

        // For clause methods (Where, OrderBy, GroupBy, Having, Set), analyze the expression.
        // Clause analysis is allowed even for conditional sites — the lambda expression itself
        // is analyzable, enabling lightweight interceptors for ChainAnalyzer Tier 1/2 chains.
        ClauseInfo? clauseInfo = null;
        PendingClauseInfo? pendingClauseInfo = null;
        var isOnJoinedBuilder = containingType.Name.Contains("JoinedQueryBuilder");
        var clauseAnalyzable = isAnalyzable || AnalyzabilityChecker.IsClauseAnalyzable(invocation, semanticModel);
        if (clauseAnalyzable && IsClauseMethod(kind) && !isOnJoinedBuilder)
        {
            try
            {
                var entityType = containingType.TypeArguments[0];
                var (clause, pending) = AnalyzeClause(kind, invocation, semanticModel, entityType);
                clauseInfo = clause;
                pendingClauseInfo = pending;

                // If we have neither successful clause nor pending clause, mark as non-analyzable
                if (clauseInfo == null && pendingClauseInfo == null)
                {
                    isAnalyzable = false;
                    reason = $"Failed to analyze {methodName}() clause expression";
                }
                else if (clauseInfo != null && !clauseInfo.IsSuccess)
                {
                    // Semantic analysis returned an error and we have no pending fallback
                    isAnalyzable = false;
                    reason = clauseInfo.ErrorMessage ?? "Failed to analyze clause expression";
                    clauseInfo = null;
                }
                // Note: if pendingClauseInfo is set, we keep isAnalyzable=true for deferred translation
            }
            catch
            {
                // If clause analysis fails, mark as non-analyzable
                isAnalyzable = false;
                reason = $"Failed to analyze {methodName}() clause";
            }
        }

        // For Join/LeftJoin/RightJoin, extract the joined entity type and analyze the join condition
        string? joinedEntityTypeName = null;
        bool isNavigationJoin = false;
        if (kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
            && methodSymbol.TypeArguments.Length > 0)
        {
            var joinedType = methodSymbol.TypeArguments[0];
            joinedEntityTypeName = joinedType.ToFullyQualifiedDisplayString();

            // Detect navigation overload: single-parameter lambda like u => u.Orders
            isNavigationJoin = IsNavigationJoinLambda(invocation, semanticModel);

            if (isAnalyzable && clauseInfo == null && !isNavigationJoin)
            {
                try
                {
                    var entityType = containingType.TypeArguments[0];
                    var joinClauseKind = kind switch
                    {
                        InterceptorKind.LeftJoin => JoinClauseKind.Left,
                        InterceptorKind.RightJoin => JoinClauseKind.Right,
                        _ => JoinClauseKind.Inner
                    };
                    // Use default dialect for now
                    clauseInfo = ClauseTranslator.TranslateJoin(
                        invocation, semanticModel, entityType, joinedType,
                        DefaultDiscoveryDialect, joinClauseKind);

                    if (clauseInfo != null && !clauseInfo.IsSuccess)
                    {
                        // Join analysis failed — clear clauseInfo but keep isAnalyzable
                        // so the fallback interceptor with concrete types is still emitted
                        clauseInfo = null;
                    }
                }
                catch
                {
                    // Join analysis failed - will use fallback
                }
            }
        }

        // Extract all entity type names from joined builder types
        var joinedEntityTypeNames = ExtractJoinedEntityTypeNames(containingType);

        // Capture resolved key type for OrderBy/ThenBy/GroupBy methods
        string? keyTypeName = null;
        if (kind is InterceptorKind.OrderBy or InterceptorKind.ThenBy or InterceptorKind.GroupBy
            && methodSymbol.TypeArguments.Length > 0)
        {
            var keyType = methodSymbol.TypeArguments[0];
            if (keyType.TypeKind != TypeKind.TypeParameter && keyType.TypeKind != TypeKind.Error)
            {
                keyTypeName = keyType.ToFullyQualifiedDisplayString();
            }
        }

        // Capture constant integer value for Limit/Offset calls.
        // Used by ToDiagnostics prebuilt chains to inline literal pagination values.
        int? constantIntValue = null;
        if (kind is InterceptorKind.Limit or InterceptorKind.Offset
            && invocation.ArgumentList.Arguments.Count > 0)
        {
            var argExpr = invocation.ArgumentList.Arguments[0].Expression;
            var constValue = semanticModel.GetConstantValue(argExpr);
            if (constValue.HasValue && constValue.Value is int intVal)
                constantIntValue = intVal;
        }

        return new UsageSiteInfo(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            builderTypeName: containingType.Name,
            entityTypeName: entityTypeName,
            isAnalyzable: isAnalyzable,
            kind: kind,
            invocationSyntax: invocation,
            uniqueId: uniqueId,
            resultTypeName: resultTypeName,
            nonAnalyzableReason: reason,
            contextClassName: ResolveContextFromCallSite(invocation, semanticModel, cancellationToken),
            projectionInfo: projectionInfo,
            clauseInfo: clauseInfo,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion,
            pendingClauseInfo: pendingClauseInfo,
            joinedEntityTypeName: joinedEntityTypeName,
            joinedEntityTypeNames: joinedEntityTypeNames,
            initializedPropertyNames: initializedPropertyNames,
            keyTypeName: keyTypeName,
            isNavigationJoin: isNavigationJoin,
            constantIntValue: constantIntValue,
            builderKind: ClassifyBuilderKind(containingType.Name));
    }

    /// <summary>
    /// Detects whether a join invocation uses the navigation overload
    /// (single-parameter lambda returning NavigationList&lt;T&gt;).
    /// </summary>
    /// <summary>
    /// Gets the lambda parameter count from the first argument of an invocation.
    /// Returns 0 if no lambda is found.
    /// </summary>
    private static int GetLambdaParameterCount(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return 0;

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        if (argument is SimpleLambdaExpressionSyntax)
            return 1;

        if (argument is ParenthesizedLambdaExpressionSyntax parenLambda)
            return parenLambda.ParameterList.Parameters.Count;

        return 0;
    }

    /// <summary>
    /// Gets the expected lambda parameter count from a method symbol's first Expression parameter.
    /// For example, Expression&lt;Func&lt;T, bool&gt;&gt; → 1, Expression&lt;Func&lt;T1, T2, bool&gt;&gt; → 2.
    /// </summary>
    private static int GetExpressionLambdaParameterCount(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return 0;

        var paramType = method.Parameters[0].Type;

        // Unwrap Expression<TDelegate> → TDelegate
        if (paramType is INamedTypeSymbol exprType
            && exprType.Name == "Expression"
            && exprType.TypeArguments.Length == 1
            && exprType.TypeArguments[0] is INamedTypeSymbol delegateType)
        {
            // Func<T, TResult> has N type args where N-1 are parameters
            // But for navigation join: Func<T, NavigationList<TJoined>> → 1 param
            // For condition join: Func<T1, T2, bool> → 2 params
            return delegateType.TypeArguments.Length - 1;
        }

        // Raw delegate types: Action<T> has 1 type arg = 1 parameter,
        // Action<T1, T2> has 2 type args = 2 parameters.
        // This handles Set(Action<T>) disambiguation from Set(T entity).
        if (paramType is INamedTypeSymbol rawDelegate
            && rawDelegate.TypeKind == TypeKind.Delegate
            && rawDelegate.TypeArguments.Length > 0)
        {
            return rawDelegate.TypeArguments.Length;
        }

        return 0;
    }

    /// <summary>
    /// Checks if a type symbol is a delegate type (Action, Func, etc.).
    /// Used to disambiguate Set(T entity) from Set(Action&lt;T&gt;) when the argument is not a lambda.
    /// </summary>
    private static bool IsDelegateParameterType(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Delegate;
    }

    private static bool IsNavigationJoinLambda(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        // Navigation overload uses a simple lambda: u => u.Orders
        // (SimpleLambdaExpression has a single parameter without parentheses)
        if (argument is SimpleLambdaExpressionSyntax)
            return true;

        // Also handle parenthesized single-param lambda: (u) => u.Orders
        if (argument is ParenthesizedLambdaExpressionSyntax parenLambda
            && parenLambda.ParameterList.Parameters.Count == 1)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an interceptor kind is a clause method.
    /// </summary>
    private static bool IsClauseMethod(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Where => true,
            InterceptorKind.OrderBy => true,
            InterceptorKind.ThenBy => true,
            InterceptorKind.GroupBy => true,
            InterceptorKind.Having => true,
            InterceptorKind.Set => true,
            InterceptorKind.DeleteWhere => true,
            InterceptorKind.UpdateSet => true,
            InterceptorKind.UpdateSetAction => true,
            InterceptorKind.UpdateWhere => true,
            _ => false
        };
    }

    /// <summary>
    /// Analyzes a clause expression based on its kind.
    /// Returns a tuple of (ClauseInfo, PendingClauseInfo) where one will be non-null.
    /// </summary>
    private static (ClauseInfo? Clause, PendingClauseInfo? Pending) AnalyzeClause(
        InterceptorKind kind,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ITypeSymbol entityType)
    {
        var dialect = DefaultDiscoveryDialect;

        // First try semantic analysis
        var clauseInfo = kind switch
        {
            InterceptorKind.Where => ClauseTranslator.TranslateWhere(invocation, semanticModel, entityType, dialect),
            InterceptorKind.DeleteWhere => ClauseTranslator.TranslateWhere(invocation, semanticModel, entityType, dialect),
            InterceptorKind.UpdateWhere => ClauseTranslator.TranslateWhere(invocation, semanticModel, entityType, dialect),
            InterceptorKind.UpdateSet => ClauseTranslator.TranslateSet(invocation, semanticModel, entityType, dialect, existingParameterCount: 0),
            InterceptorKind.UpdateSetAction => ClauseTranslator.TranslateSetAction(invocation, semanticModel, entityType, dialect, existingParameterCount: 0),
            InterceptorKind.OrderBy => ClauseTranslator.TranslateOrderBy(invocation, semanticModel, entityType, dialect),
            InterceptorKind.ThenBy => ClauseTranslator.TranslateOrderBy(invocation, semanticModel, entityType, dialect),
            InterceptorKind.GroupBy => ClauseTranslator.TranslateGroupBy(invocation, semanticModel, entityType, dialect),
            InterceptorKind.Having => ClauseTranslator.TranslateHaving(invocation, semanticModel, entityType, dialect),
            InterceptorKind.Set => ClauseTranslator.TranslateSet(invocation, semanticModel, entityType, dialect, existingParameterCount: 0),
            _ => null
        };

        // If semantic analysis succeeded, return it
        if (clauseInfo != null && clauseInfo.IsSuccess)
        {
            return (clauseInfo, null);
        }

        // Semantic analysis failed - try syntactic fallback for deferred translation
        var pendingClause = TrySyntacticAnalysis(kind, invocation, semanticModel);
        if (pendingClause != null)
        {
            return (null, pendingClause);
        }

        // Both analyses failed - return the original failure
        return (clauseInfo, null);
    }

    /// <summary>
    /// Attempts syntactic analysis when semantic analysis fails.
    /// Returns a PendingClauseInfo for deferred translation during enrichment.
    /// </summary>
    private static PendingClauseInfo? TrySyntacticAnalysis(
        InterceptorKind kind,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Get the first argument (the lambda expression)
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (argument is not LambdaExpressionSyntax lambda)
            return null;

        // Get lambda body
        var body = SyntacticExpressionParser.GetLambdaBody(lambda);
        if (body == null)
            return null;

        // Get lambda parameter names
        var parameterNames = SyntacticExpressionParser.GetLambdaParameterNames(lambda);
        if (parameterNames.Count == 0)
            return null;

        // Parse the expression syntactically with path tracking for captured variables
        var syntacticExpr = SyntacticExpressionParser.ParseWithPathTracking(body, parameterNames);

        // If the result is an unknown expression, we can't proceed
        if (syntacticExpr is SyntacticUnknown)
            return null;

        // Also parse via SqlExprParser directly from syntax (avoids adapter conversion later)
        var sqlExprParamNames = IR.SqlExprParser.GetLambdaParameterNames(lambda);
        var sqlExpr = IR.SqlExprParser.ParseWithPathTracking(body, sqlExprParamNames);

        // Determine if this is descending order (for OrderBy/ThenBy)
        var isDescending = false;
        if ((kind == InterceptorKind.OrderBy || kind == InterceptorKind.ThenBy) &&
            invocation.ArgumentList.Arguments.Count >= 2)
        {
            var directionArg = invocation.ArgumentList.Arguments[1].Expression;
            isDescending = IsDescendingDirection(directionArg, semanticModel);
        }

        // Map interceptor kind to clause kind
        var clauseKind = kind switch
        {
            InterceptorKind.Where => ClauseKind.Where,
            InterceptorKind.DeleteWhere => ClauseKind.Where,
            InterceptorKind.UpdateWhere => ClauseKind.Where,
            InterceptorKind.UpdateSet => ClauseKind.Set,
            InterceptorKind.UpdateSetAction => ClauseKind.Set,
            InterceptorKind.OrderBy => ClauseKind.OrderBy,
            InterceptorKind.ThenBy => ClauseKind.OrderBy,
            InterceptorKind.GroupBy => ClauseKind.GroupBy,
            InterceptorKind.Having => ClauseKind.Having,
            InterceptorKind.Set => ClauseKind.Set,
            _ => ClauseKind.Where
        };

        // Get the first parameter name
        var parameterName = parameterNames.First();

        return new PendingClauseInfo(clauseKind, parameterName, syntacticExpr, isDescending, parsedSqlExpr: sqlExpr);
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
    /// Discovers all usage sites in a compilation.
    /// </summary>
    public static IEnumerable<UsageSiteInfo> DiscoverAllUsageSites(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var usageSite = DiscoverUsageSite(invocation, semanticModel, cancellationToken);
                if (usageSite != null)
                {
                    yield return usageSite;
                }
            }
        }
    }

    /// <summary>
    /// Walks backward from the terminal invocation through the fluent chain,
    /// finds all Insert/InsertMany/Values calls, and collects property names
    /// from their object initializers into a union set.
    /// Returns null if any argument is non-analyzable or if the union is empty.
    /// </summary>
    /// <summary>
    /// Extracts initialized property names from a Set(entity) call's single argument.
    /// Returns null if the argument is not an analyzable object initializer.
    /// </summary>
    private static HashSet<string>? ExtractInitializedPropertyNamesFromSetPoco(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
            return null;

        var argExpr = invocation.ArgumentList.Arguments[0].Expression;
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (!TryExtractPropertyNamesFromExpression(argExpr, result))
            return null;

        return result.Count > 0 ? result : null;
    }

    private static HashSet<string>? ExtractInitializedPropertyNames(InvocationExpressionSyntax terminal)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var current = (ExpressionSyntax)terminal;

        while (current is InvocationExpressionSyntax invoc)
        {
            string? calledMethod = null;
            ExpressionSyntax? next = null;

            if (invoc.Expression is MemberAccessExpressionSyntax ma)
            {
                calledMethod = ma.Name.Identifier.ValueText;
                next = ma.Expression;
            }

            if (calledMethod == "Insert" || calledMethod == "Values")
            {
                if (!TryExtractPropertyNamesFromArgument(invoc, result))
                    return null;
            }
            else if (calledMethod == "InsertMany")
            {
                if (!TryExtractPropertyNamesFromInsertManyArgument(invoc, result))
                    return null;
            }

            if (next != null)
                current = next;
            else
                break;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Extracts property names from a single Insert()/Values() argument.
    /// Returns false if the argument is non-analyzable.
    /// </summary>
    private static bool TryExtractPropertyNamesFromArgument(InvocationExpressionSyntax invocation, HashSet<string> result)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        var argExpr = invocation.ArgumentList.Arguments[0].Expression;
        return TryExtractPropertyNamesFromExpression(argExpr, result);
    }

    /// <summary>
    /// Handles ObjectCreationExpressionSyntax and ImplicitObjectCreationExpressionSyntax,
    /// iterating Initializer.Expressions for AssignmentExpressionSyntax nodes to get property names.
    /// Returns false if the expression is non-analyzable (variable, factory method, etc.).
    /// </summary>
    private static bool TryExtractPropertyNamesFromExpression(ExpressionSyntax expression, HashSet<string> result)
    {
        InitializerExpressionSyntax? initializer = null;

        if (expression is ObjectCreationExpressionSyntax objCreation)
        {
            initializer = objCreation.Initializer;
        }
        else if (expression is ImplicitObjectCreationExpressionSyntax implicitCreation)
        {
            initializer = implicitCreation.Initializer;
        }
        else
        {
            // Non-analyzable: variable reference, factory method, etc.
            return false;
        }

        if (initializer == null)
            return true; // new User() with no initializer — no properties set

        foreach (var expr in initializer.Expressions)
        {
            if (expr is AssignmentExpressionSyntax assignment
                && assignment.Left is IdentifierNameSyntax identifier)
            {
                result.Add(identifier.Identifier.ValueText);
            }
        }

        return true;
    }

    /// <summary>
    /// Handles InsertMany() arguments: ImplicitArrayCreationExpressionSyntax,
    /// ArrayCreationExpressionSyntax, and CollectionExpressionSyntax.
    /// Returns false if non-analyzable.
    /// </summary>
    private static bool TryExtractPropertyNamesFromInsertManyArgument(InvocationExpressionSyntax invocation, HashSet<string> result)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        var argExpr = invocation.ArgumentList.Arguments[0].Expression;

        if (argExpr is ImplicitArrayCreationExpressionSyntax implicitArray && implicitArray.Initializer != null)
        {
            return ExtractFromArrayElements(implicitArray.Initializer.Expressions, result);
        }

        if (argExpr is ArrayCreationExpressionSyntax arrayCreation && arrayCreation.Initializer != null)
        {
            return ExtractFromArrayElements(arrayCreation.Initializer.Expressions, result);
        }

        if (argExpr is CollectionExpressionSyntax collectionExpr)
        {
            foreach (var element in collectionExpr.Elements)
            {
                if (element is ExpressionElementSyntax exprElement)
                {
                    if (!TryExtractPropertyNamesFromExpression(exprElement.Expression, result))
                        return false;
                }
                else
                {
                    return false; // Spread or other non-analyzable element
                }
            }
            return true;
        }

        // Non-analyzable: variable, method call, etc.
        return false;
    }

    /// <summary>
    /// Helper to iterate array initializer elements and extract property names.
    /// </summary>
    private static bool ExtractFromArrayElements(SeparatedSyntaxList<ExpressionSyntax> elements, HashSet<string> result)
    {
        foreach (var element in elements)
        {
            if (!TryExtractPropertyNamesFromExpression(element, result))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Walks the receiver invocation chain to find an Insert/InsertMany call and extracts
    /// the entity type name syntactically from its argument expression.
    /// For example, in <c>ctx.Insert(new User{}).Values(...).ToDiagnostics()</c>, this finds
    /// <c>Insert(new User{})</c> and returns "User".
    /// </summary>
    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax receiverExpression)
        => ExtractEntityTypeNameFromChain(receiverExpression, null, default);

    private static string? ExtractEntityTypeNameFromChain(
        ExpressionSyntax receiverExpression,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        var current = receiverExpression;
        while (current is InvocationExpressionSyntax invoc)
        {
            string? calledMethod = null;
            if (invoc.Expression is MemberAccessExpressionSyntax ma)
            {
                calledMethod = ma.Name.Identifier.ValueText;
                // Prepare for next iteration (walk deeper)
                var next = ma.Expression;

                if (calledMethod == "Insert" || calledMethod == "InsertMany")
                {
                    if (invoc.ArgumentList.Arguments.Count > 0)
                    {
                        var argExpr = invoc.ArgumentList.Arguments[0].Expression;
                        // Try direct new expression first
                        var result = ExtractTypeNameFromNewExpression(argExpr);
                        if (result != null)
                            return result;

                        // Try resolving through variable initializer
                        return ExtractTypeNameFromIdentifierInitializer(argExpr);
                    }
                    return null;
                }

                // Update<T>() and Delete<T>() carry the entity type as a generic argument
                if (calledMethod == "Update" || calledMethod == "Delete")
                {
                    if (ma.Name is GenericNameSyntax genericName
                        && genericName.TypeArgumentList.Arguments.Count == 1)
                    {
                        return genericName.TypeArgumentList.Arguments[0].ToString();
                    }
                    return null;
                }

                current = next;
            }
            else
            {
                break;
            }
        }

        // Fallback: if the chain root is a property access (e.g., _db.Users for QueryBuilder),
        // try semantic resolution to get the entity type from the property's return type.
        if (semanticModel != null && current is MemberAccessExpressionSyntax propAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(propAccess, cancellationToken);
            var type = typeInfo.Type as INamedTypeSymbol;
            if (type != null && (type.Name == "IQueryBuilder" || type.Name == "QueryBuilder") && type.TypeArguments.Length >= 1)
            {
                return type.TypeArguments[0].Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Syntactic-only fallback for discovering execution methods (ExecuteNonQueryAsync, ExecuteScalarAsync)
    /// on Update/Delete builders when the semantic model completely fails to resolve the method
    /// (no symbol, no candidates). This happens when generated entity types make the entire
    /// receiver chain unresolvable.
    /// </summary>
    private static UsageSiteInfo? TryDiscoverExecutionSiteSyntactically(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!InterceptableMethods.TryGetValue(methodName, out var kind))
            return null;

        // Only handle execution methods — clause methods need semantic analysis
        if (kind is not (InterceptorKind.ExecuteNonQuery or InterceptorKind.ExecuteScalar
            or InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ToAsyncEnumerable))
            return null;

        // Walk the receiver chain to find the builder root (Update<T>() or Delete<T>())
        var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
        string? builderKind = null;
        string? contextClassName = null;

        if (entityTypeName != null)
        {
            // Direct fluent chain — determine builder kind from the chain
            builderKind = DetermineBuilderKindFromChain(memberAccess.Expression);
        }
        else
        {
            // Try variable receiver tracing: del.ExecuteNonQueryAsync() where del = _db.Delete<User>().Where(...)
            entityTypeName = TryExtractEntityTypeFromVariableReceiver(
                memberAccess.Expression, semanticModel, cancellationToken,
                out builderKind, out contextClassName);
        }

        if (entityTypeName == null || builderKind == null)
            return null;

        // Get location
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        var (filePath, line, column) = location.Value;

        // Get interceptable location
        string? interceptableLocationData = null;
        int? interceptableLocationVersion = null;
#if ROSLYN_4_12_OR_GREATER
        try
        {
#pragma warning disable RSEXPERIMENTAL002
            var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
            if (interceptableLocation != null)
            {
                interceptableLocationData = interceptableLocation.Data;
                interceptableLocationVersion = interceptableLocation.Version;
            }
        }
        catch { }
#endif

        var uniqueId = GenerateUniqueId(filePath, line, column, methodName);

        // Resolve context if not already resolved by variable tracing
        if (contextClassName == null)
            contextClassName = ResolveContextFromCallSite(invocation, semanticModel, cancellationToken);

        return new UsageSiteInfo(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            builderTypeName: builderKind,
            entityTypeName: entityTypeName,
            isAnalyzable: true,
            kind: kind,
            invocationSyntax: invocation,
            uniqueId: uniqueId,
            resultTypeName: null,
            contextClassName: contextClassName,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion ?? 1,
            builderKind: ClassifyBuilderKind(builderKind));
    }

    /// <summary>
    /// Walks the receiver chain to determine the builder kind (UpdateBuilder, DeleteBuilder, etc.).
    /// </summary>
    private static string? DetermineBuilderKindFromChain(ExpressionSyntax receiverExpression)
    {
        var current = receiverExpression;
        while (current is InvocationExpressionSyntax invoc)
        {
            if (invoc.Expression is MemberAccessExpressionSyntax ma)
            {
                var calledMethod = ma.Name.Identifier.ValueText;
                if (calledMethod == "Update")
                    return "IExecutableUpdateBuilder";
                if (calledMethod == "Delete")
                    return "IExecutableDeleteBuilder";
                current = ma.Expression;
            }
            else
                break;
        }

        // If chain root is a property access (e.g., _db.Users), it's a QueryBuilder
        if (current is MemberAccessExpressionSyntax)
            return "IQueryBuilder";

        return null;
    }

    /// <summary>
    /// Traces a variable receiver (e.g., <c>del</c> in <c>del.ExecuteNonQueryAsync()</c>) back to its
    /// declaration initializer and extracts the entity type, builder kind, and context class name
    /// from the initializer's fluent chain.
    /// </summary>
    private static string? TryExtractEntityTypeFromVariableReceiver(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string? builderKind,
        out string? contextClassName)
    {
        builderKind = null;
        contextClassName = null;

        // The receiver must bottom out at an identifier (a variable name)
        // Walk through any remaining member access / invocation to find the identifier
        var current = receiver;
        while (current is InvocationExpressionSyntax invoc)
        {
            if (invoc.Expression is MemberAccessExpressionSyntax ma)
                current = ma.Expression;
            else
                break;
        }
        if (current is MemberAccessExpressionSyntax memberAccess2)
            current = memberAccess2.Expression;

        if (current is not IdentifierNameSyntax identifier)
            return null;

        // Resolve to a local symbol
        var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        if (symbol is not ILocalSymbol local || local.DeclaringSyntaxReferences.Length != 1)
            return null;

        // Find the declaration
        var declSyntax = local.DeclaringSyntaxReferences[0].GetSyntax();
        if (declSyntax is not VariableDeclaratorSyntax declarator || declarator.Initializer?.Value == null)
            return null;

        var initExpr = declarator.Initializer.Value;

        // Walk the initializer chain to extract entity type
        var entityTypeName = ExtractEntityTypeNameFromChain(initExpr, semanticModel, cancellationToken);
        if (entityTypeName == null)
            return null;

        // Determine builder kind from the initializer chain
        builderKind = DetermineBuilderKindFromChain(initExpr);

        // Resolve context from the initializer chain
        contextClassName = ResolveContextFromInitializer(initExpr, semanticModel, cancellationToken);

        return entityTypeName;
    }

    /// <summary>
    /// Walks a fluent chain initializer expression to find the context root and resolve its type.
    /// </summary>
    private static string? ResolveContextFromInitializer(
        ExpressionSyntax initializerExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Walk the fluent chain to find the root
        var current = initializerExpression;
        while (current is InvocationExpressionSyntax invoc)
        {
            if (invoc.Expression is MemberAccessExpressionSyntax ma)
                current = ma.Expression;
            else
                break;
        }

        // For property access like _db.Users, get _db
        if (current is MemberAccessExpressionSyntax propAccess)
            current = propAccess.Expression;

        // Get the type of the root receiver
        var typeInfo = semanticModel.GetTypeInfo(current, cancellationToken);
        var type = typeInfo.Type;

        // Walk base type chain looking for QuarryContext
        while (type != null)
        {
            if (type.BaseType != null && type.BaseType.Name == "QuarryContext")
                return type.Name;
            type = type.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Resolves the entity type name from an identifier by tracing to its variable declaration
    /// and inspecting the initializer expression. Handles patterns like:
    /// <c>var users = new[] { new User{} }; db.InsertMany(users)...</c>
    /// <c>var users = items.Select(i => new User{...}); db.InsertMany(users)...</c>
    /// </summary>
    private static string? ExtractTypeNameFromIdentifierInitializer(ExpressionSyntax expression)
    {
        if (expression is not IdentifierNameSyntax identifier)
            return null;

        // Find the variable declarator for this identifier in the enclosing scope
        var name = identifier.Identifier.ValueText;
        var enclosingBlock = identifier.Ancestors()
            .OfType<BlockSyntax>()
            .FirstOrDefault();

        if (enclosingBlock == null)
            return null;

        // Search for a local variable declaration with matching name
        foreach (var statement in enclosingBlock.Statements)
        {
            if (statement is not LocalDeclarationStatementSyntax localDecl)
                continue;

            foreach (var declarator in localDecl.Declaration.Variables)
            {
                if (declarator.Identifier.ValueText != name || declarator.Initializer?.Value == null)
                    continue;

                var initExpr = declarator.Initializer.Value;

                // Try direct new expression (new[] { new User{} }, new User[] { ... })
                var result = ExtractTypeNameFromNewExpression(initExpr);
                if (result != null)
                    return result;

                // Try LINQ Select: enumerable.Select(x => new User{...})
                result = ExtractTypeNameFromLinqSelect(initExpr);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the entity type name from a LINQ Select call's lambda body.
    /// Handles: <c>source.Select(x => new User { ... })</c>
    /// </summary>
    private static string? ExtractTypeNameFromLinqSelect(ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return null;

        // Check if it's a .Select(...) call
        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            methodName = memberAccess.Name.Identifier.ValueText;

        if (methodName != "Select" || invocation.ArgumentList.Arguments.Count == 0)
            return null;

        // Get the lambda argument
        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (arg is not LambdaExpressionSyntax lambda)
            return null;

        // Get the lambda body
        ExpressionSyntax? body = null;
        if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
            body = simpleLambda.ExpressionBody;
        else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda)
            body = parenLambda.ExpressionBody;

        if (body == null)
            return null;

        return ExtractTypeNameFromNewExpression(body);
    }

    /// <summary>
    /// Extracts the simple type name from an object creation or array creation expression.
    /// Returns only the unqualified name (e.g., "User" from "new Pg.User{...}") so it
    /// resolves correctly within the context's namespace in the generated interceptor code.
    /// </summary>
    private static string? ExtractTypeNameFromNewExpression(ExpressionSyntax expression)
    {
        string? fullName = null;

        // new TypeName { ... } or new TypeName(...)
        if (expression is ObjectCreationExpressionSyntax objCreation)
            fullName = objCreation.Type.ToString();

        // new[] { new TypeName{}, ... } — implicit array
        if (fullName == null
            && expression is ImplicitArrayCreationExpressionSyntax implicitArray
            && implicitArray.Initializer != null)
        {
            foreach (var elem in implicitArray.Initializer.Expressions)
            {
                if (elem is ObjectCreationExpressionSyntax elemObj)
                {
                    fullName = elemObj.Type.ToString();
                    break;
                }
            }
        }

        // new TypeName[] { ... } — explicit array
        if (fullName == null
            && expression is ArrayCreationExpressionSyntax arrayCreation
            && arrayCreation.Type?.ElementType != null)
        {
            fullName = arrayCreation.Type.ElementType.ToString();
        }

        if (fullName == null)
            return null;

        // Strip any alias or namespace prefix — return only the simple name.
        // "Pg.User" → "User", "Quarry.Tests.User" → "User", "User" → "User"
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
    }

    /// <summary>
    /// Walks the receiver invocation chain to find a method call with resolved type arguments.
    /// For example, in <c>ctx.Insert(new User{}).Values(...).ToDiagnostics()</c>, this walks from the
    /// <c>.ToDiagnostics()</c> receiver through <c>.Values()</c> to the <c>Insert()</c> call,
    /// whose TypeArguments[0] is the inferred concrete type (User).
    /// </summary>
    private static ITypeSymbol? ResolveTypeParameterFromReceiverChain(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax chainedInvocation)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(chainedInvocation, cancellationToken);
            if (symbolInfo.Symbol is IMethodSymbol chainedMethod)
            {
                // Check method-level type arguments (from generic methods like Insert<T>)
                if (chainedMethod.TypeArguments.Length > 0
                    && chainedMethod.TypeArguments[0].TypeKind != TypeKind.TypeParameter)
                {
                    return chainedMethod.TypeArguments[0];
                }

                // Check the return type's type arguments (for non-generic methods on constructed types)
                if (chainedMethod.ReturnType is INamedTypeSymbol returnType
                    && returnType.IsGenericType
                    && returnType.TypeArguments.Length > 0
                    && returnType.TypeArguments[0].TypeKind != TypeKind.TypeParameter)
                {
                    return returnType.TypeArguments[0];
                }

                // Fallback: infer from the first argument's type.
                // For Insert<T>(T entity), the semantic model may not substitute T in TypeArguments,
                // but the argument expression (e.g., new User{}) has a known concrete type.
                if (chainedMethod.TypeArguments.Length > 0
                    && chainedMethod.TypeArguments[0].TypeKind == TypeKind.TypeParameter
                    && chainedInvocation.ArgumentList.Arguments.Count > 0)
                {
                    var argExpr = chainedInvocation.ArgumentList.Arguments[0].Expression;
                    var argTypeInfo = semanticModel.GetTypeInfo(argExpr, cancellationToken);
                    if (argTypeInfo.Type is INamedTypeSymbol argType
                        && argType.TypeKind != TypeKind.TypeParameter
                        && argType.TypeKind != TypeKind.Error)
                    {
                        // For InsertMany(IEnumerable<T>), unwrap the element type
                        if (argType.IsGenericType && chainedMethod.Parameters.Length > 0)
                        {
                            var paramType = chainedMethod.Parameters[0].Type;
                            if (paramType is INamedTypeSymbol namedParamType
                                && namedParamType.IsGenericType
                                && namedParamType.TypeArguments.Length > 0
                                && namedParamType.TypeArguments[0].TypeKind == TypeKind.TypeParameter)
                            {
                                // Parameter is IEnumerable<T>, argument is SomeType<User> or User[]
                                // Get the element type from the argument's IEnumerable implementation
                                foreach (var iface in argType.AllInterfaces)
                                {
                                    if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                                        && iface.TypeArguments.Length > 0
                                        && iface.TypeArguments[0].TypeKind != TypeKind.TypeParameter)
                                    {
                                        return iface.TypeArguments[0];
                                    }
                                }
                            }
                        }

                        // For Insert<T>(T entity), the argument type IS the concrete type
                        return argType;
                    }

                    // Handle array arguments (e.g., new[] { new User{} })
                    if (argTypeInfo.Type is IArrayTypeSymbol arrayType
                        && arrayType.ElementType.TypeKind != TypeKind.TypeParameter
                        && arrayType.ElementType.TypeKind != TypeKind.Error)
                    {
                        return arrayType.ElementType;
                    }
                }
            }

            // Walk to the receiver of this invocation
            if (chainedInvocation.Expression is MemberAccessExpressionSyntax chainedMember)
            {
                current = chainedMember.Expression;
            }
            else
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the method name from an invocation expression.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>
    /// Gets the location (file path, line, column) for the method name in an invocation.
    /// </summary>
    internal static (string FilePath, int Line, int Column)? GetMethodLocation(InvocationExpressionSyntax invocation)
    {
        // Get the location of the method name, not the whole invocation
        SyntaxToken methodNameToken;

        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                methodNameToken = memberAccess.Name.Identifier;
                break;
            case IdentifierNameSyntax identifier:
                methodNameToken = identifier.Identifier;
                break;
            case GenericNameSyntax genericName:
                methodNameToken = genericName.Identifier;
                break;
            default:
                return null;
        }

        var location = methodNameToken.GetLocation();
        if (!location.IsInSource)
            return null;

        var lineSpan = location.GetLineSpan();
        var filePath = lineSpan.Path;
        // Roslyn uses 0-based line/column, InterceptsLocation uses 1-based
        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;

        return (filePath, line, column);
    }

    /// <summary>
    /// Checks if a type is a Quarry builder type.
    /// </summary>
    private static bool IsQuarryBuilderType(INamedTypeSymbol type)
    {
        if (!BuilderTypeNames.Contains(type.Name))
            return false;

        // Walk up to check namespace is Quarry
        var ns = type.ContainingNamespace;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            if (ns.Name == "Quarry")
                return true;
            ns = ns.ContainingNamespace;
        }

        return false;
    }

    /// <summary>
    /// Checks if the type name is an InsertBuilder.
    /// </summary>
    private static bool IsInsertBuilderType(string typeName)
        => typeName == "IInsertBuilder";

    /// <summary>
    /// Extracts the entity and result type arguments from a builder type.
    /// </summary>
    private static (string? EntityTypeName, string? ResultTypeName) ExtractTypeArguments(INamedTypeSymbol builderType)
    {
        if (!builderType.IsGenericType || builderType.TypeArguments.Length == 0)
            return (null, null);

        var entityType = builderType.TypeArguments[0];
        var entityTypeName = entityType.ToFullyQualifiedDisplayString();

        string? resultTypeName = null;
        var name = builderType.Name;

        // For joined builder types, only the last type arg is TResult when it exceeds the entity count
        if (name == "IJoinedQueryBuilder" && builderType.TypeArguments.Length > 2)
        {
            // IJoinedQueryBuilder<T1,T2,TResult> — TResult is index 2
            resultTypeName = builderType.TypeArguments[2].ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder3" && builderType.TypeArguments.Length > 3)
        {
            // IJoinedQueryBuilder3<T1,T2,T3,TResult> — TResult is index 3
            resultTypeName = builderType.TypeArguments[3].ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder4" && builderType.TypeArguments.Length > 4)
        {
            // IJoinedQueryBuilder4<T1,T2,T3,T4,TResult> — TResult is index 4
            resultTypeName = builderType.TypeArguments[4].ToFullyQualifiedDisplayString();
        }
        else if (!IsJoinedBuilderName(name) && builderType.TypeArguments.Length > 1)
        {
            // QueryBuilder<T, TResult> — TResult is index 1
            resultTypeName = builderType.TypeArguments[1].ToFullyQualifiedDisplayString();
        }

        return (entityTypeName, resultTypeName);
    }

    private static bool IsJoinedBuilderName(string name)
        => name is "IJoinedQueryBuilder" or "IJoinedQueryBuilder3" or "IJoinedQueryBuilder4";

    private static BuilderKind ClassifyBuilderKind(string typeName)
    {
        if (typeName.Contains("ExecutableDeleteBuilder")) return BuilderKind.ExecutableDelete;
        if (typeName.Contains("DeleteBuilder")) return BuilderKind.Delete;
        if (typeName.Contains("ExecutableUpdateBuilder")) return BuilderKind.ExecutableUpdate;
        if (typeName.Contains("UpdateBuilder")) return BuilderKind.Update;
        if (typeName.Contains("JoinedQueryBuilder")) return BuilderKind.JoinedQuery;
        if (typeName.Contains("EntityAccessor")) return BuilderKind.EntityAccessor;
        return BuilderKind.Query;
    }

    /// <summary>
    /// Extracts all entity type names from a joined builder type's type arguments.
    /// For JoinedQueryBuilder&lt;T1,T2&gt;, returns [T1, T2].
    /// For JoinedQueryBuilder3&lt;T1,T2,T3&gt;, returns [T1, T2, T3].
    /// For JoinedQueryBuilder3&lt;T1,T2,T3,TResult&gt;, returns [T1, T2, T3].
    /// Returns null for non-joined builder types.
    /// </summary>
    private static List<string>? ExtractJoinedEntityTypeNames(INamedTypeSymbol builderType)
    {
        var name = builderType.Name;
        if (!IsJoinedBuilderName(name))
            return null;

        // Determine how many entity type params based on builder name
        int entityCount;
        if (name == "IJoinedQueryBuilder")
        {
            // IJoinedQueryBuilder<T1,T2> has 2 entities
            // IJoinedQueryBuilder<T1,T2,TResult> has 2 entities + 1 result
            entityCount = 2;
        }
        else if (name == "IJoinedQueryBuilder3")
        {
            // IJoinedQueryBuilder3<T1,T2,T3> has 3 entities
            // IJoinedQueryBuilder3<T1,T2,T3,TResult> has 3 entities + 1 result
            entityCount = 3;
        }
        else if (name == "IJoinedQueryBuilder4")
        {
            // IJoinedQueryBuilder4<T1,T2,T3,T4> has 4 entities
            // IJoinedQueryBuilder4<T1,T2,T3,T4,TResult> has 4 entities + 1 result
            entityCount = 4;
        }
        else
        {
            return null;
        }

        if (builderType.TypeArguments.Length < entityCount)
            return null;

        var result = new List<string>(entityCount);
        for (int i = 0; i < entityCount; i++)
        {
            result.Add(builderType.TypeArguments[i].ToFullyQualifiedDisplayString());
        }
        return result;
    }

    /// <summary>
    /// Generates a unique identifier for a usage site.
    /// </summary>
    internal static string GenerateUniqueId(string filePath, int line, int column, string methodName)
    {
        // Create a deterministic ID based on location
        var input = $"{filePath}:{line}:{column}:{methodName}";
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        // Take first 8 bytes for a shorter ID
        return BitConverter.ToString(hashBytes, 0, 4).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Walks the receiver expression chain to find the owning QuarryContext subclass type.
    /// Returns the concrete context class name (e.g., "AppDb") or null if unresolvable.
    /// </summary>
    internal static string? ResolveContextFromCallSite(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Get the receiver expression from the invocation
        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }

        if (receiver == null)
            return null;

        // Recursively unwrap fluent chain: while receiver is an invocation, drill into its receiver
        while (receiver is InvocationExpressionSyntax chainedInvocation)
        {
            if (chainedInvocation.Expression is MemberAccessExpressionSyntax chainedMember)
            {
                receiver = chainedMember.Expression;
            }
            else
            {
                break;
            }
        }

        // If receiver is a local variable, trace through its initializer to find the context.
        // This handles patterns like: var del = _db.Delete<T>().Where(...); del.ExecuteNonQueryAsync()
        if (receiver is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is ILocalSymbol local && local.DeclaringSyntaxReferences.Length == 1)
            {
                var declSyntax = local.DeclaringSyntaxReferences[0].GetSyntax();
                if (declSyntax is VariableDeclaratorSyntax declarator && declarator.Initializer?.Value != null)
                {
                    // Create a synthetic invocation-like wrapper to reuse our chain-walking logic
                    var initExpr = declarator.Initializer.Value;
                    // Walk the initializer's fluent chain to find the root
                    while (initExpr is InvocationExpressionSyntax initInvoc)
                    {
                        if (initInvoc.Expression is MemberAccessExpressionSyntax initMember)
                            initExpr = initMember.Expression;
                        else
                            break;
                    }
                    receiver = initExpr;
                }
            }
        }

        // Now receiver should be the root (e.g., `db.Users` or `db` or `variable`)
        // For property access like db.Users, we want the type of db (the left side)
        if (receiver is MemberAccessExpressionSyntax propAccess)
        {
            receiver = propAccess.Expression;
        }

        // Get the type of the root receiver
        var typeInfo = semanticModel.GetTypeInfo(receiver, cancellationToken);
        var type = typeInfo.Type;

        // Walk base type chain looking for QuarryContext
        while (type != null)
        {
            if (type.BaseType != null && type.BaseType.Name == "QuarryContext")
            {
                return type.Name;
            }
            type = type.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Checks if a type is or inherits from QuarryContext.
    /// </summary>
    private static bool IsQuarryContextType(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "QuarryContext")
            {
                var ns = current.ContainingNamespace;
                while (ns != null && !ns.IsGlobalNamespace)
                {
                    if (ns.Name == "Quarry")
                        return true;
                    ns = ns.ContainingNamespace;
                }
            }
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Discovers a RawSql usage site (RawSqlAsync/RawSqlScalarAsync on QuarryContext).
    /// </summary>
    private static UsageSiteInfo? DiscoverRawSqlUsageSite(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol containingType,
        InterceptorKind kind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // RawSqlAsync<T> and RawSqlScalarAsync<T> are generic — resolve T
        if (!methodSymbol.IsGenericMethod || methodSymbol.TypeArguments.Length == 0)
            return null;

        var typeArgSymbol = methodSymbol.TypeArguments[0];

        // If T is an unresolved type parameter, we can't generate a typed interceptor
        if (typeArgSymbol.TypeKind == TypeKind.TypeParameter)
            return null;

        // Get location information
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        var (filePath, line, column) = location.Value;
        var methodName = methodSymbol.Name;
        var uniqueId = GenerateUniqueId(filePath, line, column, methodName);

        // Get InterceptableLocation data
        string? interceptableLocationData = null;
        int interceptableLocationVersion = 1;
#if QUARRY_GENERATOR
        try
        {
#pragma warning disable RSEXPERIMENTAL002
            var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
            if (interceptableLocation != null)
            {
                interceptableLocationData = interceptableLocation.Data;
                interceptableLocationVersion = interceptableLocation.Version;
            }
        }
        catch
        {
            // Fallback if GetInterceptableLocation fails
        }
#endif

        // Detect if this overload has CancellationToken
        var hasCancellationToken = methodSymbol.Parameters.Length >= 3
            && methodSymbol.Parameters[1].Type.Name == "CancellationToken";

        // Resolve the type T into RawSqlTypeInfo
        var rawSqlTypeInfo = ResolveRawSqlTypeInfo(typeArgSymbol, hasCancellationToken);

        // Resolve the context class name from the receiver expression
        var contextClassName = ResolveContextFromCallSite(invocation, semanticModel, cancellationToken);
        // For RawSql calls, the receiver IS the context — use the concrete type
        if (contextClassName == null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var receiverTypeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
            if (receiverTypeInfo.Type is INamedTypeSymbol receiverType && IsQuarryContextType(receiverType))
            {
                // Use the concrete subclass name, walking up to find the direct QuarryContext subclass
                var candidate = receiverType;
                while (candidate != null && candidate.BaseType?.Name != "QuarryContext")
                    candidate = candidate.BaseType;
                contextClassName = candidate?.Name ?? receiverType.Name;
            }
        }

        var resultTypeName = typeArgSymbol.ToFullyQualifiedDisplayString();

        return new UsageSiteInfo(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            builderTypeName: containingType.Name,
            entityTypeName: resultTypeName, // For RawSql, "entity type" is the result type T
            isAnalyzable: true,
            kind: kind,
            invocationSyntax: invocation,
            uniqueId: uniqueId,
            resultTypeName: resultTypeName,
            contextClassName: contextClassName,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion,
            rawSqlTypeInfo: rawSqlTypeInfo);
    }

    /// <summary>
    /// Resolves a type symbol into RawSqlTypeInfo for reader code generation.
    /// </summary>
    private static RawSqlTypeInfo ResolveRawSqlTypeInfo(ITypeSymbol typeSymbol, bool hasCancellationToken)
    {
        var typeName = typeSymbol.ToFullyQualifiedDisplayString();
        var shortName = typeSymbol.ToMinimallyQualifiedDisplayString();

        // Check if T is a scalar type
        if (IsScalarType(typeSymbol))
        {
            var scalarReaderMethod = GetReaderMethodForType(shortName);
            return new RawSqlTypeInfo(
                shortName,
                RawSqlTypeKind.Scalar,
                System.Array.Empty<RawSqlPropertyInfo>(),
                hasCancellationToken,
                scalarReaderMethod);
        }

        // T is a class/struct with properties — enumerate public settable properties
        var properties = new List<RawSqlPropertyInfo>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.SetMethod == null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.IsStatic || prop.IsIndexer)
                continue;

            var propType = prop.Type;
            var propTypeName = propType.ToMinimallyQualifiedDisplayString();
            var isNullable = propType.NullableAnnotation == NullableAnnotation.Annotated
                             || (propType is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" });
            var isEnum = propType.TypeKind == TypeKind.Enum
                         || (propType is INamedTypeSymbol nullable && nullable.Name == "Nullable"
                             && nullable.TypeArguments.Length > 0
                             && nullable.TypeArguments[0].TypeKind == TypeKind.Enum);

            // Check for EntityRef<TEntity, TKey> foreign key pattern
            bool isForeignKey = false;
            string? referencedEntityName = null;
            string effectiveClrType = propTypeName;

            if (propType is INamedTypeSymbol namedProp && namedProp.Name == "EntityRef"
                && namedProp.TypeArguments.Length == 2)
            {
                isForeignKey = true;
                referencedEntityName = namedProp.TypeArguments[0].ToMinimallyQualifiedDisplayString();
                effectiveClrType = namedProp.TypeArguments[1].ToMinimallyQualifiedDisplayString();
            }

            var readerMethod = GetReaderMethodForType(isEnum ? GetEnumUnderlyingType(propType) : effectiveClrType);

            properties.Add(new RawSqlPropertyInfo(
                propertyName: prop.Name,
                clrType: effectiveClrType,
                readerMethodName: readerMethod,
                isNullable: isNullable,
                isEnum: isEnum,
                fullClrType: propType.ToFullyQualifiedDisplayString(),
                isForeignKey: isForeignKey,
                referencedEntityName: referencedEntityName));
        }

        return new RawSqlTypeInfo(shortName, RawSqlTypeKind.Dto, properties, hasCancellationToken);
    }

    /// <summary>
    /// Checks if a type is a scalar type (primitives, string, DateTime, Guid, etc.).
    /// </summary>
    private static bool IsScalarType(ITypeSymbol type)
    {
        // Unwrap Nullable<T>
        if (type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable
            && nullable.TypeArguments.Length > 0)
        {
            type = nullable.TypeArguments[0];
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            SpecialType.System_String => true,
            SpecialType.System_Char => true,
            SpecialType.System_DateTime => true,
            _ => type.ToMinimallyQualifiedDisplayString() switch
            {
                "Guid" or "System.Guid" => true,
                "DateTimeOffset" or "System.DateTimeOffset" => true,
                "TimeSpan" or "System.TimeSpan" => true,
                "DateOnly" or "System.DateOnly" => true,
                "TimeOnly" or "System.TimeOnly" => true,
                _ => false
            }
        };
    }

    /// <summary>
    /// Gets the underlying integral type for an enum, or the type itself if not an enum.
    /// </summary>
    private static string GetEnumUnderlyingType(ITypeSymbol type)
    {
        // Unwrap Nullable<T>
        if (type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable
            && nullable.TypeArguments.Length > 0)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return enumType.EnumUnderlyingType?.ToMinimallyQualifiedDisplayString() ?? "int";
        }

        return type.ToMinimallyQualifiedDisplayString();
    }

    /// <summary>
    /// Gets the DbDataReader method name for a CLR type string.
    /// </summary>
    private static string GetReaderMethodForType(string clrType)
    {
        var baseType = clrType.TrimEnd('?');
        return baseType switch
        {
            "bool" or "Boolean" or "System.Boolean" => "GetBoolean",
            "byte" or "Byte" or "System.Byte" => "GetByte",
            "sbyte" or "SByte" or "System.SByte" => "GetByte",
            "short" or "Int16" or "System.Int16" => "GetInt16",
            "ushort" or "UInt16" or "System.UInt16" => "GetInt16",
            "int" or "Int32" or "System.Int32" => "GetInt32",
            "uint" or "UInt32" or "System.UInt32" => "GetInt32",
            "long" or "Int64" or "System.Int64" => "GetInt64",
            "ulong" or "UInt64" or "System.UInt64" => "GetInt64",
            "float" or "Single" or "System.Single" => "GetFloat",
            "double" or "Double" or "System.Double" => "GetDouble",
            "decimal" or "Decimal" or "System.Decimal" => "GetDecimal",
            "string" or "String" or "System.String" => "GetString",
            "char" or "Char" or "System.Char" => "GetChar",
            "Guid" or "System.Guid" => "GetGuid",
            "DateTime" or "System.DateTime" => "GetDateTime",
            "DateTimeOffset" or "System.DateTimeOffset" => "GetFieldValue<DateTimeOffset>",
            "TimeSpan" or "System.TimeSpan" => "GetFieldValue<TimeSpan>",
            "DateOnly" or "System.DateOnly" => "GetFieldValue<DateOnly>",
            "TimeOnly" or "System.TimeOnly" => "GetFieldValue<TimeOnly>",
            _ => "GetValue"
        };
    }
}
