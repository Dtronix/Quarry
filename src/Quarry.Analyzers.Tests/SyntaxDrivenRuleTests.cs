using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Simplification;
using Quarry.Analyzers.Rules.WastedWork;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class SyntaxDrivenRuleTests
{
    // ── QRA101: CountComparedToZeroRule ──

    [Test]
    public void QRA101_CountGreaterThanZero_Reports()
    {
        var rule = new CountComparedToZeroRule();
        // Build: Count() > 0
        var source = "class C { bool M() { return Count() > 0; } }";
        var context = CreateScalarContext(source, "Count", InterceptorKind.ExecuteScalar);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA101"));
    }

    [Test]
    public void QRA101_CountEqualsZero_Reports()
    {
        var rule = new CountComparedToZeroRule();
        var source = "class C { bool M() { return Count() == 0; } }";
        var context = CreateScalarContext(source, "Count", InterceptorKind.ExecuteScalar);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA101_CountGreaterThanFive_NoReport()
    {
        var rule = new CountComparedToZeroRule();
        var source = "class C { bool M() { return Count() > 5; } }";
        var context = CreateScalarContext(source, "Count", InterceptorKind.ExecuteScalar);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA101_NonCountMethod_NoReport()
    {
        var rule = new CountComparedToZeroRule();
        var source = "class C { bool M() { return Sum() > 0; } }";
        var context = CreateScalarContext(source, "Sum", InterceptorKind.ExecuteScalar);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // ── QRA203: OrderByWithoutLimitRule ──

    [Test]
    public void QRA203_OrderByNoLimit_Reports()
    {
        var rule = new OrderByWithoutLimitRule();
        // Simple chain: no Take/Skip/Limit/Offset ancestor
        var source = "class C { void M() { OrderBy(); } }";
        var context = CreateContextForMethod(source, "OrderBy", InterceptorKind.OrderBy);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA203_OrderByWithTake_NoReport()
    {
        var rule = new OrderByWithoutLimitRule();
        // Chain: x.OrderBy().Take() — Take is a subsequent call
        var source = "class C { void M() { x.OrderBy().Take(10); } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the OrderBy invocation (inner one)
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var orderByInv = invocations.First(inv =>
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma)
                return ma.Name.Identifier.Text == "OrderBy";
            return false;
        });

        var site = new UsageSiteInfo(
            methodName: "OrderBy", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.OrderBy,
            invocationSyntax: orderByInv, uniqueId: "test_1",
            dialect: GenSqlDialect.PostgreSQL);

        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, orderByInv,
            new EmptyAnalyzerConfigOptions());

        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA203_OrderByWithFetchFirst_NoReport()
    {
        var rule = new OrderByWithoutLimitRule();
        var source = "class C { void M() { x.OrderBy().FetchFirstAsync(); } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var orderByInv = invocations.First(inv =>
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma)
                return ma.Name.Identifier.Text == "OrderBy";
            return false;
        });

        var site = new UsageSiteInfo(
            methodName: "OrderBy", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.OrderBy,
            invocationSyntax: orderByInv, uniqueId: "test_1",
            dialect: GenSqlDialect.PostgreSQL);

        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, orderByInv,
            new EmptyAnalyzerConfigOptions());

        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    private static QueryAnalysisContext CreateScalarContext(string source, string methodName, InterceptorKind kind)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var targetInv = invocations.First(inv =>
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
            methodName: methodName, filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: kind,
            invocationSyntax: targetInv, uniqueId: "test_1",
            dialect: GenSqlDialect.PostgreSQL);

        return new QueryAnalysisContext(
            site, null, null, null, semanticModel, targetInv,
            new EmptyAnalyzerConfigOptions());
    }

    private static QueryAnalysisContext CreateContextForMethod(string source, string methodName, InterceptorKind kind)
    {
        return CreateScalarContext(source, methodName, kind);
    }
}
