using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Determines whether a query chain is analyzable at compile time.
/// </summary>
/// <remarks>
/// A query is analyzable (optimal path) when:
/// - It's a fluent chain starting from a known source (e.g. db.Users)
/// - No cross-method calls construct the query
/// - The receiver is not a local variable or parameter (i.e. not a fragmented chain)
///
/// Conditional branching (if/else) is handled by the ChainAnalyzer via clause bitmask dispatch.
/// Non-analyzable queries (fallback path) still work but use runtime column discovery.
/// </remarks>
internal static class AnalyzabilityChecker
{
    /// <summary>
    /// Checks if an invocation is part of an analyzable query chain.
    /// Returns (true, null) if analyzable, or (false, reason) if not.
    /// </summary>
    public static (bool IsAnalyzable, string? Reason) CheckAnalyzability(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Check if the receiver is a variable (cross-method or stored query)
        var receiverCheck = CheckReceiverAnalyzability(invocation, semanticModel);
        if (!receiverCheck.IsAnalyzable)
        {
            return receiverCheck;
        }

        // Check for dynamic predicate construction (passing variable lambdas)
        if (HasDynamicLambdaArgument(invocation, semanticModel))
        {
            return (false, "Lambda argument references external state");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if clause analysis can be performed on an invocation, even if the
    /// overall chain is not fully analyzable. Clause analysis is possible when the
    /// lambda expression itself is analyzable — the only blocker is being inside a
    /// conditional, which doesn't prevent expression translation.
    /// This enables lightweight interceptors for conditional clause sites in
    /// chains that the ChainAnalyzer classifies as Tier 1/2.
    /// </summary>
    public static bool IsClauseAnalyzable(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Dynamic lambda arguments prevent clause analysis
        if (HasDynamicLambdaArgument(invocation, semanticModel))
            return false;

        // Being inside a conditional or having a variable receiver is OK —
        // the lambda itself can still be analyzed for SQL translation.
        return true;
    }

    /// <summary>
    /// Checks if the receiver of the method call is analyzable.
    /// </summary>
    private static (bool IsAnalyzable, string? Reason) CheckReceiverAnalyzability(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            // Static method call or simple invocation - generally analyzable
            return (true, null);
        }

        var receiver = memberAccess.Expression;

        // If receiver is another invocation, recursively check it's a fluent chain
        if (receiver is InvocationExpressionSyntax receiverInvocation)
        {
            // This is a fluent chain like: query.Select(...).Where(...)
            // Continue checking up the chain
            return CheckReceiverAnalyzability(receiverInvocation, semanticModel);
        }

        // If receiver is an identifier, check if it's a context property or a variable
        if (receiver is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;

            switch (symbol)
            {
                case ILocalSymbol local:
                    // A local variable of QuarryContext type is analyzable (e.g. var db = new MyDb(...); db.Insert(...))
                    if (IsQuarryContextType(local.Type))
                        return (true, null);
                    // Check if the local has a single, simple initializer we can trace
                    if (HasAnalyzableInitializer(local, identifier, semanticModel))
                    {
                        return (true, null);
                    }
                    return (false, "Query builder is stored in a local variable");

                case IParameterSymbol:
                    // Parameter - not analyzable
                    return (false, "Query builder is a method parameter");

                case IFieldSymbol field:
                    // Field of QuarryContext-derived type is analyzable (e.g. db.Insert(x).ToSql())
                    if (IsQuarryContextType(field.Type))
                        return (true, null);
                    return (false, "Query builder is stored in a field");

                case IPropertySymbol propertySymbol:
                    // Property access is OK if it's a context property (like db.Users)
                    // Check if it returns QueryBuilder<T> or is a QuarryContext type
                    if (IsQueryBuilderProperty(propertySymbol) || IsQuarryContextType(propertySymbol.Type))
                    {
                        return (true, null);
                    }
                    return (false, "Query builder comes from an external property");
            }
        }

        // If receiver is a member access (like db.Users), check recursively
        if (receiver is MemberAccessExpressionSyntax nestedMemberAccess)
        {
            var symbol = semanticModel.GetSymbolInfo(nestedMemberAccess).Symbol;

            if (symbol is IPropertySymbol propertySymbol && IsQueryBuilderProperty(propertySymbol))
            {
                return (true, null);
            }
        }

        // If receiver is an invocation (like db.Users()), check the method return type
        if (receiver is InvocationExpressionSyntax nestedInvocation)
        {
            var invokedSymbol = semanticModel.GetSymbolInfo(nestedInvocation).Symbol;
            if (invokedSymbol is IMethodSymbol invokedMethod)
            {
                if (invokedMethod.ContainingType != null && IsQuarryContextType(invokedMethod.ContainingType))
                    return (true, null);
                if (invokedMethod.ReturnType is INamedTypeSymbol rt
                    && VariableTracer.IsBuilderTypeName(rt.Name))
                    return (true, null);
            }
        }

        // Default: assume analyzable for other patterns
        return (true, null);
    }

    /// <summary>
    /// Checks if a local variable has a single declaration with an analyzable initializer
    /// (e.g. a property access, constructor call, or fluent chain from a known source).
    /// Traces through builder variable assignments up to 2 hops.
    /// </summary>
    private static bool HasAnalyzableInitializer(
        ILocalSymbol local,
        IdentifierNameSyntax usage,
        SemanticModel semanticModel)
    {
        var declarator = VariableTracer.TryResolveDeclarator(usage, semanticModel, CancellationToken.None);
        if (declarator == null)
            return false;

        var initializer = VariableTracer.GetInitializerExpression(declarator);
        if (initializer == null)
            return false;

        if (CheckInitializerAnalyzability(initializer, semanticModel))
            return true;

        // If the initializer's chain root is a builder variable, trace through it
        var root = VariableTracer.WalkFluentChainRoot(initializer);
        if (root is IdentifierNameSyntax rootIdent)
        {
            var traceResult = VariableTracer.TraceToChainRoot(rootIdent, semanticModel, CancellationToken.None, maxHops: 2);
            if (traceResult.Traced)
            {
                // Re-check analyzability from the traced origin's initializer
                var tracedDeclarator = traceResult.Root is IdentifierNameSyntax tracedIdent
                    ? VariableTracer.TryResolveDeclarator(tracedIdent, semanticModel, CancellationToken.None)
                    : null;
                if (tracedDeclarator != null)
                {
                    var tracedInit = VariableTracer.GetInitializerExpression(tracedDeclarator);
                    if (tracedInit != null)
                        return CheckInitializerAnalyzability(tracedInit, semanticModel);
                }
                // Traced root is not a variable (e.g., context access) — analyzable
                if (traceResult.Root is not IdentifierNameSyntax)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an initializer expression is analyzable (property access or fluent chain).
    /// </summary>
    private static bool CheckInitializerAnalyzability(
        ExpressionSyntax initializer,
        SemanticModel semanticModel)
    {
        // Property access: db.Users
        if (initializer is MemberAccessExpressionSyntax memberAccess)
        {
            var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol is IPropertySymbol prop && IsQueryBuilderProperty(prop))
                return true;
        }

        // Fluent chain from a known source: db.Users.Where(...)
        if (initializer is InvocationExpressionSyntax initInvocation)
            return CheckReceiverAnalyzability(initInvocation, semanticModel).IsAnalyzable;

        return false;
    }

    /// <summary>
    /// Checks if a property returns a QueryBuilder type.
    /// </summary>
    private static bool IsQueryBuilderProperty(IPropertySymbol property)
    {
        var returnType = property.Type;
        if (returnType is not INamedTypeSymbol namedType)
            return false;

        // Check if it's QueryBuilder or similar
        if (namedType.Name is "QueryBuilder" or "IQueryBuilder" &&
            namedType.ContainingNamespace?.ToDisplayString().StartsWith("Quarry") == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a type derives from QuarryContext.
    /// </summary>
    private static bool IsQuarryContextType(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "QuarryContext" &&
                current.ContainingNamespace?.ToDisplayString().StartsWith("Quarry") == true)
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Checks if any lambda argument references external state that makes analysis impossible.
    /// </summary>
    private static bool HasDynamicLambdaArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not LambdaExpressionSyntax lambda)
                continue;

            // Check if the lambda captures variables that aren't constants
            var dataFlow = semanticModel.AnalyzeDataFlow(lambda);
            if (dataFlow == null || !dataFlow.Succeeded)
                continue;

            // Captured variables make the lambda potentially dynamic
            // However, we allow captures of constants and simple values
            foreach (var captured in dataFlow.Captured)
            {
                // If captured variable is not a constant, mark as non-analyzable
                // Note: We're being conservative here. A more sophisticated check
                // could determine if the captured value is compile-time determinable.
                if (captured is ILocalSymbol local && !local.IsConst)
                {
                    // Check if it's a simple literal or const-like value
                    // For now, we allow captured locals as they can still be intercepted
                    // The interceptor will generate parameter binding for these
                }
            }
        }

        // For Phase 6a, we're being permissive with lambda captures
        // The actual value binding happens at runtime anyway
        return false;
    }

    /// <summary>
    /// Determines if a query chain is fully analyzable from start to execution.
    /// This checks the entire chain, not just a single invocation.
    /// </summary>
    public static (bool IsAnalyzable, string? Reason) CheckFullChainAnalyzability(
        InvocationExpressionSyntax executionMethod,
        SemanticModel semanticModel)
    {
        // Start from the execution method and walk back through the chain
        var current = executionMethod;
        var chainMethods = new List<string>();

        while (current != null)
        {
            var methodName = GetMethodName(current);
            if (methodName != null)
            {
                chainMethods.Add(methodName);
            }

            // Check this invocation
            var (isAnalyzable, reason) = CheckAnalyzability(current, semanticModel);
            if (!isAnalyzable)
            {
                return (false, reason);
            }

            // Move to the receiver if it's an invocation
            if (current.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is InvocationExpressionSyntax receiver)
            {
                current = receiver;
            }
            else
            {
                break;
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Gets the method name from an invocation.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => null
        };
    }
}
