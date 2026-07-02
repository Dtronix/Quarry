using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Quarry.Generators.IR;

namespace Quarry.Tests.IR;

/// <summary>
/// Unit tests for the MySQL bind-order marker rewrite (#303). Markers are emitted by
/// MySQL variant rendering in place of bare '?', then rewritten back to '?' (or to
/// collection-expansion tokens) in a single pass that also records SQL-text slot order.
/// </summary>
[TestFixture]
public class MySqlBindMarkersTests
{
    [TestCase(0, ExpectedResult = "{__Q0__}")]
    [TestCase(1, ExpectedResult = "{__Q1__}")]
    [TestCase(42, ExpectedResult = "{__Q42__}")]
    public string Format_ProducesMarkerToken(int index)
    {
        return MySqlBindMarkers.Format(index);
    }

    [Test]
    public void AppendTo_MatchesFormat()
    {
        var sb = new StringBuilder();
        MySqlBindMarkers.AppendTo(sb, 17);
        Assert.That(sb.ToString(), Is.EqualTo(MySqlBindMarkers.Format(17)));
    }

    [Test]
    public void RewriteAndExtract_NoMarkers_ReturnsSameInstance()
    {
        var sql = "SELECT `Total` FROM `orders` WHERE `Total` > ?";
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(sql, null, order);
        Assert.That(ReferenceEquals(result, sql), Is.True,
            "Marker-free SQL must be returned as the original instance (zero-allocation fast path).");
        Assert.That(order, Is.Empty);
    }

    [Test]
    public void RewriteAndExtract_ChainOrderText_ExtractsIdentityOrder()
    {
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT `Total` FROM `orders` WHERE `Total` > {__Q0__} ORDER BY (`Total` + {__Q1__})",
            null, order);
        Assert.That(result, Is.EqualTo(
            "SELECT `Total` FROM `orders` WHERE `Total` > ? ORDER BY (`Total` + ?)"));
        Assert.That(order, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void RewriteAndExtract_HoistedOrderByText_ExtractsSwappedOrder()
    {
        // The #303 reproducer shape: the DistinctOrderBy wrap hoists the ORDER BY
        // expression (slot 1) textually before the WHERE (slot 0).
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT `d`.`Total` FROM (SELECT DISTINCT `Total` AS `Total`, (`Total` + {__Q1__}) AS `_o0` " +
            "FROM `orders` WHERE `Total` > {__Q0__}) AS `d` ORDER BY `d`.`_o0` ASC",
            null, order);
        Assert.That(result, Is.EqualTo(
            "SELECT `d`.`Total` FROM (SELECT DISTINCT `Total` AS `Total`, (`Total` + ?) AS `_o0` " +
            "FROM `orders` WHERE `Total` > ?) AS `d` ORDER BY `d`.`_o0` ASC"));
        Assert.That(order, Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void RewriteAndExtract_CollectionSlot_EmitsExpansionToken()
    {
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT * FROM `orders` WHERE `OrderId` IN ({__Q0__}) AND `Total` > {__Q1__}",
            n => n == 0, order);
        Assert.That(result, Is.EqualTo(
            "SELECT * FROM `orders` WHERE `OrderId` IN ({__COL_P0__}) AND `Total` > ?"));
        Assert.That(order, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void RewriteAndExtract_LiteralQuestionMarkInString_Untouched()
    {
        // The old Nth-'?' substitution miscounted when a SQL string literal contained '?';
        // the marker rewrite never touches non-marker text.
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT * FROM `t` WHERE `name` = 'what?' AND `x` > {__Q0__}",
            null, order);
        Assert.That(result, Is.EqualTo("SELECT * FROM `t` WHERE `name` = 'what?' AND `x` > ?"));
        Assert.That(order, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void RewriteAndExtract_MarkerLikeTextWithoutDigitsOrSuffix_Preserved()
    {
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT '{__Q}' AS a, '{__Qx__}' AS b, '{__Q1' AS c, `x` > {__Q2__} AS d FROM `t`",
            null, order);
        Assert.That(result, Is.EqualTo(
            "SELECT '{__Q}' AS a, '{__Qx__}' AS b, '{__Q1' AS c, `x` > ? AS d FROM `t`"));
        Assert.That(order, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void RewriteAndExtract_ExistingCollectionToken_PreservedAndNotExtracted()
    {
        // {__COL_P0__} does not share the marker prefix; it must pass through unscanned.
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "SELECT * FROM `t` WHERE `id` IN ({__COL_P0__}) AND `x` > {__Q1__}",
            null, order);
        Assert.That(result, Is.EqualTo("SELECT * FROM `t` WHERE `id` IN ({__COL_P0__}) AND `x` > ?"));
        Assert.That(order, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void RewriteAndExtract_AdjacentMarkers_AllRewritten()
    {
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "{__Q2__}{__Q0__}{__Q1__}", null, order);
        Assert.That(result, Is.EqualTo("???"));
        Assert.That(order, Is.EqualTo(new[] { 2, 0, 1 }));
    }

    [Test]
    public void RewriteAndExtract_NullTextOrder_RewritesOnly()
    {
        var result = MySqlBindMarkers.RewriteAndExtract(
            "WHERE `x` > {__Q0__}", null, null);
        Assert.That(result, Is.EqualTo("WHERE `x` > ?"));
    }

    [Test]
    public void RewriteAndExtract_MarkerAtStartAndEnd_SegmentsCorrect()
    {
        var order = new List<int>();
        var result = MySqlBindMarkers.RewriteAndExtract(
            "{__Q0__} = `a` AND `b` = {__Q1__}", null, order);
        Assert.That(result, Is.EqualTo("? = `a` AND `b` = ?"));
        Assert.That(order, Is.EqualTo(new[] { 0, 1 }));
    }
}
