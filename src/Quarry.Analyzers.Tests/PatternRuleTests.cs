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
public class PatternRuleTests
{
    // -- QRA401: QueryInsideLoopRule --

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

    // -- QRA403: ThenByWithoutOrderByRule --

    [Test]
    public void QRA403_ThenByWithoutOrderBy_Reports()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA403_ThenByDescendingWithoutOrderBy_Reports()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().{|ThenByDescending|}(x => x.Id); } }",
            "ThenByDescending",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA403_OrderByThenBy_NoReport()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().OrderBy(x => x.Name).{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA403_OrderByDescendingThenBy_NoReport()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().OrderByDescending(x => x.Name).{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA403_OrderByThenWhereThenThenBy_NoReport()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().OrderBy(x => x.Name).Where(x => x.Active).{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA403_PostCte_ThenByWithoutOrderBy_Reports()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.With<int>(null).FromCte<int>().{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA403_UnionThenByWithoutOrderBy_Reports()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic q1, dynamic q2) { q1.Union(q2).{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.ThenBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA403_NonThenBySite_NoReport()
    {
        var rule = new ThenByWithoutOrderByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().{|ThenBy|}(x => x.Id); } }",
            "ThenBy",
            InterceptorKind.OrderBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA404: HavingWithoutGroupByRule --

    [Test]
    public void QRA404_HavingWithoutGroupBy_Reports()
    {
        var rule = new HavingWithoutGroupByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().{|Having|}(x => x.Active); } }",
            "Having",
            InterceptorKind.Having);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA404_GroupByHaving_NoReport()
    {
        var rule = new HavingWithoutGroupByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().GroupBy(x => x.Tag).{|Having|}(g => g.Count > 1); } }",
            "Having",
            InterceptorKind.Having);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA404_GroupByThenWhereThenHaving_NoReport()
    {
        var rule = new HavingWithoutGroupByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().GroupBy(x => x.Tag).Where(g => g.Count > 0).{|Having|}(g => g.Count > 1); } }",
            "Having",
            InterceptorKind.Having);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA404_PostCte_HavingWithoutGroupBy_Reports()
    {
        var rule = new HavingWithoutGroupByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.With<int>(null).FromCte<int>().{|Having|}(x => x.Active); } }",
            "Having",
            InterceptorKind.Having);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA404_NonHavingSite_NoReport()
    {
        var rule = new HavingWithoutGroupByRule();
        var context = CreateContextFromSource(
            "class C { void M(dynamic db) { db.Users().{|Having|}(x => x.Active); } }",
            "Having",
            InterceptorKind.Where);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    /// <summary>
    /// Creates a QueryAnalysisContext with a real syntax tree where the target invocation
    /// is marked with {|methodName|} in the source. The marker is stripped before parsing.
    /// </summary>
    private static QueryAnalysisContext CreateContextFromSource(string markedSource, string methodName)
        => CreateContextFromSource(markedSource, methodName, InterceptorKind.ExecuteFetchAll);

    private static QueryAnalysisContext CreateContextFromSource(string markedSource, string methodName, InterceptorKind kind)
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

        var site = new RawCallSite(
            methodName: methodName,
            filePath: "Test.cs",
            line: 1, column: 1,
            uniqueId: "test_1",
            kind: kind,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: "User",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, new TextSpan(0, 0)));

        return new QueryAnalysisContext(
            site, null, null, null, semanticModel, targetInvocation,
            new EmptyAnalyzerConfigOptions());
    }
}
