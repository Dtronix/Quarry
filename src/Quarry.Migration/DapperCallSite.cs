using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Represents a detected Dapper method invocation that may be convertible to a Quarry chain call.
/// </summary>
internal sealed class DapperCallSite
{
    /// <summary>The SQL string literal from the Dapper call.</summary>
    public string Sql { get; }

    /// <summary>
    /// Parameter names from the anonymous object argument (e.g., { userId, name } → ["userId", "name"]).
    /// </summary>
    public IReadOnlyList<string> ParameterNames { get; }

    /// <summary>The Dapper method name (e.g., "QueryAsync", "ExecuteAsync").</summary>
    public string MethodName { get; }

    /// <summary>The generic type argument T (e.g., "User" from QueryAsync&lt;User&gt;), or null.</summary>
    public string? ResultTypeName { get; }

    /// <summary>The source location of the Dapper call.</summary>
    public Location Location { get; }

    /// <summary>The full invocation syntax node.</summary>
    public InvocationExpressionSyntax InvocationSyntax { get; }

    public DapperCallSite(
        string sql,
        IReadOnlyList<string> parameterNames,
        string methodName,
        string? resultTypeName,
        Location location,
        InvocationExpressionSyntax invocationSyntax)
    {
        Sql = sql;
        ParameterNames = parameterNames;
        MethodName = methodName;
        ResultTypeName = resultTypeName;
        Location = location;
        InvocationSyntax = invocationSyntax;
    }
}
