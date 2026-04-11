using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Migration;

/// <summary>
/// Represents a detected ADO.NET DbCommand.Execute* invocation that may be convertible to a Quarry chain call.
/// </summary>
internal sealed class AdoNetCallSite
{
    /// <summary>The full invocation syntax node (the Execute* call).</summary>
    public InvocationExpressionSyntax InvocationSyntax { get; }

    /// <summary>The name of the DbCommand variable.</summary>
    public string CommandVariableName { get; }

    /// <summary>The SQL string extracted from CommandText assignment.</summary>
    public string Sql { get; }

    /// <summary>Parameter names collected from Parameters.Add/AddWithValue calls.</summary>
    public IReadOnlyList<string> ParameterNames { get; }

    /// <summary>The Execute method name (e.g., "ExecuteReader", "ExecuteNonQuery", "ExecuteScalar").</summary>
    public string MethodName { get; }

    /// <summary>The source location of the Execute* call.</summary>
    public Location Location { get; }

    public AdoNetCallSite(
        InvocationExpressionSyntax invocationSyntax,
        string commandVariableName,
        string sql,
        IReadOnlyList<string> parameterNames,
        string methodName,
        Location location)
    {
        InvocationSyntax = invocationSyntax;
        CommandVariableName = commandVariableName;
        Sql = sql;
        ParameterNames = parameterNames;
        MethodName = methodName;
        Location = location;
    }
}
