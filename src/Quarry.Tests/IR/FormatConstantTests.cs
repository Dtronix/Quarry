using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.Parsing;

namespace Quarry.Tests.IR;

[TestFixture]
public class FormatConstantTests
{
    #region FormatConstantAsSqlLiteralSimple

    [Test]
    public void FormatConstant_Null_ReturnsNULL()
    {
        Assert.That(InvokeFormat(null), Is.EqualTo("NULL"));
    }

    [Test]
    public void FormatConstant_Int_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat(42), Is.EqualTo("42"));
        Assert.That(InvokeFormat(-1), Is.EqualTo("-1"));
        Assert.That(InvokeFormat(0), Is.EqualTo("0"));
    }

    [Test]
    public void FormatConstant_Long_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat(9999999999L), Is.EqualTo("9999999999"));
    }

    [Test]
    public void FormatConstant_Short_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat((short)123), Is.EqualTo("123"));
    }

    [Test]
    public void FormatConstant_Byte_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat((byte)255), Is.EqualTo("255"));
    }

    [Test]
    public void FormatConstant_Float_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat(3.14f), Is.EqualTo("3.14"));
    }

    [Test]
    public void FormatConstant_Double_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat(2.718), Is.EqualTo("2.718"));
    }

    [Test]
    public void FormatConstant_Decimal_ReturnsInvariantString()
    {
        Assert.That(InvokeFormat(99.99m), Is.EqualTo("99.99"));
    }

    [Test]
    public void FormatConstant_BoolTrue_ReturnsTRUE()
    {
        Assert.That(InvokeFormat(true), Is.EqualTo("TRUE"));
    }

    [Test]
    public void FormatConstant_BoolFalse_ReturnsFALSE()
    {
        Assert.That(InvokeFormat(false), Is.EqualTo("FALSE"));
    }

    [Test]
    public void FormatConstant_SimpleString_ReturnsSingleQuoted()
    {
        Assert.That(InvokeFormat("hello"), Is.EqualTo("'hello'"));
    }

    [Test]
    public void FormatConstant_StringWithSingleQuote_EscapesQuote()
    {
        Assert.That(InvokeFormat("it's"), Is.EqualTo("'it''s'"));
    }

    [Test]
    public void FormatConstant_StringWithBackslash_EscapesBackslash()
    {
        Assert.That(InvokeFormat(@"path\to\file"), Is.EqualTo(@"'path\\to\\file'"));
    }

    [Test]
    public void FormatConstant_StringWithBothQuoteAndBackslash()
    {
        Assert.That(InvokeFormat(@"it's a \test"), Is.EqualTo(@"'it''s a \\test'"));
    }

    [Test]
    public void FormatConstant_EmptyString_ReturnsSingleQuotedEmpty()
    {
        Assert.That(InvokeFormat(""), Is.EqualTo("''"));
    }

    [Test]
    public void FormatConstant_Char_ReturnsSingleQuoted()
    {
        Assert.That(InvokeFormat('a'), Is.EqualTo("'a'"));
    }

    [Test]
    public void FormatConstant_CharSingleQuote_Escaped()
    {
        Assert.That(InvokeFormat('\''), Is.EqualTo("''''"));
    }

    [Test]
    public void FormatConstant_UnsupportedType_ReturnsNull()
    {
        Assert.That(InvokeFormat(System.DateTime.Now), Is.Null);
        Assert.That(InvokeFormat(System.Guid.Empty), Is.Null);
    }

    #endregion

    #region EscapeSqlString

    [Test]
    public void EscapeSqlString_NoSpecialChars_ReturnsUnchanged()
    {
        Assert.That(InvokeEscape("hello world"), Is.EqualTo("hello world"));
    }

    [Test]
    public void EscapeSqlString_SingleQuotes_Doubled()
    {
        Assert.That(InvokeEscape("it's"), Is.EqualTo("it''s"));
    }

    [Test]
    public void EscapeSqlString_Backslashes_Doubled()
    {
        Assert.That(InvokeEscape(@"a\b"), Is.EqualTo(@"a\\b"));
    }

    [Test]
    public void EscapeSqlString_MultipleSingleQuotes()
    {
        Assert.That(InvokeEscape("a'b'c"), Is.EqualTo("a''b''c"));
    }

    [Test]
    public void EscapeSqlString_QuoteAndBackslash_BothEscaped()
    {
        Assert.That(InvokeEscape(@"it's a \path"), Is.EqualTo(@"it''s a \\path"));
    }

    [Test]
    public void EscapeSqlString_EmptyString_ReturnsEmpty()
    {
        Assert.That(InvokeEscape(""), Is.EqualTo(""));
    }

    #endregion

    #region Helpers

    private static string? InvokeFormat(object? value)
    {
        var method = typeof(UsageSiteDiscovery).GetMethod(
            "FormatConstantAsSqlLiteralSimple",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)method.Invoke(null, new[] { value });
    }

    private static string InvokeEscape(string value)
    {
        return Quarry.Generators.Translation.SqlLikeHelpers.EscapeSqlStringLiteral(value);
    }

    #endregion
}
