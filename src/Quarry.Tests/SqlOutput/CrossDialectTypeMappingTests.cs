using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Cross-dialect SQL output + execution tests for TypeMapping columns.
/// Verifies insert/select SQL shape when columns use Mapped&lt;&gt; and verifies
/// Money values are correctly stored/reconstituted via the harness accounts table.
/// </summary>
[TestFixture]
internal class CrossDialectTypeMappingTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Ensure MoneyMapping is registered before any test runs.
        _ = new MoneyMapping();
    }

    #region Insert Tests

    [Test]
    public async Task Insert_AccountWithMappedBalance_GeneratesCorrectSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Insert(new Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).Prepare();
        var pg = Pg.Accounts().Insert(new Pg.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).Prepare();
        var my = My.Accounts().Insert(new My.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).Prepare();
        var ss = Ss.Accounts().Insert(new Ss.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"AccountId\"",
            pg:     "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\") VALUES ($1, $2, $3, $4, $5) RETURNING \"AccountId\"",
            mysql:  "INSERT INTO `accounts` (`UserId`, `AccountName`, `Balance`, `credit_limit`, `IsActive`) VALUES (?, ?, ?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [accounts] ([UserId], [AccountName], [Balance], [credit_limit], [IsActive]) OUTPUT INSERTED.[AccountId] VALUES (@p0, @p1, @p2, @p3, @p4)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    [Test]
    public async Task Insert_AccountPartialInit_OnlyIncludesInitializedColumns()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Insert(new Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).Prepare();
        var pg = Pg.Accounts().Insert(new Pg.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).Prepare();
        var my = My.Accounts().Insert(new My.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).Prepare();
        var ss = Ss.Accounts().Insert(new Ss.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\") VALUES (@p0, @p1, @p2) RETURNING \"AccountId\"",
            pg:     "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\") VALUES ($1, $2, $3) RETURNING \"AccountId\"",
            mysql:  "INSERT INTO `accounts` (`UserId`, `AccountName`, `Balance`) VALUES (?, ?, ?); SELECT LAST_INSERT_ID()",
            ss:     "INSERT INTO [accounts] ([UserId], [AccountName], [Balance]) OUTPUT INSERTED.[AccountId] VALUES (@p0, @p1, @p2)");

        var newId = await lt.ExecuteScalarAsync<int>();
        Assert.That(newId, Is.GreaterThan(0));

        var pgNewId = await pg.ExecuteScalarAsync<int>();
        Assert.That(pgNewId, Is.GreaterThan(0));

        var myNewId = await my.ExecuteScalarAsync<int>();
        Assert.That(myNewId, Is.GreaterThan(0));

        var ssNewId = await ss.ExecuteScalarAsync<int>();
        Assert.That(ssNewId, Is.GreaterThan(0));
    }

    #endregion

    #region Select Tests

    [Test]
    public async Task Select_TupleWithMappedColumn_GeneratesCorrectSql()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Select(a => (a.AccountId, a.Balance)).Prepare();
        var pg = Pg.Accounts().Select(a => (a.AccountId, a.Balance)).Prepare();
        var my = My.Accounts().Select(a => (a.AccountId, a.Balance)).Prepare();
        var ss = Ss.Accounts().Select(a => (a.AccountId, a.Balance)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"Balance\" FROM \"accounts\"",
            pg:     "SELECT \"AccountId\", \"Balance\" FROM \"accounts\"",
            mysql:  "SELECT `AccountId`, `Balance` FROM `accounts`",
            ss:     "SELECT [AccountId], [Balance] FROM [accounts]");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        var savings = results.First(r => r.AccountId == 1);
        Assert.That(savings.Balance, Is.EqualTo(new Money(1000.50m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(3));
        var pgSavings = pgResults.First(r => r.AccountId == 1);
        Assert.That(pgSavings.Balance, Is.EqualTo(new Money(1000.50m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(3));
        var mySavings = myResults.First(r => r.AccountId == 1);
        Assert.That(mySavings.Balance, Is.EqualTo(new Money(1000.50m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(3));
        var ssSavings = ssResults.First(r => r.AccountId == 1);
        Assert.That(ssSavings.Balance, Is.EqualTo(new Money(1000.50m)));
    }

    [Test]
    public async Task Select_TupleWithAllMappedColumns_ReconstitutesMoneyFromDb()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Where(a => a.AccountId == 1).Select(a => (a.AccountId, a.AccountName, a.Balance, a.CreditLimit, a.IsActive)).Prepare();
        var pg = Pg.Accounts().Where(a => a.AccountId == 1).Select(a => (a.AccountId, a.AccountName, a.Balance, a.CreditLimit, a.IsActive)).Prepare();
        var my = My.Accounts().Where(a => a.AccountId == 1).Select(a => (a.AccountId, a.AccountName, a.Balance, a.CreditLimit, a.IsActive)).Prepare();
        var ss = Ss.Accounts().Where(a => a.AccountId == 1).Select(a => (a.AccountId, a.AccountName, a.Balance, a.CreditLimit, a.IsActive)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\" FROM \"accounts\" WHERE \"AccountId\" = 1",
            pg:     "SELECT \"AccountId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\" FROM \"accounts\" WHERE \"AccountId\" = 1",
            mysql:  "SELECT `AccountId`, `AccountName`, `Balance`, `credit_limit`, `IsActive` FROM `accounts` WHERE `AccountId` = 1",
            ss:     "SELECT [AccountId], [AccountName], [Balance], [credit_limit], [IsActive] FROM [accounts] WHERE [AccountId] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(results[0].Balance, Is.EqualTo(new Money(1000.50m)));
        Assert.That(results[0].CreditLimit, Is.EqualTo(new Money(5000.00m)));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(pgResults[0].Balance, Is.EqualTo(new Money(1000.50m)));
        Assert.That(pgResults[0].CreditLimit, Is.EqualTo(new Money(5000.00m)));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(myResults[0].Balance, Is.EqualTo(new Money(1000.50m)));
        Assert.That(myResults[0].CreditLimit, Is.EqualTo(new Money(5000.00m)));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].AccountName, Is.EqualTo("Savings"));
        Assert.That(ssResults[0].Balance, Is.EqualTo(new Money(1000.50m)));
        Assert.That(ssResults[0].CreditLimit, Is.EqualTo(new Money(5000.00m)));
    }

    #endregion

    #region Where Tests

    [Test]
    public async Task Where_OnNonMappedColumn_GeneratesStandardWhere()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt= Lite.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).Prepare();
        var pg = Pg.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).Prepare();
        var my = My.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).Prepare();
        var ss = Ss.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"AccountName\" FROM \"accounts\" WHERE \"IsActive\" = 1",
            pg:     "SELECT \"AccountId\", \"AccountName\" FROM \"accounts\" WHERE \"IsActive\" = TRUE",
            mysql:  "SELECT `AccountId`, `AccountName` FROM `accounts` WHERE `IsActive` = 1",
            ss:     "SELECT [AccountId], [AccountName] FROM [accounts] WHERE [IsActive] = 1");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.All(r => r.AccountName is "Savings" or "Checking"), Is.True);

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(2));
        Assert.That(pgResults.All(r => r.AccountName is "Savings" or "Checking"), Is.True);

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(2));
        Assert.That(myResults.All(r => r.AccountName is "Savings" or "Checking"), Is.True);

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(2));
        Assert.That(ssResults.All(r => r.AccountName is "Savings" or "Checking"), Is.True);
    }

    #endregion

    #region Round-Trip Tests

    [Test]
    public async Task RoundTrip_InsertThenSelect_PreservesMoneyValue()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var money = new Money(42.42m);
        var creditLimit = new Money(100m);

        // INSERT setup (Lite-only)
        await Lite.Accounts().Insert(new Account
        {
            UserId = 2,
            AccountName = "RoundTrip",
            Balance = money,
            CreditLimit = creditLimit,
            IsActive = true
        }).ExecuteNonQueryAsync();

        // INSERT setup (Pg)
        await Pg.Accounts().Insert(new Pg.Account
        {
            UserId = 2,
            AccountName = "RoundTrip",
            Balance = money,
            CreditLimit = creditLimit,
            IsActive = true
        }).ExecuteNonQueryAsync();

        // INSERT setup (My)
        await My.Accounts().Insert(new My.Account
        {
            UserId = 2,
            AccountName = "RoundTrip",
            Balance = money,
            CreditLimit = creditLimit,
            IsActive = true
        }).ExecuteNonQueryAsync();

        // INSERT setup (Ss)
        await Ss.Accounts().Insert(new Ss.Account
        {
            UserId = 2,
            AccountName = "RoundTrip",
            Balance = money,
            CreditLimit = creditLimit,
            IsActive = true
        }).ExecuteNonQueryAsync();

        // SELECT: cross-dialect SQL assertion
        var lt = Lite.Accounts().Where(a => a.AccountName == "RoundTrip").Select(a => (a.AccountName, a.Balance, a.CreditLimit)).Prepare();
        var pg = Pg.Accounts().Where(a => a.AccountName == "RoundTrip").Select(a => (a.AccountName, a.Balance, a.CreditLimit)).Prepare();
        var my = My.Accounts().Where(a => a.AccountName == "RoundTrip").Select(a => (a.AccountName, a.Balance, a.CreditLimit)).Prepare();
        var ss = Ss.Accounts().Where(a => a.AccountName == "RoundTrip").Select(a => (a.AccountName, a.Balance, a.CreditLimit)).Prepare();

        QueryTestHarness.AssertDialects(
            lt.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"AccountName\", \"Balance\", \"credit_limit\" FROM \"accounts\" WHERE \"AccountName\" = @p0",
            pg:     "SELECT \"AccountName\", \"Balance\", \"credit_limit\" FROM \"accounts\" WHERE \"AccountName\" = $1",
            mysql:  "SELECT `AccountName`, `Balance`, `credit_limit` FROM `accounts` WHERE `AccountName` = ?",
            ss:     "SELECT [AccountName], [Balance], [credit_limit] FROM [accounts] WHERE [AccountName] = @p0");

        var results = await lt.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Balance, Is.EqualTo(money));
        Assert.That(results[0].CreditLimit, Is.EqualTo(creditLimit));

        var pgResults = await pg.ExecuteFetchAllAsync();
        Assert.That(pgResults, Has.Count.EqualTo(1));
        Assert.That(pgResults[0].Balance, Is.EqualTo(money));
        Assert.That(pgResults[0].CreditLimit, Is.EqualTo(creditLimit));

        var myResults = await my.ExecuteFetchAllAsync();
        Assert.That(myResults, Has.Count.EqualTo(1));
        Assert.That(myResults[0].Balance, Is.EqualTo(money));
        Assert.That(myResults[0].CreditLimit, Is.EqualTo(creditLimit));

        var ssResults = await ss.ExecuteFetchAllAsync();
        Assert.That(ssResults, Has.Count.EqualTo(1));
        Assert.That(ssResults[0].Balance, Is.EqualTo(money));
        Assert.That(ssResults[0].CreditLimit, Is.EqualTo(creditLimit));
    }

    #endregion
}
