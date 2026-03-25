using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

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

        Assert.That(diag.Sql, Does.Contain("\"IsActive\" = 1"));

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

        Assert.That(diag.Sql, Does.Not.Contain("\"IsActive\" = 1"));

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

        // Top-level Parameters derived from active clause params
        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diag.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo("john"));

        // Per-clause Parameters
        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where" && c.SqlFragment.Contains("UserName"));
        Assert.That(whereClause.Parameters, Has.Count.EqualTo(1));
        Assert.That(whereClause.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(whereClause.Parameters[0].Value, Is.EqualTo("john"));
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

        // Top-level Parameters derived from active clause params
        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(diag.Parameters[0].Name, Is.EqualTo("@p0"));
        Assert.That(diag.Parameters[0].Value, Is.EqualTo("john"));
        Assert.That(diag.Parameters[1].Name, Is.EqualTo("@p1"));
        Assert.That(diag.Parameters[1].Value, Is.EqualTo(42));

        // Each Where clause owns its own parameter
        var whereClauses = diag.Clauses.Where(c => c.ClauseType == "Where").ToList();
        Assert.That(whereClauses[0].Parameters, Has.Count.EqualTo(1));
        Assert.That(whereClauses[0].Parameters[0].Value, Is.EqualTo("john"));
        Assert.That(whereClauses[1].Parameters, Has.Count.EqualTo(1));
        Assert.That(whereClauses[1].Parameters[0].Value, Is.EqualTo(42));
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

        Assert.That(diag.Sql, Does.Contain("\"UserName\" = @p"));

        // Top-level includes active clause params
        Assert.That(diag.Parameters, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(diag.Parameters.Any(p => p.Value is string s && s == "john"), Is.True);

        // Per-clause param on the conditional clause
        var conditionalWhere = diag.Clauses.First(c => c.IsConditional && c.SqlFragment.Contains("UserName"));
        Assert.That(conditionalWhere.IsActive, Is.True);
        Assert.That(conditionalWhere.Parameters, Has.Count.EqualTo(1));
        Assert.That(conditionalWhere.Parameters[0].Value, Is.EqualTo("john"));
    }

    [Test]
    public void ToDiagnostics_ConditionalWithParamInactive_TopLevelExcludesInactiveParams()
    {
        var name = "john";
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.UserName == name);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("\"UserName\" = @p"));

        // Top-level Parameters excludes inactive clause params
        Assert.That(diag.Parameters.Any(p => p.Name == "@p0"), Is.False);

        // The clause itself still has its parameter metadata
        var conditionalWhere = diag.Clauses.First(c => c.IsConditional && c.SqlFragment.Contains("UserName"));
        Assert.That(conditionalWhere.IsActive, Is.False);
        Assert.That(conditionalWhere.Parameters, Has.Count.EqualTo(1));
    }

    #endregion

    #region Expanded Diagnostic Properties

    [Test]
    public void ToDiagnostics_PrebuiltChain_HasTierReason()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.TierReason, Is.Not.Null.And.Not.Empty);
        Assert.That(diag.TierReason, Does.Contain("unconditional"));
    }

    [Test]
    public void ToDiagnostics_PrebuiltChain_DisqualifyReasonIsNull()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.DisqualifyReason, Is.Null);
    }

    [Test]
    public void ToDiagnostics_PrebuiltChain_HasSqlVariants()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.SqlVariants, Is.Not.Null);
        Assert.That(diag.SqlVariants!.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(diag.SqlVariants.Values.First().Sql, Does.Contain("SELECT"));
        Assert.That(diag.SqlVariants.Values.First().ParameterCount, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ToDiagnostics_ConditionalChain_HasMultipleSqlVariants()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.SqlVariants, Is.Not.Null);
        Assert.That(diag.SqlVariants!.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(diag.ConditionalBitCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(diag.TierReason, Does.Contain("conditional"));
    }

    [Test]
    public void ToDiagnostics_PrebuiltChain_HasCarrierClassName()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.CarrierClassName, Is.Not.Null.And.Not.Empty);
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void ToDiagnostics_UnconditionalChain_ActiveMaskIsZero()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.ActiveMask, Is.EqualTo(0UL));
        Assert.That(diag.ConditionalBitCount, Is.EqualTo(0));
    }

    [Test]
    public void ToDiagnostics_BasicSelect_IsDistinctIsFalse()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.IsDistinct, Is.False);
    }

    [Test]
    public void ToDiagnostics_WithDistinct_IsDistinctIsTrue()
    {
        var diag = _db.Users().Distinct().ToDiagnostics();

        Assert.That(diag.IsDistinct, Is.True);
    }

    [Test]
    public void ToDiagnostics_WithLimit_HasLimitValue()
    {
        var diag = _db.Users().Where(u => u.IsActive).Limit(10).ToDiagnostics();

        Assert.That(diag.Limit, Is.EqualTo(10));
    }

    [Test]
    public void ToDiagnostics_WithOffset_HasOffsetValue()
    {
        var diag = _db.Users().Where(u => u.IsActive).Offset(5).ToDiagnostics();

        Assert.That(diag.Offset, Is.EqualTo(5));
    }

    [Test]
    public void ToDiagnostics_NoLimitOrOffset_LimitAndOffsetAreNull()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        Assert.That(diag.Limit, Is.Null);
        Assert.That(diag.Offset, Is.Null);
    }

    [Test]
    public void ToDiagnostics_BareToDiagnostics_HasBasicMetadata()
    {
        var diag = _db.Users().ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("SELECT"));
        Assert.That(diag.TableName, Is.EqualTo("users"));
        Assert.That(diag.Kind, Is.EqualTo(DiagnosticQueryKind.Select));
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
        Assert.That(diag.IsCarrierOptimized, Is.True);
        Assert.That(diag.TierReason, Is.Not.Null);
        Assert.That(diag.CarrierClassName, Is.Not.Null);
    }

    [Test]
    public void ToDiagnostics_AllParametersContainsFullMetadata()
    {
        var name = "john";
        var diag = _db.Users().Where(u => u.UserName == name).ToDiagnostics();

        Assert.That(diag.AllParameters, Is.Not.Empty);
        Assert.That(diag.AllParameters.Count, Is.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Expanded DiagnosticParameter Metadata

    [Test]
    public void ToDiagnostics_ParameterHasTypeName()
    {
        var name = "john";
        var diag = _db.Users().Where(u => u.UserName == name).ToDiagnostics();

        var param = diag.AllParameters.First();
        Assert.That(param.TypeName, Is.Not.Null.And.Not.Empty);
        Assert.That(param.TypeName, Does.Contain("string").IgnoreCase);
    }

    [Test]
    public void ToDiagnostics_EnumParameter_HasIsEnumTrue()
    {
        var priority = OrderPriority.High;
        var diag = _db.Orders().Where(o => o.Priority == priority).ToDiagnostics();

        var param = diag.AllParameters.First();
        Assert.That(param.IsEnum, Is.True);
    }

    [Test]
    public void ToDiagnostics_SensitiveParameter_HasIsSensitiveTrue()
    {
        var secret = "classified";
        var diag = _db.Widgets().Where(w => w.Secret == secret).ToDiagnostics();

        var param = diag.AllParameters.First();
        Assert.That(param.IsSensitive, Is.True);
    }

    [Test]
    public void ToDiagnostics_NonConditionalParameter_IsConditionalFalse()
    {
        var name = "john";
        var diag = _db.Users().Where(u => u.UserName == name).ToDiagnostics();

        var param = diag.AllParameters.First();
        Assert.That(param.IsConditional, Is.False);
        Assert.That(param.ConditionalBitIndex, Is.Null);
    }

    [Test]
    public void ToDiagnostics_ConditionalParameter_IsConditionalTrueWithBitIndex()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        var name = "john";
        if (true)
        {
            query = query.Where(u => u.UserName == name);
        }
        var diag = query.ToDiagnostics();

        var conditionalParams = diag.AllParameters.Where(p => p.IsConditional).ToList();
        Assert.That(conditionalParams, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(conditionalParams[0].ConditionalBitIndex, Is.Not.Null);
    }

    #endregion

    #region Expanded ClauseDiagnostic Metadata

    [Test]
    public void ToDiagnostics_ClauseHasSourceLocation()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where");
        Assert.That(whereClause.SourceLocation, Is.Not.Null);
        Assert.That(whereClause.SourceLocation!.FilePath, Is.Not.Null.And.Not.Empty);
        Assert.That(whereClause.SourceLocation.Line, Is.GreaterThan(0));
        Assert.That(whereClause.SourceLocation.Column, Is.GreaterThan(0));
    }

    [Test]
    public void ToDiagnostics_ClauseSourceLocation_DoesNotContainAbsolutePath()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where");
        Assert.That(whereClause.SourceLocation, Is.Not.Null);
        // Should be project-relative, not absolute — no drive letter or root slash
        Assert.That(whereClause.SourceLocation!.FilePath, Does.Not.Match(@"^[A-Z]:\\"));
        Assert.That(whereClause.SourceLocation.FilePath, Does.Not.StartWith("/"));
    }

    [Test]
    public void ToDiagnostics_NonConditionalClause_BitIndexIsNull()
    {
        var diag = _db.Users().Where(u => u.IsActive).ToDiagnostics();

        var whereClause = diag.Clauses.First(c => c.ClauseType == "Where");
        Assert.That(whereClause.ConditionalBitIndex, Is.Null);
        Assert.That(whereClause.BranchKind, Is.Null);
    }

    [Test]
    public void ToDiagnostics_ConditionalClause_HasBitIndexAndBranchKind()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        var conditionalClauses = diag.Clauses.Where(c => c.IsConditional).ToList();
        Assert.That(conditionalClauses, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(conditionalClauses[0].ConditionalBitIndex, Is.Not.Null);
        Assert.That(conditionalClauses[0].BranchKind, Is.Not.Null);
    }

    #endregion
}
