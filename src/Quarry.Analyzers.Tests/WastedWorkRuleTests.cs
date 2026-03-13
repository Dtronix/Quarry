using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.WastedWork;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Shared.Migration;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class WastedWorkRuleTests
{
    private static QueryAnalysisContext CreateContext(
        UsageSiteInfo site, EntityInfo? entity = null, IReadOnlyList<EntityInfo>? joinedEntities = null)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { Test(); } }");
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        return new QueryAnalysisContext(
            site, entity, joinedEntities, null, semanticModel, invocation,
            new EmptyAnalyzerConfigOptions());
    }

    private static UsageSiteInfo CreateSite(
        InterceptorKind kind,
        string methodName = "Test",
        ClauseInfo? clauseInfo = null,
        ProjectionInfo? projectionInfo = null)
    {
        return new UsageSiteInfo(
            methodName: methodName,
            filePath: "Test.cs",
            line: 1, column: 1,
            builderTypeName: "QueryBuilder",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: kind,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1",
            projectionInfo: projectionInfo,
            clauseInfo: clauseInfo,
            dialect: GenSqlDialect.PostgreSQL);
    }

    // ── QRA202: WideTableSelectRule ──

    [Test]
    public void QRA202_WideEntityProjection_Reports()
    {
        var rule = new WideTableSelectRule();
        var projection = new ProjectionInfo(
            ProjectionKind.Entity, "User", new List<ProjectedColumn>(), true, null);
        var site = CreateSite(InterceptorKind.Select, projectionInfo: projection);
        var entity = CreateEntityWithColumns(15);
        var context = CreateContext(site, entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA202"));
    }

    [Test]
    public void QRA202_NarrowEntityProjection_NoReport()
    {
        var rule = new WideTableSelectRule();
        var projection = new ProjectionInfo(
            ProjectionKind.Entity, "User", new List<ProjectedColumn>(), true, null);
        var site = CreateSite(InterceptorKind.Select, projectionInfo: projection);
        var entity = CreateEntityWithColumns(5);
        var context = CreateContext(site, entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA202_DtoProjection_NoReport()
    {
        var rule = new WideTableSelectRule();
        var projection = new ProjectionInfo(
            ProjectionKind.Dto, "UserDto", new List<ProjectedColumn>(), true, null);
        var site = CreateSite(InterceptorKind.Select, projectionInfo: projection);
        var entity = CreateEntityWithColumns(15);
        var context = CreateContext(site, entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // ── QRA204: DuplicateProjectionColumnRule ──

    [Test]
    public void QRA204_DuplicateColumn_Reports()
    {
        var rule = new DuplicateProjectionColumnRule();
        var columns = new List<ProjectedColumn>
        {
            new ProjectedColumn("Name", "name", "string", "System.String", false, 0),
            new ProjectedColumn("Name2", "name", "string", "System.String", false, 1),
        };
        var projection = new ProjectionInfo(ProjectionKind.Anonymous, "Anon", columns, true, null);
        var site = CreateSite(InterceptorKind.Select, projectionInfo: projection);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA204_UniqueColumns_NoReport()
    {
        var rule = new DuplicateProjectionColumnRule();
        var columns = new List<ProjectedColumn>
        {
            new ProjectedColumn("Id", "user_id", "int", "System.Int32", false, 0),
            new ProjectedColumn("Name", "user_name", "string", "System.String", false, 1),
        };
        var projection = new ProjectionInfo(ProjectionKind.Anonymous, "Anon", columns, true, null);
        var site = CreateSite(InterceptorKind.Select, projectionInfo: projection);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // ── QRA205: CartesianProductRule ──

    [Test]
    public void QRA205_EmptyOnCondition_Reports()
    {
        var rule = new CartesianProductRule();
        var joinClause = new JoinClauseInfo(JoinClauseKind.Inner, "Order", "orders", "", new List<Quarry.Generators.Translation.ParameterInfo>());
        var site = CreateSite(InterceptorKind.Join, clauseInfo: joinClause);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA205_ValidOnCondition_NoReport()
    {
        var rule = new CartesianProductRule();
        var joinClause = new JoinClauseInfo(JoinClauseKind.Inner, "Order", "orders", "t0.\"UserId\" = t1.\"UserId\"", new List<Quarry.Generators.Translation.ParameterInfo>());
        var site = CreateSite(InterceptorKind.Join, clauseInfo: joinClause);
        var context = CreateContext(site);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    private static EntityInfo CreateEntityWithColumns(int count)
    {
        var modifiers = new ColumnModifiers();
        var columns = Enumerable.Range(0, count)
            .Select(i => new ColumnInfo($"Col{i}", $"col_{i}", "string", "System.String", false, ColumnKind.Standard, null, modifiers))
            .ToList();
        return new EntityInfo("User", "UserSchema", "Test", "users", NamingStyleKind.Exact, columns,
            new List<NavigationInfo>(), new List<IndexInfo>(), Location.None, null, null);
    }
}
