using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Represents a single method call in a SqlKata fluent chain (e.g., Where, OrderBy, Select).
/// </summary>
internal sealed class SqlKataChainStep
{
    /// <summary>The method name (e.g., "Where", "OrderBy", "Select").</summary>
    public string MethodName { get; }

    /// <summary>The raw syntax arguments for this method call.</summary>
    public IReadOnlyList<ArgumentSyntax> Arguments { get; }

    /// <summary>The source location of this method call.</summary>
    public Location Location { get; }

    public SqlKataChainStep(string methodName, IReadOnlyList<ArgumentSyntax> arguments, Location location)
    {
        MethodName = methodName;
        Arguments = arguments;
        Location = location;
    }
}

/// <summary>
/// Represents a detected SqlKata Query chain that may be convertible to a Quarry chain call.
/// </summary>
internal sealed class SqlKataCallSite
{
    /// <summary>The full chain expression from new Query(...) through terminal or last chain call.</summary>
    public ExpressionSyntax ChainExpression { get; }

    /// <summary>The table name from new Query("table_name").</summary>
    public string TableName { get; }

    /// <summary>Ordered list of chained method calls.</summary>
    public IReadOnlyList<SqlKataChainStep> Steps { get; }

    /// <summary>The terminal method name if any (e.g., "Get", "First"), or null for non-terminal chains.</summary>
    public string? TerminalMethod { get; }

    /// <summary>The source location of the chain.</summary>
    public Location Location { get; }

    /// <summary>Methods in the chain that are not supported for automatic conversion.</summary>
    public IReadOnlyList<string> UnsupportedMethods { get; }

    public SqlKataCallSite(
        ExpressionSyntax chainExpression,
        string tableName,
        IReadOnlyList<SqlKataChainStep> steps,
        string? terminalMethod,
        Location location,
        IReadOnlyList<string> unsupportedMethods)
    {
        ChainExpression = chainExpression;
        TableName = tableName;
        Steps = steps;
        TerminalMethod = terminalMethod;
        Location = location;
        UnsupportedMethods = unsupportedMethods;
    }
}
