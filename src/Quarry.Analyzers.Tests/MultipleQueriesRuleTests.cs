using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Patterns;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

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
        Assert.That(rule.Descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Info));
    }

    [Test]
    public void QRA402_NoEntityType_NoReport()
    {
        // When entityTypeName is null, rule should not report
        var rule = new MultipleQueriesSameTableRule();
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>().First();

        var site = new RawCallSite(
            methodName: "Where",
            filePath: "Test.cs",
            line: 1, column: 1,
            uniqueId: "test_1",
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: null!,
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, new TextSpan(0, 0)));

        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());

        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }
}
