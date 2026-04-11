using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Migration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class EfCoreMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.EfCoreQueryDetected,
            MigrationDiagnosticDescriptors.EfCoreQueryWithWarnings,
            MigrationDiagnosticDescriptors.EfCoreQueryNotConvertible);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var resolver = new SchemaResolver();
            var schemaMap = resolver.Resolve(compilationContext.Compilation);

            if (!schemaMap.Entities.Any())
                return;

            // Check if EF Core DbContext type exists in the compilation
            var dbContextType = compilationContext.Compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.DbContext");
            if (dbContextType == null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, schemaMap),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, SchemaMap schemaMap)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var detector = new EfCoreDetector();
        var site = detector.TryDetectSingle(context.SemanticModel, invocation);
        if (site == null) return;

        var result = EfCoreConverter.Translate(site, schemaMap);

        if (result.ChainCode == null)
        {
            var reason = result.Diagnostics.Count > 0
                ? result.Diagnostics[0].Message
                : "EF Core query could not be converted";
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.EfCoreQueryNotConvertible,
                site.Location,
                reason));
        }
        else if (result.Diagnostics.Any(d => d.Severity == ConversionDiagnosticSeverity.Warning))
        {
            var warnings = result.Diagnostics
                .Where(d => d.Severity == ConversionDiagnosticSeverity.Warning)
                .ToList();
            var firstMessage = warnings[0].Message;
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.EfCoreQueryWithWarnings,
                site.Location,
                warnings.Count,
                firstMessage));
        }
        else
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.EfCoreQueryDetected,
                site.Location,
                site.TerminalMethod));
        }
    }
}
