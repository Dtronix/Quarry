using Microsoft.Data.Sqlite;
using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;

#pragma warning disable QRY001

/// <summary>
/// Layer 8: End-to-end SQLite integration tests for TypeMapping.
/// Verifies that Money values are correctly stored as decimal and reconstituted via FromDb.
/// </summary>
[TestFixture]
internal class TypeMappingIntegrationTests
{
    private SqliteConnection _connection = null!;
    private TestDbContext _db = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Ensure MoneyMapping is registered in TypeMappingRegistry before any test runs.
        // In production, the interceptor's static field (private static readonly MoneyMapping _mapper = new())
        // handles this. In tests, we register eagerly so fallback-path tests work regardless of order.
        _ = new MoneyMapping();

        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        await CreateSchema();
        await SeedData();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _connection.DisposeAsync();
    }

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private async Task CreateSchema()
    {
        await ExecuteSqlAsync("""
            CREATE TABLE IF NOT EXISTS "users" (
                "UserId" INTEGER PRIMARY KEY,
                "UserName" TEXT NOT NULL,
                "Email" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                "LastLogin" TEXT
            )
            """);

        await ExecuteSqlAsync("""
            CREATE TABLE "accounts" (
                "AccountId" INTEGER PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "AccountName" TEXT NOT NULL,
                "Balance" REAL NOT NULL,
                "credit_limit" REAL NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY ("UserId") REFERENCES "users"("UserId")
            )
            """);
    }

    private async Task SeedData()
    {
        await ExecuteSqlAsync("""
            INSERT INTO "users" ("UserId", "UserName", "IsActive", "CreatedAt") VALUES
                (1, 'Alice', 1, '2024-01-15 00:00:00'),
                (2, 'Bob',   1, '2024-02-20 00:00:00')
            """);

        await ExecuteSqlAsync("""
            INSERT INTO "accounts" ("AccountId", "UserId", "AccountName", "Balance", "credit_limit", "IsActive") VALUES
                (1, 1, 'Savings',  1000.50, 5000.00, 1),
                (2, 1, 'Checking', 250.75,  1000.00, 1),
                (3, 2, 'Savings',  500.00,  2000.00, 0)
            """);
    }

    #region Insert Tests

    [Test]
    public async Task Insert_AccountWithMoney_StoresDecimalInDb()
    {
        var account = new Account
        {
            UserId = 1,
            AccountName = "Investment",
            Balance = new Money(999.99m),
            CreditLimit = new Money(10000m),
            IsActive = true
        };

        var rowsAffected = await _db.Insert(account).ExecuteNonQueryAsync();
        Assert.That(rowsAffected, Is.EqualTo(1), "Should insert one row");

        var id = Convert.ToInt64(await ExecuteScalarAsync("SELECT last_insert_rowid()"));

        // Verify the raw stored value is a decimal number
        var rawBalance = await ExecuteScalarAsync(
            $"SELECT \"Balance\" FROM \"accounts\" WHERE \"AccountId\" = {id}");
        Assert.That(rawBalance, Is.Not.Null);
        Assert.That(Convert.ToDecimal(rawBalance), Is.EqualTo(999.99m),
            "Balance should be stored as a decimal via ToDb()");

        var rawCreditLimit = await ExecuteScalarAsync(
            $"SELECT \"credit_limit\" FROM \"accounts\" WHERE \"AccountId\" = {id}");
        Assert.That(rawCreditLimit, Is.Not.Null);
        Assert.That(Convert.ToDecimal(rawCreditLimit), Is.EqualTo(10000m),
            "CreditLimit should be stored as a decimal via ToDb()");
    }

    #endregion

    #region Select Tests

    [Test]
    public async Task Select_TupleWithAllMappedColumns_ReconstitutesMoneyFromDb()
    {
        var results = await _db.Accounts()
            .Where(a => a.AccountId == 1)
            .Select(a => (a.AccountId, a.AccountName, a.Balance, a.CreditLimit, a.IsActive))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(1000.50m)),
            "Money should be reconstituted from decimal via FromDb()");
        Assert.That(results[0].CreditLimit, Is.EqualTo(new Money(5000.00m)),
            "CreditLimit should be reconstituted from decimal via FromDb()");
    }

    [Test]
    public async Task Select_AllAccounts_ReturnsCorrectMoneyValues()
    {
        var results = await _db.Accounts()
            .Select(a => (a.AccountId, a.AccountName, a.Balance))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThanOrEqualTo(3));

        var savings = results.First(r => r.AccountId == 1);
        Assert.That(savings.Balance, Is.EqualTo(new Money(1000.50m)));

        var checking = results.First(r => r.AccountId == 2);
        Assert.That(checking.Balance, Is.EqualTo(new Money(250.75m)));
    }

    [Test]
    public async Task Select_TupleWithMappedColumn_ReturnsMoney()
    {
        var results = await _db.Accounts()
            .Where(a => a.AccountId == 1)
            .Select(a => (a.AccountName, a.Balance))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(1000.50m)));
    }

    #endregion

    #region Where Tests

    [Test]
    public async Task Where_NonMappedColumn_FiltersCorrectly()
    {
        var results = await _db.Accounts()
            .Where(a => a.IsActive == true)
            .Select(a => (a.AccountId, a.AccountName, a.Balance, a.IsActive))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.GreaterThanOrEqualTo(2),
            "Should return only active accounts");
        Assert.That(results.All(r => r.IsActive), Is.True);
    }

    [Test]
    public async Task Where_ByAccountId_ReturnsSpecificAccount()
    {
        var results = await _db.Accounts()
            .Where(a => a.AccountId == 2)
            .Select(a => (a.AccountId, a.AccountName, a.Balance))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountName, Is.EqualTo("Checking"));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(250.75m)));
    }

    #endregion

    #region Runtime Fallback Parameter Tests

    [Test]
    public async Task FallbackPath_MoneyWhereParameter_IsConvertedByRegistry()
    {
        // Bypass the Where interceptor by calling AddWhereClause directly with a raw Money value.
        // This simulates the runtime fallback path: the Money struct goes into QueryState.Parameters
        // unconverted, and NormalizeParameterValue must convert it via TypeMappingRegistry.
        var results = await ((QueryBuilder<Account, (int AccountId, string AccountName, Money Balance)>)
            _db.Accounts().Select(a => (a.AccountId, a.AccountName, a.Balance)))
            .AddWhereClause("\"Balance\" = @p0", new Money(1000.50m))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountId, Is.EqualTo(1));
        Assert.That(results[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(1000.50m)));
    }

    [Test]
    public async Task FallbackPath_MultipleMappedParameters_AreConvertedByRegistry()
    {
        // Two Money parameters in a single WHERE clause, both unconverted.
        // Seed data: Savings(1000.50, 5000), Checking(250.75, 1000), Savings(500, 2000)
        // Balance >= 1000 AND credit_limit >= 5000 → only account 1
        var results = await ((QueryBuilder<Account, (int AccountId, Money Balance, Money CreditLimit)>)
            _db.Accounts().Select(a => (a.AccountId, a.Balance, a.CreditLimit)))
            .AddWhereClause("\"Balance\" >= @p0 AND \"credit_limit\" >= @p1",
                new Money(1000m), new Money(5000m))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountId, Is.EqualTo(1));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(1000.50m)));
        Assert.That(results[0].CreditLimit, Is.EqualTo(new Money(5000.00m)));
    }

    [Test]
    public async Task FallbackPath_MixedMappedAndPrimitiveParameters_WorkCorrectly()
    {
        // Mix of Money (mapped) and int (primitive) parameters
        var results = await ((QueryBuilder<Account, (int AccountId, string AccountName, Money Balance)>)
            _db.Accounts().Select(a => (a.AccountId, a.AccountName, a.Balance)))
            .AddWhereClause("\"Balance\" > @p0 AND \"UserId\" = @p1",
                new Money(100m), 1)
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.Balance.Amount > 100m), Is.True);
    }

    #endregion

    #region Round-Trip Tests

    [Test]
    public async Task RoundTrip_InsertThenSelect_PreservesMoneyValue()
    {
        var money = new Money(42.42m);
        var creditLimit = new Money(100m);

        await _db.Insert(new Account
        {
            UserId = 2,
            AccountName = "RoundTrip",
            Balance = money,
            CreditLimit = creditLimit,
            IsActive = true
        }).ExecuteNonQueryAsync();

        var results = await _db.Accounts()
            .Where(a => a.AccountName == "RoundTrip")
            .Select(a => (a.AccountName, a.Balance, a.CreditLimit))
            .ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Balance, Is.EqualTo(money),
            "Money value should survive insert->select round-trip");
        Assert.That(results[0].CreditLimit, Is.EqualTo(creditLimit),
            "CreditLimit should survive insert->select round-trip");
    }

    #endregion
}
