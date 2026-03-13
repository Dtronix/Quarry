using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Quarry.Analyzers.Rules.Patterns;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class MultipleQueriesRuleTests
{
    [Test]
    public void QRA402_RuleId_IsCorrect()
    {
        var rule = new MultipleQueriesSameTableRule();
        Assert.That(rule.RuleId, Is.EqualTo("QRA402"));
    }

    [Test]
    public void QRA402_Descriptor_IsCorrect()
    {
        var rule = new MultipleQueriesSameTableRule();
        Assert.That(rule.Descriptor.Id, Is.EqualTo("QRA402"));
        Assert.That(rule.Descriptor.Category, Is.EqualTo("QuarryAnalyzer"));
        Assert.That(rule.Descriptor.DefaultSeverity, Is.EqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Info));
    }

    [Test]
    public void QRA402_NoEntityType_NoReport()
    {
        // When entityTypeName is null, rule should not report
        var rule = new MultipleQueriesSameTableRule();
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();

        var site = new Quarry.Generators.Models.UsageSiteInfo(
            methodName: "Where", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: null!,
            isAnalyzable: true, kind: Quarry.Generators.Models.InterceptorKind.Where,
            invocationSyntax: invocation, uniqueId: "test_1",
            dialect: GenSqlDialect.PostgreSQL);

        var context = new Quarry.Analyzers.Rules.QueryAnalysisContext(
            site, null, null, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());

        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }
}
