using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Quarry.Analyzers.Rules;

internal interface IQueryAnalysisRule
{
    string RuleId { get; }
    DiagnosticDescriptor Descriptor { get; }
    IEnumerable<Diagnostic> Analyze(QueryAnalysisContext context);
}
