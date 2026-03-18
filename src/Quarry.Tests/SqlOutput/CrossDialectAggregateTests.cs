using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;

#pragma warning disable QRY001

[TestFixture]
internal class CrossDialectAggregateTests : CrossDialectTestBase
{
    #region GroupBy

    [Test]
    public void GroupBy_SingleColumn()
    {
        AssertDialects(
            Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*) AS [Item2] FROM [orders] GROUP BY [Status]");
    }

    #endregion

    #region Having

    [Test]
    public void Having_CountGreaterThan()
    {
        AssertDialects(
            Lite.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\" FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2` FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 5",
            ss:     "SELECT [Status], COUNT(*) AS [Item2] FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 5");
    }

    #endregion

    #region Aggregate Functions

    [Test]
    public void Select_Count_Sum()
    {
        AssertDialects(
            Lite.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            Pg.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            My.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            Ss.Orders().Where(o => true).GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToDiagnostics(),
            sqlite: "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*) AS \"Item2\", SUM(\"Total\") AS \"Item3\" FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*) AS `Item2`, SUM(\"Total\") AS `Item3` FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*) AS [Item2], SUM(\"Total\") AS [Item3] FROM [orders] GROUP BY [Status]");
    }

    #endregion
}
