namespace Quarry.Tests;

/// <summary>
/// Unit tests for QuarryContextAttribute.
/// </summary>
public class QuarryContextAttributeTests
{
    [Test]
    public void QuarryContextAttribute_CanBeInstantiated()
    {
        var attr = new QuarryContextAttribute();
        Assert.That(attr, Is.Not.Null);
    }

    [TestCase(SqlDialect.SQLite)]
    [TestCase(SqlDialect.PostgreSQL)]
    [TestCase(SqlDialect.MySQL)]
    [TestCase(SqlDialect.SqlServer)]
    public void Dialect_CanBeSetAndRetrieved(SqlDialect dialect)
    {
        var attr = new QuarryContextAttribute { Dialect = dialect };
        Assert.That(attr.Dialect, Is.EqualTo(dialect));
    }

    [Test]
    public void Schema_DefaultsToNull()
    {
        var attr = new QuarryContextAttribute();
        Assert.That(attr.Schema, Is.Null);
    }

    [TestCase("public")]
    [TestCase("dbo")]
    [TestCase("myschema")]
    public void Schema_CanBeSetAndRetrieved(string schema)
    {
        var attr = new QuarryContextAttribute { Schema = schema };
        Assert.That(attr.Schema, Is.EqualTo(schema));
    }

    [Test]
    public void Attribute_HasCorrectUsage()
    {
        var usage = typeof(QuarryContextAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .FirstOrDefault();

        Assert.That(usage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(usage!.ValidOn, Is.EqualTo(AttributeTargets.Class));
            Assert.That(usage.AllowMultiple, Is.False);
            Assert.That(usage.Inherited, Is.False);
        });
    }

    [Test]
    public void Attribute_CanBeAppliedToClass()
    {
        // This test verifies the attribute can be applied - compilation would fail if not
        var attr = typeof(TestContextWithAttribute)
            .GetCustomAttributes(typeof(QuarryContextAttribute), false)
            .Cast<QuarryContextAttribute>()
            .FirstOrDefault();

        Assert.That(attr, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(attr!.Dialect, Is.EqualTo(SqlDialect.PostgreSQL));
            Assert.That(attr.Schema, Is.EqualTo("public"));
        });
    }

    [QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
    private class TestContextWithAttribute
    {
    }
}
