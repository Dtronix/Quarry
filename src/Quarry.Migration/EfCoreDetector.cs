using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Detects EF Core LINQ chains in a Roslyn syntax tree.
/// </summary>
internal sealed class EfCoreDetector
{
    private static readonly HashSet<string> TerminalMethods = new(StringComparer.Ordinal)
    {
        "ToListAsync", "ToList", "ToArrayAsync", "ToArray",
        "FirstAsync", "First", "FirstOrDefaultAsync", "FirstOrDefault",
        "SingleAsync", "Single", "SingleOrDefaultAsync", "SingleOrDefault",
        "CountAsync", "Count", "LongCountAsync", "LongCount",
        "SumAsync", "Sum", "AverageAsync", "Average",
        "MinAsync", "Min", "MaxAsync", "Max",
        "AnyAsync", "Any", "AllAsync", "All",
    };

    private static readonly HashSet<string> ChainMethods = new(StringComparer.Ordinal)
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "Select", "GroupBy", "Take", "Skip", "Distinct",
        "Join",
    };

    private static readonly HashSet<string> UnsupportedEfCoreMethods = new(StringComparer.Ordinal)
    {
        "GroupJoin",
        "Include", "ThenInclude", "AsNoTracking", "AsTracking",
        "FromSqlRaw", "FromSqlInterpolated", "ExecuteUpdate", "ExecuteDelete",
        "AsSplitQuery", "AsNoTrackingWithIdentityResolution",
    };

    public IReadOnlyList<EfCoreCallSite> Detect(SemanticModel model, SyntaxNode root)
    {
        var results = new List<EfCoreCallSite>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var callSite = TryDetect(model, invocation);
            if (callSite != null)
                results.Add(callSite);
        }

        return results;
    }

    /// <summary>
    /// Checks a single invocation expression. Used by the analyzer which receives one node at a time.
    /// </summary>
    public EfCoreCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        return TryDetect(model, invocation);
    }

    private static EfCoreCallSite? TryDetect(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        // Get the method name — we only care about terminal methods as entry points
        var methodName = GetMethodName(invocation);
        if (methodName == null || !TerminalMethods.Contains(methodName))
            return null;

        // Walk the chain backwards to find the DbSet<T> root
        var steps = new List<EfCoreChainStep>();
        var unsupported = new List<string>();
        string? entityTypeName = null;
        ExpressionSyntax? currentExpr = invocation.Expression;

        // The terminal is this invocation itself, walk into its member access
        while (currentExpr is MemberAccessExpressionSyntax memberAccess)
        {
            var expr = memberAccess.Expression;

            // Check if the expression before this member access is an invocation (part of the chain)
            if (expr is InvocationExpressionSyntax chainInvocation)
            {
                // First check if this invocation returns a DbSet<T> (e.g., db.Set<User>())
                entityTypeName = TryGetDbSetEntityType(model, chainInvocation);
                if (entityTypeName != null)
                    break;

                var chainMethodName = GetMethodName(chainInvocation);
                if (chainMethodName != null)
                {
                    if (ChainMethods.Contains(chainMethodName))
                    {
                        steps.Add(new EfCoreChainStep(
                            chainMethodName,
                            chainInvocation.ArgumentList.Arguments.ToList(),
                            chainInvocation.GetLocation()));
                    }
                    else if (UnsupportedEfCoreMethods.Contains(chainMethodName))
                    {
                        unsupported.Add(chainMethodName);
                        // Still record as step so we can walk past it
                    }
                    else if (TerminalMethods.Contains(chainMethodName))
                    {
                        // Nested terminal (e.g., .Where(...).Count()) — this is the inner one;
                        // our outer terminal is the real one. Skip this detection, the inner
                        // invocation will be detected on its own pass.
                        return null;
                    }
                }

                currentExpr = chainInvocation.Expression;
                if (currentExpr is MemberAccessExpressionSyntax)
                    continue;
            }

            // Check if this expression is a DbSet<T> access (property or Set<T>() call)
            entityTypeName = TryGetDbSetEntityType(model, expr);
            if (entityTypeName != null)
                break;

            // If it's a direct member access on a property (e.g., context.Users.Where)
            // and the property type is DbSet<T>
            entityTypeName = TryGetDbSetEntityType(model, memberAccess.Expression);
            if (entityTypeName != null)
                break;

            break;
        }

        if (entityTypeName == null)
            return null;

        // Reverse steps since we walked backwards
        steps.Reverse();

        return new EfCoreCallSite(
            chainExpression: (ExpressionSyntax)invocation,
            entityTypeName: entityTypeName,
            steps: steps,
            terminalMethod: methodName,
            location: invocation.GetLocation(),
            unsupportedMethods: unsupported);
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name switch
            {
                GenericNameSyntax generic => generic.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => null,
            };
        }

        return null;
    }

    private static string? TryGetDbSetEntityType(SemanticModel model, ExpressionSyntax expression)
    {
        var typeInfo = model.GetTypeInfo(expression);
        var type = typeInfo.Type;
        if (type == null)
            return null;

        // Check if the type is DbSet<T>
        if (type is INamedTypeSymbol namedType && IsDbSetType(namedType))
        {
            if (namedType.TypeArguments.Length == 1)
                return namedType.TypeArguments[0].Name;
        }

        return null;
    }

    private static bool IsDbSetType(INamedTypeSymbol type)
    {
        // Check for Microsoft.EntityFrameworkCore.DbSet<T>
        if (type.Name == "DbSet" && type.IsGenericType)
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            if (ns == "Microsoft.EntityFrameworkCore")
                return true;
        }

        return false;
    }
}
