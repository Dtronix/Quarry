using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Represents a single method call in an EF Core LINQ chain (e.g., Where, OrderBy, Select).
/// </summary>
internal sealed class EfCoreChainStep
{
    /// <summary>The method name (e.g., "Where", "OrderBy", "Select").</summary>
    public string MethodName { get; }

    /// <summary>The raw syntax arguments for this method call.</summary>
    public IReadOnlyList<ArgumentSyntax> Arguments { get; }

    /// <summary>The source location of this method call.</summary>
    public Location Location { get; }

    public EfCoreChainStep(string methodName, IReadOnlyList<ArgumentSyntax> arguments, Location location)
    {
        MethodName = methodName;
        Arguments = arguments;
        Location = location;
    }
}

/// <summary>
/// Represents a detected EF Core LINQ chain that may be convertible to a Quarry chain call.
/// </summary>
internal sealed class EfCoreCallSite
{
    /// <summary>The full chain expression from DbSet access through terminal.</summary>
    public ExpressionSyntax ChainExpression { get; }

    /// <summary>The entity type name T from DbSet&lt;T&gt;.</summary>
    public string EntityTypeName { get; }

    /// <summary>Ordered list of chained method calls (excluding terminal).</summary>
    public IReadOnlyList<EfCoreChainStep> Steps { get; }

    /// <summary>The terminal method name (e.g., "ToListAsync", "FirstAsync").</summary>
    public string TerminalMethod { get; }

    /// <summary>The source location of the chain.</summary>
    public Location Location { get; }

    /// <summary>Methods in the chain that are not supported for automatic conversion.</summary>
    public IReadOnlyList<string> UnsupportedMethods { get; }

    public EfCoreCallSite(
        ExpressionSyntax chainExpression,
        string entityTypeName,
        IReadOnlyList<EfCoreChainStep> steps,
        string terminalMethod,
        Location location,
        IReadOnlyList<string> unsupportedMethods)
    {
        ChainExpression = chainExpression;
        EntityTypeName = entityTypeName;
        Steps = steps;
        TerminalMethod = terminalMethod;
        Location = location;
        UnsupportedMethods = unsupportedMethods;
    }
}
