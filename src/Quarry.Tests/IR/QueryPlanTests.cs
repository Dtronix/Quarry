using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
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

    private static Quarry.Generators.IR.QueryPlan CreateSimpleSelectPlan(string whereColumn = "age")
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
            groupByExprs: System.Array.Empty<SqlExpr>(),
            havingExprs: System.Array.Empty<SqlExpr>(),
            projection: new SelectProjection(ProjectionKind.Entity, "User",
                System.Array.Empty<ProjectedColumn>(), isIdentity: true),
            pagination: null,
            isDistinct: false,
            setTerms: System.Array.Empty<SetTerm>(),
            insertColumns: System.Array.Empty<InsertColumn>(),
            conditionalTerms: System.Array.Empty<ConditionalTerm>(),
            possibleMasks: System.Array.Empty<ulong>(),
            parameters: System.Array.Empty<QueryParameter>(),
            tier: Quarry.Generators.Models.OptimizationTier.PrebuiltDispatch);
    }
}
