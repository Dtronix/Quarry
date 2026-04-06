using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Migration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class DapperMigrationAnalyzer : DiagnosticAnalyzer
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

        context.RegisterCompilationStartAction(compilationContext =>
        {
            // Build the schema map once per compilation
            var resolver = new SchemaResolver();
            var schemaMap = resolver.Resolve(compilationContext.Compilation);

            // If no Quarry schemas found, nothing to convert to
            if (!schemaMap.Entities.Any())
                return;

            // Check if Dapper.SqlMapper type exists in the compilation
            var dapperType = compilationContext.Compilation.GetTypeByMetadataName("Dapper.SqlMapper");
            if (dapperType == null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, schemaMap),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, SchemaMap schemaMap)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var detector = new DapperDetector();
        var site = detector.TryDetectSingle(context.SemanticModel, invocation);
        if (site == null) return;

        // Try to translate — attempt SQLite first (most permissive), fall back to other dialects
        var parseResult = TryParseWithFallback(site.Sql);
        var emitter = new ChainEmitter(schemaMap);
        var result = emitter.Translate(parseResult, site);

            if (result.ChainCode == null)
            {
                // Cannot convert
                var reason = result.Diagnostics.Count > 0
                    ? result.Diagnostics[0].Message
                    : "SQL could not be parsed";
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDiagnosticDescriptors.DapperQueryNotConvertible,
                    site.Location,
                    reason));
            }
            else if (result.Diagnostics.Any(d => d.Severity == ConversionDiagnosticSeverity.Warning))
            {
                // Converted with Sql.Raw fallback
                var rawCount = result.Diagnostics.Count(d => d.Severity == ConversionDiagnosticSeverity.Warning);
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDiagnosticDescriptors.DapperQueryWithRawFallback,
                    site.Location,
                    rawCount));
            }
            else
            {
                // Fully convertible
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDiagnosticDescriptors.DapperQueryDetected,
                    site.Location,
                    site.MethodName));
        }
    }

    internal static Quarry.Shared.Sql.Parser.SqlParseResult TryParseWithFallback(string sql)
    {
        // Try SQLite first (most permissive LIMIT/OFFSET syntax)
        var result = Quarry.Shared.Sql.Parser.SqlParser.Parse(sql, SqlDialect.SQLite);
        if (result.Success)
            return result;

        // Fall back to SQL Server (handles TOP, OFFSET/FETCH)
        var sqlServerResult = Quarry.Shared.Sql.Parser.SqlParser.Parse(sql, SqlDialect.SqlServer);
        if (sqlServerResult.Success)
            return sqlServerResult;

        // Return the original (SQLite) result with its diagnostics
        return result;
    }
}
