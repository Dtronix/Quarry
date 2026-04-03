using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

[TestFixture]
internal class CrossDialectHasManyThroughTests
{
    #region HasManyThrough with Any predicate

    [Test]
    public async Task HasManyThrough_Any_WithPredicate()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lt = Lite.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var pg = Pg.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var my = My.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();
        var ss = Ss.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
            .Select(u => u.UserName).ToDiagnostics();

        // String literals in subquery predicates are inlined across all dialects
        QueryTestHarness.AssertDialects(
            lt, pg, my, ss,
            sqlite: "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland'))",
            pg:     "SELECT \"UserName\" FROM \"users\" WHERE EXISTS (SELECT 1 FROM \"user_addresses\" AS \"sq0\" INNER JOIN \"addresses\" AS \"j0\" ON \"sq0\".\"AddressId\" = \"j0\".\"AddressId\" WHERE \"sq0\".\"UserId\" = \"users\".\"UserId\" AND (\"j0\".\"City\" = 'Portland'))",
            mysql:  "SELECT `UserName` FROM `users` WHERE EXISTS (SELECT 1 FROM `user_addresses` AS `sq0` INNER JOIN `addresses` AS `j0` ON `sq0`.`AddressId` = `j0`.`AddressId` WHERE `sq0`.`UserId` = `users`.`UserId` AND (`j0`.`City` = 'Portland'))",
            ss:     "SELECT [UserName] FROM [users] WHERE EXISTS (SELECT 1 FROM [user_addresses] AS [sq0] INNER JOIN [addresses] AS [j0] ON [sq0].[AddressId] = [j0].[AddressId] WHERE [sq0].[UserId] = [users].[UserId] AND ([j0].[City] = 'Portland'))");
    }

    #endregion
}
