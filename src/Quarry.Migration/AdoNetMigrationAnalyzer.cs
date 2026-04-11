using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Migration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class AdoNetMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.AdoNetQueryDetected,
            MigrationDiagnosticDescriptors.AdoNetQueryWithWarnings,
            MigrationDiagnosticDescriptors.AdoNetQueryNotConvertible);

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

            // Check if DbCommand type exists in the compilation
            var dbCommandType = compilationContext.Compilation.GetTypeByMetadataName(
                "System.Data.Common.DbCommand");
            if (dbCommandType == null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, schemaMap),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, SchemaMap schemaMap)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var detector = new AdoNetDetector();
        var site = detector.TryDetectSingle(context.SemanticModel, invocation);
        if (site == null) return;

        var result = AdoNetConverter.TranslateWithFallback(site, schemaMap);

        if (result.ChainCode == null)
        {
            var reason = result.Diagnostics.Count > 0
                ? result.Diagnostics[0].Message
                : "SQL could not be parsed";
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.AdoNetQueryNotConvertible,
                site.Location,
                reason));
        }
        else if (result.IsSuggestionOnly)
        {
            var reason = result.Diagnostics.Count > 0
                ? result.Diagnostics[0].Message
                : "Manual conversion required";
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.AdoNetQueryNotConvertible,
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
                MigrationDiagnosticDescriptors.AdoNetQueryWithWarnings,
                site.Location,
                warnings.Count,
                firstMessage));
        }
        else
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.AdoNetQueryDetected,
                site.Location,
                site.MethodName));
        }
    }
}
