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

    // -- QRA502 (Warning) / QRA503 (Error): SuboptimalForDialectRule --

    [Test]
    public void QRA502_MysqlRightJoin_Reports()
    {
        // MySQL fully supports RIGHT JOIN, but its query planner is suboptimal — perf hint only.
        var rule = new SuboptimalForDialectRule();
        var site = CreateSite(InterceptorKind.RightJoin, methodName: "RightJoin");
        var context = CreateContext(site, GenSqlDialect.MySQL);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA502"));
        Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("RIGHT JOIN"));
    }

    [Test]
    public void QRA502_SqliteRightJoin_NoReport()
    {
        // SQLite ≥ 3.39 (June 2022) supports RIGHT JOIN. Microsoft.Data.Sqlite 10.0.3
        // ships SQLite 3.49.1, so the legacy "unsupported" hint no longer applies.
        var rule = new SuboptimalForDialectRule();
        var site = CreateSite(InterceptorKind.RightJoin, methodName: "RightJoin");
        var context = CreateContext(site, GenSqlDialect.SQLite);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
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
    public void QRA503_MysqlFullOuterJoin_Reports()
    {
        // MySQL has never supported FULL OUTER JOIN — generated SQL is rejected at parse time.
        var rule = new UnsupportedForDialectRule();
        var site = CreateSite(InterceptorKind.FullOuterJoin, methodName: "FullOuterJoin");
        var context = CreateContext(site, GenSqlDialect.MySQL);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA503"));
        Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("FULL OUTER JOIN"));
    }

    [Test]
    public void QRA503_SqliteFullOuterJoin_NoReport()
    {
        // SQLite ≥ 3.39 supports FULL OUTER JOIN; Microsoft.Data.Sqlite 10.0.3 ships 3.49.1.
        var rule = new UnsupportedForDialectRule();
        var site = CreateSite(InterceptorKind.FullOuterJoin, methodName: "FullOuterJoin");
        var context = CreateContext(site, GenSqlDialect.SQLite);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA503_PostgresFullOuterJoin_NoReport()
    {
        var rule = new UnsupportedForDialectRule();
        var site = CreateSite(InterceptorKind.FullOuterJoin, methodName: "FullOuterJoin");
        var context = CreateContext(site, GenSqlDialect.PostgreSQL);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA503_SqlServerFullOuterJoin_NoReport()
    {
        var rule = new UnsupportedForDialectRule();
        var site = CreateSite(InterceptorKind.FullOuterJoin, methodName: "FullOuterJoin");
        var context = CreateContext(site, GenSqlDialect.SqlServer);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA503_SqlServerOffsetWithoutOrderBy_Reports()
    {
        // SQL Server rejects OFFSET/FETCH without ORDER BY at parse time.
        var rule = new UnsupportedForDialectRule();
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var executionInvocation = invocations.First();

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SqlServer, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA503"));
        Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("ORDER BY"));
    }

    [Test]
    public void QRA503_SqlServerOffsetWithOrderBy_NoReport()
    {
        var rule = new UnsupportedForDialectRule();
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.OrderBy(x).Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var executionInvocation = invocations.First();

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SqlServer, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA503_SqliteOffsetWithoutOrderBy_NoReport()
    {
        // SQLite supports OFFSET without ORDER BY — no diagnostic expected.
        var rule = new UnsupportedForDialectRule();
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ExecuteFetchAllAsync(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var executionInvocation = invocations.First();

        var site = CreateSite(InterceptorKind.ExecuteFetchAll, methodName: "ExecuteFetchAllAsync");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SQLite, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA503_SqlServerOffsetWithoutOrderBy_AtToAsyncEnumerableTerminal_Reports()
    {
        // ToAsyncEnumerable is a streaming terminal that assembles the same OFFSET/FETCH SQL
        // as ExecuteFetchAllAsync — the rule must guard it too. Regression for an earlier
        // gap in IsExecutionSite that omitted ToAsyncEnumerable.
        var rule = new UnsupportedForDialectRule();
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { db.Offset(10).ToAsyncEnumerable(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var executionInvocation = invocations.First();

        var site = CreateSite(InterceptorKind.ToAsyncEnumerable, methodName: "ToAsyncEnumerable");
        var context = CreateContextWithSyntax(site, GenSqlDialect.SqlServer, executionInvocation, semanticModel);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA503"));
        Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("ORDER BY"));
    }

    // -- QRA503 full-pipeline integration tests --
    //
    // The unit tests above hand the rule a synthetic RawCallSite. These pipeline tests
    // run the full analyzer against real C# source so that any regression in
    // UsageSiteDiscovery, ContextParser, or QuarryQueryAnalyzer wiring is also caught.

    private static string FullOuterJoinSource(string dialect) => $@"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}}

public class OrderSchema : Schema
{{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
}}

[QuarryContext(Dialect = SqlDialect.{dialect})]
public partial class TestDb : QuarryContext
{{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}}

public class Service
{{
    public IJoinedQueryBuilder<User, Order> Build(TestDb db)
    {{
        return db.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id);
    }}
}}";

    private static string OffsetSource(string dialect, bool withOrderBy) => $@"
using Quarry;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace TestApp;

public class UserSchema : Schema
{{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}}

[QuarryContext(Dialect = SqlDialect.{dialect})]
public partial class TestDb : QuarryContext
{{
    public partial IEntityAccessor<User> Users();
}}

public class Service
{{
    public Task<IList<string>> Run(TestDb db)
    {{
        return db.Users()
            .Where(u => true)
            .Select(u => u.UserName)
            {(withOrderBy ? ".OrderBy(u => u)" : string.Empty)}
            .Offset(10)
            .ExecuteFetchAllAsync();
    }}
}}";

    [Test]
    public async Task QRA503_Pipeline_MysqlFullOuterJoin_EmitsError()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(FullOuterJoinSource("MySQL"));
        var qra503 = diags.Where(d => d.Id == "QRA503").ToList();
        Assert.That(qra503, Has.Count.EqualTo(1), "QRA503 should fire once on MySQL FULL OUTER JOIN");
        Assert.That(qra503[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(qra503[0].GetMessage(), Does.Contain("MySQL"));
        Assert.That(qra503[0].GetMessage(), Does.Contain("FULL OUTER JOIN"));
    }

    [Test]
    public async Task QRA503_Pipeline_PostgresFullOuterJoin_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(FullOuterJoinSource("PostgreSQL"));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_SqlServerFullOuterJoin_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(FullOuterJoinSource("SqlServer"));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_SqliteFullOuterJoin_NoDiagnostic()
    {
        // SQLite ≥ 3.39 supports FULL OUTER JOIN; the rule is intentionally absent.
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(FullOuterJoinSource("SQLite"));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_SqlServerOffsetWithoutOrderBy_EmitsError()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(OffsetSource("SqlServer", withOrderBy: false));
        var qra503 = diags.Where(d => d.Id == "QRA503").ToList();
        Assert.That(qra503, Has.Count.EqualTo(1), "QRA503 should fire once on SqlServer OFFSET without ORDER BY");
        Assert.That(qra503[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(qra503[0].GetMessage(), Does.Contain("ORDER BY"));
    }

    [Test]
    public async Task QRA503_Pipeline_SqlServerOffsetWithOrderBy_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(OffsetSource("SqlServer", withOrderBy: true));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_SqliteOffsetWithoutOrderBy_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(OffsetSource("SQLite", withOrderBy: false));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_PostgresOffsetWithoutOrderBy_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(OffsetSource("PostgreSQL", withOrderBy: false));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }

    [Test]
    public async Task QRA503_Pipeline_MysqlOffsetWithoutOrderBy_NoDiagnostic()
    {
        var diags = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(OffsetSource("MySQL", withOrderBy: false));
        Assert.That(diags.Where(d => d.Id == "QRA503"), Is.Empty);
    }
}
