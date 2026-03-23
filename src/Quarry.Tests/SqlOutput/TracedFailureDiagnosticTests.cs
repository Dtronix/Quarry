using Quarry;
using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable CS0162 // Unreachable code — conditional branching tests use if(true)/if(false) intentionally

/// <summary>
/// Cross-dialect traced tests mirroring known failing patterns.
/// .Trace() is added so the generator emits [Trace] comments in the .g.cs files for inspection.
/// </summary>
[TestFixture]
internal class TracedFailureDiagnosticTests : CrossDialectTestBase
{
    #region CountSubquery with Enum Predicate

    [Test]
    public void Traced_CountSubquery_WithEnumPredicate()
    {
        AssertDialects(
            Lite.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Trace()
                .ToDiagnostics(),
            Pg.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Trace()
                .ToDiagnostics(),
            My.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Trace()
                .ToDiagnostics(),
            Ss.Users()
                .Where(u => u.Orders.Count(o => o.Priority == OrderPriority.Urgent) > 2)
                .Select(u => (u.UserId, u.UserName))
                .Trace()
                .ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE (SELECT COUNT(*) FROM \"orders\" AS \"sq0\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"sq0\".\"Priority\" = 3)) > 2",
            mysql:  "SELECT `UserId`, `UserName` FROM `users` WHERE (SELECT COUNT(*) FROM `orders` AS `sq0` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`sq0`.`Priority` = 3)) > 2",
            ss:     "SELECT [UserId], [UserName] FROM [users] WHERE (SELECT COUNT(*) FROM [orders] AS [sq0] WHERE [sq0].[UserId] = [users].[UserId] AND ([sq0].[Priority] = 3)) > 2");
    }

    #endregion

    #region Navigation Join with Inferred Type

    [Test]
    public void Traced_NavigationJoin_InferredType_TupleProjection()
    {
        AssertDialects(
            Lite.Users()
                .Join(u => u.Orders)
                .Select((u, o) => (u.UserName, o.Total))
                .Trace()
                .ToDiagnostics(),
            Pg.Users()
                .Join(u => u.Orders)
                .Select((u, o) => (u.UserName, o.Total))
                .Trace()
                .ToDiagnostics(),
            My.Users()
                .Join(u => u.Orders)
                .Select((u, o) => (u.UserName, o.Total))
                .Trace()
                .ToDiagnostics(),
            Ss.Users()
                .Join(u => u.Orders)
                .Select((u, o) => (u.UserName, o.Total))
                .Trace()
                .ToDiagnostics(),
            sqlite: "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            pg:     "SELECT \"t0\".\"UserName\", \"t1\".\"Total\" FROM \"users\" AS \"t0\" INNER JOIN \"orders\" AS \"t1\" ON \"t0\".\"UserId\" = \"t1\".\"UserId\"",
            mysql:  "SELECT `t0`.`UserName`, `t1`.`Total` FROM `users` AS `t0` INNER JOIN `orders` AS `t1` ON `t0`.`UserId` = `t1`.`UserId`",
            ss:     "SELECT [t0].[UserName], [t1].[Total] FROM [users] AS [t0] INNER JOIN [orders] AS [t1] ON [t0].[UserId] = [t1].[UserId]");
    }

    #endregion

    #region SetPoco with Multiple Columns

    [Test]
    public void Traced_SetPoco_MultipleColumns()
    {
        AssertDialects(
            Lite.Users().Update().Set(new User { UserName = "Poco", IsActive = false }).Where(u => u.UserId == 3).Trace().ToDiagnostics(),
            Pg.Users().Update().Set(new Pg.User { UserName = "Poco", IsActive = false }).Where(u => u.UserId == 3).Trace().ToDiagnostics(),
            My.Users().Update().Set(new My.User { UserName = "Poco", IsActive = false }).Where(u => u.UserId == 3).Trace().ToDiagnostics(),
            Ss.Users().Update().Set(new Ss.User { UserName = "Poco", IsActive = false }).Where(u => u.UserId == 3).Trace().ToDiagnostics(),
            sqlite: "UPDATE \"users\" SET \"UserName\" = @p0, \"IsActive\" = @p1 WHERE \"UserId\" = 3",
            pg:     "UPDATE \"users\" SET \"UserName\" = $1, \"IsActive\" = $2 WHERE \"UserId\" = 3",
            mysql:  "UPDATE `users` SET `UserName` = ?, `IsActive` = ? WHERE `UserId` = 3",
            ss:     "UPDATE [users] SET [UserName] = @p0, [IsActive] = @p1 WHERE [UserId] = 3");
    }

    #endregion

    #region MutuallyExclusive OrderBy — Else Branch

    [Test]
    public void Traced_MutuallyExclusiveOrderBy_ElseBranch()
    {
        // Conditional chain pattern requires variable assignment — single-dialect only
        IQueryBuilder<User> query = Lite.Users().Where(u => u.IsActive);
        if (false)
        {
            query = query.OrderBy(u => u.UserName);
        }
        else
        {
            query = query.OrderBy(u => u.UserId);
        }
        var diag = query.Trace().ToDiagnostics();

        Assert.That(diag.Sql, Does.Contain("ORDER BY"));
        Assert.That(diag.Sql, Does.Contain("\"UserId\""));
        Assert.That(diag.Sql, Does.Not.Contain("UserName"));
        Assert.That(diag.IsCarrierOptimized, Is.True);
    }

    #endregion
}
