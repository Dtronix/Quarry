using System.Collections.Generic;

namespace Quarry.Generators.Models;

/// <summary>
/// Represents an interceptor method to be generated.
/// Contains all information needed to generate the [InterceptsLocation] attribute
/// and the interceptor method implementation.
/// </summary>
internal sealed class InterceptorMethodInfo
{
    public InterceptorMethodInfo(
        UsageSiteInfo usageSite,
        string interceptorMethodName,
        bool isOptimalPath = true,
        string? sqlFragment = null,
        int? parameterIndex = null,
        IReadOnlyList<string>? columnNames = null,
        string? readerDelegateCode = null,
        string? parameterBinderCode = null)
    {
        UsageSite = usageSite;
        InterceptorMethodName = interceptorMethodName;
        SqlFragment = sqlFragment;
        ParameterIndex = parameterIndex;
        ColumnNames = columnNames;
        IsOptimalPath = isOptimalPath;
        ReaderDelegateCode = readerDelegateCode;
        ParameterBinderCode = parameterBinderCode;
    }

    /// <summary>
    /// Gets the usage site this interceptor will intercept.
    /// </summary>
    public UsageSiteInfo UsageSite { get; }

    /// <summary>
    /// Gets the unique method name for this interceptor.
    /// Format: {MethodName}_{UniqueId}
    /// </summary>
    public string InterceptorMethodName { get; }

    /// <summary>
    /// Gets the generated SQL fragment for this interceptor, if applicable.
    /// </summary>
    public string? SqlFragment { get; }

    /// <summary>
    /// Gets the parameter index for parameter binding.
    /// </summary>
    public int? ParameterIndex { get; }

    /// <summary>
    /// Gets the column names for Select interceptors.
    /// </summary>
    public IReadOnlyList<string>? ColumnNames { get; }

    /// <summary>
    /// Gets whether this interceptor uses the optimal path (compile-time column binding).
    /// </summary>
    public bool IsOptimalPath { get; }

    /// <summary>
    /// Gets the generated reader delegate code for Select interceptors.
    /// </summary>
    public string? ReaderDelegateCode { get; }

    /// <summary>
    /// Gets the generated parameter binder code for Where/Set interceptors.
    /// </summary>
    public string? ParameterBinderCode { get; }
}
