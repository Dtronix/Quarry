using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Picks the <c>CommandBehavior</c> literal that the generated interceptor passes to the
/// executor. Decision is per-query and per-dialect: <c>SequentialAccess</c> is added only
/// when the projection contains a streaming-shaped column (<c>byte[]</c>, <see cref="System.IO.Stream"/>),
/// and never for SQLite (Microsoft.Data.Sqlite always reads lazily — the flag is pure overhead).
/// For small fixed-width row workloads (the 99% case) buffered mode is equal-or-faster on
/// every provider, so the default omits <c>SequentialAccess</c>.
/// </summary>
internal static class CommandBehaviorSelector
{
    private const string SingleResultOnly = "System.Data.CommandBehavior.SingleResult";
    private const string SingleResultSequential = "System.Data.CommandBehavior.SingleResult | System.Data.CommandBehavior.SequentialAccess";

    /// <summary>
    /// Returns the fully-qualified C# expression for the <c>CommandBehavior</c> argument
    /// to pass to a carrier executor method.
    /// </summary>
    /// <param name="dialect">The target SQL dialect for this call site.</param>
    /// <param name="columns">Projected columns produced by the analyzer. May be empty for non-projection terminals.</param>
    public static string Select(SqlDialect dialect, IReadOnlyList<ProjectedColumn>? columns)
    {
        if (dialect == SqlDialect.SQLite)
            return SingleResultOnly;

        if (columns == null || columns.Count == 0)
            return SingleResultOnly;

        for (int i = 0; i < columns.Count; i++)
        {
            if (IsLargeColumnType(columns[i].FullClrType))
                return SingleResultSequential;
        }

        return SingleResultOnly;
    }

    private static bool IsLargeColumnType(string fullClrType)
    {
        // byte[] / Byte[] / global::System.Byte[] all end in []
        if (fullClrType.EndsWith("byte[]", System.StringComparison.Ordinal) ||
            fullClrType.EndsWith("Byte[]", System.StringComparison.Ordinal))
            return true;

        // Any type whose name ends in "Stream" — Stream, MemoryStream, FileStream, etc.
        // The projected column FullClrType is the result-type member, which for streaming
        // payloads is virtually always a Stream-derived type.
        if (fullClrType.EndsWith("Stream", System.StringComparison.Ordinal))
            return true;

        return false;
    }
}
