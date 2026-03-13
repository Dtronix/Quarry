using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

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
        var clause = context.Site.ClauseInfo;
        if (clause == null || clause.Kind != ClauseKind.Where || !clause.IsSuccess)
            yield break;

        var dialect = context.Site.Dialect;
        var sql = clause.SqlFragment;

        // PostgreSQL: LOWER() + LIKE → suggest ILIKE
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
