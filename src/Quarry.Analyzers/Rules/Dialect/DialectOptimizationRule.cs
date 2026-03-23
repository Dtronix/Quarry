using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Analyzers.Rules.Dialect;

/// <summary>
/// QRA501: Suggests dialect-specific optimizations.
/// </summary>
internal sealed class DialectOptimizationRule : IQueryAnalysisRule
{
    public string RuleId => "QRA501";
    public DiagnosticDescriptor Descriptor => AnalyzerDiagnosticDescriptors.DialectOptimization;

    public IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context)
    {
        if (context.Site.ClauseKind != ClauseKind.Where || context.Site.Expression == null)
            yield break;

        var dialect = context.Context?.Dialect;
        if (dialect == null)
            yield break;

        var sql = context.GetRenderedSql()!;

        // PostgreSQL: LOWER() + LIKE -> suggest ILIKE
        if (dialect == SqlDialect.PostgreSQL &&
            sql.Contains("LOWER(", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIKE", StringComparison.OrdinalIgnoreCase))
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "PostgreSQL: Consider using ILIKE instead of LOWER() + LIKE for case-insensitive matching");
        }

        // SQLite: string comparison could use COLLATE NOCASE
        if (dialect == SqlDialect.SQLite &&
            sql.Contains("LOWER(", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains(" = ", StringComparison.OrdinalIgnoreCase))
        {
            yield return Diagnostic.Create(
                Descriptor,
                context.InvocationSyntax.GetLocation(),
                "SQLite: Consider using COLLATE NOCASE instead of LOWER() for case-insensitive comparison");
        }
    }
}
