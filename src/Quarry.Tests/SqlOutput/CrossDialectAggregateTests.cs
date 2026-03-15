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
            Lite.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            Pg.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            My.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            Ss.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            sqlite: "SELECT \"Status\", COUNT(*) FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*) FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*) FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*) FROM [orders] GROUP BY [Status]");
    }

    #endregion

    #region Having

    [Test]
    public void Having_CountGreaterThan()
    {
        AssertDialects(
            Lite.Orders().GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            Pg.Orders().GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            My.Orders().GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            Ss.Orders().GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ToTestCase(),
            sqlite: "SELECT \"Status\", COUNT(*) FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            pg:     "SELECT \"Status\", COUNT(*) FROM \"orders\" GROUP BY \"Status\" HAVING COUNT(*) > 5",
            mysql:  "SELECT `Status`, COUNT(*) FROM `orders` GROUP BY `Status` HAVING COUNT(*) > 5",
            ss:     "SELECT [Status], COUNT(*) FROM [orders] GROUP BY [Status] HAVING COUNT(*) > 5");
    }

    #endregion

    #region Aggregate Functions

    [Test]
    public void Select_Count_Sum()
    {
        AssertDialects(
            Lite.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToTestCase(),
            Pg.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToTestCase(),
            My.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToTestCase(),
            Ss.Orders().GroupBy(o => o.Status).Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total))).ToTestCase(),
            sqlite: "SELECT \"Status\", COUNT(*), SUM(\"Total\") FROM \"orders\" GROUP BY \"Status\"",
            pg:     "SELECT \"Status\", COUNT(*), SUM(\"Total\") FROM \"orders\" GROUP BY \"Status\"",
            mysql:  "SELECT `Status`, COUNT(*), SUM(\"Total\") FROM `orders` GROUP BY `Status`",
            ss:     "SELECT [Status], COUNT(*), SUM(\"Total\") FROM [orders] GROUP BY [Status]");
    }

    #endregion
}
