using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

/// <summary>
/// Layer 7: Cross-dialect SQL output tests for TypeMapping columns.
/// Verifies insert/select SQL shape is correct when columns use Mapped&lt;&gt;.
/// </summary>
[TestFixture]
internal class CrossDialectTypeMappingTests : CrossDialectTestBase
{
    #region Insert Tests

    [Test]
    public void Insert_AccountWithMappedBalance_GeneratesCorrectSql()
    {
        AssertDialects(
            Lite.Accounts().Insert(new Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).ToSql(),
            Pg.Accounts().Insert(new Pg.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).ToSql(),
            My.Accounts().Insert(new My.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).ToSql(),
            Ss.Accounts().Insert(new Ss.Account { UserId = 1, AccountName = "Savings", Balance = new Money(100m), CreditLimit = new Money(500m), IsActive = true }).ToSql(),
            sqlite: "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\") VALUES (@p0, @p1, @p2, @p3, @p4) RETURNING \"AccountId\"",
            pg:     "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\", \"credit_limit\", \"IsActive\") VALUES ($1, $2, $3, $4, $5) RETURNING \"AccountId\"",
            mysql:  "INSERT INTO `accounts` (`UserId`, `AccountName`, `Balance`, `credit_limit`, `IsActive`) VALUES (?, ?, ?, ?, ?)",
            ss:     "INSERT INTO [accounts] ([UserId], [AccountName], [Balance], [credit_limit], [IsActive]) VALUES (@p0, @p1, @p2, @p3, @p4) OUTPUT INSERTED.[AccountId]");
    }

    [Test]
    public void Insert_AccountPartialInit_OnlyIncludesInitializedColumns()
    {
        AssertDialects(
            Lite.Accounts().Insert(new Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).ToSql(),
            Pg.Accounts().Insert(new Pg.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).ToSql(),
            My.Accounts().Insert(new My.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).ToSql(),
            Ss.Accounts().Insert(new Ss.Account { UserId = 1, AccountName = "Checking", Balance = new Money(0m) }).ToSql(),
            sqlite: "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\") VALUES (@p0, @p1, @p2) RETURNING \"AccountId\"",
            pg:     "INSERT INTO \"accounts\" (\"UserId\", \"AccountName\", \"Balance\") VALUES ($1, $2, $3) RETURNING \"AccountId\"",
            mysql:  "INSERT INTO `accounts` (`UserId`, `AccountName`, `Balance`) VALUES (?, ?, ?)",
            ss:     "INSERT INTO [accounts] ([UserId], [AccountName], [Balance]) VALUES (@p0, @p1, @p2) OUTPUT INSERTED.[AccountId]");
    }

    #endregion

    #region Select Tests (Strengthened to 4-dialect)

    [Test]
    public void Select_TupleWithMappedColumn_GeneratesCorrectSql()
    {
        AssertDialects(
            Lite.Accounts().Select(a => (a.AccountId, a.Balance)).ToDiagnostics(),
            Pg.Accounts().Select(a => (a.AccountId, a.Balance)).ToDiagnostics(),
            My.Accounts().Select(a => (a.AccountId, a.Balance)).ToDiagnostics(),
            Ss.Accounts().Select(a => (a.AccountId, a.Balance)).ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"Balance\" FROM \"accounts\"",
            pg:     "SELECT \"AccountId\", \"Balance\" FROM \"accounts\"",
            mysql:  "SELECT `AccountId`, `Balance` FROM `accounts`",
            ss:     "SELECT [AccountId], [Balance] FROM [accounts]");
    }

    #endregion

    #region Where Tests (Strengthened to 4-dialect)

    [Test]
    public void Where_OnNonMappedColumn_GeneratesStandardWhere()
    {
        AssertDialects(
            Lite.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).ToDiagnostics(),
            Pg.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).ToDiagnostics(),
            My.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).ToDiagnostics(),
            Ss.Accounts().Where(a => a.IsActive == true).Select(a => (a.AccountId, a.AccountName)).ToDiagnostics(),
            sqlite: "SELECT \"AccountId\", \"AccountName\" FROM \"accounts\" WHERE (\"IsActive\" = 1)",
            pg:     "SELECT \"AccountId\", \"AccountName\" FROM \"accounts\" WHERE (\"IsActive\" = TRUE)",
            mysql:  "SELECT `AccountId`, `AccountName` FROM `accounts` WHERE (`IsActive` = 1)",
            ss:     "SELECT [AccountId], [AccountName] FROM [accounts] WHERE ([IsActive] = 1)");
    }

    #endregion
}
