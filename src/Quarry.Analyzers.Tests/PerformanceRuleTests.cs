using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Performance;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class PerformanceRuleTests
{
    private static QueryAnalysisContext CreateWhereContext(string sqlFragment, EntityInfo? entity = null)
    {
        var expression = new SqlRawExpr(sqlFragment);
        var site = new RawCallSite(
            methodName: "Where",
            filePath: "Test.cs",
            line: 1, column: 1,
            uniqueId: "test_1",
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: "User",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, new TextSpan(0, 0)),
            expression: expression,
            clauseKind: ClauseKind.Where);

        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        return new QueryAnalysisContext(
            site, entity, null, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());
    }

    // -- QRA301: LeadingWildcardLikeRule --

    [Test]
    public void QRA301_LeadingWildcard_Reports()
    {
        var rule = new LeadingWildcardLikeRule();
        var context = CreateWhereContext("t0.\"Name\" LIKE '%test%'");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA301_TrailingWildcardOnly_NoReport()
    {
        var rule = new LeadingWildcardLikeRule();
        var context = CreateWhereContext("t0.\"Name\" LIKE 'test%'");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA302: FunctionOnColumnInWhereRule --

    [Test]
    public void QRA302_LowerFunction_Reports()
    {
        var rule = new FunctionOnColumnInWhereRule();
        var context = CreateWhereContext("LOWER(t0.\"Name\") = @p0");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("LOWER"));
    }

    [Test]
    public void QRA302_NoFunction_NoReport()
    {
        var rule = new FunctionOnColumnInWhereRule();
        var context = CreateWhereContext("t0.\"Name\" = @p0");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA303: OrOnDifferentColumnsRule --

    [Test]
    public void QRA303_OrDifferentColumns_Reports()
    {
        var rule = new OrOnDifferentColumnsRule();
        var context = CreateWhereContext("t0.\"Name\" = @p0 OR t0.\"Email\" = @p1");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA303_OrSameColumn_NoReport()
    {
        var rule = new OrOnDifferentColumnsRule();
        var context = CreateWhereContext("t0.\"Name\" = @p0 OR t0.\"Name\" = @p1");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA304: WhereOnNonIndexedColumnRule --

    [Test]
    public void QRA304_NonIndexedColumn_Reports()
    {
        var rule = new WhereOnNonIndexedColumnRule();
        var entity = CreateEntityWithIndex("Email", "IX_Email");
        var context = CreateWhereContext("t0.\"Name\" = @p0", entity: entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("Name"));
    }

    [Test]
    public void QRA304_IndexedColumn_NoReport()
    {
        var rule = new WhereOnNonIndexedColumnRule();
        var entity = CreateEntityWithIndex("Email", "IX_Email");
        var context = CreateWhereContext("t0.\"Email\" = @p0", entity: entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA305: MutableArrayInClauseRule --

    [Test]
    public void QRA305_StaticReadonlyArray_Reports()
    {
        var rule = new MutableArrayInClauseRule();
        var context = CreateWhereContextWithLambda(
            "private static readonly string[] _statuses = new[] { \"a\", \"b\" };",
            "o => _statuses.Contains(o.Status)");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA305"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("_statuses"));
    }

    [Test]
    public void QRA305_MutableStaticArray_NoReport()
    {
        // Mutable static (not readonly) — won't be inlined, so no false warning
        var rule = new MutableArrayInClauseRule();
        var context = CreateWhereContextWithLambda(
            "private static string[] _statuses = new[] { \"a\", \"b\" };",
            "o => _statuses.Contains(o.Status)");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA305_LocalArray_NoReport()
    {
        var rule = new MutableArrayInClauseRule();
        // Local variable — not a field, so rule should not fire
        var context = CreateWhereContextWithLambda(
            "", // no field needed
            "o => new[] { \"a\", \"b\" }.Contains(o.Status)");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    private static QueryAnalysisContext CreateWhereContextWithLambda(string fieldDeclaration, string lambdaExpr)
    {
        var source = $@"
using System.Linq;
class Param {{ public string Status {{ get; set; }} }}
class C {{
    {fieldDeclaration}
    void M() {{
        System.Func<Param, bool> f = null;
        Where({lambdaExpr});
    }}
    static void Where(System.Func<Param, bool> predicate) {{ }}
}}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
        };
        var runtimeRef = System.IO.Path.Combine(runtimeDir, "System.Runtime.dll");
        if (System.IO.File.Exists(runtimeRef))
            refs.Add(MetadataReference.CreateFromFile(runtimeRef));

        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the Where(...) invocation
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .First(inv => inv.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "Where");

        var site = new RawCallSite(
            methodName: "Where",
            filePath: "Test.cs",
            line: 1, column: 1,
            uniqueId: "test_qra305",
            kind: InterceptorKind.Where,
            builderKind: BuilderKind.Query,
            entityTypeName: "Param",
            resultTypeName: "Param",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, new TextSpan(0, 0)),
            expression: null,
            clauseKind: ClauseKind.Where);

        return new QueryAnalysisContext(
            site, null, null, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());
    }

    private static EntityInfo CreateEntityWithIndex(string indexedPropertyName, string indexName)
    {
        var modifiers = new ColumnModifiers();
        var columns = new List<ColumnInfo>
        {
            new ColumnInfo("Name", "Name", "string", "System.String", false, ColumnKind.Standard, null, modifiers),
            new ColumnInfo("Email", "Email", "string", "System.String", false, ColumnKind.Standard, null, modifiers),
        };
        var indexes = new List<IndexInfo>
        {
            new IndexInfo(indexName, new List<IndexColumnInfo> { new IndexColumnInfo(indexedPropertyName, SortDirection.Ascending) }, false, null, null, false, null),
        };
        return new EntityInfo("User", "UserSchema", "Test", "users", NamingStyleKind.Exact, columns,
            new List<NavigationInfo>(), indexes, Location.None, null, null);
    }
}
