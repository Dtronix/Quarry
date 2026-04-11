using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Migration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class SqlKataMigrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            MigrationDiagnosticDescriptors.SqlKataQueryDetected,
            MigrationDiagnosticDescriptors.SqlKataQueryWithWarnings,
            MigrationDiagnosticDescriptors.SqlKataQueryNotConvertible);

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

            // Check if SqlKata.Query type exists in the compilation
            var sqlKataType = compilationContext.Compilation.GetTypeByMetadataName("SqlKata.Query");
            if (sqlKataType == null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeObjectCreation(nodeContext, schemaMap),
                SyntaxKind.ObjectCreationExpression);
        });
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, SchemaMap schemaMap)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        // Check if this is a SqlKata.Query creation
        var typeInfo = context.SemanticModel.GetTypeInfo(creation);
        if (typeInfo.Type?.Name != "Query" ||
            typeInfo.Type.ContainingNamespace?.ToDisplayString() != "SqlKata")
            return;

        // Build the full call site from this creation
        var detector = new SqlKataDetector();
        var sites = detector.Detect(context.SemanticModel, creation.Parent ?? creation);
        var site = sites.FirstOrDefault();
        if (site == null) return;

        var result = SqlKataConverter.Translate(site, schemaMap);

        if (result.ChainCode == null)
        {
            var reason = result.Diagnostics.Count > 0
                ? result.Diagnostics[0].Message
                : "SqlKata query could not be converted";
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.SqlKataQueryNotConvertible,
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
                MigrationDiagnosticDescriptors.SqlKataQueryWithWarnings,
                site.Location,
                warnings.Count,
                firstMessage));
        }
        else
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.SqlKataQueryDetected,
                site.Location,
                site.TableName));
        }
    }
}
