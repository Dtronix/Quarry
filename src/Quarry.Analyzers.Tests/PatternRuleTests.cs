using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Patterns;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class PatternRuleTests
{
    // ── QRA401: QueryInsideLoopRule ──

    [Test]
    public void QRA401_InsideForLoop_Reports()
    {
        var rule = new QueryInsideLoopRule();
        var context = CreateContextFromSource(
            "class C { void M() { for (int i = 0; i < 10; i++) { {|FetchAllAsync|}(); } } }",
            "FetchAllAsync");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA401_InsideForEachLoop_Reports()
    {
        var rule = new QueryInsideLoopRule();
        var context = CreateContextFromSource(
            "class C { void M() { foreach (var x in new int[0]) { {|FetchAllAsync|}(); } } }",
            "FetchAllAsync");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA401_InsideWhileLoop_Reports()
    {
        var rule = new QueryInsideLoopRule();
        var context = CreateContextFromSource(
            "class C { void M() { while (true) { {|FetchAllAsync|}(); } } }",
            "FetchAllAsync");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA401_OutsideLoop_NoReport()
    {
        var rule = new QueryInsideLoopRule();
        var context = CreateContextFromSource(
            "class C { void M() { {|FetchAllAsync|}(); } }",
            "FetchAllAsync");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA401_NonExecutionMethod_NoReport()
    {
        var rule = new QueryInsideLoopRule();
        var context = CreateContextFromSource(
            "class C { void M() { for (int i = 0; i < 10; i++) { {|Where|}(); } } }",
            "Where");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    /// <summary>
    /// Creates a QueryAnalysisContext with a real syntax tree where the target invocation
    /// is marked with {|methodName|} in the source. The marker is stripped before parsing.
    /// </summary>
    private static QueryAnalysisContext CreateContextFromSource(string markedSource, string methodName)
    {
        // Strip markers - find the invocation by method name
        var source = markedSource.Replace("{|", "").Replace("|}", "");
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the target invocation
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var targetInvocation = invocations.First(inv =>
        {
            var name = inv.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => ""
            };
            return name == methodName;
        });

        var site = new UsageSiteInfo(
            methodName: methodName,
            filePath: "Test.cs",
            line: 1, column: 1,
            builderTypeName: "QueryBuilder",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.ExecuteFetchAll,
            invocationSyntax: targetInvocation,
            uniqueId: "test_1",
            dialect: GenSqlDialect.PostgreSQL);

        return new QueryAnalysisContext(
            site, null, null, null, semanticModel, targetInvocation,
            new EmptyAnalyzerConfigOptions());
    }
}
