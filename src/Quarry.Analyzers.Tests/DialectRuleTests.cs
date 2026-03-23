using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Dialect;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class DialectRuleTests
{
    private static ContextInfo CreateContextInfo(GenSqlDialect dialect)
    {
        return new ContextInfo(
            className: "TestDbContext",
            @namespace: "Test",
            dialect: dialect,
            schema: null,
            entities: new List<EntityInfo>(),
            entityMappings: new List<EntityMapping>(),
            location: Location.None);
    }

    private static RawCallSite CreateSite(
        InterceptorKind kind,
        string methodName = "Test",
        SqlExpr? expression = null,
        ClauseKind? clauseKind = null)
    {
        return new RawCallSite(
            methodName: methodName,
            filePath: "Test.cs",
            line: 1,
            column: 1,
            uniqueId: "test_1",
            kind: kind,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: "User",
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: new DiagnosticLocation("Test.cs", 1, 1, new TextSpan(0, 0)),
            expression: expression,
            clauseKind: clauseKind);
    }

    private static QueryAnalysisContext CreateContext(RawCallSite site, GenSqlDialect dialect)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();
        var contextInfo = CreateContextInfo(dialect);

        return new QueryAnalysisContext(
            site, null, null, contextInfo, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());
    }

    private static QueryAnalysisContext CreateContextWithSyntax(RawCallSite site, GenSqlDialect dialect, SyntaxNode invocationNode, SemanticModel semanticModel)
    {
        var contextInfo = CreateContextInfo(dialect);
        return new QueryAnalysisContext(
            site, null, null, contextInfo, semanticModel, invocationNode,
            new EmptyAnalyzerConfigOptions());
    }

    // -- QRA501: DialectOptimizationRule --

    [Test]
    public void QRA501_PostgresLowerLike_SuggestsILike()
    {
        var rule = new DialectOptimizationRule();
        var expression = new SqlRawExpr("LOWER(t0.\"Name\") LIKE '%test%'");
        var site = CreateSite(InterceptorKind.Where, methodName: "Where",
            expression: expression, clauseKind: ClauseKind.Where);
        var context = CreateContext(site, GenSqlDialect.PostgreSQL);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("ILIKE"));
    }

    [Test]
    public void QRA501_SqlServerLowerLike_NoReport()
    {
        var rule = new DialectOptimizationRule();
        var expression = new SqlRawExpr("LOWER(t0.\"Name\") LIKE '%test%'");
        var site = CreateSite(InterceptorKind.Where, methodName: "Where",
            expression: expression, clauseKind: ClauseKind.Where);
        var context = CreateContext(site, GenSqlDialect.SqlServer);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA502: SuboptimalForDialectRule --

    [Test]
    public void QRA502_SqliteRightJoin_Reports()
    {
        var rule = new SuboptimalForDialectRule();
        var site = CreateSite(InterceptorKind.RightJoin, methodName: "RightJoin");
        var context = CreateContext(site, GenSqlDialect.SQLite);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("RIGHT JOIN"));
    }

    [Test]
    public void QRA502_PostgresRightJoin_NoReport()
    {
        var rule = new SuboptimalForDialectRule();
        var site = CreateSite(InterceptorKind.RightJoin, methodName: "RightJoin");
        var context = CreateContext(site, GenSqlDialect.PostgreSQL);
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

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SqlServer, executionInvocation, semanticModel);
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

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SqlServer, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA502_SqliteOffsetWithoutOrderBy_NoReport()
    {
        var rule = new SuboptimalForDialectRule();
        // SQLite supports OFFSET without ORDER BY -- no diagnostic expected
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        // The outermost invocation (ExecuteFetchAllAsync) is first in pre-order traversal
        var executionInvocation = invocations.First();

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SQLite, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }
}
