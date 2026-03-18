using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001
#pragma warning disable CS0162 // Unreachable code — conditional branching tests use if(true)/if(false) intentionally

/// <summary>
/// Tests that ToDiagnostics() returns correct clause metadata, parameter values,
/// and IsActive flags for both unconditional and conditional chains.
/// </summary>
[TestFixture]
internal class CrossDialectDiagnosticsTests
{
    private MockDbConnection _connection = null!;
    private TestDbContext _db = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new MockDbConnection();
        _db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region Clauses — Unconditional

    [Test]
    public void ToDiagnostics_UnconditionalWhere_HasClausesWithIsActiveTrue()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.Clauses, Is.Not.Empty);
        var whereClauses = diag.Clauses.Where(c => c.ClauseType == "Where").ToList();
        Assert.That(whereClauses, Has.Count.EqualTo(1));
        Assert.That(whereClauses[0].IsConditional, Is.False);
        Assert.That(whereClauses[0].IsActive, Is.True);
        Assert.That(whereClauses[0].SqlFragment, Does.Contain("IsActive"));
    }

    [Test]
    public void ToDiagnostics_MultipleUnconditionalClauses_AllActive()
    {
        var diag = _db.Users()
            .Where(u => u.IsActive)
            .OrderBy(u => u.UserName)
            .ToDiagnostics();

        var whereClauses = diag.Clauses.Where(c => c.ClauseType == "Where").ToList();
        var orderByClauses = diag.Clauses.Where(c => c.ClauseType == "OrderBy").ToList();

        Assert.That(whereClauses, Has.Count.EqualTo(1));
        Assert.That(orderByClauses, Has.Count.EqualTo(1));
        Assert.That(whereClauses[0].IsActive, Is.True);
        Assert.That(orderByClauses[0].IsActive, Is.True);
    }

    #endregion

    #region Clauses — Conditional (if/else branching)

    [Test]
    public void ToDiagnostics_ConditionalWhereActive_ClauseIsConditionalAndActive()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("IsActive"));

        var conditionalClauses = diag.Clauses.Where(c => c.IsConditional).ToList();
        Assert.That(conditionalClauses, Has.Count.GreaterThanOrEqualTo(1));

        var activeConditional = conditionalClauses.First(c => c.SqlFragment.Contains("IsActive"));
        Assert.That(activeConditional.IsActive, Is.True);
    }

    [Test]
    public void ToDiagnostics_ConditionalWhereInactive_ClauseIsConditionalAndNotActive()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));

        var conditionalClauses = diag.Clauses.Where(c => c.IsConditional).ToList();
        Assert.That(conditionalClauses, Has.Count.GreaterThanOrEqualTo(1));

        var inactiveConditional = conditionalClauses.First(c => c.SqlFragment.Contains("IsActive"));
        Assert.That(inactiveConditional.IsActive, Is.False);
    }

    #endregion

    #region Parameters

    [Test]
    public void ToDiagnostics_WithCapturedParameter_ParametersContainValue()
    {
        var name = "john";
        var diag = _db.Users().Where(u => u.UserName == name).ToDiagnostics();

        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diag.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo("john"));
    }

    [Test]
    public void ToDiagnostics_WithMultipleParameters_AllParametersPresent()
    {
        var name = "john";
        var id = 42;
        var diag = _db.Users()
            .Where(u => u.UserName == name)
            .Where(u => u.UserId == id)
            .ToDiagnostics();

        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(diag.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo("john"));
        Assert.That(diag.Parameters[1].Name, Is.EqualTo("@p1"));
        Assert.That(diag.Parameters[1].Value, Is.EqualTo(42));
    }

    #endregion

    #region Optimization Tier

    [Test]
    public void ToDiagnostics_PrebuiltChain_ReportsPrebuiltDispatchTier()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
    }

    [Test]
    public void ToDiagnostics_PrebuiltChain_ReportsCorrectDialect()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.Dialect, Is.EqualTo(SqlDialect.SQLite));
    }

    #endregion

    #region Conditional + Parameters Combined

    [Test]
    public void ToDiagnostics_ConditionalWithParamActive_ParameterIncluded()
    {
        var name = "john";
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.UserName == name);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UserName"));
        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void ToDiagnostics_ConditionalWithParamInactive_SqlExcludesClause()
    {
        var name = "john";
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.UserName == name);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("UserName"));
    }

    #endregion
}
