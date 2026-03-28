using Quarry.Internal;

namespace Quarry.Tests;

/// <summary>
/// Tests for ScalarConverter null/DBNull handling (fixes 2.1 and 2.2).
/// </summary>
[TestFixture]
public class ScalarConverterNullTests
{
    // --- null input ---

    [Test]
    public void Convert_NullToString_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<string>(null!), Is.Null);

    [Test]
    public void Convert_NullToNullableInt_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<int?>(null!), Is.Null);

    [Test]
    public void Convert_NullToNullableLong_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<long?>(null!), Is.Null);

    [Test]
    public void Convert_NullToNullableDecimal_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<decimal?>(null!), Is.Null);

    [Test]
    public void Convert_NullToNullableDouble_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<double?>(null!), Is.Null);

    [Test]
    public void Convert_NullToNullableBool_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<bool?>(null!), Is.Null);

    [Test]
    public void Convert_NullToInt_ReturnsDefault() =>
        Assert.That(ScalarConverter.Convert<int>(null!), Is.EqualTo(0));

    [Test]
    public void Convert_NullToBool_ReturnsDefault() =>
        Assert.That(ScalarConverter.Convert<bool>(null!), Is.False);

    // --- DBNull input ---

    [Test]
    public void Convert_DBNullToString_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<string>(DBNull.Value), Is.Null);

    [Test]
    public void Convert_DBNullToNullableInt_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<int?>(DBNull.Value), Is.Null);

    [Test]
    public void Convert_DBNullToNullableLong_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<long?>(DBNull.Value), Is.Null);

    [Test]
    public void Convert_DBNullToInt_ReturnsDefault() =>
        Assert.That(ScalarConverter.Convert<int>(DBNull.Value), Is.EqualTo(0));

    [Test]
    public void Convert_DBNullToNullableDecimal_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<decimal?>(DBNull.Value), Is.Null);

    [Test]
    public void Convert_DBNullToNullableDateTime_ReturnsNull() =>
        Assert.That(ScalarConverter.Convert<DateTime?>(DBNull.Value), Is.Null);
}
