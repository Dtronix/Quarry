using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Migration;

namespace Quarry.Migration.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DapperMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.DapperQueryDetected,
            MigrationDiagnosticDescriptors.DapperQueryWithRawFallback,
            MigrationDiagnosticDescriptors.DapperQueryNotConvertible);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    }
}
