using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

[TestFixture]
public class QueryPlanTests
{
    #region TableRef Tests

    [Test]
    public void TableRef_Equality()
    {
        var a = new TableRef("users", "public", "t0");
        var b = new TableRef("users", "public", "t0");
        var c = new TableRef("orders", "public", "t1");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    #region WhereTerm Tests

    [Test]
    public void WhereTerm_Equality()
    {
        var condition = new BinaryOpExpr(
            new ResolvedColumnExpr("\"age\""),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));

        var a = new WhereTerm(condition, bitIndex: null);
        var b = new WhereTerm(condition, bitIndex: null);

        Assert.That(a.Equals(b), Is.True);
    }

    [Test]
    public void WhereTerm_ConditionalBitIndex()
    {
        var condition = new ResolvedColumnExpr("\"name\"");
        var a = new WhereTerm(condition, bitIndex: 0);
        var b = new WhereTerm(condition, bitIndex: 1);

        Assert.That(a.Equals(b), Is.False);
    }

    #endregion

    #region OrderTerm Tests

    [Test]
    public void OrderTerm_Equality()
    {
        var col = new ResolvedColumnExpr("\"name\"");
        var a = new OrderTerm(col, isDescending: true);
        var b = new OrderTerm(col, isDescending: true);
        var c = new OrderTerm(col, isDescending: false);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    #region PaginationPlan Tests

    [Test]
    public void PaginationPlan_LiteralEquality()
    {
        var a = new PaginationPlan(literalLimit: 10, literalOffset: 20);
        var b = new PaginationPlan(literalLimit: 10, literalOffset: 20);
        var c = new PaginationPlan(literalLimit: 10, literalOffset: 30);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    [Test]
    public void PaginationPlan_ParameterEquality()
    {
        var a = new PaginationPlan(limitParamIndex: 0, offsetParamIndex: 1);
        var b = new PaginationPlan(limitParamIndex: 0, offsetParamIndex: 1);

        Assert.That(a.Equals(b), Is.True);
    }

    #endregion

    #region QueryParameter Tests

    [Test]
    public void QueryParameter_Equality()
    {
        var a = new QueryParameter(0, "string", "name", isCaptured: true, expressionPath: "Body.Right");
        var b = new QueryParameter(0, "string", "name", isCaptured: true, expressionPath: "Body.Right");
        var c = new QueryParameter(1, "int", "age");

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    #region QueryPlan Tests

    [Test]
    public void QueryPlan_SimpleSelect_Equality()
    {
        var plan1 = CreateSimpleSelectPlan();
        var plan2 = CreateSimpleSelectPlan();

        Assert.That(plan1.Equals(plan2), Is.True);
        Assert.That(plan1.GetHashCode(), Is.EqualTo(plan2.GetHashCode()));
    }

    [Test]
    public void QueryPlan_DifferentWhere_NotEqual()
    {
        var plan1 = CreateSimpleSelectPlan();
        var plan2 = CreateSimpleSelectPlan(whereColumn: "email");

        Assert.That(plan1.Equals(plan2), Is.False);
    }

    [Test]
    public void QueryPlan_DifferentGroupByExprs_NotEqual()
    {
        var plan1 = CreateSimpleSelectPlan(groupByExprs: new SqlExpr[] { new ResolvedColumnExpr("\"name\"") });
        var plan2 = CreateSimpleSelectPlan(groupByExprs: new SqlExpr[] { new ResolvedColumnExpr("\"age\"") });

        Assert.That(plan1.Equals(plan2), Is.False);
    }

    [Test]
    public void QueryPlan_DifferentHavingExprs_NotEqual()
    {
        var having1 = new BinaryOpExpr(new ResolvedColumnExpr("\"count\""), SqlBinaryOperator.GreaterThan, new LiteralExpr("5", "int"));
        var having2 = new BinaryOpExpr(new ResolvedColumnExpr("\"count\""), SqlBinaryOperator.GreaterThan, new LiteralExpr("10", "int"));

        var plan1 = CreateSimpleSelectPlan(havingExprs: new SqlExpr[] { having1 });
        var plan2 = CreateSimpleSelectPlan(havingExprs: new SqlExpr[] { having2 });

        Assert.That(plan1.Equals(plan2), Is.False);
    }

    [Test]
    public void QueryPlan_DifferentPossibleMasks_NotEqual()
    {
        var plan1 = CreateSimpleSelectPlan(possibleMasks: new ulong[] { 0, 1 });
        var plan2 = CreateSimpleSelectPlan(possibleMasks: new ulong[] { 0, 1, 2, 3 });

        Assert.That(plan1.Equals(plan2), Is.False);
    }

    [Test]
    public void QueryPlan_DifferentUnmatchedMethodNames_NotEqual()
    {
        var plan1 = CreateSimpleSelectPlan(unmatchedMethodNames: new[] { "AddWhereClause" });
        var plan2 = CreateSimpleSelectPlan(unmatchedMethodNames: null);

        Assert.That(plan1.Equals(plan2), Is.False);
    }

    #endregion

    #region AssembledPlan Equality Tests

    [Test]
    public void AssembledPlan_DifferentSqlVariants_NotEqual()
    {
        var plan = CreateSimpleSelectPlan();
        var raw = CreateMinimalRaw();
        var entity = Generators.IR.EntityRef.FromEntityInfo(CreateTestEntity());
        var bound = new BoundCallSite(raw, "Ctx", "App", GenSqlDialect.PostgreSQL, "users", null, entity);
        var site = new TranslatedCallSite(bound);

        var variants1 = new Dictionary<ulong, AssembledSqlVariant>
        {
            { 0, new AssembledSqlVariant("SELECT * FROM users", 0) }
        };
        var variants2 = new Dictionary<ulong, AssembledSqlVariant>
        {
            { 0, new AssembledSqlVariant("SELECT * FROM users WHERE age > 18", 1) }
        };

        var a = new AssembledPlan(plan, variants1, null, 0, site, System.Array.Empty<TranslatedCallSite>(), "User", null, GenSqlDialect.PostgreSQL);
        var b = new AssembledPlan(plan, variants2, null, 0, site, System.Array.Empty<TranslatedCallSite>(), "User", null, GenSqlDialect.PostgreSQL);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void AssembledPlan_DifferentEntitySchemaNamespace_NotEqual()
    {
        var plan = CreateSimpleSelectPlan();
        var raw = CreateMinimalRaw();
        var entity = Generators.IR.EntityRef.FromEntityInfo(CreateTestEntity());
        var bound = new BoundCallSite(raw, "Ctx", "App", GenSqlDialect.PostgreSQL, "users", null, entity);
        var site = new TranslatedCallSite(bound);
        var variants = new Dictionary<ulong, AssembledSqlVariant>();

        var a = new AssembledPlan(plan, variants, null, 0, site, System.Array.Empty<TranslatedCallSite>(), "User", null, GenSqlDialect.PostgreSQL, entitySchemaNamespace: "App.Schema");
        var b = new AssembledPlan(plan, variants, null, 0, site, System.Array.Empty<TranslatedCallSite>(), "User", null, GenSqlDialect.PostgreSQL, entitySchemaNamespace: "App.Models");

        Assert.That(a.Equals(b), Is.False);
    }

    #endregion

    #region AssembledSqlVariant Tests

    [Test]
    public void AssembledSqlVariant_Equality()
    {
        var a = new AssembledSqlVariant("SELECT * FROM users WHERE age > 18", 0);
        var b = new AssembledSqlVariant("SELECT * FROM users WHERE age > 18", 0);
        var c = new AssembledSqlVariant("SELECT * FROM users", 0);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    #region SelectProjection Tests

    [Test]
    public void SelectProjection_IdentityEquality()
    {
        var a = new SelectProjection(ProjectionKind.Entity, "User",
            System.Array.Empty<ProjectedColumn>(), isIdentity: true);
        var b = new SelectProjection(ProjectionKind.Entity, "User",
            System.Array.Empty<ProjectedColumn>(), isIdentity: true);

        Assert.That(a.Equals(b), Is.True);
    }

    #endregion

    #region InsertColumn Tests

    [Test]
    public void InsertColumn_Equality()
    {
        var a = new InsertColumn("\"name\"", 0);
        var b = new InsertColumn("\"name\"", 0);
        var c = new InsertColumn("\"email\"", 1);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals(c), Is.False);
    }

    #endregion

    private static Quarry.Generators.IR.QueryPlan CreateSimpleSelectPlan(
        string whereColumn = "age",
        IReadOnlyList<SqlExpr>? groupByExprs = null,
        IReadOnlyList<SqlExpr>? havingExprs = null,
        IReadOnlyList<ulong>? possibleMasks = null,
        IReadOnlyList<string>? unmatchedMethodNames = null)
    {
        var table = new TableRef("users");
        var whereExpr = new BinaryOpExpr(
            new ResolvedColumnExpr($"\"{whereColumn}\""),
            SqlBinaryOperator.GreaterThan,
            new LiteralExpr("18", "int"));

        return new Quarry.Generators.IR.QueryPlan(
            kind: Quarry.Generators.Models.QueryKind.Select,
            primaryTable: table,
            joins: System.Array.Empty<JoinPlan>(),
            whereTerms: new[] { new WhereTerm(whereExpr) },
            orderTerms: System.Array.Empty<OrderTerm>(),
            groupByExprs: groupByExprs ?? System.Array.Empty<SqlExpr>(),
            havingExprs: havingExprs ?? System.Array.Empty<SqlExpr>(),
            projection: new SelectProjection(ProjectionKind.Entity, "User",
                System.Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: System.Array.Empty<SetTerm>(),
            insertColumns: System.Array.Empty<InsertColumn>(),
            conditionalTerms: System.Array.Empty<ConditionalTerm>(),
            possibleMasks: possibleMasks ?? System.Array.Empty<ulong>(),
            parameters: System.Array.Empty<QueryParameter>(),
            tier: Quarry.Generators.Models.OptimizationTier.PrebuiltDispatch,
            unmatchedMethodNames: unmatchedMethodNames);
    }

    private static RawCallSite CreateMinimalRaw()
    {
        return new RawCallSite(
            methodName: "ExecuteFetchAllAsync",
            filePath: "test.cs",
            line: 1,
            column: 1,
            uniqueId: "test_id",
            kind: InterceptorKind.ExecuteFetchAll,
            builderKind: BuilderKind.Query,
            entityTypeName: "User",
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: null,
            interceptableLocationVersion: 1,
            location: default);
    }

    private static EntityInfo CreateTestEntity()
    {
        var mods = new ColumnModifiers();
        return new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, mods),
                new ColumnInfo("Age", "age", "int", "int", false, ColumnKind.Standard, null, mods, isValueType: true),
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None);
    }
}
