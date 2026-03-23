using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Simplification;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;

namespace Quarry.Analyzers.Tests;

[TestFixture]
public class SimplificationRuleTests
{
    private static QueryAnalysisContext CreateWhereContext(string sqlFragment, int paramCount = 0, EntityInfo? entity = null)
    {
        // Create a SqlRawExpr that will render to the desired SQL fragment
        SqlExpr? expression = new SqlRawExpr(sqlFragment);

        var site = CreateSite(InterceptorKind.Where, expression: expression, clauseKind: ClauseKind.Where);

        return CreateContext(site, entity);
    }

    private static RawCallSite CreateSite(
        InterceptorKind kind,
        string methodName = "Where",
        SqlExpr? expression = null,
        ClauseKind? clauseKind = null,
        ProjectionInfo? projectionInfo = null)
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
            clauseKind: clauseKind,
            projectionInfo: projectionInfo,
            contextClassName: "TestDbContext",
            contextNamespace: "Test");
    }

    private static QueryAnalysisContext CreateContext(RawCallSite site, EntityInfo? entity = null)
    {
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

    // -- QRA102: SingleValueInRule --

    [Test]
    public void QRA102_SingleParameterIn_Reports()
    {
        var rule = new SingleValueInRule();
        var context = CreateWhereContext("t0.\"UserId\" IN (@p0)", paramCount: 1);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA102"));
    }

    [Test]
    public void QRA102_MultipleParameterIn_NoReport()
    {
        var rule = new SingleValueInRule();
        var context = CreateWhereContext("t0.\"UserId\" IN (@p0, @p1)", paramCount: 2);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA103: TautologicalConditionRule --

    [Test]
    public void QRA103_OneEqualsOne_Reports()
    {
        var rule = new TautologicalConditionRule();
        var context = CreateWhereContext("1 = 1");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA103_SameColumnBothSides_Reports()
    {
        var rule = new TautologicalConditionRule();
        var context = CreateWhereContext("t0.\"Name\" = t0.\"Name\"");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA103_NormalCondition_NoReport()
    {
        var rule = new TautologicalConditionRule();
        var context = CreateWhereContext("t0.\"IsActive\" = @p0");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA104: ContradictoryConditionRule --

    [Test]
    public void QRA104_ConflictingRanges_Reports()
    {
        var rule = new ContradictoryConditionRule();
        var context = CreateWhereContext("t0.\"Age\" > 50 AND t0.\"Age\" < 30");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void QRA104_ValidRange_NoReport()
    {
        var rule = new ContradictoryConditionRule();
        var context = CreateWhereContext("t0.\"Age\" > 18 AND t0.\"Age\" < 65");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void QRA104_ConflictingEquals_Reports()
    {
        var rule = new ContradictoryConditionRule();
        var context = CreateWhereContext("t0.\"Status\" = 1 AND t0.\"Status\" = 2");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    // -- QRA105: RedundantConditionRule --

    [Test]
    public void QRA105_WeakerGreaterThan_Reports()
    {
        var rule = new RedundantConditionRule();
        var context = CreateWhereContext("t0.\"Age\" > 50 AND t0.\"Age\" > 30");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("QRA105"));
    }

    [Test]
    public void QRA105_DifferentColumns_NoReport()
    {
        var rule = new RedundantConditionRule();
        var context = CreateWhereContext("t0.\"Age\" > 50 AND t0.\"Score\" > 30");
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    // -- QRA106: NullableWithoutNullCheckRule --

    [Test]
    public void QRA106_NullableColumnEquality_Reports()
    {
        var rule = new NullableWithoutNullCheckRule();
        var entity = CreateEntityWithNullableColumn("Email");
        var context = CreateWhereContext("t0.\"Email\" = @p0", paramCount: 1, entity: entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("Email"));
    }

    [Test]
    public void QRA106_NullableWithIsNull_NoReport()
    {
        var rule = new NullableWithoutNullCheckRule();
        var entity = CreateEntityWithNullableColumn("Email");
        var context = CreateWhereContext("t0.\"Email\" = @p0 AND t0.\"Email\" IS NOT NULL", paramCount: 1, entity: entity);
        var diagnostics = rule.Analyze(context).ToList();
        Assert.That(diagnostics, Is.Empty);
    }

    private static EntityInfo CreateEntityWithNullableColumn(string columnName)
    {
        var modifiers = new ColumnModifiers();
        var columns = new List<ColumnInfo>
        {
            new ColumnInfo("UserId", "UserId", "int", "System.Int32", false, ColumnKind.PrimaryKey, null, modifiers),
            new ColumnInfo(columnName, columnName, "string", "System.String", true, ColumnKind.Standard, null, modifiers),
        };
        return new EntityInfo("User", "UserSchema", "Test", "users", NamingStyleKind.Exact, columns,
            new List<NavigationInfo>(), new List<IndexInfo>(), Location.None, null, null);
    }
}
