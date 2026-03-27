using Quarry.Internal;

namespace Quarry.Tests;

[TestFixture]
public class ScalarConverterTests
{
    // --- int ---

    [Test]
    public void Convert_Int32_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<int>((object)42), Is.EqualTo(42));

    [Test]
    public void Convert_Int32_FromBoxedLong() =>
        Assert.That(ScalarConverter.Convert<int>((object)42L), Is.EqualTo(42));

    [Test]
    public void Convert_NullableInt32_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<int?>((object)42), Is.EqualTo(42));

    // --- long ---

    [Test]
    public void Convert_Int64_FromBoxedLong() =>
        Assert.That(ScalarConverter.Convert<long>((object)100L), Is.EqualTo(100L));

    [Test]
    public void Convert_Int64_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<long>((object)100), Is.EqualTo(100L));

    [Test]
    public void Convert_NullableInt64_FromBoxedLong() =>
        Assert.That(ScalarConverter.Convert<long?>((object)100L), Is.EqualTo(100L));

    // --- decimal ---

    [Test]
    public void Convert_Decimal_FromBoxedDecimal() =>
        Assert.That(ScalarConverter.Convert<decimal>((object)3.14m), Is.EqualTo(3.14m));

    [Test]
    public void Convert_Decimal_FromBoxedDouble() =>
        Assert.That(ScalarConverter.Convert<decimal>((object)3.14), Is.EqualTo((decimal)3.14));

    [Test]
    public void Convert_Decimal_FromBoxedLong() =>
        Assert.That(ScalarConverter.Convert<decimal>((object)42L), Is.EqualTo(42m));

    [Test]
    public void Convert_NullableDecimal_FromBoxedDecimal() =>
        Assert.That(ScalarConverter.Convert<decimal?>((object)3.14m), Is.EqualTo(3.14m));

    // --- double ---

    [Test]
    public void Convert_Double_FromBoxedDouble() =>
        Assert.That(ScalarConverter.Convert<double>((object)2.718), Is.EqualTo(2.718));

    [Test]
    public void Convert_Double_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<double>((object)42), Is.EqualTo(42.0));

    [Test]
    public void Convert_NullableDouble_FromBoxedDouble() =>
        Assert.That(ScalarConverter.Convert<double?>((object)2.718), Is.EqualTo(2.718));

    // --- float ---

    [Test]
    public void Convert_Single_FromBoxedFloat() =>
        Assert.That(ScalarConverter.Convert<float>((object)1.5f), Is.EqualTo(1.5f));

    [Test]
    public void Convert_NullableSingle_FromBoxedFloat() =>
        Assert.That(ScalarConverter.Convert<float?>((object)1.5f), Is.EqualTo(1.5f));

    // --- short ---

    [Test]
    public void Convert_Int16_FromBoxedShort() =>
        Assert.That(ScalarConverter.Convert<short>((object)(short)7), Is.EqualTo((short)7));

    [Test]
    public void Convert_Int16_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<short>((object)7), Is.EqualTo((short)7));

    [Test]
    public void Convert_NullableInt16_FromBoxedShort() =>
        Assert.That(ScalarConverter.Convert<short?>((object)(short)7), Is.EqualTo((short)7));

    // --- byte ---

    [Test]
    public void Convert_Byte_FromBoxedByte() =>
        Assert.That(ScalarConverter.Convert<byte>((object)(byte)255), Is.EqualTo((byte)255));

    [Test]
    public void Convert_NullableByte_FromBoxedByte() =>
        Assert.That(ScalarConverter.Convert<byte?>((object)(byte)255), Is.EqualTo((byte)255));

    // --- bool ---

    [Test]
    public void Convert_Boolean_FromBoxedBool() =>
        Assert.That(ScalarConverter.Convert<bool>((object)true), Is.True);

    [Test]
    public void Convert_Boolean_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<bool>((object)1), Is.True);

    [Test]
    public void Convert_NullableBoolean_FromBoxedBool() =>
        Assert.That(ScalarConverter.Convert<bool?>((object)false), Is.False);

    // --- string ---

    [Test]
    public void Convert_String_FromBoxedInt() =>
        Assert.That(ScalarConverter.Convert<string>((object)42), Is.EqualTo("42"));

    [Test]
    public void Convert_String_FromBoxedString() =>
        Assert.That(ScalarConverter.Convert<string>((object)"hello"), Is.EqualTo("hello"));

    // --- fallback path (uncommon types hitting Convert.ChangeType) ---

    [Test]
    public void Convert_DateTime_FallbackPath() =>
        Assert.That(ScalarConverter.Convert<DateTime>((object)"2026-01-15"),
            Is.EqualTo(new DateTime(2026, 1, 15)));

    [Test]
    public void Convert_NullableDateTime_FallbackPath() =>
        Assert.That(ScalarConverter.Convert<DateTime?>((object)"2026-01-15"),
            Is.EqualTo(new DateTime(2026, 1, 15)));
}
