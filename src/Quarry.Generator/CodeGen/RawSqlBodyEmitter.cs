using System.Text;
using Quarry.Generators.Models;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Emits interceptor method bodies for RawSql sites (RawSqlAsync, RawSqlScalarAsync).
/// These bypass the query builder chain — they take raw SQL + parameters directly.
/// </summary>
/// <remarks>
/// Replaces generation methods in InterceptorCodeGenerator.RawSql.cs.
/// Phase 6A Step 3 ports methods one kind at a time.
/// </remarks>
internal static class RawSqlBodyEmitter
{
    /// <summary>
    /// Emits a RawSqlAsync interceptor body.
    /// Creates DbCommand from raw SQL, binds interpolated parameters, executes reader.
    /// </summary>
    public static void EmitRawSqlAsync(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateRawSqlAsyncInterceptor
    }

    /// <summary>
    /// Emits a RawSqlScalarAsync interceptor body.
    /// </summary>
    public static void EmitRawSqlScalarAsync(
        StringBuilder sb,
        UsageSiteInfo site,
        string entityType)
    {
        // Phase 6A Step 3: Port from InterceptorCodeGenerator.GenerateRawSqlScalarAsyncInterceptor
    }
}
