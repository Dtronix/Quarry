using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Dialect;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class DialectRuleTests
{
    private static QueryAnalysisContext CreateContext(UsageSiteInfo site)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        return new QueryAnalysisContext(
            site, null, null, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());
    }

    // ── QRA501: DialectOptimizationRule ──

    [Test]
    public void QRA501_PostgresLowerLike_SuggestsILike()
    {
        var rule = new DialectOptimizationRule();
        var clause = ClauseInfo.Success(ClauseKind.Where, "LOWER(t0.\"Name\") LIKE '%test%'", new List<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "Where", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.Where,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", clauseInfo: clause, dialect: GenSqlDialect.PostgreSQL);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("ILIKE"));
    }

    [Test]
    public void QRA501_SqlServerLowerLike_NoReport()
    {
        var rule = new DialectOptimizationRule();
        var clause = ClauseInfo.Success(ClauseKind.Where, "LOWER(t0.\"Name\") LIKE '%test%'", new List<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "Where", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.Where,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", clauseInfo: clause, dialect: GenSqlDialect.SqlServer);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // ── QRA502: SuboptimalForDialectRule ──

    [Test]
    public void QRA502_SqliteRightJoin_Reports()
    {
        var rule = new SuboptimalForDialectRule();
        var site = new UsageSiteInfo(
            methodName: "RightJoin", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.RightJoin,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", dialect: GenSqlDialect.SQLite);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("RIGHT JOIN"));
    }

    [Test]
    public void QRA502_PostgresRightJoin_NoReport()
    {
        var rule = new SuboptimalForDialectRule();
        var site = new UsageSiteInfo(
            methodName: "RightJoin", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.RightJoin,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", dialect: GenSqlDialect.PostgreSQL);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA502_SqlServerOffsetWithoutOrderBy_Reports()
    {
        var rule = new SuboptimalForDialectRule();
        // Build syntax: db.Offset(10).ExecuteFetchAllAsync()
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        // The outermost invocation (ExecuteFetchAllAsync) is first in pre-order traversal
        var executionInvocation = invocations.First();

        var site = new UsageSiteInfo(
            methodName: "ExecuteFetchAllAsync", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.ExecuteFetchAll,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", dialect: GenSqlDialect.SqlServer);
        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, executionInvocation,
            new EmptyAnalyzerConfigOptions());
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("ORDER BY"));
    }

    [Test]
    public void QRA502_SqlServerOffsetWithOrderBy_NoReport()
    {
        var rule = new SuboptimalForDialectRule();
        // Build syntax: db.OrderBy(x).Offset(10).ExecuteFetchAllAsync()
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.OrderBy(x).Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        // The outermost invocation (ExecuteFetchAllAsync) is first in pre-order traversal
        var executionInvocation = invocations.First();

        var site = new UsageSiteInfo(
            methodName: "ExecuteFetchAllAsync", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.ExecuteFetchAll,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", dialect: GenSqlDialect.SqlServer);
        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, executionInvocation,
            new EmptyAnalyzerConfigOptions());
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA502_SqliteOffsetWithoutOrderBy_NoReport()
    {
        var rule = new SuboptimalForDialectRule();
        // SQLite supports OFFSET without ORDER BY — no diagnostic expected
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        // The outermost invocation (ExecuteFetchAllAsync) is first in pre-order traversal
        var executionInvocation = invocations.First();

        var site = new UsageSiteInfo(
            methodName: "ExecuteFetchAllAsync", filePath: "Test.cs", line: 1, column: 1,
            builderTypeName: "QueryBuilder", entityTypeName: "User",
            isAnalyzable: true, kind: InterceptorKind.ExecuteFetchAll,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1", dialect: GenSqlDialect.SQLite);
        var context = new QueryAnalysisContext(
            site, null, null, null, semanticModel, executionInvocation,
            new EmptyAnalyzerConfigOptions());
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }
}
