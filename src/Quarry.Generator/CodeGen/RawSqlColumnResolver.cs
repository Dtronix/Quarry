using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Sql.Parser;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Resolves compile-time column ordinals from a SQL literal and a set of DTO properties.
/// Used by FileEmitter to decide between static ordinal readers and runtime struct readers.
/// </summary>
internal static class RawSqlColumnResolver
{
    /// <summary>
    /// A single resolved column mapping: property name → column ordinal in the SQL SELECT list.
    /// </summary>
    internal readonly struct ResolvedColumn
    {
        public string PropertyName { get; }
        public int Ordinal { get; }
        public RawSqlPropertyInfo Property { get; }

        public ResolvedColumn(string propertyName, int ordinal, RawSqlPropertyInfo property)
        {
            PropertyName = propertyName;
            Ordinal = ordinal;
            Property = property;
        }
    }

    /// <summary>
    /// Result of column resolution. Either <see cref="Columns"/> is populated (success)
    /// or <see cref="FallbackReason"/> explains why resolution failed.
    /// </summary>
    internal sealed class ColumnResolutionResult
    {
        /// <summary>Resolved property-to-ordinal mappings. Null on failure.</summary>
        public IReadOnlyList<ResolvedColumn>? Columns { get; }

        /// <summary>Human-readable reason for falling back to runtime ordinal discovery.</summary>
        public string? FallbackReason { get; }

        /// <summary>
        /// Position (0-based) of the unresolvable column expression, or -1 if not applicable.
        /// Used for QRY041 diagnostic emission.
        /// </summary>
        public int UnresolvableColumnPosition { get; }

        private ColumnResolutionResult(IReadOnlyList<ResolvedColumn>? columns, string? fallbackReason, int unresolvableColumnPosition)
        {
            Columns = columns;
            FallbackReason = fallbackReason;
            UnresolvableColumnPosition = unresolvableColumnPosition;
        }

        public bool IsResolved => Columns != null;

        public static ColumnResolutionResult Success(IReadOnlyList<ResolvedColumn> columns) =>
            new(columns, null, -1);

        public static ColumnResolutionResult Fallback(string reason, int unresolvablePosition = -1) =>
            new(null, reason, unresolvablePosition);
    }

    /// <summary>
    /// Attempts to resolve compile-time column ordinals from a SQL literal.
    /// </summary>
    /// <param name="sqlLiteral">The SQL string literal from the call site.</param>
    /// <param name="dialect">The SQL dialect for parsing.</param>
    /// <param name="properties">The DTO properties to match against SQL columns.</param>
    /// <returns>A result indicating success with resolved columns, or a fallback reason.</returns>
    public static ColumnResolutionResult Resolve(
        string sqlLiteral,
        SqlDialect dialect,
        IReadOnlyList<RawSqlPropertyInfo> properties)
    {
        // Parse the SQL
        var parseResult = SqlParser.Parse(sqlLiteral, dialect);

        if (!parseResult.Success)
            return ColumnResolutionResult.Fallback("SQL parse failed");

        if (parseResult.HasUnsupported)
            return ColumnResolutionResult.Fallback("SQL contains unsupported constructs (CTEs, UNION, window functions)");

        var statement = parseResult.SelectStatement;
        if (statement == null)
            return ColumnResolutionResult.Fallback("SQL is not a SELECT statement");

        // Extract column names from the SELECT list
        var sqlColumnNames = new List<string>(statement.Columns.Count);
        for (int i = 0; i < statement.Columns.Count; i++)
        {
            var column = statement.Columns[i];

            if (column is SqlStarColumn)
                return ColumnResolutionResult.Fallback("SQL contains SELECT *");

            if (column is SqlSelectColumn selectCol)
            {
                // Use alias if present
                if (selectCol.Alias != null)
                {
                    sqlColumnNames.Add(selectCol.Alias);
                    continue;
                }

                // Use column name from simple column reference
                if (selectCol.Expression is SqlColumnRef colRef)
                {
                    sqlColumnNames.Add(colRef.ColumnName);
                    continue;
                }

                // Unresolvable: complex expression without alias
                return ColumnResolutionResult.Fallback(
                    $"Column at position {i} is an expression without an alias",
                    i);
            }

            // Unknown column node type — shouldn't happen, but be safe
            return ColumnResolutionResult.Fallback($"Unknown column node type at position {i}");
        }

        // Match SQL columns to DTO properties (case-insensitive)
        var resolved = new List<ResolvedColumn>();
        for (int propIndex = 0; propIndex < properties.Count; propIndex++)
        {
            var prop = properties[propIndex];
            int ordinal = -1;

            for (int colIndex = 0; colIndex < sqlColumnNames.Count; colIndex++)
            {
                if (string.Equals(sqlColumnNames[colIndex], prop.PropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    ordinal = colIndex;
                    break;
                }
            }

            // Only include properties that have a matching column
            if (ordinal >= 0)
            {
                resolved.Add(new ResolvedColumn(prop.PropertyName, ordinal, prop));
            }
        }

        // If no properties matched any SQL columns, fall back — a static reader
        // with zero assignments would construct default objects less efficiently
        // than the struct reader (which gracefully handles zero matches).
        if (resolved.Count == 0)
            return ColumnResolutionResult.Fallback("No SQL columns match any DTO properties");

        return ColumnResolutionResult.Success(resolved);
    }
}
