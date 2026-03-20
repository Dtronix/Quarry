using Quarry;
using Quarry.Tests.Samples;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable CS0162 // Unreachable code — conditional branching tests use if(true)/if(false) intentionally

/// <summary>
/// Verifies that conditional (variable-based) chains produce correct SQL via ToDiagnostics()
/// for Select, Update, and Delete operations.  Each test exercises the branch-active and
/// branch-inactive variant confirming the SQL includes/excludes the conditional clause.
/// </summary>
[TestFixture]
internal class ConditionalChainSqlTests
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

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional Where (active / inactive)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalWhere_Active_IncludesSql()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("IsActive"));
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
        Assert.That(diag.IsCarrierOptimized, Is.True);

        var cond = diag.Clauses.Where(c => c.IsConditional && c.SqlFragment.Contains("IsActive")).ToList();
        Assert.That(cond, Has.Count.EqualTo(1));
        Assert.That(cond[0].IsActive, Is.True);
    }

    [Test]
    public void Select_ConditionalWhere_Inactive_ExcludesSql()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
        Assert.That(diag.IsCarrierOptimized, Is.True);

        var cond = diag.Clauses.Where(c => c.IsConditional && c.SqlFragment.Contains("IsActive")).ToList();
        Assert.That(cond, Has.Count.EqualTo(1));
        Assert.That(cond[0].IsActive, Is.False);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional OrderBy (active / inactive)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalOrderBy_Active_IncludesSql()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => u.IsActive);
        if (true)
        {
            query = query.OrderBy(u => u.UserName);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("ORDER BY"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Select_ConditionalOrderBy_Inactive_ExcludesSql()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => u.IsActive);
        if (false)
        {
            query = query.OrderBy(u => u.UserName);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("ORDER BY"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — mutually exclusive OrderBy (if/else)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_MutuallyExclusiveOrderBy_IfBranch()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => u.IsActive);
        if (true)
        {
            query = query.OrderBy(u => u.UserName);
        }
        else
        {
            query = query.OrderBy(u => u.UserId);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UserName"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Select_MutuallyExclusiveOrderBy_ElseBranch()
    {
        IQueryBuilder<User> query = _db.Users().Where(u => u.IsActive);
        if (false)
        {
            query = query.OrderBy(u => u.UserName);
        }
        else
        {
            query = query.OrderBy(u => u.UserId);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("\"UserId\""));
        Assert.That(diag.Sql, Does.Not.Contain("UserName"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  SELECT — conditional Where with captured parameter
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Select_ConditionalWhere_CapturedParam_Active()
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
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Select_ConditionalWhere_CapturedParam_Inactive()
    {
        var name = "john";
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.UserName == name);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Not.Contain("UserName"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set<TValue> — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetValue_ConditionalWhere_Active()
    {
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName, "Updated").All();
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Contain("IsActive"));
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Update_SetValue_ConditionalWhere_Inactive()
    {
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName, "Updated").All();
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) literal — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_Literal_ConditionalWhere_Active()
    {
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = "Patched").All();
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Update_SetAction_Literal_ConditionalWhere_Inactive()
    {
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = "Patched").All();
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) captured — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_Captured_ConditionalWhere_Active()
    {
        var name = "ActionCapture";
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = name).All();
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
        Assert.That(diag.Parameters.Any(p => "ActionCapture".Equals(p.Value)), Is.True,
            "Captured variable value should appear in parameters");
    }

    [Test]
    public void Update_SetAction_Captured_ConditionalWhere_Inactive()
    {
        var name = "ActionCapture";
        IExecutableUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = name).All();
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("UPDATE"));
        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  DELETE — conditional Where
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Delete_ConditionalWhere_Active()
    {
        IExecutableDeleteBuilder<User> query = _db.Users().Delete().All();
        if (true)
        {
            query = query.Where(u => u.IsActive == false);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("DELETE"));
        Assert.That(diag.Sql, Does.Contain("IsActive"));
        Assert.That(diag.Tier, Is.EqualTo(DiagnosticOptimizationTier.PrebuiltDispatch));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Delete_ConditionalWhere_Inactive()
    {
        IExecutableDeleteBuilder<User> query = _db.Users().Delete().All();
        if (false)
        {
            query = query.Where(u => u.IsActive == false);
        }
        var diag = query.ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("DELETE"));
        Assert.That(diag.Sql, Does.Not.Contain("IsActive"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    //  UPDATE Set(Action<T>) — conditional additional Set(Action<T>)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void Update_SetAction_ConditionalAdditionalSetAction_Active()
    {
        IUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = "x");
        if (true)
        {
            query = query.Set(u => u.IsActive = false);
        }
        var diag = query.All().ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
        Assert.That(diag.Sql, Does.Contain("\"IsActive\""));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    [Test]
    public void Update_SetAction_ConditionalAdditionalSetAction_Inactive()
    {
        IUpdateBuilder<User> query = _db.Users().Update().Set(u => u.UserName = "x");
        if (false)
        {
            query = query.Set(u => u.IsActive = false);
        }
        var diag = query.All().ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("\"UserName\""));
        Assert.That(diag.Sql, Does.Not.Contain("\"IsActive\""));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }
}
