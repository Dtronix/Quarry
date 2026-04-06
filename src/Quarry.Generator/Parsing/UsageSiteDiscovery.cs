using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.IR;
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
        "IJoinedQueryBuilder5",
        "IJoinedQueryBuilder6",
        "IUpdateBuilder",
        "IExecutableUpdateBuilder",
        "IDeleteBuilder",
        "IExecutableDeleteBuilder",
        "IInsertBuilder",
        "IBatchInsertBuilder",
        "IExecutableBatchInsert",
        "IEntityAccessor",
        "EntityAccessor",
        "PreparedQuery"
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
        ["CrossJoin"] = InterceptorKind.CrossJoin,
        ["FullOuterJoin"] = InterceptorKind.FullOuterJoin,
        ["ExecuteFetchAllAsync"] = InterceptorKind.ExecuteFetchAll,
        ["ExecuteFetchFirstAsync"] = InterceptorKind.ExecuteFetchFirst,
        ["ExecuteFetchFirstOrDefaultAsync"] = InterceptorKind.ExecuteFetchFirstOrDefault,
        ["ExecuteFetchSingleAsync"] = InterceptorKind.ExecuteFetchSingle,
        ["ExecuteFetchSingleOrDefaultAsync"] = InterceptorKind.ExecuteFetchSingleOrDefault,
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
        ["Insert"] = InterceptorKind.InsertTransition,
        ["InsertBatch"] = InterceptorKind.BatchInsertColumnSelector,
        ["Values"] = InterceptorKind.BatchInsertValues,
        ["Trace"] = InterceptorKind.Trace,
        ["Prepare"] = InterceptorKind.Prepare,
        ["Union"] = InterceptorKind.Union,
        ["UnionAll"] = InterceptorKind.UnionAll,
        ["Intersect"] = InterceptorKind.Intersect,
        ["IntersectAll"] = InterceptorKind.IntersectAll,
        ["Except"] = InterceptorKind.Except,
        ["ExceptAll"] = InterceptorKind.ExceptAll
    };

    // Methods on InsertBuilder that need special handling
    private static readonly HashSet<string> InsertBuilderMethods = new(StringComparer.Ordinal)
    {
        "ExecuteNonQueryAsync",
        "ExecuteScalarAsync",
        "ToDiagnostics"
    };

    // Methods on IBatchInsertBuilder that need special handling
    private static readonly HashSet<string> BatchInsertBuilderMethods = new(StringComparer.Ordinal)
    {
        "Values",
        "WithTimeout"
    };

    // Methods on IExecutableBatchInsert that need special handling
    private static readonly HashSet<string> ExecutableBatchInsertMethods = new(StringComparer.Ordinal)
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

        // CTE definition method — has arguments, so needs explicit check
        if (methodName == "With"
            && invocation.ArgumentList.Arguments.Count > 0
            && invocation.Expression is MemberAccessExpressionSyntax)
            return true;

        return false;
    }

    /// <summary>
    /// Discovers a raw call site from an invocation expression, returning a RawCallSite
    /// suitable for the incremental pipeline. This method is self-contained:
    /// - Resolves method symbols with CandidateSymbols fallback
    /// - Classifies InterceptorKind and extracts entity types
    /// - Parses clause expressions to SqlExpr via SqlExprParser.ParseWithPathTracking()
    /// - Extracts SetAction assignments directly (no ClauseTranslator)
    /// - Detects disqualifying patterns (loop, try/catch, lambda capture, conditional)
    /// - Computes a ChainId for chain grouping
    /// </summary>
    public static RawCallSite? DiscoverRawCallSite(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Symbol resolution ──────────────────────────────────────
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        IMethodSymbol methodSymbol;
        if (symbolInfo.Symbol is IMethodSymbol resolved)
        {
            methodSymbol = resolved;
        }
        else if (symbolInfo.CandidateSymbols.Length == 1
                 && symbolInfo.CandidateSymbols[0] is IMethodSymbol candidate)
        {
            methodSymbol = candidate;
        }
        else if (symbolInfo.CandidateSymbols.Length > 1)
        {
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
            // No candidates at all — try resolving against the concrete context type.
            // During source generation, CTE methods (With<TDto>) on the base QuarryContext
            // return QuarryContext, but the chain may continue with context-specific methods
            // like Users() that only exist on the derived class. Resolve via the chain root
            // variable's concrete type instead.
            var postCteResolved = TryResolveViaChainRootContext(invocation, semanticModel, cancellationToken);
            if (postCteResolved != null)
            {
                methodSymbol = postCteResolved;
            }
            else
            {
                // Fallback: syntactic-only discovery for execution methods
                return TryDiscoverExecutionSiteSyntactically(invocation, semanticModel, cancellationToken);
            }
        }

        // ── Step 2: Containing type check ──────────────────────────────────
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        var methodName = methodSymbol.Name;

        // ── Step 3: RawSql detection ───────────────────────────────────────
        if (RawSqlMethods.TryGetValue(methodName, out var rawSqlKind) && IsQuarryContextType(containingType))
        {
            return DiscoverRawSqlUsageSite(invocation, methodSymbol, containingType, rawSqlKind, semanticModel, cancellationToken);
        }

        // ── Step 3b: CTE method detection ─────────────────────────────────
        if (IsQuarryContextType(containingType) && methodName is "With" or "FromCte")
        {
            var result = DiscoverCteSite(invocation, methodSymbol, containingType, methodName, semanticModel, cancellationToken);
            return result;
        }

        // ── Step 4: Chain root detection ───────────────────────────────────
        if (IsQuarryContextType(containingType)
            && methodSymbol.Parameters.Length == 0
            && methodSymbol.ReturnType is INamedTypeSymbol returnType
            && returnType is { Arity: 1 }
            && returnType.Name is "IQueryBuilder" or "EntityAccessor" or "IEntityAccessor")
        {
            var rootEntityType = returnType.TypeArguments[0];
            var rootEntityTypeName = rootEntityType.ToFullyQualifiedDisplayString();

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

            var contextClassName = containingType.Name;
            var contextNamespace = containingType.ContainingNamespace?.IsGlobalNamespace == false
                ? containingType.ContainingNamespace.ToDisplayString()
                : null;

            var crChainId = ComputeChainId(invocation, semanticModel, cancellationToken);
            var crIsInsideLoop = DetectLoopAncestor(invocation);
            var (crIsCteInner, crCteArgStart) = DetectCteInnerChain(invocation, semanticModel, cancellationToken);
            if (crIsCteInner && crChainId != null)
                crChainId += $":cte-inner:{crCteArgStart}";

            return new RawCallSite(
                methodName: methodName,
                filePath: rootFilePath,
                line: rootLine,
                column: rootColumn,
                uniqueId: rootUniqueId,
                kind: InterceptorKind.ChainRoot,
                builderKind: BuilderKind.Query,
                entityTypeName: rootEntityTypeName,
                resultTypeName: null,
                isAnalyzable: true,
                nonAnalyzableReason: null,
                interceptableLocationData: rootLocationData,
                interceptableLocationVersion: rootLocationVersion,
                location: new DiagnosticLocation(rootFilePath, rootLine, rootColumn, invocation.Span),
                contextClassName: contextClassName,
                contextNamespace: contextNamespace,
                builderTypeName: "IQueryBuilder",
                chainId: crChainId,
                isInsideLoop: crIsInsideLoop,
                isCteInnerChain: crIsCteInner);
        }

        // ── Step 5: Builder type / extension method check ──────────────────
        if (!IsQuarryBuilderType(containingType))
        {
            if (methodSymbol.IsExtensionMethod
                && InterceptableMethods.ContainsKey(methodName)
                && methodSymbol.ReceiverType is INamedTypeSymbol receiverType
                && IsQuarryBuilderType(receiverType))
            {
                containingType = receiverType;
            }
            else
            {
                return null;
            }
        }

        // ── Step 6: InterceptorKind classification ─────────────────────────
        if (!InterceptableMethods.TryGetValue(methodName, out var kind))
        {
            if (methodName == "ToDiagnostics" && IsInsertBuilderType(containingType.Name))
                kind = InterceptorKind.InsertToDiagnostics;
            else if (IsBatchInsertBuilderType(containingType.Name) && BatchInsertBuilderMethods.Contains(methodName))
                kind = methodName == "Values" ? InterceptorKind.BatchInsertValues : InterceptorKind.WithTimeout;
            else if (IsExecutableBatchInsertType(containingType.Name) && ExecutableBatchInsertMethods.Contains(methodName))
                kind = methodName switch
                {
                    "ExecuteNonQueryAsync" => InterceptorKind.BatchInsertExecuteNonQuery,
                    "ExecuteScalarAsync" => InterceptorKind.BatchInsertExecuteScalar,
                    "ToDiagnostics" => InterceptorKind.BatchInsertToDiagnostics,
                    _ => InterceptorKind.Unknown
                };
            else
                return null;
        }

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

        // Remap generic InterceptableMethods kinds to batch insert kinds when on batch insert types
        if (IsExecutableBatchInsertType(containingType.Name) && ExecutableBatchInsertMethods.Contains(methodName))
        {
            kind = methodName switch
            {
                "ExecuteNonQueryAsync" => InterceptorKind.BatchInsertExecuteNonQuery,
                "ExecuteScalarAsync" => InterceptorKind.BatchInsertExecuteScalar,
                "ToDiagnostics" => InterceptorKind.BatchInsertToDiagnostics,
                _ => kind
            };
        }

        if (IsBatchInsertBuilderType(containingType.Name) && methodName == "Values")
        {
            kind = InterceptorKind.BatchInsertValues;
        }

        // ── Step 6b: PreparedQuery terminal detection ──────────────────────
        bool isPreparedTerminal = containingType.Name == "PreparedQuery";

        HashSet<string>? initializedPropertyNames = null;
        if (kind is InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics)
        {
            initializedPropertyNames = ExtractInitializedPropertyNames(invocation);
        }
        // For .Prepare() on insert builders, extract InitializedPropertyNames from the chain
        // so InsertInfo can be built during chain analysis
        if (kind == InterceptorKind.Prepare && IsInsertBuilderType(containingType.Name))
        {
            initializedPropertyNames = ExtractInitializedPropertyNames(invocation);
        }

        if (methodName == "Where" && containingType.Name.Contains("DeleteBuilder"))
        {
            kind = InterceptorKind.DeleteWhere;
        }

        if (methodName == "Set" && containingType.Name.Contains("UpdateBuilder"))
        {
            if (invocation.ArgumentList.Arguments.Count == 1 && !methodSymbol.IsGenericMethod)
            {
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
        if (methodName == "Where" && containingType.Name.Contains("UpdateBuilder"))
        {
            kind = InterceptorKind.UpdateWhere;
        }

        // ── Step 7: Location + interceptable location ──────────────────────
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        var (filePath, line, column) = location.Value;

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
        catch { }
#endif

        // ── Step 8: Entity type extraction ─────────────────────────────────
        // For PreparedQuery terminals, trace through the variable to find the originating builder's
        // entity type, since PreparedQuery<TResult> only carries TResult, not the entity type.
        if (isPreparedTerminal && invocation.Expression is MemberAccessExpressionSyntax preparedMa)
        {
            var originatingBuilderType = ResolvePreparedQueryOriginBuilderType(
                preparedMa.Expression, semanticModel, cancellationToken);
            if (originatingBuilderType != null)
                containingType = originatingBuilderType;
        }

        // ── Step 8b: Remap kind for prepared insert/batch-insert terminals ─
        // After resolving the originating builder type, remap generic kinds to
        // insert-specific kinds so FileEmitter dispatches correctly.
        if (isPreparedTerminal)
        {
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
            else if (IsExecutableBatchInsertType(containingType.Name) && ExecutableBatchInsertMethods.Contains(methodName))
            {
                kind = methodName switch
                {
                    "ExecuteNonQueryAsync" => InterceptorKind.BatchInsertExecuteNonQuery,
                    "ExecuteScalarAsync" => InterceptorKind.BatchInsertExecuteScalar,
                    "ToDiagnostics" => InterceptorKind.BatchInsertToDiagnostics,
                    _ => kind
                };
            }
        }

        var (entityTypeName, resultTypeName, isValueTypeResult) = ExtractTypeArguments(containingType);
        if (entityTypeName == null)
            return null;

        var resolvedSyntactically = false;
        if (containingType.TypeArguments.Length > 0 && containingType.TypeArguments[0].TypeKind == TypeKind.TypeParameter
            && invocation.Expression is MemberAccessExpressionSyntax memberAccessForType)
        {
            var concreteType = ResolveTypeParameterFromReceiverChain(
                memberAccessForType.Expression, semanticModel, cancellationToken);
            if (concreteType != null)
            {
                containingType = containingType.OriginalDefinition.Construct(concreteType);
                (entityTypeName, resultTypeName, isValueTypeResult) = ExtractTypeArguments(containingType);
                if (entityTypeName == null)
                    return null;
            }
            else
            {
                var syntacticTypeName = ExtractEntityTypeNameFromChain(memberAccessForType.Expression);
                if (syntacticTypeName != null)
                {
                    entityTypeName = syntacticTypeName;
                    resolvedSyntactically = true;
                }
            }
        }

        if (!resolvedSyntactically
            && containingType.TypeArguments.Length > 0
            && containingType.TypeArguments[0].TypeKind == TypeKind.TypeParameter)
            return null;

        // ── Step 9: Analyzability check ────────────────────────────────────
        var (isAnalyzable, reason) = AnalyzabilityChecker.CheckAnalyzability(invocation, semanticModel, cancellationToken);

        var uniqueId = GenerateUniqueId(filePath, line, column, methodName);

        // ── Step 10: Projection analysis ───────────────────────────────────
        ProjectionInfo? projectionInfo = null;
        if (kind == InterceptorKind.Select && isAnalyzable)
        {
            var isOnJoinedBuilderForSelect = containingType.Name.Contains("JoinedQueryBuilder");
            try
            {
                if (isOnJoinedBuilderForSelect)
                {
                    var entityCount = containingType.TypeArguments.Length;
                    projectionInfo = Projection.ProjectionAnalyzer.AnalyzeJoinedSyntaxOnly(
                        invocation,
                        entityCount,
                        DefaultDiscoveryDialect);
                }
                else
                {
                    var entityType = containingType.TypeArguments[0];
                    projectionInfo = ProjectionAnalyzer.AnalyzeFromTypeSymbol(
                        invocation,
                        semanticModel,
                        entityType,
                        DefaultDiscoveryDialect);
                }
            }
            catch
            {
                isAnalyzable = false;
                reason = "Failed to analyze Select() projection";
            }
        }

        // ── Step 11: SqlExpr parsing for clause methods ────────────────────
        SqlExpr? expression = null;
        ClauseKind? clauseKind = null;
        bool isDescending = false;

        var isOnJoinedBuilder = containingType.Name.Contains("JoinedQueryBuilder");
        var clauseAnalyzable = isAnalyzable || AnalyzabilityChecker.IsClauseAnalyzable(invocation, semanticModel);
        // UpdateSetAction uses Action<T> lambdas (block bodies) that can't be parsed to SqlExpr.
        // It's handled separately in Step 12 via ExtractSetActionAssignments.
        if (clauseAnalyzable && IsClauseMethod(kind) && kind != InterceptorKind.UpdateSetAction)
        {
            var parsed = TryParseLambdaToSqlExpr(kind, invocation, semanticModel);
            if (parsed != null)
            {
                expression = parsed.Value.Expression;
                clauseKind = parsed.Value.ClauseKind;
                isDescending = parsed.Value.IsDescending;
            }
            else if (!isOnJoinedBuilder)
            {
                // Only mark non-analyzable for non-joined builders.
                // Joined builder clause methods may have multi-param lambdas that
                // TryParseLambdaToSqlExpr handles; if it fails, keep analyzable
                // so the chain still gets an interceptor.
                isAnalyzable = false;
                reason = $"Failed to analyze {methodName}() clause expression";
            }
        }

        // ── Step 12: SetAction handling ────────────────────────────────────
        IReadOnlyList<Models.SetActionAssignment>? setActionAssignments = null;
        IReadOnlyList<Translation.ParameterInfo>? setActionParameters = null;
        IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? setActionAllCapturedIdentifiers = null;

        if (kind == InterceptorKind.UpdateSetAction)
        {
            var setResult = ExtractSetActionAssignments(invocation, semanticModel);
            if (setResult != null)
            {
                setActionAssignments = setResult.Value.Assignments;
                setActionParameters = setResult.Value.Parameters;
                setActionAllCapturedIdentifiers = setResult.Value.AllCapturedIdentifiers;
            }
        }

        // ── Step 13: Join entity extraction ────────────────────────────────
        string? joinedEntityTypeName = null;
        bool isNavigationJoin = false;
        if (kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
            or InterceptorKind.CrossJoin or InterceptorKind.FullOuterJoin
            && methodSymbol.TypeArguments.Length > 0)
        {
            var joinedType = methodSymbol.TypeArguments[0];
            joinedEntityTypeName = joinedType.ToFullyQualifiedDisplayString();

            isNavigationJoin = IsNavigationJoinLambda(invocation, semanticModel);

            // Parse join lambda to SqlExpr if not already parsed above
            if (expression == null)
            {
                var parsed = TryParseLambdaToSqlExpr(kind, invocation, semanticModel);
                if (parsed != null)
                {
                    expression = parsed.Value.Expression;
                    clauseKind = parsed.Value.ClauseKind;
                    isDescending = parsed.Value.IsDescending;
                }
            }
        }

        var joinedEntityTypeNames = ExtractJoinedEntityTypeNames(containingType);

        // ── Step 14: Key type / constant value extraction ──────────────────
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

        int? constantIntValue = null;
        if (kind is InterceptorKind.Limit or InterceptorKind.Offset
            && invocation.ArgumentList.Arguments.Count > 0)
        {
            var argExpr = invocation.ArgumentList.Arguments[0].Expression;
            var constValue = semanticModel.GetConstantValue(argExpr);
            if (constValue.HasValue && constValue.Value is int intVal)
                constantIntValue = intVal;
        }

        // ── Step 15: Disqualifiers + ChainId ───────────────────────────────
        var isInsideLoop = DetectLoopAncestor(invocation);
        var isInsideTryCatch = DetectTryCatchAncestor(invocation);
        var isCapturedInLambda = DetectLambdaCaptureAncestor(invocation);
        var nestingContext = DetectNestingContext(invocation);
        var chainId = ComputeChainId(invocation, semanticModel, cancellationToken);
        var (isCteInnerChain, cteArgSpanStart) = DetectCteInnerChain(invocation, semanticModel, cancellationToken);
        if (isCteInnerChain && chainId != null)
            chainId += $":cte-inner:{cteArgSpanStart}";

        var (isPassedAsArgument, isAssignedFromNonQuarryMethod) =
            DetectVariableDisqualifiers(invocation, semanticModel);

        // Convert InitializedPropertyNames from HashSet to sorted ImmutableArray
        ImmutableArray<string>? sortedPropertyNames = null;
        if (initializedPropertyNames != null && initializedPropertyNames.Count > 0)
        {
            var sorted = new List<string>(initializedPropertyNames);
            sorted.Sort(StringComparer.Ordinal);
            sortedPropertyNames = ImmutableArray.CreateRange(sorted);
        }

        // For join sites, extract ordered lambda parameter names for multi-entity resolution
        ImmutableArray<string>? lambdaParamNames = null;
        if (expression != null && kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin
            or InterceptorKind.FullOuterJoin)
        {
            if (invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax joinLambda)
            {
                var ordered = IR.SqlExprParser.GetLambdaParameterNamesOrdered(joinLambda);
                if (ordered.Count >= 2)
                    lambdaParamNames = ImmutableArray.CreateRange(ordered);
            }
        }

        // Extract batch insert column names from column selector lambda
        ImmutableArray<string>? batchInsertColumnNames = null;
        if (kind == InterceptorKind.BatchInsertColumnSelector)
        {
            batchInsertColumnNames = ExtractBatchInsertColumnNames(invocation);
        }
        // For batch insert terminals, walk the chain to find the column selector and extract column names
        if (kind is InterceptorKind.BatchInsertExecuteNonQuery
            or InterceptorKind.BatchInsertExecuteScalar
            or InterceptorKind.BatchInsertToDiagnostics)
        {
            batchInsertColumnNames = ExtractBatchInsertColumnNamesFromChain(invocation, semanticModel, cancellationToken);
        }
        // For .Prepare() on batch insert builders, also walk the chain for column names
        if (kind == InterceptorKind.Prepare && IsExecutableBatchInsertType(containingType.Name))
        {
            batchInsertColumnNames = ExtractBatchInsertColumnNamesFromChain(invocation, semanticModel, cancellationToken);
        }

        // ── Step 15b: Set operation operand chain linking ────────────────────
        string? operandChainId = null;
        int? operandArgEndLine = null;
        int? operandArgEndColumn = null;
        if (kind is InterceptorKind.Union or InterceptorKind.UnionAll
            or InterceptorKind.Intersect or InterceptorKind.IntersectAll
            or InterceptorKind.Except or InterceptorKind.ExceptAll)
        {
            operandChainId = ExtractSetOperationOperandChainId(invocation, semanticModel, cancellationToken);
            // Record the end position of the operand argument expression
            // so ChainAnalyzer can bound inline operand splitting.
            if (invocation.ArgumentList.Arguments.Count >= 1)
            {
                var argSpan = invocation.ArgumentList.Arguments[0].Expression.GetLocation().GetLineSpan();
                operandArgEndLine = argSpan.EndLinePosition.Line + 1; // 0-based → 1-based
                operandArgEndColumn = argSpan.EndLinePosition.Character + 1;
            }
        }

        // ── Step 15c: PreparedQuery escape detection ────────────────────────
        string? preparedQueryEscapeReason = null;
        if (kind == InterceptorKind.Prepare)
        {
            preparedQueryEscapeReason = DetectPreparedQueryEscape(invocation);
        }

        // ── Step 16: Build RawCallSite directly ────────────────────────────
        return new RawCallSite(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            uniqueId: uniqueId,
            kind: kind,
            builderKind: ClassifyBuilderKind(containingType.Name),
            entityTypeName: entityTypeName,
            resultTypeName: resultTypeName,
            isAnalyzable: isAnalyzable,
            nonAnalyzableReason: reason,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion,
            location: new DiagnosticLocation(filePath, line, column, invocation.Span),
            expression: expression,
            clauseKind: clauseKind,
            isDescending: isDescending,
            projectionInfo: projectionInfo,
            joinedEntityTypeName: joinedEntityTypeName,
            initializedPropertyNames: sortedPropertyNames,
            constantIntValue: constantIntValue,
            isNavigationJoin: isNavigationJoin,
            contextClassName: ResolveContextFromCallSite(invocation, semanticModel, cancellationToken),
            isInsideLoop: isInsideLoop,
            isInsideTryCatch: isInsideTryCatch,
            isCapturedInLambda: isCapturedInLambda,
            isPassedAsArgument: isPassedAsArgument,
            isAssignedFromNonQuarryMethod: isAssignedFromNonQuarryMethod,
            nestingContext: nestingContext,
            chainId: chainId,
            builderTypeName: containingType.Name,
            joinedEntityTypeNames: joinedEntityTypeNames,
            setActionAssignments: setActionAssignments,
            setActionParameters: setActionParameters,
            setActionAllCapturedIdentifiers: setActionAllCapturedIdentifiers,
            lambdaParameterNames: lambdaParamNames,
            batchInsertColumnNames: batchInsertColumnNames,
            isPreparedTerminal: isPreparedTerminal,
            preparedQueryEscapeReason: preparedQueryEscapeReason,
            isValueTypeResult: isValueTypeResult,
            operandChainId: operandChainId,
            operandArgEndLine: operandArgEndLine,
            operandArgEndColumn: operandArgEndColumn,
            isCteInnerChain: isCteInnerChain);
    }

    /// <summary>
    /// Discovers raw call sites from an invocation expression, returning one or more RawCallSites.
    /// For navigation join sites, this also forward-scans the fluent chain to discover post-join
    /// method calls (Select, Trace, ToDiagnostics, etc.) that Roslyn cannot resolve because the
    /// joined entity type is generated and type inference for the navigation lambda fails.
    /// </summary>
    public static ImmutableArray<RawCallSite> DiscoverRawCallSites(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var site = DiscoverRawCallSite(invocation, semanticModel, cancellationToken);
        if (site == null)
            return ImmutableArray<RawCallSite>.Empty;

        EnrichDisplayClassInfo(site, invocation);

        // For navigation joins, forward-scan the chain to discover post-join sites
        // that Roslyn can't resolve due to generated entity type inference failure.
        if (site.IsNavigationJoin
            && site.Kind is InterceptorKind.Join or InterceptorKind.LeftJoin or InterceptorKind.RightJoin)
        {
            var postJoinSites = DiscoverPostJoinSites(
                invocation, site, semanticModel, cancellationToken);
            if (postJoinSites.Length > 0)
            {
                var builder = ImmutableArray.CreateBuilder<RawCallSite>(1 + postJoinSites.Length);
                builder.Add(site);
                builder.AddRange(postJoinSites);
                return builder.MoveToImmutable();
            }
        }

        // For CTE definitions, forward-scan the chain to discover post-With methods
        // that Roslyn can't resolve because With() returns QuarryContext (base class)
        // and context-specific methods like Users() aren't on the base class.
        if (site.Kind == InterceptorKind.CteDefinition)
        {
            var postCteSites = DiscoverPostCteSites(
                invocation, site, semanticModel, cancellationToken);
            if (postCteSites.Length > 0)
            {
                var builder = ImmutableArray.CreateBuilder<RawCallSite>(1 + postCteSites.Length);
                builder.Add(site);
                builder.AddRange(postCteSites);
                return builder.MoveToImmutable();
            }
        }

        return ImmutableArray.Create(site);
    }

    /// <summary>
    /// Walks forward from a navigation join invocation through the fluent chain,
    /// creating RawCallSites for each post-join method call (Select, OrderBy, Trace,
    /// ToDiagnostics, ExecuteFetchAllAsync, etc.) that the normal discovery missed.
    /// </summary>
    private static ImmutableArray<RawCallSite> DiscoverPostJoinSites(
        InvocationExpressionSyntax joinInvocation,
        RawCallSite joinSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<RawCallSite>();
        var entityTypeName = joinSite.EntityTypeName;

        // Resolve the joined entity type name. The Join site may have "TJoined" (unresolved type
        // parameter) because Roslyn couldn't infer it. Try to resolve from the navigation lambda's
        // semantic type info (e.g., NavigationList<Order> → Order).
        string? joinedEntityTypeName = joinSite.JoinedEntityTypeName;
        if (joinedEntityTypeName == null || joinedEntityTypeName == "TJoined")
        {
            if (joinInvocation.ArgumentList.Arguments.Count > 0
                && joinInvocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax joinLambda)
            {
                var body = IR.SqlExprParser.GetLambdaBody(joinLambda);
                if (body != null)
                {
                    var bodyType = semanticModel.GetTypeInfo(body, cancellationToken).Type;
                    if (bodyType is INamedTypeSymbol navType
                        && navType.Name == "NavigationList"
                        && navType.TypeArguments.Length == 1
                        && navType.TypeArguments[0].TypeKind != TypeKind.TypeParameter)
                    {
                        joinedEntityTypeName = navType.TypeArguments[0].ToDisplayString();
                    }
                }
            }
        }

        // Build the joined entity type names list for synthetic post-join sites
        List<string>? joinedEntityTypeNames = null;
        if (joinedEntityTypeName != null && joinedEntityTypeName != "TJoined")
        {
            joinedEntityTypeNames = new List<string> { entityTypeName, joinedEntityTypeName };
        }

        // Walk UP the syntax tree to find invocations that use this join's result as receiver.
        // Pattern: invocation.Parent is MemberAccessExpressionSyntax.Parent is InvocationExpressionSyntax
        var currentInvoc = joinInvocation;
        while (currentInvoc.Parent is MemberAccessExpressionSyntax parentMa
               && parentMa.Parent is InvocationExpressionSyntax parentInvoc
               && parentMa.Expression == currentInvoc)
        {
            var methodName = parentMa.Name.Identifier.ValueText;
            if (!InterceptableMethods.TryGetValue(methodName, out var kind))
                break;

            // Check if normal discovery can resolve this invocation (lightweight symbol check)
            var parentSymbolInfo = semanticModel.GetSymbolInfo(parentInvoc, cancellationToken);
            var parentResolved = parentSymbolInfo.Symbol is IMethodSymbol pms && IsQuarryBuilderType(pms.ContainingType);
            if (!parentResolved && parentSymbolInfo.CandidateSymbols.Length == 1
                && parentSymbolInfo.CandidateSymbols[0] is IMethodSymbol pc)
                parentResolved = IsQuarryBuilderType(pc.ContainingType);
            if (parentResolved)
            {
                // Normal discovery would succeed — no need for synthetic site.
                // Continue walking in case later sites in the chain fail.
                currentInvoc = parentInvoc;
                continue;
            }

            // Get location info
            var location = GetMethodLocation(parentInvoc);
            if (location == null)
                break;

            var (filePath, line, column) = location.Value;

            // Get interceptable location
            string? interceptableLocationData = null;
            int interceptableLocationVersion = 1;
#if QUARRY_GENERATOR
            try
            {
#pragma warning disable RSEXPERIMENTAL002
                var interceptableLocation = semanticModel.GetInterceptableLocation(parentInvoc, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
                if (interceptableLocation != null)
                {
                    interceptableLocationData = interceptableLocation.Data;
                    interceptableLocationVersion = interceptableLocation.Version;
                }
            }
            catch { }
#endif
            if (interceptableLocationData == null)
                break;

            var uniqueId = GenerateUniqueId(filePath, line, column, methodName);
            var chainId = ComputeChainId(parentInvoc, semanticModel, cancellationToken);

            // Detect disqualifying patterns
            var isInsideLoop = DetectLoopAncestor(parentInvoc);
            var isInsideTryCatch = DetectTryCatchAncestor(parentInvoc);
            var isCapturedInLambda = DetectLambdaCaptureAncestor(parentInvoc);
            var nestingContext = DetectNestingContext(parentInvoc);

            // For Select(), analyze the projection syntactically
            ProjectionInfo? projectionInfo = null;
            if (kind == InterceptorKind.Select)
            {
                try
                {
                    // Entity count = 2 for a 2-entity nav join (primary + joined)
                    projectionInfo = Projection.ProjectionAnalyzer.AnalyzeJoinedSyntaxOnly(
                        parentInvoc,
                        entityCount: 2,
                        DefaultDiscoveryDialect);
                }
                catch { }
            }

            // Parse clause lambda to SqlExpr if applicable
            SqlExpr? expression = null;
            ClauseKind? clauseKind = null;
            bool isDescending = false;
            if (IsClauseMethod(kind))
            {
                var parsed = TryParseLambdaToSqlExpr(kind, parentInvoc, semanticModel);
                if (parsed != null)
                {
                    expression = parsed.Value.Expression;
                    clauseKind = parsed.Value.ClauseKind;
                    isDescending = parsed.Value.IsDescending;
                }
            }

            var postJoinSite = new RawCallSite(
                methodName: methodName,
                filePath: filePath,
                line: line,
                column: column,
                uniqueId: uniqueId,
                kind: kind,
                builderKind: BuilderKind.JoinedQuery,
                entityTypeName: entityTypeName,
                resultTypeName: null,
                isAnalyzable: true,
                nonAnalyzableReason: null,
                interceptableLocationData: interceptableLocationData,
                interceptableLocationVersion: interceptableLocationVersion,
                location: new DiagnosticLocation(filePath, line, column, parentInvoc.Span),
                expression: expression,
                clauseKind: clauseKind,
                isDescending: isDescending,
                projectionInfo: projectionInfo,
                contextClassName: joinSite.ContextClassName,
                contextNamespace: joinSite.ContextNamespace,
                isInsideLoop: isInsideLoop,
                isInsideTryCatch: isInsideTryCatch,
                isCapturedInLambda: isCapturedInLambda,
                nestingContext: nestingContext,
                chainId: chainId,
                builderTypeName: "IJoinedQueryBuilder",
                joinedEntityTypeNames: joinedEntityTypeNames);

            EnrichDisplayClassInfo(postJoinSite, parentInvoc);
            results.Add(postJoinSite);

            currentInvoc = parentInvoc;
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// Walks forward from a CTE With() invocation through the fluent chain,
    /// creating RawCallSites for methods that the normal discovery missed because
    /// With() returns QuarryContext (base class) and subsequent context-specific
    /// methods (like Users()) can't be resolved on the base class.
    /// </summary>
    private static ImmutableArray<RawCallSite> DiscoverPostCteSites(
        InvocationExpressionSyntax cteInvocation,
        RawCallSite cteSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<RawCallSite>();

        // Walk UP the syntax tree to find invocations that use the CTE result as receiver.
        var currentInvoc = cteInvocation;
        while (currentInvoc.Parent is MemberAccessExpressionSyntax parentMa
               && parentMa.Parent is InvocationExpressionSyntax parentInvoc
               && parentMa.Expression == currentInvoc)
        {
            var methodName = parentMa.Name.Identifier.ValueText;

            // Check if normal discovery can resolve this invocation
            var parentSymbolInfo = semanticModel.GetSymbolInfo(parentInvoc, cancellationToken);
            if (parentSymbolInfo.Symbol is IMethodSymbol || parentSymbolInfo.CandidateSymbols.Length > 0)
            {
                // Normal discovery should handle it — continue walking
                currentInvoc = parentInvoc;
                continue;
            }

            // Determine the site kind
            InterceptorKind kind;
            BuilderKind builderKind = BuilderKind.Query;
            string? entityTypeName = null;

            if (InterceptableMethods.TryGetValue(methodName, out kind))
            {
                // Known builder method (Join, Select, Where, Prepare, etc.)
            }
            else if (parentInvoc.ArgumentList.Arguments.Count == 0
                     && methodName.Length > 0 && char.IsUpper(methodName[0]))
            {
                // Likely a chain root (Users(), Orders(), etc.)
                // Resolve against the concrete context class
                var resolved = TryResolveViaChainRootContext(parentInvoc, semanticModel, cancellationToken);
                if (resolved == null)
                    break;

                kind = InterceptorKind.ChainRoot;
                var returnType = resolved.ReturnType as INamedTypeSymbol;
                if (returnType is { Arity: 1 })
                    entityTypeName = returnType.TypeArguments[0].ToFullyQualifiedDisplayString();
            }
            else
            {
                break;
            }

            // Get location info
            var location = GetMethodLocation(parentInvoc);
            if (location == null)
                break;

            var (filePath, line, column) = location.Value;

            // Get interceptable location
            string? interceptableLocationData = null;
            int interceptableLocationVersion = 1;
#if QUARRY_GENERATOR
            try
            {
#pragma warning disable RSEXPERIMENTAL002
                var interceptableLocation = semanticModel.GetInterceptableLocation(parentInvoc, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
                if (interceptableLocation != null)
                {
                    interceptableLocationData = interceptableLocation.Data;
                    interceptableLocationVersion = interceptableLocation.Version;
                }
            }
            catch { }
#endif
            if (interceptableLocationData == null)
                break;

            var uniqueId = GenerateUniqueId(filePath, line, column, methodName);
            var chainId = ComputeChainId(parentInvoc, semanticModel, cancellationToken);
            var isInsideLoop = DetectLoopAncestor(parentInvoc);

            if (entityTypeName == null)
                entityTypeName = cteSite.EntityTypeName;

            // Parse clause lambda if applicable
            SqlExpr? expression = null;
            ClauseKind? clauseKind = null;
            bool isDescending = false;
            if (IsClauseMethod(kind))
            {
                var parsed = TryParseLambdaToSqlExpr(kind, parentInvoc, semanticModel);
                if (parsed != null)
                {
                    expression = parsed.Value.Expression;
                    clauseKind = parsed.Value.ClauseKind;
                    isDescending = parsed.Value.IsDescending;
                }
            }

            // Analyze projection for Select()
            ProjectionInfo? projectionInfo = null;
            if (kind == InterceptorKind.Select)
            {
                try
                {
                    var lambdaParamCount = GetLambdaParameterCount(parentInvoc);
                    projectionInfo = Projection.ProjectionAnalyzer.AnalyzeJoinedSyntaxOnly(
                        parentInvoc,
                        entityCount: lambdaParamCount > 0 ? lambdaParamCount : 1,
                        DefaultDiscoveryDialect);
                }
                catch { }
            }

            var postCteSite = new RawCallSite(
                methodName: methodName,
                filePath: filePath,
                line: line,
                column: column,
                uniqueId: uniqueId,
                kind: kind,
                builderKind: builderKind,
                entityTypeName: entityTypeName,
                resultTypeName: null,
                isAnalyzable: true,
                nonAnalyzableReason: null,
                interceptableLocationData: interceptableLocationData,
                interceptableLocationVersion: interceptableLocationVersion,
                location: new DiagnosticLocation(filePath, line, column, parentInvoc.Span),
                expression: expression,
                clauseKind: clauseKind,
                isDescending: isDescending,
                projectionInfo: projectionInfo,
                contextClassName: cteSite.ContextClassName,
                contextNamespace: cteSite.ContextNamespace,
                isInsideLoop: isInsideLoop,
                chainId: chainId);

            EnrichDisplayClassInfo(postCteSite, parentInvoc);
            results.Add(postCteSite);

            // When we hit Prepare(), also discover prepared terminal usages on the
            // assigned variable (e.g., lt.ToDiagnostics(), lt.ExecuteFetchAllAsync()).
            if (kind == InterceptorKind.Prepare)
            {
                var preparedTerminals = DiscoverPreparedTerminalsForCteChain(
                    parentInvoc, cteSite, chainId, semanticModel, cancellationToken);
                results.AddRange(preparedTerminals);
            }

            currentInvoc = parentInvoc;
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// Discovers prepared terminal method calls (ToDiagnostics, ExecuteFetchAllAsync, etc.)
    /// on the variable assigned from a CTE chain's .Prepare() call.
    /// </summary>
    private static ImmutableArray<RawCallSite> DiscoverPreparedTerminalsForCteChain(
        InvocationExpressionSyntax prepareInvocation,
        RawCallSite cteSite,
        string? chainId,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<RawCallSite>();

        // Find the variable name the chain is assigned to
        var varName = GetAssignedVariableName(prepareInvocation);
        if (varName == null)
            return results.ToImmutable();

        // Find the containing method to scan for variable usages
        SyntaxNode? scope = null;
        foreach (var ancestor in prepareInvocation.Ancestors())
        {
            if (ancestor is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                scope = ancestor;
                break;
            }
        }
        if (scope == null)
            return results.ToImmutable();

        // Scan for invocations on the variable (e.g., lt.ToDiagnostics(), lt.ExecuteFetchAllAsync())
        foreach (var descendant in scope.DescendantNodes())
        {
            if (descendant is InvocationExpressionSyntax usage
                && usage != prepareInvocation
                && usage.Expression is MemberAccessExpressionSyntax usageMa
                && usageMa.Expression is IdentifierNameSyntax usageIdent
                && usageIdent.Identifier.ValueText == varName)
            {
                var usageMethodName = usageMa.Name.Identifier.ValueText;
                if (!InterceptableMethods.TryGetValue(usageMethodName, out var usageKind))
                    continue;

                var location = GetMethodLocation(usage);
                if (location == null)
                    continue;

                var (filePath, line, column) = location.Value;

                string? interceptableLocationData = null;
                int interceptableLocationVersion = 1;
#if QUARRY_GENERATOR
                try
                {
#pragma warning disable RSEXPERIMENTAL002
                    var interceptableLocation = semanticModel.GetInterceptableLocation(usage, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002
                    if (interceptableLocation != null)
                    {
                        interceptableLocationData = interceptableLocation.Data;
                        interceptableLocationVersion = interceptableLocation.Version;
                    }
                }
                catch { }
#endif
                if (interceptableLocationData == null)
                    continue;

                var uniqueId = GenerateUniqueId(filePath, line, column, usageMethodName);
                var usageChainId = chainId; // Same chain as the Prepare site

                var terminalSite = new RawCallSite(
                    methodName: usageMethodName,
                    filePath: filePath,
                    line: line,
                    column: column,
                    uniqueId: uniqueId,
                    kind: usageKind,
                    builderKind: BuilderKind.Query,
                    entityTypeName: cteSite.EntityTypeName,
                    resultTypeName: null,
                    isAnalyzable: true,
                    nonAnalyzableReason: null,
                    interceptableLocationData: interceptableLocationData,
                    interceptableLocationVersion: interceptableLocationVersion,
                    location: new DiagnosticLocation(filePath, line, column, usage.Span),
                    contextClassName: cteSite.ContextClassName,
                    contextNamespace: cteSite.ContextNamespace,
                    chainId: usageChainId,
                    isPreparedTerminal: true);

                EnrichDisplayClassInfo(terminalSite, usage);
                results.Add(terminalSite);
            }
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// Parses a lambda argument to SqlExpr without semantic translation.
    /// </summary>
    private static (SqlExpr Expression, ClauseKind ClauseKind, bool IsDescending)?
        TryParseLambdaToSqlExpr(
            InterceptorKind kind,
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (argument is not LambdaExpressionSyntax lambda)
            return null;

        var body = IR.SqlExprParser.GetLambdaBody(lambda);
        if (body == null)
            return null;

        var parameterNames = IR.SqlExprParser.GetLambdaParameterNames(lambda);
        if (parameterNames.Count == 0)
            return null;

        var sqlExpr = IR.SqlExprParser.ParseWithPathTracking(body, parameterNames);

        // Annotate captured variable types using semantic model (needed for collection IN expansion)
        sqlExpr = IR.SqlExprAnnotator.AnnotateCapturedTypes(sqlExpr, body, semanticModel);

        // Inline constant collection arrays (e.g., new[] { "a", "b" }.Contains(x) → IN ('a', 'b'))
        sqlExpr = IR.SqlExprAnnotator.InlineConstantCollections(sqlExpr, body, semanticModel);

        // Inline constant string values in LIKE patterns (e.g., u.Name.Contains(ReadonlyField) → LIKE '%value%')
        sqlExpr = IR.SqlExprAnnotator.InlineConstantLikePatterns(sqlExpr, body, semanticModel);

        var isDescending = false;
        if ((kind == InterceptorKind.OrderBy || kind == InterceptorKind.ThenBy) &&
            invocation.ArgumentList.Arguments.Count >= 2)
        {
            var directionArg = invocation.ArgumentList.Arguments[1].Expression;
            isDescending = IsDescendingDirection(directionArg, semanticModel);
        }

        var clauseKind = kind switch
        {
            InterceptorKind.Where => ClauseKind.Where,
            InterceptorKind.DeleteWhere => ClauseKind.Where,
            InterceptorKind.UpdateWhere => ClauseKind.Where,
            InterceptorKind.UpdateSet => ClauseKind.Set,
            InterceptorKind.UpdateSetAction => ClauseKind.Set,
            InterceptorKind.UpdateSetPoco => ClauseKind.Set,
            InterceptorKind.OrderBy => ClauseKind.OrderBy,
            InterceptorKind.ThenBy => ClauseKind.OrderBy,
            InterceptorKind.GroupBy => ClauseKind.GroupBy,
            InterceptorKind.Having => ClauseKind.Having,
            InterceptorKind.Set => ClauseKind.Set,
            InterceptorKind.Join => ClauseKind.Join,
            InterceptorKind.LeftJoin => ClauseKind.Join,
            InterceptorKind.RightJoin => ClauseKind.Join,
            InterceptorKind.CrossJoin => ClauseKind.Join,
            InterceptorKind.FullOuterJoin => ClauseKind.Join,
            _ => ClauseKind.Where
        };

        return (sqlExpr, clauseKind, isDescending);
    }

    /// <summary>
    /// Extracts column names from a batch insert column selector lambda.
    /// Handles both single-column (u => u.Username) and tuple (u => (u.Username, u.Password)) forms.
    /// </summary>
    private static ImmutableArray<string>? ExtractBatchInsertColumnNames(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var argExpr = invocation.ArgumentList.Arguments[0].Expression;
        if (argExpr is not LambdaExpressionSyntax lambda)
            return null;

        var body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };

        if (body == null) return null;

        var names = new List<string>();

        // Tuple form: u => (u.Username, u.Password)
        if (body is TupleExpressionSyntax tuple)
        {
            foreach (var arg in tuple.Arguments)
            {
                if (arg.Expression is MemberAccessExpressionSyntax ma)
                    names.Add(ma.Name.Identifier.ValueText);
                else
                    return null; // non-analyzable
            }
        }
        // Single-column form: u => u.Username
        else if (body is MemberAccessExpressionSyntax singleMa)
        {
            names.Add(singleMa.Name.Identifier.ValueText);
        }
        else
        {
            return null; // non-analyzable
        }

        return names.Count > 0 ? ImmutableArray.CreateRange(names) : null;
    }

    /// <summary>
    /// Walks the receiver chain from a batch insert terminal to find the
    /// BatchInsertColumnSelector (Insert(lambda)) and extract column names.
    /// When the syntactic walk hits a variable, traces through its declaration
    /// to find the InsertBatch call in the initializer chain.
    /// </summary>
    private static ImmutableArray<string>? ExtractBatchInsertColumnNamesFromChain(
        InvocationExpressionSyntax terminal,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var result = WalkChainForInsertBatch(terminal.Expression);
        if (result != null)
            return result;

        // Syntactic walk didn't find InsertBatch — the receiver may be a variable.
        // Trace through variable declarations to find the initializer chain.
        var root = VariableTracer.WalkFluentChainRoot(
            terminal.Expression is MemberAccessExpressionSyntax ma ? ma.Expression : terminal.Expression);
        if (root is IdentifierNameSyntax)
        {
            var traceResult = VariableTracer.TraceToChainRoot(root, semanticModel, ct, maxHops: 2);
            if (traceResult.Traced && traceResult.Initializers != null)
            {
                // Iterate the initializers already collected by TraceToChainRoot
                // instead of re-resolving declarators.
                foreach (var initializer in traceResult.Initializers)
                {
                    // Try walking this initializer's chain for InsertBatch
                    if (initializer is InvocationExpressionSyntax initInvoc)
                    {
                        result = WalkChainForInsertBatch(initInvoc.Expression);
                        if (result != null)
                            return result;
                        // Also check if initInvoc itself is the InsertBatch call
                        if (initInvoc.Expression is MemberAccessExpressionSyntax initMa
                            && initMa.Name.Identifier.ValueText == "InsertBatch"
                            && initInvoc.ArgumentList.Arguments.Count == 1
                            && initInvoc.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax)
                        {
                            return ExtractBatchInsertColumnNames(initInvoc);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Syntactic walk looking for InsertBatch(lambda) in a receiver chain.
    /// </summary>
    private static ImmutableArray<string>? WalkChainForInsertBatch(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is MemberAccessExpressionSyntax ma)
        {
            if (ma.Expression is InvocationExpressionSyntax invoc)
            {
                if (invoc.Expression is MemberAccessExpressionSyntax innerMa
                    && innerMa.Name.Identifier.ValueText == "InsertBatch"
                    && invoc.ArgumentList.Arguments.Count == 1
                    && invoc.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax)
                {
                    return ExtractBatchInsertColumnNames(invoc);
                }
                current = invoc.Expression;
            }
            else
                break;
        }
        return null;
    }

    /// <summary>
    /// Detects if the invocation is inside a for/while/foreach/do loop.
    /// </summary>
    private static bool DetectLoopAncestor(SyntaxNode node)
    {
        SyntaxNode? child = node;
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is ForStatementSyntax forStmt)
            {
                // Only disqualify if inside the loop body, not the initializer/condition/incrementor
                if (child == forStmt.Statement)
                    return true;
            }
            else if (ancestor is WhileStatementSyntax || ancestor is DoStatementSyntax)
            {
                return true;
            }
            else if (ancestor is ForEachStatementSyntax forEachStmt)
            {
                // The collection expression (iteration source) is evaluated once — not a loop.
                // Only disqualify if inside the loop body.
                if (child == forEachStmt.Statement)
                    return true;
            }
            // Stop at method boundary
            if (ancestor is MethodDeclarationSyntax || ancestor is LocalFunctionStatementSyntax)
                break;
            child = ancestor;
        }
        return false;
    }

    /// <summary>
    /// Detects if the invocation is inside a try/catch block.
    /// </summary>
    private static bool DetectTryCatchAncestor(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is TryStatementSyntax)
                return true;
            if (ancestor is MethodDeclarationSyntax || ancestor is LocalFunctionStatementSyntax)
                break;
        }
        return false;
    }

    /// <summary>
    /// Detects if the invocation is inside a lambda or delegate expression
    /// beyond the immediate query lambda parameter.
    /// </summary>
    private static bool DetectLambdaCaptureAncestor(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is LambdaExpressionSyntax || ancestor is AnonymousMethodExpressionSyntax)
            {
                // Check if this lambda is a direct argument to the Quarry method invocation
                // (e.g., .Where(u => ...)) — that's expected, not a capture.
                if (ancestor.Parent is ArgumentSyntax arg
                    && arg.Parent is ArgumentListSyntax argList
                    && argList.Parent is InvocationExpressionSyntax)
                    continue;
                return true;
            }
            if (ancestor is MethodDeclarationSyntax || ancestor is LocalFunctionStatementSyntax)
                break;
        }
        return false;
    }

    /// <summary>
    /// Detects variable-level disqualifiers by walking all references to the chain root variable.
    /// </summary>
    private static (bool IsPassedAsArgument, bool IsAssignedFromNonQuarryMethod) DetectVariableDisqualifiers(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Find the root variable name from the chain
        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax ma2)
            receiver = ma2.Expression;
        if (receiver == null)
            return (false, false);

        receiver = VariableTracer.WalkFluentChainRoot(receiver);

        if (receiver is not IdentifierNameSyntax rootId)
            return (false, false);

        // If the root variable is a QuarryContext (not a builder), passing it to helper
        // methods is fine — the context is a shared resource, not a chain variable.
        var rootSymbol = semanticModel.GetSymbolInfo(rootId).Symbol;
        ITypeSymbol? rootType = rootSymbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };
        if (rootType != null)
        {
            var t = rootType;
            while (t != null)
            {
                if (t.Name == "QuarryContext" && t.ContainingNamespace?.Name == "Quarry")
                    return (false, false);
                t = t.BaseType;
            }
        }

        var varName = rootId.Identifier.Text;
        var methodBody = invocation.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault()?.Body;
        if (methodBody == null)
            return (false, false);

        bool passed = false;
        bool nonQuarry = false;

        foreach (var id in methodBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.Text != varName) continue;

            // Passed as argument to non-Quarry method
            if (id.Parent is ArgumentSyntax
                && id.Parent.Parent is ArgumentListSyntax al
                && al.Parent is InvocationExpressionSyntax ce)
            {
                var mn = ce.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax i => i.Identifier.Text,
                    _ => null
                };
                if (mn != null && !IsKnownBuilderMethod(mn))
                    passed = true;
            }

            // Captured in lambda/local function (not a Quarry clause lambda)
            var cl = id.Ancestors().FirstOrDefault(a =>
                a is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax);
            if (cl != null && cl.Ancestors().Any(a => a == methodBody)
                && !(cl.Parent is ArgumentSyntax && cl.Parent.Parent is ArgumentListSyntax && cl.Parent.Parent.Parent is InvocationExpressionSyntax))
            {
                passed = true;
            }

            // Assigned from non-Quarry method
            if (id.Parent is AssignmentExpressionSyntax asgn
                && asgn.Left == id
                && asgn.Right is InvocationExpressionSyntax rhs)
            {
                var mn = rhs.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax i => i.Identifier.Text,
                    _ => null
                };
                if (mn != null && !IsKnownBuilderMethod(mn))
                    nonQuarry = true;
            }
        }

        return (passed, nonQuarry);
    }

    private static bool IsKnownBuilderMethod(string name)
    {
        return name is "Where" or "OrderBy" or "ThenBy" or "Select" or "GroupBy" or "Having"
            or "Set" or "Join" or "LeftJoin" or "RightJoin" or "CrossJoin" or "FullOuterJoin" or "Limit" or "Offset" or "Distinct"
            or "ExecuteFetchAllAsync" or "ExecuteFetchFirstAsync" or "ExecuteFetchFirstOrDefaultAsync"
            or "ExecuteFetchSingleAsync" or "ExecuteFetchSingleOrDefaultAsync"
            or "ExecuteScalarAsync" or "ExecuteNonQueryAsync"
            or "ToAsyncEnumerable" or "ToDiagnostics" or "Prepare"
            or "Users" or "Orders" or "Accounts" or "Products" or "Projects";
    }

    /// <summary>
    /// Extracts the ChainId of the operand (right-hand) query builder argument
    /// for set operation calls (Union, Intersect, Except, etc.).
    /// The operand is passed as the first argument and may be an invocation chain
    /// or a variable referencing a builder.
    /// </summary>
    private static string? ExtractSetOperationOperandChainId(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count < 1)
            return null;

        var operandExpr = invocation.ArgumentList.Arguments[0].Expression;

        // The operand is typically an invocation chain (e.g., db.Users().Where(...))
        // or a variable referencing a builder (e.g., otherQuery).
        // Find the outermost invocation in the operand expression to compute its ChainId.
        if (operandExpr is InvocationExpressionSyntax operandInvocation)
        {
            return ComputeChainId(operandInvocation, semanticModel, cancellationToken);
        }

        // If it's a variable, trace through to find the chain root.
        // ComputeChainId handles this via VariableTracer when called with
        // any invocation in the chain, but here we have just an identifier.
        // Build a synthetic chain lookup from the identifier.
        if (operandExpr is IdentifierNameSyntax identifier)
        {
            var traceResult = VariableTracer.TraceToChainRoot(identifier, semanticModel, cancellationToken, maxHops: 2);
            var rootExpr = traceResult.Root;

            // Find the containing method to build the ChainId
            foreach (var ancestor in invocation.Ancestors())
            {
                if (ancestor is MethodDeclarationSyntax method)
                {
                    var filePath = invocation.SyntaxTree.FilePath;
                    var varName = traceResult.FirstVariableName ?? identifier.Identifier.ValueText;
                    return $"{filePath}:{method.Span.Start}:{varName}";
                }
                if (ancestor is LocalFunctionStatementSyntax localFunc)
                {
                    var filePath = invocation.SyntaxTree.FilePath;
                    var varName = traceResult.FirstVariableName ?? identifier.Identifier.ValueText;
                    return $"{filePath}:{localFunc.Span.Start}:{varName}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Detects if the result variable of a .Prepare() call escapes the declaring method scope.
    /// Returns a reason string if escaped, null if contained.
    /// Checks: returned from method, passed as argument, captured in lambda, assigned to field/property.
    /// </summary>
    internal static string? DetectPreparedQueryEscape(InvocationExpressionSyntax prepareInvocation)
    {
        // Find the variable the .Prepare() result is assigned to
        string? varName = null;

        // Case 1: var q = ...Prepare();
        if (prepareInvocation.Parent is EqualsValueClauseSyntax evc
            && evc.Parent is VariableDeclaratorSyntax vds)
        {
            varName = vds.Identifier.Text;
        }
        // Case 2: q = ...Prepare(); (reassignment)
        else if (prepareInvocation.Parent is AssignmentExpressionSyntax asgn
            && asgn.Left is IdentifierNameSyntax assignId)
        {
            varName = assignId.Identifier.Text;
        }

        if (varName == null)
        {
            // .Prepare() result not assigned — either chained directly (which is fine) or discarded
            return null;
        }

        // Find the enclosing method body
        SyntaxNode? methodBody = null;
        foreach (var ancestor in prepareInvocation.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax method)
            {
                methodBody = method.Body ?? (SyntaxNode?)method.ExpressionBody;
                break;
            }
            if (ancestor is LocalFunctionStatementSyntax localFunc)
            {
                methodBody = localFunc.Body ?? (SyntaxNode?)localFunc.ExpressionBody;
                break;
            }
        }
        if (methodBody == null)
            return null;

        foreach (var id in methodBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.Text != varName) continue;

            // Returned from method
            if (id.Parent is ReturnStatementSyntax)
                return "returned from method";

            // Assigned to field, property, or other non-local target
            if (id.Parent is AssignmentExpressionSyntax asgn && asgn.Right == id)
            {
                return "assigned to field or property";
            }

            // Passed as argument to non-PreparedQuery terminal method
            if (id.Parent is ArgumentSyntax
                && id.Parent.Parent is ArgumentListSyntax al
                && al.Parent is InvocationExpressionSyntax ce)
            {
                var mn = ce.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax i => i.Identifier.Text,
                    _ => null
                };
                if (mn != null && !IsPreparedQueryTerminal(mn))
                    return "passed as argument";
            }

            // Captured in lambda or local function
            var cl = id.Ancestors().FirstOrDefault(a =>
                a is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax);
            if (cl != null && cl.Ancestors().Any(a => a == methodBody))
                return "captured in lambda";
        }

        return null;
    }

    private static bool IsPreparedQueryTerminal(string name)
    {
        return name is "ToDiagnostics"
            or "ExecuteFetchAllAsync" or "ExecuteFetchFirstAsync" or "ExecuteFetchFirstOrDefaultAsync"
            or "ExecuteFetchSingleAsync" or "ExecuteFetchSingleOrDefaultAsync"
            or "ExecuteScalarAsync" or "ExecuteNonQueryAsync"
            or "ToAsyncEnumerable";
    }

    /// <summary>
    /// Detects if the invocation is inside an if statement or ternary expression.
    /// Returns NestingContext with the condition text and nesting depth.
    /// </summary>
    private static NestingContext? DetectNestingContext(SyntaxNode node)
    {
        // Walk all ancestors to count total nesting depth and capture innermost if info
        int totalIfDepth = 0;
        string? innermostCondition = null;
        BranchKind innermostBranchKind = BranchKind.Independent;
        bool passedThroughElse = false;

        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is ElseClauseSyntax)
            {
                passedThroughElse = true;
                continue;
            }
            if (ancestor is IfStatementSyntax ifStatement)
            {
                totalIfDepth++;
                if (innermostCondition == null)
                {
                    innermostCondition = ifStatement.Condition.ToString();
                    if (passedThroughElse || ifStatement.Else != null || ifStatement.Parent is ElseClauseSyntax)
                        innermostBranchKind = BranchKind.MutuallyExclusive;
                }
                passedThroughElse = false;
                continue;
            }
            if (ancestor is ConditionalExpressionSyntax ternary && innermostCondition == null)
            {
                innermostCondition = ternary.Condition.ToString();
                innermostBranchKind = BranchKind.MutuallyExclusive;
                continue;
            }
            if (ancestor is MethodDeclarationSyntax || ancestor is LocalFunctionStatementSyntax)
                break;
        }

        if (innermostCondition == null)
            return null;

        return new NestingContext(innermostCondition, totalIfDepth, innermostBranchKind);
    }

    /// <summary>
    /// Computes a stable chain ID linking all call sites in the same fluent chain.
    /// Derived from the receiver expression's root variable name and enclosing method scope.
    /// </summary>
    private static string? ComputeChainId(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        // Walk the fluent chain to find the root receiver
        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            receiver = memberAccess.Expression;

        if (receiver == null)
            return null;

        // Walk up through fluent chain to the syntactic root
        receiver = VariableTracer.WalkFluentChainRoot(receiver);

        // Trace through variable assignments to find the original chain origin
        var traceResult = VariableTracer.TraceToChainRoot(receiver, semanticModel, ct, maxHops: 2);
        var rootExpr = traceResult.Root;
        var rootText = rootExpr.ToString();

        // Check if the traced root is a local variable whose type is a Quarry builder.
        // After tracing, this is only true when tracing stopped at maxHops (the root
        // is still a builder variable we couldn't trace further).
        bool rootIsBuilderLocal = false;
        if (rootExpr is IdentifierNameSyntax rootIdent)
        {
            var symbol = semanticModel.GetSymbolInfo(rootIdent, ct).Symbol;
            if (symbol is ILocalSymbol localSymbol)
            {
                if (VariableTracer.IsBuilderType(localSymbol.Type))
                    rootIsBuilderLocal = true;
            }
        }

        // Determine the assigned variable name for chain linking.
        // When tracing succeeded and the root is a non-builder (e.g., context variable),
        // use the first variable name from the trace to link all sites together.
        string? assignedVarName = null;
        if (!rootIsBuilderLocal)
        {
            if (traceResult.Traced && traceResult.FirstVariableName != null)
                assignedVarName = traceResult.FirstVariableName;
            else
                assignedVarName = GetAssignedVariableName(invocation);
        }

        // Find the containing method scope
        int statementStart = -1;
        // For chains inside object/collection initializers, use the individual
        // initializer expression's span to differentiate independent chains
        // that share the same outer statement.
        int initializerMemberStart = -1;
        foreach (var ancestor in invocation.Ancestors())
        {
            if (initializerMemberStart < 0
                && ancestor is AssignmentExpressionSyntax initAssign
                && initAssign.Parent is InitializerExpressionSyntax)
            {
                initializerMemberStart = initAssign.SpanStart;
            }
            // Check LocalFunctionStatementSyntax BEFORE the generic StatementSyntax check,
            // because LocalFunctionStatementSyntax derives from StatementSyntax and would
            // otherwise be consumed by the generic handler (which does `continue`), preventing
            // it from being recognized as a scope boundary. This matters for top-level programs
            // where there is no enclosing MethodDeclarationSyntax.
            if (ancestor is LocalFunctionStatementSyntax localFunc)
            {
                var filePath = invocation.SyntaxTree.FilePath;
                if (rootIsBuilderLocal)
                    return $"{filePath}:{localFunc.Span.Start}:{rootText}";
                if (assignedVarName != null)
                    return $"{filePath}:{localFunc.Span.Start}:{assignedVarName}";
                var scopeKey = initializerMemberStart >= 0 ? initializerMemberStart
                    : statementStart >= 0 ? statementStart : localFunc.Span.Start;
                return $"{filePath}:{scopeKey}:{rootText}";
            }
            if (ancestor is StatementSyntax stmt && !(ancestor is BlockSyntax))
            {
                // Capture the innermost statement only. Using the outermost statement would
                // collapse standalone chains in different branches (if/else, try/catch) into
                // a single ChainId, causing false-positive QRY033 "forked query chain" errors.
                if (statementStart < 0)
                    statementStart = stmt.SpanStart;
                continue; // Keep walking to find the containing method too
            }
            if (ancestor is MethodDeclarationSyntax method)
            {
                var filePath = invocation.SyntaxTree.FilePath;
                // Variable-based chains: use method scope + variable name (no statement differentiation)
                // so all uses of the same variable merge into one chain.
                if (rootIsBuilderLocal)
                    return $"{filePath}:{method.Span.Start}:{rootText}";
                // Assignment linkage: traced first variable or direct assignment.
                if (assignedVarName != null)
                    return $"{filePath}:{method.Span.Start}:{assignedVarName}";
                // Standalone fluent chains: use statement scope to distinguish separate chains.
                // For initializer members, use the individual member's span for finer differentiation.
                var scopeKey = initializerMemberStart >= 0 ? initializerMemberStart
                    : statementStart >= 0 ? statementStart : method.Span.Start;
                return $"{filePath}:{scopeKey}:{rootText}";
            }
        }

        return rootText;
    }

    /// <summary>
    /// Checks if the invocation is part of an expression being assigned to a local variable.
    /// Returns the variable name if found, null otherwise.
    /// Handles both declarations (var query = ...) and assignments (query = ...).
    /// </summary>
    private static string? GetAssignedVariableName(InvocationExpressionSyntax invocation)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            // var query = _db.Users().Where(...)
            if (ancestor is EqualsValueClauseSyntax equalsClause
                && equalsClause.Parent is VariableDeclaratorSyntax declarator)
            {
                return declarator.Identifier.Text;
            }
            // query = query.Where(...)
            // Skip property assignments inside object initializers (e.g., new Dto { Prop = db.Users()... })
            // — each initializer expression is an independent chain, not a shared variable.
            if (ancestor is AssignmentExpressionSyntax assignment
                && assignment.Left is IdentifierNameSyntax ident)
            {
                if (assignment.Parent is InitializerExpressionSyntax)
                    break; // Stop — don't walk past initializer to outer assignments
                return ident.Identifier.Text;
            }
            // Stop at statement boundary
            if (ancestor is StatementSyntax)
                break;
        }
        return null;
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
    /// Gets the expected lambda parameter count from a method symbol's first delegate parameter.
    /// For example, Func&lt;T, bool&gt; → 1, Func&lt;T1, T2, bool&gt; → 2.
    /// </summary>
    private static int GetExpressionLambdaParameterCount(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return 0;

        var paramType = method.Parameters[0].Type;

        // Delegate types: Func<T, bool> has 2 type args → 1 parameter,
        // Action<T> has 1 type arg → 1 parameter.
        // Func<T, TResult> → N-1 parameters (last type arg is return type).
        // Action<T1, T2> → N parameters (all type args are parameters).
        if (paramType is INamedTypeSymbol rawDelegate
            && rawDelegate.TypeKind == TypeKind.Delegate
            && rawDelegate.TypeArguments.Length > 0)
        {
            // Func<> returns the last type arg, so parameter count = Length - 1
            // Action<> has no return type, so parameter count = Length
            return rawDelegate.Name.StartsWith("Func")
                ? rawDelegate.TypeArguments.Length - 1
                : rawDelegate.TypeArguments.Length;
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
    /// Extracts SetAction assignments directly from the lambda body without ClauseTranslator.
    /// Returns assignments and parameters for Set(Action&lt;T&gt;) patterns.
    /// </summary>
    private static (IReadOnlyList<Models.SetActionAssignment> Assignments, IReadOnlyList<Translation.ParameterInfo> Parameters, IReadOnlyDictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? AllCapturedIdentifiers)?
        ExtractSetActionAssignments(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.ArgumentList.Arguments.Count < 1)
            return null;

        var lambdaArg = invocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // Extract the lambda parameter name
        string parameterName;
        if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
            parameterName = simpleLambda.Parameter.Identifier.Text;
        else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda && parenLambda.ParameterList.Parameters.Count > 0)
            parameterName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
        else
            return null;

        // Collect assignment expressions from the lambda body
        var assignmentExprs = new List<AssignmentExpressionSyntax>();
        if (lambda.Body is AssignmentExpressionSyntax singleAssignment)
        {
            assignmentExprs.Add(singleAssignment);
        }
        else if (lambda.Body is BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is ExpressionStatementSyntax exprStmt
                    && exprStmt.Expression is AssignmentExpressionSyntax assignment)
                {
                    assignmentExprs.Add(assignment);
                }
                else
                {
                    return null; // Non-assignment statement — bail out
                }
            }
        }
        else
        {
            return null;
        }

        if (assignmentExprs.Count == 0)
            return null;

        var assignments = new List<Models.SetActionAssignment>();
        var parameters = new List<Translation.ParameterInfo>();

        for (int i = 0; i < assignmentExprs.Count; i++)
        {
            var asgn = assignmentExprs[i];

            // Left side: u.PropertyName → column name (unquoted — quoted during enrichment)
            if (asgn.Left is not MemberAccessExpressionSyntax memberAccess)
                return null;

            var propertyName = memberAccess.Name.Identifier.Text;
            var columnSql = propertyName;

            // Right side: value expression
            var valueExpr = asgn.Right;
            var typeInfo = semanticModel.GetTypeInfo(valueExpr);
            var valueType = typeInfo.Type?.ToDisplayString() ?? "object";
            var valueExpression = valueExpr.ToFullString().Trim();

            // Check if the value is a compile-time constant that can be inlined into SQL
            var constantValue = semanticModel.GetConstantValue(valueExpr);
            if (constantValue.HasValue)
            {
                var inlinedSql = FormatConstantAsSqlLiteralSimple(constantValue.Value);
                if (inlinedSql != null)
                {
                    assignments.Add(new Models.SetActionAssignment(
                        columnSql, valueTypeName: null, customTypeMappingClass: null,
                        inlinedSqlValue: inlinedSql, inlinedCSharpExpression: valueExpression));
                    continue;
                }
            }

            // Check if the value expression contains column references (lambda parameter member accesses).
            // If so, parse through SqlExprParser to decompose into column refs + captured vars.
            if (ContainsLambdaParamRef(valueExpr, parameterName))
            {
                // Store the expression text and lambda param name as strings.
                // The SqlExpr parsing/binding happens in CallSiteTranslator where the
                // result is stored on TranslatedClause (not cached through the pipeline).
                assignments.Add(new Models.SetActionAssignment(
                    columnSql, valueTypeName: null, customTypeMappingClass: null,
                    columnExpressionText: valueExpr.ToFullString().Trim(),
                    columnExpressionLambdaParam: parameterName));
                continue;
            }

            var paramIndex = parameters.Count;
            var isCaptured = IsSetActionCapturedVariable(valueExpr, parameterName);

            var paramInfo = new Translation.ParameterInfo(paramIndex, $"@p{paramIndex}", valueType, valueExpression,
                isCaptured: isCaptured,
                expressionPath: isCaptured ? valueExpression : null);

            // Populate CapturedFieldName by extracting the root identifier from the value expression
            if (isCaptured)
            {
                var (rootName, rootExpr) = ExtractRootIdentifier(valueExpr, parameterName);
                if (rootName != null)
                {
                    paramInfo.CapturedFieldName = rootName;
                    if (rootExpr != null)
                    {
                        var rootTypeInfo = semanticModel.GetTypeInfo(rootExpr);
                        if (rootTypeInfo.Type != null)
                            paramInfo.CapturedFieldType = rootTypeInfo.Type.ToDisplayString(
                                Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                }
            }

            parameters.Add(paramInfo);

            assignments.Add(new Models.SetActionAssignment(
                columnSql, valueTypeName: null, customTypeMappingClass: null));
        }

        // Collect all external identifiers from non-inlined value expressions
        // so BuildExtractionPlans can create extractors for computed expressions (a + b)
        // and column expressions with captured variables (e.EndTime - e.StartTime + captured)
        Dictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>? allCapturedIdentifiers = null;

        // From simple captured parameters
        if (parameters.Count > 0)
        {
            foreach (var p in parameters)
            {
                if (!p.IsCaptured)
                    continue;

                // Find the assignment whose value expression matches this parameter
                // (non-inlined assignments have no InlinedSqlValue)
                var valueExpr = assignmentExprs.FirstOrDefault(a =>
                    a.Right.ToFullString().Trim() == p.ValueExpression);
                if (valueExpr == null)
                    continue;

                var identifiers = CollectExternalIdentifiers(valueExpr.Right, semanticModel, parameterName);
                if (identifiers != null)
                {
                    allCapturedIdentifiers ??= new Dictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>(StringComparer.Ordinal);
                    foreach (var kvp in identifiers)
                    {
                        if (!allCapturedIdentifiers.ContainsKey(kvp.Key))
                            allCapturedIdentifiers[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // From column expression assignments (which may contain captured variables mixed with columns)
        foreach (var asgn in assignmentExprs)
        {
            if (!ContainsLambdaParamRef(asgn.Right, parameterName))
                continue;
            var identifiers = CollectExternalIdentifiers(asgn.Right, semanticModel, parameterName);
            if (identifiers != null)
            {
                allCapturedIdentifiers ??= new Dictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>(StringComparer.Ordinal);
                foreach (var kvp in identifiers)
                {
                    if (!allCapturedIdentifiers.ContainsKey(kvp.Key))
                        allCapturedIdentifiers[kvp.Key] = kvp.Value;
                }
            }
        }

        return (assignments, parameters, allCapturedIdentifiers);
    }

    /// <summary>
    /// Formats a compile-time constant value as a SQL literal without requiring a translation context.
    /// Uses PostgreSQL boolean format (TRUE/FALSE) — ChainAnalyzer detects boolean literals and
    /// re-tags them for dialect-specific formatting downstream.
    /// Unsupported types return null, causing the value to be emitted as a runtime parameter instead.
    /// </summary>
    private static string? FormatConstantAsSqlLiteralSimple(object? value)
    {
        return value switch
        {
            null => "NULL",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short s => s.ToString(System.Globalization.CultureInfo.InvariantCulture),
            byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint ui => ui.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort us => us.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool bv => bv ? "TRUE" : "FALSE",
            char c => $"'{EscapeSqlString(c.ToString())}'",
            string str => $"'{EscapeSqlString(str)}'",
            _ => null // DateTime, Guid, byte[], enums, etc. fall back to parameter binding
        };
    }

    /// <summary>
    /// Escapes a string for use in a SQL single-quoted literal.
    /// Handles single quotes and backslashes (PostgreSQL standard_conforming_strings=on).
    /// </summary>
    private static string EscapeSqlString(string value)
    {
        if (value.IndexOf('\'') < 0 && value.IndexOf('\\') < 0)
            return value;
        return value.Replace("'", "''").Replace("\\", "\\\\");
    }

    /// <summary>
    /// Extracts the root identifier from a value expression.
    /// For simple identifiers (e.g., <c>localVar</c>), returns the identifier text.
    /// For member access chains (e.g., <c>obj.Property.Nested</c>), walks left to the root identifier.
    /// Returns null if the root is the lambda parameter or cannot be determined.
    /// </summary>
    private static (string? Name, ExpressionSyntax? RootExpression) ExtractRootIdentifier(ExpressionSyntax expr, string lambdaParamName)
    {
        // Walk left through member access to find the root
        var current = expr;
        while (current is MemberAccessExpressionSyntax memberAccess)
            current = memberAccess.Expression;

        if (current is IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.Text;
            if (name == lambdaParamName)
                return (null, null);
            return (name, identifier);
        }

        return (null, null);
    }

    /// <summary>
    /// Walks an expression tree and collects all external identifiers (locals, parameters,
    /// static/instance fields) that are NOT the lambda parameter or type/method references.
    /// Used to discover all captured variables in computed SetAction expressions like <c>a + b</c>.
    /// </summary>
    private static Dictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>?
        CollectExternalIdentifiers(ExpressionSyntax expr, SemanticModel semanticModel, string lambdaParamName)
    {
        var result = new Dictionary<string, (string Type, bool IsStaticField, string? ContainingClass)>(StringComparer.Ordinal);

        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is not IdentifierNameSyntax identifier)
                continue;

            var name = identifier.Identifier.Text;
            if (name == lambdaParamName)
                continue;

            if (result.ContainsKey(name))
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;
            if (symbol == null)
                continue;

            if (symbol is ILocalSymbol local)
            {
                var varType = ResolveSymbolType(local.Type, local, semanticModel);
                result[name] = (varType, false, null);
            }
            else if (symbol is IParameterSymbol param && !param.IsThis)
            {
                var varType = ResolveSymbolType(param.Type, param, semanticModel);
                result[name] = (varType, false, null);
            }
            else if (symbol is IFieldSymbol field)
            {
                var varType = ResolveSymbolType(field.Type, field, semanticModel);
                var containingClass = field.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                result[name] = (varType, field.IsStatic, containingClass);
            }
            // Skip types, methods, namespaces, properties, events
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Resolves a type symbol to a fully qualified string, handling error types
    /// via the same fallback used by <see cref="DisplayClassNameResolver.CollectCapturedVariableTypes"/>.
    /// </summary>
    private static string ResolveSymbolType(ITypeSymbol typeSymbol, ISymbol ownerSymbol, SemanticModel semanticModel)
    {
        if (typeSymbol.TypeKind == TypeKind.Error)
        {
            var typeName = typeSymbol.Name;
            if (!string.IsNullOrEmpty(typeName) && typeName != "?")
            {
                // Try to qualify via using directives (same approach as DisplayClassNameResolver)
                var declRef = ownerSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (declRef != null && declRef.SyntaxTree.GetRoot() is CompilationUnitSyntax root)
                {
                    var compilation = semanticModel.Compilation;
                    var schemaName = typeName + "Schema";
                    foreach (var usingDir in root.Usings)
                    {
                        if (usingDir.Alias != null || usingDir.Name == null)
                            continue;
                        var ns = usingDir.Name.ToString();
                        if (compilation.GetTypeByMetadataName(ns + "." + schemaName) != null)
                            return "global::" + ns + "." + typeName;
                    }
                }
            }
            return "object";
        }

        var result = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.IsNullOrWhiteSpace(result) ? "object" : result;
    }

    /// <summary>
    /// Recursively checks whether an expression contains any member access on the lambda parameter
    /// (e.g., e.EndTime, e.StartTime). When true, the expression must be parsed through
    /// the SqlExpr pipeline to decompose into SQL column references + captured variable parameters.
    /// </summary>
    private static bool ContainsLambdaParamRef(ExpressionSyntax expr, string lambdaParamName)
    {
        if (expr is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is IdentifierNameSyntax memberId
            && memberId.Identifier.Text == lambdaParamName)
            return true;

        foreach (var child in expr.ChildNodes())
        {
            if (child is ExpressionSyntax childExpr && ContainsLambdaParamRef(childExpr, lambdaParamName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if an expression in a SetAction lambda is a captured variable.
    /// </summary>
    private static bool IsSetActionCapturedVariable(ExpressionSyntax expr, string lambdaParamName)
    {
        if (expr is LiteralExpressionSyntax)
            return false;
        if (expr is DefaultExpressionSyntax)
            return false;
        if (expr is TypeOfExpressionSyntax)
            return false;
        if (expr is IdentifierNameSyntax id && id.Identifier.Text == lambdaParamName)
            return false;
        if (expr is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is IdentifierNameSyntax memberId
            && memberId.Identifier.Text == lambdaParamName)
            return false;
        return true;
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

            if (calledMethod == "Insert")
            {
                if (!TryExtractPropertyNamesFromArgument(invoc, result))
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
    /// Walks the receiver invocation chain to find an Insert call and extracts
    /// the entity type name syntactically from its argument expression.
    /// For example, in <c>ctx.Insert(new User{}).ToDiagnostics()</c>, this finds
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

                if (calledMethod == "Insert")
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
    private static RawCallSite? TryDiscoverExecutionSiteSyntactically(
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
            or InterceptorKind.ExecuteFetchSingleOrDefault or InterceptorKind.ToAsyncEnumerable))
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

        // Detect disqualifiers
        var isInsideLoop = DetectLoopAncestor(invocation);
        var isInsideTryCatch = DetectTryCatchAncestor(invocation);
        var isCapturedInLambda = DetectLambdaCaptureAncestor(invocation);
        var nestingContext = DetectNestingContext(invocation);
        var chainId = ComputeChainId(invocation, semanticModel, cancellationToken);
        var (isPassedAsArgument, isAssignedFromNonQuarryMethod) =
            DetectVariableDisqualifiers(invocation, semanticModel);

        return new RawCallSite(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            uniqueId: uniqueId,
            kind: kind,
            builderKind: ClassifyBuilderKind(builderKind),
            entityTypeName: entityTypeName,
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion ?? 1,
            location: new DiagnosticLocation(filePath, line, column, invocation.Span),
            contextClassName: contextClassName,
            builderTypeName: builderKind,
            isInsideLoop: isInsideLoop,
            isInsideTryCatch: isInsideTryCatch,
            isCapturedInLambda: isCapturedInLambda,
            isPassedAsArgument: isPassedAsArgument,
            isAssignedFromNonQuarryMethod: isAssignedFromNonQuarryMethod,
            nestingContext: nestingContext,
            chainId: chainId);
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
    /// <c>var users = new[] { new User{} }; db.InsertBatch(u => u.Name).Values(users)...</c>
    /// <c>var users = items.Select(i => new User{...}); db.InsertBatch(u => u.Name).Values(users)...</c>
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
    /// <summary>
    /// Resolves the originating builder type from a PreparedQuery variable.
    /// Traces through the variable assignment to find the .Prepare() call,
    /// then gets the builder type from the .Prepare() method's containing type.
    /// </summary>
    private static INamedTypeSymbol? ResolvePreparedQueryOriginBuilderType(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Walk through the receiver to the chain root
        var root = VariableTracer.WalkFluentChainRoot(receiver);

        // Trace through variable declarations
        var traceResult = VariableTracer.TraceToChainRoot(root, semanticModel, cancellationToken, maxHops: 2);

        // Walk through the initializers to find the .Prepare() call
        if (traceResult.Initializers != null)
        {
            foreach (var init in traceResult.Initializers)
            {
                if (init is InvocationExpressionSyntax initInvoc
                    && initInvoc.Expression is MemberAccessExpressionSyntax initMa
                    && initMa.Name.Identifier.Text == "Prepare")
                {
                    // Get the type of the receiver of .Prepare()
                    var receiverTypeInfo = semanticModel.GetTypeInfo(initMa.Expression, cancellationToken);
                    if (receiverTypeInfo.Type is INamedTypeSymbol builderType && IsQuarryBuilderType(builderType))
                        return builderType;
                }

                // Also try walking the fluent chain inside the initializer
                var current = init;
                while (current is InvocationExpressionSyntax inv
                    && inv.Expression is MemberAccessExpressionSyntax ma)
                {
                    if (ma.Name.Identifier.Text == "Prepare")
                    {
                        var receiverTypeInfo = semanticModel.GetTypeInfo(ma.Expression, cancellationToken);
                        if (receiverTypeInfo.Type is INamedTypeSymbol bt && IsQuarryBuilderType(bt))
                            return bt;
                    }
                    current = ma.Expression;
                }
            }
        }

        // Direct case: receiver is the .Prepare() invocation itself
        if (receiver is InvocationExpressionSyntax directInvoc
            && directInvoc.Expression is MemberAccessExpressionSyntax directMa
            && directMa.Name.Identifier.Text == "Prepare")
        {
            var receiverTypeInfo = semanticModel.GetTypeInfo(directMa.Expression, cancellationToken);
            if (receiverTypeInfo.Type is INamedTypeSymbol directBt && IsQuarryBuilderType(directBt))
                return directBt;
        }

        return null;
    }

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
                        // For InsertBatch(Func<T, TColumns>), unwrap the element type
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

    private static bool IsBatchInsertBuilderType(string typeName)
        => typeName == "IBatchInsertBuilder";

    private static bool IsExecutableBatchInsertType(string typeName)
        => typeName == "IExecutableBatchInsert";

    /// <summary>
    /// Extracts the entity and result type arguments from a builder type.
    /// Also reports whether the result type is a value type (needed for nullable return type handling).
    /// </summary>
    private static (string? EntityTypeName, string? ResultTypeName, bool IsValueTypeResult) ExtractTypeArguments(INamedTypeSymbol builderType)
    {
        if (!builderType.IsGenericType || builderType.TypeArguments.Length == 0)
            return (null, null, false);

        var entityType = builderType.TypeArguments[0];
        var entityTypeName = entityType.ToFullyQualifiedDisplayString();

        string? resultTypeName = null;
        ITypeSymbol? resultTypeSymbol = null;
        var name = builderType.Name;

        // For joined builder types, only the last type arg is TResult when it exceeds the entity count
        if (name == "IJoinedQueryBuilder" && builderType.TypeArguments.Length > 2)
        {
            // IJoinedQueryBuilder<T1,T2,TResult> — TResult is index 2
            resultTypeSymbol = builderType.TypeArguments[2];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder3" && builderType.TypeArguments.Length > 3)
        {
            // IJoinedQueryBuilder3<T1,T2,T3,TResult> — TResult is index 3
            resultTypeSymbol = builderType.TypeArguments[3];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder4" && builderType.TypeArguments.Length > 4)
        {
            // IJoinedQueryBuilder4<T1,T2,T3,T4,TResult> — TResult is index 4
            resultTypeSymbol = builderType.TypeArguments[4];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder5" && builderType.TypeArguments.Length > 5)
        {
            // IJoinedQueryBuilder5<T1,T2,T3,T4,T5,TResult> — TResult is index 5
            resultTypeSymbol = builderType.TypeArguments[5];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }
        else if (name == "IJoinedQueryBuilder6" && builderType.TypeArguments.Length > 6)
        {
            // IJoinedQueryBuilder6<T1,T2,T3,T4,T5,T6,TResult> — TResult is index 6
            resultTypeSymbol = builderType.TypeArguments[6];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }
        else if (!IsJoinedBuilderName(name) && builderType.TypeArguments.Length > 1)
        {
            // QueryBuilder<T, TResult> — TResult is index 1
            resultTypeSymbol = builderType.TypeArguments[1];
            resultTypeName = resultTypeSymbol.ToFullyQualifiedDisplayString();
        }

        var isValueType = resultTypeSymbol?.IsValueType == true;
        return (entityTypeName, resultTypeName, isValueType);
    }

    private static bool IsJoinedBuilderName(string name)
        => name is "IJoinedQueryBuilder" or "IJoinedQueryBuilder3" or "IJoinedQueryBuilder4"
            or "IJoinedQueryBuilder5" or "IJoinedQueryBuilder6";

    private static BuilderKind ClassifyBuilderKind(string typeName)
    {
        if (typeName.Contains("ExecutableDeleteBuilder")) return BuilderKind.ExecutableDelete;
        if (typeName.Contains("DeleteBuilder")) return BuilderKind.Delete;
        if (typeName.Contains("ExecutableUpdateBuilder")) return BuilderKind.ExecutableUpdate;
        if (typeName.Contains("UpdateBuilder")) return BuilderKind.Update;
        if (typeName.Contains("ExecutableBatchInsert")) return BuilderKind.ExecutableBatchInsert;
        if (typeName.Contains("BatchInsertBuilder")) return BuilderKind.BatchInsert;
        if (typeName.Contains("InsertBuilder")) return BuilderKind.Insert;
        if (typeName.Contains("JoinedQueryBuilder")) return BuilderKind.JoinedQuery;
        if (typeName.Contains("EntityAccessor")) return BuilderKind.EntityAccessor;
        if (typeName == "PreparedQuery") return BuilderKind.Query; // fallback — actual kind determined from chain
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
        else if (name == "IJoinedQueryBuilder5")
        {
            // IJoinedQueryBuilder5<T1,T2,T3,T4,T5> has 5 entities
            // IJoinedQueryBuilder5<T1,T2,T3,T4,T5,TResult> has 5 entities + 1 result
            entityCount = 5;
        }
        else if (name == "IJoinedQueryBuilder6")
        {
            // IJoinedQueryBuilder6<T1,T2,T3,T4,T5,T6> has 6 entities
            // IJoinedQueryBuilder6<T1,T2,T3,T4,T5,T6,TResult> has 6 entities + 1 result
            entityCount = 6;
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

        // Walk the fluent chain to the syntactic root, then trace through variable
        // assignments (up to 2 hops) to find the original chain origin.
        receiver = VariableTracer.WalkFluentChainRoot(receiver);
        var traceResult = VariableTracer.TraceToChainRoot(receiver, semanticModel, cancellationToken, maxHops: 2);
        receiver = traceResult.Root;

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
    /// Discovers a CTE usage site (With or FromCte on QuarryContext).
    /// </summary>
    private static RawCallSite? DiscoverCteSite(
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol containingType,
        string methodName,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        var (filePath, line, column) = location.Value;

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
        catch { }
#endif
        if (interceptableLocationData == null)
            return null;

        var uniqueId = GenerateUniqueId(filePath, line, column, methodName);
        var kind = methodName == "With" ? InterceptorKind.CteDefinition : InterceptorKind.FromCte;

        // Extract TDto type from type arguments.
        // With<TDto>(inner) has 1 type arg; With<TEntity, TDto>(inner) has 2 — TDto is always last.
        string? cteEntityTypeName = null;
        IReadOnlyList<IR.CteColumn>? cteColumns = null;
        if (methodSymbol.TypeArguments.Length >= 1)
        {
            var dtoType = methodSymbol.TypeArguments[methodSymbol.TypeArguments.Length - 1];
            cteEntityTypeName = dtoType.ToFullyQualifiedDisplayString();

            // Resolve DTO columns during discovery so they flow through the pipeline
            if (kind == InterceptorKind.CteDefinition && dtoType is INamedTypeSymbol namedDto)
            {
                cteColumns = IR.CteDtoResolver.ResolveColumns(namedDto);
            }
        }

        // For CteDefinition, store the argument's SpanStart for matching to inner chain groups
        int? cteInnerArgSpanStart = null;
        if (kind == InterceptorKind.CteDefinition
            && invocation.ArgumentList.Arguments.Count > 0)
        {
            cteInnerArgSpanStart = invocation.ArgumentList.Arguments[0].SpanStart;
        }

        // Store the inner query parameter type for correct interceptor signature generation.
        // With<TDto>(IQueryBuilder<TDto>) → "IQueryBuilder<TDto>"
        // With<TEntity, TDto>(IQueryBuilder<TEntity, TDto>) → "IQueryBuilder<TEntity, TDto>"
        string? cteParamType = null;
        if (kind == InterceptorKind.CteDefinition && methodSymbol.Parameters.Length > 0)
        {
            var paramSym = methodSymbol.Parameters[0].Type;
            if (paramSym is INamedTypeSymbol paramNamed && paramNamed.IsGenericType)
            {
                var typeArgs = string.Join(", ", paramNamed.TypeArguments.Select(
                    t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                cteParamType = $"{paramNamed.Name}<{typeArgs}>";
            }
        }

        // Use the concrete context class type rather than QuarryContext base class,
        // because CTE methods are defined on the base class but interceptors must match
        // the concrete derived type. Walk the chain root to find the actual context variable.
        var contextClassName = containingType.Name;
        var contextNamespace = containingType.ContainingNamespace?.IsGlobalNamespace == false
            ? containingType.ContainingNamespace.ToDisplayString()
            : null;
        if (invocation.Expression is MemberAccessExpressionSyntax cteMa)
        {
            var chainRoot = VariableTracer.WalkFluentChainRoot(cteMa.Expression);
            var rootType = semanticModel.GetTypeInfo(chainRoot, cancellationToken).Type;
            if (rootType is INamedTypeSymbol rootNamed && IsQuarryContextType(rootNamed))
            {
                contextClassName = rootNamed.Name;
                contextNamespace = rootNamed.ContainingNamespace?.IsGlobalNamespace == false
                    ? rootNamed.ContainingNamespace.ToDisplayString()
                    : null;
            }
        }

        var chainId = ComputeChainId(invocation, semanticModel, cancellationToken);
        var isInsideLoop = DetectLoopAncestor(invocation);

        // Use the CTE DTO type as the entity type placeholder
        var entityTypeName = cteEntityTypeName ?? "object";

        return new RawCallSite(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            uniqueId: uniqueId,
            kind: kind,
            builderKind: BuilderKind.Query,
            entityTypeName: entityTypeName,
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion,
            location: new DiagnosticLocation(filePath, line, column, invocation.Span),
            contextClassName: contextClassName,
            contextNamespace: contextNamespace,
            chainId: chainId,
            isInsideLoop: isInsideLoop,
            cteEntityTypeName: cteEntityTypeName,
            cteInnerArgSpanStart: cteInnerArgSpanStart,
            cteColumns: cteColumns,
            builderTypeName: cteParamType);
    }

    /// <summary>
    /// Resolves a method call against the concrete context type when the semantic model
    /// can't resolve it because the receiver type is the base QuarryContext class.
    /// This happens after CTE With() calls, which return QuarryContext during source
    /// generation but the chain continues with context-specific methods like Users().
    /// </summary>
    private static IMethodSymbol? TryResolveViaChainRootContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return null;

        var methodName = ma.Name.Identifier.ValueText;

        // Walk the fluent chain root to find the context variable
        var chainRoot = VariableTracer.WalkFluentChainRoot(ma.Expression);
        var rootType = semanticModel.GetTypeInfo(chainRoot, ct).Type;

        if (rootType is not INamedTypeSymbol rootNamed || !IsQuarryContextType(rootNamed))
            return null;

        // Don't resolve against the base class itself — only derived context classes
        if (rootNamed.Name == "QuarryContext")
            return null;

        var argCount = invocation.ArgumentList.Arguments.Count;
        foreach (var member in rootNamed.GetMembers(methodName))
        {
            if (member is IMethodSymbol ms && ms.Parameters.Length == argCount)
                return ms;
        }

        // Also check base type members (for methods defined on QuarryContext like FromCte)
        var baseType = rootNamed.BaseType;
        while (baseType != null)
        {
            foreach (var member in baseType.GetMembers(methodName))
            {
                if (member is IMethodSymbol ms && ms.Parameters.Length == argCount)
                    return ms;
            }
            baseType = baseType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Detects whether an invocation is inside a CTE inner chain (argument to a With() call).
    /// Returns the flag and the argument's SpanStart for ChainId differentiation.
    /// </summary>
    private static (bool IsCteInner, int ArgSpanStart) DetectCteInnerChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            // Stop at method/function boundaries
            if (ancestor is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax)
                break;

            if (ancestor is ArgumentSyntax arg
                && arg.Parent is ArgumentListSyntax argList
                && argList.Parent is InvocationExpressionSyntax parentInvocation
                && parentInvocation.Expression is MemberAccessExpressionSyntax parentMa
                && parentMa.Name.Identifier.ValueText == "With")
            {
                // Syntactic match — verify semantically that it's a Quarry CTE method.
                // The semantic model may not fully resolve the With() call during source
                // generation (e.g., when the generated context class isn't yet available),
                // so fall back to candidate symbols if the primary symbol is null.
                var symbolInfo = semanticModel.GetSymbolInfo(parentInvocation, ct);
                var parentSymbol = symbolInfo.Symbol as IMethodSymbol;
                if (parentSymbol == null && symbolInfo.CandidateSymbols.Length > 0)
                    parentSymbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;
                if (parentSymbol != null && IsQuarryContextType(parentSymbol.ContainingType))
                    return (true, arg.SpanStart);
            }
        }
        return (false, -1);
    }

    /// <summary>
    /// Discovers a RawSql usage site (RawSqlAsync/RawSqlScalarAsync on QuarryContext).
    /// </summary>
    private static RawCallSite? DiscoverRawSqlUsageSite(
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

        // Get location information
        var location = GetMethodLocation(invocation);
        if (location == null)
            return null;

        // If T is an unresolved type parameter, emit QRY031 diagnostic via a non-analyzable site.
        // InterceptableLocationData is intentionally null so the FileEmitter skips code generation.
        if (typeArgSymbol.TypeKind == TypeKind.TypeParameter)
        {
            var (fp, ln, col) = location.Value;

            // Resolve context so the site flows through pipeline grouping
            var diagContextClassName = ResolveContextFromCallSite(invocation, semanticModel, cancellationToken);
            if (diagContextClassName == null && invocation.Expression is MemberAccessExpressionSyntax diagMemberAccess)
            {
                var receiverTypeInfo = semanticModel.GetTypeInfo(diagMemberAccess.Expression, cancellationToken);
                if (receiverTypeInfo.Type is INamedTypeSymbol receiverType && IsQuarryContextType(receiverType))
                {
                    var candidate = receiverType;
                    while (candidate != null && candidate.BaseType?.Name != "QuarryContext")
                        candidate = candidate.BaseType;
                    diagContextClassName = candidate?.Name ?? receiverType.Name;
                }
            }

            return new RawCallSite(
                methodName: methodSymbol.Name,
                filePath: fp,
                line: ln,
                column: col,
                uniqueId: GenerateUniqueId(fp, ln, col, methodSymbol.Name),
                kind: kind,
                builderKind: BuilderKind.Query,
                entityTypeName: typeArgSymbol.Name,
                resultTypeName: typeArgSymbol.Name,
                isAnalyzable: false,
                nonAnalyzableReason: typeArgSymbol.Name,
                interceptableLocationData: null,
                interceptableLocationVersion: 1,
                location: new DiagnosticLocation(fp, ln, col, invocation.Span),
                contextClassName: diagContextClassName);
        }

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

        // Detect disqualifiers
        var isInsideLoop = DetectLoopAncestor(invocation);
        var isInsideTryCatch = DetectTryCatchAncestor(invocation);
        var isCapturedInLambda = DetectLambdaCaptureAncestor(invocation);
        var nestingContext = DetectNestingContext(invocation);
        var chainId = ComputeChainId(invocation, semanticModel, cancellationToken);

        return new RawCallSite(
            methodName: methodName,
            filePath: filePath,
            line: line,
            column: column,
            uniqueId: uniqueId,
            kind: kind,
            builderKind: BuilderKind.Query,
            entityTypeName: resultTypeName, // For RawSql, "entity type" is the result type T
            resultTypeName: resultTypeName,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: interceptableLocationData,
            interceptableLocationVersion: interceptableLocationVersion,
            location: new DiagnosticLocation(filePath, line, column, invocation.Span),
            contextClassName: contextClassName,
            builderTypeName: containingType.Name,
            isInsideLoop: isInsideLoop,
            isInsideTryCatch: isInsideTryCatch,
            isCapturedInLambda: isCapturedInLambda,
            nestingContext: nestingContext,
            chainId: chainId)
        {
            // Store invocation for deferred RawSqlTypeInfo resolution in DisplayClassEnricher.
            // The supplemental compilation is needed to resolve generated entity type members.
            EnrichmentInvocation = invocation
        };
    }

    /// <summary>
    /// Resolves a type symbol into RawSqlTypeInfo for reader code generation.
    /// Called by DisplayClassEnricher after the supplemental compilation is built,
    /// so generated entity types have their members available.
    /// </summary>
    internal static RawSqlTypeInfo ResolveRawSqlTypeInfo(ITypeSymbol typeSymbol, bool hasCancellationToken)
    {
        var typeName = typeSymbol.ToFullyQualifiedDisplayString();
        var shortName = typeSymbol.ToMinimallyQualifiedDisplayString();

        // Check if T is a scalar type
        if (IsScalarType(typeSymbol))
        {
            var scalarReaderMethod = TypeClassification.GetReaderMethod(shortName);
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

            var readerMethod = TypeClassification.GetReaderMethod(isEnum ? GetEnumUnderlyingType(propType) : effectiveClrType);

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
    internal static bool IsScalarType(ITypeSymbol type)
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
    internal static string GetEnumUnderlyingType(ITypeSymbol type)
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
    /// Stores the lambda syntax reference on a RawCallSite for deferred batch enrichment
    /// by DisplayClassEnricher in Stage 2.5.
    /// </summary>
    private static void EnrichDisplayClassInfo(
        RawCallSite site,
        InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (arg is not LambdaExpressionSyntax lambda)
            return;

        site.EnrichmentLambda = lambda;
    }
}
