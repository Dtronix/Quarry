using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Performance;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class PerformanceRuleTests
{
    private static QueryAnalysisContext CreateWhereContext(string sqlFragment, EntityInfo? entity = null)
    {
        var clause = ClauseInfo.Success(ClauseKind.Where, sqlFragment, new List<ParameterInfo>());
        var site = new UsageSiteInfo(
            methodName: "Where",
            filePath: "Test.cs",
            line: 1, column: 1,
            builderTypeName: "QueryBuilder",
            entityTypeName: "User",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Test")),
            uniqueId: "test_1",
            clauseInfo: clause,
            dialect: GenSqlDialect.PostgreSQL);

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

    // ── QRA301: LeadingWildcardLikeRule ──

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

    // ── QRA302: FunctionOnColumnInWhereRule ──

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

    // ── QRA303: OrOnDifferentColumnsRule ──

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

    // ── QRA304: WhereOnNonIndexedColumnRule ──

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
